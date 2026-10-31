using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Core.Interfaces;

public interface ITranslationService
{
    Task<string> TranslateAsync(string text, string targetLanguage = "ru", CancellationToken ct = default);
    Task TranslateGameAchievementsAsync(GameMetadata game, string targetLanguage = "ru", IProgress<int>? progress = null, CancellationToken ct = default);
}
