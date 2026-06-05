using System.Reflection;
using CS2_Admin.Config;

namespace CS2_Admin.Utils;

public static class CommandAliasResolver
{
    public static HashSet<string> BuildSet(CommandsConfig? commandsConfig)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (commandsConfig == null) return set;

        foreach (var prop in typeof(CommandsConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(List<string>)) continue;
            if (prop.GetValue(commandsConfig) is not List<string> list) continue;
            foreach (var alias in list)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    set.Add(alias);
                }
            }
        }

        return set;
    }

    public static bool IsCommandPrefix(HashSet<string> aliases, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (aliases.Count == 0) return false;
        if (!text.StartsWith("!") && !text.StartsWith("/")) return false;
        var firstToken = text.Split(' ', 2)[0].TrimStart('!', '/');
        return !string.IsNullOrEmpty(firstToken) && aliases.Contains(firstToken);
    }
}
