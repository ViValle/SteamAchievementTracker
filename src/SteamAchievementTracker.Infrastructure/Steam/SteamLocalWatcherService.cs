using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using SteamAchievementTracker.Core.Interfaces;
using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Infrastructure.Steam;

public class SteamLocalWatcherService : ISteamLocalWatcherService
{
    private readonly HashSet<string> _knownUnlocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private FileSystemWatcher? _fileWatcher;
    private System.Threading.Timer? _pollTimer;
    private DateTime _lastKnownWriteTime = DateTime.MinValue;
    private uint? _currentAppId;
    private string? _currentFilePath;
    private bool _isDisposed;

    public event Action<LocalAchievementUnlock>? OnAchievementUnlocked;

    public uint? CurrentAppId => _currentAppId;

    public void StartWatching(uint appId)
    {
        lock (_lock)
        {
            if (_currentAppId == appId && _fileWatcher != null)
                return;

            StopWatchingInternal();

            _currentAppId = appId;
            _currentFilePath = SteamUserDetector.GetLibraryCachePath(appId);

            // Инициализируем уже известные ачивки, чтобы не спамить уведомлениями о старых
            _knownUnlocked.Clear();
            var existing = ReadLocalAchievements(appId);
            foreach (var ach in existing)
            {
                _knownUnlocked.Add(ach.ApiName);
            }

            Debug.WriteLine($"[SteamLocalWatcher] Запущен мониторинг для AppID {appId}. Известных ачивок: {_knownUnlocked.Count}");

            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                var dir = Path.GetDirectoryName(_currentFilePath);
                var fileName = Path.GetFileName(_currentFilePath);

                if (Directory.Exists(dir))
                {
                    try
                    {
                        _fileWatcher = new FileSystemWatcher(dir, fileName)
                        {
                            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                            EnableRaisingEvents = true
                        };

                        _fileWatcher.Changed += OnFileChanged;
                        _fileWatcher.Created += OnFileChanged;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SteamLocalWatcher] Ошибка инициализации FileSystemWatcher: {ex.Message}");
                    }
                }

                if (File.Exists(_currentFilePath))
                {
                    _lastKnownWriteTime = File.GetLastWriteTimeUtc(_currentFilePath);
                }
            }

            // Запускаем таймер периодической проверки (1 сек) как надежную страховку
            _pollTimer = new System.Threading.Timer(_ => CheckFileForNewUnlocks(), null, 1000, 1000);
        }
    }

    public void StopWatching()
    {
        lock (_lock)
        {
            StopWatchingInternal();
        }
    }

    private void StopWatchingInternal()
    {
        _currentAppId = null;
        _currentFilePath = null;

        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Changed -= OnFileChanged;
            _fileWatcher.Created -= OnFileChanged;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }

        if (_pollTimer != null)
        {
            _pollTimer.Dispose();
            _pollTimer = null;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Небольшая задержка, чтобы Steam завершил запись
        Task.Delay(100).ContinueWith(_ => CheckFileForNewUnlocks());
    }

    private void CheckFileForNewUnlocks()
    {
        if (_isDisposed) return;

        string? filePath;
        uint? appId;

        lock (_lock)
        {
            filePath = _currentFilePath;
            appId = _currentAppId;
        }

        if (string.IsNullOrEmpty(filePath) || !appId.HasValue || !File.Exists(filePath))
            return;

        try
        {
            var writeTime = File.GetLastWriteTimeUtc(filePath);
            if (writeTime == _lastKnownWriteTime)
                return;

            _lastKnownWriteTime = writeTime;

            var currentUnlocks = ReadLocalAchievementsFromFile(filePath, appId.Value);
            var newUnlocks = new List<LocalAchievementUnlock>();

            lock (_lock)
            {
                foreach (var unlock in currentUnlocks)
                {
                    if (_knownUnlocked.Add(unlock.ApiName))
                    {
                        newUnlocks.Add(unlock);
                    }
                }
            }

            foreach (var newUnlock in newUnlocks)
            {
                Debug.WriteLine($"[SteamLocalWatcher] ОБНАРУЖЕНО НОВОЕ ДОСТИЖЕНИЕ: {newUnlock.ApiName} ('{newUnlock.DisplayName}')!");
                OnAchievementUnlocked?.Invoke(newUnlock);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SteamLocalWatcher] Ошибка при проверке файла: {ex.Message}");
        }
    }

    public IReadOnlyList<LocalAchievementUnlock> ReadLocalAchievements(uint appId)
    {
        var filePath = SteamUserDetector.GetLibraryCachePath(appId);
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return Array.Empty<LocalAchievementUnlock>();

        return ReadLocalAchievementsFromFile(filePath, appId);
    }

    private static IReadOnlyList<LocalAchievementUnlock> ReadLocalAchievementsFromFile(string filePath, uint appId)
    {
        var results = new Dictionary<string, LocalAchievementUnlock>(StringComparer.OrdinalIgnoreCase);

        string? content = null;
        for (int retry = 0; retry < 5; retry++)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                content = reader.ReadToEnd();
                break;
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(50);
            }
        }

        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<LocalAchievementUnlock>();

        // 1. Попытка парсинга через JsonDocument
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var rootItem in doc.RootElement.EnumerateArray())
                {
                    if (rootItem.ValueKind == JsonValueKind.Array && rootItem.GetArrayLength() >= 2)
                    {
                        var keyName = rootItem[0].GetString();
                        if (string.Equals(keyName, "achievements", StringComparison.OrdinalIgnoreCase))
                        {
                            var dataObj = rootItem[1].GetProperty("data");

                            // Проверяем vecHighlight
                            if (dataObj.TryGetProperty("vecHighlight", out var highlightElem) && highlightElem.ValueKind == JsonValueKind.Array)
                            {
                                ParseJsonAchievementArray(highlightElem, appId, results);
                            }

                            // Проверяем vecAchievedHidden
                            if (dataObj.TryGetProperty("vecAchievedHidden", out var hiddenElem) && hiddenElem.ValueKind == JsonValueKind.Array)
                            {
                                ParseJsonAchievementArray(hiddenElem, appId, results);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SteamLocalWatcher] Ошибка JsonDocument парсинга: {ex.Message}");
        }

        // 2. Дополнительный regex поиск для любых пропущенных элементов
        try
        {
            var matches = Regex.Matches(content, @"\{""strID"":""(?<id>[^""]+)"",""strName"":""(?<name>[^""]+)"",""strDescription"":""(?<desc>[^""]*)"",""bAchieved"":(?<ach>true|false)(?:[^}]*?""rtUnlocked"":(?<time>\d+))?(?:[^}]*?""strImage"":""(?<img>[^""]*)"")?");
            foreach (Match m in matches)
            {
                var id = m.Groups["id"].Value;
                var isAchieved = string.Equals(m.Groups["ach"].Value, "true", StringComparison.OrdinalIgnoreCase);
                if (!isAchieved || string.IsNullOrEmpty(id)) continue;

                if (!results.ContainsKey(id))
                {
                    var unlock = new LocalAchievementUnlock
                    {
                        AppId = appId,
                        ApiName = id,
                        DisplayName = m.Groups["name"].Value,
                        Description = m.Groups["desc"].Value,
                        IconUrl = m.Groups["img"].Success ? m.Groups["img"].Value : null
                    };

                    if (m.Groups["time"].Success && long.TryParse(m.Groups["time"].Value, out var ts) && ts > 0)
                    {
                        unlock.UnlockTime = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
                    }

                    results[id] = unlock;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SteamLocalWatcher] Ошибка Regex парсинга: {ex.Message}");
        }

        return results.Values.ToList();
    }

    private static void ParseJsonAchievementArray(JsonElement arrayElem, uint appId, Dictionary<string, LocalAchievementUnlock> results)
    {
        foreach (var item in arrayElem.EnumerateArray())
        {
            if (item.TryGetProperty("strID", out var idProp))
            {
                var id = idProp.GetString();
                if (string.IsNullOrEmpty(id)) continue;

                bool isAchieved = item.TryGetProperty("bAchieved", out var achProp) && achProp.GetBoolean();
                if (!isAchieved) continue;

                var unlock = new LocalAchievementUnlock
                {
                    AppId = appId,
                    ApiName = id,
                    DisplayName = item.TryGetProperty("strName", out var nameProp) ? nameProp.GetString() : id,
                    Description = item.TryGetProperty("strDescription", out var descProp) ? descProp.GetString() : "",
                    IconUrl = item.TryGetProperty("strImage", out var imgProp) ? imgProp.GetString() : null
                };

                if (item.TryGetProperty("rtUnlocked", out var rtProp) && rtProp.TryGetInt64(out var ts) && ts > 0)
                {
                    unlock.UnlockTime = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
                }

                results[id] = unlock;
            }
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        StopWatchingInternal();
    }
}
