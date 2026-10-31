using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Core.Interfaces;

public interface ISteamLocalWatcherService : IDisposable
{
    event Action<LocalAchievementUnlock> OnAchievementUnlocked;
    void StartWatching(uint appId);
    void StopWatching();
    uint? CurrentAppId { get; }
    IReadOnlyList<LocalAchievementUnlock> ReadLocalAchievements(uint appId);
}
