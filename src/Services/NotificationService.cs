using CS2_Admin.Config;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Services;

public class NotificationService
{
    private readonly ISwiftlyCore _core;
    private readonly MessagesConfig _messagesConfig;
    private readonly TagsConfig _tags;
    private readonly PermissionsConfig _permissions;

    public NotificationService(ISwiftlyCore core, MessagesConfig messagesConfig, TagsConfig tags, PermissionsConfig permissions)
    {
        _core = core;
        _messagesConfig = messagesConfig;
        _tags = tags;
        _permissions = permissions;
    }

    public void SendToPlayer(IPlayer player, string htmlMessage, string chatMessage)
    {
        PlayerUtils.SendNotification(player, _messagesConfig, htmlMessage, chatMessage);
    }

    public void BroadcastToAll(string message)
    {
        foreach (var p in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            p.SendChat(message);
        }
    }

    public void BroadcastNotification(string adminName, string targetName, string durationText, string reason, Action<IPlayer, string> sendNotification)
    {
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            sendNotification(player, visibleAdmin);
        }
    }

    private string ResolveVisibleAdminName(IPlayer viewer, string adminName)
    {
        if (_tags.ShowAdminName)
            return adminName;

        var permService = new PermissionService(_core, _permissions);
        var isAdminViewer = permService.HasPermission(viewer, _permissions.AdminRoot)
            || (!string.IsNullOrWhiteSpace(_permissions.AdminMenu) && permService.HasPermission(viewer, _permissions.AdminMenu))
            || (!string.IsNullOrWhiteSpace(_permissions.ListPlayers) && permService.HasPermission(viewer, _permissions.ListPlayers));

        return isAdminViewer ? adminName : LocalizerHelper.Get(_core, "admin");
    }
}
