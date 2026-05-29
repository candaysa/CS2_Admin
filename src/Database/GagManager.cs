using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using System.Collections.Concurrent;

namespace CS2_Admin.Database;

public class GagManager
{
    private readonly ISwiftlyCore _core;
    private readonly ConcurrentDictionary<ulong, CacheEntry> _gagCache = new();
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);
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

                var gag = connection.FirstOrDefault<Gag>(g => g.SteamId == steamId && g.StatusValue == GagStatusNames.Active);
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
            var gag = connection.FirstOrDefault<Gag>(g =>
                g.SteamId == steamId &&
                g.StatusValue == GagStatusNames.Active &&
                (g.ExpiresAt == null || g.ExpiresAt > DateTime.UtcNow));

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
                var gags = connection.Select<Gag>(g => g.SteamId == steamId);
                return gags.Count();
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
                var expiredGags = connection.Select<Gag>(g =>
                    g.StatusValue == GagStatusNames.Active &&
                    g.ExpiresAt != null &&
                    g.ExpiresAt <= DateTime.UtcNow);

                var cleaned = 0;
                foreach (var gag in expiredGags)
                {
                    gag.Status = GagStatus.Expired;
                    connection.Update(gag);
                    cleaned++;
                }

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

    private sealed record CacheEntry(Gag Gag, DateTime CachedAtUtc);
}
