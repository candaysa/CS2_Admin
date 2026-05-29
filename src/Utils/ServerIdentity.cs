using SwiftlyS2.Shared;
using System.Net;

namespace CS2_Admin.Utils;

public static class ServerIdentity
{
    private static readonly string[] MapCvarCandidates =
    [
        "mapname",
        "map",
        "currentmap",
        "host_map"
    ];

    private static readonly HttpClient PublicIpHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    private static readonly string[] PublicIpEndpoints =
    [
        "https://api.ipify.org",
        "https://icanhazip.com"
    ];

    private static string? _configuredPublicIp;
    private static string? _cachedPublicIp;
    private static DateTime _cachedPublicIpUntilUtc;
    private static int? _cachedPort;
    private static int? _cachedMaxPlayers;

    public static void ConfigurePublicIp(string? publicIp)
    {
        _configuredPublicIp = IsUsablePublicServerIp(publicIp) ? publicIp!.Trim() : null;
    }

    public static string GetName(ISwiftlyCore core)
    {
        try
        {
            var cvar = core.ConVar.Find<string>("hostname");
            if (cvar != null && !string.IsNullOrWhiteSpace(cvar.Value))
            {
                return cvar.Value.Trim();
            }
        }
        catch
        {
        }

        return "Unknown Server";
    }

    public static string GetIp(ISwiftlyCore core)
    {
        if (!string.IsNullOrWhiteSpace(_configuredPublicIp))
        {
            return _configuredPublicIp;
        }

        var hostIp = TryGetHostIp(core);
        if (!string.IsNullOrWhiteSpace(hostIp))
        {
            return hostIp;
        }

        try
        {
            var cvar = core.ConVar.Find<string>("ip");
            if (cvar != null && !string.IsNullOrWhiteSpace(cvar.Value))
            {
                var value = cvar.Value.Trim();
                if (IsUsablePublicServerIp(value))
                {
                    return value;
                }
            }
        }
        catch
        {
        }

        var publicIp = TryGetPublicIpFromNetwork();
        if (!string.IsNullOrWhiteSpace(publicIp))
        {
            return publicIp;
        }

        return "0.0.0.0";
    }

    public static int GetPort(ISwiftlyCore core)
    {
        foreach (var cvarName in new[] { "hostport", "port" })
        {
            try
            {
                var intCvar = core.ConVar.Find<int>(cvarName);
                if (intCvar != null && IsUsablePort(intCvar.Value))
                {
                    _cachedPort = intCvar.Value;
                    return intCvar.Value;
                }
            }
            catch
            {
            }

            try
            {
                var longCvar = core.ConVar.Find<long>(cvarName);
                if (longCvar != null && IsUsablePort((int)longCvar.Value))
                {
                    _cachedPort = (int)longCvar.Value;
                    return (int)longCvar.Value;
                }
            }
            catch
            {
            }

            try
            {
                var stringCvar = core.ConVar.Find<string>(cvarName);
                if (stringCvar != null && int.TryParse(stringCvar.Value?.Trim(), out var parsedPort) && IsUsablePort(parsedPort))
                {
                    _cachedPort = parsedPort;
                    return parsedPort;
                }
            }
            catch
            {
            }
        }

        return _cachedPort ?? 0;
    }

    public static string GetServerId(ISwiftlyCore core)
    {
        return $"{GetIp(core)}:{GetPort(core)}";
    }

    public static int GetMaxPlayers(ISwiftlyCore core, int fallback = 64)
    {
        foreach (var cvarName in new[] { "sv_visiblemaxplayers", "maxplayers", "sv_maxplayers" })
        {
            var cvarValue = TryGetIntCvar(core, cvarName);
            if (cvarValue.HasValue && IsUsableMaxPlayers(cvarValue.Value))
            {
                _cachedMaxPlayers = cvarValue.Value;
                return cvarValue.Value;
            }
        }

        if (_cachedMaxPlayers.HasValue && IsUsableMaxPlayers(_cachedMaxPlayers.Value))
        {
            return _cachedMaxPlayers.Value;
        }

        return IsUsableMaxPlayers(fallback) ? fallback : 64;
    }

    public static string GetCurrentMap(ISwiftlyCore core)
    {
        var engineMap = TryGetMapFromEngine(core);
        if (!string.IsNullOrWhiteSpace(engineMap))
        {
            return engineMap;
        }

        foreach (var cvarName in MapCvarCandidates)
        {
            try
            {
                var cvar = core.ConVar.Find<string>(cvarName);
                if (cvar == null || string.IsNullOrWhiteSpace(cvar.Value))
                {
                    continue;
                }

                var normalized = NormalizeMapName(cvar.Value);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }
            catch
            {
            }
        }

        return "unknown";
    }

    private static string? TryGetMapFromEngine(ISwiftlyCore core)
    {
        try
        {
            var coreType = core.GetType();
            var engineObject =
                coreType.GetProperty("Engine")?.GetValue(core) ??
                coreType.GetProperty("EngineService")?.GetValue(core);

            if (engineObject == null)
            {
                return null;
            }

            var engineType = engineObject.GetType();
            var globalVars = engineType.GetProperty("GlobalVars")?.GetValue(engineObject);
            if (globalVars != null)
            {
                var mapName = globalVars.GetType().GetProperty("MapName")?.GetValue(globalVars) as string;
                var normalized = NormalizeMapName(mapName);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }

            var map = engineType.GetProperty("Map")?.GetValue(engineObject) as string;
            var fallback = NormalizeMapName(map);
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeMapName(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var value = rawValue.Trim();
        if (value.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        value = value.Replace("\\", "/", StringComparison.Ordinal);
        var slashIndex = value.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex < value.Length - 1)
        {
            value = value[(slashIndex + 1)..];
        }

        if (value.StartsWith("workshop/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
            value = parts.Length > 0 ? parts[^1] : value;
        }

        return value.Trim();
    }

    private static string? TryGetHostIp(ISwiftlyCore core)
    {
        try
        {
            var hostIpInt = core.ConVar.Find<int>("hostip");
            if (hostIpInt != null)
            {
                foreach (var value in ConvertHostIpToCandidates(unchecked((uint)hostIpInt.Value)))
                {
                    if (IsUsablePublicServerIp(value))
                    {
                        return value;
                    }
                }
            }
        }
        catch
        {
        }

        try
        {
            var hostIpLong = core.ConVar.Find<long>("hostip");
            if (hostIpLong != null)
            {
                foreach (var value in ConvertHostIpToCandidates(unchecked((uint)hostIpLong.Value)))
                {
                    if (IsUsablePublicServerIp(value))
                    {
                        return value;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static IEnumerable<string> ConvertHostIpToCandidates(uint hostIp)
    {
        var b1 = hostIp & 0xFF;
        var b2 = (hostIp >> 8) & 0xFF;
        var b3 = (hostIp >> 16) & 0xFF;
        var b4 = (hostIp >> 24) & 0xFF;
        yield return $"{b1}.{b2}.{b3}.{b4}";
        yield return $"{b4}.{b3}.{b2}.{b1}";
    }

    private static string? TryGetPublicIpFromNetwork()
    {
        if (!string.IsNullOrWhiteSpace(_cachedPublicIp) && DateTime.UtcNow < _cachedPublicIpUntilUtc)
        {
            return _cachedPublicIp;
        }

        foreach (var endpoint in PublicIpEndpoints)
        {
            try
            {
                var value = PublicIpHttpClient.GetStringAsync(endpoint)
                    .GetAwaiter()
                    .GetResult()
                    .Trim();

                if (!IsUsablePublicServerIp(value))
                {
                    continue;
                }

                _cachedPublicIp = value;
                _cachedPublicIpUntilUtc = DateTime.UtcNow.AddMinutes(30);
                return value;
            }
            catch
            {
            }
        }

        return null;
    }

    private static bool IsUsablePublicServerIp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!IPAddress.TryParse(value.Trim(), out var address))
        {
            return false;
        }

        if (IPAddress.Any.Equals(address) || IPAddress.None.Equals(address) || IPAddress.Loopback.Equals(address))
        {
            return false;
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            10 => false,
            100 when bytes[1] is >= 64 and <= 127 => false,
            127 => false,
            169 when bytes[1] == 254 => false,
            172 when bytes[1] is >= 16 and <= 31 => false,
            192 when bytes[1] == 168 => false,
            _ => true
        };
    }

    private static bool IsUsablePort(int port)
    {
        return port is > 0 and <= 65535;
    }

    private static bool IsUsableMaxPlayers(int value)
    {
        return value is > 0 and <= 128;
    }

    private static int? TryGetIntCvar(ISwiftlyCore core, string cvarName)
    {
        try
        {
            var intCvar = core.ConVar.Find<int>(cvarName);
            if (intCvar != null)
            {
                return intCvar.Value;
            }
        }
        catch
        {
        }

        try
        {
            var longCvar = core.ConVar.Find<long>(cvarName);
            if (longCvar != null)
            {
                return (int)longCvar.Value;
            }
        }
        catch
        {
        }

        try
        {
            var stringCvar = core.ConVar.Find<string>(cvarName);
            if (stringCvar != null && int.TryParse(stringCvar.Value?.Trim(), out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
        }

        return null;
    }
}
