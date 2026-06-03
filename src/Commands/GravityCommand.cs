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

public class GravityCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly Dictionary<int, float> _gravityOverrides = new();

    public GravityCommand(
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
        var args = NormalizeArgs(context.Args, CommandsConfig.Gravity);

        if (!HasPerm(context, Permissions.Gravity))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 2 || !float.TryParse(args[1], out var scale))
        {
            Reply(context, "gravity_usage");
            return;
        }

        scale = Math.Clamp(scale, 0.1f, 10.0f);

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

        if (Math.Abs(scale - 1.0f) < 0.01f)
        {
            _gravityOverrides.Remove(playerId);
        }
        else
        {
            _gravityOverrides[playerId] = scale;
            StartGravityEnforcer(target.SteamID, playerId, scale);
        }

        try
        {
            pawn.GravityScale = scale;
            pawn.GravityScaleUpdated();
        }
        catch
        {
            try { pawn.GravityScale = scale; } catch { }
        }

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        var targetName = target.Controller.PlayerName;

        BroadcastNotification(adminName, "gravity_notification", targetName, scale.ToString("0.##"));

        PlayerUtils.SendNotification(target, Messages,
            L("gravity_personal_html", scale.ToString("0.##")),
            $" \x02{L("prefix")}\x01 {L("gravity_personal_chat", scale.ToString("0.##"))}");

        AdminLogManager.AddLogAsync("gravity", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"scale={scale.ToString("0.00")}", targetName);
        Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} set gravity of {Target} to {Scale}", adminName, targetName, scale);
    }

    private void StartGravityEnforcer(ulong steamId, int playerId, float scale)
    {
        void Enforce()
        {
            if (!_gravityOverrides.TryGetValue(playerId, out var currentScale) || Math.Abs(currentScale - scale) > 0.001f)
                return;

            var player = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == steamId);
            if (player?.PlayerPawn?.IsValid != true)
            {
                _gravityOverrides.Remove(playerId);
                return;
            }

            try
            {
                player.PlayerPawn.GravityScale = scale;
                player.PlayerPawn.GravityScaleUpdated();
            }
            catch
            {
                try { player.PlayerPawn.GravityScale = scale; } catch { }
            }
            Core.Scheduler.DelayBySeconds(0.1f, Enforce);
        }

        Core.Scheduler.DelayBySeconds(0.1f, Enforce);
    }
}
