using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class TeamCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public TeamCommand(
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
        var args = NormalizeArgs(context.Args, CommandsConfig.ChangeTeam);

        if (!HasPerm(context, Permissions.ChangeTeam))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 2)
        {
            Reply(context, "team_usage");
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

        var team = PlayerUtils.ParseTeam(args[1]);
        if (team == null)
        {
            Reply(context, "invalid_team");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        var targetName = target.Controller.PlayerName;
        var prefix = L("prefix");
        var teamName = PlayerUtils.GetTeamName((int)team.Value, PluginLocalizer.Get(Core));

        target.ChangeTeam(team.Value);

        PlayerUtils.SendNotification(target, Messages,
            L("team_changed_personal_html", teamName, ResolveVisibleAdminName(target, adminName)),
            $" \x02{prefix}\x01 {L("team_changed_personal_chat", teamName, ResolveVisibleAdminName(target, adminName))}");

        foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            player.SendChat($" \x02{prefix}\x01 {L("team_changed_notification", visibleAdmin, targetName, teamName)}");
        }

        AdminLogManager.AddLogAsync("team", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"team={teamName}", target.Controller.PlayerName);
        Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} moved {Target} to {Team}", adminName, targetName, teamName);
    }
}
