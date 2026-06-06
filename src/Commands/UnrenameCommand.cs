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

    public override async void Execute(ICommandContext context)
    {
        try
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

            var canTarget = await PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, target.SteamID, allowSelf: true);
            if (!canTarget)
                return;

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var targetName = target.Controller.PlayerName;

            var originalName = await _playerNameHistoryManager.GetOriginalNameAsync(target.SteamID);
            if (string.IsNullOrWhiteSpace(originalName))
            {
                ReplyRaw(context, L("unrename_no_history"));
                return;
            }

            target.Controller.PlayerName = originalName;
            await _playerNameHistoryManager.DeleteCustomNameAsync(target.SteamID);

            Core.Scheduler.NextTick(() =>
            {
                BroadcastNotification(adminName, "unrename_notification", targetName, originalName);

                PlayerUtils.SendNotification(target, Messages,
                    $"<font color='#00ff00'><b>{L("unrename_personal_html")}</b></font><br><br>{L("label_original_name")}: <font color='#00ff00'>{originalName}</font><br>{L("label_by")}: <font color='#ffcc00'>{ResolveVisibleAdminName(target, adminName)}</font>",
                    $" \x02{L("prefix")}\x01 {L("unrename_personal_chat", originalName, ResolveVisibleAdminName(target, adminName))}");
            });

            _ = AdminLogManager.AddLogAsync("unrename", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"restored_name={originalName}", targetName);
            Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} restored name of {Target} to {OriginalName}", adminName, targetName, originalName);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Unrename command failed");
        }
    }
}
