using System.Net.Http;
using SteamAchievementTracker.Core.Models;
using SteamAchievementTracker.Infrastructure.Services;
using Xunit;

namespace SteamAchievementTracker.Tests;

public class TranslationServiceTests
{
    [Fact]
    public async Task TranslateAsync_Translates_English_Phrase_To_Russian()
    {
        using var httpClient = new HttpClient();
        var translator = new GoogleTranslateService(httpClient);

        var result = await translator.TranslateAsync("Customize 25 weapons.", "ru");

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        // Должен содержать русские буквы
        Assert.True(result.Any(c => (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я')));
    }

    [Fact]
    public async Task TranslateGameAchievementsAsync_Translates_All_Achievements()
    {
        using var httpClient = new HttpClient();
        var translator = new GoogleTranslateService(httpClient);

        var game = new GameMetadata
        {
            AppId = 999,
            Name = "Test Game",
            Achievements = new List<Achievement>
            {
                new Achievement { ApiName = "ACH_1", DisplayName = "First Blood", Description = "Kill 1 enemy" },
                new Achievement { ApiName = "ACH_2", DisplayName = "Root Cause", Description = "Find the cause" }
            }
        };

        await translator.TranslateGameAchievementsAsync(game, "ru");

        Assert.True(game.Achievements[0].DisplayName.Any(c => (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я')));
        Assert.True(game.Achievements[0].Description.Any(c => (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я')));
        Assert.True(game.Achievements[1].DisplayName.Any(c => (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я')));
        Assert.True(game.Achievements[1].Description.Any(c => (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я')));
    }
}
