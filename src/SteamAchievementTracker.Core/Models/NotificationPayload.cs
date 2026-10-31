namespace SteamAchievementTracker.Core.Models;

public class SessionInfo
{
    public uint AppId { get; set; }
    public int ProcessId { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;
}

public class NotificationPayload
{
    public uint AppId { get; set; }
    public string AchievementApiName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AchievementRank Rank { get; set; }
    public double GlobalPercent { get; set; }
    public string? IconPath { get; set; }
    public string? SoundPath { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
