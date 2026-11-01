using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Overlay.Wpf;

public partial class WpfOverlayWindow : Window
{
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int GWL_EXSTYLE = -20;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private readonly Storyboard _slideInStoryboard;
    private readonly Storyboard _slideOutStoryboard;
    private CancellationTokenSource? _hideCts;

    public WpfOverlayWindow()
    {
        InitializeComponent();

        _slideInStoryboard = (Storyboard)FindResource("SlideInStoryboard");
        _slideOutStoryboard = (Storyboard)FindResource("SlideOutStoryboard");

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Делаем окно полностью кликабельным насквозь (Click-Through) и без перехвата фокуса
        var hwnd = new WindowInteropHelper(this).Handle;
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        // Позиционируем по центру сверху экрана (Top-Center)
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        Left = (screenWidth - Width) / 2.0;
        Top = 18.0;
    }

    public void DisplayNotification(NotificationPayload payload)
    {
        Dispatcher.Invoke(() =>
        {
            _hideCts?.Cancel();
            _hideCts = new CancellationTokenSource();
            var token = _hideCts.Token;

            // Позиционируем и показываем
            Visibility = Visibility.Visible;
            Topmost = true;

            // Настройка текста и ранга
            TitleText.Text = payload.Title;
            DescriptionText.Text = payload.Description;
            RarityPercentageText.Text = $"• {payload.GlobalPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}% игроков";

            var hexColor = payload.Rank.GetHexColor();
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hexColor)!;

            RootBorder.BorderBrush = brush;
            GlowEffect.Color = ((SolidColorBrush)brush).Color;
            RankBadgeText.Text = payload.Rank.GetDisplayName().ToUpperInvariant();
            RankBadgeText.Foreground = brush;

            RankIconText.Text = payload.Rank switch
            {
                AchievementRank.Bronze => "🥉",
                AchievementRank.Silver => "🥈",
                AchievementRank.Gold => "🥇",
                AchievementRank.Diamond => "💎",
                _ => "🏆"
            };

            // Загрузка иконки (поддерживаем как локальный кэш, так и прямой URL)
            if (!string.IsNullOrEmpty(payload.IconPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    if (payload.IconPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        payload.IconPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        bitmap.UriSource = new Uri(payload.IconPath);
                    }
                    else if (System.IO.File.Exists(payload.IconPath))
                    {
                        bitmap.UriSource = new Uri(payload.IconPath, UriKind.Absolute);
                    }
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    IconImage.Source = bitmap;
                }
                catch
                {
                    IconImage.Source = null;
                }
            }
            else
            {
                IconImage.Source = null;
            }

            // Вычисляем динамические габариты окна под текст и центрируем
            UpdateLayout();
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            Left = (screenWidth - ActualWidth) / 2.0;
            Top = 18.0;

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW | SWP_NOACTIVATE);
            }

            // Сбрасываем предыдущие анимации перед новым показом
            _slideOutStoryboard.Stop(this);
            _slideInStoryboard.Stop(this);
            RootBorder.Opacity = 0.0;
            RootTransform.Y = -40;

            // Запуск анимации появления (Fade-in + Slide-down)
            _slideInStoryboard.Begin(this, true);

            // Автоматическое скрытие через 5 секунд (достаточно для чтения длинного описания)
            Task.Delay(5000, token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    Dispatcher.Invoke(() =>
                    {
                        _slideInStoryboard.Stop(this);
                        _slideOutStoryboard.Begin(this, true);
                    });
                }
            }, TaskScheduler.Default);
        });
    }

    public void Dismiss()
    {
        Dispatcher.Invoke(() =>
        {
            _hideCts?.Cancel();
            _slideInStoryboard.Stop(this);
            _slideOutStoryboard.Begin(this, true);
        });
    }
}
