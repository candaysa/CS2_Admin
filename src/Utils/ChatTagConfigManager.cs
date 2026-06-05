using CS2_Admin.Config;
using CS2_Admin.Database;
using SwiftlyS2.Shared;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CS2_Admin.Utils;

public class ChatTagConfigManager
{
    private const string FileName = "tags.json";
    private const string SectionName = "CS2AdminTags";
    private static readonly string[] SwiftlyColorNames =
    [
        "default",
        "white",
        "silver",
        "gray",
        "grey",
        "lightyellow",
        "yellow",
        "gold",
        "lightred",
        "red",
        "darkred",
        "olive",
        "lime",
        "green",
        "lightblue",
        "blue",
        "darkblue",
        "bluegrey",
        "lightpurple",
        "purple",
        "magenta"
    ];
    private static readonly HashSet<string> SwiftlyColorSet = new(SwiftlyColorNames, StringComparer.OrdinalIgnoreCase);
    private readonly ISwiftlyCore _core;
    private readonly object _sync = new();

    public ChatTagsFileConfig Config { get; private set; } = new();

    public ChatTagConfigManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public void Load()
    {
        var path = _core.Configuration.GetConfigPath(FileName);
        var loaded = LoadInternal(path);
        if (loaded == null)
        {
            Config = new ChatTagsFileConfig();
            Save();
            return;
        }

        var requiresNormalizationPersist = RequiresNormalizationPersist(loaded);
        Config = Normalize(loaded);
        if (requiresNormalizationPersist)
        {
            Save();
        }
    }

    public async Task SyncWithGroupsAsync(GroupDbManager groupManager)
    {
        var groups = await groupManager.GetAllGroupsAsync();
        var groupNames = new HashSet<string>(groups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);

        bool changed = false;
        lock (_sync)
        {
            Config = Normalize(Config);

            foreach (var name in groupNames)
            {
                if (Config.Groups.ContainsKey(name))
                {
                    continue;
                }

                // Yeni eklenen grup için preset renk ata (admin=kırmızı, mod=mavi vs.)
                // GetPresetStyle zaten tüm normalizasyonu yapıyor.
                Config.Groups[name] = GetPresetStyle(name);
                changed = true;
            }

            var stale = Config.Groups.Keys
                .Where(key => !groupNames.Contains(key))
                .ToList();

            foreach (var key in stale)
            {
                Config.Groups.Remove(key);
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    public ChatTagGroupStyle GetStyleForGroup(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return CreateDefaultStyle();
        }

        var key = groupName.Trim();

        lock (_sync)
        {
            if (Config.Groups.TryGetValue(key, out var style))
            {
                return NormalizeStyle(style);
            }
        }

        // Config'de tanımsız — bilinen grup ise preset renk ata, değilse default
        return GetPresetStyle(key);
    }

    // Bilinen yaygın grup adları için hazır renk paleti.
    // Config'de tanımsız olsalar bile chat'te renkli tag gözükmesini sağlar.
    private static ChatTagGroupStyle GetPresetStyle(string groupName)
    {
        return groupName.ToLowerInvariant() switch
        {
            "owner" or "headadmin" or "founder"
                => new ChatTagGroupStyle { ChatColor = "[white]",   TagColor = "[red]",       NameColor = "[gold]" },
            "admin" or "administrator"
                => new ChatTagGroupStyle { ChatColor = "[white]",   TagColor = "[darkred]",   NameColor = "[red]" },
            "senioradmin" or "sradmin" or "srmod"
                => new ChatTagGroupStyle { ChatColor = "[white]",   TagColor = "[purple]",    NameColor = "[lightpurple]" },
            "moderator" or "mod"
                => new ChatTagGroupStyle { ChatColor = "[white]",   TagColor = "[blue]",      NameColor = "[lightblue]" },
            "helper" or "support"
                => new ChatTagGroupStyle { ChatColor = "[white]",   TagColor = "[green]",     NameColor = "[lime]" },
            "vip" or "vip+"
                => new ChatTagGroupStyle { ChatColor = "[white]",   TagColor = "[gold]",      NameColor = "[yellow]" },
            "tester"
                => new ChatTagGroupStyle { ChatColor = "[white]",   TagColor = "[olive]",     NameColor = "[lightyellow]" },
            _ => CreateDefaultStyle()
        };
    }

    public void Save()
    {
        var path = _core.Configuration.GetConfigPath(FileName);
        var wrapper = new Dictionary<string, ChatTagsFileConfig>
        {
            [SectionName] = Normalize(Config)
        };

        var json = JsonSerializer.Serialize(wrapper, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static ChatTagsFileConfig Normalize(ChatTagsFileConfig config)
    {
        config.Version = ChatTagsFileConfig.CurrentVersion;
        config.PlayerTag = string.IsNullOrWhiteSpace(config.PlayerTag) ? "PLAYER" : config.PlayerTag.Trim();
        // Legacy "Enabled" alanı kaldırıldı. Eski config'lerdeki "Enabled" artık
        // "ChatEnabled"ı ezmeyecek — sadece null yapılarak temizlenir.
        if (config.Enabled.HasValue)
        {
            config.Enabled = null;
        }

        var defaultSupportedColors = new ChatTagsFileConfig().SupportedColors;
        config.SupportedColors = defaultSupportedColors;
        config.Groups ??= new Dictionary<string, ChatTagGroupStyle>();

        var normalized = new Dictionary<string, ChatTagGroupStyle>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in config.Groups)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            normalized[pair.Key.Trim()] = NormalizeStyle(pair.Value);
        }

        config.Groups = normalized;
        return config;
    }

    private static ChatTagGroupStyle NormalizeStyle(ChatTagGroupStyle? style)
    {
        style ??= new ChatTagGroupStyle();
        style.ChatColor = NormalizeColor(style.ChatColor, string.Empty);
        style.TagColor = NormalizeColor(style.TagColor, string.Empty);
        style.NameColor = NormalizeColor(style.NameColor, string.Empty);
        return style;
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('#'))
        {
            var alias = trimmed[1..].Trim();
            if (SwiftlyColorSet.Contains(alias))
            {
                return $"[{alias.ToLowerInvariant()}]";
            }

            return fallback;
        }

        if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length > 2)
        {
            var name = trimmed[1..^1].Trim();
            if (SwiftlyColorSet.Contains(name))
            {
                return $"[{name.ToLowerInvariant()}]";
            }

            return fallback;
        }

        if (SwiftlyColorSet.Contains(trimmed))
        {
            return $"[{trimmed.ToLowerInvariant()}]";
        }

        return fallback;
    }

    private static ChatTagGroupStyle CreateDefaultStyle()
    {
        // Config'de tanımsız ve bilinen grup da değilse bu renkler kullanılır.
        // Boş bırakırsak chat'te "[ ]" parantezleri renksiz ve çirkin gözükür.
        return new ChatTagGroupStyle
        {
            ChatColor = "[white]",
            TagColor = "[green]",
            NameColor = "[default]"
        };
    }

    private static bool RequiresNormalizationPersist(ChatTagsFileConfig config)
    {
        if (config.Version != ChatTagsFileConfig.CurrentVersion)
        {
            return true;
        }

        // Legacy "Enabled" alanı hâlâ set edilmiş — temizlenmesi için persist gerekli
        if (config.Enabled != null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(config.PlayerTag))
        {
            return true;
        }



        if (config.SupportedColors == null || config.SupportedColors.Count == 0)
        {
            return true;
        }

        var expected = new ChatTagsFileConfig().SupportedColors;
        if (!config.SupportedColors.SequenceEqual(expected))
        {
            return true;
        }

        if (config.Groups == null)
        {
            return true;
        }

        foreach (var pair in config.Groups)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
            {
                return true;
            }
        }

        return false;
    }

    private static ChatTagsFileConfig? LoadInternal(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var raw = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(raw) as JsonObject;
            if (root == null)
            {
                return null;
            }

            if (root[SectionName] is JsonObject section)
            {
                return section.Deserialize<ChatTagsFileConfig>();
            }

            return root.Deserialize<ChatTagsFileConfig>();
        }
        catch
        {
            return null;
        }
    }
}
