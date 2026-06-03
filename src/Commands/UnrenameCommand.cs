using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class UnrenameCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly PlayerNameHistoryManager _playerNameHistoryManager;

    public UnrenameCommand(
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
        var args = NormalizeArgs(context.Args, CommandsConfig.Unrename);

        if (!HasPerm(context, Permissions.Unrename))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 1)
        {
            Reply(context, "unrename_usage");
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

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        var targetName = target.Controller.PlayerName;

        var originalName = _playerNameHistoryManager.GetOriginalNameAsync(target.SteamID).GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(originalName))
        {
            ReplyRaw(context, L("unrename_no_history"));
            return;
        }

        target.Controller.PlayerName = originalName;
        _playerNameHistoryManager.DeleteCustomNameAsync(target.SteamID).GetAwaiter().GetResult();

        BroadcastNotification(adminName, "unrename_notification", targetName, originalName);

        PlayerUtils.SendNotification(target, Messages,
            L("unrename_personal_html", originalName, ResolveVisibleAdminName(target, adminName)),
            $" \x02{L("prefix")}\x01 {L("unrename_personal_chat", originalName, ResolveVisibleAdminName(target, adminName))}");

        AdminLogManager.AddLogAsync("unrename", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"restored_name={originalName}", targetName);
        Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} restored name of {Target} to {OriginalName}", adminName, targetName, originalName);
    }
}
