using System.IO;
using System.Text;
using SteamAchievementTracker.Infrastructure.Steam;
using Xunit;

namespace SteamAchievementTracker.Tests;

public class SteamLocalWatcherTests
{
    [Fact]
    public void SteamUserDetector_FindsSteamPathOrActiveId()
    {
        var steamPath = SteamUserDetector.GetSteamPath();
        var steamId3 = SteamUserDetector.DetectActiveSteamId3();
        var steamId64 = SteamUserDetector.DetectActiveSteamId64();

        // На машине пользователя установлен Steam, проверяем корректность обнаружения
        if (steamPath != null)
        {
            Assert.True(Directory.Exists(steamPath));
        }

        if (steamId3.HasValue)
        {
            Assert.True(steamId3.Value > 0);
            Assert.NotNull(steamId64);
            Assert.Equal(SteamUserDetector.SteamId64Base + steamId3.Value, steamId64.Value);
        }
    }

    [Fact]
    public void SteamLocalWatcher_CanReadLocalAchievements_IfFileExists()
    {
        using var watcher = new SteamLocalWatcherService();
        
        // Проверяем чтение реального файла Dead Island Definitive Edition (AppID 383150), если есть
        var unlocks = watcher.ReadLocalAchievements(383150);
        if (unlocks.Count > 0)
        {
            Assert.Contains(unlocks, u => u.ApiName == "ACH_41");
            var ach41 = unlocks.First(u => u.ApiName == "ACH_41");
            Assert.Equal("ACH_41", ach41.ApiName);
            Assert.False(string.IsNullOrEmpty(ach41.DisplayName));
        }
    }

    [Fact]
    public void SteamStatsBinaryReader_CanRead_UserGameStats_And_Schema()
    {
        var steamPath = SteamUserDetector.GetSteamPath();
        var steamId3 = SteamUserDetector.DetectActiveSteamId3();
        if (steamPath != null && steamId3.HasValue)
        {
            var statsPath = Path.Combine(steamPath, "appcache", "stats", $"UserGameStats_{steamId3.Value}_383150.bin");
            if (File.Exists(statsPath))
            {
                var unlocks = SteamStatsBinaryReader.ReadLocalUnlockedAchievements(383150, steamId3.Value, steamPath);
                Assert.True(unlocks.Count >= 24);
                Assert.Contains(unlocks, u => u.ApiName.Equals("ACH_10", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(unlocks, u => u.ApiName.Equals("ACH_41", StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
