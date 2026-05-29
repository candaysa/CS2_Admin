using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_actions_log")]
public class AdminActionLogRecord
{
    [Key]
    public long Id { get; set; }

    [Column("action")]
    public string Action { get; set; } = string.Empty;

    [Column("target_steamid")]
    public ulong? TargetSteamId { get; set; }

    [Column("target_name")]
    public string? TargetName { get; set; }

    [Column("target_userid")]
    public int? TargetUserId { get; set; }

    [Column("admin_name")]
    public string AdminName { get; set; } = string.Empty;

    [Column("admin_steamid")]
    public ulong? AdminSteamId { get; set; }

    [Column("reason")]
    public string? Reason { get; set; }

    [Column("server_id")]
    public string ServerId { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
