using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;
using System.Drawing;

namespace CS2_Admin.Commands;

public class BurnCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly HashSet<int> _burnPlayers = new();

    public BurnCommand(
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
        var args = NormalizeArgs(context.Args, CommandsConfig.Burn);

        if (!HasPerm(context, Permissions.Burn))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 1)
        {
            Reply(context, "burn_usage");
            return;
        }

        var durationSeconds = 8;
        var isInfinite = false;
        if (args.Length > 1 && int.TryParse(args[1], out var parsedDuration))
        {
            if (parsedDuration == -1)
                isInfinite = true;
            else
                durationSeconds = Math.Clamp(parsedDuration, 1, 60);
        }

        var damagePerTick = 5;
        if (args.Length > 2 && int.TryParse(args[2], out var parsedDamage))
            damagePerTick = Math.Clamp(parsedDamage, 1, 100);

        var targets = PlayerUtils.FindPlayersByTarget(Core, args[0], includeDeadPlayers: false, caller: context.Sender)
            .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
            .ToList();
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

        foreach (var target in targets)
        {
            var targetSteamId = target.SteamID;
            Core.Scheduler.NextTick(() =>
            {
                var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                if (liveTarget?.IsValid != true)
                    return;

                var pawn = liveTarget.PlayerPawn;
                if (pawn?.IsValid != true || pawn.Health <= 0)
                    return;

                _burnPlayers.Add(liveTarget.PlayerID);
                StartBurnEffect(liveTarget, isInfinite ? null : durationSeconds, damagePerTick);
            });
        }

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        var durationLabel = isInfinite ? L("permanent") : durationSeconds.ToString();

        BroadcastNotification(adminName, "burn_notification", targets.Count, durationLabel, damagePerTick);

        AdminLogManager.AddLogAsync("burn", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targets.Count};duration={(isInfinite ? "infinite" : durationSeconds.ToString())};dmg={damagePerTick}");
        Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} set {Count} player(s) on fire", adminName, targets.Count);
    }

    private void StartBurnEffect(IPlayer player, int? durationSeconds, int damagePerTick)
    {
        var pawn = player.PlayerPawn;
        if (pawn?.IsValid != true)
            return;

        var ticks = durationSeconds ?? 30;
        var interval = 1.0f;
        var tickCount = 0;

        void BurnTick()
        {
            if (tickCount >= ticks || !player.IsValid || pawn?.Health <= 0)
            {
                _burnPlayers.Remove(player.PlayerID);
                return;
            }

            if (pawn?.IsValid == true && pawn.Health > 0)
            {
                pawn.Health = Math.Max(pawn.Health - damagePerTick, 0);
                pawn.HealthUpdated();

                if (pawn.Health <= 0)
                {
                    pawn.CommitSuicide(false, false);
                    _burnPlayers.Remove(player.PlayerID);
                    return;
                }
            }

            tickCount++;
            Core.Scheduler.DelayBySeconds(interval, BurnTick);
        }

        Core.Scheduler.DelayBySeconds(interval, BurnTick);
    }
}

