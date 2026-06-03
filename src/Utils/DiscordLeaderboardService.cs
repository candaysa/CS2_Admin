using CS2_Admin.Database;
using CS2_Admin.Models;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public class DiscordLeaderboardService
{
    private readonly ISwiftlyCore _core;
    private readonly DiscordRestClient _restClient;
    private readonly string _leaderboardChannelId;
    private readonly int _leaderboardTopLimit;
    private PlayerSessionManager? _playerSessionManager;
    private RankLeaderboardDbManager? _rankLeaderboardDbManager;
    private DiscordMessageStateDbManager? _discordMessageStateDbManager;

    public DiscordLeaderboardService(ISwiftlyCore core, DiscordRestClient restClient,
        string leaderboardChannelId, int leaderboardTopLimit)
    {
        _core = core;
        _restClient = restClient;
        _leaderboardChannelId = leaderboardChannelId;
        _leaderboardTopLimit = leaderboardTopLimit;
    }

    public void SetDatabaseManagers(PlayerSessionManager? psm,
        RankLeaderboardDbManager? rldm,
        DiscordMessageStateDbManager? dmsdm)
    {
        _playerSessionManager = psm;
        _rankLeaderboardDbManager = rldm;
        _discordMessageStateDbManager = dmsdm;
    }

    public async Task UpsertLeaderboardMessagesAsync()
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
            && await _restClient.UpdateEmbedAsync(_leaderboardChannelId, sharedMessageId, embed) == true)
        {
            await _restClient.CleanupDuplicateEmbedsAsync(_leaderboardChannelId, DiscordHelpers.GetEmbedTitle(embed), sharedMessageId);
            return;
        }

        var messageId = await _restClient.SendEmbedAsync(_leaderboardChannelId, embed);
        if (!string.IsNullOrWhiteSpace(messageId))
        {
            await _discordMessageStateDbManager.UpsertMessageIdAsync(messageKey, _leaderboardChannelId, messageId);
            await _restClient.CleanupDuplicateEmbedsAsync(_leaderboardChannelId, DiscordHelpers.GetEmbedTitle(embed), messageId);
        }
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

    private static string BuildPlaytimeLeaderboardColumn(IReadOnlyList<PlayerPlaytimeEntry> entries, int startRank)
    {
        var lines = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var rank = startRank + i;
            lines.Add($"{DiscordHelpers.GetRankPrefix(rank)} {DiscordHelpers.BuildSteamProfileMarkdown(entry.PlayerName, entry.SteamId)}");
            lines.Add($"Playtime: `{DiscordHelpers.FormatDurationFromSeconds(entry.TotalSeconds)}`");
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
            lines.Add($"{DiscordHelpers.GetRankPrefix(rank)} {DiscordHelpers.BuildSteamProfileMarkdown(entry.Name, entry.Steam)}");
            lines.Add($"Points: `{entry.Points}`");
            lines.Add($"Kills: `{entry.Kills}` | Deaths: `{entry.Deaths}`");
            if (i < entries.Count - 1)
            {
                lines.Add(string.Empty);
            }
        }

        return string.Join("\n", lines);
    }
}
