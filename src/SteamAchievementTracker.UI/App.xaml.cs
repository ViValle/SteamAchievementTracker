using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SteamAchievementTracker.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void LogError(string prefix, Exception? ex)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamAchievementTracker", "Logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "app.log"), $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {prefix}: {ex}\n");
        }
        catch { }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError("CRITICAL UI EXCEPTION", e.Exception);
        Debug.WriteLine($"[CRITICAL UI EXCEPTION] {e.Exception}");
        MessageBox.Show(
            $"Произошла ошибка в работе интерфейса:\n{e.Exception.Message}\n\nПриложение продолжит работу.",
            "Предупреждение",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true; // Предотвращаем аварийное завершение процесса
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogError("CRITICAL DOMAIN EXCEPTION", ex);
            Debug.WriteLine($"[CRITICAL DOMAIN EXCEPTION] {ex}");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogError("TASK EXCEPTION", e.Exception);
        Debug.WriteLine($"[TASK EXCEPTION] {e.Exception}");
        e.SetObserved(); // Предотвращаем сбой от фоновых задач
    }
}
