namespace CS2_Admin.Utils;

public static class SanctionDurationParser
{
    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = 60 * 24;
    private const int MinutesPerWeek = 60 * 24 * 7;
    private const int MinutesPerMonth = 60 * 24 * 30;

    public static bool TryParseToMinutes(string? rawDuration, out int minutes)
    {
        minutes = 0;

        if (string.IsNullOrWhiteSpace(rawDuration))
        {
            return false;
        }

        var normalized = rawDuration.Trim().ToLowerInvariant();
        if (normalized is "perm" or "permanent")
        {
            minutes = -1;
            return true;
        }

        if (int.TryParse(normalized, out var directMinutes))
        {
            if (directMinutes < -1)
            {
                return false;
            }

            minutes = directMinutes;
            return true;
        }

        if (normalized.Length < 2)
        {
            return false;
        }

        var suffix = normalized[^1];
        var valuePart = normalized[..^1];
        if (!long.TryParse(valuePart, out var value) || value < 0)
        {
            return false;
        }

        long computedMinutes = suffix switch
        {
            's' => value == 0 ? 0 : (value + 59) / 60, // round up to nearest minute
            'h' => value * MinutesPerHour,
            'd' => value * MinutesPerDay,
            'w' => value * MinutesPerWeek,
            'm' => value * MinutesPerMonth,
            _ => -1
        };

        if (computedMinutes < 0 || computedMinutes > int.MaxValue)
        {
            return false;
        }

        minutes = (int)computedMinutes;
        return true;
    }
}
