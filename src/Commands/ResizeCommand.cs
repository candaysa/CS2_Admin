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

public class ResizeCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public ResizeCommand(
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
        var args = NormalizeArgs(context.Args, CommandsConfig.Resize);

        if (!HasPerm(context, Permissions.Resize))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 2 || !float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale))
        {
            Reply(context, "resize_usage");
            return;
        }

        scale = Math.Clamp(scale, 0.2f, 3.0f);

        var targets = PlayerUtils.FindPlayersByTarget(Core, args[0], includeDeadPlayers: true, caller: context.Sender);
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
            if (TrySetPlayerScale(target, scale))
                applied++;
        }

        if (applied == 0)
        {
            Reply(context, "resize_not_supported");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        var prefix = L("prefix");

        BroadcastNotification(adminName, "resize_notification", applied, scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

        AdminLogManager.AddLogAsync("resize", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={applied};scale={scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
        Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} resized {Count} player(s) to {Scale}", adminName, applied, scale);
    }

    private bool TrySetPlayerScale(IPlayer player, float scale)
    {
        var pawn = player.PlayerPawn;
        if (pawn?.IsValid != true)
            return false;

        try
        {
            pawn.SetScale(scale);
            return true;
        }
        catch
        {
            try
            {
                var prop = pawn.GetType().GetProperty("ModelScale");
                if (prop != null)
                {
                    prop.SetValue(pawn, scale);
                    pawn.HealthUpdated();
                    return true;
                }
            }
            catch { }
        }

        return false;
    }
}

