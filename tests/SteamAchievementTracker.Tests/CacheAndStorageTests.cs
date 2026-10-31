using SteamAchievementTracker.Core.Models;
using SteamAchievementTracker.Infrastructure.Storage;
using Xunit;

namespace SteamAchievementTracker.Tests;

public class CacheAndStorageTests : IDisposable
{
    private readonly string _testDir;
    private readonly ImageCacheService _imageService;
    private readonly LocalCacheManager _cacheManager;

    public CacheAndStorageTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SAT_Test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);

        var http = new HttpClient();
        _imageService = new ImageCacheService(http, Path.Combine(_testDir, "Images"));
        _cacheManager = new LocalCacheManager(_imageService, Path.Combine(_testDir, "Data"));
    }

    [Fact]
    public async Task Save_And_Load_Game_Works_Correctly()
    {
        var game = new GameMetadata
        {
            AppId = 413150,
            Name = "Stardew Valley",
            Achievements = new List<Achievement>
            {
                new()
                {
                    ApiName = "ACH_GREENHORN",
                    DisplayName = "Greenhorn",
                    Description = "Earn 15,000g",
                    GlobalPercent = 78.4,
                    Rank = AchievementRank.Bronze
                },
                new()
                {
                    ApiName = "ACH_FRET_NOT",
                    DisplayName = "Prairie King",
                    Description = "Beat Journey of the Prairie King",
                    GlobalPercent = 1.2,
                    Rank = AchievementRank.Gold
                }
            }
        };

        await _cacheManager.SaveGameAsync(game);

        var loaded = await _cacheManager.LoadGameAsync(413150);
        Assert.NotNull(loaded);
        Assert.Equal("Stardew Valley", loaded.Name);
        Assert.Equal(2, loaded.TotalAchievements);
        Assert.Equal(AchievementRank.Gold, loaded.Achievements[1].Rank);

        var all = await _cacheManager.GetAllCachedGamesAsync();
        Assert.Single(all);

        await _cacheManager.ClearAllCacheAsync();
        var allAfterClear = await _cacheManager.GetAllCachedGamesAsync();
        Assert.Empty(allAfterClear);
    }

    [Fact]
    public async Task SteamWebApiClient_Can_Fetch_Live_Game_Metadata()
    {
        var client = new SteamAchievementTracker.Infrastructure.Steam.SteamWebApiClient(
            new HttpClient(),
            new SteamAchievementTracker.Infrastructure.Services.RankEvaluator());

        // AppID 383150 (Dead Island Definitive Edition)
        var game = await client.GetGameDetailsAsync(383150);

        Assert.NotNull(game);
        Assert.Equal("Dead Island Definitive Edition", game.Name);
        Assert.True(game.TotalAchievements > 50, $"Expected >50 achievements, got {game.TotalAchievements}");
        Assert.Contains(game.Achievements, a => a.Rank == AchievementRank.Gold || a.Rank == AchievementRank.Silver || a.Rank == AchievementRank.Bronze);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }
}
