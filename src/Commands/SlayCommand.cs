using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class SlayCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public SlayCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminDbManager adminDbManager)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminDbManager = adminDbManager;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Slay);

            if (!HasPerm(context, Permissions.Slay))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "slay_usage");
                return;
            }

            var targets = PlayerUtils.FindPlayersByTarget(Core, args[0], includeDeadPlayers: false, caller: context.Sender);
            if (targets.Count == 0)
            {
                Reply(context, "no_valid_targets");
                return;
            }

            targets = await PlayerUtils.FilterTargetsByAccessAsync(Core, _adminDbManager, context, targets, allowSelf: true);
            if (targets.Count == 0)
            {
                Reply(context, "no_valid_targets");
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var prefix = L("prefix");

            foreach (var target in targets)
            {
                if (target.PlayerPawn?.IsValid == true)
                {
                    target.PlayerPawn.CommitSuicide(false, true);
                }
            }

            foreach (var target in targets)
            {
                PlayerUtils.SendNotification(target, Messages,
                    $"<font color='#ff0000'><b>{L("slayed_personal_html")}</b></font><br><br>{L("label_by")}: <font color='#ffcc00'>{ResolveVisibleAdminName(target, adminName)}</font>",
                    $" \x02{prefix}\x01 {L("slayed_personal_chat", ResolveVisibleAdminName(target, adminName))}");
            }

            if (targets.Count == 1)
            {
                var targetName = targets[0].Controller.PlayerName;
                foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                {
                    var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                    player.SendChat($" \x02{prefix}\x01 {L("slayed_notification_single", visibleAdmin, targetName)}");
                }
            }
            else
            {
                foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                {
                    var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                    player.SendChat($" \x02{prefix}\x01 {L("slayed_notification_multiple", visibleAdmin, targets.Count)}");
                }
            }

            var targetSteamIds = string.Join(",", targets.Select(t => t.SteamID));
            _ = AdminLogManager.AddLogAsync("slay", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targetSteamIds};count={targets.Count}");
            Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} slayed {Count} player(s)", adminName, targets.Count);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Slay command failed");
        }
    }
}

