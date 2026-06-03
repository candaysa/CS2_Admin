using CS2_Admin.Database;
using CS2_Admin.Config;
using CS2_Admin.Models;
using CS2_Admin.Services;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;

namespace CS2_Admin.Commands;

public class AdminTimeCommand : CommandBase
{
    private readonly AdminPlaytimeDbManager _adminPlaytimeDbManager;
    private readonly AdminPlaytimeConfig _adminPlaytimeConfig;

    public AdminTimeCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminPlaytimeDbManager adminPlaytimeDbManager,
        AdminPlaytimeConfig adminPlaytimeConfig)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminPlaytimeDbManager = adminPlaytimeDbManager;
        _adminPlaytimeConfig = adminPlaytimeConfig;
    }

    public override void Execute(ICommandContext context)
    {
        if (!HasPerm(context, Permissions.AdminTime))
        {
            Reply(context, "no_permission");
            return;
        }

        _ = Task.Run(async () =>
        {
            var topAdmins = await _adminPlaytimeDbManager.GetTopAdminsAsync(_adminPlaytimeConfig.MenuTopLimit);

            Core.Scheduler.NextTick(() =>
            {
                if (context.IsSentByPlayer && context.Sender != null)
                {
                    var viewer = context.Sender;
                    Core.Scheduler.DelayBySeconds(0.1f, () =>
                    {
                        if (viewer.IsValid)
                        {
                            OpenPlaytimeMenu(viewer, topAdmins);
                        }
                    });
                    return;
                }

                if (topAdmins.Count == 0)
                {
                    Reply(context, "admintime_no_data");
                    return;
                }

                ReplyRaw(context, L("admintime_console_header"));
                for (var i = 0; i < topAdmins.Count; i++)
                {
                    var entry = topAdmins[i];
                    ReplyRaw(context, L("admintime_console_entry", i + 1, entry.PlayerName, entry.SteamId, entry.PlaytimeMinutes));
                }
            });
        });
    }

    private void OpenPlaytimeMenu(IPlayer player, IReadOnlyList<AdminPlaytime> entries)
    {
        var builder = Core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(L("menu_admin_playtime"));

        if (entries.Count == 0)
        {
            var emptyButton = new ButtonMenuOption(L("admintime_no_data")) { CloseAfterClick = true };
            emptyButton.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(emptyButton);
        }
        else
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var row = entries[i];
                var text = L("admintime_menu_entry", i + 1, row.PlayerName, row.PlaytimeMinutes);
                var button = new ButtonMenuOption(text) { CloseAfterClick = false };
                button.Click += (_, _) => ValueTask.CompletedTask;
                builder.AddOption(button);
            }
        }

        Core.MenusAPI.OpenMenuForPlayer(player, builder.Build());
    }
}
