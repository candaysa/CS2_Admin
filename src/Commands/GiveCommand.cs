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

    public override void Execute(ICommandContext context)
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

        var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
        if (target == null)
        {
            Reply(context, "player_not_found");
            return;
        }

        var canTarget = PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, target.SteamID, allowSelf: false)
            .GetAwaiter().GetResult();
        if (!canTarget)
            return;

        var itemName = string.Join(" ", args.Skip(1));
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            Reply(context, "player_not_found");
            return;
        }

        target.PlayerPawn?.ItemServices?.GiveItem(itemName);

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        var targetName = target.Controller.PlayerName;

        BroadcastNotification(adminName, "give_notification", targetName, itemName);

        PlayerUtils.SendNotification(target, Messages,
            L("give_personal_html", itemName, ResolveVisibleAdminName(target, adminName)),
            $" \x02{L("prefix")}\x01 {L("give_personal_chat", itemName, ResolveVisibleAdminName(target, adminName))}");

        AdminLogManager.AddLogAsync("give", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"item={itemName}", targetName);
        Core.Logger.LogInformation("[CS2_Admin] {Admin} gave {ItemName} to {Target}", adminName, targetName, itemName);
    }
}
