using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SteamAchievementTracker.Core.Models;

public class GameMetadata : INotifyPropertyChanged
{
    private uint _appId;
    private string _name = string.Empty;
    private string? _headerImageUrl;
    private string? _localHeaderImagePath;
    private List<Achievement> _achievements = new();
    private DateTime _lastUpdated = DateTime.UtcNow;

    public uint AppId
    {
        get => _appId;
        set => SetField(ref _appId, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string? HeaderImageUrl
    {
        get => _headerImageUrl;
        set
        {
            if (SetField(ref _headerImageUrl, value))
                OnPropertyChanged(nameof(DisplayHeaderSource));
        }
    }

    public string? LocalHeaderImagePath
    {
        get => _localHeaderImagePath;
        set
        {
            if (SetField(ref _localHeaderImagePath, value))
                OnPropertyChanged(nameof(DisplayHeaderSource));
        }
    }

    public List<Achievement> Achievements
    {
        get => _achievements;
        set
        {
            if (SetField(ref _achievements, value))
                NotifyStatsChanged();
        }
    }

    public int TotalAchievements
    {
        get => Achievements.Count;
        set { }
    }

    public int UnlockedCount
    {
        get => Achievements.Count(a => a.IsUnlocked);
        set { }
    }

    public double CompletionPercentage
    {
        get => TotalAchievements > 0
            ? Math.Round((double)UnlockedCount / TotalAchievements * 100.0, 1)
            : 0.0;
        set { }
    }

    public bool IsDiamondEarned
    {
        get => TotalAchievements > 0 && UnlockedCount == TotalAchievements;
        set { }
    }

    public string DisplayHeaderSource
    {
        get
        {
            if (!string.IsNullOrEmpty(LocalHeaderImagePath) && System.IO.File.Exists(LocalHeaderImagePath))
                return LocalHeaderImagePath;
            if (!string.IsNullOrEmpty(HeaderImageUrl))
                return HeaderImageUrl;
            return $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{AppId}/header.jpg";
        }
        set { }
    }

    public DateTime LastUpdated
    {
        get => _lastUpdated;
        set => SetField(ref _lastUpdated, value);
    }

    public void NotifyStatsChanged()
    {
        OnPropertyChanged(nameof(TotalAchievements));
        OnPropertyChanged(nameof(UnlockedCount));
        OnPropertyChanged(nameof(CompletionPercentage));
        OnPropertyChanged(nameof(IsDiamondEarned));
        OnPropertyChanged(nameof(DisplayHeaderSource));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
