using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace CS2_Admin.Commands;

public class KickCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public KickCommand(
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
        var args = NormalizeArgs(context.Args, CommandsConfig.Kick);

        if (!HasPerm(context, Permissions.Kick))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 1)
        {
            Reply(context, "kick_usage");
            return;
        }

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

        string reason = args.Length > 1
            ? string.Join(" ", args.Skip(1))
            : L("no_reason");

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        var targetName = target.Controller.PlayerName;

        var prefix = L("prefix");
        foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{prefix}\x01 {L("kicked_notification", visibleAdmin, targetName, reason)}");
        }

        PlayerUtils.SendNotification(target, Messages,
            L("kicked_personal_html", reason),
            $" \x02{prefix}\x01 {L("kicked_personal_chat", reason)}");

        var targetSteamId = target.SteamID;
        Core.Scheduler.DelayBySeconds(2f, () =>
        {
            var playerToKick = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
            playerToKick?.Kick(reason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
        });

        AdminLogManager.AddLogAsync("kick", adminName, context.Sender?.SteamID ?? 0, targetSteamId, target.IPAddress, $"reason={reason}", target.Controller.PlayerName, target.PlayerID, reason);
        Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} kicked {Target}. Reason: {Reason}",
            adminName, targetName, reason);
    }
}
