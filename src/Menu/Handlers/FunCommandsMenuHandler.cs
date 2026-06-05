using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;
using CS2_Admin.Config;
using CS2_Admin.Utils;

namespace CS2_Admin.Menu.Handlers;

public class FunCommandsMenuHandler : IAdminMenuHandler
{
    private readonly ISwiftlyCore _core;
    private readonly PluginConfig _config;

    private enum FunAction
    {
        Slap, God, Slay, Respawn, Team, Noclip, Goto, Bring, Freeze, Unfreeze, Resize, Blind, Glow, Beacon, Burn, Disarm, Speed, Gravity, Hp, Money, Give
    }

    public FunCommandsMenuHandler(ISwiftlyCore core, PluginConfig config)
    {
        _core = core;
        _config = config;
    }

    public IMenuAPI CreateMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();

        string title = T("menu_fun_commands");
        builder.Design.SetMenuTitle(title);

        if (HasPermission(player, _config.Permissions.Slap))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_slap"], () => BuildPlayerSelectMenu(player, FunAction.Slap)));

        if (HasPermission(player, _config.Permissions.God))
            builder.AddOption(new SubmenuMenuOption(T("menu_god", "Toggle God"), () => BuildPlayerSelectMenu(player, FunAction.God)));

        if (HasPermission(player, _config.Permissions.Slay))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_slay"], () => BuildPlayerSelectMenu(player, FunAction.Slay)));

        if (HasPermission(player, _config.Permissions.Respawn))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_respawn"], () => BuildPlayerSelectMenu(player, FunAction.Respawn)));

        if (HasPermission(player, _config.Permissions.ChangeTeam))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_team"], () => BuildPlayerSelectMenu(player, FunAction.Team)));

        if (HasPermission(player, _config.Permissions.NoClip))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_noclip"], () => BuildPlayerSelectMenu(player, FunAction.Noclip)));

        if (HasPermission(player, _config.Permissions.Goto))
            builder.AddOption(new SubmenuMenuOption(T("menu_goto"), () => BuildPlayerSelectMenu(player, FunAction.Goto)));

        if (HasPermission(player, _config.Permissions.Bring))
            builder.AddOption(new SubmenuMenuOption(T("menu_bring"), () => BuildPlayerSelectMenu(player, FunAction.Bring)));

        if (HasPermission(player, _config.Permissions.Freeze))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_freeze"], () => BuildPlayerSelectMenu(player, FunAction.Freeze)));

        if (HasPermission(player, _config.Permissions.Unfreeze))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_unfreeze"], () => BuildPlayerSelectMenu(player, FunAction.Unfreeze)));

        if (HasPermission(player, _config.Permissions.Resize))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_resize"], () => BuildPlayerSelectMenu(player, FunAction.Resize)));



        if (HasPermission(player, _config.Permissions.Blind))
            builder.AddOption(new SubmenuMenuOption(T("menu_blind", "Blind Player"), () => BuildPlayerSelectMenu(player, FunAction.Blind)));

        if (HasPermission(player, _config.Permissions.Glow))
            builder.AddOption(new SubmenuMenuOption(T("menu_glow", "Glow Player"), () => BuildPlayerSelectMenu(player, FunAction.Glow)));

        if (HasPermission(player, _config.Permissions.Beacon))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_beacon"], () => BuildPlayerSelectMenu(player, FunAction.Beacon)));

        if (HasPermission(player, _config.Permissions.Burn))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_burn"], () => BuildPlayerSelectMenu(player, FunAction.Burn)));

        if (HasPermission(player, _config.Permissions.Disarm))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_disarm"], () => BuildPlayerSelectMenu(player, FunAction.Disarm)));

        if (HasPermission(player, _config.Permissions.Speed))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_speed"], () => BuildPlayerSelectMenu(player, FunAction.Speed)));

        if (HasPermission(player, _config.Permissions.Gravity))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_gravity"], () => BuildPlayerSelectMenu(player, FunAction.Gravity)));

        if (HasPermission(player, _config.Permissions.Hp))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_hp"], () => BuildPlayerSelectMenu(player, FunAction.Hp)));

        if (HasPermission(player, _config.Permissions.Money))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_money"], () => BuildPlayerSelectMenu(player, FunAction.Money)));

        if (HasPermission(player, _config.Permissions.Give))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_give"], () => BuildPlayerSelectMenu(player, FunAction.Give)));

        return builder.Build();
    }

    private bool HasPermission(IPlayer player, string permission)
    {
        return _core.Permission.PlayerHasPermission(player.SteamID, permission)
               || _core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot);
    }

    private IMenuAPI BuildPlayerSelectMenu(IPlayer admin, FunAction action)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_player", "Select Player"));

        var players = _core.PlayerManager
            .GetAllPlayers()
            .Where(p => p.IsValid)
            .ToList();

        players = ApplyPlayerFilter(players, action);

        if (players.Count == 0)
        {
            var empty = new ButtonMenuOption(T("menu_no_players")) { CloseAfterClick = true };
            empty.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(empty);
            return builder.Build();
        }

        foreach (var target in players)
        {
            var name = target.Controller.PlayerName ?? PluginLocalizer.Get(_core)["player_fallback_name", target.PlayerID];

            if (action is FunAction.Team or FunAction.Slap or FunAction.Blind or FunAction.Glow or FunAction.Resize or FunAction.Speed or FunAction.Gravity or FunAction.Hp or FunAction.Money or FunAction.Give or FunAction.Beacon or FunAction.Freeze or FunAction.Burn)
            {
                var subMenuAction = new Func<IMenuAPI>(() => BuildActionSubMenu(admin, target, action));
                var btn = new SubmenuMenuOption(name, subMenuAction);
                builder.AddOption(btn);
            }
            else
            {
                var btn = new ButtonMenuOption(name) { CloseAfterClick = false };
                btn.Click += (_, args) =>
                {
                    ExecuteFunAction(args.Player, target, action);
                    return ValueTask.CompletedTask;
                };
                builder.AddOption(btn);
            }
        }

        return builder.Build();
    }

    private IMenuAPI BuildActionSubMenu(IPlayer admin, IPlayer target, FunAction action)
    {
        return action switch
        {
            FunAction.Team => BuildTeamSelectMenu(admin, target),
            FunAction.Slap => BuildSlapDamageMenu(admin, target),
            FunAction.Blind => BuildBlindDurationMenu(admin, target),
            FunAction.Glow => BuildGlowColorMenu(admin, target),
            FunAction.Resize => BuildValueMenu(admin, target, action, [0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2.0f, 2.2f, 2.4f, 2.6f, 2.8f, 3.0f]),
            FunAction.Speed => BuildValueMenu(admin, target, action, [0.50f, 0.80f, 1.00f, 1.20f, 1.50f, 2.00f]),
            FunAction.Gravity => BuildValueMenu(admin, target, action, [0.20f, 0.50f, 1.00f, 1.50f, 2.00f]),
            FunAction.Hp => BuildValueMenu(admin, target, action, [1f, 25f, 50f, 100f, 200f]),
            FunAction.Money => BuildValueMenu(admin, target, action, [0f, 800f, 16000f]),
            FunAction.Give => BuildGiveItemMenu(admin, target),

            FunAction.Beacon => BuildBeaconDurationMenu(admin, target),
            FunAction.Freeze or FunAction.Burn => BuildTimedActionDurationMenu(admin, target, action),
            _ => BuildTeamSelectMenu(admin, target) // fallback
        };
    }

    private IMenuAPI BuildTeamSelectMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_team", "Select Team"));

        int currentTeam = target.PlayerPawn?.IsValid == true ? target.PlayerPawn.TeamNum : 0;

        if (currentTeam != 2) AddTeamButton(builder, target, T("team_t"), "t");
        if (currentTeam != 3) AddTeamButton(builder, target, T("team_ct"), "ct");
        if (currentTeam != 1) AddTeamButton(builder, target, T("team_spec"), "spec");

        return builder.Build();
    }

    private void AddTeamButton(IMenuBuilderAPI builder, IPlayer target, string label, string teamArg)
    {
        var option = new ButtonMenuOption(label) { CloseAfterClick = false };
        option.Click += (_, args) =>
        {
            var caller = args.Player;
            var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.ChangeTeam, "team");
            _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {teamArg}"));
            return ValueTask.CompletedTask;
        };
        builder.AddOption(option);
    }

    private IMenuAPI BuildSlapDamageMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_duration", "Select Duration"));

        var damages = new[] { 0, 5, 10, 50, 90, 100 };
        foreach (var damage in damages)
        {
            var value = damage;
            var option = new ButtonMenuOption(value.ToString()) { CloseAfterClick = false };
            option.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Slap, "slap");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {value}"));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        return builder.Build();
    }

    private IMenuAPI BuildValueMenu(IPlayer admin, IPlayer target, FunAction action, IReadOnlyList<float> values)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_value", "Select Value"));

        foreach (var value in values)
        {
            var current = value;
            var option = new ButtonMenuOption(current.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)) { CloseAfterClick = false };
            option.Click += (_, args) =>
            {
                var caller = args.Player;
                var targetId = target.PlayerID;
                var command = action switch
                {
                    FunAction.Resize => CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Resize, "resize"),
                    FunAction.Speed => CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Speed, "speed"),
                    FunAction.Gravity => CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Gravity, "gravity"),
                    FunAction.Hp => CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Hp, "hp"),
                    FunAction.Money => CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Money, "money"),
                    _ => string.Empty
                };

                if (!string.IsNullOrWhiteSpace(command))
                {
                    if (action is FunAction.Hp or FunAction.Money)
                    {
                        _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{command} {targetId} {(int)current}"));
                    }
                    else
                    {
                        _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{command} {targetId} {current.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"));
                    }
                }
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        return builder.Build();
    }



    private IMenuAPI BuildBlindDurationMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_duration", "Select Duration"));

        var durations = new[] { 3, 5, 10, 20, 30, 45 };
        foreach (var duration in durations)
        {
            var current = duration;
            var option = new ButtonMenuOption($"{current}s") { CloseAfterClick = false };
            option.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Blind, "blind");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {current}"));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        return builder.Build();
    }

    private IMenuAPI BuildGlowColorMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_value", "Select Value"));

        var presets = new (string Label, string Args)[]
        {
            (T("glow_color_red"), "255 64 64 180"),
            (T("glow_color_green"), "64 255 64 180"),
            (T("glow_color_blue"), "64 160 255 180"),
            (T("glow_color_yellow"), "255 220 64 180"),
            (T("glow_color_purple"), "180 64 255 180"),
            (T("glow_color_cyan"), "64 255 255 180"),
            (T("glow_color_white"), "255 255 255 180"),
            (T("menu_off"), "off")
        };

        foreach (var preset in presets)
        {
            var current = preset;
            var option = new ButtonMenuOption(current.Label) { CloseAfterClick = false };
            option.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Glow, "glow");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {current.Args}"));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        return builder.Build();
    }

    private IMenuAPI BuildBeaconDurationMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_duration", "Select Duration"));

        var stop = new ButtonMenuOption(T("menu_off", "Off")) { CloseAfterClick = false };
        stop.Click += (_, args) =>
        {
            var caller = args.Player;
            var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Beacon, "beacon");
            _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} off"));
            return ValueTask.CompletedTask;
        };
        builder.AddOption(stop);

        var durations = new[] { 5, 10, 20, 40, 60, 120 };
        foreach (var duration in durations)
        {
            var current = duration;
            var option = new ButtonMenuOption($"{current}s") { CloseAfterClick = false };
            option.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Beacon, "beacon");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {current}"));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        return builder.Build();
    }

    private IMenuAPI BuildTimedActionDurationMenu(IPlayer admin, IPlayer target, FunAction action)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_duration", "Select Duration"));

        // Infinite option first.
        var infinite = new ButtonMenuOption(PluginLocalizer.Get(_core)["permanent"]) { CloseAfterClick = false };
        infinite.Click += (_, args) =>
        {
            var caller = args.Player;
            if (action == FunAction.Freeze)
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Freeze, "freeze");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID}"));
            }
            else
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Burn, "burn");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} -1 5"));
            }

            return ValueTask.CompletedTask;
        };
        builder.AddOption(infinite);

        for (var seconds = 5; seconds <= 60; seconds += 5)
        {
            var current = seconds;
            var option = new ButtonMenuOption($"{current}s") { CloseAfterClick = false };
            option.Click += (_, args) =>
            {
                var caller = args.Player;
                if (action == FunAction.Freeze)
                {
                    var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Freeze, "freeze");
                    _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {current}"));
                }
                else
                {
                    var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Burn, "burn");
                    _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {current} 5"));
                }

                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        return builder.Build();
    }

    private IMenuAPI BuildGiveItemMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_item", "Select Item"));

        var items = new (string Display, string Item)[]
        {
            ("AK-47", "weapon_ak47"),
            ("M4A1", "weapon_m4a1"),
            ("AWP", "weapon_awp"),
            ("Deagle", "weapon_deagle"),
            ("SSG 08", "weapon_ssg08"),
            ("Flash", "weapon_flashbang"),
            ("HE Grenade", "weapon_hegrenade"),
            ("Smoke", "weapon_smokegrenade"),
            ("Molotov", "weapon_molotov"),
            ("Defuse Kit", "item_defuser"),
            ("Assault Suit", "item_assaultsuit")
        };

        foreach (var (display, item) in items)
        {
            var current = item;
            var option = new ButtonMenuOption(display) { CloseAfterClick = false };
            option.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Give, "give");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {current}"));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        return builder.Build();
    }

    private string T(string key, string fallback, params object[] args)
    {
        try
        {
            var localizer = PluginLocalizer.Get(_core);
            var value = args.Length == 0 ? localizer[key] : localizer[key, args];
            return string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
                ? (args.Length == 0 ? fallback : string.Format(fallback, args))
                : value;
        }
        catch
        {
            return args.Length == 0 ? fallback : string.Format(fallback, args);
        }
    }

    private string T(string key, params object[] args) => T(key, key, args);

    private void ExecuteFunAction(IPlayer admin, IPlayer target, FunAction action)
    {
        var targetId = target.PlayerID;

        switch (action)
        {
            case FunAction.Slap:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Slap, "slap");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.God:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.God, "god");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Slay:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Slay, "slay");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Respawn:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Respawn, "respawn");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Noclip:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.NoClip, "noclip");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Goto:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Goto, "goto");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Bring:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Bring, "bring");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Freeze:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Freeze, "freeze");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Unfreeze:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Unfreeze, "unfreeze");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }

            case FunAction.Beacon:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Beacon, "beacon");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId} 20"));
                break;
            }
            case FunAction.Burn:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Burn, "burn");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId} 8 5"));
                break;
            }
            case FunAction.Disarm:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Disarm, "disarm");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
        }
    }

    private static bool IsAlive(IPlayer player)
    {
        return player.PlayerPawn?.IsValid == true && player.PlayerPawn.Health > 0;
    }

    private List<IPlayer> ApplyPlayerFilter(List<IPlayer> players, FunAction action)
    {
        return action switch
        {
            FunAction.Respawn => players.Where(p => !IsAlive(p)).ToList(),
            FunAction.Beacon => players,
            _ => players.Where(IsAlive).ToList()
        };
    }
}
