using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Misc;
using System.Text.Json;
using System.Globalization;
using System.Reflection;

namespace CS2_Admin.Commands;

public class PlayerCommands
{
    private static readonly string[] BurnParticleNames =
    [];

    private readonly ISwiftlyCore _core;
    private readonly DiscordWebhook _discord;
    private readonly PermissionsConfig _permissions;
    private readonly CommandsConfig _commands;
    private readonly TagsConfig _tags;
    private readonly MessagesConfig _messagesConfig;
    private readonly BanManager _banManager;
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly WarnManager _warnManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly AdminLogManager _adminLogManager;
    private readonly MultiServerConfig _multiServerConfig;
    private readonly HashSet<int> _noclipPlayers = new();
    private readonly HashSet<int> _frozenPlayers = new();
    private readonly HashSet<int> _freezeVisualPlayers = new();
    private readonly HashSet<int> _beaconPlayers = new();
    private readonly HashSet<int> _burnPlayers = new();
    private readonly HashSet<int> _drugPlayers = new();
    private readonly HashSet<int> _burnVisualWarnedPlayers = new();
    private readonly HashSet<int> _freezeVisualWarnedPlayers = new();
    private readonly HashSet<int> _drugVisualWarnedPlayers = new();
    private readonly HashSet<int> _drugVisualEndLoggedPlayers = new();
    private readonly HashSet<int> _freezeVisualAppliedLoggedPlayers = new();
    private readonly HashSet<int> _drugVisualAppliedLoggedPlayers = new();
    private readonly HashSet<int> _burnVisualAppliedLoggedPlayers = new();
    private readonly HashSet<int> _burnVisualProbeLoggedPlayers = new();
    private readonly HashSet<int> _freezeVisualProbeLoggedPlayers = new();
    private readonly HashSet<int> _drugVisualProbeLoggedPlayers = new();
    private readonly Dictionary<int, float> _freezeOriginalViewmodelFov = new();
    private readonly Dictionary<int, (float X, float Y, float Z)> _freezeOriginalViewmodelOffsets = new();
    private readonly Dictionary<int, QAngle> _drugOriginalRotations = new();
    private readonly Dictionary<int, float> _drugOriginalViewmodelFov = new();
    private readonly Dictionary<int, (float X, float Y, float Z)> _burnOriginalViewmodelOffsets = new();
    private readonly Dictionary<int, DateTime> _burnFallbackMolotovLastSpawnUtc = new();

    private record PlayerListEntry(
        int Id,
        string Name,
        string SteamId,
        int Team,
        string TeamName,
        int Score,
        int Ping,
        bool IsAlive,
        string Ip,
        string Tag
    );

    public PlayerCommands(
        ISwiftlyCore core,
        DiscordWebhook discord,
        PermissionsConfig permissions,
        CommandsConfig commands,
        TagsConfig tags,
        MessagesConfig messagesConfig,
        BanManager banManager,
        MuteManager muteManager,
        GagManager gagManager,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        AdminLogManager adminLogManager,
        MultiServerConfig multiServerConfig)
    {
        _core = core;
        _discord = discord;
        _permissions = permissions;
        _commands = commands;
        _tags = tags;
        _messagesConfig = messagesConfig;
        _banManager = banManager;
        _muteManager = muteManager;
        _gagManager = gagManager;
        _warnManager = warnManager;
        _adminDbManager = adminDbManager;
        _adminLogManager = adminLogManager;
        _multiServerConfig = multiServerConfig;
    }

    public void OnKickCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Kick);

        if (!HasPermission(context, _permissions.Kick))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["kick_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        if (!CanTarget(context, target, allowSelf: true))
        {
            return;
        }

        string reason = args.Length > 1 
            ? string.Join(" ", args.Skip(1)) 
            : PluginLocalizer.Get(_core)["no_reason"];

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetName = target.Controller.PlayerName;

        // Broadcast to all players
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["kicked_notification", visibleAdmin, targetName, reason]}");
        }
        
        // Personal message to target
        PlayerUtils.SendNotification(target, _messagesConfig,
            PluginLocalizer.Get(_core)["kicked_personal_html", reason],
            $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["kicked_personal_chat", reason]}");

        // Kick after short delay to show message
        var targetSteamId = target.SteamID;
        _core.Scheduler.DelayBySeconds(2f, () =>
        {
            var playerToKick = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
            playerToKick?.Kick(reason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
        });

        _ = _discord.SendKickNotificationAsync(adminName, targetName, reason);
        _ = _adminLogManager.AddLogAsync("kick", adminName, context.Sender?.SteamID ?? 0, targetSteamId, target.IPAddress, $"reason={reason}", target.Controller.PlayerName);

        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} kicked {Target}. Reason: {Reason}", 
            adminName, targetName, reason);
    }

    public void OnSlapCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Slap);

        if (!HasPermission(context, _permissions.Slap))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slap_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false)
            .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
            .ToList();
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var damage = 5;
        if (args.Length > 1 && int.TryParse(args[1], out var parsedDamage))
        {
            damage = Math.Clamp(parsedDamage, 0, 100);
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        foreach (var target in targets)
        {
            _core.Scheduler.NextTick(() =>
            {
                var liveTarget = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamID);
                if (liveTarget?.IsValid != true)
                {
                    return;
                }

                var livePawn = liveTarget.PlayerPawn;
                if (livePawn?.IsValid != true || livePawn.Health <= 0)
                {
                    return;
                }

                if (damage > 0)
                {
                    var currentHealth = livePawn.Health;
                    var expectedHealth = Math.Max(currentHealth - damage, 0);

                    if (expectedHealth <= 0)
                    {
                        if (livePawn.Health > 0)
                        {
                            livePawn.CommitSuicide(false, true);
                        }

                        return;
                    }

                    if (livePawn.Health > expectedHealth)
                    {
                        livePawn.Health = expectedHealth;
                        livePawn.HealthUpdated();
                    }
                }

                // Slap feedback: consistent medium-strength pop and random sway.
                var currentVelocity = livePawn.AbsVelocity;
                const float verticalBoost = 260f;
                const float horizontalBoost = 95f;
                var randomX = (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * horizontalBoost;
                var randomY = (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * horizontalBoost;
                var newVelocity = new Vector(
                    currentVelocity.X + randomX,
                    currentVelocity.Y + randomY,
                    MathF.Max(currentVelocity.Z + 30f, verticalBoost));
                livePawn.AbsVelocity = newVelocity;
                var currentOrigin = livePawn.AbsOrigin ?? new Vector(0, 0, 0);
                var currentRotation = livePawn.AbsRotation ?? new QAngle(0, 0, 0);
                livePawn.Teleport(currentOrigin, currentRotation, newVelocity);

                PlayerUtils.SendNotification(
                    liveTarget,
                    _messagesConfig,
                    PluginLocalizer.Get(_core)["slapped_personal_html", ResolveVisibleAdminName(liveTarget, adminName), damage],
                    $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slapped_personal_chat", ResolveVisibleAdminName(liveTarget, adminName), damage]}");

                var targetName = liveTarget.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"];
                foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                {
                    var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                    player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slapped_notification", visibleAdmin, targetName, damage]}");
                }

                _ = _adminLogManager.AddLogAsync("slap", adminName, context.Sender?.SteamID ?? 0, liveTarget.SteamID, liveTarget.IPAddress, $"damage={damage}", liveTarget.Controller.PlayerName);
                _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} slapped {Target} for {Damage} damage", adminName, targetName, damage);
            });
        }
    }

    public void OnSlayCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Slay);

        if (!HasPermission(context, _permissions.Slay))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slay_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];

        foreach (var target in targets)
        {
            if (target.PlayerPawn?.IsValid == true)
            {
                target.PlayerPawn.CommitSuicide(false, true);
            }
        }

        // Personal message to targets
        foreach (var target in targets)
        {
            PlayerUtils.SendNotification(target, _messagesConfig,
                PluginLocalizer.Get(_core)["slayed_personal_html", ResolveVisibleAdminName(target, adminName)],
                $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slayed_personal_chat", ResolveVisibleAdminName(target, adminName)]}");
        }

        if (targets.Count == 1)
        {
            var targetName = targets[0].Controller.PlayerName;
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slayed_notification_single", visibleAdmin, targetName]}");
            }
        }
        else
        {
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slayed_notification_multiple", visibleAdmin, targets.Count]}");
            }
        }

        var targetSteamIds = string.Join(",", targets.Select(t => t.SteamID));
        _ = _adminLogManager.AddLogAsync("slay", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targetSteamIds};count={targets.Count}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} slayed {Count} player(s)", adminName, targets.Count);
    }

    public void OnRespawnCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Respawn);

        if (!HasPermission(context, _permissions.Respawn))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["respawn_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0]);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];

        foreach (var target in targets)
        {
            if (target.Controller.TeamNum >= 2) // T or CT
            {
                target.Respawn();
            }
        }

        // Personal message to targets
        foreach (var target in targets)
        {
            PlayerUtils.SendNotification(target, _messagesConfig,
                PluginLocalizer.Get(_core)["respawned_personal_html", ResolveVisibleAdminName(target, adminName)],
                $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["respawned_personal_chat", ResolveVisibleAdminName(target, adminName)]}");
        }

        if (targets.Count == 1)
        {
            var targetName = targets[0].Controller.PlayerName;
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["respawned_notification_single", visibleAdmin, targetName]}");
            }
        }
        else
        {
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["respawned_notification_multiple", visibleAdmin, targets.Count]}");
            }
        }

        var targetSteamIds = string.Join(",", targets.Select(t => t.SteamID));
        _ = _adminLogManager.AddLogAsync("respawn", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targetSteamIds};count={targets.Count}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} respawned {Count} player(s)", adminName, targets.Count);
    }

    public void OnTeamCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.ChangeTeam);

        if (!HasPermission(context, _permissions.ChangeTeam))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["team_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        if (!CanTarget(context, target, allowSelf: true))
        {
            return;
        }

        var team = PlayerUtils.ParseTeam(args[1]);
        if (team == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["invalid_team"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetName = target.Controller.PlayerName;
        var teamName = PlayerUtils.GetTeamName((int)team.Value, PluginLocalizer.Get(_core));

        target.ChangeTeam(team.Value);
        
        // Personal message to target
        PlayerUtils.SendNotification(target, _messagesConfig,
            PluginLocalizer.Get(_core)["team_changed_personal_html", teamName, ResolveVisibleAdminName(target, adminName)],
            $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["team_changed_personal_chat", teamName, ResolveVisibleAdminName(target, adminName)]}");

        // Broadcast to all players
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["team_changed_notification", visibleAdmin, targetName, teamName]}");
        }

        _ = _adminLogManager.AddLogAsync("team", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"team={teamName}", target.Controller.PlayerName);
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} moved {Target} to {Team}", adminName, targetName, teamName);
    }

    public void OnGotoCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Goto);

        if (!HasPermission(context, _permissions.Goto))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (!context.IsSentByPlayer || context.Sender == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_only_command"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["goto_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        if (!CanTarget(context, target))
        {
            return;
        }

        var admin = context.Sender;

        if (admin.PlayerID == target.PlayerID)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cannot_target_self_goto"]}");
            return;
        }

        var adminPawn = admin.PlayerPawn;
        var targetPawn = target.PlayerPawn;

        if (adminPawn?.IsValid != true || targetPawn?.IsValid != true)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["both_must_be_alive"]}");
            return;
        }

        // Teleport admin near the target, facing them, to avoid getting stuck inside each other
        var targetPos = targetPawn.AbsOrigin ?? adminPawn.AbsOrigin ?? new Vector(0, 0, 0);
        var adminPos = adminPawn.AbsOrigin ?? targetPos;

        var dx = targetPos.X - adminPos.X;
        var dy = targetPos.Y - adminPos.Y;

        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance < 0.001f)
        {
            // If we're already very close, just pick an arbitrary horizontal direction
            dx = 1f;
            dy = 0f;
            distance = 1f;
        }

        dx /= distance;
        dy /= distance;

        const float offset = 50f; // units away from the target

        var destX = targetPos.X - dx * offset;
        var destY = targetPos.Y - dy * offset;
        var destZ = targetPos.Z;

        var destPos = new Vector(destX, destY, destZ);

        // Calculate yaw so the admin looks at the target
        var lookDx = targetPos.X - destX;
        var lookDy = targetPos.Y - destY;
        var yawRad = MathF.Atan2(lookDy, lookDx);
        var yawDeg = yawRad * (180f / MathF.PI);

        var destRot = new QAngle(0, yawDeg, 0);

        var velocity = adminPawn.AbsVelocity;
        adminPawn.Teleport(destPos, destRot, velocity);

        var adminName = admin.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetName = target.Controller.PlayerName;

        admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["goto_success", targetName]}");

        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["goto_notification", visibleAdmin, targetName]}");
        }

        _ = _adminLogManager.AddLogAsync("goto", adminName, admin.SteamID, target.SteamID, target.IPAddress, "", target.Controller.PlayerName);
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} teleported to {Target}", adminName, targetName);
    }

    public void OnBringCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Bring);

        if (!HasPermission(context, _permissions.Bring))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (!context.IsSentByPlayer || context.Sender == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_only_command"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["bring_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        if (!CanTarget(context, target))
        {
            return;
        }

        var admin = context.Sender;

        if (admin.PlayerID == target.PlayerID)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cannot_target_self_bring"]}");
            return;
        }

        var targetPawn = target.PlayerPawn;
        var adminPawn = admin.PlayerPawn;
        if (targetPawn?.IsValid != true || adminPawn?.IsValid != true)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["both_must_be_alive"]}");
            return;
        }

        // Bring target to a stable point in front of admin.
        var adminPos = adminPawn.AbsOrigin ?? new Vector(0, 0, 0);
        var adminRot = adminPawn.AbsRotation ?? new QAngle(0, 0, 0);
        var yawRad = adminRot.Y * (MathF.PI / 180f);
        const float bringOffset = 70f;
        var destPos = new Vector(
            adminPos.X + MathF.Cos(yawRad) * bringOffset,
            adminPos.Y + MathF.Sin(yawRad) * bringOffset,
            adminPos.Z + 2f);

        var destRot = targetPawn.AbsRotation;
        targetPawn.Teleport(destPos, destRot, new Vector(0, 0, 0));

        var adminName = admin.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetName = target.Controller.PlayerName;

        target.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["bring_success", ResolveVisibleAdminName(target, adminName)]}");

        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["bring_notification", visibleAdmin, targetName]}");
        }

        _ = _adminLogManager.AddLogAsync("bring", adminName, admin.SteamID, target.SteamID, target.IPAddress, "", target.Controller.PlayerName);
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} brought {Target}", adminName, targetName);
    }

    private Vector? GetAimPosition(IPlayer player)
    {
        var pawn = player.PlayerPawn;
        if (pawn == null)
            return null;

        var eyePos = pawn.EyePosition;
        if (!eyePos.HasValue)
            return null;

        pawn.EyeAngles.ToDirectionVectors(out var forward, out _, out _);

        var startPos = new Vector(eyePos.Value.X, eyePos.Value.Y, eyePos.Value.Z);
        var endPos = startPos + forward * 8192;

        var trace = new CGameTrace();
        _core.Trace.SimpleTrace(
            startPos,
            endPos,
            RayType_t.RAY_TYPE_LINE,
            RnQueryObjectSet.Static | RnQueryObjectSet.Dynamic,
            MaskTrace.Solid | MaskTrace.Player,
            MaskTrace.Empty,
            MaskTrace.Empty,
            CollisionGroup.Player,
            ref trace,
            pawn
        );

        if (trace.Fraction < 1.0f)
        {
            // Offset the hit position back along the trace direction to avoid spawning inside walls
            var hitPos = trace.EndPos;
            var traceDir = endPos - startPos;
            var traceDirLen = MathF.Sqrt(traceDir.X * traceDir.X + traceDir.Y * traceDir.Y + traceDir.Z * traceDir.Z);
            if (traceDirLen > 0.001f)
            {
                // Normalize and offset back by 32 units (player hull radius)
                var nx = traceDir.X / traceDirLen;
                var ny = traceDir.Y / traceDirLen;
                var nz = traceDir.Z / traceDirLen;
                const float wallOffset = 32f;
                hitPos = new Vector(hitPos.X - nx * wallOffset, hitPos.Y - ny * wallOffset, hitPos.Z - nz * wallOffset);
            }
            // Add small Z offset so player doesn't clip into the ground
            return new Vector(hitPos.X, hitPos.Y, hitPos.Z + 10);
        }

        return null;
    }

    public void OnNoclipCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.NoClip);

        if (!HasPermission(context, _permissions.NoClip))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["noclip_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        if (!CanTarget(context, target, allowSelf: true))
        {
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetName = target.Controller.PlayerName;

        bool isEnabled = _noclipPlayers.Contains(target.PlayerID);
        
        if (isEnabled)
        {
            PlayerUtils.SetNoclip(_core, target, false);
            _noclipPlayers.Remove(target.PlayerID);
        }
        else
        {
            PlayerUtils.SetNoclip(_core, target, true);
            _noclipPlayers.Add(target.PlayerID);
        }

        var state = !isEnabled ? $"\x04{PluginLocalizer.Get(_core)["noclip_on"]}\x01" : $"\x02{PluginLocalizer.Get(_core)["noclip_off"]}\x01";
        
        // Personal message to target
        var stateText = !isEnabled ? PluginLocalizer.Get(_core)["noclip_on"] : PluginLocalizer.Get(_core)["noclip_off"];
        var stateColor = !isEnabled ? "#00ff00" : "#ff0000";
        var senderIsTarget = context.Sender != null && context.Sender.SteamID == target.SteamID;
        if (!senderIsTarget)
        {
            PlayerUtils.SendNotification(target, _messagesConfig,
                PluginLocalizer.Get(_core)["noclip_toggled_personal_html", stateColor, stateText, ResolveVisibleAdminName(target, adminName)],
                $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["noclip_toggled_personal_chat", state, ResolveVisibleAdminName(target, adminName)]}");
        }

        // Broadcast to all players
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["noclip_toggled_notification", visibleAdmin, state, targetName]}");
        }

        _ = _adminLogManager.AddLogAsync("noclip", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"state={(!isEnabled ? "on" : "off")}", target.Controller.PlayerName);
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} toggled noclip {State} for {Target}", 
            adminName, !isEnabled ? "ON" : "OFF", targetName);
    }

    public void OnFreezeCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Freeze);

        if (!HasPermission(context, _permissions.Freeze))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["freeze_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];

        int? durationSeconds = null;
        if (args.Length >= 2 && int.TryParse(args[1], out var parsedSeconds) && parsedSeconds > 0)
        {
            durationSeconds = parsedSeconds;
        }
        _core.Logger.LogInformationIfEnabled(
            "[CS2_Admin][Debug] freeze start by={Admin} targets={Count} duration={Duration}",
            adminName,
            targets.Count,
            durationSeconds?.ToString() ?? "infinite");

        foreach (var target in targets)
        {
            var targetSteamId = target.SteamID;
            _core.Scheduler.NextTick(() =>
            {
                var liveTarget = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                if (liveTarget?.IsValid != true)
                {
                    return;
                }

                var playerId = liveTarget.PlayerID;
                PlayerUtils.Freeze(liveTarget);
                _frozenPlayers.Add(playerId);
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] freeze applied steamid={SteamId} playerId={PlayerId}", liveTarget.SteamID, playerId);
                if (_freezeVisualPlayers.Add(playerId))
                {
                    StartFreezeVisualPulse(liveTarget.SteamID);
                }

                if (durationSeconds.HasValue)
                {
                    _core.Scheduler.DelayBySeconds(durationSeconds.Value, () =>
                    {
                        _core.Scheduler.NextTick(() =>
                        {
                            var player = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.PlayerID == playerId);
                            if (player == null)
                            {
                                return;
                            }

                            if (_frozenPlayers.Contains(playerId))
                            {
                                PlayerUtils.Unfreeze(player);
                                _frozenPlayers.Remove(playerId);
                                _freezeVisualPlayers.Remove(playerId);
                            }
                        });
                    });
                }
            });
        }

        // Personal message to targets
        foreach (var target in targets)
        {
            PlayerUtils.SendNotification(target, _messagesConfig,
                PluginLocalizer.Get(_core)["frozen_personal_html", ResolveVisibleAdminName(target, adminName)],
                $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["frozen_personal_chat", ResolveVisibleAdminName(target, adminName)]}");
        }

        if (targets.Count == 1)
        {
            var targetName = targets[0].Controller.PlayerName;
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["freeze_notification_single", visibleAdmin, targetName]}");
            }
        }
        else
        {
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["freeze_notification_multiple", visibleAdmin, targets.Count]}");
            }
        }

        var freezeTargetSteamIds = string.Join(",", targets.Select(t => t.SteamID));
        _ = _adminLogManager.AddLogAsync("freeze", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={freezeTargetSteamIds};count={targets.Count};duration={durationSeconds?.ToString() ?? "0"}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} froze {Count} player(s)", adminName, targets.Count);
    }

    public void OnUnfreezeCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Unfreeze);

        if (!HasPermission(context, _permissions.Unfreeze))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unfreeze_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0]);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];

        foreach (var target in targets)
        {
            PlayerUtils.Unfreeze(target);
            _frozenPlayers.Remove(target.PlayerID);
            _freezeVisualPlayers.Remove(target.PlayerID);
        }

        // Personal message to targets
        foreach (var target in targets)
        {
            PlayerUtils.SendNotification(target, _messagesConfig,
                PluginLocalizer.Get(_core)["unfrozen_personal_html", ResolveVisibleAdminName(target, adminName)],
                $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unfrozen_personal_chat", ResolveVisibleAdminName(target, adminName)]}");
        }

        if (targets.Count == 1)
        {
            var targetName = targets[0].Controller.PlayerName;
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unfreeze_notification_single", visibleAdmin, targetName]}");
            }
        }
        else
        {
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unfreeze_notification_multiple", visibleAdmin, targets.Count]}");
            }
        }

        var unfreezeTargetSteamIds = string.Join(",", targets.Select(t => t.SteamID));
        _ = _adminLogManager.AddLogAsync("unfreeze", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={unfreezeTargetSteamIds};count={targets.Count}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} unfroze {Count} player(s)", adminName, targets.Count);
    }

    public void OnResizeCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Resize);

        if (!HasPermission(context, _permissions.Resize))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2 || !TryParseFloat(args[1], out var scale))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["resize_usage"]}");
            return;
        }

        scale = Math.Clamp(scale, 0.2f, 3.0f);
        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var applied = 0;
        foreach (var target in targets)
        {
            if (TrySetPlayerScale(target, scale))
            {
                applied++;
            }
        }

        if (applied == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["resize_not_supported"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetNames = FormatTargetNames(targets);
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["resize_notification", visibleAdmin, applied, scale.ToString("0.00", CultureInfo.InvariantCulture)]} | target: {targetNames}");
        }

        _ = _adminLogManager.AddLogAsync("resize", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={applied};scale={scale.ToString("0.00", CultureInfo.InvariantCulture)}");
    }

    public void OnDrugCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Drug);

        if (!HasPermission(context, _permissions.Drug))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["drug_usage"]}");
            return;
        }

        var durationSeconds = 10;
        if (args.Length > 1 && int.TryParse(args[1], out var parsedDuration))
        {
            durationSeconds = Math.Clamp(parsedDuration, 1, 60);
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false)
            .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
            .ToList();
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }
        _core.Logger.LogInformationIfEnabled(
            "[CS2_Admin][Debug] drug start by={Admin} targets={Count} duration={Duration}",
            context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"],
            targets.Count,
            durationSeconds);

        foreach (var target in targets)
        {
            var targetSteamId = target.SteamID;
            _core.Scheduler.NextTick(() =>
            {
                var liveTarget = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                if (liveTarget?.IsValid != true)
                {
                    return;
                }

                var pawn = liveTarget.PlayerPawn;
                if (pawn?.IsValid != true || pawn.Health <= 0)
                {
                    return;
                }

                var playerId = liveTarget.PlayerID;
                _drugPlayers.Add(playerId);
                _drugVisualEndLoggedPlayers.Remove(playerId);
                LogVisualProbeOnce(liveTarget, "drug", _drugVisualProbeLoggedPlayers);
                _drugOriginalRotations[playerId] = pawn.AbsRotation ?? new QAngle(0, 0, 0);
                if (!_drugOriginalViewmodelFov.ContainsKey(playerId))
                {
                    _drugOriginalViewmodelFov[playerId] = pawn.ViewmodelFOV > 0 ? pawn.ViewmodelFOV : 68f;
                }
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] drug applied steamid={SteamId} playerId={PlayerId} duration={Duration} baseFov={Fov}", liveTarget.SteamID, playerId, durationSeconds, _drugOriginalViewmodelFov[playerId]);

                StartDrugEffect(liveTarget.SteamID, durationSeconds);
            });
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetNames = FormatTargetNames(targets);
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["drug_notification", visibleAdmin, targets.Count, durationSeconds]} | target: {targetNames}");
        }

        _ = _adminLogManager.AddLogAsync("drug", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targets.Count};duration={durationSeconds}");
    }

    public void OnBeaconCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Beacon);

        if (!HasPermission(context, _permissions.Beacon))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["beacon_usage"]}");
            return;
        }

        var durationSeconds = 20;
        var stopRequested = args.Length > 1 && (args[1].Equals("off", StringComparison.OrdinalIgnoreCase) || args[1] == "0");
        if (args.Length > 1 && !stopRequested && int.TryParse(args[1], out var parsedDuration))
        {
            durationSeconds = Math.Clamp(parsedDuration, 1, 120);
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var started = 0;
        var stopped = 0;
        foreach (var target in targets)
        {
            if (stopRequested)
            {
                if (_beaconPlayers.Remove(target.PlayerID))
                {
                    stopped++;
                }

                continue;
            }

            _beaconPlayers.Add(target.PlayerID);
            StartBeaconEffect(target.SteamID, durationSeconds);
            started++;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        if (stopRequested)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["beacon_stopped", stopped]}");
            _ = _adminLogManager.AddLogAsync("beacon", adminName, context.Sender?.SteamID ?? 0, null, null, $"mode=off;targets={stopped}");
            return;
        }

        var targetNames = FormatTargetNames(targets);
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["beacon_started", visibleAdmin, started, durationSeconds]} | target: {targetNames}");
        }

        _ = _adminLogManager.AddLogAsync("beacon", adminName, context.Sender?.SteamID ?? 0, null, null, $"mode=on;targets={started};duration={durationSeconds}");
    }

    public void OnBurnCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Burn);

        if (!HasPermission(context, _permissions.Burn))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["burn_usage"]}");
            return;
        }

        var durationSeconds = 8;
        var isInfiniteDuration = false;
        if (args.Length > 1 && int.TryParse(args[1], out var parsedDuration))
        {
            if (parsedDuration == -1)
            {
                isInfiniteDuration = true;
                durationSeconds = -1;
            }
            else
            {
                durationSeconds = Math.Clamp(parsedDuration, 1, 60);
            }
        }

        var damagePerTick = 5;
        if (args.Length > 2 && int.TryParse(args[2], out var parsedDamage))
        {
            damagePerTick = Math.Clamp(parsedDamage, 1, 100);
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false)
            .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
            .ToList();
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        foreach (var target in targets)
        {
            var targetSteamId = target.SteamID;
            _core.Scheduler.NextTick(() =>
            {
                var liveTarget = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                if (liveTarget?.IsValid != true)
                {
                    return;
                }

                var pawn = liveTarget.PlayerPawn;
                if (pawn?.IsValid != true || pawn.Health <= 0)
                {
                    return;
                }

                _burnPlayers.Add(liveTarget.PlayerID);
                _ = liveTarget.SendCenterHTMLAsync("<font color='#ff7a3d'><b>BURNING</b></font><br><font color='#ffd2bd'>Fire damage active</font>", 900);
                StartBurnEffect(liveTarget.SteamID, isInfiniteDuration ? null : durationSeconds, damagePerTick);
            });
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetNames = FormatTargetNames(targets);
        var durationLabel = isInfiniteDuration
            ? PluginLocalizer.Get(_core)["duration_permanent"]
            : durationSeconds.ToString(CultureInfo.InvariantCulture);
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["burn_notification", visibleAdmin, targets.Count, durationLabel, damagePerTick]} | target: {targetNames}");
        }

        _ = _adminLogManager.AddLogAsync("burn", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targets.Count};duration={(isInfiniteDuration ? "infinite" : durationSeconds.ToString(CultureInfo.InvariantCulture))};dmg={damagePerTick}");
    }

    public void OnDisarmCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Disarm);

        if (!HasPermission(context, _permissions.Disarm))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["disarm_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var changed = 0;
        foreach (var target in targets)
        {
            var itemServices = target.PlayerPawn?.ItemServices;
            if (itemServices?.IsValid == true)
            {
                itemServices.RemoveItems();
                changed++;
            }
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetNames = FormatTargetNames(targets);
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["disarm_notification", visibleAdmin, changed]} | target: {targetNames}");
        }

        _ = _adminLogManager.AddLogAsync("disarm", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={changed}");
    }

    public void OnSpeedCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Speed);

        if (!HasPermission(context, _permissions.Speed))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2 || !TryParseFloat(args[1], out var speedMultiplier))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["speed_usage"]}");
            return;
        }

        speedMultiplier = Math.Clamp(speedMultiplier, 0.1f, 10.0f);

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false)
            .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
            .ToList();
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        foreach (var target in targets)
        {
            var pawn = target.PlayerPawn;
            if (pawn?.IsValid != true)
            {
                continue;
            }

            pawn.VelocityModifier = speedMultiplier;
            pawn.VelocityModifierUpdated();
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetNames = FormatTargetNames(targets);
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["speed_notification", visibleAdmin, targets.Count, speedMultiplier.ToString("0.00", CultureInfo.InvariantCulture)]} | target: {targetNames}");
        }

        _ = _adminLogManager.AddLogAsync("speed", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targets.Count};speed={speedMultiplier.ToString("0.00", CultureInfo.InvariantCulture)}");
    }

    public void OnGravityCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Gravity);

        if (!HasPermission(context, _permissions.Gravity))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2 || !TryParseFloat(args[1], out var gravityMultiplier))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["gravity_usage"]}");
            return;
        }

        gravityMultiplier = Math.Clamp(gravityMultiplier, 0.1f, 5.0f);

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false)
            .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
            .ToList();
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        foreach (var target in targets)
        {
            ApplyGravityWithRetries(target.SteamID, gravityMultiplier, retries: 6, totalRetries: 6);
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetNames = FormatTargetNames(targets);
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["gravity_notification", visibleAdmin, targets.Count, gravityMultiplier.ToString("0.00", CultureInfo.InvariantCulture)]} | target: {targetNames}");
        }

        _ = _adminLogManager.AddLogAsync("gravity", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targets.Count};gravity={gravityMultiplier.ToString("0.00", CultureInfo.InvariantCulture)}");
    }

    public void OnRenameCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Rename);

        if (!HasPermission(context, _permissions.Rename))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["rename_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var newName = string.Join(" ", args.Skip(1)).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(newName))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["rename_usage"]}");
            return;
        }

        if (newName.Length > 64)
        {
            newName = newName[..64];
        }

        var oldNamesBySteamId = new Dictionary<ulong, string>();
        foreach (var target in targets)
        {
            if (target.Controller == null || !target.Controller.IsValid)
            {
                continue;
            }

            var existing = target.Controller.PlayerName;
            oldNamesBySteamId[target.SteamID] = string.IsNullOrWhiteSpace(existing)
                ? PluginLocalizer.Get(_core)["player_fallback_name", target.PlayerID]
                : existing;
        }

        var changed = 0;
        var renamedTargetOldNames = new List<string>();
        foreach (var target in targets)
        {
            if (target.Controller == null || !target.Controller.IsValid)
            {
                continue;
            }

            var oldName = oldNamesBySteamId.TryGetValue(target.SteamID, out var snapshotOldName)
                ? snapshotOldName
                : target.Controller.PlayerName;
            if (string.IsNullOrWhiteSpace(oldName))
            {
                oldName = PluginLocalizer.Get(_core)["player_fallback_name", target.PlayerID];
            }

            target.Controller.PlayerName = newName;
            target.Controller.PlayerNameUpdated();
            changed++;
            renamedTargetOldNames.Add(oldName);
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetNames = renamedTargetOldNames.Count > 0
            ? string.Join(", ", renamedTargetOldNames.Distinct(StringComparer.OrdinalIgnoreCase))
            : FormatTargetNames(targets);
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["rename_notification", visibleAdmin, changed, newName]} | target: {targetNames}");
        }

        _ = _adminLogManager.AddLogAsync("rename", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={changed};name={newName}");
    }

    public void OnHpCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Hp);

        if (!HasPermission(context, _permissions.Hp))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2 || !int.TryParse(args[1], out var hp))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["hp_usage"]}");
            return;
        }

        hp = Math.Clamp(hp, 0, 1000);
        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var changed = 0;
        foreach (var target in targets)
        {
            var pawn = target.PlayerPawn;
            if (pawn?.IsValid != true)
            {
                continue;
            }

            if (hp <= 0 && pawn.Health > 0)
            {
                pawn.CommitSuicide(false, true);
            }
            else
            {
                pawn.Health = hp;
                pawn.HealthUpdated();
            }

            changed++;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetNames = FormatTargetNames(targets);
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["hp_notification", visibleAdmin, changed, hp]} | target: {targetNames}");
        }

        _ = _adminLogManager.AddLogAsync("hp", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={changed};hp={hp}");
    }

    public void OnMoneyCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Money);

        if (!HasPermission(context, _permissions.Money))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2 || !int.TryParse(args[1], out var amount))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["money_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var commandName = context.CommandName ?? string.Empty;
        var isAdditive = commandName.Contains("givemoney", StringComparison.OrdinalIgnoreCase);
        amount = Math.Clamp(amount, 0, 65000);

        var changed = 0;
        foreach (var target in targets)
        {
            var moneyServices = target.Controller.InGameMoneyServices;
            if (moneyServices?.IsValid != true)
            {
                continue;
            }

            if (isAdditive)
            {
                var next = Math.Clamp(moneyServices.Account + amount, 0, 65000);
                moneyServices.Account = next;
            }
            else
            {
                moneyServices.Account = amount;
            }

            moneyServices.AccountUpdated();
            changed++;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var key = isAdditive ? "money_add_notification" : "money_set_notification";
        var targetNames = FormatTargetNames(targets);
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)[key, visibleAdmin, changed, amount]} | target: {targetNames}");
        }

        var mode = isAdditive ? "add" : "set";
        _ = _adminLogManager.AddLogAsync("money", adminName, context.Sender?.SteamID ?? 0, null, null, $"mode={mode};targets={changed};amount={amount}");
    }

    public void OnGiveCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Give);

        if (!HasPermission(context, _permissions.Give))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["give_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false)
            .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
            .ToList();
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var item = NormalizeItemName(args[1]);
        var changed = 0;
        foreach (var target in targets)
        {
            var itemServices = target.PlayerPawn?.ItemServices;
            if (itemServices?.IsValid != true)
            {
                continue;
            }

            itemServices.GiveItem(item);
            changed++;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetNames = FormatTargetNames(targets);
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["give_notification", visibleAdmin, changed, item]} | target: {targetNames}");
        }

        _ = _adminLogManager.AddLogAsync("give", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={changed};item={item}");
    }

    public void OnPlayersCommand(ICommandContext context)
    {
        if (!HasPermission(context, _permissions.ListPlayers))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.ListPlayers);
        var isJson = args.Length >= 1 && string.Equals(args[0], "-json", StringComparison.OrdinalIgnoreCase);

        var players = _core.PlayerManager
            .GetAllPlayers()
            .Where(p => p.IsValid)
            .OrderBy(p => p.Controller.PlayerName)
            .ToList();

        if (!isJson)
        {
            var lines = new List<string>
            {
                PluginLocalizer.Get(_core)["players_list_header"]
            };

            foreach (var player in players)
            {
                var tag = _tags.Enabled
                    ? PlayerUtils.GetScoreTag(player, _tags.PlayerTag)
                    : "-";

                lines.Add(
                    PluginLocalizer.Get(_core)["players_list_entry", player.PlayerID, tag, player.Controller.PlayerName ?? PluginLocalizer.Get(_core)["player_fallback_name", player.PlayerID]]);
            }

            lines.Add(PluginLocalizer.Get(_core)["players_list_footer"]);

            var output = string.Join('\n', lines);

            if (context.IsSentByPlayer && context.Sender != null)
            {
                context.Sender.SendConsole(output);

                if (context.Sender.IsValid && !context.Sender.IsFakeClient)
                {
                    context.Sender.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["players_list_console"]}");
                }
            }
            else
            {
                _core.Logger.LogInformationIfEnabled("{PlayerList}", output);
            }
        }
        else
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

            var entries = players
                .Select(p =>
                {
                    var teamNum = p.Controller.TeamNum;
                    var teamName = PlayerUtils.GetTeamName(teamNum, PluginLocalizer.Get(_core));
                    var score = p.Controller.Score;
                    var ping = (int)p.Controller.Ping;
                    var isAlive = p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0;
                    var ip = (p.IPAddress ?? PluginLocalizer.Get(_core)["unknown"]).Split(':')[0];
                    var tag = _tags.Enabled
                        ? PlayerUtils.GetScoreTag(p, _tags.PlayerTag)
                        : "-";

                    return new PlayerListEntry(
                        p.PlayerID,
                        p.Controller.PlayerName,
                        p.SteamID.ToString(),
                        teamNum,
                        teamName,
                        score,
                        ping,
                        isAlive,
                        ip,
                        tag
                    );
                })
                .ToList();

            var json = JsonSerializer.Serialize(entries, options);

            if (context.IsSentByPlayer && context.Sender != null)
            {
                context.Sender.SendConsole(json);

                if (context.Sender.IsValid && !context.Sender.IsFakeClient)
                {
                    context.Sender.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["players_list_json_console"]}");
                }
            }
            else
            {
                _core.Logger.LogInformationIfEnabled("{PlayerListJson}", json);
            }
        }
    }

    public void OnWhoCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Who);

        if (!HasPermission(context, _permissions.Who))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["who_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        var steamId64 = target.SteamID;
        var targetPlayerId = target.PlayerID;
        var targetIp = target.IPAddress;

        _ = Task.Run(async () =>
        {
            var admin = await _adminDbManager.GetAdminAsync(steamId64);
            var effectiveFlags = await _adminDbManager.GetEffectiveFlagsAsync(steamId64);
            var effectiveImmunity = await _adminDbManager.GetEffectiveImmunityAsync(steamId64);
            var ban = await _banManager.GetActiveBanAsync(steamId64, targetIp, _multiServerConfig.Enabled);
            var mute = await _muteManager.GetActiveMuteAsync(steamId64);
            var gag = await _gagManager.GetActiveGagAsync(steamId64);
            var warn = await _warnManager.GetActiveWarnAsync(steamId64);
            var totalBans = await _banManager.GetTotalBansAsync(steamId64);
            var totalMutes = await _muteManager.GetTotalMutesAsync(steamId64);
            var totalGags = await _gagManager.GetTotalGagsAsync(steamId64);
            var totalWarns = await _warnManager.GetTotalWarnsAsync(steamId64);

            _core.Scheduler.NextTick(() =>
            {
                var liveTarget = _core.PlayerManager.GetPlayer(targetPlayerId);
                var name = liveTarget?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["player_fallback_name", targetPlayerId];
                var userId = targetPlayerId;
                var ip = (liveTarget?.IPAddress ?? targetIp ?? PluginLocalizer.Get(_core)["who_unknown"]).Split(':')[0];
                var ping = liveTarget != null ? (int)liveTarget.Controller.Ping : 0;
                var teamNum = liveTarget?.Controller.TeamNum ?? 0;
                var teamName = PlayerUtils.GetTeamName(teamNum, PluginLocalizer.Get(_core));
                var isAlive = liveTarget?.PlayerPawn?.IsValid == true && liveTarget.PlayerPawn.Health > 0;

                var lines = new List<string>
                {
                    PluginLocalizer.Get(_core)["who_header", name],
                    PluginLocalizer.Get(_core)["who_name", name],
                    PluginLocalizer.Get(_core)["who_userid", userId],
                    PluginLocalizer.Get(_core)["who_steamid", steamId64],
                    PluginLocalizer.Get(_core)["who_team", teamName, teamNum],
                    PluginLocalizer.Get(_core)["who_ip", ip],
                    PluginLocalizer.Get(_core)["who_ping", ping],
                    PluginLocalizer.Get(_core)["who_alive", isAlive ? PluginLocalizer.Get(_core)["players_yes"] : PluginLocalizer.Get(_core)["players_no"]]
                };

                if (admin != null)
                {
                    var flags = effectiveFlags.Length == 0
                        ? PluginLocalizer.Get(_core)["who_none"]
                        : string.Join(",", effectiveFlags);
                    lines.Add(PluginLocalizer.Get(_core)["who_admin_flags", flags, effectiveImmunity]);
                }

                if (ban != null && ban.IsActive)
                {
                    var expires = ban.IsPermanent ? PluginLocalizer.Get(_core)["duration_permanent"] : ban.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? PluginLocalizer.Get(_core)["who_unknown"];
                    lines.Add(PluginLocalizer.Get(_core)["who_active_ban_yes", ban.Reason, expires]);
                }
                else
                {
                    lines.Add(PluginLocalizer.Get(_core)["who_active_ban_no"]);
                }

                if (mute != null && mute.IsActive)
                {
                    var expires = mute.IsPermanent ? PluginLocalizer.Get(_core)["duration_permanent"] : mute.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? PluginLocalizer.Get(_core)["who_unknown"];
                    lines.Add(PluginLocalizer.Get(_core)["who_active_mute_yes", mute.Reason, expires]);
                }
                else
                {
                    lines.Add(PluginLocalizer.Get(_core)["who_active_mute_no"]);
                }

                if (gag != null && gag.IsActive)
                {
                    var expires = gag.IsPermanent ? PluginLocalizer.Get(_core)["duration_permanent"] : gag.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? PluginLocalizer.Get(_core)["who_unknown"];
                    lines.Add(PluginLocalizer.Get(_core)["who_active_gag_yes", gag.Reason, expires]);
                }
                else
                {
                    lines.Add(PluginLocalizer.Get(_core)["who_active_gag_no"]);
                }

                if (warn != null && warn.IsActive)
                {
                    var expires = warn.IsPermanent ? PluginLocalizer.Get(_core)["duration_permanent"] : warn.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? PluginLocalizer.Get(_core)["who_unknown"];
                    lines.Add(PluginLocalizer.Get(_core)["who_active_warn_yes", warn.Reason, expires]);
                }
                else
                {
                    lines.Add(PluginLocalizer.Get(_core)["who_active_warn_no"]);
                }

                lines.Add(PluginLocalizer.Get(_core)["who_total_bans", totalBans]);
                lines.Add(PluginLocalizer.Get(_core)["who_total_mutes", totalMutes]);
                lines.Add(PluginLocalizer.Get(_core)["who_total_gags", totalGags]);
                lines.Add(PluginLocalizer.Get(_core)["who_total_warns", totalWarns]);

                lines.Add(PluginLocalizer.Get(_core)["who_footer", name]);

                var output = string.Join('\n', lines);

                if (context.IsSentByPlayer && context.Sender != null)
                {
                    context.Sender.SendConsole(output);

                    if (context.Sender.IsValid && !context.Sender.IsFakeClient)
                    {
                        context.Sender.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["who_console"]}");
                    }
                }
                else
                {
                    _core.Logger.LogInformationIfEnabled("{WhoInfo}", output);
                }
            });
        });
    }

    public void OnPlayerDisconnect(int playerId)
    {
        _noclipPlayers.Remove(playerId);
        _frozenPlayers.Remove(playerId);
        _freezeVisualPlayers.Remove(playerId);
        _beaconPlayers.Remove(playerId);
        _burnPlayers.Remove(playerId);
        _drugPlayers.Remove(playerId);
        _burnVisualWarnedPlayers.Remove(playerId);
        _freezeVisualWarnedPlayers.Remove(playerId);
        _drugVisualWarnedPlayers.Remove(playerId);
        _drugVisualEndLoggedPlayers.Remove(playerId);
        _freezeVisualAppliedLoggedPlayers.Remove(playerId);
        _drugVisualAppliedLoggedPlayers.Remove(playerId);
        _burnVisualAppliedLoggedPlayers.Remove(playerId);
        _burnVisualProbeLoggedPlayers.Remove(playerId);
        _freezeVisualProbeLoggedPlayers.Remove(playerId);
        _drugVisualProbeLoggedPlayers.Remove(playerId);
        _freezeOriginalViewmodelFov.Remove(playerId);
        _freezeOriginalViewmodelOffsets.Remove(playerId);
        _drugOriginalRotations.Remove(playerId);
        _drugOriginalViewmodelFov.Remove(playerId);
        _burnOriginalViewmodelOffsets.Remove(playerId);
        _burnFallbackMolotovLastSpawnUtc.Remove(playerId);
    }

    private void StartFreezeVisualPulse(ulong targetSteamId)
    {
        _core.Scheduler.DelayBySeconds(1f, () =>
        {
            _core.Scheduler.NextTick(() =>
            {
                var target = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                if (target?.IsValid != true)
                {
                    return;
                }

                var playerId = target.PlayerID;
                if (!_frozenPlayers.Contains(playerId) || !_freezeVisualPlayers.Contains(playerId))
                {
                    _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] freeze pulse stop steamid={SteamId} playerId={PlayerId} reason=not_frozen", target.SteamID, playerId);
                    _freezeVisualPlayers.Remove(playerId);
                    return;
                }

                var pawn = target.PlayerPawn;
                if (pawn?.IsValid != true || pawn.Health <= 0)
                {
                    _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] freeze pulse stop steamid={SteamId} playerId={PlayerId} reason=invalid_or_dead", target.SteamID, playerId);
                    _frozenPlayers.Remove(playerId);
                    _freezeVisualPlayers.Remove(playerId);
                    return;
                }

                _ = target.SendCenterHTMLAsync("<font color='#7ec8ff'><b>FROZEN</b></font><br><font color='#bfe7ff'>Movement blocked</font>", 900);
                StartFreezeVisualPulse(targetSteamId);
            });
        });
    }

    private void CaptureFreezeVisualState(IPlayer target)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            return;
        }

        var playerId = target.PlayerID;
        if (_freezeOriginalViewmodelFov.ContainsKey(playerId))
        {
            return;
        }

        var capturedFov = pawn.ViewmodelFOV > 0 ? pawn.ViewmodelFOV : 68f;
        _freezeOriginalViewmodelFov[playerId] = capturedFov;
        _freezeOriginalViewmodelOffsets[playerId] = (pawn.ViewmodelOffsetX, pawn.ViewmodelOffsetY, pawn.ViewmodelOffsetZ);
        _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] freeze capture fov steamid={SteamId} playerId={PlayerId} fov={Fov}", target.SteamID, playerId, capturedFov);
    }

    private void ApplyFreezeVisualState(IPlayer target)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            return;
        }

        var playerId = target.PlayerID;
        var baseFov = _freezeOriginalViewmodelFov.TryGetValue(playerId, out var originalFov) && originalFov > 0
            ? originalFov
            : (pawn.ViewmodelFOV > 0 ? pawn.ViewmodelFOV : 68f);
        var freezeFov = Math.Clamp(baseFov - 18f, 38f, 72f);
        if (!TrySetViewmodelFov(target, freezeFov))
        {
            if (_freezeVisualWarnedPlayers.Add(playerId))
            {
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] freeze fov apply failed steamid={SteamId} playerId={PlayerId} base={BaseFov} target={TargetFov}", target.SteamID, playerId, baseFov, freezeFov);
            }
            return;
        }

        if (_freezeVisualAppliedLoggedPlayers.Add(playerId))
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] freeze fov apply success steamid={SteamId} playerId={PlayerId} snapshot={Snapshot}", target.SteamID, playerId, BuildVisualSnapshot(target));
        }

        if (_freezeOriginalViewmodelOffsets.TryGetValue(playerId, out var offsets))
        {
            TrySetFloatPropertyWithUpdated(pawn, "ViewmodelOffsetX", offsets.X - 0.9f);
            TrySetFloatPropertyWithUpdated(pawn, "ViewmodelOffsetY", offsets.Y - 0.6f);
            TrySetFloatPropertyWithUpdated(pawn, "ViewmodelOffsetZ", offsets.Z - 1.4f);
        }

        ApplyCameraJolt(target, 0f, 0f, playerId % 2 == 0 ? -9f : 9f);

        if (!TryApplyFlashOverlay(target, 1.25f, 110f))
        {
            if (_freezeVisualWarnedPlayers.Add(playerId))
            {
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] freeze flash apply failed steamid={SteamId} playerId={PlayerId}", target.SteamID, playerId);
            }
        }

        if (_freezeVisualWarnedPlayers.Remove(playerId))
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] freeze fov apply recovered steamid={SteamId} playerId={PlayerId}", target.SteamID, playerId);
        }
    }

    private void RestoreFreezeVisualState(int playerId)
    {
        if (!_freezeOriginalViewmodelFov.TryGetValue(playerId, out var originalFov) || originalFov <= 0)
        {
            _freezeOriginalViewmodelFov.Remove(playerId);
            return;
        }

        var target = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.PlayerID == playerId);
        var pawn = target?.PlayerPawn;
        if (target != null && pawn?.IsValid == true)
        {
            ClearFlashOverlay(target);
            if (!TrySetViewmodelFov(target, originalFov))
            {
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] freeze fov restore failed playerId={PlayerId} fov={Fov}", playerId, originalFov);
            }
            else
            {
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] freeze fov restored playerId={PlayerId} fov={Fov}", playerId, originalFov);
            }

            if (_freezeOriginalViewmodelOffsets.TryGetValue(playerId, out var offsets))
            {
                TrySetFloatPropertyWithUpdated(pawn, "ViewmodelOffsetX", offsets.X);
                TrySetFloatPropertyWithUpdated(pawn, "ViewmodelOffsetY", offsets.Y);
                TrySetFloatPropertyWithUpdated(pawn, "ViewmodelOffsetZ", offsets.Z);
            }

            ResetCameraRoll(target);
        }

        _freezeVisualWarnedPlayers.Remove(playerId);
        _freezeVisualAppliedLoggedPlayers.Remove(playerId);
        _freezeOriginalViewmodelFov.Remove(playerId);
        _freezeOriginalViewmodelOffsets.Remove(playerId);
    }

    private void StartBeaconEffect(ulong targetSteamId, int ticksRemaining)
    {
        _core.Scheduler.DelayBySeconds(1f, () =>
        {
            var target = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
            if (target?.IsValid != true)
            {
                return;
            }

            if (!_beaconPlayers.Contains(target.PlayerID))
            {
                return;
            }

            var targetName = target.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"];
            var playerId = target.PlayerID;
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["beacon_ping", targetName, playerId]}");
            }

            if (ticksRemaining <= 1)
            {
                _beaconPlayers.Remove(playerId);
                return;
            }

            StartBeaconEffect(targetSteamId, ticksRemaining - 1);
        });
    }

    private void StartBurnEffect(ulong targetSteamId, int? ticksRemaining, int damagePerTick)
    {
        _core.Scheduler.DelayBySeconds(1f, () =>
        {
            _core.Scheduler.NextTick(() =>
            {
                var target = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                if (target?.IsValid != true)
                {
                    return;
                }

                var playerId = target.PlayerID;
                if (!_burnPlayers.Contains(playerId))
                {
                    return;
                }

                var pawn = target.PlayerPawn;
                if (pawn?.IsValid != true || pawn.Health <= 0)
                {
                    _burnPlayers.Remove(playerId);
                    return;
                }

                var nextHealth = Math.Max(pawn.Health - damagePerTick, 0);
                if (nextHealth <= 0)
                {
                    pawn.CommitSuicide(false, true);
                    _burnPlayers.Remove(playerId);
                    return;
                }

                pawn.Health = nextHealth;
                pawn.HealthUpdated();
                PlayerUtils.SendNotification(
                    target,
                    _messagesConfig,
                    PluginLocalizer.Get(_core)["burn_personal_html", damagePerTick],
                    $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["burn_personal_chat", damagePerTick]}");
                _ = target.SendCenterHTMLAsync("<font color='#ff7a3d'><b>BURNING</b></font><br><font color='#ffd2bd'>Fire damage active</font>", 900);

                if (ticksRemaining.HasValue && ticksRemaining.Value <= 1)
                {
                    _burnPlayers.Remove(playerId);
                    return;
                }

                var nextTicks = ticksRemaining.HasValue ? ticksRemaining.Value - 1 : (int?)null;
                StartBurnEffect(targetSteamId, nextTicks, damagePerTick);
            });
        });
    }

    private void StartDrugEffect(ulong targetSteamId, int ticksRemaining)
    {
        _core.Scheduler.DelayBySeconds(1f, () =>
        {
            _core.Scheduler.NextTick(() =>
            {
                var target = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                if (target?.IsValid != true)
                {
                    return;
                }

                var playerId = target.PlayerID;
                if (!_drugPlayers.Contains(playerId))
                {
                    return;
                }

                var pawn = target.PlayerPawn;
                if (pawn?.IsValid != true || pawn.Health <= 0)
                {
                    _drugPlayers.Remove(playerId);
                    _drugOriginalRotations.Remove(playerId);
                    _drugOriginalViewmodelFov.Remove(playerId);
                    return;
                }

                pawn.VelocityModifier = 0.60f;
                pawn.VelocityModifierUpdated();

                var origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
                var baseRot = _drugOriginalRotations.TryGetValue(playerId, out var savedRot)
                    ? savedRot
                    : (pawn.AbsRotation ?? new QAngle(0, 0, 0));
                var nextYaw = baseRot.Y + Random.Shared.Next(-18, 19);
                var nextRoll = ticksRemaining % 2 == 0 ? 12f : -12f;
                var randomX = Random.Shared.Next(-45, 46);
                var randomY = Random.Shared.Next(-45, 46);
                var currentVelocity = pawn.AbsVelocity;
                pawn.AbsVelocity = new Vector(currentVelocity.X + randomX, currentVelocity.Y + randomY, currentVelocity.Z);
                pawn.Teleport(origin, new QAngle(baseRot.X, nextYaw, nextRoll), pawn.AbsVelocity);

                // Visual drug effect: pulse viewmodel FOV with a small random jitter.
                var baseFov = _drugOriginalViewmodelFov.TryGetValue(playerId, out var originalFov) && originalFov > 0
                    ? originalFov
                    : 68f;
                var phase = (ticksRemaining % 8) * 0.8f;
                var wave = MathF.Sin(phase) * 7f;
                var jitter = (float)(Random.Shared.NextDouble() * 4.0 - 2.0);
                var drugFov = Math.Clamp(baseFov + wave + jitter, 45f, 95f);
                var flashAlpha = 35f + (MathF.Abs(MathF.Sin(phase)) * 95f);
                if (!TrySetViewmodelFov(target, drugFov))
                {
                    if (_drugVisualWarnedPlayers.Add(playerId))
                    {
                        _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] drug fov apply failed steamid={SteamId} playerId={PlayerId} base={BaseFov} target={TargetFov}", target.SteamID, playerId, baseFov, drugFov);
                    }
                }
                else if (!TryApplyFlashOverlay(target, 0.75f, flashAlpha))
                {
                    if (_drugVisualWarnedPlayers.Add(playerId))
                    {
                        _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] drug flash apply failed steamid={SteamId} playerId={PlayerId} alpha={Alpha}", target.SteamID, playerId, flashAlpha);
                    }
                }
                else if (_drugVisualWarnedPlayers.Remove(playerId))
                {
                    _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] drug fov apply recovered steamid={SteamId} playerId={PlayerId}", target.SteamID, playerId);
                }
                else if (_drugVisualAppliedLoggedPlayers.Add(playerId))
                {
                    _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] drug fov apply success steamid={SteamId} playerId={PlayerId} snapshot={Snapshot}", target.SteamID, playerId, BuildVisualSnapshot(target));
                }
                _ = target.SendCenterHTMLAsync("<font color='#c49cff'><b>DRUGGED</b></font><br><font color='#eadbff'>Vision distorted</font>", 900);

                if (ticksRemaining <= 1)
                {
                    pawn.VelocityModifier = 1.0f;
                    pawn.VelocityModifierUpdated();
                    ClearFlashOverlay(target);
                    if (_drugOriginalViewmodelFov.TryGetValue(playerId, out var restoreFov) && restoreFov > 0)
                    {
                        TrySetViewmodelFov(target, restoreFov);
                    }
                    var finalOrigin = pawn.AbsOrigin ?? origin;
                    if (_drugOriginalRotations.TryGetValue(playerId, out var originalRot))
                    {
                        pawn.Teleport(finalOrigin, new QAngle(originalRot.X, originalRot.Y, 0f), pawn.AbsVelocity);
                    }
                    _drugPlayers.Remove(playerId);
                    _drugOriginalRotations.Remove(playerId);
                    _drugOriginalViewmodelFov.Remove(playerId);
                    _drugVisualWarnedPlayers.Remove(playerId);
                    _drugVisualAppliedLoggedPlayers.Remove(playerId);
                    if (_drugVisualEndLoggedPlayers.Add(playerId))
                    {
                        _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] drug end steamid={SteamId} playerId={PlayerId}", target.SteamID, playerId);
                    }
                    return;
                }

                StartDrugEffect(targetSteamId, ticksRemaining - 1);
            });
        });
    }

    private void ApplyGravityWithRetries(ulong targetSteamId, float gravityMultiplier, int retries, int totalRetries)
    {
        _core.Scheduler.NextTick(() =>
        {
            var target = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
            var pawn = target?.PlayerPawn;
            if (pawn?.IsValid != true)
            {
                return;
            }

            var applied = TryApplyGravity(target!, gravityMultiplier, out var appliedBy);
            if (!applied && retries <= 0)
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Gravity could not be applied to {SteamId}. Runtime does not expose a writable gravity member.", targetSteamId);
            }
            else if (applied && retries == totalRetries)
            {
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] gravity applied steamid={SteamId} value={Gravity} via={Method}", targetSteamId, gravityMultiplier, appliedBy);
            }
        });

        if (retries <= 0)
        {
            return;
        }

        _core.Scheduler.DelayBySeconds(0.5f, () => ApplyGravityWithRetries(targetSteamId, gravityMultiplier, retries - 1, totalRetries));
    }

    private bool TryApplyGravity(IPlayer target, float gravityMultiplier, out string method)
    {
        method = string.Empty;
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            return false;
        }

        var applied = false;

        try
        {
            pawn.GravityScale = gravityMultiplier;
            pawn.GravityScaleUpdated();
            method = "Pawn.GravityScale";
            applied = true;
        }
        catch
        {
            // Continue with reflection-based fallbacks.
        }

        if (TrySetFloatPropertyWithUpdated(pawn, "GravityScale", gravityMultiplier))
        {
            method = string.IsNullOrWhiteSpace(method) ? "Pawn.GravityScale(reflection)" : method;
            applied = true;
        }

        if (TrySetFloatPropertyWithUpdated(pawn, "Gravity", gravityMultiplier))
        {
            method = string.IsNullOrWhiteSpace(method) ? "Pawn.Gravity(reflection)" : method;
            applied = true;
        }

        if (TrySetFloatPropertyWithUpdated(pawn, "GravityMultiplier", gravityMultiplier))
        {
            method = string.IsNullOrWhiteSpace(method) ? "Pawn.GravityMultiplier(reflection)" : method;
            applied = true;
        }

        if (target.Controller?.IsValid == true)
        {
            if (TrySetFloatPropertyWithUpdated(target.Controller, "GravityScale", gravityMultiplier))
            {
                method = string.IsNullOrWhiteSpace(method) ? "Controller.GravityScale(reflection)" : method;
                applied = true;
            }

            if (TrySetFloatPropertyWithUpdated(target.Controller, "Gravity", gravityMultiplier))
            {
                method = string.IsNullOrWhiteSpace(method) ? "Controller.Gravity(reflection)" : method;
                applied = true;
            }
        }

        if (applied)
        {
            var origin = pawn.AbsOrigin;
            var rotation = pawn.AbsRotation;
            if (origin != null && rotation != null)
            {
                pawn.Teleport(origin, rotation, pawn.AbsVelocity);
            }
        }

        return applied;
    }

    private string ResolveVisibleAdminName(IPlayer viewer, string adminName)
    {
        if (_tags.ShowAdminName)
        {
            return adminName;
        }

        var isAdminViewer =
            _core.Permission.PlayerHasPermission(viewer.SteamID, _permissions.AdminRoot) ||
            (!string.IsNullOrWhiteSpace(_permissions.AdminMenu) && _core.Permission.PlayerHasPermission(viewer.SteamID, _permissions.AdminMenu)) ||
            (!string.IsNullOrWhiteSpace(_permissions.ListPlayers) && _core.Permission.PlayerHasPermission(viewer.SteamID, _permissions.ListPlayers));

        return isAdminViewer ? adminName : "Admin";
    }

    private static string FormatTargetNames(IEnumerable<IPlayer> targets, int maxNames = 5)
    {
        var names = targets
            .Where(t => t.IsValid)
            .Select(t => t.Controller.PlayerName ?? $"#{t.PlayerID}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxNames + 1)
            .ToList();

        if (names.Count == 0)
        {
            return "-";
        }

        if (names.Count <= maxNames)
        {
            return string.Join(", ", names);
        }

        var visible = names.Take(maxNames);
        return $"{string.Join(", ", visible)}, ...";
    }

    private bool TryApplyBurnVisual(IPlayer target, int durationSeconds, bool allowMolotovFallback = false)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            return false;
        }

        var particleApplied = DispatchBurnParticles(pawn);
        var burnPaths = new List<string>();
        if (particleApplied)
        {
            burnPaths.Add("particle");
        }

        var duration = durationSeconds < 0 ? 60 : Math.Max(1, durationSeconds);
        var applied = particleApplied;

        try
        {
            pawn.AcceptInput<float>("IgniteLifetime", duration, pawn, pawn, 0);
            applied = true;
            burnPaths.Add("IgniteLifetime<float>");
        }
        catch
        {
            // Continue with additional runtime fallbacks below.
        }

        if (!applied)
        {
            try
            {
                pawn.AcceptInput<float>("Ignite", duration, pawn, pawn, 0);
                applied = true;
                burnPaths.Add("Ignite<float>");
            }
            catch
            {
                // Continue.
            }
        }

        if (!applied)
        {
            try
            {
                pawn.AcceptInput<string>("Ignite", string.Empty, pawn, pawn, 0);
                applied = true;
                burnPaths.Add("Ignite<string>");
            }
            catch
            {
                // Continue.
            }
        }

        if (!applied)
        {
            try
            {
                pawn.AcceptInput<string>("StartBurning", string.Empty, pawn, pawn, 0);
                applied = true;
                burnPaths.Add("StartBurning<string>");
            }
            catch
            {
                // Continue.
            }
        }

        if (!applied)
        {
            try
            {
                pawn.AcceptInput<float>("IgniteNumHitboxFires", 4f, pawn, pawn, 0);
                applied = true;
                burnPaths.Add("IgniteNumHitboxFires<float>");
            }
            catch
            {
                // Continue.
            }
        }

        if (!applied)
        {
            try
            {
                pawn.AcceptInput<string>("IgniteNumHitboxFires", "4", pawn, pawn, 0);
            }
            catch
            {
                // Runtime may not expose ignite inputs for this pawn type.
            }
        }

        if (!applied)
        {
            try
            {
                pawn.AcceptInput<string>("IgniteLifetime", duration.ToString(CultureInfo.InvariantCulture), pawn, pawn, 0);
                applied = true;
                burnPaths.Add("IgniteLifetime<string>");
            }
            catch
            {
                // Runtime may not expose ignite inputs for this pawn type.
            }
        }

        if (!applied)
        {
            if (_burnVisualWarnedPlayers.Add(target.PlayerID))
            {
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] burn visual not applied steamid={SteamId}; ignite/particle path unsupported on this runtime.", target.SteamID);
            }
        }
        else
        {
            _burnVisualWarnedPlayers.Remove(target.PlayerID);
            if (_burnVisualAppliedLoggedPlayers.Add(target.PlayerID))
            {
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] burn visual paths steamid={SteamId} paths={Paths}", target.SteamID, string.Join(",", burnPaths));
            }
        }

        return applied;
    }

    private bool TryApplyFlashOverlay(IPlayer target, float durationSeconds, float maxAlpha)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            return false;
        }

        var duration = Math.Clamp(durationSeconds, 0f, 5f);
        var alpha = Math.Clamp(maxAlpha, 0f, 255f);

        var durationApplied = false;
        var alphaApplied = false;

        try
        {
            pawn.FlashDuration = duration;
            pawn.FlashDurationUpdated();
            durationApplied = true;
        }
        catch
        {
            // Continue with fallbacks.
        }

        try
        {
            pawn.FlashMaxAlpha = alpha;
            pawn.FlashMaxAlphaUpdated();
            alphaApplied = true;
        }
        catch
        {
            // Continue with fallbacks.
        }

        durationApplied |=
            TrySetFloatPropertyWithUpdated(pawn, "FlashDuration", duration) ||
            TrySetFloatPropertyWithUpdated(pawn, "BlindDuration", duration);

        alphaApplied |=
            TrySetFloatPropertyWithUpdated(pawn, "FlashMaxAlpha", alpha) ||
            TrySetFloatPropertyWithUpdated(pawn, "BlindAlpha", alpha);

        // Some runtimes expose the blind window only via nested GameTime wrappers.
        // We hold the window open and clear it manually when the effect ends.
        var blindWindowApplied =
            TrySetNestedNumericPropertyWithUpdated(pawn, "BlindStartTime", 0f) |
            TrySetNestedNumericPropertyWithUpdated(pawn, "BlindUntilTime", duration > 0f ? 999999f : 0f);

        return durationApplied || alphaApplied || blindWindowApplied;
    }

    private void ClearFlashOverlay(IPlayer target)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            return;
        }

        try
        {
            pawn.FlashDuration = 0f;
            pawn.FlashDurationUpdated();
        }
        catch
        {
            // Continue with fallbacks.
        }

        try
        {
            pawn.FlashMaxAlpha = 0f;
            pawn.FlashMaxAlphaUpdated();
        }
        catch
        {
            // Continue with fallbacks.
        }

        TrySetFloatPropertyWithUpdated(pawn, "FlashDuration", 0f);
        TrySetFloatPropertyWithUpdated(pawn, "FlashMaxAlpha", 0f);
        TrySetFloatPropertyWithUpdated(pawn, "BlindDuration", 0f);
        TrySetFloatPropertyWithUpdated(pawn, "BlindAlpha", 0f);
        TrySetNestedNumericPropertyWithUpdated(pawn, "BlindStartTime", 0f);
        TrySetNestedNumericPropertyWithUpdated(pawn, "BlindUntilTime", 0f);
    }

    private void CaptureBurnVisualState(IPlayer target)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            return;
        }

        var playerId = target.PlayerID;
        if (_burnOriginalViewmodelOffsets.ContainsKey(playerId))
        {
            return;
        }

        _burnOriginalViewmodelOffsets[playerId] = (pawn.ViewmodelOffsetX, pawn.ViewmodelOffsetY, pawn.ViewmodelOffsetZ);
    }

    private void ApplyBurnScreenEffect(IPlayer target)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            return;
        }

        var playerId = target.PlayerID;
        var baseOffsets = _burnOriginalViewmodelOffsets.TryGetValue(playerId, out var savedOffsets)
            ? savedOffsets
            : (X: pawn.ViewmodelOffsetX, Y: pawn.ViewmodelOffsetY, Z: pawn.ViewmodelOffsetZ);

        var offsetX = baseOffsets.X + (float)(Random.Shared.NextDouble() * 1.6 - 0.8);
        var offsetY = baseOffsets.Y + (float)(Random.Shared.NextDouble() * 1.4 - 0.7);
        var offsetZ = baseOffsets.Z + (float)(Random.Shared.NextDouble() * 2.4 - 1.2);

        TrySetFloatPropertyWithUpdated(pawn, "ViewmodelOffsetX", offsetX);
        TrySetFloatPropertyWithUpdated(pawn, "ViewmodelOffsetY", offsetY);
        TrySetFloatPropertyWithUpdated(pawn, "ViewmodelOffsetZ", offsetZ);
        TryApplyAimPunch(
            pawn,
            new QAngle((float)(Random.Shared.NextDouble() * 2.6 + 0.8), (float)(Random.Shared.NextDouble() * 3.0 - 1.5), 0f),
            new QAngle((float)(Random.Shared.NextDouble() * 10.0 + 4.0), (float)(Random.Shared.NextDouble() * 8.0 - 4.0), 0f));
        ApplyCameraJolt(
            target,
            (float)(Random.Shared.NextDouble() * 2.0 - 1.0),
            (float)(Random.Shared.NextDouble() * 2.4 - 1.2),
            (float)(Random.Shared.NextDouble() * 12.0 - 6.0));
    }

    private void RestoreBurnVisualState(int playerId)
    {
        var target = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.PlayerID == playerId);
        var pawn = target?.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            _burnOriginalViewmodelOffsets.Remove(playerId);
            return;
        }

        if (_burnOriginalViewmodelOffsets.TryGetValue(playerId, out var offsets))
        {
            TrySetFloatPropertyWithUpdated(pawn, "ViewmodelOffsetX", offsets.X);
            TrySetFloatPropertyWithUpdated(pawn, "ViewmodelOffsetY", offsets.Y);
            TrySetFloatPropertyWithUpdated(pawn, "ViewmodelOffsetZ", offsets.Z);
        }

        TryApplyAimPunch(pawn, new QAngle(0f, 0f, 0f), new QAngle(0f, 0f, 0f));
        if (target != null)
        {
            ResetCameraRoll(target);
        }
        _burnOriginalViewmodelOffsets.Remove(playerId);
    }

    private bool DispatchBurnParticles(CBasePlayerPawn pawn)
    {
        var dispatched = false;

        try
        {
            var recipients = _core.PlayerManager
                .GetAllPlayers()
                .Where(p => p.IsValid && !p.IsFakeClient)
                .Select(p => p.PlayerID)
                .Distinct()
                .ToArray();

            if (recipients.Length == 0)
            {
                return false;
            }

            var filter = CRecipientFilter.FromPlayers(recipients);
            var attachName = new CUtlSymbolLarge { Value = string.Empty };

            foreach (var particleName in BurnParticleNames)
            {
                try
                {
                    _core.Engine.DispatchParticleEffect(
                        particleName,
                        ParticleAttachment_t.PATTACH_ABSORIGIN_FOLLOW,
                        0,
                        attachName,
                        filter,
                        false,
                        0,
                        pawn);
                    dispatched = true;
                }
                catch
                {
                    // Try next particle candidate.
                }
            }
        }
        catch
        {
            // Non-fatal visual path.
        }

        return dispatched;
    }

    private bool TryApplyAimPunch(CCSPlayerPawn pawn, QAngle angle, QAngle velocity)
    {
        var applied = false;

        try
        {
            pawn.AimPunchAngle = angle;
            applied = true;
        }
        catch
        {
            // Continue with reflection fallback.
        }

        try
        {
            pawn.AimPunchAngleVel = velocity;
            applied = true;
        }
        catch
        {
            // Continue with reflection fallback.
        }

        applied |= TrySetQAnglePropertyWithUpdated(pawn, "AimPunchAngle", angle);
        applied |= TrySetQAnglePropertyWithUpdated(pawn, "AimPunchAngleVel", velocity);
        return applied;
    }

    private void ApplyCameraJolt(IPlayer target, float pitchDelta, float yawDelta, float roll)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            return;
        }

        var origin = pawn.AbsOrigin;
        var rotation = pawn.AbsRotation;
        if (origin == null || rotation == null)
        {
            return;
        }

        var currentRotation = rotation.Value;
        pawn.Teleport(
            origin,
            new QAngle(currentRotation.X + pitchDelta, currentRotation.Y + yawDelta, roll),
            pawn.AbsVelocity);
    }

    private void ResetCameraRoll(IPlayer target)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            return;
        }

        var origin = pawn.AbsOrigin;
        var rotation = pawn.AbsRotation;
        if (origin == null || rotation == null)
        {
            return;
        }

        var currentRotation = rotation.Value;
        pawn.Teleport(
            origin,
            new QAngle(currentRotation.X, currentRotation.Y, 0f),
            pawn.AbsVelocity);
    }

    private void TrySpawnMolotovBurnVisual(IPlayer target)
    {
        var targetSteamId = target.SteamID;
        _core.Scheduler.NextTick(() =>
        {
            try
            {
                var liveTarget = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                var pawn = liveTarget?.PlayerPawn;
                if (pawn?.IsValid != true)
                {
                    return;
                }

                var origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
                var rotation = pawn.AbsRotation ?? new QAngle(89f, 0f, 0f);
                var team = pawn.Team;
                if (liveTarget?.Controller?.TeamNum == 3)
                {
                    team = Team.CT;
                }
                else
                {
                    team = Team.T;
                }

                CMolotovProjectile.EmitGrenade(
                    new Vector(origin.X, origin.Y, origin.Z + 96f),
                    new QAngle(89f, rotation.Y, 0f),
                    new Vector(0f, 0f, -1200f),
                    team,
                    pawn);
            }
            catch (Exception ex)
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Burn visual fallback (molotov) failed for steamid={SteamId}: {Message}", targetSteamId, ex.Message);
            }
        });
    }

    private bool ShouldSpawnBurnMolotovFallback(int playerId)
    {
        var now = DateTime.UtcNow;
        if (_burnFallbackMolotovLastSpawnUtc.TryGetValue(playerId, out var lastSpawnUtc))
        {
            if ((now - lastSpawnUtc).TotalSeconds < 2.5)
            {
                return false;
            }
        }

        _burnFallbackMolotovLastSpawnUtc[playerId] = now;
        return true;
    }

    private bool TrySetViewmodelFov(CCSPlayerPawn pawn, float value)
    {
        try
        {
            pawn.ViewmodelFOV = value;
            pawn.ViewmodelFOVUpdated();
            return true;
        }
        catch
        {
            // Continue with reflection fallbacks.
        }

        return TrySetFloatPropertyWithUpdated(pawn, "ViewmodelFOV", value)
            || TrySetFloatPropertyWithUpdated(pawn, "ViewModelFOV", value);
    }

    private bool TrySetViewmodelFov(IPlayer target, float value)
    {
        var applied = false;

        var pawn = target.PlayerPawn;
        if (pawn?.IsValid == true)
        {
            applied |= TrySetViewmodelFov(pawn, value);
        }

        var controller = target.Controller;
        if (controller?.IsValid == true)
        {
            applied |= TrySetFloatPropertyWithUpdated(controller, "FOV", value);
            applied |= TrySetFloatPropertyWithUpdated(controller, "Fov", value);
            applied |= TrySetFloatPropertyWithUpdated(controller, "DefaultFOV", value);
            applied |= TrySetFloatPropertyWithUpdated(controller, "DesiredFOV", value);
            applied |= TrySetFloatPropertyWithUpdated(controller, "ViewmodelFOV", value);
            applied |= TrySetFloatPropertyWithUpdated(controller, "ViewModelFOV", value);
        }

        return applied;
    }

    private void LogVisualProbeOnce(IPlayer target, string effectName, HashSet<int> seenPlayers)
    {
        if (!seenPlayers.Add(target.PlayerID))
        {
            return;
        }

        _core.Logger.LogInformationIfEnabled(
            "[CS2_Admin][Debug] {Effect} probe steamid={SteamId} playerId={PlayerId} pawnType={PawnType} controllerType={ControllerType} snapshot={Snapshot}",
            effectName,
            target.SteamID,
            target.PlayerID,
            target.PlayerPawn?.GetType().FullName ?? "null",
            target.Controller?.GetType().FullName ?? "null",
            BuildVisualSnapshot(target));

        LogMemberMatches(target.PlayerPawn, effectName, "pawn");
        LogMemberMatches(target.Controller, effectName, "controller");
    }

    private void LogMemberMatches(object? instance, string effectName, string scope)
    {
        if (instance == null)
        {
            return;
        }

        try
        {
            var type = instance.GetType();
            var members = new List<string>();

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!IsVisualCandidateName(property.Name))
                {
                    continue;
                }

                object? value = null;
                try
                {
                    value = property.GetValue(instance);
                }
                catch
                {
                    value = "<unreadable>";
                }

                members.Add($"{property.Name}={FormatVisualValue(value)}");
            }

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!IsVisualCandidateName(field.Name))
                {
                    continue;
                }

                object? value = null;
                try
                {
                    value = field.GetValue(instance);
                }
                catch
                {
                    value = "<unreadable>";
                }

                members.Add($"{field.Name}={FormatVisualValue(value)}");
            }

            var payload = members.Count == 0 ? "<none>" : string.Join(", ", members.Distinct().Take(32));
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] {Effect} member probe {Scope}={Payload}", effectName, scope, payload);
        }
        catch (Exception ex)
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] {Effect} member probe {Scope} failed: {Message}", effectName, scope, ex.Message);
        }
    }

    private static bool IsVisualCandidateName(string memberName)
    {
        return memberName.Contains("FOV", StringComparison.OrdinalIgnoreCase)
            || memberName.Contains("View", StringComparison.OrdinalIgnoreCase)
            || memberName.Contains("Flash", StringComparison.OrdinalIgnoreCase)
            || memberName.Contains("Blind", StringComparison.OrdinalIgnoreCase)
            || memberName.Contains("Punch", StringComparison.OrdinalIgnoreCase)
            || memberName.Contains("Roll", StringComparison.OrdinalIgnoreCase)
            || memberName.Contains("Yaw", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildVisualSnapshot(IPlayer target)
    {
        try
        {
            var pawn = target.PlayerPawn;
            var controller = target.Controller;
            var pawnFov = pawn?.IsValid == true ? pawn.ViewmodelFOV.ToString(CultureInfo.InvariantCulture) : "null";
            var pawnRot = pawn?.IsValid == true && pawn.AbsRotation != null
                ? $"{pawn.AbsRotation.Value.X.ToString("0.##", CultureInfo.InvariantCulture)}/{pawn.AbsRotation.Value.Y.ToString("0.##", CultureInfo.InvariantCulture)}/{pawn.AbsRotation.Value.Z.ToString("0.##", CultureInfo.InvariantCulture)}"
                : "null";
            var controllerFov = controller?.IsValid == true ? ReadVisualMemberValue(controller, "FOV", "DefaultFOV", "DesiredFOV", "ViewmodelFOV", "ViewModelFOV") : "null";
            return $"pawnFov={pawnFov};controllerFov={controllerFov};rot={pawnRot}";
        }
        catch (Exception ex)
        {
            return $"snapshot_error={ex.Message}";
        }
    }

    private static string ReadVisualMemberValue(object instance, params string[] memberNames)
    {
        foreach (var memberName in memberNames)
        {
            try
            {
                var type = instance.GetType();
                var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    return FormatVisualValue(property.GetValue(instance));
                }

                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return FormatVisualValue(field.GetValue(instance));
                }
            }
            catch
            {
                // Ignore and continue probing.
            }
        }

        return "n/a";
    }

    private static string FormatVisualValue(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is float floatValue)
        {
            return floatValue.ToString("0.##", CultureInfo.InvariantCulture);
        }

        if (value is double doubleValue)
        {
            return doubleValue.ToString("0.##", CultureInfo.InvariantCulture);
        }

        return value.ToString() ?? string.Empty;
    }

    private static bool TryParseFloat(string input, out float value)
    {
        return float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static string NormalizeItemName(string input)
    {
        var normalized = input.Trim();
        if (normalized.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("item_", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return $"weapon_{normalized}";
    }

    private bool TrySetPlayerScale(IPlayer player, float scale)
    {
        var applied = false;
        var pawn = player.PlayerPawn;
        if (pawn?.IsValid == true)
        {
            try
            {
                pawn.SetScale(scale);
                applied = true;
            }
            catch
            {
                // Runtime may not expose SetScale for this pawn type; fallback to reflection below.
            }

            applied |= TrySetFloatPropertyWithUpdated(pawn, "Scale", scale);
            applied |= TrySetFloatPropertyWithUpdated(pawn, "ModelScale", scale);
        }

        if (player.Controller?.IsValid == true)
        {
            applied |= TrySetFloatPropertyWithUpdated(player.Controller, "Scale", scale);
            applied |= TrySetFloatPropertyWithUpdated(player.Controller, "ModelScale", scale);
        }

        return applied;
    }

    private static bool TrySetFloatPropertyWithUpdated(object instance, string propertyName, float value)
    {
        try
        {
            var type = instance.GetType();
            var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
            {
                object converted = property.PropertyType == typeof(float)
                    ? value
                    : Convert.ChangeType(value, property.PropertyType, CultureInfo.InvariantCulture);
                property.SetValue(instance, converted);

                var updated = type.GetMethod($"{propertyName}Updated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                updated?.Invoke(instance, null);
                return true;
            }

            var field = type.GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && !field.IsInitOnly)
            {
                object converted = field.FieldType == typeof(float)
                    ? value
                    : Convert.ChangeType(value, field.FieldType, CultureInfo.InvariantCulture);
                field.SetValue(instance, converted);

                var updated = type.GetMethod($"{propertyName}Updated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                updated?.Invoke(instance, null);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetQAnglePropertyWithUpdated(object instance, string propertyName, QAngle value)
    {
        try
        {
            var type = instance.GetType();
            var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite && property.PropertyType == typeof(QAngle))
            {
                property.SetValue(instance, value);
                var updated = type.GetMethod($"{propertyName}Updated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                updated?.Invoke(instance, null);
                return true;
            }

            var field = type.GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && !field.IsInitOnly && field.FieldType == typeof(QAngle))
            {
                field.SetValue(instance, value);
                var updated = type.GetMethod($"{propertyName}Updated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                updated?.Invoke(instance, null);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetNestedNumericPropertyWithUpdated(object owner, string propertyName, float value)
    {
        try
        {
            var ownerType = owner.GetType();

            object? nested = null;
            var property = ownerType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                nested = property.GetValue(owner);
            }
            else
            {
                var field = ownerType.GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                nested = field?.GetValue(owner);
            }

            if (nested == null)
            {
                return false;
            }

            var nestedType = nested.GetType();
            var applied = false;

            foreach (var nestedProperty in nestedType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!nestedProperty.CanWrite || !IsSupportedNumericType(nestedProperty.PropertyType))
                {
                    continue;
                }

                try
                {
                    nestedProperty.SetValue(nested, ConvertNumericValue(value, nestedProperty.PropertyType));
                    applied = true;
                }
                catch
                {
                    // Try next candidate.
                }
            }

            foreach (var nestedField in nestedType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (nestedField.IsInitOnly || !IsSupportedNumericType(nestedField.FieldType))
                {
                    continue;
                }

                try
                {
                    nestedField.SetValue(nested, ConvertNumericValue(value, nestedField.FieldType));
                    applied = true;
                }
                catch
                {
                    // Try next candidate.
                }
            }

            if (!applied)
            {
                return false;
            }

            var updated = ownerType.GetMethod($"{propertyName}Updated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            updated?.Invoke(owner, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSupportedNumericType(Type type)
    {
        var targetType = Nullable.GetUnderlyingType(type) ?? type;
        return targetType == typeof(float)
            || targetType == typeof(double)
            || targetType == typeof(int)
            || targetType == typeof(uint)
            || targetType == typeof(long)
            || targetType == typeof(ulong)
            || targetType == typeof(short)
            || targetType == typeof(ushort);
    }

    private static object ConvertNumericValue(float value, Type targetType)
    {
        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return Convert.ChangeType(value, nonNullableType, CultureInfo.InvariantCulture);
    }

    private bool HasPermission(ICommandContext context, string permission)
    {
        if (!context.IsSentByPlayer)
            return true;

        var steamId = context.Sender!.SteamID;
        return _core.Permission.PlayerHasPermission(steamId, permission)
               || _core.Permission.PlayerHasPermission(steamId, _permissions.AdminRoot);
    }

    private bool CanTarget(ICommandContext context, IPlayer target, bool allowSelf = false)
    {
        if (!context.IsSentByPlayer || context.Sender == null)
        {
            return true;
        }

        if (context.Sender.SteamID == target.SteamID)
        {
            if (allowSelf)
            {
                return true;
            }

            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cannot_target_self"]}");
            return false;
        }

        var adminImm = _adminDbManager.GetEffectiveImmunityAsync(context.Sender.SteamID).GetAwaiter().GetResult();
        var targetImm = _adminDbManager.GetEffectiveImmunityAsync(target.SteamID).GetAwaiter().GetResult();
        if (targetImm >= adminImm && targetImm > 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cannot_target_immunity"]}");
            return false;
        }

        return true;
    }

    private List<IPlayer> FilterTargetsByCanTarget(ICommandContext context, IEnumerable<IPlayer> targets, bool allowSelf = false)
    {
        var result = new List<IPlayer>();
        foreach (var target in targets)
        {
            if (CanTarget(context, target, allowSelf))
            {
                result.Add(target);
            }
        }

        return result;
    }

}


