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

        if (args.Length < 2 || !float.TryParse(args[1], out var multiplier))
        {
            Reply(context, "speed_usage");
            return;
        }

        multiplier = Math.Clamp(multiplier, 0.1f, 10.0f);

        var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
        if (target == null)
        {
            Reply(context, "player_not_found");
            return;
        }

        var canTarget = PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, target.SteamID, allowSelf: true)
            .GetAwaiter().GetResult();
        if (!canTarget)
            return;

        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            Reply(context, "player_not_found");
            return;
        }

        var playerId = target.PlayerID;

        if (Math.Abs(multiplier - 1.0f) < 0.01f)
        {
            _speedOverrides.Remove(playerId);
        }
        else
        {
            _speedOverrides[playerId] = multiplier;
            StartSpeedEnforcer(target.SteamID, playerId, multiplier);
        }

        pawn.VelocityModifier = multiplier;

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        var targetName = target.Controller.PlayerName;

        BroadcastNotification(adminName, "speed_notification", targetName, multiplier.ToString("0.##"));

        PlayerUtils.SendNotification(target, Messages,
            L("speed_personal_html", multiplier.ToString("0.##")),
            $" \x02{L("prefix")}\x01 {L("speed_personal_chat", multiplier.ToString("0.##"))}");

        AdminLogManager.AddLogAsync("speed", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"multiplier={multiplier.ToString("0.00")}", targetName);
        Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} set speed of {Target} to {Multiplier}", adminName, targetName, multiplier);
    }

    private void StartSpeedEnforcer(ulong steamId, int playerId, float multiplier)
    {
        void Enforce()
        {
            if (!_speedOverrides.TryGetValue(playerId, out var currentMultiplier) || Math.Abs(currentMultiplier - multiplier) > 0.001f)
                return;

            var player = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == steamId);
            if (player?.PlayerPawn?.IsValid != true)
            {
                _speedOverrides.Remove(playerId);
                return;
            }

            player.PlayerPawn.VelocityModifier = multiplier;
            Core.Scheduler.DelayBySeconds(0.1f, Enforce);
        }

        Core.Scheduler.DelayBySeconds(0.1f, Enforce);
    }
}
