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

namespace CS2_Admin.Commands;

public class UnburyCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public UnburyCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.Unbury);

            if (!HasPerm(context, Permissions.Unbury))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "unbury_usage");
                return;
            }

            var targets = PlayerUtils.FindPlayersByTarget(Core, args[0], includeDeadPlayers: false, caller: context.Sender)
                .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
                .ToList();

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

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            float depth = 30.0f; // Distance to push up

            foreach (var target in targets)
            {
                var targetSteamId = target.SteamID;
                Core.Scheduler.NextTick(() =>
                {
                    var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                    var pawn = liveTarget?.PlayerPawn;

                    if (pawn?.IsValid == true && pawn.Health > 0)
                    {
                        var origin = pawn.AbsOrigin;
                        if (origin != null)
                        {
                            var newPos = new Vector(origin.Value.X, origin.Value.Y, origin.Value.Z + depth);
                            pawn.Teleport(newPos, pawn.AbsRotation, pawn.AbsVelocity);
                        }
                    }
                });
            }

            BroadcastNotification(adminName, "unbury_notification", FormatTargetName(targets));

            _ = AdminLogManager.AddLogAsync("unbury", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targets.Count}");
            Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} unburied {Count} player(s)", adminName, targets.Count);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Unbury command failed");
        }
    }
}
