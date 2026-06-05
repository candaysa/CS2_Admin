using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class AddBanCommand : CommandBase
{
    private readonly BanManager _banManager;
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly WarnManager _warnManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly PlayerIpDbManager _playerIpDbManager;
    private readonly PlayerSessionManager _playerSessionManager;
    private readonly RecentPlayersTracker _recentPlayersTracker;
    private readonly DiscordBotService _discord;
    private readonly SanctionMenuConfig _sanctions;
    private readonly MultiServerConfig _multiServerConfig;
    private readonly int _banType;
    private readonly PlayerSanctionStateService _sanctionStateService;

    public AddBanCommand(
        ISwiftlyCore core,
        BanManager banManager,
        MuteManager muteManager,
        GagManager gagManager,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        AdminLogManager adminLogManager,
        PlayerIpDbManager playerIpDbManager,
        PlayerSessionManager playerSessionManager,
        RecentPlayersTracker recentPlayersTracker,
        DiscordBotService discord,
        PermissionsConfig permissions,
        CommandsConfig commands,
        TagsConfig tags,
        MessagesConfig messages,
        SanctionMenuConfig sanctions,
        MultiServerConfig multiServerConfig,
        int banType,
        PlayerSanctionStateService sanctionStateService,
        PermissionService permissionService)
        : base(core, permissions, commands, tags, messages, adminLogManager, permissionService)
    {
        _banManager = banManager;
        _muteManager = muteManager;
        _gagManager = gagManager;
        _warnManager = warnManager;
        _adminDbManager = adminDbManager;
        _playerIpDbManager = playerIpDbManager;
        _playerSessionManager = playerSessionManager;
        _recentPlayersTracker = recentPlayersTracker;
        _discord = discord;
        _sanctions = sanctions;
        _multiServerConfig = multiServerConfig;
        _banType = banType is >= 1 and <= 3 ? banType : 1;
        _sanctionStateService = sanctionStateService;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.AddBan);

            if (!HasPerm(context, Permissions.AddBan))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 2)
            {
                Reply(context, "addban_usage");
                return;
            }

            if (!PlayerUtils.TryParseSteamId(args[0], out var targetSteamId))
            {
                Reply(context, "invalid_steamid");
                return;
            }

            if (!SanctionDurationParser.TryParseToMinutes(args[1], out var duration))
            {
                Reply(context, "invalid_duration");
                return;
            }

            var reason = args.Length > 2 ? string.Join(" ", args.Skip(2)) : L("no_reason");
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var adminSteamId = context.Sender?.SteamID ?? 0;
            var isGlobal = ResolveGlobalMode();

            await AddOfflineSteamBanAsync(context, targetSteamId, duration, reason, adminName, adminSteamId, isGlobal);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] AddBan command failed");
        }
    }

    private bool ResolveGlobalMode()
    {
        if (!_multiServerConfig.Enabled)
        {
            return false;
        }

        return _multiServerConfig.GlobalBansByDefault;
    }

    private async Task AddOfflineSteamBanAsync(
        ICommandContext context,
        ulong targetSteamId,
        int duration,
        string reason,
        string adminName,
        ulong adminSteamId,
        bool isGlobal)
    {
        var existingBan = await _banManager.GetActiveBanFreshAsync(targetSteamId, null, _multiServerConfig.Enabled);
        if (existingBan != null)
        {
            Core.Scheduler.NextTick(() => Reply(context, "steamid_already_banned", targetSteamId));
            return;
        }

        _banManager.SetAdminContext(adminName, adminSteamId);
        var ok = await _banManager.AddBanAsync(targetSteamId, targetSteamId.ToString(), duration, reason, isGlobal);
        if (!ok)
        {
            Core.Scheduler.NextTick(() => Reply(context, "addban_failed"));
            return;
        }

        Core.Scheduler.NextTick(() =>
        {
            var durationDisplay = duration <= 0 ? L("permanent") : L("duration_minutes", duration);
            Reply(context, "addban_success", targetSteamId, durationDisplay);
        });

        await AdminLogManager.AddLogAsync("addban", adminName, adminSteamId, targetSteamId, null, $"duration={duration};global={isGlobal};reason={reason}", null, null, reason);
    }
}
