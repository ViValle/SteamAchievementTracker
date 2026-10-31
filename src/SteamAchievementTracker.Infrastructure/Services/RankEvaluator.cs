using SteamAchievementTracker.Core.Interfaces;
using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Infrastructure.Services;

public class RankEvaluator : IRankEvaluator
{
    public AchievementRank Evaluate(double globalPercentage)
    {
        return globalPercentage switch
        {
            > 50.0 => AchievementRank.Bronze,
            >= 15.0 => AchievementRank.Silver,
            _ => AchievementRank.Gold
        };
    }

    public bool IsDiamondTriggered(int unlockedCount, int totalCount)
    {
        return totalCount > 0 && unlockedCount >= totalCount;
    }
}
