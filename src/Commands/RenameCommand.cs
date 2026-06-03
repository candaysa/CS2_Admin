using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class RenameCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly PlayerNameHistoryManager _playerNameHistoryManager;

    public RenameCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminDbManager adminDbManager,
        PlayerNameHistoryManager playerNameHistoryManager)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminDbManager = adminDbManager;
        _playerNameHistoryManager = playerNameHistoryManager;
    }

    public override void Execute(ICommandContext context)
    {
        var args = NormalizeArgs(context.Args, CommandsConfig.Rename);

        if (!HasPerm(context, Permissions.Rename))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 2)
        {
            Reply(context, "rename_usage");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
        if (target == null)
        {
            Reply(context, "player_not_found");
            return;
        }

        var canTarget = PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, target.SteamID, allowSelf: true)
            .GetAwaiter().GetResult();
        if (!canTarget)
            return;

        var newName = string.Join(" ", args.Skip(1));
        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        var targetName = target.Controller.PlayerName;

        target.Controller.PlayerName = newName;
        _playerNameHistoryManager.SetCustomNameAsync(target.SteamID, newName).GetAwaiter().GetResult();

        BroadcastNotification(adminName, "rename_notification", targetName, newName);

        PlayerUtils.SendNotification(target, Messages,
            L("rename_personal_html", newName, ResolveVisibleAdminName(target, adminName)),
            $" \x02{L("prefix")}\x01 {L("rename_personal_chat", newName, ResolveVisibleAdminName(target, adminName))}");

        AdminLogManager.AddLogAsync("rename", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"new_name={newName}", targetName);
        Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} renamed {Target} to {NewName}", adminName, targetName, newName);
    }
}
