using CS2_Admin.Utils;
using SwiftlyS2.Shared;

namespace CS2_Admin.Services;

public static class LocalizerHelper
{
    // Named placeholder → positional index mapping.
    // Convention:  {admin}=0  {target}=1  (third-arg names)=2  (fourth-arg names)=3
    private static readonly (string Name, string Positional)[] NamedPlaceholders =
    [
        ("{admin}",           "{0}"),
        ("{target}",          "{1}"),
        // third-arg semantic aliases (all map to {2})
        ("{duration}",        "{2}"),
        ("{seconds}",         "{2}"),
        ("{value}",           "{2}"),
        ("{state}",           "{2}"),
        ("{damage}",          "{2}"),
        ("{scale}",           "{2}"),
        ("{multiplier}",      "{2}"),
        ("{health}",          "{2}"),
        ("{amount}",          "{2}"),
        ("{item}",            "{2}"),
        ("{map}",             "{2}"),
        ("{team}",            "{2}"),
        ("{count}",           "{2}"),
        ("{name}",            "{2}"),
        // fourth-arg semantic aliases (all map to {3})
        ("{reason}",          "{3}"),
        ("{damage_per_tick}", "{3}"),
    ];

    /// <summary>
    /// Replaces named placeholders such as {admin}, {target}, {duration}, {reason}
    /// with positional ones ({0}, {1}, {2}, {3}) so string.Format works correctly.
    /// Falls back gracefully if the key already uses positional placeholders.
    /// </summary>
    private static string ApplyNamedPlaceholders(string format)
    {
        foreach (var (name, positional) in NamedPlaceholders)
            format = format.Replace(name, positional);
        return format;
    }

    public static string Get(ISwiftlyCore core, string key)
    {
        var raw = PluginLocalizer.Get(core)[key];
        return ApplyNamedPlaceholders(raw);
    }

    public static string Get(ISwiftlyCore core, string key, params object[] args)
    {
        // Get the raw translation string (no formatting), apply named→positional mapping, then format.
        try
        {
            var raw = PluginLocalizer.Get(core)[key];
            return string.Format(ApplyNamedPlaceholders(raw), args);
        }
        catch
        {
            // Fall back to the default Swiftly formatter if something goes wrong.
            return PluginLocalizer.Get(core)[key, args];
        }
    }

    public static string GetWithFallback(ISwiftlyCore core, string key, string fallback)
    {
        try
        {
            var val = PluginLocalizer.Get(core)[key];
            val = ApplyNamedPlaceholders(val);
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
            var raw = PluginLocalizer.Get(core)[key];
            var val = string.Format(ApplyNamedPlaceholders(raw), args);
            return string.Equals(val, key, StringComparison.OrdinalIgnoreCase) ? string.Format(fallback, args) : val;
        }
        catch
        {
            return string.Format(fallback, args);
        }
    }
}
