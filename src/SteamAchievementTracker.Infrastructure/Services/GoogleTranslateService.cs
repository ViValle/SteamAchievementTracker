using System.Net.Http;
using System.Text;
using System.Text.Json;
using SteamAchievementTracker.Core.Interfaces;
using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Infrastructure.Services;

public class GoogleTranslateService : ITranslationService
{
    private readonly HttpClient _httpClient;

    public GoogleTranslateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage = "ru", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        if (targetLanguage.Equals("ru", StringComparison.OrdinalIgnoreCase) && ContainsCyrillic(text))
            return text;

        try
        {
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetLanguage}&dt=t&q={Uri.EscapeDataString(text)}";
            var response = await _httpClient.GetStringAsync(url, ct);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var sentences = root[0];
                var sb = new StringBuilder();
                foreach (var s in sentences.EnumerateArray())
                {
                    if (s.ValueKind == JsonValueKind.Array && s.GetArrayLength() > 0)
                    {
                        var translatedPart = s[0].GetString();
                        if (!string.IsNullOrEmpty(translatedPart))
                            sb.Append(translatedPart);
                    }
                }

                var result = sb.ToString();
                return string.IsNullOrWhiteSpace(result) ? text : result;
            }
        }
        catch
        {
            // Фолбэк на оригинальный текст при сетевом сбое
        }

        return text;
    }

    public async Task TranslateGameAchievementsAsync(GameMetadata game, string targetLanguage = "ru", IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (game == null || game.Achievements.Count == 0)
            return;

        int total = game.Achievements.Count;
        int completed = 0;

        await Parallel.ForEachAsync(game.Achievements, new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = ct }, async (ach, token) =>
        {
            if (!ContainsCyrillic(ach.DisplayName))
            {
                ach.DisplayName = await TranslateAsync(ach.DisplayName, targetLanguage, token);
            }

            if (!string.IsNullOrEmpty(ach.Description) && !ContainsCyrillic(ach.Description))
            {
                ach.Description = await TranslateAsync(ach.Description, targetLanguage, token);
            }

            var current = Interlocked.Increment(ref completed);
            progress?.Report((int)((double)current / total * 100));
        });
    }

    private static bool ContainsCyrillic(string text)
    {
        return text.Any(c => (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё');
    }
}
