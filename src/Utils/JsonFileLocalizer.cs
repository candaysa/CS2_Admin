using System.Globalization;
using System.Text.Json;
using SwiftlyS2.Shared.Translation;

namespace CS2_Admin.Utils;

public sealed class JsonFileLocalizer : ILocalizer
{
    private readonly Dictionary<string, string> _primary;
    private readonly Dictionary<string, string> _fallback;
    public LocalizerDiagnostics Diagnostics { get; }

    private JsonFileLocalizer(
        Dictionary<string, string> primary,
        Dictionary<string, string> fallback,
        LocalizerDiagnostics diagnostics)
    {
        _primary = primary;
        _fallback = fallback;
        Diagnostics = diagnostics;
    }

    public string this[string key] => Resolve(key);

    public string this[string key, params object[] args]
    {
        get
        {
            var text = Resolve(key);
            if (args.Length == 0)
            {
                return text;
            }

            try
            {
                return string.Format(CultureInfo.InvariantCulture, text, args);
            }
            catch
            {
                return text;
            }
        }
    }

    public static JsonFileLocalizer? TryCreate(string directoryPath, string language)
    {
        try
        {
            var normalized = (language ?? "en").Trim().ToLowerInvariant();
            var primaryFile = Path.Combine(directoryPath, $"{normalized}.jsonc");
            var fallbackFile = Path.Combine(directoryPath, "en.jsonc");

            var primary = TryLoadMap(directoryPath, $"{normalized}.jsonc");
            var fallback = TryLoadMap(directoryPath, "en.jsonc");

            if (primary.Count == 0 && fallback.Count == 0)
            {
                return null;
            }

            var missingPrimaryKeys = fallback.Keys
                .Where(key => !primary.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sameAsFallbackCount = fallback.Keys.Count(key =>
                primary.TryGetValue(key, out var primaryValue)
                && !string.IsNullOrWhiteSpace(primaryValue)
                && fallback.TryGetValue(key, out var fallbackValue)
                && string.Equals(primaryValue.Trim(), fallbackValue.Trim(), StringComparison.Ordinal));

            var diagnostics = new LocalizerDiagnostics(
                normalized,
                primary.Count,
                fallback.Count,
                missingPrimaryKeys,
                sameAsFallbackCount);

            return new JsonFileLocalizer(primary, fallback, diagnostics);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CS2Admin] ERROR in JsonFileLocalizer.TryCreate: {ex.Message}");
            Console.WriteLine($"[CS2Admin] StackTrace: {ex.StackTrace}");
            return null;
        }
    }

    private string Resolve(string key)
    {
        if (_primary.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (_fallback.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return key;
    }

    private static Dictionary<string, string> TryLoadMap(string directoryPath, string fileName)
    {
        var path = Path.Combine(directoryPath, fileName);
        string? raw = null;

        if (File.Exists(path))
        {
            try
            {
                raw = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CS2_Admin] Failed to read {path}: {ex.Message}");
            }
        }

        if (raw == null)
        {
            var resourceName = $"CS2_Admin.Translations.{fileName}";
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                raw = reader.ReadToEnd();
            }
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var cleaned = RemoveSingleLineComments(raw);
        var document = JsonDocument.Parse(cleaned, new JsonDocumentOptions
        {
            AllowTrailingCommas = true
        });
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in document.RootElement.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String)
            {
                map[item.Name] = item.Value.GetString() ?? string.Empty;
            }
        }

        return map;
    }

    private static string RemoveSingleLineComments(string input)
    {
        var output = new System.Text.StringBuilder(input.Length);
        var inString = false;
        var escapeNext = false;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (inString)
            {
                output.Append(c);

                if (escapeNext)
                {
                    escapeNext = false;
                    continue;
                }

                if (c == '\\')
                {
                    escapeNext = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                output.Append(c);
                continue;
            }

            if (c == '/' && i + 1 < input.Length && input[i + 1] == '/')
            {
                while (i < input.Length && input[i] != '\n')
                {
                    i++;
                }

                if (i < input.Length)
                {
                    output.Append(input[i]);
                }

                continue;
            }

            output.Append(c);
        }

        return output.ToString();
    }
}

public sealed class LocalizerDiagnostics
{
    public string Language { get; }
    public int PrimaryKeyCount { get; }
    public int FallbackKeyCount { get; }
    public int SameAsFallbackCount { get; }
    public double SameAsFallbackRate => FallbackKeyCount == 0 ? 0 : (double)SameAsFallbackCount / FallbackKeyCount;
    public int MissingPrimaryCount => MissingPrimaryKeys.Count;
    public double MissingPrimaryRate => FallbackKeyCount == 0 ? 0 : (double)MissingPrimaryCount / FallbackKeyCount;
    public IReadOnlyList<string> MissingPrimaryKeys { get; }

    public LocalizerDiagnostics(
        string language,
        int primaryKeyCount,
        int fallbackKeyCount,
        IReadOnlyList<string> missingPrimaryKeys,
        int sameAsFallbackCount)
    {
        Language = language;
        PrimaryKeyCount = primaryKeyCount;
        FallbackKeyCount = fallbackKeyCount;
        MissingPrimaryKeys = missingPrimaryKeys;
        SameAsFallbackCount = sameAsFallbackCount;
    }

    public string GetMissingSample(int maxCount = 8)
    {
        if (MissingPrimaryKeys.Count == 0 || maxCount <= 0)
        {
            return "-";
        }

        var sample = MissingPrimaryKeys.Take(maxCount);
        return string.Join(", ", sample);
    }
}
