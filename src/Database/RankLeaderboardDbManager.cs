using Dapper;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public sealed class RankLeaderboardEntry
{
    public string Steam { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Points { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public long Playtime { get; set; }
}

public class RankLeaderboardDbManager
{
    private readonly ISwiftlyCore _core;

    public RankLeaderboardDbManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public async Task<List<RankLeaderboardEntry>> GetTopPointsAsync(int limit)
    {
        var safeLimit = Math.Clamp(limit, 1, 50);

        try
        {
            using var connection = _core.Database.GetConnection("host");
            var rows = await connection.QueryAsync<RankLeaderboardEntry>(
                """
                SELECT
                    `steam` AS `Steam`,
                    `name` AS `Name`,
                    `value` AS `Points`,
                    `kills` AS `Kills`,
                    `deaths` AS `Deaths`,
                    `playtime` AS `Playtime`
                FROM `rank_base`
                ORDER BY `value` DESC, `kills` DESC, `name` ASC
                LIMIT @Limit
                """,
                new { Limit = safeLimit });

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error getting rank leaderboard: {Message}", ex.Message);
            return [];
        }
    }
}
