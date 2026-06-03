using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;

namespace CS2_Admin.Services;

public class SanctionService
{
    private readonly BanManager _banManager;
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly WarnManager _warnManager;
    private readonly PlayerSanctionStateService _sanctionStateService;
    private readonly MultiServerConfig _multiServerConfig;

    public SanctionService(
        BanManager banManager,
        MuteManager muteManager,
        GagManager gagManager,
        WarnManager warnManager,
        PlayerSanctionStateService sanctionStateService,
        MultiServerConfig multiServerConfig)
    {
        _banManager = banManager;
        _muteManager = muteManager;
        _gagManager = gagManager;
        _warnManager = warnManager;
        _sanctionStateService = sanctionStateService;
        _multiServerConfig = multiServerConfig;
    }

    public bool ResolveGlobalMode()
    {
        return _multiServerConfig.Enabled && _multiServerConfig.GlobalBansByDefault;
    }

    public async Task RefreshStateAsync(ulong steamId, string? ipAddress)
    {
        await _sanctionStateService.RefreshAsync(steamId, ipAddress);
    }

    public BanManager BanManager => _banManager;
    public MuteManager MuteManager => _muteManager;
    public GagManager GagManager => _gagManager;
    public WarnManager WarnManager => _warnManager;
    public PlayerSanctionStateService SanctionStateService => _sanctionStateService;
    public MultiServerConfig MultiServerConfig => _multiServerConfig;
}
