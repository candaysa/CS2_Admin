using CS2_Admin.Config;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Services;

public class PermissionService
{
    private readonly ISwiftlyCore _core;
    private readonly PermissionsConfig _permissions;

    public PermissionService(ISwiftlyCore core, PermissionsConfig permissions)
    {
        _core = core;
        _permissions = permissions;
    }

    public bool HasPermission(ICommandContext context, string permission)
    {
        if (!context.IsSentByPlayer)
            return true;

        return HasPermission(context.Sender!.SteamID, permission);
    }

    public bool HasPermission(IPlayer player, string permission)
    {
        if (player == null || !player.IsValid)
            return false;

        return HasPermission(player.SteamID, permission);
    }

    public bool HasPermission(ulong steamId, string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            return true;

        return _core.Permission.PlayerHasPermission(steamId, permission)
               || IsRoot(steamId);
    }

    public bool IsRoot(ulong steamId)
    {
        return _core.Permission.PlayerHasPermission(steamId, _permissions.AdminRoot)
               || _permissions.RootBypassPermissions.Any(p => _core.Permission.PlayerHasPermission(steamId, p));
    }

    public string ResolveVisibleAdminName(IPlayer viewer, string adminName, TagsConfig tags, PermissionsConfig permissions)
    {
        if (tags.ShowAdminName)
            return adminName;

        var isAdminViewer = HasPermission(viewer, permissions.AdminRoot)
            || (!string.IsNullOrWhiteSpace(permissions.AdminMenu) && HasPermission(viewer, permissions.AdminMenu))
            || (!string.IsNullOrWhiteSpace(permissions.ListPlayers) && HasPermission(viewer, permissions.ListPlayers));

        return isAdminViewer ? adminName : LocalizerHelper.Get(_core, "admin");
    }

    public static bool CheckPermission(ISwiftlyCore core, ICommandContext context, string permission)
    {
        if (!context.IsSentByPlayer)
            return true;

        return core.Permission.PlayerHasPermission(context.Sender!.SteamID, permission);
    }
}
