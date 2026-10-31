using System.Net.Http;
using SteamAchievementTracker.Core.Models;
using SteamAchievementTracker.Infrastructure.Services;
using SteamAchievementTracker.Infrastructure.Steam;
using Xunit;

namespace SteamAchievementTracker.Tests;

public class SteamSyncIntegrationTests
{
    [Fact]
    public void SteamUserDetector_Detects_Active_Steam_User()
    {
        var steamId = SteamUserDetector.DetectActiveSteamId64();
        // В CI окружении без установленного клиента Steam (например, GitHub Actions) тест завершается успешно
        if (!steamId.HasValue)
        {
            return;
        }

        Assert.True(steamId.Value > 76561197960265728UL);
    }

    [Fact]
    public async Task SteamWebApiClient_Fetches_DeadIsland_And_Syncs_User_Achievements()
    {
        using var httpClient = new HttpClient();
        var evaluator = new RankEvaluator();
        var client = new SteamWebApiClient(httpClient, evaluator);

        // AppID 383150 = Dead Island Definitive Edition
        var game = await client.GetGameDetailsAsync(383150);

        Assert.NotNull(game);
        Assert.Equal("Dead Island Definitive Edition", game.Name);
        Assert.False(string.IsNullOrWhiteSpace(game.HeaderImageUrl));
        Assert.True(game.TotalAchievements >= 50);

        // На машине разработчика или при наличии активного Steam проверяем синхронизацию
        const ulong testSteamId64 = 76561198059804869UL;
        if (game.UnlockedCount == 0)
        {
            await client.SyncUserAchievementsAsync(game, testSteamId64);
        }

        if (game.UnlockedCount > 0)
        {
            Assert.True(game.CompletionPercentage > 0.0);
            var firstUnlocked = game.Achievements.FirstOrDefault(a => a.IsUnlocked);
            Assert.NotNull(firstUnlocked);
            Assert.False(string.IsNullOrEmpty(firstUnlocked.DisplayIconSource));
            Assert.Contains("✔", firstUnlocked.UnlockStatusText);
            Assert.Contains("% игроков", firstUnlocked.FormattedGlobalPercent);
            Assert.DoesNotContain("451%", firstUnlocked.FormattedGlobalPercent);
        }
    }
}
