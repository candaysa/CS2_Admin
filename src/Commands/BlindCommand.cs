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

public class BlindCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public BlindCommand(
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
        var args = NormalizeArgs(context.Args, CommandsConfig.Blind);

        if (!HasPerm(context, Permissions.Blind))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 2 || !int.TryParse(args[1], out var parsedDuration))
        {
            Reply(context, "blind_usage");
            return;
        }

        var durationSeconds = Math.Clamp(parsedDuration, 1, 60);

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

                ApplyBlindEffect(liveTarget, durationSeconds);

                Core.Scheduler.DelayBySeconds(durationSeconds, () =>
                {
                    var sameTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                    if (sameTarget?.IsValid == true)
                        ClearBlindEffect(sameTarget);
                });
            });
        }

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        if (targets.Count == 1)
            BroadcastNotification(adminName, "blind_notification_single", targets[0].Controller.PlayerName, durationSeconds);
        else
            BroadcastNotification(adminName, "blind_notification_multiple", targets.Count, durationSeconds);

        AdminLogManager.AddLogAsync("blind", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targets.Count};duration={durationSeconds}");
        Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} blinded {Count} player(s) for {Duration}s", adminName, targets.Count, durationSeconds);
    }

    private void ApplyBlindEffect(IPlayer target, float holdSeconds)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
            return;

        try
        {
            pawn.FlashDuration = holdSeconds;
            pawn.FlashDurationUpdated();
            pawn.FlashMaxAlpha = 255f;
            pawn.FlashMaxAlphaUpdated();
        }
        catch
        {
            try
            {
                var flashProp = pawn.GetType().GetProperty("FlashDuration");
                flashProp?.SetValue(pawn, holdSeconds);
                var alphaProp = pawn.GetType().GetProperty("FlashMaxAlpha");
                alphaProp?.SetValue(pawn, 255f);
            }
            catch { }
        }
    }

    private void ClearBlindEffect(IPlayer target)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
            return;

        try
        {
            pawn.FlashDuration = 0f;
            pawn.FlashDurationUpdated();
            pawn.FlashMaxAlpha = 0f;
            pawn.FlashMaxAlphaUpdated();
        }
        catch
        {
            try
            {
                var flashProp = pawn.GetType().GetProperty("FlashDuration");
                flashProp?.SetValue(pawn, 0f);
                var alphaProp = pawn.GetType().GetProperty("FlashMaxAlpha");
                alphaProp?.SetValue(pawn, 0f);
            }
            catch { }
        }
    }
}

