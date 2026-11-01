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
    private FileSystemWatcher? _libraryCacheWatcher;
    private FileSystemWatcher? _statsWatcher;
    private System.Threading.Timer? _pollTimer;

    private DateTime _lastKnownLibraryCacheTime = DateTime.MinValue;
    private DateTime _lastKnownStatsTime = DateTime.MinValue;
    private DateTime _lastKnownProgressTime = DateTime.MinValue;
    private int _lastKnownProgressUnlockedCount = -1;

    private uint? _currentAppId;
    private string? _currentLibraryCachePath;
    private string? _currentUserStatsPath;
    private string? _currentProgressPath;
    private string? _steamPath;
    private uint? _steamId3;
    private bool _isDisposed;

    public event Action<LocalAchievementUnlock>? OnAchievementUnlocked;

    public uint? CurrentAppId => _currentAppId;

    public void StartWatching(uint appId)
    {
        lock (_lock)
        {
            if (_currentAppId == appId && (_libraryCacheWatcher != null || _pollTimer != null))
                return;

            StopWatchingInternal();

            _currentAppId = appId;
            _steamPath = SteamUserDetector.GetSteamPath();
            _steamId3 = SteamUserDetector.DetectActiveSteamId3();
            _currentLibraryCachePath = SteamUserDetector.GetLibraryCachePath(appId);
            _currentUserStatsPath = SteamUserDetector.GetUserGameStatsPath(appId);
            _currentProgressPath = SteamUserDetector.GetAchievementProgressPath();

            // Инициализируем уже известные ачивки, чтобы не спамить уведомлениями о старых
            _knownUnlocked.Clear();
            var existing = ReadLocalAchievements(appId);
            foreach (var ach in existing)
            {
                _knownUnlocked.Add(ach.ApiName);
            }

            if (!string.IsNullOrEmpty(_currentProgressPath) && File.Exists(_currentProgressPath))
            {
                _lastKnownProgressUnlockedCount = ReadUnlockedCountFromProgress(_currentProgressPath, appId);
                _lastKnownProgressTime = File.GetLastWriteTimeUtc(_currentProgressPath);
            }

            if (!string.IsNullOrEmpty(_currentUserStatsPath) && File.Exists(_currentUserStatsPath))
            {
                _lastKnownStatsTime = File.GetLastWriteTimeUtc(_currentUserStatsPath);
            }

            if (!string.IsNullOrEmpty(_currentLibraryCachePath) && File.Exists(_currentLibraryCachePath))
            {
                _lastKnownLibraryCacheTime = File.GetLastWriteTimeUtc(_currentLibraryCachePath);
            }

            Debug.WriteLine($"[SteamLocalWatcher] Запущен многоуровневый мониторинг для AppID {appId}. Известных ачивок: {_knownUnlocked.Count}, счетчик прогресса: {_lastKnownProgressUnlockedCount}");

            // 1. Наблюдатель за librarycache (achievement_progress.json и {appId}.json)
            if (!string.IsNullOrEmpty(_currentLibraryCachePath))
            {
                var dir = Path.GetDirectoryName(_currentLibraryCachePath);
                if (Directory.Exists(dir))
                {
                    try
                    {
                        _libraryCacheWatcher = new FileSystemWatcher(dir, "*.json")
                        {
                            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                            EnableRaisingEvents = true
                        };
                        _libraryCacheWatcher.Changed += OnFileChanged;
                        _libraryCacheWatcher.Created += OnFileChanged;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SteamLocalWatcher] Ошибка инициализации LibraryCache FileSystemWatcher: {ex.Message}");
                    }
                }
            }

            // 2. Наблюдатель за appcache/stats (UserGameStats_*.bin)
            if (!string.IsNullOrEmpty(_currentUserStatsPath))
            {
                var statsDir = Path.GetDirectoryName(_currentUserStatsPath);
                var statsFileName = Path.GetFileName(_currentUserStatsPath);
                if (Directory.Exists(statsDir))
                {
                    try
                    {
                        _statsWatcher = new FileSystemWatcher(statsDir, statsFileName)
                        {
                            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                            EnableRaisingEvents = true
                        };
                        _statsWatcher.Changed += OnFileChanged;
                        _statsWatcher.Created += OnFileChanged;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SteamLocalWatcher] Ошибка инициализации Stats FileSystemWatcher: {ex.Message}");
                    }
                }
            }

            // 3. 1-секундный таймер-поллер для 100% гарантии реакции
            _pollTimer = new System.Threading.Timer(_ => CheckFilesForNewUnlocks(), null, 800, 800);
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
        _currentLibraryCachePath = null;
        _currentUserStatsPath = null;
        _currentProgressPath = null;

        if (_libraryCacheWatcher != null)
        {
            _libraryCacheWatcher.EnableRaisingEvents = false;
            _libraryCacheWatcher.Changed -= OnFileChanged;
            _libraryCacheWatcher.Created -= OnFileChanged;
            _libraryCacheWatcher.Dispose();
            _libraryCacheWatcher = null;
        }

        if (_statsWatcher != null)
        {
            _statsWatcher.EnableRaisingEvents = false;
            _statsWatcher.Changed -= OnFileChanged;
            _statsWatcher.Created -= OnFileChanged;
            _statsWatcher.Dispose();
            _statsWatcher = null;
        }

        if (_pollTimer != null)
        {
            _pollTimer.Dispose();
            _pollTimer = null;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Task.Delay(80).ContinueWith(_ => CheckFilesForNewUnlocks());
    }

    private void CheckFilesForNewUnlocks()
    {
        if (_isDisposed) return;

        uint? appId;
        string? libraryCachePath;
        string? statsPath;
        string? progressPath;

        lock (_lock)
        {
            appId = _currentAppId;
            libraryCachePath = _currentLibraryCachePath;
            statsPath = _currentUserStatsPath;
            progressPath = _currentProgressPath;
        }

        if (!appId.HasValue) return;

        bool hasChange = false;

        // 1. Проверяем UserGameStats бинарный кэш (Steam Client обновляет его первым в реальном времени)
        if (!string.IsNullOrEmpty(statsPath) && File.Exists(statsPath))
        {
            try
            {
                var wt = File.GetLastWriteTimeUtc(statsPath);
                if (wt != _lastKnownStatsTime)
                {
                    _lastKnownStatsTime = wt;
                    hasChange = true;
                }
            }
            catch { }
        }

        // 2. Проверяем achievement_progress.json (счетчик разблокированных ачивок)
        if (!string.IsNullOrEmpty(progressPath) && File.Exists(progressPath))
        {
            try
            {
                var wt = File.GetLastWriteTimeUtc(progressPath);
                if (wt != _lastKnownProgressTime)
                {
                    _lastKnownProgressTime = wt;
                    var count = ReadUnlockedCountFromProgress(progressPath, appId.Value);
                    if (count > _lastKnownProgressUnlockedCount)
                    {
                        _lastKnownProgressUnlockedCount = count;
                        hasChange = true;
                    }
                }
            }
            catch { }
        }

        // 3. Проверяем librarycache/{appId}.json
        if (!string.IsNullOrEmpty(libraryCachePath) && File.Exists(libraryCachePath))
        {
            try
            {
                var wt = File.GetLastWriteTimeUtc(libraryCachePath);
                if (wt != _lastKnownLibraryCacheTime)
                {
                    _lastKnownLibraryCacheTime = wt;
                    hasChange = true;
                }
            }
            catch { }
        }

        if (!hasChange) return;

        try
        {
            var currentUnlocks = ReadLocalAchievements(appId.Value);
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
            Debug.WriteLine($"[SteamLocalWatcher] Ошибка при проверке файлов: {ex.Message}");
        }
    }

    public IReadOnlyList<LocalAchievementUnlock> ReadLocalAchievements(uint appId)
    {
        var dict = new Dictionary<string, LocalAchievementUnlock>(StringComparer.OrdinalIgnoreCase);

        var steamPath = _steamPath ?? SteamUserDetector.GetSteamPath();
        var steamId3 = _steamId3 ?? SteamUserDetector.DetectActiveSteamId3();

        // 1. Чтение напрямую из бинарного кэша статистики Steam Client (мгновенно и точно)
        if (!string.IsNullOrEmpty(steamPath) && steamId3.HasValue)
        {
            var binaryUnlocks = SteamStatsBinaryReader.ReadLocalUnlockedAchievements(appId, steamId3.Value, steamPath);
            foreach (var u in binaryUnlocks)
            {
                dict[u.ApiName] = u;
            }
        }

        // 2. Чтение из librarycache/{appId}.json (обогащаем DisplayName, Description, IconUrl)
        var libraryCachePath = _currentLibraryCachePath ?? SteamUserDetector.GetLibraryCachePath(appId);
        if (!string.IsNullOrEmpty(libraryCachePath) && File.Exists(libraryCachePath))
        {
            var jsonUnlocks = ReadLocalAchievementsFromFile(libraryCachePath, appId);
            foreach (var u in jsonUnlocks)
            {
                if (dict.TryGetValue(u.ApiName, out var existing))
                {
                    if (!string.IsNullOrEmpty(u.DisplayName)) existing.DisplayName = u.DisplayName;
                    if (!string.IsNullOrEmpty(u.Description)) existing.Description = u.Description;
                    if (!string.IsNullOrEmpty(u.IconUrl)) existing.IconUrl = u.IconUrl;
                    if (u.UnlockTime > existing.UnlockTime) existing.UnlockTime = u.UnlockTime;
                }
                else
                {
                    dict[u.ApiName] = u;
                }
            }
        }

        return dict.Values.ToList();
    }

    private static int ReadUnlockedCountFromProgress(string progressJsonPath, uint appId)
    {
        if (!File.Exists(progressJsonPath)) return 0;
        try
        {
            using var stream = new FileStream(progressJsonPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("mapCache", out var mapCache) && mapCache.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in mapCache.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() >= 2)
                    {
                        if (item[0].TryGetUInt32(out var id) && id == appId)
                        {
                            if (item[1].TryGetProperty("unlocked", out var unlockedElem))
                            {
                                return unlockedElem.GetInt32();
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return 0;
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
            bool isAchieved = item.TryGetProperty("bAchieved", out var achElem) && achElem.GetBoolean();
            if (!isAchieved) continue;

            string? id = item.TryGetProperty("strID", out var idElem) ? idElem.GetString() : null;
            if (string.IsNullOrEmpty(id)) continue;

            string? name = item.TryGetProperty("strName", out var nameElem) ? nameElem.GetString() : id;
            string? desc = item.TryGetProperty("strDescription", out var descElem) ? descElem.GetString() : string.Empty;
            string? img = item.TryGetProperty("strImage", out var imgElem) ? imgElem.GetString() : null;

            DateTime unlockTime = DateTime.UtcNow;
            if (item.TryGetProperty("rtUnlocked", out var timeElem) && timeElem.GetInt64() > 0)
            {
                unlockTime = DateTimeOffset.FromUnixTimeSeconds(timeElem.GetInt64()).UtcDateTime;
            }

            results[id] = new LocalAchievementUnlock
            {
                AppId = appId,
                ApiName = id,
                DisplayName = name ?? id,
                Description = desc ?? string.Empty,
                IconUrl = img,
                UnlockTime = unlockTime
            };
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopWatching();
        GC.SuppressFinalize(this);
    }
}
