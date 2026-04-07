using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;

namespace CS2_Admin.Menu.Handlers;

public class AdminManagementHandler : IAdminMenuHandler
{
    private readonly ISwiftlyCore _core;
    private readonly PluginConfig _config;
    private readonly AdminDbManager _adminManager;
    private readonly GroupDbManager _groupManager;
    private readonly AdminLogManager _adminLogManager;

    public AdminManagementHandler(
        ISwiftlyCore core,
        PluginConfig config,
        AdminDbManager adminManager,
        GroupDbManager groupManager,
        AdminLogManager adminLogManager)
    {
        _core = core;
        _config = config;
        _adminManager = adminManager;
        _groupManager = groupManager;
        _adminLogManager = adminLogManager;
    }

    public IMenuAPI CreateMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        var title = T("menu_admin_management");
        builder.Design.SetMenuTitle(title);

        if (!IsRoot(player))
        {
            var noPermissionButton = new ButtonMenuOption(T("no_permission")) { CloseAfterClick = true };
            noPermissionButton.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(noPermissionButton);
            return builder.Build();
        }

        var addAdminText = T("menu_add_admin");
        builder.AddOption(new SubmenuMenuOption(addAdminText, () => BuildAddAdminMenu(player)));

        var removeAdminText = T("menu_remove_admin");
        builder.AddOption(new SubmenuMenuOption(removeAdminText, () => BuildRemoveAdminMenu(player)));

        var listAdminsText = T("menu_list_admins");
        builder.AddOption(new SubmenuMenuOption(listAdminsText, () => BuildListAdminsMenu(player)));

        return builder.Build();
    }

    private bool IsRoot(IPlayer player) => _core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot);

    private IMenuAPI BuildAddAdminMenu(IPlayer admin)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        var title = T("menu_select_player_add");
        builder.Design.SetMenuTitle(title);

        var players = _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid).ToList();
        foreach (var target in players)
        {
            var fallbackName = T("player_fallback_name", target.PlayerID);
            var btn = new ButtonMenuOption(target.Controller.PlayerName ?? fallbackName) { CloseAfterClick = false };
            btn.Click += (_, args) =>
            {
                var adminPlayer = args.Player;
                _core.Scheduler.NextTick(() => OpenAddAdminGroupMenu(adminPlayer, target));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(btn);
        }

        return builder.Build();
    }

    private void OpenAddAdminGroupMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_group"));

        var groups = _groupManager.GetAllGroupsAsync().GetAwaiter().GetResult();
        foreach (var group in groups)
        {
            var groupBtn = new ButtonMenuOption(T("menu_group_with_immunity", group.Name, group.Immunity)) { CloseAfterClick = true };
            groupBtn.Click += (_, args) =>
            {
                var adminPlayer = args.Player;
                _core.Scheduler.NextTick(() => ExecuteAddAdminWithGroup(adminPlayer, target, group.Name));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(groupBtn);
        }

        if (groups.Count == 0)
        {
            var empty = new ButtonMenuOption(T("menu_no_groups")) { CloseAfterClick = true };
            empty.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(empty);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private IMenuAPI BuildRemoveAdminMenu(IPlayer admin)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        var title = T("menu_select_admin_remove");
        builder.Design.SetMenuTitle(title);

        var players = _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid).ToList();
        foreach (var target in players)
        {
            var fallbackName = T("player_fallback_name", target.PlayerID);
            var btn = new ButtonMenuOption(target.Controller.PlayerName ?? fallbackName) { CloseAfterClick = true };
            btn.Click += (_, args) =>
            {
                var adminPlayer = args.Player;
                _core.Scheduler.NextTick(() => ExecuteRemoveAdmin(adminPlayer, target));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(btn);
        }

        return builder.Build();
    }

    private void ExecuteAddAdminWithGroup(IPlayer admin, IPlayer target, string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return;

        if (!IsRoot(admin))
        {
            admin.SendChat($" \x02{T("prefix")}\x01 {T("no_permission")}");
            return;
        }

        var targetName = target.Controller.PlayerName ?? T("player_fallback_name", target.PlayerID);
        var adminName = admin.Controller.PlayerName ?? T("console_name");
        var targetSteamId = target.SteamID;
        var adminSteamId = admin.SteamID;

        _ = Task.Run(async () =>
        {
            var success = await _adminManager.AddAdminAsync(
                targetSteamId,
                targetName,
                string.Empty,
                0,
                groupName,
                adminName,
                adminSteamId,
                null);

            if (!success)
            {
                _core.Scheduler.NextTick(() => admin.SendChat($" \x02{T("prefix")}\x01 {T("addadmin_failed")}"));
                return;
            }

            var effectiveFlags = await _adminManager.GetEffectiveFlagsAsync(targetSteamId);
            _core.Scheduler.NextTick(() =>
            {
                admin.SendChat($" \x02{T("prefix")}\x01 {T("addadmin_success", targetName, targetSteamId, string.Join(",", effectiveFlags))}");
                target.SendChat($" \x02{T("prefix")}\x01 {T("addadmin_granted")}");
                if (_config.Tags.Enabled)
                {
                    PlayerUtils.SetScoreTagReliable(_core, target.PlayerID, groupName);
                }

                var reloadCmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.AdminReload, "adminreload");
                admin.ExecuteCommand(reloadCmd);
            });

            await _adminLogManager.AddLogAsync(
                "addadmin",
                adminName,
                adminSteamId,
                targetSteamId,
                target.IPAddress,
                $"groups={groupName};immunity=0;source=menu",
                targetName);
        });
    }

    private void ExecuteRemoveAdmin(IPlayer admin, IPlayer target)
    {
        if (!IsRoot(admin))
        {
            admin.SendChat($" \x02{T("prefix")}\x01 {T("no_permission")}");
            return;
        }

        var steamId = target.SteamID;
        var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.RemoveAdmin, "removeadmin");
        _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {steamId}"));
    }

    private void ExecuteListAdmins(IPlayer admin)
    {
        if (!IsRoot(admin))
        {
            admin.SendChat($" \x02{T("prefix")}\x01 {T("no_permission")}");
            return;
        }

        var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.ListAdmins, "listadmins");
        _core.Scheduler.NextTick(() => admin.ExecuteCommand(cmd));
    }

    private IMenuAPI BuildListAdminsMenu(IPlayer admin)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_list_admins"));

        var admins = _adminManager.GetAllAdminsAsync().GetAwaiter().GetResult();
        if (admins.Count == 0)
        {
            var empty = new ButtonMenuOption(T("menu_no_admins")) { CloseAfterClick = true };
            empty.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(empty);
            return builder.Build();
        }

        foreach (var item in admins)
        {
            var title = T("menu_admin_list_entry", item.Name, item.Id, item.SteamId);
            var detail = new ButtonMenuOption(title) { CloseAfterClick = false };
            detail.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(detail);
        }

        return builder.Build();
    }

    private string T(string key, params object[] args)
    {
        try
        {
            var localizer = PluginLocalizer.Get(_core);
            return args.Length == 0 ? localizer[key] : localizer[key, args];
        }
        catch
        {
            return key;
        }
    }
}


