using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class GodCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly HashSet<int> _godPlayers = new();

    public GodCommand(
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

    public override void Execute(ICommandContext context)
    {
        var args = NormalizeArgs(context.Args, CommandsConfig.God);

        if (!HasPerm(context, Permissions.God))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 1)
        {
            Reply(context, "god_usage");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(Core, args[0], includeDeadPlayers: true, caller: context.Sender);
        if (targets.Count == 0)
        {
            Reply(context, "no_valid_targets");
            return;
        }

        targets = PlayerUtils.FilterTargetsByAccessAsync(Core, _adminDbManager, context, targets, allowSelf: true)
            .GetAwaiter().GetResult();
        if (targets.Count == 0)
        {
            Reply(context, "no_valid_targets");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");

        foreach (var target in targets)
        {
            var pawn = target.PlayerPawn;
            if (pawn?.IsValid != true) continue;

            var isGod = _godPlayers.Contains(target.PlayerID);
            if (isGod)
            {
                pawn.TakesDamage = true;
                pawn.TakesDamageUpdated();
                _godPlayers.Remove(target.PlayerID);
            }
            else
            {
                pawn.TakesDamage = false;
                pawn.TakesDamageUpdated();
                _godPlayers.Add(target.PlayerID);
            }

            var stateLabel = !isGod ? L("god_enabled") : L("god_disabled");
            var targetName = target.Controller.PlayerName;

            PlayerUtils.SendNotification(target, Messages,
                L("god_personal_html", stateLabel, ResolveVisibleAdminName(target, adminName)),
                $" \x02{L("prefix")}\x01 {L("god_personal_chat", stateLabel, ResolveVisibleAdminName(target, adminName))}");
        }

        foreach (var target in targets)
        {
            var stateLabel = _godPlayers.Contains(target.PlayerID) ? L("god_enabled") : L("god_disabled");
            var targetName = target.Controller.PlayerName;
            foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                player.SendChat($" \x02{L("prefix")}\x01 {L("god_notification", visibleAdmin, targetName, stateLabel)}");
            }
        }

        foreach (var target in targets)
        {
            var stateLabel = _godPlayers.Contains(target.PlayerID) ? L("god_enabled") : L("god_disabled");
            _ = AdminLogManager.AddLogAsync("god", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"enabled={stateLabel == L("god_enabled")}", target.Controller.PlayerName);
        }
    }
}

