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

public class GlowCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public GlowCommand(
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
        var args = NormalizeArgs(context.Args, CommandsConfig.Glow);

        if (!HasPerm(context, Permissions.Glow))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 2)
        {
            Reply(context, "glow_usage");
            return;
        }

        var disableGlow = args[1].Equals("off", StringComparison.OrdinalIgnoreCase);
        var r = 255;
        var g = 255;
        var b = 255;
        var a = 180;

        if (!disableGlow)
        {
            if (args.Length < 4 ||
                !int.TryParse(args[1], out r) ||
                !int.TryParse(args[2], out g) ||
                !int.TryParse(args[3], out b))
            {
                Reply(context, "glow_usage");
                return;
            }

            if (args.Length > 4 && !int.TryParse(args[4], out a))
            {
                Reply(context, "glow_usage");
                return;
            }

            r = Math.Clamp(r, 0, 255);
            g = Math.Clamp(g, 0, 255);
            b = Math.Clamp(b, 0, 255);
            a = Math.Clamp(a, 0, 255);
        }

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

                if (disableGlow)
                    ClearGlow(liveTarget);
                else
                    ApplyGlow(liveTarget, r, g, b, a);
            });
        }

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");

        if (disableGlow)
        {
            if (targets.Count == 1)
                BroadcastNotification(adminName, "glow_off_notification_single", targets[0].Controller.PlayerName);
            else
                BroadcastNotification(adminName, "glow_off_notification_multiple", targets.Count);
        }
        else
        {
            if (targets.Count == 1)
                BroadcastNotification(adminName, "glow_notification_single", targets[0].Controller.PlayerName);
            else
                BroadcastNotification(adminName, "glow_notification_multiple", targets.Count);
        }

        var details = disableGlow
            ? $"targets={targets.Count};off=true"
            : $"targets={targets.Count};rgba={r},{g},{b},{a}";

        AdminLogManager.AddLogAsync("glow", adminName, context.Sender?.SteamID ?? 0, null, null, details);
        Core.Logger.LogInformation("[CS2_Admin] {Admin} set glow for {Count} player(s)", adminName, targets.Count);
    }

    private void ApplyGlow(IPlayer target, int r, int g, int b, int a)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
            return;

        pawn.Render = new(r, g, b, 255);
        pawn.RenderUpdated();
    }

    private void ClearGlow(IPlayer target)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
            return;

        pawn.Render = new(255, 255, 255, 255);
        pawn.RenderUpdated();
    }
}

