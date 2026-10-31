using SteamAchievementTracker.Core.Interfaces;
using SteamAchievementTracker.Core.Models;
using SteamAchievementTracker.Injector.Safety;
using Xunit;

namespace SteamAchievementTracker.Tests;

public class MockSteamApiClient : ISteamApiClient
{
    public bool VacStatusToReturn { get; set; } = false;

    public Task<GameMetadata?> GetGameDetailsAsync(uint appId, string? apiKey = null, CancellationToken ct = default)
    {
        return Task.FromResult<GameMetadata?>(new GameMetadata { AppId = appId, Name = "Test Game" });
    }

    public Task<Dictionary<string, double>> GetGlobalAchievementPercentagesAsync(uint appId, CancellationToken ct = default)
    {
        return Task.FromResult(new Dictionary<string, double>());
    }

    public Task<bool> IsVacSecuredAsync(uint appId, CancellationToken ct = default)
    {
        return Task.FromResult(VacStatusToReturn);
    }

    public Task<bool> SyncUserAchievementsAsync(GameMetadata game, ulong? steamId64 = null, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }
}

public class AntiCheatScannerTests
{
    [Fact]
    public async Task Scanner_Aborts_When_Steam_App_Is_Vac_Secured()
    {
        var mockApi = new MockSteamApiClient { VacStatusToReturn = true };
        var scanner = new AntiCheatScanner(mockApi);

        // AppID 730 (CS:GO / CS2 с VAC)
        var result = await scanner.EvaluateProcessSafetyAsync(1234, 730);

        Assert.False(result.IsSafe);
        Assert.Equal("Valve Anti-Cheat (VAC)", result.DetectedThreat);
        Assert.Contains("Valve Anti-Cheat", result.Reason);
    }

    [Fact]
    public async Task Scanner_Allows_Safe_Singleplayer_App_Without_VAC()
    {
        var mockApi = new MockSteamApiClient { VacStatusToReturn = false };
        var scanner = new AntiCheatScanner(mockApi);

        // Тестируем процесс самого текущего тестового раннера (он чист от EAC/BattlEye)
        int currentPid = Environment.ProcessId;
        var result = await scanner.EvaluateProcessSafetyAsync(currentPid, 413150);

        Assert.True(result.IsSafe);
        Assert.Null(result.DetectedThreat);
    }
}
