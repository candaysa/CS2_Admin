using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2_Admin.Commands;

public class SpeedCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly Dictionary<int, float> _speedOverrides = new();

    public SpeedCommand(
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
        var args = NormalizeArgs(context.Args, CommandsConfig.Speed);

        if (!HasPerm(context, Permissions.Speed))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 2 || !float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var multiplier))
        {
            Reply(context, "speed_usage");
            return;
        }

        multiplier = Math.Clamp(multiplier, 0.1f, 10.0f);

        var targets = PlayerUtils.FindPlayersByTarget(Core, args[0], includeDeadPlayers: false, caller: context.Sender);
        if (targets.Count == 0)
        {
            Reply(context, "no_valid_targets");
            return;
        }

        targets = PlayerUtils.FilterTargetsByAccessAsync(Core, _adminDbManager, context, targets, allowSelf: true)
            .GetAwaiter().GetResult();
        if (targets.Count == 0)
        {
            Reply(context, "no_valid_targets");
            return;
        }

        var applied = 0;
        foreach (var target in targets)
        {
            var pawn = target.PlayerPawn;
            if (pawn?.IsValid != true)
                continue;

            var playerId = target.PlayerID;

            if (Math.Abs(multiplier - 1.0f) < 0.01f)
            {
                // Remove override; enforcer loop will stop on its own
                _speedOverrides.Remove(playerId);
            }
            else
            {
                // Update the override value. If an enforcer is already running for this player,
                // the old one will detect the value change and stop automatically.
                _speedOverrides[playerId] = multiplier;
                StartSpeedEnforcer(target.SteamID, playerId, multiplier);
            }

            try
            {
                pawn.VelocityModifier = multiplier;
            }
            catch { }

            applied++;

            PlayerUtils.SendNotification(target, Messages,
                $"<font color='#00ff88'><b>SPEED</b></font><br><br>Değer: <font color='#00ff88'>{multiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}x</font>",
                $" \x02{L("prefix")}\x01 {L("speed_personal_chat", multiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))}");
        }

        if (applied == 0)
        {
            Reply(context, "no_valid_targets");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");

        string targetLabel = targets.Count == 1 ? targets[0].Controller.PlayerName : applied.ToString();
        BroadcastNotification(adminName, "speed_notification", targetLabel, multiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

        AdminLogManager.AddLogAsync("speed", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={applied};multiplier={multiplier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
        Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} set speed of {Count} player(s) to {Multiplier}", adminName, applied, multiplier);
    }

    private void StartSpeedEnforcer(ulong steamId, int playerId, float multiplier)
    {
        void Enforce()
        {
            // Stop if the override was removed or changed to a different value (another call superseded us)
            if (!_speedOverrides.TryGetValue(playerId, out var currentMultiplier) || Math.Abs(currentMultiplier - multiplier) > 0.001f)
                return;

            var player = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == steamId);
            if (player?.PlayerPawn?.IsValid != true)
            {
                _speedOverrides.Remove(playerId);
                return;
            }

            try
            {
                player.PlayerPawn.VelocityModifier = multiplier;
            }
            catch { }

            Core.Scheduler.NextTick(Enforce);
        }

        Core.Scheduler.NextTick(Enforce);
    }
}
