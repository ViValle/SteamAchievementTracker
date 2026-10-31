using SteamAchievementTracker.Core.Models;
using SteamAchievementTracker.Infrastructure.Services;
using Xunit;

namespace SteamAchievementTracker.Tests;

public class RankEvaluatorTests
{
    private readonly RankEvaluator _evaluator = new();

    [Theory]
    [InlineData(85.5, AchievementRank.Bronze)]
    [InlineData(50.1, AchievementRank.Bronze)]
    [InlineData(50.0, AchievementRank.Silver)]
    [InlineData(25.0, AchievementRank.Silver)]
    [InlineData(15.0, AchievementRank.Silver)]
    [InlineData(14.9, AchievementRank.Gold)]
    [InlineData(2.1, AchievementRank.Gold)]
    [InlineData(0.1, AchievementRank.Gold)]
    public void Evaluate_Returns_Correct_Rank_Based_On_Percentage(double globalPct, AchievementRank expected)
    {
        var rank = _evaluator.Evaluate(globalPct);
        Assert.Equal(expected, rank);
    }

    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(5, 5, true)]
    [InlineData(4, 5, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 10, false)]
    public void IsDiamondTriggered_Requires_100_Percent_Completion(int unlocked, int total, bool expected)
    {
        var result = _evaluator.IsDiamondTriggered(unlocked, total);
        Assert.Equal(expected, result);
    }
}
