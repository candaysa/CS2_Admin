using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_mutes")]
public class Mute
{
    [Key]
    public int Id { get; set; }

    [Column("steamid")]
    public ulong SteamId { get; set; }

    [Column("admin_name")]
    public string AdminName { get; set; } = string.Empty;

    [Column("admin_steamid")]
    public ulong AdminSteamId { get; set; }

    [Column("reason")]
    public string Reason { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [Column("status")]
    public string StatusValue { get; set; } = MuteStatusNames.Active;

    [NotMapped]
    public MuteStatus Status
    {
        get => MuteStatusNames.Parse(StatusValue);
        set => StatusValue = MuteStatusNames.ToDatabaseValue(value);
    }

    [Column("unmute_admin_name")]
    public string? UnmuteAdminName { get; set; }

    [Column("unmute_admin_steamid")]
    public ulong? UnmuteAdminSteamId { get; set; }

    [Column("unmute_reason")]
    public string? UnmuteReason { get; set; }

    [Column("unmute_date")]
    public DateTime? UnmuteDate { get; set; }

    [NotMapped]
    public bool IsPermanent => ExpiresAt == null;
    
    [NotMapped]
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    
    [NotMapped]
    public bool IsActive => Status == MuteStatus.Active && !IsExpired;
    
    [NotMapped]
    public TimeSpan? TimeRemaining => ExpiresAt?.Subtract(DateTime.UtcNow);
}

public enum MuteStatus
{
    Active,
    Expired,
    Unmuted
}

public static class MuteStatusNames
{
    public const string Active = "active";
    public const string Expired = "expired";
    public const string Unmuted = "unmuted";

    public static MuteStatus Parse(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            Active or "0" => MuteStatus.Active,
            Expired or "1" => MuteStatus.Expired,
            Unmuted or "2" => MuteStatus.Unmuted,
            _ => Enum.TryParse<MuteStatus>(value, ignoreCase: true, out var parsed)
                ? parsed
                : MuteStatus.Active
        };
    }

    public static string ToDatabaseValue(MuteStatus status)
    {
        return status switch
        {
            MuteStatus.Active => Active,
            MuteStatus.Expired => Expired,
            MuteStatus.Unmuted => Unmuted,
            _ => Active
        };
    }
}
