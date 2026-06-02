using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Models;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Utils;

public class DiscordBotService
{
    private const string DiscordApiBaseUrl = "https://discord.com/api/v10";

    private static readonly Regex ReportMenuMessageRegex = new(
        @"^Target:\s*(?<target>.+?)\s*\|\s*Reason:\s*(?<reason>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex TargetSteamIdRegex = new(
        @"\((?<steamid>\d{17})\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly ISwiftlyCore _core;
    private readonly string _serverName;
    private readonly string _botToken;
    private readonly string _defaultChannelId;
    private readonly string _chatChannelId;
    private readonly string _connectionChannelId;
    private readonly string _callAdminChannelId;
    private readonly string _reportChannelId;
    private readonly string _adminTimeChannelId;
    private readonly string _serverStatusChannelId;
    private readonly string _leaderboardChannelId;
    private readonly string _customConnectUrl;

    private readonly int _serverStatusUpdateSeconds;
    private readonly int _leaderboardUpdateMinutes;
    private readonly int _leaderboardTopLimit;

    private readonly SemaphoreSlim _statusMessageLock = new(1, 1);
    private readonly string _bannerUrl;
    private readonly HttpClient _httpClient;
    private readonly string _statePath;
    private readonly object _stateLock = new();
    private readonly ConcurrentDictionary<string, DateTime> _configurationWarningTimestamps = new(StringComparer.Ordinal);

    private DiscordStateFile _state;
    private CancellationTokenSource? _gatewayCts;
    private CancellationTokenSource? _serverStatusPublishCts;
    private CancellationTokenSource? _serverStatusUpdateCts;
    private CancellationTokenSource? _leaderboardUpdateCts;
    private PlayerSessionManager? _playerSessionManager;
    private DiscordServerStatusDbManager? _discordServerStatusDbManager;
    private RankLeaderboardDbManager? _rankLeaderboardDbManager;
    private DiscordMessageStateDbManager? _discordMessageStateDbManager;
    private WarnManager? _warnManager;
    private AdminLogManager? _adminLogManager;
    private ClientWebSocket? _gatewaySocket;
    private Task? _gatewayTask;
    private Task? _gatewayHeartbeatTask;
    private int? _gatewaySequence;
    private string? _gatewaySessionId;

    public DiscordBotService(ISwiftlyCore core, DiscordFileConfig config)
    {
        _core = core;
        _serverName = config.ServerName ?? string.Empty;
        _botToken = config.BotToken ?? string.Empty;
        _defaultChannelId = config.AdminLogChannelId ?? string.Empty;
        _chatChannelId = config.ChatLogChannelId ?? string.Empty;
        _connectionChannelId = config.ConnectionLogChannelId ?? string.Empty;
        _callAdminChannelId = config.CallAdminChannelId ?? string.Empty;
        _reportChannelId = config.ReportChannelId ?? string.Empty;
        _adminTimeChannelId = config.AdminTimeChannelId ?? string.Empty;
        _serverStatusChannelId = config.ServerStatusChannelId ?? string.Empty;
        _leaderboardChannelId = config.LeaderboardChannelId ?? string.Empty;
        _customConnectUrl = config.CustomConnectUrl ?? string.Empty;

        _serverStatusUpdateSeconds = Math.Max(10, config.ServerStatusUpdateSeconds);
        _leaderboardUpdateMinutes = Math.Max(1, config.LeaderboardUpdateMinutes);
        _leaderboardTopLimit = Math.Clamp(config.LeaderboardTopLimit, 1, 25);
        _bannerUrl = config.BannerUrl ?? string.Empty;
        _httpClient = SharedHttpClient;
        _statePath = core.Configuration.GetConfigPath("discord-state.json");
        _state = LoadState();

        _core.Logger.LogInformationIfEnabled(
            "[CS2_Admin][Debug][Discord] config botConfigured={BotConfigured} adminLogChannel={AdminLogChannel} chatChannel={ChatChannel} connectionChannel={ConnectionChannel} reportChannel={ReportChannel}",
            HasBotConfiguration(),
            MaskChannelId(_defaultChannelId),
            MaskChannelId(_chatChannelId),
            MaskChannelId(_connectionChannelId),
            MaskChannelId(_reportChannelId));
    }

    public void SetDatabaseManagers(WarnManager warnManager, AdminLogManager adminLogManager)
    {
        _warnManager = warnManager;
        _adminLogManager = adminLogManager;
    }

    public void StartBackgroundUpdates(
        PlayerSessionManager playerSessionManager,
        DiscordServerStatusDbManager discordServerStatusDbManager,
        RankLeaderboardDbManager rankLeaderboardDbManager,
        DiscordMessageStateDbManager discordMessageStateDbManager)
    {
        _playerSessionManager = playerSessionManager;
        _discordServerStatusDbManager = discordServerStatusDbManager;
        _rankLeaderboardDbManager = rankLeaderboardDbManager;
        _discordMessageStateDbManager = discordMessageStateDbManager;

        StopBackgroundUpdates();

        EnsureGatewayConnection();

        _serverStatusPublishCts = _core.Scheduler.RepeatBySeconds(_serverStatusUpdateSeconds, () => _ = PublishServerStatusAsync());
        _ = PublishServerStatusAsync();

        if (HasBotConfiguration() && !string.IsNullOrWhiteSpace(_serverStatusChannelId))
        {
            _serverStatusUpdateCts = _core.Scheduler.RepeatBySeconds(_serverStatusUpdateSeconds, () => _ = UpsertServerStatusMessageAsync());
            _ = UpsertServerStatusMessageAsync();
        }

        if (HasBotConfiguration() && !string.IsNullOrWhiteSpace(_leaderboardChannelId))
        {
            var intervalSeconds = _leaderboardUpdateMinutes * 60f;
            _leaderboardUpdateCts = _core.Scheduler.RepeatBySeconds(intervalSeconds, () => _ = UpsertLeaderboardMessagesAsync());
            _ = UpsertLeaderboardMessagesAsync();
        }
    }

    public void StopBackgroundUpdates()
    {
        _gatewayCts?.Cancel();
        _gatewayCts = null;

        try
        {
            _gatewaySocket?.Abort();
            _gatewaySocket?.Dispose();
        }
        catch
        {
        }

        _gatewaySocket = null;
        _gatewayTask = null;
        _gatewayHeartbeatTask = null;
        _gatewaySequence = null;
        _gatewaySessionId = null;

        _serverStatusPublishCts?.Cancel();
        _serverStatusPublishCts = null;

        _serverStatusUpdateCts?.Cancel();
        _serverStatusUpdateCts = null;

        _leaderboardUpdateCts?.Cancel();
        _leaderboardUpdateCts = null;
    }

    public void EnsureGatewayConnection()
    {
        if (!HasBotConfiguration())
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][Discord] gateway skipped reason=no_bot_token");
            return;
        }

        if (_gatewayTask != null && !_gatewayTask.IsCompleted && _gatewayCts?.IsCancellationRequested == false)
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][Discord] gateway skipped reason=already_running");
            return;
        }

        _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][Discord] gateway starting");
        StartGatewayConnection();
    }

    public async Task SendConnectNotificationAsync(IPlayer player)
    {
        if (player == null || !player.IsValid || player.IsFakeClient)
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][DiscordConnect] skipped reason=invalid_or_fake");
            return;
        }

        await SendConnectNotificationAsync(
            player.Controller.PlayerName,
            player.SteamID,
            player.IPAddress,
            GetActiveHumanPlayerCount());
    }

    public async Task SendConnectNotificationAsync(string? playerName, ulong steamId, string? ipAddress, int activePlayers)
    {
        var channelId = ResolveLogChannelId(_connectionChannelId);
        _core.Logger.LogInformationIfEnabled(
            "[CS2_Admin][Debug][DiscordConnect] start steamid={SteamId} name={Name} ip={Ip} preferredChannel={PreferredChannel} resolvedChannel={ResolvedChannel} fallback={Fallback}",
            steamId,
            string.IsNullOrWhiteSpace(playerName) ? "-" : playerName,
            string.IsNullOrWhiteSpace(ipAddress) ? "-" : ipAddress,
            MaskChannelId(_connectionChannelId),
            MaskChannelId(channelId),
            string.IsNullOrWhiteSpace(_connectionChannelId));

        if (string.IsNullOrWhiteSpace(channelId))
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][DiscordConnect] skipped steamid={SteamId} reason=no_channel", steamId);
            return;
        }

        try
        {
            var snapshot = await BuildPlayerSnapshotAsync(
                playerName,
                steamId,
                ipAddress,
                includeGeoLookup: true);

            var embed = new
            {
                title = T("discord_connect_title", "Player {0} connected", EscapeMarkdown(snapshot.DisplayName)),
                url = BuildSteamProfileUrl(snapshot.SteamId),
                description = T(
                    "discord_connect_description",
                    "{0} connected from {1} {2}\n**SteamID:** `{3}`\n**IP:** ||{4}||",
                    EscapeMarkdown(snapshot.DisplayName),
                    CountryCodeToDiscordFlag(snapshot.CountryCode),
                    snapshot.CountryName,
                    snapshot.SteamId,
                    snapshot.IpAddress),
                color = 65280, // Green
                footer = new
                {
                    text = T(
                        "discord_active_players_footer",
                        "Active Players: {0}/{1} | Server: {2}",
                        activePlayers,
                        ServerIdentity.GetMaxPlayers(_core, _core.PlayerManager.PlayerCap),
                        GetServerLabel())
                }
            };
            
            var messageId = await SendEmbedToChannelAsync(channelId, embed);
            _core.Logger.LogInformationIfEnabled(
                "[CS2_Admin][Debug][DiscordConnect] result steamid={SteamId} channel={ChannelId} success={Success} messageId={MessageId}",
                steamId,
                MaskChannelId(channelId),
                !string.IsNullOrWhiteSpace(messageId),
                string.IsNullOrWhiteSpace(messageId) ? "-" : messageId);
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error sending connect notification: {Message}", ex.Message);
        }
    }

    public async Task SendDisconnectNotificationAsync(string? playerName, ulong steamId, string? ipAddress)
    {
        var channelId = ResolveLogChannelId(_connectionChannelId);
        _core.Logger.LogInformationIfEnabled(
            "[CS2_Admin][Debug][DiscordDisconnect] start steamid={SteamId} name={Name} ip={Ip} preferredChannel={PreferredChannel} resolvedChannel={ResolvedChannel} fallback={Fallback}",
            steamId,
            string.IsNullOrWhiteSpace(playerName) ? "-" : playerName,
            string.IsNullOrWhiteSpace(ipAddress) ? "-" : ipAddress,
            MaskChannelId(_connectionChannelId),
            MaskChannelId(channelId),
            string.IsNullOrWhiteSpace(_connectionChannelId));

        if (string.IsNullOrWhiteSpace(channelId))
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][DiscordDisconnect] skipped steamid={SteamId} reason=no_channel", steamId);
            return;
        }

        try
        {
            var snapshot = await BuildPlayerSnapshotAsync(playerName, steamId, ipAddress, includeGeoLookup: true);
            var activePlayers = GetActiveHumanPlayerCount();
            
            var embed = new
            {
                title = T("discord_disconnect_title", "Player {0} disconnected", EscapeMarkdown(snapshot.DisplayName)),
                url = BuildSteamProfileUrl(snapshot.SteamId),
                description = T(
                    "discord_disconnect_description",
                    "{0} disconnected from {1} {2}\n**SteamID:** `{3}`\n**IP:** ||{4}||",
                    EscapeMarkdown(snapshot.DisplayName),
                    CountryCodeToDiscordFlag(snapshot.CountryCode),
                    snapshot.CountryName,
                    snapshot.SteamId,
                    snapshot.IpAddress),
                color = 16711680, // Red
                footer = new
                {
                    text = T(
                        "discord_active_players_footer",
                        "Active Players: {0}/{1} | Server: {2}",
                        activePlayers,
                        ServerIdentity.GetMaxPlayers(_core, _core.PlayerManager.PlayerCap),
                        GetServerLabel())
                }
            };

            var messageId = await SendEmbedToChannelAsync(channelId, embed);
            _core.Logger.LogInformationIfEnabled(
                "[CS2_Admin][Debug][DiscordDisconnect] result steamid={SteamId} channel={ChannelId} success={Success} messageId={MessageId}",
                steamId,
                MaskChannelId(channelId),
                !string.IsNullOrWhiteSpace(messageId),
                string.IsNullOrWhiteSpace(messageId) ? "-" : messageId);
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error sending disconnect notification: {Message}", ex.Message);
        }
    }

    public async Task SendChatNotificationAsync(IPlayer player, string message, bool teamOnly)
    {
        if (player == null || !player.IsValid || player.IsFakeClient)
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][DiscordChat] service skipped reason=invalid_or_fake");
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][DiscordChat] service skipped steamid={SteamId} reason=empty", player.SteamID);
            return;
        }

        var trimmed = message.Trim();
        if (trimmed.StartsWith("!") || trimmed.StartsWith("/"))
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][DiscordChat] service skipped steamid={SteamId} reason=command text={Text}", player.SteamID, trimmed);
            return;
        }

        var channelId = ResolveLogChannelId(_chatChannelId);
        _core.Logger.LogInformationIfEnabled(
            "[CS2_Admin][Debug][DiscordChat] service start steamid={SteamId} name={Name} teamOnly={TeamOnly} preferredChannel={PreferredChannel} resolvedChannel={ResolvedChannel} fallback={Fallback} textLength={Length}",
            player.SteamID,
            player.Controller.PlayerName ?? "-",
            teamOnly,
            MaskChannelId(_chatChannelId),
            MaskChannelId(channelId),
            string.IsNullOrWhiteSpace(_chatChannelId),
            trimmed.Length);

        if (string.IsNullOrWhiteSpace(channelId))
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][DiscordChat] service skipped steamid={SteamId} reason=no_channel", player.SteamID);
            return;
        }

        try
        {
            var snapshot = await BuildPlayerSnapshotAsync(
                player.Controller.PlayerName,
                player.SteamID,
                player.IPAddress);

            var scopePrefix = teamOnly ? "[Team] " : string.Empty;
            var line = $"{CountryCodeToDiscordFlag(snapshot.CountryCode)} [{EscapeMarkdown(GetServerLabel())}] | {scopePrefix}**{EscapeMarkdown(snapshot.DisplayName)}** (`{snapshot.SteamId}`): {EscapeMarkdown(trimmed)}";
            var messageId = await SendMessageToChannelAsync(channelId, line);
            _core.Logger.LogInformationIfEnabled(
                "[CS2_Admin][Debug][DiscordChat] service result steamid={SteamId} channel={ChannelId} success={Success} messageId={MessageId}",
                player.SteamID,
                MaskChannelId(channelId),
                !string.IsNullOrWhiteSpace(messageId),
                string.IsNullOrWhiteSpace(messageId) ? "-" : messageId);
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error sending chat notification: {Message}", ex.Message);
        }
    }

    public async Task SendBanNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["discord_permanent"] : PluginLocalizer.Get(_core)["discord_minutes", duration];
            var embed = BuildModerationActionEmbed("Ban", 15158332, adminName, "0", targetName, "-", $"Duration: `{durationText}`\nReason: `{reason}`");

            await SendEmbedToChannelAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending ban notification: {Message}", ex.Message);
        }
    }

    public async Task SendUnbanNotificationAsync(string adminName, string targetSteamId, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var embed = BuildModerationActionEmbed("Unban", 3066993, adminName, "0", "-", targetSteamId, $"Reason: `{reason}`");

            await SendEmbedToChannelAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending unban notification: {Message}", ex.Message);
        }
    }

    public async Task SendMuteNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["discord_permanent"] : PluginLocalizer.Get(_core)["discord_minutes", duration];
            var embed = BuildModerationActionEmbed("Mute", 15105570, adminName, "0", targetName, "-", $"Duration: `{durationText}`\nReason: `{reason}`");

            await SendEmbedToChannelAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending mute notification: {Message}", ex.Message);
        }
    }

    public async Task SendGagNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["discord_permanent"] : PluginLocalizer.Get(_core)["discord_minutes", duration];
            var embed = BuildModerationActionEmbed("Gag", 15105570, adminName, "0", targetName, "-", $"Duration: `{durationText}`\nReason: `{reason}`");

            await SendEmbedToChannelAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending gag notification: {Message}", ex.Message);
        }
    }

    public async Task SendKickNotificationAsync(string adminName, string targetName, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var embed = BuildModerationActionEmbed("Kick", 15844367, adminName, "0", targetName, "-", $"Reason: `{reason}`");

            await SendEmbedToChannelAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending kick notification: {Message}", ex.Message);
        }
    }

    public async Task SendSilenceNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["discord_permanent"] : PluginLocalizer.Get(_core)["discord_minutes", duration];
            var embed = BuildModerationActionEmbed("Silence", 10181046, adminName, "0", targetName, "-", $"Duration: `{durationText}`\nReason: `{reason}`");

            await SendEmbedToChannelAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending silence notification: {Message}", ex.Message);
        }
    }

    public async Task SendWarnNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["discord_permanent"] : PluginLocalizer.Get(_core)["discord_minutes", duration];
            var embed = BuildModerationActionEmbed("Warn", 16098851, adminName, "0", targetName, "-", $"Duration: `{durationText}`\nReason: `{reason}`");

            await SendEmbedToChannelAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending warn notification: {Message}", ex.Message);
        }
    }

    public async Task SendCallAdminNotificationAsync(string playerName, ulong playerSteamId, string message, string serverId)
    {
        var channelId = ResolveChannelId(_callAdminChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var reporterValue = $"[{EscapeMarkdown(playerName)}]({BuildSteamProfileUrl(playerSteamId)})";
            var embed = new
            {
                title = T("discord_calladmin_title_embed", ":rotating_light: {0} CallAdmin", GetServerLabel()),
                description = T(
                    "discord_calladmin_description",
                    "**Reporter:** {0}\n**Server:** `{1}`",
                    reporterValue,
                    string.IsNullOrWhiteSpace(serverId) ? GetServerLabel() : serverId),
                color = 10181046,
                fields = new[]
                {
                    new { name = T("discord_player", "Player"), value = EscapeMarkdown(playerName), inline = true },
                    new { name = T("discord_steamid", "SteamID"), value = $"`{playerSteamId}`", inline = true },
                    new { name = "\u200B", value = "\u200B", inline = true },
                    new { name = T("discord_calladmin_message", "Message"), value = string.IsNullOrWhiteSpace(message) ? "-" : EscapeMarkdown(message), inline = false }
                },
                footer = new { text = T("discord_calladmin_footer", "CS2_Admin | CallAdmin | {0} UTC", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")) },
                timestamp = DateTime.UtcNow.ToString("o")
            };

            await SendEmbedToChannelAsync(
                channelId,
                embed,
                "@everyone",
                BuildLinkButtonComponents(
                    (T("discord_reporter_profile_button", "Reporter Profile"), BuildSteamProfileUrl(playerSteamId)),
                    (T("discord_server_status_button", "Server Status"), BuildSteamServerUrl())),
                allowEveryoneMention: true);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending calladmin notification: {Message}", ex.Message);
        }
    }

    public async Task SendReportNotificationAsync(string playerName, ulong playerSteamId, string message, string serverId)
    {
        var channelId = ResolveChannelId(_reportChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var rawMessage = string.IsNullOrWhiteSpace(message) ? "-" : message.Trim();
            var targetValue = "-";
            var displayMessage = rawMessage;
            ulong? targetSteamId = null;
            var targetName = "-";

            var parsed = ReportMenuMessageRegex.Match(rawMessage);
            if (parsed.Success)
            {
                targetValue = parsed.Groups["target"].Value.Trim();
                displayMessage = parsed.Groups["reason"].Value.Trim();
                targetSteamId = TryExtractSteamId(targetValue);
                targetName = StripTrailingSteamId(targetValue);
            }
            else
            {
                targetName = targetValue;
            }

            var reporterValue = $"[{EscapeMarkdown(playerName)}]({BuildSteamProfileUrl(playerSteamId)})";
            var targetDisplayValue = targetSteamId is > 0
                ? $"[{EscapeMarkdown(targetName)}]({BuildSteamProfileUrl(targetSteamId.Value)})"
                : EscapeMarkdown(targetName);
            var embed = new
            {
                title = T("discord_report_title_embed", ":triangular_flag_on_post: {0} Player Report", GetServerLabel()),
                description = T("discord_report_description", "**Reporter:** {0}\n**Target:** {1}", reporterValue, targetDisplayValue),
                color = 16763904,
                fields = new[]
                {
                    new { name = T("discord_server", "Server"), value = $"`{(string.IsNullOrWhiteSpace(serverId) ? GetServerLabel() : serverId)}`", inline = true },
                    new { name = T("discord_reporter_steamid", "Reporter SteamID"), value = $"`{playerSteamId}`", inline = true },
                    new { name = T("discord_target_steamid", "Target SteamID"), value = targetSteamId is > 0 ? $"`{targetSteamId.Value}`" : "-", inline = true },
                    new { name = T("discord_reason", "Reason"), value = string.IsNullOrWhiteSpace(displayMessage) ? "-" : EscapeMarkdown(displayMessage), inline = false }
                },
                footer = new { text = T("discord_report_footer", "CS2_Admin | Report System | {0} UTC", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")) },
                timestamp = DateTime.UtcNow.ToString("o")
            };

            await SendEmbedToChannelAsync(
                channelId,
                embed,
                "@everyone",
                BuildReportInteractiveComponents(serverId, targetSteamId ?? 0, playerSteamId),
                allowEveryoneMention: true);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending report notification: {Message}", ex.Message);
        }
    }

    public async Task SendAdminTimeNotificationAsync(IReadOnlyList<AdminPlaytime> entries)
    {
        var channelId = ResolveChannelId(_adminTimeChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var topEntries = entries.Take(10).ToList();
            var fields = new List<object>();
            if (topEntries.Count == 0)
            {
                fields.Add(new { name = "No data", value = "No admin playtime data yet.", inline = false });
            }
            else
            {
                var leftColumn = topEntries.Take((topEntries.Count + 1) / 2).ToList();
                var rightColumn = topEntries.Skip(leftColumn.Count).ToList();
                fields.Add(new { name = "\u200B", value = BuildAdminPlaytimeColumn(leftColumn, 1), inline = true });
                if (rightColumn.Count > 0)
                {
                    fields.Add(new { name = "\u200B", value = BuildAdminPlaytimeColumn(rightColumn, leftColumn.Count + 1), inline = true });
                }
            }

            var embed = new
            {
                title = $":trophy: {GetServerLabel()} Admin Leaderboard (TOP {topEntries.Count})",
                color = 3447003,
                description = $"Admin playtime ranking for `{GetServerLabel()}`.",
                fields = fields.ToArray(),
                footer = new { text = $"Last update | {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC" },
                timestamp = DateTime.UtcNow.ToString("o")
            };

            await SendEmbedToChannelAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending admin playtime notification: {Message}", ex.Message);
        }
    }

    public async Task SendAdminActionNotificationAsync(string action, string adminName, ulong adminSteamId, ulong? targetSteamId, string? details, string serverId, string? targetName = null)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var embed = BuildModerationActionEmbed(
                string.IsNullOrWhiteSpace(action) ? "Admin Action" : action.Trim(),
                3447003,
                string.IsNullOrWhiteSpace(adminName) ? "-" : adminName,
                adminSteamId.ToString(),
                string.IsNullOrWhiteSpace(targetName) ? "-" : targetName,
                targetSteamId?.ToString() ?? "-",
                string.IsNullOrWhiteSpace(details) ? "-" : details,
                serverId);

            await SendEmbedToChannelAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending admin action notification: {Message}", ex.Message);
        }
    }

    private async Task PublishServerStatusAsync()
    {
        if (_discordServerStatusDbManager == null)
        {
            return;
        }

        try
        {
            var mapName = ServerIdentity.GetCurrentMap(_core);
            if (IsUnknownMapName(mapName))
            {
                _core.Logger.LogInformationIfEnabled("[CS2_Admin] Skipping Discord server status publish because current map is unknown.");
                return;
            }

            await _discordServerStatusDbManager.UpsertStatusAsync(new DiscordServerStatusSnapshot(
                ServerIdentity.GetServerId(_core),
                "default",
                GetServerLabel(),
                string.Empty,
                ServerIdentity.GetIp(_core),
                ServerIdentity.GetPort(_core),
                mapName,
                GetActiveHumanPlayerCount(),
                ServerIdentity.GetMaxPlayers(_core, _core.PlayerManager.PlayerCap),
                BuildJoinUrl(ServerIdentity.GetIp(_core), ServerIdentity.GetPort(_core))));
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error publishing shared server status: {Message}", ex.Message);
        }
    }

    private async Task UpsertServerStatusMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(_serverStatusChannelId))
        {
            return;
        }

        await _statusMessageLock.WaitAsync();
        try
        {
            var status = BuildCurrentServerStatus();
            if (IsUnknownMapName(status.MapName))
            {
                await RemoveServerStatusMessageAsync(status);
                _core.Logger.LogInformationIfEnabled("[CS2_Admin] Skipping Discord server status message because current map is unknown.");
                return;
            }

            var embed = BuildIndividualServerStatusEmbed(status, true);
            var state = GetServerState(status.ServerId);
            
            var messageKey = $"serverstatus:default:{status.ServerId}:{_serverStatusChannelId}";
            var messageId = _discordMessageStateDbManager == null
                ? null
                : await _discordMessageStateDbManager.GetMessageIdAsync(messageKey);

            var previousMessageId = !string.IsNullOrWhiteSpace(messageId)
                ? messageId
                : state.ServerStatusMessageId;

            if (!string.IsNullOrWhiteSpace(previousMessageId)
                && await UpdateEmbedInChannelAsync(_serverStatusChannelId, previousMessageId, embed, null, null))
            {
                return;
            }

            var newMessageId = await SendEmbedToChannelAsync(_serverStatusChannelId, embed, null, null);
            if (!string.IsNullOrWhiteSpace(newMessageId))
            {
                state.ServerStatusMessageId = newMessageId;
                SaveState();
                if (_discordMessageStateDbManager != null)
                {
                    await _discordMessageStateDbManager.UpsertMessageIdAsync(messageKey, _serverStatusChannelId, newMessageId);
                }

                if (!string.IsNullOrWhiteSpace(previousMessageId) && !string.Equals(previousMessageId, newMessageId, StringComparison.Ordinal))
                {
                    await DeleteMessageAsync(_serverStatusChannelId, previousMessageId);
                }
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error updating server status message: {Message}", ex.Message);
        }
        finally
        {
            _statusMessageLock.Release();
        }
    }

    private async Task RemoveServerStatusMessageAsync(DiscordServerStatus status)
    {
        if (string.IsNullOrWhiteSpace(_serverStatusChannelId))
        {
            return;
        }

        var state = GetServerState(status.ServerId);
        var messageKey = $"serverstatus:default:{status.ServerId}:{_serverStatusChannelId}";
        var messageId = _discordMessageStateDbManager == null
            ? null
            : await _discordMessageStateDbManager.GetMessageIdAsync(messageKey);

        var previousMessageId = !string.IsNullOrWhiteSpace(messageId)
            ? messageId
            : state.ServerStatusMessageId;

        if (!string.IsNullOrWhiteSpace(previousMessageId))
        {
            await DeleteMessageAsync(_serverStatusChannelId, previousMessageId);
        }

        if (!string.IsNullOrWhiteSpace(state.ServerStatusMessageId))
        {
            state.ServerStatusMessageId = null;
            SaveState();
        }
    }

    private static bool IsUnknownMapName(string? mapName)
    {
        return string.IsNullOrWhiteSpace(mapName)
            || string.Equals(mapName.Trim(), "unknown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mapName.Trim(), "-", StringComparison.OrdinalIgnoreCase);
    }

    private DiscordServerStatus BuildCurrentServerStatus()
    {
        var ip = ServerIdentity.GetIp(_core);
        var port = ServerIdentity.GetPort(_core);
        return new DiscordServerStatus
        {
            ServerId = ServerIdentity.GetServerId(_core),
            HubKey = "default",
            ServerName = GetServerLabel(),
            ButtonLabel = string.Empty,
            ServerIp = ip,
            ServerPort = port,
            MapName = ServerIdentity.GetCurrentMap(_core),
            PlayerCount = GetActiveHumanPlayerCount(),
            MaxPlayers = ServerIdentity.GetMaxPlayers(_core, _core.PlayerManager.PlayerCap),
            JoinUrl = BuildJoinUrl(ip, port),
            LastHeartbeatAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private async Task UpsertLeaderboardMessagesAsync()
    {
        if (_playerSessionManager == null || _rankLeaderboardDbManager == null || _discordMessageStateDbManager == null || string.IsNullOrWhiteSpace(_leaderboardChannelId))
        {
            return;
        }

        try
        {
            await UpsertSharedLeaderboardMessageAsync(
                $"leaderboard:points:{_leaderboardChannelId}",
                await BuildPointsLeaderboardEmbedAsync());

            await UpsertSharedLeaderboardMessageAsync(
                $"leaderboard:playtime:{_leaderboardChannelId}",
                await BuildPlaytimeLeaderboardEmbedAsync());
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error updating leaderboard messages: {Message}", ex.Message);
        }
    }

    private async Task UpsertSharedLeaderboardMessageAsync(string messageKey, object embed)
    {
        if (_discordMessageStateDbManager == null)
        {
            return;
        }

        var sharedMessageId = await _discordMessageStateDbManager.GetMessageIdAsync(messageKey);
        if (!string.IsNullOrWhiteSpace(sharedMessageId)
            && await UpdateEmbedInChannelAsync(_leaderboardChannelId, sharedMessageId, embed))
        {
            await CleanupDuplicateEmbedsAsync(_leaderboardChannelId, GetEmbedTitle(embed), sharedMessageId);
            return;
        }

        var messageId = await SendEmbedToChannelAsync(_leaderboardChannelId, embed);
        if (!string.IsNullOrWhiteSpace(messageId))
        {
            await _discordMessageStateDbManager.UpsertMessageIdAsync(messageKey, _leaderboardChannelId, messageId);
            await CleanupDuplicateEmbedsAsync(_leaderboardChannelId, GetEmbedTitle(embed), messageId);
        }
    }

    private object BuildIndividualServerStatusEmbed(DiscordServerStatus status, bool isOnline)
    {
        var displayName = string.IsNullOrWhiteSpace(status.ServerName)
            ? status.ButtonLabel
            : status.ServerName;
            
        var title = EscapeMarkdown(TrimLabel(displayName));
        
        var bannerUrl = _bannerUrl;

        var statusText = isOnline
            ? T("discord_server_status_online", "Online 🟢")
            : T("discord_server_status_offline", "Offline 🔴");
        var statusColor = isOnline ? 0x2ECC71 : 0xE74C3C;
        var mapName = string.IsNullOrWhiteSpace(status.MapName) ? T("discord_server_status_unknown_map", "unknown") : status.MapName;

        return new
        {
            title = title,
            color = statusColor,
            fields = new[]
            {
                new { name = T("discord_server_status_map_field", "🗺️ Map"), value = EscapeMarkdown(mapName), inline = true },
                new { name = T("discord_server_status_players_field", "👥 Players"), value = $"{status.PlayerCount}/{status.MaxPlayers}", inline = true },
                new { name = "\u200B", value = "\u200B", inline = true },
                new { name = T("discord_server_status_ip_field", "🌐 IP Address"), value = $"{status.ServerIp}:{status.ServerPort}", inline = true },
                new { name = T("discord_server_status_status_field", "📊 Status"), value = statusText, inline = true },
                new { name = "\u200B", value = "\u200B", inline = true },
                new { name = T("discord_server_status_connect_field", "⌨️ Quick Connect"), value = $"```\nconnect {status.ServerIp}:{status.ServerPort}\n```", inline = false }
            },
            image = string.IsNullOrWhiteSpace(bannerUrl) ? null : new { url = bannerUrl },
            footer = new { text = T("discord_last_update_footer", "Last update | {0} UTC", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")) },
            timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    private string T(string key, string fallback, params object[] args)
    {
        try
        {
            var localizer = PluginLocalizer.Get(_core);
            var value = args.Length == 0 ? localizer[key] : localizer[key, args];
            return string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
                ? (args.Length == 0 ? fallback : string.Format(System.Globalization.CultureInfo.InvariantCulture, fallback, args))
                : value;
        }
        catch
        {
            return args.Length == 0 ? fallback : string.Format(System.Globalization.CultureInfo.InvariantCulture, fallback, args);
        }
    }

    private static List<DiscordServerStatus> NormalizeServerStatuses(
        IReadOnlyList<DiscordServerStatus> statuses,
        DateTime nowUtc,
        TimeSpan occupiedServerMaxAge,
        TimeSpan emptyServerMaxAge)
    {
        return statuses
            .Where(status => !string.IsNullOrWhiteSpace(status.ServerIp) && status.ServerPort > 0)
            .Where(status => IsStatusFresh(status, nowUtc, occupiedServerMaxAge, emptyServerMaxAge))
            .GroupBy(
                status => BuildStatusDisplayKey(status),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(status => !string.Equals(status.MapName, "unknown", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(status => status.LastHeartbeatAt)
                .ThenByDescending(status => status.UpdatedAt)
                .First())
            .OrderBy(status => BuildStatusDisplayKey(status), StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.ServerPort)
            .ToList();
    }

    private static bool IsStatusFresh(
        DiscordServerStatus status,
        DateTime nowUtc,
        TimeSpan occupiedServerMaxAge,
        TimeSpan emptyServerMaxAge)
    {
        var maxAge = status.PlayerCount > 0 ? occupiedServerMaxAge : emptyServerMaxAge;
        var lastSeenAt = status.LastHeartbeatAt > status.UpdatedAt ? status.LastHeartbeatAt : status.UpdatedAt;
        return lastSeenAt >= nowUtc - maxAge;
    }

    private static string BuildStatusDisplayKey(DiscordServerStatus status)
    {
        if (!string.IsNullOrWhiteSpace(status.ServerName))
        {
            return status.ServerName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(status.ButtonLabel))
        {
            return status.ButtonLabel.Trim();
        }

        return $"{status.ServerIp}:{status.ServerPort}";
    }

    private async Task<object> BuildPointsLeaderboardEmbedAsync()
    {
        var entries = _rankLeaderboardDbManager == null
            ? []
            : await _rankLeaderboardDbManager.GetTopPointsAsync(_leaderboardTopLimit);

        var fields = new List<object>();

        if (entries.Count == 0)
        {
            fields.Add(new { name = "No data", value = "No rank data found in rank_base yet.", inline = false });
        }
        else
        {
            var leftColumn = entries.Take((entries.Count + 1) / 2).ToList();
            var rightColumn = entries.Skip(leftColumn.Count).ToList();

            fields.Add(new
            {
                name = "\u200B",
                value = BuildPointsLeaderboardColumn(leftColumn, 1),
                inline = true
            });

            if (rightColumn.Count > 0)
            {
                fields.Add(new
                {
                    name = "\u200B",
                    value = BuildPointsLeaderboardColumn(rightColumn, leftColumn.Count + 1),
                    inline = true
                });
            }
        }

        return new
        {
            title = $":trophy: Top 10 Rank",
            color = 0xF1C40F,
            fields = fields.ToArray(),
            footer = new { text = $"Last update | {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC" },
            timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    private async Task<object> BuildPlaytimeLeaderboardEmbedAsync()
    {
        var entries = _playerSessionManager == null
            ? []
            : await _playerSessionManager.GetGlobalTopPlaytimeAsync(_leaderboardTopLimit);

        var fields = new List<object>();

        if (entries.Count == 0)
        {
            fields.Add(new { name = "No data", value = "No global player playtime data yet.", inline = false });
        }
        else
        {
            var leftColumn = entries.Take((entries.Count + 1) / 2).ToList();
            var rightColumn = entries.Skip(leftColumn.Count).ToList();

            fields.Add(new
            {
                name = "\u200B",
                value = BuildPlaytimeLeaderboardColumn(leftColumn, 1),
                inline = true
            });

            if (rightColumn.Count > 0)
            {
                fields.Add(new
                {
                    name = "\u200B",
                    value = BuildPlaytimeLeaderboardColumn(rightColumn, leftColumn.Count + 1),
                    inline = true
                });
            }
        }

        return new
        {
            title = $":trophy: Top 10 Playtime",
            color = 0x3498DB,
            fields = fields.ToArray(),
            footer = new { text = $"Last update | {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC" },
            timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    private async Task<string?> SendEmbedToChannelAsync(string channelId, object embed, string? messageContent = null, object[]? components = null, bool allowEveryoneMention = false)
    {
        if (!IsDiscordChannelReady("send embed", channelId))
        {
            return null;
        }

        return await CreateMessageAsync(channelId, BuildMessagePayload(messageContent, embed, components, allowEveryoneMention));
    }

    private async Task<string?> SendMessageToChannelAsync(string channelId, string messageContent)
    {
        if (!IsDiscordChannelReady("send message", channelId) || string.IsNullOrWhiteSpace(messageContent))
        {
            return null;
        }

        return await CreateMessageAsync(channelId, BuildMessagePayload(messageContent, null, null));
    }

    private async Task<bool> UpdateEmbedInChannelAsync(string channelId, string messageId, object embed, string? messageContent = null, object[]? components = null, bool allowEveryoneMention = false)
    {
        if (!HasBotConfiguration() || string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(messageId))
        {
            return false;
        }

        return await UpdateMessageAsync(channelId, messageId, BuildMessagePayload(messageContent, embed, components, allowEveryoneMention));
    }

    private async Task<string?> CreateMessageAsync(string channelId, object payload)
    {
        var endpoint = $"{DiscordApiBaseUrl}/channels/{channelId}/messages";
        _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][DiscordREST] create message request channel={ChannelId}", MaskChannelId(channelId));
        using var request = BuildDiscordRequest(HttpMethod.Post, endpoint, payload);
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            await LogDiscordFailureAsync("create message", response);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<DiscordMessageResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        _core.Logger.LogInformationIfEnabled(
            "[CS2_Admin][Debug][DiscordREST] create message success channel={ChannelId} messageId={MessageId}",
            MaskChannelId(channelId),
            string.IsNullOrWhiteSpace(data?.Id) ? "-" : data.Id);

        return data?.Id;
    }

    private async Task<bool> RespondToInteractionAsync(string interactionId, string interactionToken, int type, object? data = null)
    {
        if (!HasBotConfiguration())
        {
            return false;
        }

        var endpoint = $"{DiscordApiBaseUrl}/interactions/{interactionId}/{interactionToken}/callback";
        using var request = BuildDiscordRequest(HttpMethod.Post, endpoint, new { type, data });
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        await LogDiscordFailureAsync("respond to interaction", response);
        return false;
    }

    private async Task<bool> UpdateMessageAsync(string channelId, string messageId, object payload)
    {
        var endpoint = $"{DiscordApiBaseUrl}/channels/{channelId}/messages/{messageId}";
        using var request = BuildDiscordRequest(HttpMethod.Patch, endpoint, payload);
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        await LogDiscordFailureAsync("update message", response);
        return false;
    }

    private async Task<bool> DeleteMessageAsync(string channelId, string messageId)
    {
        if (!HasBotConfiguration() || string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(messageId))
        {
            return false;
        }

        var endpoint = $"{DiscordApiBaseUrl}/channels/{channelId}/messages/{messageId}";
        using var request = new HttpRequestMessage(HttpMethod.Delete, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return true;
        }

        await LogDiscordFailureAsync("delete message", response);
        return false;
    }

    private async Task CleanupDuplicateEmbedsAsync(string channelId, string? title, string keepMessageId)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(keepMessageId))
        {
            return;
        }

        var duplicateIds = await GetDuplicateMessageIdsByTitleAsync(channelId, title, keepMessageId);
        foreach (var duplicateId in duplicateIds)
        {
            await DeleteMessageAsync(channelId, duplicateId);
        }
    }

    private async Task<List<string>> GetDuplicateMessageIdsByTitleAsync(string channelId, string title, string keepMessageId)
    {
        if (!HasBotConfiguration() || string.IsNullOrWhiteSpace(channelId))
        {
            return [];
        }

        var endpoint = $"{DiscordApiBaseUrl}/channels/{channelId}/messages?limit=50";
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            await LogDiscordFailureAsync("fetch messages", response);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var duplicateIds = new List<string>();

        foreach (var messageElement in document.RootElement.EnumerateArray())
        {
            var messageId = messageElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(messageId) || string.Equals(messageId, keepMessageId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!messageElement.TryGetProperty("embeds", out var embedsElement) || embedsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var embedElement in embedsElement.EnumerateArray())
            {
                var embedTitle = embedElement.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
                if (string.Equals(embedTitle, title, StringComparison.Ordinal))
                {
                    duplicateIds.Add(messageId);
                    break;
                }
            }
        }

        return duplicateIds;
    }

    private static string? GetEmbedTitle(object embed)
    {
        return embed.GetType().GetProperty("title")?.GetValue(embed) as string;
    }

    private static JsonObject BuildMessagePayload(string? messageContent, object? embed, object[]? components, bool allowEveryoneMention = false)
    {
        var payload = new JsonObject
        {
            ["content"] = string.IsNullOrWhiteSpace(messageContent) ? null : messageContent,
            ["allowed_mentions"] = JsonSerializer.SerializeToNode(
                allowEveryoneMention
                    ? new { parse = new[] { "everyone" } }
                    : new { parse = Array.Empty<string>() },
                JsonOptions)
        };

        if (embed != null)
        {
            payload["embeds"] = JsonSerializer.SerializeToNode(new[] { embed }, JsonOptions);
        }

        if (components is { Length: > 0 })
        {
            payload["components"] = JsonSerializer.SerializeToNode(components, JsonOptions);
        }

        return payload;
    }

    private HttpRequestMessage BuildDiscordRequest(HttpMethod method, string endpoint, object payload)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        return request;
    }

    private async Task LogDiscordFailureAsync(string action, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        _core.Logger.LogWarningIfEnabled(
            "[CS2_Admin] Discord bot {Action} failed with status {StatusCode}. Response: {Body}",
            action,
            response.StatusCode,
            string.IsNullOrWhiteSpace(body) ? "-" : body);
    }

    private bool HasBotConfiguration()
    {
        return !string.IsNullOrWhiteSpace(_botToken);
    }

    private bool IsDiscordChannelReady(string action, string channelId)
    {
        if (string.IsNullOrWhiteSpace(_botToken))
        {
            LogDiscordConfigurationWarning(action, "BotToken is empty");
            return false;
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            LogDiscordConfigurationWarning(action, "channel id is empty");
            return false;
        }

        return true;
    }

    private void LogDiscordConfigurationWarning(string action, string reason)
    {
        var key = $"{action}:{reason}";
        var now = DateTime.UtcNow;
        if (_configurationWarningTimestamps.TryGetValue(key, out var lastLogged) && (now - lastLogged).TotalSeconds < 60)
        {
            return;
        }

        _configurationWarningTimestamps[key] = now;
        _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord bot cannot {Action}: {Reason}.", action, reason);
    }

    private static string MaskChannelId(string? channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return "-";
        }

        var value = channelId.Trim();
        return value.Length <= 8
            ? value
            : $"...{value[^6..]}";
    }

    private void StartGatewayConnection()
    {
        _gatewayCts?.Cancel();
        _gatewayCts = new CancellationTokenSource();
        _gatewayTask = Task.Run(() => RunGatewayLoopAsync(_gatewayCts.Token));
    }

    private async Task RunGatewayLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                _gatewaySocket = socket;

                await socket.ConnectAsync(new Uri("wss://gateway.discord.gg/?v=10&encoding=json"), cancellationToken);
                await ReceiveGatewayMessagesAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord gateway connection failed: {Message}", ex.Message);
            }
            finally
            {
                _gatewaySocket = null;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ReceiveGatewayMessagesAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var payload = await ReceiveGatewayPayloadAsync(socket, cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            await HandleGatewayPayloadAsync(socket, payload, cancellationToken);
        }
    }

    private async Task<string?> ReceiveGatewayPayloadAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", cancellationToken);
                }
                catch
                {
                }

                return null;
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task HandleGatewayPayloadAsync(ClientWebSocket socket, string payload, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("s", out var seqElement) && seqElement.ValueKind == JsonValueKind.Number)
        {
            _gatewaySequence = seqElement.GetInt32();
        }

        var op = root.GetProperty("op").GetInt32();
        switch (op)
        {
            case 0:
                {
                    if (root.TryGetProperty("t", out var tElement) && tElement.ValueKind == JsonValueKind.String)
                    {
                        var eventName = tElement.GetString();
                        if (eventName == "READY" && root.TryGetProperty("d", out var readyData))
                        {
                            if (readyData.TryGetProperty("session_id", out var sessionIdElement))
                            {
                                _gatewaySessionId = sessionIdElement.GetString();
                            }
                        }
                        else if (eventName == "INTERACTION_CREATE" && root.TryGetProperty("d", out var dElement))
                        {
                            var interactionData = dElement.Clone();
                            _ = Task.Run(() => HandleInteractionCreateAsync(interactionData), cancellationToken);
                        }
                    }
                    break;
                }
            case 10:
                {
                    var heartbeatIntervalMs = root.GetProperty("d").GetProperty("heartbeat_interval").GetInt32();
                    _gatewayHeartbeatTask = Task.Run(() => RunHeartbeatLoopAsync(socket, heartbeatIntervalMs, cancellationToken), cancellationToken);
                    if (!string.IsNullOrWhiteSpace(_gatewaySessionId) && _gatewaySequence.HasValue)
                    {
                        await SendResumeAsync(socket, cancellationToken);
                    }
                    else
                    {
                        await SendIdentifyAsync(socket, cancellationToken);
                    }
                    break;
                }
            case 1:
                await SendHeartbeatAsync(socket, cancellationToken);
                break;
            case 7:
            case 9:
                if (op == 9)
                {
                    _gatewaySessionId = null;
                    _gatewaySequence = null;
                }
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", cancellationToken);
                }
                catch
                {
                }
                break;
            case 11:
                break;
        }
    }

    private async Task HandleInteractionCreateAsync(JsonElement data)
    {
        try
        {
            var id = data.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var token = data.TryGetProperty("token", out var tokenElement) ? tokenElement.GetString() : null;
            var applicationId = data.TryGetProperty("application_id", out var applicationIdElement) ? applicationIdElement.GetString() : null;
            var type = data.TryGetProperty("type", out var typeElement) ? typeElement.GetInt32() : 0; // 3 = Message Component

            if (type == 3 && id != null && token != null && data.TryGetProperty("data", out var componentData))
            {
                var customId = componentData.TryGetProperty("custom_id", out var customIdElement) ? customIdElement.GetString() : null;
                if (customId != null && customId.StartsWith("report_resolve_"))
                {
                    await HandleReportResolveInteractionAsync(id, token, applicationId, data, customId);
                }
                else if (customId != null && customId.StartsWith("report_punish_"))
                {
                    await HandleReportPunishInteractionAsync(id, token, applicationId, data, customId);
                }
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error handling interaction: {Message}", ex.Message);
        }
    }

    private async Task HandleReportResolveInteractionAsync(string interactionId, string interactionToken, string? applicationId, JsonElement data, string customId)
    {
        try
        {
            if (!await RespondToInteractionAsync(interactionId, interactionToken, 6))
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord resolve interaction defer failed for custom_id={CustomId}", customId);
                return;
            }

            var member = data.GetProperty("member");
            var user = member.GetProperty("user");
            var userId = user.GetProperty("id").GetString();

            var message = data.GetProperty("message");
            var messageId = message.TryGetProperty("id", out var messageIdElement) ? messageIdElement.GetString() : null;
            var channelId = message.TryGetProperty("channel_id", out var channelIdElement) ? channelIdElement.GetString() : null;
            var embeds = message.GetProperty("embeds");
            if (embeds.GetArrayLength() == 0)
            {
                await SendInteractionFollowupAsync(applicationId, interactionToken, T("discord_report_resolve_followup_missing_embed", "Report message embed could not be found."));
                return;
            }

            var oldEmbed = embeds[0];

            var newEmbed = JsonObject.Create(oldEmbed);
            if (newEmbed == null)
            {
                await SendInteractionFollowupAsync(applicationId, interactionToken, T("discord_report_resolve_followup_parse_failed", "Report message embed could not be parsed."));
                return;
            }

            if (newEmbed != null)
            {
                newEmbed["color"] = 65433; // Green

                if (newEmbed.TryGetPropertyValue("description", out var descNode) && descNode != null)
                {
                    var desc = descNode.GetValue<string>();
                    newEmbed["description"] = $"{desc}\n\n{T("discord_report_resolved_status", "**Status:** ✅ Resolved by <@{0}>", userId ?? "0")}";
                }
            }

            var payload = new
            {
                embeds = new JsonObject[] { newEmbed! },
                components = Array.Empty<object>()
            };

            if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(messageId) || !await UpdateMessageAsync(channelId, messageId, payload))
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord resolve interaction message update failed for custom_id={CustomId}", customId);
                await SendInteractionFollowupAsync(applicationId, interactionToken, T("discord_report_resolve_followup_failed", "Report could not be marked as resolved."));
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error in resolve interaction: {Message}", ex.Message);
            await SendInteractionFollowupAsync(applicationId, interactionToken, T("discord_report_resolve_internal_error", "An internal error occurred while resolving the report."));
        }
    }

    private async Task HandleReportPunishInteractionAsync(string interactionId, string interactionToken, string? applicationId, JsonElement data, string customId)
    {
        try
        {
            if (!await RespondToInteractionAsync(interactionId, interactionToken, 5, new { flags = 64 }))
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord punish interaction defer failed for custom_id={CustomId}", customId);
                return;
            }

            var parts = customId.Split('_');
            if (parts.Length < 3 || !ulong.TryParse(parts[2], out var targetSteamId))
            {
                await EditInteractionOriginalResponseAsync(applicationId, interactionToken, BuildInteractionEditErrorPayload(T("discord_report_target_parse_failed", "Report target SteamID could not be parsed.")));
                return;
            }

            var warns = _warnManager != null ? await _warnManager.GetWarnHistoryAsync(targetSteamId, WarnHistoryFilter.All, 5) : [];
            var logs = _adminLogManager != null ? await _adminLogManager.GetTargetHistoryAsync(targetSteamId, 5) : [];

            var descBuilder = new System.Text.StringBuilder();
            if (warns.Count == 0 && logs.Count == 0)
            {
                descBuilder.AppendLine(T("discord_report_no_punishments", "No recent punishments found for this player."));
            }
            else
            {
                if (warns.Count > 0)
                {
                    descBuilder.AppendLine(T("discord_report_recent_warnings", "**Recent Warnings:**"));
                    foreach (var warn in warns)
                    {
                        descBuilder.AppendLine($"- [{warn.CreatedAt:yyyy-MM-dd}] `{warn.Reason}` by {warn.AdminName}");
                    }
                    descBuilder.AppendLine();
                }

                if (logs.Count > 0)
                {
                    descBuilder.AppendLine(T("discord_report_recent_actions", "**Recent Actions:**"));
                    foreach (var log in logs)
                    {
                        descBuilder.AppendLine($"- [{log.CreatedAt:yyyy-MM-dd}] `{log.Action}`: {log.Details}");
                    }
                }
            }

            var embed = new
            {
                title = T("discord_report_punishments_title", "Player Punishments"),
                description = descBuilder.ToString(),
                color = 16711680 // Red
            };

            var payload = new
            {
                content = "",
                embeds = new[] { embed }
            };

            if (!await EditInteractionOriginalResponseAsync(applicationId, interactionToken, payload))
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord punish interaction response edit failed for custom_id={CustomId}", customId);
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error in punish interaction: {Message}", ex.Message);
            await EditInteractionOriginalResponseAsync(applicationId, interactionToken, BuildInteractionEditErrorPayload(T("discord_report_punishments_internal_error", "An internal error occurred while loading punishments.")));
        }
    }

    private async Task SendInteractionErrorAsync(string interactionId, string interactionToken, string message)
    {
        await RespondToInteractionAsync(interactionId, interactionToken, 4, BuildInteractionErrorPayload(message));
    }

    private async Task<bool> SendInteractionFollowupAsync(string? applicationId, string interactionToken, string message)
    {
        if (!HasBotConfiguration() || string.IsNullOrWhiteSpace(applicationId))
        {
            return false;
        }

        var endpoint = $"{DiscordApiBaseUrl}/webhooks/{applicationId}/{interactionToken}";
        using var request = BuildDiscordRequest(HttpMethod.Post, endpoint, BuildInteractionErrorPayload(message));
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        await LogDiscordFailureAsync("send interaction followup", response);
        return false;
    }

    private async Task<bool> EditInteractionOriginalResponseAsync(string? applicationId, string interactionToken, object payload)
    {
        if (!HasBotConfiguration() || string.IsNullOrWhiteSpace(applicationId))
        {
            return false;
        }

        var endpoint = $"{DiscordApiBaseUrl}/webhooks/{applicationId}/{interactionToken}/messages/@original";
        using var request = BuildDiscordRequest(HttpMethod.Patch, endpoint, payload);
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        await LogDiscordFailureAsync("edit interaction response", response);
        return false;
    }

    private static object BuildInteractionErrorPayload(string message)
    {
        return new
        {
            content = $"CS2_Admin: {message}",
            flags = 64
        };
    }

    private static object BuildInteractionEditErrorPayload(string message)
    {
        return new
        {
            content = $"CS2_Admin: {message}",
            embeds = Array.Empty<object>()
        };
    }

    private async Task RunHeartbeatLoopAsync(ClientWebSocket socket, int heartbeatIntervalMs, CancellationToken cancellationToken)
    {
        var jitterMs = Random.Shared.Next(0, Math.Max(heartbeatIntervalMs, 1));
        await Task.Delay(jitterMs, cancellationToken);

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            await SendHeartbeatAsync(socket, cancellationToken);
            await Task.Delay(heartbeatIntervalMs, cancellationToken);
        }
    }

    private async Task SendHeartbeatAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            op = 1,
            d = _gatewaySequence
        }, JsonOptions);

        await SendGatewayPayloadAsync(socket, payload, cancellationToken);
    }

    private async Task SendIdentifyAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            op = 2,
            d = new
            {
                token = _botToken,
                intents = 1,
                properties = new
                {
                    os = Environment.OSVersion.Platform.ToString(),
                    browser = "CS2_Admin",
                    device = "CS2_Admin"
                },
                presence = new
                {
                    since = (long?)null,
                    activities = Array.Empty<object>(),
                    status = "online",
                    afk = false
                }
            }
        }, JsonOptions);

        await SendGatewayPayloadAsync(socket, payload, cancellationToken);
    }

    private async Task SendResumeAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            op = 6,
            d = new
            {
                token = _botToken,
                session_id = _gatewaySessionId,
                seq = _gatewaySequence
            }
        }, JsonOptions);

        await SendGatewayPayloadAsync(socket, payload, cancellationToken);
    }

    private static async Task SendGatewayPayloadAsync(ClientWebSocket socket, string payload, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static string FormatPlaytime(int totalMinutes)
    {
        if (totalMinutes <= 0)
        {
            return "0m";
        }

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours == 0)
        {
            return $"{minutes}m";
        }

        return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
    }

    private static string FormatDurationFromSeconds(long totalSeconds)
    {
        if (totalSeconds <= 0)
        {
            return "0m";
        }

        var totalMinutes = (int)Math.Floor(totalSeconds / 60d);
        if (totalMinutes < 60)
        {
            return $"{totalMinutes}m";
        }

        var days = totalMinutes / (60 * 24);
        var hours = (totalMinutes / 60) % 24;
        var minutes = totalMinutes % 60;

        if (days > 0)
        {
            return minutes == 0
                ? $"{days}d {hours}h"
                : $"{days}d {hours}h {minutes}m";
        }

        return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
    }

    private static string BuildPlaytimeLeaderboardColumn(IReadOnlyList<PlayerPlaytimeEntry> entries, int startRank)
    {
        var lines = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var rank = startRank + i;
            lines.Add($"{GetRankPrefix(rank)} {BuildSteamProfileMarkdown(entry.PlayerName, entry.SteamId)}");
            lines.Add($"Playtime: `{FormatDurationFromSeconds(entry.TotalSeconds)}`");
            if (i < entries.Count - 1)
            {
                lines.Add(string.Empty);
            }
        }

        return string.Join("\n", lines);
    }

    private static string BuildPointsLeaderboardColumn(IReadOnlyList<RankLeaderboardEntry> entries, int startRank)
    {
        var lines = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var rank = startRank + i;
            lines.Add($"{GetRankPrefix(rank)} {BuildSteamProfileMarkdown(entry.Name, entry.Steam)}");
            lines.Add($"Points: `{entry.Points}`");
            lines.Add($"Kills: `{entry.Kills}` | Deaths: `{entry.Deaths}`");
            if (i < entries.Count - 1)
            {
                lines.Add(string.Empty);
            }
        }

        return string.Join("\n", lines);
    }

    private static string BuildAdminPlaytimeColumn(IReadOnlyList<AdminPlaytime> entries, int startRank)
    {
        var lines = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var rank = startRank + i;
            lines.Add($"{GetRankPrefix(rank)} {BuildSteamProfileMarkdown(entry.PlayerName, entry.SteamId)}");
            lines.Add($"Playtime: `{FormatPlaytime(entry.PlaytimeMinutes)}`");
            if (i < entries.Count - 1)
            {
                lines.Add(string.Empty);
            }
        }

        return string.Join("\n", lines);
    }

    private static string GetRankPrefix(int rank)
    {
        return rank switch
        {
            1 => ":first_place:",
            2 => ":second_place:",
            3 => ":third_place:",
            _ => $"`{rank}.`"
        };
    }

    private static string BuildSteamProfileMarkdown(string? playerName, ulong steamId)
    {
        var safeName = EscapeMarkdown(TrimPlayerName(playerName));
        return steamId > 0
            ? $"**[{safeName}]({BuildSteamProfileUrl(steamId)})**"
            : $"**{safeName}**";
    }

    private static string BuildSteamProfileMarkdown(string? playerName, string? steamId)
    {
        var safeName = EscapeMarkdown(TrimPlayerName(playerName));
        if (PlayerUtils.TryParseSteamId(steamId ?? string.Empty, out var steamId64) && steamId64 > 0)
        {
            return $"**[{safeName}]({BuildSteamProfileUrl(steamId64)})**";
        }

        return $"**{safeName}**";
    }

    private static string CountryCodeToDiscordFlag(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return ":flag_white:";
        }

        var normalized = countryCode.Trim().ToLowerInvariant();
        return normalized.Length == 2 && normalized.All(c => c is >= 'a' and <= 'z')
            ? $":flag_{normalized}:"
            : ":flag_white:";
    }

    private string GetServerLabel()
    {
        if (!string.IsNullOrWhiteSpace(_serverName))
        {
            return _serverName.Trim();
        }

        var configuredName = ServerIdentity.GetName(_core);
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            return configuredName;
        }

        var serverId = ServerIdentity.GetServerId(_core);
        return string.IsNullOrWhiteSpace(serverId) ? "Server" : serverId;
    }

    private int GetActiveHumanPlayerCount()
    {
        return _core.PlayerManager
            .GetAllPlayers()
            .Count(player => player.IsValid && !player.IsFakeClient);
    }

    private async Task<PlayerSnapshot> BuildPlayerSnapshotAsync(string? playerName, ulong steamId, string? ipAddress, bool includeGeoLookup = true)
    {
        var displayName = string.IsNullOrWhiteSpace(playerName) ? "Unknown" : playerName.Trim();
        var normalizedIp = NormalizeIp(ipAddress);
        var location = includeGeoLookup
            ? await ResolveGeoLocationAsync(normalizedIp)
            : GeoLocation.Unknown();
        var resolvedIp = string.IsNullOrWhiteSpace(normalizedIp) ? "Unknown" : normalizedIp;
        return new PlayerSnapshot(displayName, steamId, resolvedIp, location.CountryCode, location.CountryName);
    }

    private async Task<GeoLocation> ResolveGeoLocationAsync(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || IsPrivateOrLocalIp(ipAddress))
        {
            return GeoLocation.Unknown();
        }

        try
        {
            using var response = await _httpClient.GetAsync($"https://ipwho.is/{Uri.EscapeDataString(ipAddress)}");
            if (!response.IsSuccessStatusCode)
            {
                return GeoLocation.Unknown();
            }

            var json = await response.Content.ReadAsStringAsync();
            var geoResponse = JsonSerializer.Deserialize<GeoIpResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (geoResponse is null || !geoResponse.Success || string.IsNullOrWhiteSpace(geoResponse.CountryCode))
            {
                return GeoLocation.Unknown();
            }

            return new GeoLocation(
                geoResponse.CountryCode.Trim().ToUpperInvariant(),
                string.IsNullOrWhiteSpace(geoResponse.Country) ? "Unknown" : geoResponse.Country.Trim());
        }
        catch
        {
            return GeoLocation.Unknown();
        }
    }

    private object BuildModerationActionEmbed(
        string action,
        int color,
        string adminName,
        string adminSteamId,
        string playerName,
        string playerId,
        string details,
        string? serverId = null)
    {
        var resolvedAction = string.IsNullOrWhiteSpace(action) ? "Admin Action" : action.Trim();
        var resolvedServer = string.IsNullOrWhiteSpace(serverId) ? ServerIdentity.GetServerId(_core) : serverId.Trim();
        var adminValue = string.IsNullOrWhiteSpace(adminSteamId) || adminSteamId == "0"
            ? EscapeMarkdown(adminName)
            : $"[{EscapeMarkdown(adminName)}]({BuildSteamProfileUrl(adminSteamId)})";
        var playerValue = string.IsNullOrWhiteSpace(playerId) || playerId == "-" || playerId == "0"
            ? EscapeMarkdown(playerName)
            : $"[{EscapeMarkdown(playerName)}]({BuildSteamProfileUrl(playerId)})";

        return new
        {
            title = $"{GetServerLabel()} | Admin Log",
            description = $":shield: **{EscapeMarkdown(resolvedAction)}** action executed on `{(string.IsNullOrWhiteSpace(resolvedServer) ? GetServerLabel() : resolvedServer)}`.",
            color,
            fields = new object[]
            {
                new { name = "Action", value = $"`{EscapeMarkdown(resolvedAction)}`", inline = true },
                new { name = "Admin", value = adminValue, inline = true },
                new { name = "Target", value = playerValue, inline = true },
                new { name = "Admin SteamID", value = string.IsNullOrWhiteSpace(adminSteamId) || adminSteamId == "0" ? "-" : $"`{adminSteamId}`", inline = true },
                new { name = "Target ID", value = string.IsNullOrWhiteSpace(playerId) || playerId == "0" ? "-" : $"`{playerId}`", inline = true },
                new { name = "Server", value = $"`{(string.IsNullOrWhiteSpace(resolvedServer) ? "-" : resolvedServer)}`", inline = true },
                new { name = "Details", value = string.IsNullOrWhiteSpace(details) ? "-" : details, inline = false }
            },
            footer = new { text = "CS2_Admin | Admin Logs" },
            timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    private string ResolveChannelId(string preferredChannelId)
    {
        if (!string.IsNullOrWhiteSpace(preferredChannelId))
        {
            return preferredChannelId;
        }

        return _defaultChannelId;
    }

    private string ResolveLogChannelId(string preferredChannelId)
    {
        if (!string.IsNullOrWhiteSpace(preferredChannelId))
        {
            return preferredChannelId;
        }

        if (!string.IsNullOrWhiteSpace(_defaultChannelId))
        {
            return _defaultChannelId;
        }

        return _reportChannelId;
    }

    private object[]? BuildServerStatusComponents(IReadOnlyList<DiscordServerStatus> statuses)
    {
        var buttons = statuses
            .Where(x => !string.IsNullOrWhiteSpace(x.JoinUrl))
            .Take(25)
            .Select(x => new
            {
                type = 2,
                style = 5,
                label = x.ButtonLabel,
                url = x.JoinUrl
            })
            .ToList();

        if (buttons.Count == 0)
        {
            return null;
        }

        var rows = new List<object>();
        for (var i = 0; i < buttons.Count; i += 5)
        {
            rows.Add(new
            {
                type = 1,
                components = buttons.Skip(i).Take(5).ToArray()
            });
        }

        return rows.ToArray();
    }

    private string GetStatusButtonLabel()
    {
        if (!string.IsNullOrWhiteSpace(_serverName))
        {
            return TrimLabel(_serverName);
        }

        return TrimLabel(ServerIdentity.GetServerId(_core));
    }

    private string BuildJoinUrl(string ip, int port)
    {
        if (string.IsNullOrWhiteSpace(ip) || port <= 0)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(_customConnectUrl))
        {
            return _customConnectUrl.Replace("{IP}", ip).Replace("{PORT}", port.ToString());
        }

        return $"https://cs2browser.net/?search={Uri.EscapeDataString($"{ip}:{port}")}";
    }

    private string BuildSteamServerUrl()
    {
        var ip = ServerIdentity.GetIp(_core);
        var port = ServerIdentity.GetPort(_core);
        if (string.IsNullOrWhiteSpace(ip) || port <= 0 || ip == "0.0.0.0")
        {
            return BuildSteamProfileUrl(0);
        }

        return $"https://steamcommunity.com/linkfilter/?url=steam://connect/{ip}:{port}";
    }

    private static string BuildSteamProfileUrl(ulong steamId)
    {
        return steamId > 0
            ? $"https://steamcommunity.com/profiles/{steamId}"
            : "https://steamcommunity.com/";
    }

    private static string BuildSteamProfileUrl(string? steamId)
    {
        return ulong.TryParse(steamId, out var parsedSteamId)
            ? BuildSteamProfileUrl(parsedSteamId)
            : BuildSteamProfileUrl(0);
    }

    private static ulong? TryExtractSteamId(string? targetValue)
    {
        if (string.IsNullOrWhiteSpace(targetValue))
        {
            return null;
        }

        var match = TargetSteamIdRegex.Match(targetValue.Trim());
        if (!match.Success)
        {
            return null;
        }

        return ulong.TryParse(match.Groups["steamid"].Value, out var steamId) ? steamId : null;
    }

    private static string StripTrailingSteamId(string? targetValue)
    {
        if (string.IsNullOrWhiteSpace(targetValue))
        {
            return "-";
        }

        return TargetSteamIdRegex.Replace(targetValue.Trim(), string.Empty).Trim();
    }

    private static object[]? BuildLinkButtonComponents(params (string Label, string Url)[] links)
    {
        var validLinks = links
            .Where(x => !string.IsNullOrWhiteSpace(x.Label) && !string.IsNullOrWhiteSpace(x.Url))
            .Take(5)
            .Select(x => new
            {
                type = 2,
                style = 5,
                label = TrimLabel(x.Label),
                url = x.Url
            })
            .ToArray();

        if (validLinks.Length == 0)
        {
            return null;
        }

        return
        [
            new
            {
                type = 1,
                components = validLinks
            }
        ];
    }

    private object[]? BuildReportInteractiveComponents(string serverId, ulong targetSteamId, ulong reporterSteamId)
    {
        return new object[]
        {
            new
            {
                type = 1,
                components = new object[]
                {
                    new
                    {
                        type = 2,
                        style = 3, // Success / Green
                        label = T("discord_report_mark_resolved_button", "Mark as resolved"),
                        custom_id = $"report_resolve_{serverId}_{targetSteamId}_{reporterSteamId}",
                        emoji = new { name = "✅" }
                    },
                    new
                    {
                        type = 2,
                        style = 4, // Danger / Red
                        label = T("discord_report_player_punishments_button", "Player Punishments"),
                        custom_id = $"report_punish_{targetSteamId}",
                        emoji = new { name = "🚫" }
                    }
                }
            }
        };
    }

    private static string EscapeMarkdown(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }

    private static string TrimLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "Server";
        }

        var trimmed = label.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }

    private DiscordStateFile LoadState()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return new DiscordStateFile();
            }

            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<DiscordStateFile>(json, JsonOptions) ?? new DiscordStateFile();
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Failed to load discord-state.json: {Message}", ex.Message);
            return new DiscordStateFile();
        }
    }

    private void SaveState()
    {
        lock (_stateLock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_statePath, JsonSerializer.Serialize(_state, JsonOptions));
            }
            catch (Exception ex)
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Failed to save discord-state.json: {Message}", ex.Message);
            }
        }
    }

    private DiscordServerState GetServerState(string? serverId = null)
    {
        serverId ??= ServerIdentity.GetServerId(_core);
        lock (_stateLock)
        {
            if (!_state.Servers.TryGetValue(serverId, out var state))
            {
                state = new DiscordServerState();
                _state.Servers[serverId] = state;
            }

            return state;
        }
    }

    private DiscordServerState GetHubState(string hubKey)
    {
        lock (_stateLock)
        {
            if (!_state.Hubs.TryGetValue(hubKey, out var state))
            {
                state = new DiscordServerState();
                _state.Hubs[hubKey] = state;
            }

            return state;
        }
    }

    private static string NormalizeIp(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return string.Empty;
        }

        var value = ipAddress.Trim();
        var colonIndex = value.IndexOf(':');
        if (colonIndex > 0 && value.Count(c => c == ':') == 1)
        {
            value = value[..colonIndex];
        }

        return value;
    }

    private static string TrimPlayerName(string? playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return "Unknown";
        }

        var trimmed = playerName.Trim();
        return trimmed.Length <= 64 ? trimmed : trimmed[..64];
    }

    private static bool IsPrivateOrLocalIp(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || ipAddress.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(ipAddress, out var address))
        {
            return true;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        return address.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => IsPrivateIpv4(address.GetAddressBytes()),
            System.Net.Sockets.AddressFamily.InterNetworkV6 => address.IsIPv6LinkLocal || address.IsIPv6SiteLocal,
            _ => true
        };
    }

    private static bool IsPrivateIpv4(byte[] bytes)
    {
        if (bytes.Length != 4)
        {
            return true;
        }

        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    private sealed record PlayerSnapshot(string DisplayName, ulong SteamId, string IpAddress, string CountryCode, string CountryName)
    {
        public string CountryLabel => $"{CountryName} ({CountryCode})";
    }

    private sealed record GeoLocation(string CountryCode, string CountryName)
    {
        public static GeoLocation Unknown() => new("??", "Unknown");
    }

    private sealed class GeoIpResponse
    {
        public bool Success { get; set; }

        [JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }
    }

    private sealed class DiscordMessageResponse
    {
        public string? Id { get; set; }
    }

    private sealed class DiscordStateFile
    {
        public Dictionary<string, DiscordServerState> Servers { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, DiscordServerState> Hubs { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class DiscordServerState
    {
        public string? ServerStatusMessageId { get; set; }
        public string? LeaderboardMessageId { get; set; }
    }
}
