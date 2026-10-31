using System.Windows;
using SteamAchievementTracker.Core.Interfaces;
using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Overlay.Wpf;

public class WpfOverlayController : IOverlayRenderer, IDisposable
{
    private WpfOverlayWindow? _window;
    private readonly object _lock = new();

    public void EnsureWindowCreated()
    {
        lock (_lock)
        {
            if (_window == null)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _window = new WpfOverlayWindow();
                    _window.Show();
                });
            }
        }
    }

    public void ShowNotification(NotificationPayload payload)
    {
        EnsureWindowCreated();
        _window?.DisplayNotification(payload);
    }

    public void DismissNotification()
    {
        _window?.Dismiss();
    }

    public void Dispose()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _window?.Close();
            _window = null;
        });
        GC.SuppressFinalize(this);
    }
}
