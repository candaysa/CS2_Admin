using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dapper;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using System.Collections.Concurrent;

namespace CS2_Admin.Database;

public class MuteManager
{
    private readonly ISwiftlyCore _core;
    private readonly ConcurrentDictionary<ulong, CacheEntry> _muteCache = new();
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromSeconds(30);
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
                _core.Logger.LogInformationIfEnabled(
                    "[CS2_Admin][Trace][Mute] add steamid={SteamId} muteId={MuteId} admin={Admin} expiresAt={ExpiresAt} reason={Reason}",
                    steamId,
                    mute.Id,
                    mute.AdminName,
                    mute.ExpiresAt?.ToString("o") ?? "permanent",
                    mute.Reason);

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

                var mute = connection.QueryFirstOrDefault<Mute>(
                    $@"SELECT * FROM `admin_mutes` WHERE `steamid` = @SteamId AND {PunishmentQueryCompat.ActiveStatusWhere} ORDER BY `created_at` DESC LIMIT 1",
                    new { SteamId = steamId }
                );

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
                _core.Logger.LogInformationIfEnabled(
                    "[CS2_Admin][Trace][Mute] unmute steamid={SteamId} muteId={MuteId} admin={Admin} reason={Reason}",
                    steamId,
                    mute.Id,
                    mute.UnmuteAdminName ?? "-",
                    mute.UnmuteReason ?? "-");

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

                    _core.Logger.LogInformationIfEnabled(
                        "[CS2_Admin][Trace][Mute] cache-hit steamid={SteamId} muteId={MuteId} expiresAt={ExpiresAt} cachedAt={CachedAt}",
                        steamId,
                        cachedMute.Id,
                        cachedMute.ExpiresAt?.ToString("o") ?? "permanent",
                        cachedEntry.CachedAtUtc.ToString("o"));
                    return cachedMute;
                }

                _muteCache.TryRemove(steamId, out _);
            }

            using var connection = _core.Database.GetConnection("mysql_detailed");
            
            var mute = connection.QueryFirstOrDefault<Mute>(
                $@"SELECT * FROM `admin_mutes` 
                  WHERE `steamid` = @SteamId 
                    AND {PunishmentQueryCompat.ActiveStatusWhere} 
                    AND (`expires_at` IS NULL OR `expires_at` > @Now) 
                  ORDER BY `created_at` DESC LIMIT 1",
                new { SteamId = steamId, Now = DateTime.UtcNow }
            );

            if (mute != null)
            {
                _muteCache[steamId] = new CacheEntry(mute, DateTime.UtcNow);
                _core.Logger.LogInformationIfEnabled(
                    "[CS2_Admin][Trace][Mute] db-load-active steamid={SteamId} muteId={MuteId} admin={Admin} createdAt={CreatedAt} expiresAt={ExpiresAt} reason={Reason}",
                    steamId,
                    mute.Id,
                    mute.AdminName,
                    mute.CreatedAt.ToString("o"),
                    mute.ExpiresAt?.ToString("o") ?? "permanent",
                    mute.Reason);
            }
            else
            {
                _muteCache.TryRemove(steamId, out _);
                _core.Logger.LogInformationIfEnabled(
                    "[CS2_Admin][Trace][Mute] db-load-none steamid={SteamId}",
                    steamId);
            }

            return mute;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error checking mute: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<Mute?> GetActiveMuteFreshAsync(ulong steamId)
    {
        try
        {
            InvalidateCache(steamId);

            using var connection = _core.Database.GetConnection("mysql_detailed");
            var mute = connection.Query<Mute>(
                $@"SELECT * FROM `admin_mutes`
                  WHERE `steamid` = @SteamId
                    AND {PunishmentQueryCompat.ActiveStatusWhere}
                    AND (`expires_at` IS NULL OR `expires_at` > @Now)
                  ORDER BY `created_at` DESC LIMIT 1",
                new { SteamId = steamId, Now = DateTime.UtcNow }
            ).FirstOrDefault(IsMaterializedMute);

            if (mute != null)
            {
                _muteCache[steamId] = new CacheEntry(mute, DateTime.UtcNow);
                _core.Logger.LogInformationIfEnabled(
                    "[CS2_Admin][Trace][Mute] fresh-db-load-active steamid={SteamId} muteId={MuteId} admin={Admin} createdAt={CreatedAt} expiresAt={ExpiresAt} reason={Reason}",
                    steamId,
                    mute.Id,
                    mute.AdminName,
                    mute.CreatedAt.ToString("o"),
                    mute.ExpiresAt?.ToString("o") ?? "permanent",
                    mute.Reason);
            }
            else
            {
                _muteCache.TryRemove(steamId, out _);
                _core.Logger.LogInformationIfEnabled(
                    "[CS2_Admin][Trace][Mute] fresh-db-load-none steamid={SteamId}",
                    steamId);
            }

            return mute;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error checking fresh mute: {Message}", ex.Message);
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
                return connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM `admin_mutes` WHERE `steamid` = @SteamId",
                    new { SteamId = steamId }
                );
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
                
                var cleaned = connection.Execute(
                    $@"UPDATE `admin_mutes` 
                      SET `status` = '3' 
                      WHERE {PunishmentQueryCompat.ActiveStatusWhere} 
                        AND `expires_at` IS NOT NULL 
                        AND `expires_at` <= @Now",
                    new { Now = DateTime.UtcNow }
                );

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

    public void InvalidateCache(ulong steamId)
    {
        if (_muteCache.TryRemove(steamId, out _))
        {
            _core.Logger.LogInformationIfEnabled(
                "[CS2_Admin][Trace][Mute] cache-invalidate steamid={SteamId}", steamId);
        }
    }

    private static bool IsMaterializedMute(Mute mute)
    {
        return mute.Id > 0 && mute.IsActive;
    }

    private sealed record CacheEntry(Mute Mute, DateTime CachedAtUtc);
}
