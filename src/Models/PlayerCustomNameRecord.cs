namespace CS2_Admin.Models;

public class PlayerCustomNameRecord
{
    public long Id { get; set; }
    public ulong SteamId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
