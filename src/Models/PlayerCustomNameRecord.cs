using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_player_custom_names")]
public class PlayerCustomNameRecord
{
    [Key]
    public long Id { get; set; }

    [Column("steamid")]
    public ulong SteamId { get; set; }

    [Column("player_name")]
    public string PlayerName { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
