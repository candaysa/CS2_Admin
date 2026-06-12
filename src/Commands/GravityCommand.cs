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

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Gravity);

            if (!HasPerm(context, Permissions.Gravity))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 2 || !float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale))
            {
                Reply(context, "gravity_usage");
                return;
            }

            scale = Math.Clamp(scale, 0.1f, 10.0f);

            var targets = PlayerUtils.FindPlayersByTarget(Core, args[0], includeDeadPlayers: false, caller: context.Sender);
            if (targets.Count == 0)
            {
                Reply(context, "no_valid_targets");
                return;
            }

            targets = await PlayerUtils.FilterTargetsByAccessAsync(Core, _adminDbManager, context, targets, allowSelf: true);
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
                    try { pawn.GravityScale = scale; } catch (Exception ex) { Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] GravityScale fallback failed for {SteamId}", target.SteamID); }
                }

                applied++;

                PlayerUtils.SendNotification(target, Messages,
                    $"<font color='#ffd700'><b>{L("gravity")}</b></font><br><br>{L("label_value")}: <font color='#ffd700'>{scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}x</font>",
                    $" \x02{L("prefix")}\x01 {L("gravity_personal_chat", scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))}");
            }

            if (applied == 0)
            {
                Reply(context, "no_valid_targets");
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");

            var targetLabel = FormatTargetName(targets);
            BroadcastNotification(adminName, "gravity_notification", targetLabel, scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

            _ = AdminLogManager.AddLogAsync("gravity", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={applied};scale={scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
            Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} set gravity of {Count} player(s) to {Scale}", adminName, applied, scale);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Gravity command failed");
        }
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
                try { player.PlayerPawn.GravityScale = scale; } catch (Exception ex) { Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] GravityScale enforcer fallback failed for {SteamId}", player.SteamID); }
            }
            Core.Scheduler.DelayBySeconds(0.1f, Enforce);
        }

        Core.Scheduler.DelayBySeconds(0.1f, Enforce);
    }
}
