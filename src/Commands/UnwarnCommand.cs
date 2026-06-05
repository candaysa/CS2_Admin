using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

using CS2_Admin.Services;
namespace CS2_Admin.Commands;

public class UnwarnCommand : CommandBase
{
    private readonly WarnManager _warnManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly DiscordBotService _discordBotService;
    private readonly PlayerSanctionStateService _sanctionStateService;

    public UnwarnCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        DiscordBotService discordBotService,
        PlayerSanctionStateService sanctionStateService)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _warnManager = warnManager;
        _adminDbManager = adminDbManager;
        _discordBotService = discordBotService;
        _sanctionStateService = sanctionStateService;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Unwarn);
            if (!HasPerm(context, Permissions.Unwarn))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "unwarn_usage");
                return;
            }

            var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
            if (target == null)
            {
                Reply(context, "player_not_found");
                return;
            }

            var reason = args.Length > 1
                ? string.Join(" ", args.Skip(1))
                : L("no_reason");

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var adminSteamId = context.Sender?.SteamID ?? 0;
            var targetName = target.Controller.PlayerName ?? L("unknown");
            var targetSteamId = target.SteamID;
            var targetIp = target.IPAddress;

            if (!await ValidateCanPunishAsync(context, targetSteamId))
                return;

            _warnManager.SetAdminContext(adminName, adminSteamId);
            var ok = await _warnManager.UnwarnAsync(targetSteamId, reason);
            if (!ok)
            {
                Core.Scheduler.NextTick(() => Reply(context, "player_not_warned", targetName));
                return;
            }

            await _sanctionStateService.RefreshAsync(targetSteamId, targetIp);

            Core.Scheduler.NextTick(() =>
            {
                Reply(context, "unwarned_notification", targetName, reason);

                var onlineTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                if (onlineTarget != null)
                {
                    PlayerUtils.SendNotification(
                        onlineTarget,
                        Messages,
                        $"<font color='#00ff00'><b>{L("unwarned_personal_html")}</b></font><br><br>{L("label_reason")}: <font color='#ffffff'>{reason}</font>",
                        $" \x02{L("prefix")}\x01 {L("unwarned_personal_chat", reason)}");
                }
            });

            await AdminLogManager.AddLogAsync("unwarn", adminName, adminSteamId, targetSteamId, targetIp, $"reason={reason}", targetName, target.PlayerID, reason);
            Core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} removed warn from {Target}. Reason: {Reason}",
                adminName, targetName, reason);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Unwarn command failed");
        }
    }

    private async Task<bool> ValidateCanPunishAsync(ICommandContext context, ulong targetSteamId)
    {
        return await PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, targetSteamId);
    }
}

