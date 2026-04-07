using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Translation;

namespace CS2_Admin.Utils;

public static class PluginLocalizer
{
    private static ILocalizer? _overrideLocalizer;
    private static string? _configuredPrefix;
    private static readonly object Sync = new();

    public static void SetOverride(ILocalizer? localizer)
    {
        lock (Sync)
        {
            _overrideLocalizer = localizer;
        }
    }

    public static void SetConfiguredPrefix(string? prefix)
    {
        lock (Sync)
        {
            _configuredPrefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim();
        }
    }

    public static ILocalizer Get(ISwiftlyCore core)
    {
        lock (Sync)
        {
            var baseLocalizer = _overrideLocalizer ?? core.Localizer;
            if (string.IsNullOrWhiteSpace(_configuredPrefix))
            {
                return baseLocalizer;
            }

            return new PrefixOverrideLocalizer(baseLocalizer, _configuredPrefix);
        }
    }

    private sealed class PrefixOverrideLocalizer(ILocalizer inner, string prefix) : ILocalizer
    {
        public string this[string key]
        {
            get
            {
                if (key.Equals("prefix", StringComparison.OrdinalIgnoreCase))
                {
                    return prefix;
                }

                return inner[key];
            }
        }

        public string this[string key, params object[] args]
        {
            get
            {
                if (key.Equals("prefix", StringComparison.OrdinalIgnoreCase))
                {
                    return prefix;
                }

                return inner[key, args];
            }
        }
    }
}
