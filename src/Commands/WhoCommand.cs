using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

using CS2_Admin.Services;
namespace CS2_Admin.Commands;

public class WhoCommand : CommandBase
{
    private readonly BanManager _banManager;
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly WarnManager _warnManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly PlayerSanctionStateService _sanctionStateService;
    private readonly MultiServerConfig _multiServerConfig;

    public WhoCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        BanManager banManager,
        MuteManager muteManager,
        GagManager gagManager,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        PlayerSanctionStateService sanctionStateService,
        MultiServerConfig multiServerConfig)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _banManager = banManager;
        _muteManager = muteManager;
        _gagManager = gagManager;
        _warnManager = warnManager;
        _adminDbManager = adminDbManager;
        _sanctionStateService = sanctionStateService;
        _multiServerConfig = multiServerConfig;
    }

    public override void Execute(ICommandContext context)
    {
        var args = NormalizeArgs(context.Args, CommandsConfig.Who);
        if (!HasPerm(context, Permissions.Who))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 1)
        {
            Reply(context, "who_usage");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
        if (target == null)
        {
            Reply(context, "player_not_found");
            return;
        }

        var steamId64 = target.SteamID;
        var targetPlayerId = target.PlayerID;
        var targetIp = target.IPAddress;

        _ = Task.Run(async () =>
        {
            var admin = await _adminDbManager.GetAdminAsync(steamId64);
            var effectiveFlags = await _adminDbManager.GetEffectiveFlagsAsync(steamId64);
            var effectiveImmunity = await _adminDbManager.GetEffectiveImmunityAsync(steamId64);
            var ban = await _banManager.GetActiveBanAsync(steamId64, targetIp, _multiServerConfig.Enabled);
            var mute = await _muteManager.GetActiveMuteAsync(steamId64);
            var gag = await _gagManager.GetActiveGagAsync(steamId64);
            var warn = await _warnManager.GetActiveWarnAsync(steamId64);
            var totalBans = await _banManager.GetTotalBansAsync(steamId64);
            var totalMutes = await _muteManager.GetTotalMutesAsync(steamId64);
            var totalGags = await _gagManager.GetTotalGagsAsync(steamId64);
            var totalWarns = await _warnManager.GetTotalWarnsAsync(steamId64);

            Core.Scheduler.NextTick(() =>
            {
                var liveTarget = Core.PlayerManager.GetPlayer(targetPlayerId);
                var name = liveTarget?.Controller.PlayerName ?? L("player_fallback_name", targetPlayerId);
                var userId = targetPlayerId;
                var ip = (liveTarget?.IPAddress ?? targetIp ?? L("unknown")).Split(':')[0];
                var ping = liveTarget != null ? (int)liveTarget.Controller.Ping : 0;
                var teamNum = liveTarget?.Controller.TeamNum ?? 0;
                var teamName = PlayerUtils.GetTeamName(teamNum, PluginLocalizer.Get(Core));
                var isAlive = liveTarget?.PlayerPawn?.IsValid == true && liveTarget.PlayerPawn.Health > 0;

                var lines = new List<string>
                {
                    L("who_header", name),
                    L("who_name", name),
                    L("who_userid", userId),
                    L("who_steamid", steamId64.ToString()),
                    L("who_team", teamName, teamNum),
                    L("who_ip", ip),
                    L("who_ping", ping),
                    L("who_alive", isAlive ? L("yes") : L("no"))
                };

                if (admin != null)
                {
                    var flags = effectiveFlags.Length == 0
                        ? L("who_none")
                        : string.Join(",", effectiveFlags);
                    lines.Add(L("who_admin_flags", flags, effectiveImmunity));
                }

                if (ban != null && ban.IsActive)
                {
                    var expires = ban.IsPermanent ? L("permanent") : (ban.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? L("unknown"));
                    lines.Add(L("who_active_ban_yes", ban.Reason, expires));
                }
                else
                {
                    lines.Add(L("who_active_ban_no"));
                }

                if (mute != null && mute.IsActive)
                {
                    var expires = mute.IsPermanent ? L("permanent") : (mute.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? L("unknown"));
                    lines.Add(L("who_active_mute_yes", mute.Reason, expires));
                }
                else
                {
                    lines.Add(L("who_active_mute_no"));
                }

                if (gag != null && gag.IsActive)
                {
                    var expires = gag.IsPermanent ? L("permanent") : (gag.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? L("unknown"));
                    lines.Add(L("who_active_gag_yes", gag.Reason, expires));
                }
                else
                {
                    lines.Add(L("who_active_gag_no"));
                }

                if (warn != null && warn.IsActive)
                {
                    var expires = warn.IsPermanent ? L("permanent") : (warn.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? L("unknown"));
                    lines.Add(L("who_active_warn_yes", warn.Reason, expires));
                }
                else
                {
                    lines.Add(L("who_active_warn_no"));
                }

                lines.Add(L("who_total_bans", totalBans));
                lines.Add(L("who_total_mutes", totalMutes));
                lines.Add(L("who_total_gags", totalGags));
                lines.Add(L("who_total_warns", totalWarns));

                lines.Add(L("who_footer", name));

                var output = string.Join('\n', lines);

                if (context.IsSentByPlayer && context.Sender != null)
                {
                    context.Sender.SendConsole(output);
                    if (context.Sender.IsValid && !context.Sender.IsFakeClient)
                        context.Sender.SendChat($" \x02{L("prefix")}\x01 {L("who_console")}");
                }
                else
                {
                    Core.Logger.LogInformationIfEnabled("{WhoInfo}", output);
                }
            });
        });
    }
}

