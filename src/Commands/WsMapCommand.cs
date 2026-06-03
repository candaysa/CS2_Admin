using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using System.Collections.Concurrent;
using System.Text.Json;

namespace CS2_Admin.Commands;

public class WsMapCommand : CommandBase
{
    private const string WorkshopDetailsApiUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
    private static readonly HttpClient WorkshopApiClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };
    private static readonly ConcurrentDictionary<uint, string> WorkshopNameCache = new();

    private readonly GameMapsConfig _gameMaps;
    private readonly WorkshopMapsConfig _workshopMaps;

    public WsMapCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        GameMapsConfig gameMaps,
        WorkshopMapsConfig workshopMaps)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _gameMaps = gameMaps;
        _workshopMaps = workshopMaps;

        foreach (var entry in _workshopMaps.Maps)
        {
            if (!string.IsNullOrWhiteSpace(entry.Key))
            {
                WorkshopNameCache.TryAdd(entry.Value, entry.Key);
            }
        }
    }

    public override void Execute(ICommandContext context)
    {
        var args = NormalizeArgs(context.Args, CommandsConfig.ChangeWSMap);

        if (!HasPerm(context, Permissions.ChangeWSMap))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 1)
        {
            Reply(context, "wsmap_usage");
            ReplyRaw(context, L("wsmap_available", string.Join(", ", _workshopMaps.Maps.Keys)));
            return;
        }

        var input = args[0];
        uint workshopId;
        string mapDisplayName;

        if (!uint.TryParse(input, out workshopId))
        {
            var matchedMap = _workshopMaps.Maps.FirstOrDefault(m =>
                m.Key.Contains(input, StringComparison.OrdinalIgnoreCase));

            if (matchedMap.Key == null)
            {
                Reply(context, "wsmap_not_found", input);
                return;
            }

            workshopId = matchedMap.Value;
            mapDisplayName = matchedMap.Key;
            WorkshopNameCache[workshopId] = mapDisplayName;
        }
        else
        {
            mapDisplayName = ResolveWorkshopDisplayName(workshopId);
        }

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        const float changeDelaySeconds = 3f;

        BroadcastNotification(adminName, "wsmap_changing", mapDisplayName, changeDelaySeconds);

        Core.Scheduler.DelayBySeconds(changeDelaySeconds, () =>
        {
            Core.Engine.ExecuteCommand($"changelevel workshop/{workshopId}");
        });

        AdminLogManager.AddLogAsync("wsmap", adminName, context.Sender?.SteamID ?? 0, null, null, $"workshop_id={workshopId};map_name={mapDisplayName}");
        Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} changed to workshop map {MapName} ({WorkshopId})", adminName, mapDisplayName, workshopId);
    }

    private string ResolveWorkshopDisplayName(uint workshopId)
    {
        var knownMap = _workshopMaps.Maps.FirstOrDefault(m => m.Value == workshopId);
        if (!string.IsNullOrWhiteSpace(knownMap.Key))
        {
            WorkshopNameCache[workshopId] = knownMap.Key;
            return knownMap.Key;
        }

        if (WorkshopNameCache.TryGetValue(workshopId, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var fetched = TryFetchWorkshopTitle(workshopId);
        if (!string.IsNullOrWhiteSpace(fetched))
        {
            WorkshopNameCache[workshopId] = fetched;
            return fetched;
        }

        return workshopId.ToString();
    }

    private string? TryFetchWorkshopTitle(uint workshopId)
    {
        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["itemcount"] = "1",
                ["publishedfileids[0]"] = workshopId.ToString()
            });
            using var response = WorkshopApiClient.PostAsync(WorkshopDetailsApiUrl, form).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var responseNode))
            {
                return null;
            }

            if (!responseNode.TryGetProperty("publishedfiledetails", out var detailsNode) ||
                detailsNode.ValueKind != JsonValueKind.Array ||
                detailsNode.GetArrayLength() == 0)
            {
                return null;
            }

            var first = detailsNode[0];
            if (!first.TryGetProperty("title", out var titleNode))
            {
                return null;
            }

            var title = titleNode.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(title) ? null : title;
        }
        catch (Exception ex)
        {
            Core.Logger.LogDebug("[CS2_Admin] Workshop title resolve failed for {WorkshopId}: {Message}", workshopId, ex.Message);
            return null;
        }
    }
}
