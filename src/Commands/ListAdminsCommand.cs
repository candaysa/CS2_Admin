using CS2_Admin.Database;
using CS2_Admin.Config;
using CS2_Admin.Services;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class ListAdminsCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public ListAdminsCommand(
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
        if (!HasPerm(context, Permissions.ListAdmins))
        {
            Reply(context, "no_permission");
            return;
        }

        _ = Task.Run(async () =>
        {
            var admins = await _adminDbManager.GetAllAdminsAsync();
            Core.Scheduler.NextTick(() =>
            {
                if (admins.Count == 0)
                {
                    Reply(context, "listadmins_none");
                    return;
                }

                Reply(context, "listadmins_header", admins.Count);
                foreach (var admin in admins)
                {
                    var expiry = admin.IsPermanent
                        ? L("permanent")
                        : L("admin_expires", admin.ExpiresAt?.ToString("yyyy-MM-dd") ?? L("unknown"));
                    ReplyRaw(context, L("listadmins_entry", admin.Name, admin.SteamId, admin.Groups, admin.Immunity, expiry));
                }
            });
        });
    }
}
