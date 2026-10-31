namespace SteamAchievementTracker.Core.Models;

public class LocalAchievementUnlock
{
    public uint AppId { get; set; }
    public string ApiName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public DateTime UnlockTime { get; set; } = DateTime.UtcNow;
}
