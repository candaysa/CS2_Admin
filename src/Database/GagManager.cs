using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dapper;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using System.Collections.Concurrent;

namespace CS2_Admin.Database;

public class GagManager
{
    private readonly ISwiftlyCore _core;
    private readonly ConcurrentDictionary<ulong, CacheEntry> _gagCache = new();
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromSeconds(30);
    private readonly AsyncLocal<AdminContext> _currentAdmin = new();

    public GagManager(ISwiftlyCore core)
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

            _core.Logger.LogInformationIfEnabled("[CS2_Admin] Gag database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Gag database initialization warning: {Message}", ex.Message);
        }
    }

    public async Task<bool> AddGagAsync(ulong steamId, int durationMinutes, string reason)
    {
        return await Task.Run(() =>
        {
            try
            {
                var admin = _currentAdmin.Value ?? new AdminContext();
                DateTime? expiresAt = durationMinutes > 0 ? DateTime.UtcNow.AddMinutes(durationMinutes) : null;

                var gag = new Gag
                {
                    SteamId = steamId,
                    AdminName = admin.Name,
                    AdminSteamId = admin.SteamId,
                    Reason = reason,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt,
                    Status = GagStatus.Active
                };

                using var connection = _core.Database.GetConnection("mysql_detailed");
                var id = connection.Insert(gag);
                gag.Id = Convert.ToInt32(id);
                _gagCache[steamId] = new CacheEntry(gag, DateTime.UtcNow);
                _core.Logger.LogInformationIfEnabled(
                    "[CS2_Admin][Trace][Gag] add steamid={SteamId} gagId={GagId} admin={Admin} expiresAt={ExpiresAt} reason={Reason}",
                    steamId,
                    gag.Id,
                    gag.AdminName,
                    gag.ExpiresAt?.ToString("o") ?? "permanent",
                    gag.Reason);

                return true;
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error adding gag: {Message}", ex.Message);
                return false;
            }
        });
    }

    public async Task<bool> UngagAsync(ulong steamId, string ungagReason)
    {
        return await Task.Run(() =>
        {
            try
            {
                var admin = _currentAdmin.Value ?? new AdminContext();
                using var connection = _core.Database.GetConnection("mysql_detailed");

                var gag = connection.QueryFirstOrDefault<Gag>(
                    $@"SELECT * FROM `admin_gags` WHERE `steamid` = @SteamId AND {PunishmentQueryCompat.ActiveStatusWhere} ORDER BY `created_at` DESC LIMIT 1",
                    new { SteamId = steamId }
                );

                if (gag == null)
                {
                    return false;
                }

                gag.Status = GagStatus.Ungagged;
                gag.UngagAdminName = admin.Name;
                gag.UngagAdminSteamId = admin.SteamId;
                gag.UngagReason = ungagReason;
                gag.UngagDate = DateTime.UtcNow;

                connection.Update(gag);
                _gagCache.TryRemove(steamId, out _);
                _core.Logger.LogInformationIfEnabled(
                    "[CS2_Admin][Trace][Gag] ungag steamid={SteamId} gagId={GagId} admin={Admin} reason={Reason}",
                    steamId,
                    gag.Id,
                    gag.UngagAdminName ?? "-",
                    gag.UngagReason ?? "-");

                return true;
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error ungagging player: {Message}", ex.Message);
                return false;
            }
        });
    }

    public async Task<Gag?> GetActiveGagAsync(ulong steamId)
    {
        try
        {
            if (_gagCache.TryGetValue(steamId, out var cachedEntry))
            {
                var cachedGag = cachedEntry.Gag;
                if (DateTime.UtcNow - cachedEntry.CachedAtUtc < _cacheLifetime)
                {
                    if (cachedGag.IsExpired || cachedGag.Status != GagStatus.Active)
                    {
                        _gagCache.TryRemove(steamId, out _);
                        return null;
                    }

                    _core.Logger.LogInformationIfEnabled(
                        "[CS2_Admin][Trace][Gag] cache-hit steamid={SteamId} gagId={GagId} expiresAt={ExpiresAt} cachedAt={CachedAt}",
                        steamId,
                        cachedGag.Id,
                        cachedGag.ExpiresAt?.ToString("o") ?? "permanent",
                        cachedEntry.CachedAtUtc.ToString("o"));
                    return cachedGag;
                }

                _gagCache.TryRemove(steamId, out _);
            }

            using var connection = _core.Database.GetConnection("mysql_detailed");
            
            var gag = connection.QueryFirstOrDefault<Gag>(
                $@"SELECT * FROM `admin_gags` 
                  WHERE `steamid` = @SteamId 
                    AND {PunishmentQueryCompat.ActiveStatusWhere} 
                    AND (`expires_at` IS NULL OR `expires_at` > @Now) 
                  ORDER BY `created_at` DESC LIMIT 1",
                new { SteamId = steamId, Now = DateTime.UtcNow }
            );

            if (gag != null)
            {
                _gagCache[steamId] = new CacheEntry(gag, DateTime.UtcNow);
                _core.Logger.LogInformationIfEnabled(
                    "[CS2_Admin][Trace][Gag] db-load-active steamid={SteamId} gagId={GagId} admin={Admin} createdAt={CreatedAt} expiresAt={ExpiresAt} reason={Reason}",
                    steamId,
                    gag.Id,
                    gag.AdminName,
                    gag.CreatedAt.ToString("o"),
                    gag.ExpiresAt?.ToString("o") ?? "permanent",
                    gag.Reason);
            }
            else
            {
                _gagCache.TryRemove(steamId, out _);
            }

            return gag;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error checking gag: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<Gag?> GetActiveGagFreshAsync(ulong steamId)
    {
        try
        {
            InvalidateCache(steamId);

            using var connection = _core.Database.GetConnection("mysql_detailed");
            var gag = connection.Query<Gag>(
                $@"SELECT * FROM `admin_gags`
                  WHERE `steamid` = @SteamId
                    AND {PunishmentQueryCompat.ActiveStatusWhere}
                    AND (`expires_at` IS NULL OR `expires_at` > @Now)
                  ORDER BY `created_at` DESC LIMIT 1",
                new { SteamId = steamId, Now = DateTime.UtcNow }
            ).FirstOrDefault(IsMaterializedGag);

            if (gag != null)
            {
                _gagCache[steamId] = new CacheEntry(gag, DateTime.UtcNow);
                _core.Logger.LogInformationIfEnabled(
                    "[CS2_Admin][Trace][Gag] fresh-db-load-active steamid={SteamId} gagId={GagId} admin={Admin} createdAt={CreatedAt} expiresAt={ExpiresAt} reason={Reason}",
                    steamId,
                    gag.Id,
                    gag.AdminName,
                    gag.CreatedAt.ToString("o"),
                    gag.ExpiresAt?.ToString("o") ?? "permanent",
                    gag.Reason);
            }
            else
            {
                _gagCache.TryRemove(steamId, out _);
                _core.Logger.LogInformationIfEnabled(
                    "[CS2_Admin][Trace][Gag] fresh-db-load-none steamid={SteamId}",
                    steamId);
            }

            return gag;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error checking fresh gag: {Message}", ex.Message);
            return null;
        }
    }

    public Gag? GetActiveGagFromCache(ulong steamId)
    {
        if (_gagCache.TryGetValue(steamId, out var cachedEntry))
        {
            if (DateTime.UtcNow - cachedEntry.CachedAtUtc >= _cacheLifetime)
            {
                _gagCache.TryRemove(steamId, out _);
                return null;
            }

            if (cachedEntry.Gag.IsActive)
            {
                return cachedEntry.Gag;
            }
        }

        return null;
    }

    public async Task<int> GetTotalGagsAsync(ulong steamId)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var connection = _core.Database.GetConnection("mysql_detailed");
                return connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM `admin_gags` WHERE `steamid` = @SteamId",
                    new { SteamId = steamId }
                );
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error getting total gags: {Message}", ex.Message);
                return 0;
            }
        });
    }

    public async Task CleanupExpiredGagsAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                using var connection = _core.Database.GetConnection("mysql_detailed");
                
                var cleaned = connection.Execute(
                    $@"UPDATE `admin_gags` 
                      SET `status` = '3' 
                      WHERE {PunishmentQueryCompat.ActiveStatusWhere} 
                        AND `expires_at` IS NOT NULL 
                        AND `expires_at` <= @Now",
                    new { Now = DateTime.UtcNow }
                );

                if (cleaned > 0)
                {
                    _core.Logger.LogInformationIfEnabled("[CS2_Admin] Marked {Count} gags as expired", cleaned);
                    _gagCache.Clear();
                }
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error cleaning expired gags: {Message}", ex.Message);
            }
        });
    }

    public void ClearCache()
    {
        _gagCache.Clear();
    }

    public void InvalidateCache(ulong steamId)
    {
        if (_gagCache.TryRemove(steamId, out _))
        {
            _core.Logger.LogInformationIfEnabled(
                "[CS2_Admin][Trace][Gag] cache-invalidate steamid={SteamId}", steamId);
        }
    }

    private static bool IsMaterializedGag(Gag gag)
    {
        return gag.Id > 0 && gag.IsActive;
    }

    private sealed record CacheEntry(Gag Gag, DateTime CachedAtUtc);
}
