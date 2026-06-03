using System.Net;
using System.Text.RegularExpressions;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Utils;

internal static class DiscordHelpers
{
    private static readonly Regex TargetSteamIdRegex = new(
        @"\((?<steamid>\d{17})\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string EscapeMarkdown(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }

    internal static string BuildSteamProfileUrl(ulong steamId)
    {
        return steamId > 0
            ? $"https://steamcommunity.com/profiles/{steamId}"
            : "https://steamcommunity.com/";
    }

    internal static string BuildSteamProfileUrl(string? steamId)
    {
        return ulong.TryParse(steamId, out var parsedSteamId)
            ? BuildSteamProfileUrl(parsedSteamId)
            : BuildSteamProfileUrl(0);
    }

    internal static string CountryCodeToDiscordFlag(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return ":flag_white:";
        }

        var normalized = countryCode.Trim().ToLowerInvariant();
        return normalized.Length == 2 && normalized.All(c => c is >= 'a' and <= 'z')
            ? $":flag_{normalized}:"
            : ":flag_white:";
    }

    internal static string FormatPlaytime(int totalMinutes)
    {
        if (totalMinutes <= 0)
        {
            return "0m";
        }

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours == 0)
        {
            return $"{minutes}m";
        }

        return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
    }

    internal static string FormatDurationFromSeconds(long totalSeconds)
    {
        if (totalSeconds <= 0)
        {
            return "0m";
        }

        var totalMinutes = (int)Math.Floor(totalSeconds / 60d);
        if (totalMinutes < 60)
        {
            return $"{totalMinutes}m";
        }

        var days = totalMinutes / (60 * 24);
        var hours = (totalMinutes / 60) % 24;
        var minutes = totalMinutes % 60;

        if (days > 0)
        {
            return minutes == 0
                ? $"{days}d {hours}h"
                : $"{days}d {hours}h {minutes}m";
        }

        return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
    }

    internal static string GetRankPrefix(int rank)
    {
        return rank switch
        {
            1 => ":first_place:",
            2 => ":second_place:",
            3 => ":third_place:",
            _ => $"`{rank}.`"
        };
    }

    internal static string MaskChannelId(string? channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return "-";
        }

        var value = channelId.Trim();
        return value.Length <= 8
            ? value
            : $"...{value[^6..]}";
    }

    internal static string NormalizeIp(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return string.Empty;
        }

        var value = ipAddress.Trim();
        var colonIndex = value.IndexOf(':');
        if (colonIndex > 0 && value.Count(c => c == ':') == 1)
        {
            value = value[..colonIndex];
        }

        return value;
    }

    internal static bool IsPrivateOrLocalIp(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || ipAddress.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(ipAddress, out var address))
        {
            return true;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        return address.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => IsPrivateIpv4(address.GetAddressBytes()),
            System.Net.Sockets.AddressFamily.InterNetworkV6 => address.IsIPv6LinkLocal || address.IsIPv6SiteLocal,
            _ => true
        };
    }

    internal static bool IsPrivateIpv4(byte[] bytes)
    {
        if (bytes.Length != 4)
        {
            return true;
        }

        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    internal static string TrimLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "Server";
        }

        var trimmed = label.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }

    internal static string TrimPlayerName(string? playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return "Unknown";
        }

        var trimmed = playerName.Trim();
        return trimmed.Length <= 64 ? trimmed : trimmed[..64];
    }

    internal static ulong? TryExtractSteamId(string? targetValue)
    {
        if (string.IsNullOrWhiteSpace(targetValue))
        {
            return null;
        }

        var match = TargetSteamIdRegex.Match(targetValue.Trim());
        if (!match.Success)
        {
            return null;
        }

        return ulong.TryParse(match.Groups["steamid"].Value, out var steamId) ? steamId : null;
    }

    internal static string StripTrailingSteamId(string? targetValue)
    {
        if (string.IsNullOrWhiteSpace(targetValue))
        {
            return "-";
        }

        return TargetSteamIdRegex.Replace(targetValue.Trim(), string.Empty).Trim();
    }

    internal static object[]? BuildLinkButtonComponents(params (string Label, string Url)[] links)
    {
        var validLinks = links
            .Where(x => !string.IsNullOrWhiteSpace(x.Label) && !string.IsNullOrWhiteSpace(x.Url))
            .Take(5)
            .Select(x => new
            {
                type = 2,
                style = 5,
                label = TrimLabel(x.Label),
                url = x.Url
            })
            .ToArray();

        if (validLinks.Length == 0)
        {
            return null;
        }

        return
        [
            new
            {
                type = 1,
                components = validLinks
            }
        ];
    }

    internal static string? GetEmbedTitle(object embed)
    {
        return embed.GetType().GetProperty("title")?.GetValue(embed) as string;
    }

    internal static string BuildSteamProfileMarkdown(string? playerName, ulong steamId)
    {
        var safeName = EscapeMarkdown(TrimPlayerName(playerName));
        return steamId > 0
            ? $"**[{safeName}]({BuildSteamProfileUrl(steamId)})**"
            : $"**{safeName}**";
    }

    internal static string BuildSteamProfileMarkdown(string? playerName, string? steamId)
    {
        var safeName = EscapeMarkdown(TrimPlayerName(playerName));
        if (PlayerUtils.TryParseSteamId(steamId ?? string.Empty, out var steamId64) && steamId64 > 0)
        {
            return $"**[{safeName}]({BuildSteamProfileUrl(steamId64)})**";
        }

        return $"**{safeName}**";
    }
}
