using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using System.Net;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace CS2_Admin.Commands;

public class IpBanCommand : CommandBase
{
    private readonly BanManager _banManager;
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly WarnManager _warnManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly PlayerIpDbManager _playerIpDbManager;
    private readonly PlayerSessionManager _playerSessionManager;
    private readonly RecentPlayersTracker _recentPlayersTracker;
    private readonly DiscordBotService _discord;
    private readonly SanctionMenuConfig _sanctions;
    private readonly MultiServerConfig _multiServerConfig;
    private readonly int _banType;
    private readonly PlayerSanctionStateService _sanctionStateService;

    public IpBanCommand(
        ISwiftlyCore core,
        BanManager banManager,
        MuteManager muteManager,
        GagManager gagManager,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        AdminLogManager adminLogManager,
        PlayerIpDbManager playerIpDbManager,
        PlayerSessionManager playerSessionManager,
        RecentPlayersTracker recentPlayersTracker,
        DiscordBotService discord,
        PermissionsConfig permissions,
        CommandsConfig commands,
        TagsConfig tags,
        MessagesConfig messages,
        SanctionMenuConfig sanctions,
        MultiServerConfig multiServerConfig,
        int banType,
        PlayerSanctionStateService sanctionStateService,
        PermissionService permissionService)
        : base(core, permissions, commands, tags, messages, adminLogManager, permissionService)
    {
        _banManager = banManager;
        _muteManager = muteManager;
        _gagManager = gagManager;
        _warnManager = warnManager;
        _adminDbManager = adminDbManager;
        _playerIpDbManager = playerIpDbManager;
        _playerSessionManager = playerSessionManager;
        _recentPlayersTracker = recentPlayersTracker;
        _discord = discord;
        _sanctions = sanctions;
        _multiServerConfig = multiServerConfig;
        _banType = banType is >= 1 and <= 3 ? banType : 1;
        _sanctionStateService = sanctionStateService;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            await HandleOnlineBanAsync(context, true);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] IpBan command failed");
        }
    }

    private async Task HandleOnlineBanAsync(ICommandContext context, bool ipMode)
    {
        var args = NormalizeArgs(context.Args, ipMode ? CommandsConfig.IpBan : CommandsConfig.Ban);

        if (!HasPerm(context, Permissions.Ban))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 2)
        {
            Reply(context, ipMode ? "ipban_usage" : "ban_usage");
            return;
        }

        var targetArg = args[0];
        if (!SanctionDurationParser.TryParseToMinutes(args[1], out var duration))
        {
            Reply(context, "invalid_duration");
            return;
        }

        var reason = args.Length > 2 ? string.Join(" ", args.Skip(2)) : L("no_reason");
        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        var adminSteamId = context.Sender?.SteamID ?? 0;
        var isGlobal = ResolveGlobalMode();
        var applyMode = ResolveBanApplyMode(ipMode);
        var shouldBanSteam = (applyMode & BanApplyMode.Steam) != 0;
        var shouldBanIp = (applyMode & BanApplyMode.Ip) != 0;
        var resolvedTarget = PlayerUtils.FindPlayerByTarget(Core, targetArg);
        var targetSnapshot = resolvedTarget == null
            ? null
            : new OnlineTargetSnapshot(
                resolvedTarget.PlayerID,
                resolvedTarget.SteamID,
                resolvedTarget.Controller.PlayerName ?? L("unknown"),
                resolvedTarget.IPAddress);

        if (targetSnapshot != null)
        {
            if (shouldBanIp && string.IsNullOrWhiteSpace(targetSnapshot.IpAddress))
            {
                Core.Scheduler.NextTick(() => Reply(context, "lastban_no_ip"));
                return;
            }

            if (!await ValidateCanPunishAsync(context, targetSnapshot.SteamId))
            {
                return;
            }

            _banManager.SetAdminContext(adminName, adminSteamId);

            var steamApplied = false;
            var ipApplied = false;
            var steamDbError = false;
            var ipDbError = false;

            if (shouldBanSteam)
            {
                var existingSteam = await _banManager.GetActiveBanFreshAsync(targetSnapshot.SteamId, null, _multiServerConfig.Enabled);
                if (existingSteam == null)
                {
                    _banManager.InvalidateCache(targetSnapshot.SteamId, null);
                    steamApplied = await _banManager.AddBanAsync(targetSnapshot.SteamId, targetSnapshot.Name, duration, reason, isGlobal);
                    if (!steamApplied)
                    {
                        steamDbError = true;
                    }
                }
            }

            if (shouldBanIp)
            {
                var existingIp = await _banManager.GetActiveBanFreshAsync(0, targetSnapshot.IpAddress, _multiServerConfig.Enabled);
                if (existingIp == null)
                {
                    _banManager.InvalidateCache(0, targetSnapshot.IpAddress);
                    ipApplied = await _banManager.AddIpBanAsync(targetSnapshot.IpAddress!, targetSnapshot.Name, duration, reason, isGlobal, targetSnapshot.SteamId);
                    if (!ipApplied)
                    {
                        ipDbError = true;
                    }
                }
            }

            if (!steamApplied && !ipApplied)
            {
                if (steamDbError || ipDbError)
                {
                    Core.Scheduler.NextTick(() => Reply(context, "ban_db_error"));
                }
                else
                {
                    Core.Scheduler.NextTick(() => Reply(context, "player_already_banned", targetSnapshot.Name));
                }
                return;
            }

            await _sanctionStateService.RefreshAsync(targetSnapshot.SteamId, targetSnapshot.IpAddress);

            var durationText = duration <= 0 ? L("duration_permanently") : L("duration_for_minutes", duration);
            Core.Scheduler.NextTick(() =>
            {
                BroadcastNotification(adminName, "banned_notification", targetSnapshot.Name, durationText, reason);

                var onlineTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSnapshot.SteamId);
                if (onlineTarget != null)
                {
                    var durationDisplay = duration <= 0 ? L("permanent") : L("duration_minutes", duration);
                    PlayerUtils.SendNotification(
                        onlineTarget,
                        Messages,
                        $"<font color='#ff0000'><b>{L("banned_personal_html")}</b></font><br><br>{L("label_duration")}: <font color='#ffcc00'>{durationDisplay}</font><br>{L("label_reason")}: <font color='#ffffff'>{reason}</font>",
                        $" \x02{L("prefix")}\x01 {L("banned_personal_chat", durationText, reason)}");

                    var kickDelaySeconds = Messages.BanKickDelaySeconds > 0
                        ? Messages.BanKickDelaySeconds
                        : Math.Max(1f, Messages.CenterHtmlDurationMs / 1000f);

                    Core.Scheduler.DelayBySeconds(kickDelaySeconds, () =>
                    {
                        var playerToKick = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSnapshot.SteamId);
                        playerToKick?.Kick($"Banned: {reason}", ENetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED);
                    });
                }
            });

            var actionName = shouldBanSteam && shouldBanIp
                ? "ban_both"
                : shouldBanIp ? "ipban" : "ban";
            await AdminLogManager.AddLogAsync(
                actionName,
                adminName,
                adminSteamId,
                targetSnapshot.SteamId,
                targetSnapshot.IpAddress,
                $"duration={duration};global={isGlobal};reason={reason};ban_type={_banType};steam_applied={steamApplied};ip_applied={ipApplied}",
                targetSnapshot.Name,
                targetSnapshot.PlayerId,
                reason);
            return;
        }

        if (!ipMode && PlayerUtils.TryParseSteamId(targetArg, out var offlineSteamId))
        {
            if (!shouldBanSteam && shouldBanIp)
            {
                Core.Scheduler.NextTick(() =>
                {
                    ReplyRaw(context, T("ban_type_requires_ip_target", "BanMode is IP-only. Use target IP with !ban/!ipban."));
                });
                return;
            }

            await AddOfflineSteamBanAsync(context, offlineSteamId, duration, reason, adminName, adminSteamId, isGlobal);

            if (shouldBanIp)
            {
                var knownIps = await _playerIpDbManager.GetAllKnownIpsAsync(offlineSteamId);
                var appliedCount = 0;
                foreach (var knownIp in knownIps.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var added = await AddOfflineIpBanAsync(context, knownIp, duration, reason, adminName, adminSteamId, isGlobal, notifyResult: false);
                    if (added)
                    {
                        appliedCount++;
                    }
                }

                if (appliedCount > 0)
                {
                    Core.Scheduler.NextTick(() =>
                    {
                        ReplyRaw(context, T("ban_type_known_ips_applied", "Applied IP bans for {0} known IP(s).", appliedCount));
                    });
                }
            }

            return;
        }

        if (shouldBanIp && TryNormalizeIpTarget(targetArg, out var normalizedIp))
        {
            await AddOfflineIpBanAsync(context, normalizedIp, duration, reason, adminName, adminSteamId, isGlobal);
            return;
        }

        if (ipMode)
        {
            await AddOfflineIpBanAsync(context, targetArg, duration, reason, adminName, adminSteamId, isGlobal);
            return;
        }

        Core.Scheduler.NextTick(() => Reply(context, "player_not_found"));
    }

    private bool ResolveGlobalMode()
    {
        if (!_multiServerConfig.Enabled)
        {
            return false;
        }

        return _multiServerConfig.GlobalBansByDefault;
    }

    private BanApplyMode ResolveBanApplyMode(bool ipMode)
    {
        if (ipMode)
        {
            return BanApplyMode.Ip;
        }

        return _banType switch
        {
            2 => BanApplyMode.Ip,
            3 => BanApplyMode.Steam | BanApplyMode.Ip,
            _ => BanApplyMode.Steam
        };
    }

    private static bool TryNormalizeIpTarget(string input, out string normalizedIp)
    {
        normalizedIp = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (!IPAddress.TryParse(input.Trim(), out var parsed))
        {
            return false;
        }

        normalizedIp = parsed.ToString();
        return true;
    }

    private string T(string key, string fallback, params object[] args)
    {
        try
        {
            var value = args.Length == 0 ? L(key) : L(key, args);
            return string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
                ? (args.Length == 0 ? fallback : string.Format(fallback, args))
                : value;
        }
        catch
        {
            return args.Length == 0 ? fallback : string.Format(fallback, args);
        }
    }

    private async Task<bool> ValidateCanPunishAsync(ICommandContext context, ulong targetSteamId)
    {
        return await PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, targetSteamId);
    }

    private async Task AddOfflineSteamBanAsync(
        ICommandContext context,
        ulong targetSteamId,
        int duration,
        string reason,
        string adminName,
        ulong adminSteamId,
        bool isGlobal)
    {
        var existingBan = await _banManager.GetActiveBanFreshAsync(targetSteamId, null, _multiServerConfig.Enabled);
        if (existingBan != null)
        {
            Core.Scheduler.NextTick(() => Reply(context, "steamid_already_banned", targetSteamId));
            return;
        }

        _banManager.SetAdminContext(adminName, adminSteamId);
        var ok = await _banManager.AddBanAsync(targetSteamId, targetSteamId.ToString(), duration, reason, isGlobal);
        if (!ok)
        {
            Core.Scheduler.NextTick(() => Reply(context, "ban_db_error"));
            return;
        }

        Core.Scheduler.NextTick(() =>
        {
            var durationDisplay = duration <= 0 ? L("permanent") : L("duration_minutes", duration);
            Reply(context, "addban_success", targetSteamId, durationDisplay);
        });

        await AdminLogManager.AddLogAsync("addban", adminName, adminSteamId, targetSteamId, null, $"duration={duration};global={isGlobal};reason={reason}", null, null, reason);
    }

    private async Task<bool> AddOfflineIpBanAsync(
        ICommandContext context,
        string ipAddress,
        int duration,
        string reason,
        string adminName,
        ulong adminSteamId,
        bool isGlobal,
        bool notifyResult = true)
    {
        if (!TryNormalizeIpTarget(ipAddress, out var normalizedIp))
        {
            if (notifyResult)
            {
                Core.Scheduler.NextTick(() => ReplyRaw(context, T("invalid_ip", "Invalid IP address.")));
            }
            return false;
        }

        var existing = await _banManager.GetActiveBanFreshAsync(0, normalizedIp, _multiServerConfig.Enabled);
        if (existing != null)
        {
            if (notifyResult)
            {
                Core.Scheduler.NextTick(() => Reply(context, "lastban_ip_already_banned", normalizedIp));
            }
            return false;
        }

        _banManager.SetAdminContext(adminName, adminSteamId);
        var ok = await _banManager.AddIpBanAsync(normalizedIp, normalizedIp, duration, reason, isGlobal);
        if (notifyResult)
        {
            Core.Scheduler.NextTick(() =>
            {
                ReplyRaw(context, ok
                    ? T("ipban_success", "IP {0} banned successfully.", normalizedIp)
                    : T("ban_db_error", "Failed to apply ban: database error. Check server console."));
            });
        }

        if (ok)
        {
            await AdminLogManager.AddLogAsync("ipban", adminName, adminSteamId, null, normalizedIp, $"duration={duration};global={isGlobal};reason={reason}", null, null, reason);
        }

        return ok;
    }

    private sealed record OnlineTargetSnapshot(int PlayerId, ulong SteamId, string Name, string? IpAddress);

    [Flags]
    private enum BanApplyMode
    {
        None = 0,
        Steam = 1,
        Ip = 2
    }
}
