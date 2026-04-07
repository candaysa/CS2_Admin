using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;
using CS2_Admin.Config;
using CS2_Admin.Utils;

namespace CS2_Admin.Menu.Handlers;

public class ServerManagementHandler : IAdminMenuHandler
{
    private readonly ISwiftlyCore _core;
    private readonly PluginConfig _config;

    public ServerManagementHandler(ISwiftlyCore core, PluginConfig config)
    {
        _core = core;
        _config = config;
    }

    public IMenuAPI CreateMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        var title = T("menu_server_management");
        builder.Design.SetMenuTitle(title);

        // Restart game
        if (HasPermission(player, _config.Permissions.RestartGame))
        {
            var restartText = T("menu_restart_game");
            builder.AddOption(new SubmenuMenuOption(restartText, () => CreateRestartGameMenu(player)));
        }

        // Change map
        if (HasPermission(player, _config.Permissions.ChangeMap))
        {
            var mapText = T("menu_change_map");
            builder.AddOption(new SubmenuMenuOption(mapText, () => CreateChangeMapMenu(player)));
        }

        // Change workshop map
        if (HasPermission(player, _config.Permissions.ChangeWSMap))
        {
            var wsMapText = T("menu_change_ws_map");
            builder.AddOption(new SubmenuMenuOption(wsMapText, () => CreateChangeWSMapMenu(player)));
        }

        if (HasPermission(player, _config.Permissions.HeadshotMode))
        {
            var hsOnText = T("menu_headshot_on");
            var hsOn = new ButtonMenuOption(hsOnText) { CloseAfterClick = true };
            hsOn.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.HeadshotOn, "hson");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(hsOn);

            var hsOffText = T("menu_headshot_off");
            var hsOff = new ButtonMenuOption(hsOffText) { CloseAfterClick = true };
            hsOff.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.HeadshotOff, "hsoff");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(hsOff);
        }

        if (HasPermission(player, _config.Permissions.BunnyHop))
        {
            var bunnyOnText = T("menu_bunny_on");
            var bunnyOn = new ButtonMenuOption(bunnyOnText) { CloseAfterClick = true };
            bunnyOn.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.BunnyOn, "bunnyon");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(bunnyOn);

            var bunnyOffText = T("menu_bunny_off");
            var bunnyOff = new ButtonMenuOption(bunnyOffText) { CloseAfterClick = true };
            bunnyOff.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.BunnyOff, "bunnyoff");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(bunnyOff);
        }

        if (HasPermission(player, _config.Permissions.RespawnMode))
        {
            var respawnOnText = T("menu_respawn_on");
            var respawnOn = new ButtonMenuOption(respawnOnText) { CloseAfterClick = true };
            respawnOn.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.RespawnOn, "respawnon");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(respawnOn);

            var respawnOffText = T("menu_respawn_off");
            var respawnOff = new ButtonMenuOption(respawnOffText) { CloseAfterClick = true };
            respawnOff.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.RespawnOff, "respawnoff");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(respawnOff);
        }

        return builder.Build();
    }

    private IMenuAPI CreateRestartGameMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        var title = T("menu_restart_game");
        builder.Design.SetMenuTitle(title);

        // Select seconds (1-10)
        var delayText = T("menu_restart_delay");
        var slider = new SliderMenuOption(delayText, 1, 10, 2, 1);
        builder.AddOption(slider);

        var nowText = T("menu_restart_now");
        var btn = new ButtonMenuOption(nowText) { CloseAfterClick = true };
        btn.Click += (_, args) =>
        {
            var caller = args.Player;
            var seconds = (int)slider.GetValue(caller);
            var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.RestartGame, "rr");
            _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {seconds}"));
            return ValueTask.CompletedTask;
        };
        builder.AddOption(btn);

        return builder.Build();
    }

    private IMenuAPI CreateChangeMapMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        var title = T("menu_change_map");
        builder.Design.SetMenuTitle(title);

        if (_config.GameMaps.Maps != null)
        {
            foreach (var map in _config.GameMaps.Maps)
            {
                var btn = new ButtonMenuOption(map.Value) { CloseAfterClick = true };
                btn.Click += (_, args) =>
                {
                    var caller = args.Player;
                    var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.ChangeMap, "map");
                    _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {map.Key}"));
                    return ValueTask.CompletedTask;
                };
                builder.AddOption(btn);
            }
        }

        return builder.Build();
    }

    private IMenuAPI CreateChangeWSMapMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        var title = T("menu_change_ws_map");
        builder.Design.SetMenuTitle(title);

        if (_config.WorkshopMaps.Maps != null)
        {
            foreach (var map in _config.WorkshopMaps.Maps)
            {
                var displayName = map.Key;
                var workshopId = map.Value;

                var btn = new ButtonMenuOption(displayName) { CloseAfterClick = true };
                btn.Click += (_, args) =>
                {
                    var caller = args.Player;
                    var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.ChangeWSMap, "wsmap");
                    _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {workshopId}"));
                    return ValueTask.CompletedTask;
                };
                builder.AddOption(btn);
            }
        }

        return builder.Build();
    }

    private bool HasPermission(IPlayer player, string permission)
    {
        return _core.Permission.PlayerHasPermission(player.SteamID, permission)
               || _core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot);
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


