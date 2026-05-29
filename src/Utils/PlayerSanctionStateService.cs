using System.Collections.Concurrent;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Models;

namespace CS2_Admin.Utils;

public sealed class PlayerSanctionStateService
{
    private readonly BanManager _banManager;
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly WarnManager _warnManager;
    private readonly MultiServerConfig _multiServerConfig;
    private readonly ConcurrentDictionary<ulong, PlayerSanctionState> _states = new();

    public PlayerSanctionStateService(
        BanManager banManager,
        MuteManager muteManager,
        GagManager gagManager,
        WarnManager warnManager,
        MultiServerConfig multiServerConfig)
    {
        _banManager = banManager;
        _muteManager = muteManager;
        _gagManager = gagManager;
        _warnManager = warnManager;
        _multiServerConfig = multiServerConfig;
    }

    public async Task<PlayerSanctionState> RefreshAsync(ulong steamId, string? ipAddress)
    {
        var banTask = _banManager.GetActiveBanAsync(steamId, ipAddress, _multiServerConfig.Enabled);
        var muteTask = _muteManager.GetActiveMuteAsync(steamId);
        var gagTask = _gagManager.GetActiveGagAsync(steamId);
        var warnTask = _warnManager.GetActiveWarnAsync(steamId);

        await Task.WhenAll(banTask, muteTask, gagTask, warnTask);

        var state = new PlayerSanctionState(
            steamId,
            ipAddress,
            banTask.Result,
            muteTask.Result,
            gagTask.Result,
            warnTask.Result,
            DateTime.UtcNow);

        _states[steamId] = state;
        return state;
    }

    public PlayerSanctionState? GetCachedState(ulong steamId)
    {
        return _states.TryGetValue(steamId, out var state) ? state : null;
    }

    public Ban? GetCachedBan(ulong steamId)
    {
        return GetCachedState(steamId)?.Ban;
    }

    public Mute? GetCachedMute(ulong steamId)
    {
        return GetCachedState(steamId)?.Mute;
    }

    public Gag? GetCachedGag(ulong steamId)
    {
        return GetCachedState(steamId)?.Gag;
    }

    public Warn? GetCachedWarn(ulong steamId)
    {
        return GetCachedState(steamId)?.Warn;
    }

    public void Invalidate(ulong steamId)
    {
        _states.TryRemove(steamId, out _);
    }
}

public sealed record PlayerSanctionState(
    ulong SteamId,
    string? IpAddress,
    Ban? Ban,
    Mute? Mute,
    Gag? Gag,
    Warn? Warn,
    DateTime LoadedAt);
