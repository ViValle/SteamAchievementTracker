namespace SteamAchievementTracker.Core.Models;

public record AntiCheatCheckResult(
    bool IsSafe,
    string? DetectedThreat,
    string Reason,
    int ScannedModulesCount = 0,
    DateTime Timestamp = default)
{
    public DateTime Timestamp { get; init; } = Timestamp == default ? DateTime.UtcNow : Timestamp;
}
