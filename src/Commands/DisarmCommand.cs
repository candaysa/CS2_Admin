using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;
using System.Drawing;

namespace CS2_Admin.Commands;

public class DisarmCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public DisarmCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.Disarm);

            if (!HasPerm(context, Permissions.Disarm))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "disarm_usage");
                return;
            }

            var targets = PlayerUtils.FindPlayersByTarget(Core, args[0], includeDeadPlayers: true, caller: context.Sender);
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

            Core.Scheduler.NextTick(() =>
            {
                var changed = 0;
                foreach (var target in targets)
                {
                    var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamID);
                    if (liveTarget?.IsValid != true) continue;
                    
                    var itemServices = liveTarget.PlayerPawn?.ItemServices;
                    if (itemServices?.IsValid == true)
                    {
                        itemServices.RemoveItems();
                        changed++;
                    }
                }

                if (changed == 0) return;

                var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
                foreach (var disTarget in targets)
                {
                    var liveDis = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == disTarget.SteamID);
                    if (liveDis?.IsValid != true) continue;
                    PlayerUtils.SendNotification(liveDis, Messages,
                        $"<font color='#c0392b'><b>{L("disarm_personal_html")}</b></font><br><br>{L("label_by")}: <font color='#ffd700'>{ResolveVisibleAdminName(liveDis, adminName)}</font>",
                        $" \x02{L("prefix")}\x01 {L("disarm_personal_chat", ResolveVisibleAdminName(liveDis, adminName))}");
                }

                if (changed > 0)
                    BroadcastNotification(adminName, "disarm_notification", FormatTargetName(targets));

                _ = AdminLogManager.AddLogAsync("disarm", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={changed}");
                Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} disarmed {Count} player(s)", adminName, changed);
            });
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Disarm command failed");
        }
    }
}

