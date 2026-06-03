using CS2_Admin.Utils;
using SwiftlyS2.Shared;

namespace CS2_Admin.Services;

public static class LocalizerHelper
{
    public static string Get(ISwiftlyCore core, string key)
    {
        return PluginLocalizer.Get(core)[key];
    }

    public static string Get(ISwiftlyCore core, string key, params object[] args)
    {
        return PluginLocalizer.Get(core)[key, args];
    }

    public static string GetWithFallback(ISwiftlyCore core, string key, string fallback)
    {
        try
        {
            var val = PluginLocalizer.Get(core)[key];
            return string.Equals(val, key, StringComparison.OrdinalIgnoreCase) ? fallback : val;
        }
        catch
        {
            return fallback;
        }
    }

    public static string GetWithFallback(ISwiftlyCore core, string key, string fallback, params object[] args)
    {
        try
        {
            var val = PluginLocalizer.Get(core)[key, args];
            return string.Equals(val, key, StringComparison.OrdinalIgnoreCase) ? string.Format(fallback, args) : val;
        }
        catch
        {
            return string.Format(fallback, args);
        }
    }
}
