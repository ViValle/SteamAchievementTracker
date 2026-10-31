namespace SteamAchievementTracker.Core.Models;

public enum AchievementRank
{
    Bronze,    // > 50%
    Silver,    // 15% - 50%
    Gold,      // < 15%
    Diamond    // 100% unlocked in the game
}

public static class AchievementRankExtensions
{
    public static string GetDisplayName(this AchievementRank rank) => rank switch
    {
        AchievementRank.Bronze => "Бронза",
        AchievementRank.Silver => "Серебро",
        AchievementRank.Gold => "Золото",
        AchievementRank.Diamond => "Платина (100%)",
        _ => "Неизвестно"
    };

    public static string GetHexColor(this AchievementRank rank) => rank switch
    {
        AchievementRank.Bronze => "#CD7F32",
        AchievementRank.Silver => "#C0C0C0",
        AchievementRank.Gold => "#FFD700",
        AchievementRank.Diamond => "#00F0FF",
        _ => "#FFFFFF"
    };

    public static (byte R, byte G, byte B) GetRgbColor(this AchievementRank rank) => rank switch
    {
        AchievementRank.Bronze => (205, 127, 50),
        AchievementRank.Silver => (192, 192, 192),
        AchievementRank.Gold => (255, 215, 0),
        AchievementRank.Diamond => (0, 240, 255),
        _ => (255, 255, 255)
    };
}
