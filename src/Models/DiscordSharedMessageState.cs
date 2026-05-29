using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_discord_message_state")]
public class DiscordSharedMessageState
{
    [Key]
    public long Id { get; set; }

    [Column("message_key")]
    public string MessageKey { get; set; } = string.Empty;

    [Column("channel_id")]
    public string ChannelId { get; set; } = string.Empty;

    [Column("message_id")]
    public string MessageId { get; set; } = string.Empty;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
