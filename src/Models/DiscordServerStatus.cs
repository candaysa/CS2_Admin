using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_discord_server_status")]
public class DiscordServerStatus
{
    [Key]
    public long Id { get; set; }

    [Column("server_id")]
    public string ServerId { get; set; } = string.Empty;

    [Column("hub_key")]
    public string HubKey { get; set; } = "default";

    [Column("server_name")]
    public string ServerName { get; set; } = string.Empty;

    [Column("button_label")]
    public string ButtonLabel { get; set; } = string.Empty;

    [Column("server_ip")]
    public string ServerIp { get; set; } = string.Empty;

    [Column("server_port")]
    public int ServerPort { get; set; }

    [Column("map_name")]
    public string MapName { get; set; } = string.Empty;

    [Column("player_count")]
    public int PlayerCount { get; set; }

    [Column("max_players")]
    public int MaxPlayers { get; set; }

    [Column("join_url")]
    public string JoinUrl { get; set; } = string.Empty;

    [Column("last_heartbeat_at")]
    public DateTime LastHeartbeatAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
