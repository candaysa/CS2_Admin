using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using System.Collections.Concurrent;

namespace CS2_Admin.Database;

public class MuteManager
{
    private readonly ISwiftlyCore _core;
    private readonly ConcurrentDictionary<ulong, CacheEntry> _muteCache = new();
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);
    private readonly AsyncLocal<AdminContext> _currentAdmin = new();

    public MuteManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public void SetAdminContext(string? adminName, ulong? adminSteamId)
    {
        _currentAdmin.Value = new AdminContext
        {
            Name = adminName ?? PluginLocalizer.Get(_core)["console_name"],
            SteamId = adminSteamId ?? 0
        };
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            MigrationRunner.RunMigrations(connection);

            _core.Logger.LogInformationIfEnabled("[CS2_Admin] Mute database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Mute database initialization warning: {Message}", ex.Message);
        }
    }

    public async Task<bool> AddMuteAsync(ulong steamId, int durationMinutes, string reason)
    {
        return await Task.Run(() =>
        {
            try
            {
                var admin = _currentAdmin.Value ?? new AdminContext();
                DateTime? expiresAt = durationMinutes > 0 ? DateTime.UtcNow.AddMinutes(durationMinutes) : null;

                var mute = new Mute
                {
                    SteamId = steamId,
                    AdminName = admin.Name,
                    AdminSteamId = admin.SteamId,
                    Reason = reason,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt,
                    Status = MuteStatus.Active
                };

                using var connection = _core.Database.GetConnection("mysql_detailed");
                var id = connection.Insert(mute);
                mute.Id = Convert.ToInt32(id);
                _muteCache[steamId] = new CacheEntry(mute, DateTime.UtcNow);

                return true;
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error adding mute: {Message}", ex.Message);
                return false;
            }
        });
    }

    public async Task<bool> UnmuteAsync(ulong steamId, string unmuteReason)
    {
        return await Task.Run(() =>
        {
            try
            {
                var admin = _currentAdmin.Value ?? new AdminContext();
                using var connection = _core.Database.GetConnection("mysql_detailed");

                var mute = connection.FirstOrDefault<Mute>(m => m.SteamId == steamId && m.StatusValue == MuteStatusNames.Active);
                if (mute == null)
                {
                    return false;
                }

                mute.Status = MuteStatus.Unmuted;
                mute.UnmuteAdminName = admin.Name;
                mute.UnmuteAdminSteamId = admin.SteamId;
                mute.UnmuteReason = unmuteReason;
                mute.UnmuteDate = DateTime.UtcNow;

                connection.Update(mute);
                _muteCache.TryRemove(steamId, out _);

                return true;
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error unmuting player: {Message}", ex.Message);
                return false;
            }
        });
    }

    public async Task<Mute?> GetActiveMuteAsync(ulong steamId)
    {
        try
        {
            if (_muteCache.TryGetValue(steamId, out var cachedEntry))
            {
                var cachedMute = cachedEntry.Mute;
                if (DateTime.UtcNow - cachedEntry.CachedAtUtc < _cacheLifetime)
                {
                    if (cachedMute.IsExpired || cachedMute.Status != MuteStatus.Active)
                    {
                        _muteCache.TryRemove(steamId, out _);
                        return null;
                    }

                    return cachedMute;
                }

                _muteCache.TryRemove(steamId, out _);
            }

            using var connection = _core.Database.GetConnection("mysql_detailed");
            var mutes = connection.Select<Mute>(m => m.SteamId == steamId && m.StatusValue == MuteStatusNames.Active);
            var mute = mutes.FirstOrDefault(m => m.ExpiresAt == null || m.ExpiresAt > DateTime.UtcNow);

            if (mute != null)
            {
                _muteCache[steamId] = new CacheEntry(mute, DateTime.UtcNow);
            }
            else
            {
                _muteCache.TryRemove(steamId, out _);
            }

            return mute;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error checking mute: {Message}", ex.Message);
            return null;
        }
    }

    public Mute? GetActiveMuteFromCache(ulong steamId)
    {
        if (_muteCache.TryGetValue(steamId, out var cachedEntry))
        {
            if (DateTime.UtcNow - cachedEntry.CachedAtUtc >= _cacheLifetime)
            {
                _muteCache.TryRemove(steamId, out _);
                return null;
            }

            if (cachedEntry.Mute.IsActive)
            {
                return cachedEntry.Mute;
            }
        }

        return null;
    }

    public async Task<int> GetTotalMutesAsync(ulong steamId)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var connection = _core.Database.GetConnection("mysql_detailed");
                var mutes = connection.Select<Mute>(m => m.SteamId == steamId);
                return mutes.Count();
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error getting total mutes: {Message}", ex.Message);
                return 0;
            }
        });
    }

    public async Task UpdateExpiredMutesAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                using var connection = _core.Database.GetConnection("mysql_detailed");
                var activeMutes = connection.Select<Mute>(m => m.StatusValue == MuteStatusNames.Active);
                var expiredMutes = activeMutes.Where(m => m.ExpiresAt != null && m.ExpiresAt <= DateTime.UtcNow);

                var cleaned = 0;
                foreach (var mute in expiredMutes)
                {
                    mute.Status = MuteStatus.Expired;
                    connection.Update(mute);
                    cleaned++;
                }

                if (cleaned > 0)
                {
                    _core.Logger.LogInformationIfEnabled("[CS2_Admin] Marked {Count} mutes as expired", cleaned);
                    _muteCache.Clear();
                }
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error cleaning expired mutes: {Message}", ex.Message);
            }
        });
    }

    public void ClearCache()
    {
        _muteCache.Clear();
    }

    private sealed record CacheEntry(Mute Mute, DateTime CachedAtUtc);
}
