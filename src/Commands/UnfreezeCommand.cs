using CS2_Admin.Database;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;

using CS2_Admin.Services;
using CS2_Admin.Config;
namespace CS2_Admin.Commands;

public class UnfreezeCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly HashSet<int> _frozenPlayers = new();
    private readonly HashSet<int> _freezeVisualPlayers = new();
    private readonly Dictionary<int, float> _freezeOriginalViewmodelFov = new();
    private readonly Dictionary<int, (float X, float Y, float Z)> _freezeOriginalViewmodelOffsets = new();

    public UnfreezeCommand(
        ISwiftlyCore core,
        AdminDbManager adminDbManager,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService) : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminDbManager = adminDbManager;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Unfreeze);

            if (!HasPerm(context, Permissions.Unfreeze))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "unfreeze_usage");
                return;
            }

            var targets = PlayerUtils.FindPlayersByTarget(Core, args[0], caller: context.Sender);
            if (targets.Count == 0)
            {
                Reply(context, "no_valid_targets");
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");

            foreach (var target in targets)
            {
                PlayerUtils.Unfreeze(target);
                var playerId = target.PlayerID;
                _frozenPlayers.Remove(playerId);
                _freezeVisualPlayers.Remove(playerId);
                RestoreFreezeVisuals(playerId);
            }

            foreach (var target in targets)
            {
                PlayerUtils.SendNotification(target, Messages,
                    $"<font color='#00ff00'><b>{L("unfrozen_personal_html")}</b></font><br><br>{L("label_by")}: <font color='#ffcc00'>{ResolveVisibleAdminName(target, adminName)}</font>",
                    $" \x02{L("prefix")}\x01 {L("unfrozen_personal_chat", ResolveVisibleAdminName(target, adminName))}");
            }

            if (targets.Count > 0)
            {
                BroadcastNotification(adminName, "unfreeze_notification", FormatTargetName(targets));
            }

            var unfreezeTargetSteamIds = string.Join(",", targets.Select(t => t.SteamID));
            _ = AdminLogManager.AddLogAsync("unfreeze", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={unfreezeTargetSteamIds};count={targets.Count}");
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Unfreeze command failed");
        }
    }

    private void RestoreFreezeVisuals(int playerId)
    {
        if (_freezeOriginalViewmodelFov.TryGetValue(playerId, out var originalFov))
        {
            var player = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.PlayerID == playerId);
            if (player?.PlayerPawn?.IsValid == true)
            {
                player.PlayerPawn.ViewmodelFOV = originalFov;
                player.PlayerPawn.ViewmodelFOVUpdated();
            }

            _freezeOriginalViewmodelFov.Remove(playerId);
        }

        if (_freezeOriginalViewmodelOffsets.TryGetValue(playerId, out var originalOffsets))
        {
            var player = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.PlayerID == playerId);
            if (player?.PlayerPawn?.IsValid == true)
            {
                player.PlayerPawn.ViewmodelOffsetX = originalOffsets.X;
                player.PlayerPawn.ViewmodelOffsetY = originalOffsets.Y;
                player.PlayerPawn.ViewmodelOffsetZ = originalOffsets.Z;
            }

            _freezeOriginalViewmodelOffsets.Remove(playerId);
        }
    }
}


