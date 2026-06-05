using System.Text.RegularExpressions;

namespace CS2_Admin.Utils;

public static class SafeName
{
    private static readonly Regex SupplementaryChars = new(@"[\uD800-\uDBFF\uDC00-\uDFFF]", RegexOptions.Compiled);

    public static string ForPlayer(string? name, ulong steamId, int maxLength = 64)
    {
        var s = string.IsNullOrWhiteSpace(name) ? steamId.ToString() : name.Trim();
        s = SupplementaryChars.Replace(s, string.Empty);
        if (string.IsNullOrWhiteSpace(s))
        {
            s = steamId.ToString();
        }
        return s.Length <= maxLength ? s : s.Substring(0, maxLength);
    }

    public static string ForReason(string? reason, int maxLength = 2048)
    {
        var s = (reason ?? string.Empty).Trim();
        s = SupplementaryChars.Replace(s, string.Empty);
        if (s.Length <= maxLength)
        {
            return s;
        }
        return s.Substring(0, maxLength);
    }
}
