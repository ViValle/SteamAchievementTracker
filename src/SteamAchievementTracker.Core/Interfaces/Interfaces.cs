using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Core.Interfaces;

public interface ISteamApiClient
{
    Task<GameMetadata?> GetGameDetailsAsync(uint appId, string? apiKey = null, CancellationToken ct = default);
    Task<Dictionary<string, double>> GetGlobalAchievementPercentagesAsync(uint appId, CancellationToken ct = default);
    Task<bool> IsVacSecuredAsync(uint appId, CancellationToken ct = default);
    Task<bool> SyncUserAchievementsAsync(GameMetadata game, ulong? steamId64 = null, CancellationToken ct = default);
}

public interface ICacheManager
{
    Task<GameMetadata?> LoadGameAsync(uint appId);
    Task SaveGameAsync(GameMetadata game);
    Task<IReadOnlyList<GameMetadata>> GetAllCachedGamesAsync();
    Task RemoveGameAsync(uint appId);
    Task ClearAllCacheAsync();
    long GetCacheSizeBytes();
}

public interface IImageCacheService
{
    Task<string?> CacheImageAsync(uint appId, string key, string url);
    void ClearImageCache();
    long GetImageCacheSizeBytes();
}

public interface IRankEvaluator
{
    AchievementRank Evaluate(double globalPercentage);
    bool IsDiamondTriggered(int unlockedCount, int totalCount);
}

public interface IAntiCheatScanner
{
    Task<AntiCheatCheckResult> EvaluateProcessSafetyAsync(int processId, uint appId);
}

public interface IAudioService
{
    void PlayRankSound(AchievementRank rank);
    void EnsureAudioAssets();
    double Volume { get; set; } // От 0.0 до 1.0 (или 0% - 100%)
}

public interface IInjectorEngine
{
    Task<bool> InjectAsync(int processId, uint appId, string payloadPath);
    Task<bool> EjectAsync(int processId);
}

public interface ISteamApiHookService : IDisposable
{
    event Action<string>? OnAchievementReported;
    bool Attach(int processId);
    void Detach();
    bool IsAttached { get; }
}

public interface ISessionTracker
{
    void StartSession(uint appId, int processId);
    void EndSession(uint appId);
    TimeSpan GetCurrentSessionTime(uint appId);
    TimeSpan GetTotalPlaytime(uint appId);
}

public interface IOverlayRenderer
{
    void ShowNotification(NotificationPayload payload);
    void DismissNotification();
}

public interface IIpcService : IDisposable
{
    event Action<NotificationPayload>? OnNotificationReceived;
    Task StartServerAsync(string pipeName, CancellationToken ct = default);
    Task SendNotificationAsync(string pipeName, NotificationPayload payload);
    void StopServer();
}
