using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2_Admin.Commands;

public class GiveCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public GiveCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.Give);

            if (!HasPerm(context, Permissions.Give))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 2)
            {
                Reply(context, "give_usage");
                return;
            }

            var itemName = string.Join(" ", args.Skip(1));
            var isGroupTarget = PlayerUtils.IsGroupTarget(args[0]);
            var targets = PlayerUtils.FindPlayersByTarget(Core, args[0], caller: context.Sender);

            if (targets.Count == 0)
            {
                Reply(context, "player_not_found");
                return;
            }

            var filteredTargets = await PlayerUtils.FilterTargetsByAccessAsync(Core, _adminDbManager, context, targets, allowSelf: true);
            if (filteredTargets.Count == 0)
            {
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var givenCount = 0;

            Core.Scheduler.NextTick(() =>
            {
                foreach (var target in filteredTargets)
                {
                    var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamID);
                    if (liveTarget?.IsValid != true) continue;

                    var pawn = liveTarget.PlayerPawn;
                    if (pawn?.IsValid != true)
                        continue;

                    pawn.ItemServices?.GiveItem(itemName);

                    PlayerUtils.SendNotification(liveTarget, Messages,
                        $"<font color='#00ff00'><b>{L("give_personal_html")}</b></font><br><br>{L("label_item")}: <font color='#00ff00'>{itemName}</font><br>{L("label_by")}: <font color='#ffcc00'>{ResolveVisibleAdminName(liveTarget, adminName)}</font>",
                        $" \x02{L("prefix")}\x01 {L("give_personal_chat", itemName, ResolveVisibleAdminName(liveTarget, adminName))}");

                    _ = AdminLogManager.AddLogAsync("give", adminName, context.Sender?.SteamID ?? 0, liveTarget.SteamID, liveTarget.IPAddress, $"item={itemName}", liveTarget.Controller.PlayerName);
                    givenCount++;
                }

                if (givenCount == 0) return;

                BroadcastNotification(adminName, "give_notification", FormatTargetName(filteredTargets), itemName);

                Core.Logger.LogInformation("[CS2_Admin] {Admin} gave {ItemName} to {Count} player(s)", adminName, itemName, givenCount);
            });
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Give command failed");
        }
    }
}
