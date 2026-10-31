using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SteamAchievementTracker.Core.Models;

public class Achievement : INotifyPropertyChanged
{
    private string _apiName = string.Empty;
    private string _displayName = string.Empty;
    private string _description = string.Empty;
    private string _iconUrl = string.Empty;
    private string _iconGrayUrl = string.Empty;
    private string? _localIconPath;
    private double _globalPercent;
    private AchievementRank _rank;
    private bool _isUnlocked;
    private DateTime? _unlockTime;

    public string ApiName
    {
        get => _apiName;
        set => SetField(ref _apiName, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public string IconUrl
    {
        get => _iconUrl;
        set
        {
            if (SetField(ref _iconUrl, value))
                OnPropertyChanged(nameof(DisplayIconSource));
        }
    }

    public string IconGrayUrl
    {
        get => _iconGrayUrl;
        set
        {
            if (SetField(ref _iconGrayUrl, value))
                OnPropertyChanged(nameof(DisplayIconSource));
        }
    }

    public string? LocalIconPath
    {
        get => _localIconPath;
        set
        {
            if (SetField(ref _localIconPath, value))
                OnPropertyChanged(nameof(DisplayIconSource));
        }
    }

    public double GlobalPercent
    {
        get => _globalPercent;
        set
        {
            if (SetField(ref _globalPercent, value))
                OnPropertyChanged(nameof(FormattedGlobalPercent));
        }
    }

    public string FormattedGlobalPercent
    {
        get => $"{GlobalPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}% игроков";
        set { }
    }

    public AchievementRank Rank
    {
        get => _rank;
        set
        {
            if (SetField(ref _rank, value))
            {
                OnPropertyChanged(nameof(RankColor));
                OnPropertyChanged(nameof(RankDisplayName));
            }
        }
    }

    public bool IsUnlocked
    {
        get => _isUnlocked;
        set
        {
            if (SetField(ref _isUnlocked, value))
            {
                OnPropertyChanged(nameof(UnlockStatusText));
                OnPropertyChanged(nameof(UnlockStatusColor));
                OnPropertyChanged(nameof(UnlockStatusBg));
                OnPropertyChanged(nameof(UnlockStatusBorder));
                OnPropertyChanged(nameof(CardBorderBrush));
                OnPropertyChanged(nameof(DisplayOpacity));
            }
        }
    }

    public DateTime? UnlockTime
    {
        get => _unlockTime;
        set
        {
            if (SetField(ref _unlockTime, value))
                OnPropertyChanged(nameof(UnlockStatusText));
        }
    }

    public string RankColor => Rank.GetHexColor();
    public string RankDisplayName => Rank.GetDisplayName();

    public string DisplayIconSource
    {
        get
        {
            if (!string.IsNullOrEmpty(LocalIconPath) && System.IO.File.Exists(LocalIconPath))
                return LocalIconPath;
            if (!string.IsNullOrEmpty(IconUrl))
                return IconUrl;
            return IconGrayUrl ?? string.Empty;
        }
        set { }
    }

    public double DisplayOpacity
    {
        get => IsUnlocked ? 1.0 : 0.65;
        set { }
    }

    public string UnlockStatusText
    {
        get => IsUnlocked
            ? (UnlockTime.HasValue ? $"✔ Разблокировано ({UnlockTime.Value.ToLocalTime():dd.MM.yyyy})" : "✔ Разблокировано")
            : "🔒 Заблокировано";
        set { }
    }

    public string UnlockStatusColor
    {
        get => IsUnlocked ? "#2ED573" : "#687387";
        set { }
    }

    public string UnlockStatusBg
    {
        get => IsUnlocked ? "#153322" : "#1A202C";
        set { }
    }

    public string UnlockStatusBorder
    {
        get => IsUnlocked ? "#205535" : "#283042";
        set { }
    }

    public string CardBorderBrush
    {
        get => IsUnlocked ? "#284A36" : "#252B3C";
        set { }
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
