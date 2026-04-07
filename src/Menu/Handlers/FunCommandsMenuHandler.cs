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
        Slap,
        Slay,
        Respawn,
        Team,
        Noclip,
        Goto,
        Bring,
        Freeze,
        Unfreeze,
        Resize,
        Drug,
        Beacon,
        Burn,
        Disarm,
        Speed,
        Gravity,
        Hp,
        Money,
        Give
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

        if (HasPermission(player, _config.Permissions.Drug))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_drug"], () => BuildPlayerSelectMenu(player, FunAction.Drug)));

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
        builder.Design.SetMenuTitle(T("menu_select_player"));

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
            var btn = new ButtonMenuOption(target.Controller.PlayerName ?? PluginLocalizer.Get(_core)["player_fallback_name", target.PlayerID])
            {
                CloseAfterClick = false
            };
            btn.Click += (_, args) =>
            {
                var adminPlayer = args.Player;
                _core.Scheduler.NextTick(() =>
                {
                    if (action == FunAction.Team)
                    {
                        OpenTeamSelectMenu(adminPlayer, target);
                    }
                    else if (action == FunAction.Slap)
                    {
                        OpenSlapDamageMenu(adminPlayer, target);
                    }
                    else if (action == FunAction.Resize)
                    {
                        OpenValueMenu(adminPlayer, target, action, [0.50f, 0.80f, 1.00f, 1.20f, 1.50f, 2.00f]);
                    }
                    else if (action == FunAction.Speed)
                    {
                        OpenValueMenu(adminPlayer, target, action, [0.50f, 0.80f, 1.00f, 1.20f, 1.50f, 2.00f]);
                    }
                    else if (action == FunAction.Gravity)
                    {
                        OpenValueMenu(adminPlayer, target, action, [0.20f, 0.50f, 1.00f, 1.50f, 2.00f]);
                    }
                    else if (action == FunAction.Hp)
                    {
                        OpenValueMenu(adminPlayer, target, action, [1f, 25f, 50f, 100f, 200f]);
                    }
                    else if (action == FunAction.Money)
                    {
                        OpenValueMenu(adminPlayer, target, action, [0f, 800f, 16000f]);
                    }
                    else if (action == FunAction.Give)
                    {
                        OpenGiveItemMenu(adminPlayer, target);
                    }
                    else if (action == FunAction.Drug)
                    {
                        OpenDrugDurationMenu(adminPlayer, target);
                    }
                    else if (action == FunAction.Beacon)
                    {
                        OpenBeaconDurationMenu(adminPlayer, target);
                    }
                    else if (action == FunAction.Freeze || action == FunAction.Burn)
                    {
                        OpenTimedActionDurationMenu(adminPlayer, target, action);
                    }
                    else
                    {
                        ExecuteFunAction(adminPlayer, target, action);
                    }
                });
                return ValueTask.CompletedTask;
            };
            builder.AddOption(btn);
        }

        return builder.Build();
    }

    private void OpenTeamSelectMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_team"));

        AddTeamButton(builder, admin, target, T("team_t"), "t");
        AddTeamButton(builder, admin, target, T("team_ct"), "ct");
        AddTeamButton(builder, admin, target, T("team_spec"), "spec");

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private void AddTeamButton(IMenuBuilderAPI builder, IPlayer admin, IPlayer target, string label, string teamArg)
    {
        var option = new ButtonMenuOption(label) { CloseAfterClick = false };
        option.Click += (_, args) =>
        {
            var caller = args.Player;
            var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.ChangeTeam, "team");
            var targetId = target.PlayerID;
            ExecuteAndReopenPlayerSelect(caller, FunAction.Team, $"{cmd} {targetId} {teamArg}");
            return ValueTask.CompletedTask;
        };
        builder.AddOption(option);
    }

    private void OpenSlapDamageMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_duration"));

        var damages = new[] { 0, 5, 10, 50, 90, 100 };
        foreach (var damage in damages)
        {
            var value = damage;
            var option = new ButtonMenuOption(value.ToString()) { CloseAfterClick = false };
            option.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Slap, "slap");
                var targetId = target.PlayerID;
                ExecuteAndReopenSameMenu(caller, $"{cmd} {targetId} {value}", p => OpenSlapDamageMenu(p, target));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private void OpenValueMenu(IPlayer admin, IPlayer target, FunAction action, IReadOnlyList<float> values)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_value"));

        foreach (var value in values)
        {
            var current = value;
            var option = new ButtonMenuOption(current.ToString("0.##")) { CloseAfterClick = false };
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

                if (string.IsNullOrWhiteSpace(command))
                {
                    return ValueTask.CompletedTask;
                }

                if (action is FunAction.Hp or FunAction.Money)
                {
                    ExecuteAndReopenSameMenu(caller, $"{command} {targetId} {(int)current}", p => OpenValueMenu(p, target, action, values));
                }
                else
                {
                    ExecuteAndReopenSameMenu(caller, $"{command} {targetId} {current.ToString("0.##")}", p => OpenValueMenu(p, target, action, values));
                }

                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private void OpenDrugDurationMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_duration"));

        var durations = new[] { 5, 10, 20, 40, 60 };
        foreach (var duration in durations)
        {
            var current = duration;
            var option = new ButtonMenuOption($"{current}s") { CloseAfterClick = false };
            option.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Drug, "drug");
                ExecuteAndReopenSameMenu(caller, $"{cmd} {target.PlayerID} {current}", p => OpenDrugDurationMenu(p, target));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private void OpenBeaconDurationMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_duration"));

        var stop = new ButtonMenuOption(T("menu_off")) { CloseAfterClick = false };
        stop.Click += (_, args) =>
        {
            var caller = args.Player;
            var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Beacon, "beacon");
            ExecuteAndReopenSameMenu(caller, $"{cmd} {target.PlayerID} off", p => OpenBeaconDurationMenu(p, target));
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
                ExecuteAndReopenSameMenu(caller, $"{cmd} {target.PlayerID} {current}", p => OpenBeaconDurationMenu(p, target));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private void OpenTimedActionDurationMenu(IPlayer admin, IPlayer target, FunAction action)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_duration"));

        // Infinite option first.
        var infinite = new ButtonMenuOption(PluginLocalizer.Get(_core)["duration_permanent"]) { CloseAfterClick = false };
        infinite.Click += (_, args) =>
        {
            var caller = args.Player;
            if (action == FunAction.Freeze)
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Freeze, "freeze");
                ExecuteAndReopenSameMenu(caller, $"{cmd} {target.PlayerID}", p => OpenTimedActionDurationMenu(p, target, action));
            }
            else
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Burn, "burn");
                ExecuteAndReopenSameMenu(caller, $"{cmd} {target.PlayerID} -1 5", p => OpenTimedActionDurationMenu(p, target, action));
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
                    ExecuteAndReopenSameMenu(caller, $"{cmd} {target.PlayerID} {current}", p => OpenTimedActionDurationMenu(p, target, action));
                }
                else
                {
                    var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Burn, "burn");
                    ExecuteAndReopenSameMenu(caller, $"{cmd} {target.PlayerID} {current} 5", p => OpenTimedActionDurationMenu(p, target, action));
                }

                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private void OpenGiveItemMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_item"));

        var items = new[]
        {
            "weapon_ak47",
            "weapon_m4a1",
            "weapon_awp",
            "weapon_deagle",
            "weapon_hegrenade",
            "item_assaultsuit"
        };

        foreach (var item in items)
        {
            var current = item;
            var option = new ButtonMenuOption(current) { CloseAfterClick = false };
            option.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Give, "give");
                ExecuteAndReopenPlayerSelect(caller, FunAction.Give, $"{cmd} {target.PlayerID} {current}");
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
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

    private void ExecuteFunAction(IPlayer admin, IPlayer target, FunAction action)
    {
        var targetId = target.PlayerID;

        switch (action)
        {
            case FunAction.Slap:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Slap, "slap");
                ExecuteAndReopenPlayerSelect(admin, action, $"{cmd} {targetId}");
                break;
            }
            case FunAction.Slay:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Slay, "slay");
                ExecuteAndReopenPlayerSelect(admin, action, $"{cmd} {targetId}");
                break;
            }
            case FunAction.Respawn:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Respawn, "respawn");
                ExecuteAndReopenPlayerSelect(admin, action, $"{cmd} {targetId}");
                break;
            }
            case FunAction.Noclip:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.NoClip, "noclip");
                ExecuteAndReopenPlayerSelect(admin, action, $"{cmd} {targetId}");
                break;
            }
            case FunAction.Goto:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Goto, "goto");
                ExecuteAndReopenPlayerSelect(admin, action, $"{cmd} {targetId}");
                break;
            }
            case FunAction.Bring:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Bring, "bring");
                ExecuteAndReopenPlayerSelect(admin, action, $"{cmd} {targetId}");
                break;
            }
            case FunAction.Freeze:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Freeze, "freeze");
                ExecuteAndReopenPlayerSelect(admin, action, $"{cmd} {targetId}");
                break;
            }
            case FunAction.Unfreeze:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Unfreeze, "unfreeze");
                ExecuteAndReopenPlayerSelect(admin, action, $"{cmd} {targetId}");
                break;
            }
            case FunAction.Drug:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Drug, "drug");
                ExecuteAndReopenPlayerSelect(admin, action, $"{cmd} {targetId} 5");
                break;
            }
            case FunAction.Beacon:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Beacon, "beacon");
                ExecuteAndReopenPlayerSelect(admin, action, $"{cmd} {targetId} 20");
                break;
            }
            case FunAction.Burn:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Burn, "burn");
                ExecuteAndReopenPlayerSelect(admin, action, $"{cmd} {targetId} 8 5");
                break;
            }
            case FunAction.Disarm:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Disarm, "disarm");
                ExecuteAndReopenPlayerSelect(admin, action, $"{cmd} {targetId}");
                break;
            }
        }
    }

    private void ExecuteAndReopenPlayerSelect(IPlayer caller, FunAction action, string command)
    {
        _core.Scheduler.NextTick(() => caller.ExecuteCommand(command));
        _core.Scheduler.DelayBySeconds(0.08f, () =>
        {
            if (!caller.IsValid)
            {
                return;
            }

            _core.MenusAPI.OpenMenuForPlayer(caller, BuildPlayerSelectMenu(caller, action));
        });
    }

    private void ExecuteAndReopenSameMenu(IPlayer caller, string command, Action<IPlayer> reopenMenu)
    {
        _core.Scheduler.NextTick(() => caller.ExecuteCommand(command));

        void Reopen()
        {
            if (!caller.IsValid)
            {
                return;
            }

            reopenMenu(caller);
        }

        _core.Scheduler.DelayBySeconds(0.06f, Reopen);
        _core.Scheduler.DelayBySeconds(0.20f, Reopen);
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


