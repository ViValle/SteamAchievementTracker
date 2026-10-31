using System.Collections.Concurrent;
using System.Text.Json;
using SteamAchievementTracker.Core.Interfaces;
using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Infrastructure.Services;

public class SessionTracker : ISessionTracker
{
    private readonly ConcurrentDictionary<uint, SessionInfo> _activeSessions = new();
    private readonly ConcurrentDictionary<uint, TimeSpan> _totalPlaytimes = new();
    private readonly string _storageFile;
    private readonly object _fileLock = new();

    public SessionTracker(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamAchievementTracker", "Data");

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _storageFile = Path.Combine(dir, "sessions.json");
        LoadPlaytimes();
    }

    public void StartSession(uint appId, int processId)
    {
        var session = new SessionInfo
        {
            AppId = appId,
            ProcessId = processId,
            StartTime = DateTime.UtcNow
        };
        _activeSessions[appId] = session;
    }

    public void EndSession(uint appId)
    {
        if (_activeSessions.TryRemove(appId, out var session))
        {
            session.EndTime = DateTime.UtcNow;
            var duration = session.Duration;

            _totalPlaytimes.AddOrUpdate(appId, duration, (_, existing) => existing + duration);
            SavePlaytimes();
        }
    }

    public TimeSpan GetCurrentSessionTime(uint appId)
    {
        if (_activeSessions.TryGetValue(appId, out var session))
        {
            return DateTime.UtcNow - session.StartTime;
        }
        return TimeSpan.Zero;
    }

    public TimeSpan GetTotalPlaytime(uint appId)
    {
        var current = GetCurrentSessionTime(appId);
        if (_totalPlaytimes.TryGetValue(appId, out var total))
        {
            return total + current;
        }
        return current;
    }

    private void LoadPlaytimes()
    {
        try
        {
            if (File.Exists(_storageFile))
            {
                var json = File.ReadAllText(_storageFile);
                var dict = JsonSerializer.Deserialize<Dictionary<string, double>>(json);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        if (uint.TryParse(kvp.Key, out var id))
                        {
                            _totalPlaytimes[id] = TimeSpan.FromSeconds(kvp.Value);
                        }
                    }
                }
            }
        }
        catch
        {
            // Игнорируем повреждения файла истории
        }
    }

    private void SavePlaytimes()
    {
        lock (_fileLock)
        {
            try
            {
                var dict = _totalPlaytimes.ToDictionary(k => k.Key.ToString(), v => v.Value.TotalSeconds);
                var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storageFile, json);
            }
            catch
            {
                // Не крашим процесс при ошибке записи статистики
            }
        }
    }
}
