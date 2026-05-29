using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_player_sessions")]
public class PlayerSessionRecord
{
    [Key]
    public long Id { get; set; }

    [Column("steamid")]
    public ulong SteamId { get; set; }

    [Column("player_name")]
    public string PlayerName { get; set; } = string.Empty;

    [Column("server_id")]
    public string ServerId { get; set; } = string.Empty;

    [Column("connected_at")]
    public DateTime ConnectedAt { get; set; }

    [Column("disconnected_at")]
    public DateTime? DisconnectedAt { get; set; }

    [Column("duration_seconds")]
    public int DurationSeconds { get; set; }

    [Column("last_userid")]
    public int? LastUserId { get; set; }

    [Column("last_ip")]
    public string? LastIp { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
