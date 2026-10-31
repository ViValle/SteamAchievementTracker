using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using SteamAchievementTracker.Core.Interfaces;
using SteamAchievementTracker.Core.Models;
using SteamAchievementTracker.Infrastructure.Audio;
using SteamAchievementTracker.Infrastructure.Ipc;
using SteamAchievementTracker.Infrastructure.Services;
using SteamAchievementTracker.Infrastructure.Steam;
using SteamAchievementTracker.Infrastructure.Storage;
using SteamAchievementTracker.Injector;
using SteamAchievementTracker.Injector.Safety;
using SteamAchievementTracker.Overlay.Hooks;
using SteamAchievementTracker.Overlay.Wpf;

namespace SteamAchievementTracker.UI.ViewModels;

public class ProcessItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayText => $"{Name} (PID: {Id})";
}

public class MainViewModel : ViewModelBase
{
    private readonly ISteamApiClient _steamApiClient;
    private readonly ICacheManager _cacheManager;
    private readonly IImageCacheService _imageCache;
    private readonly IRankEvaluator _rankEvaluator;
    private readonly IAntiCheatScanner _antiCheatScanner;
    private readonly IAudioService _audioService;
    private readonly InjectorEngine _injectorEngine;
    private readonly IOverlayRenderer _overlayRenderer;
    private readonly ISessionTracker _sessionTracker;
    private readonly IIpcService _ipcService;
    private readonly SteamApiHook _steamApiHook;
    private readonly ITranslationService _translationService;
    private readonly ISteamLocalWatcherService _localWatcherService;

    private string _inputAppId = "413150"; // По умолчанию пример: Stardew Valley или Witcher 3
    private GameMetadata? _selectedGame;
    private bool _isLoading;
    private string _statusMessage = "Готов к работе.";
    private string _cacheSizeFormatted = "0 KB";
    private ProcessItem? _selectedProcess;
    private AntiCheatCheckResult? _lastSafetyResult;
    private bool _isInjected;
    private int _soundVolume = 45;

    public ObservableCollection<GameMetadata> Games { get; } = new();
    public ObservableCollection<Achievement> FilteredAchievements { get; } = new();
    public ObservableCollection<ProcessItem> RunningProcesses { get; } = new();
    public ObservableCollection<string> ConsoleLogs { get; } = new();

    public int SoundVolume
    {
        get => _soundVolume;
        set
        {
            if (SetField(ref _soundVolume, value))
            {
                _audioService.Volume = value / 100.0;
            }
        }
    }

    public string InputAppId
    {
        get => _inputAppId;
        set => SetField(ref _inputAppId, value);
    }

    public GameMetadata? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetField(ref _selectedGame, value))
            {
                UpdateFilteredAchievements();
                if (value != null)
                {
                    _localWatcherService?.StartWatching(value.AppId);
                }
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetField(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string CacheSizeFormatted
    {
        get => _cacheSizeFormatted;
        set => SetField(ref _cacheSizeFormatted, value);
    }

    public ProcessItem? SelectedProcess
    {
        get => _selectedProcess;
        set => SetField(ref _selectedProcess, value);
    }

    public AntiCheatCheckResult? LastSafetyResult
    {
        get => _lastSafetyResult;
        set => SetField(ref _lastSafetyResult, value);
    }

    public bool IsInjected
    {
        get => _isInjected;
        set => SetField(ref _isInjected, value);
    }

    public ICommand AddGameCommand { get; }
    public ICommand DeleteGameCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand RefreshProcessesCommand { get; }
    public ICommand EvaluateSafetyCommand { get; }
    public ICommand InjectHookCommand { get; }
    public ICommand DetachHookCommand { get; }
    public ICommand TestOverlayCommand { get; }
    public ICommand TestRankCommand { get; }
    public ICommand SyncGameCommand { get; }
    public ICommand TranslateGameCommand { get; }

    public MainViewModel()
    {
        // Инициализация сервисов (Clean Architecture Composition Root)
        var httpClient = new HttpClient();
        _rankEvaluator = new RankEvaluator();
        _steamApiClient = new SteamWebApiClient(httpClient, _rankEvaluator);
        _imageCache = new ImageCacheService(httpClient);
        _cacheManager = new LocalCacheManager(_imageCache);
        _antiCheatScanner = new AntiCheatScanner(_steamApiClient);
        _audioService = new AudioService();
        _audioService.Volume = _soundVolume / 100.0;
        _injectorEngine = new InjectorEngine(_antiCheatScanner);
        _overlayRenderer = new WpfOverlayController();
        _sessionTracker = new SessionTracker();
        _ipcService = new NamedPipeIpcService();
        _steamApiHook = new SteamApiHook();
        _translationService = new GoogleTranslateService(httpClient);
        _localWatcherService = new SteamLocalWatcherService();

        // Подписки на события логов и инжекта
        _injectorEngine.OnLogMessage += msg => AddLog(msg);
        _injectorEngine.OnSafetyEvaluated += res => LastSafetyResult = res;

        _steamApiHook.OnAchievementReported += OnInGameAchievementTriggered;
        _localWatcherService.OnAchievementUnlocked += OnLocalAchievementUnlocked;

        // Команды
        AddGameCommand = new AsyncRelayCommand(AddGameAsync);
        DeleteGameCommand = new AsyncRelayCommand(DeleteGameAsync);
        ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync);
        RefreshProcessesCommand = new RelayCommand(RefreshProcesses);
        EvaluateSafetyCommand = new AsyncRelayCommand(EvaluateSafetyAsync);
        InjectHookCommand = new AsyncRelayCommand(InjectHookAsync);
        DetachHookCommand = new RelayCommand(DetachHook);
        TestOverlayCommand = new RelayCommand(p => TestAchievement(p as Achievement));
        TestRankCommand = new RelayCommand(p => TestRank(p?.ToString()));
        SyncGameCommand = new AsyncRelayCommand(SyncGameAsync);
        TranslateGameCommand = new AsyncRelayCommand(TranslateGameAsync);

        // Начальная загрузка
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        AddLog("[System] Инициализация трекера достижений...");
        RefreshProcesses();
        await LoadCachedGamesAsync();
        UpdateCacheSizeDisplay();

        // Запуск IPC слушателя
        try
        {
            await _ipcService.StartServerAsync("SteamAchievementTracker_Pipe");
            _ipcService.OnNotificationReceived += payload =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _audioService.PlayRankSound(payload.Rank);
                    _overlayRenderer.ShowNotification(payload);
                    AddLog($"[IPC] Получено уведомление о разблокировке: {payload.Title} ({payload.Rank})");
                });
            };
        }
        catch (Exception ex)
        {
            AddLog($"[IPC] Ошибка запуска IPC: {ex.Message}");
        }
    }

    private async Task LoadCachedGamesAsync()
    {
        try
        {
            var cached = await _cacheManager.GetAllCachedGamesAsync();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Games.Clear();
                foreach (var game in cached)
                {
                    Games.Add(game);
                }

                if (Games.Count > 0)
                {
                    SelectedGame = Games[0];
                }
            });

            // Фоновая тихая синхронизация для загруженных игр
            _ = Task.Run(async () =>
            {
                foreach (var game in cached)
                {
                    try
                    {
                        if (await _steamApiClient.SyncUserAchievementsAsync(game))
                        {
                            game.NotifyStatsChanged();
                            await _cacheManager.SaveGameAsync(game);
                        }
                    }
                    catch
                    {
                        // Игнорируем сетевые ошибки тихой фоновой синхронизации
                    }
                }

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    UpdateFilteredAchievements();
                    OnPropertyChanged(nameof(SelectedGame));
                });
            });
        }
        catch (Exception ex)
        {
            AddLog($"[Cache] Ошибка загрузки кэша: {ex.Message}");
        }
    }

    private async Task AddGameAsync()
    {
        if (!uint.TryParse(InputAppId.Trim(), out var appId) || appId == 0)
        {
            MessageBox.Show("Введите корректный числовой Steam AppID.", "Ошибка ввода", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        StatusMessage = $"Загрузка метаданных для AppID {appId}...";
        AddLog($"[WebAPI] Запрос метаданных для AppID {appId}...");

        try
        {
            var metadata = await _steamApiClient.GetGameDetailsAsync(appId);
            if (metadata == null || metadata.Achievements.Count == 0)
            {
                AddLog($"[WebAPI] Предупреждение: Достижения для AppID {appId} не найдены или игра скрыта.");
                if (metadata == null) metadata = new GameMetadata { AppId = appId, Name = $"App {appId}" };
            }

            StatusMessage = $"Кэширование иконок для {metadata.Name}...";
            AddLog($"[Cache] Загрузка и кэширование иконок достижений ({metadata.Achievements.Count} шт.)...");

            // Кэшируем обложку игры
            var headerToDownload = !string.IsNullOrEmpty(metadata.HeaderImageUrl)
                ? metadata.HeaderImageUrl
                : $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";

            metadata.LocalHeaderImagePath = await _imageCache.CacheImageAsync(appId, "header", headerToDownload);
            if (string.IsNullOrEmpty(metadata.HeaderImageUrl))
            {
                metadata.HeaderImageUrl = headerToDownload;
            }

            // Фоновая параллельная загрузка ВСЕХ иконок достижений
            int cachedIconsCount = 0;
            await Parallel.ForEachAsync(metadata.Achievements, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (ach, token) =>
            {
                var iconToDownload = !string.IsNullOrEmpty(ach.IconUrl) ? ach.IconUrl : ach.IconGrayUrl;
                if (!string.IsNullOrEmpty(iconToDownload))
                {
                    ach.LocalIconPath = await _imageCache.CacheImageAsync(appId, ach.ApiName, iconToDownload);
                    if (ach.LocalIconPath != null) Interlocked.Increment(ref cachedIconsCount);
                }
            });

            metadata.NotifyStatsChanged();

            // Автоматический перевод на русский язык, если ачивки на английском
            bool hasRussian = metadata.Achievements.Take(10).Any(a => a.DisplayName.Any(c => (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я')));
            if (!hasRussian && metadata.Achievements.Count > 0)
            {
                StatusMessage = $"Перевод достижений {metadata.Name} на русский язык...";
                AddLog($"[Translator] Ачивки на английском. Выполняется автоматический перевод на русский ({metadata.Achievements.Count} шт.)...");
                await _translationService.TranslateGameAchievementsAsync(metadata, "ru");
                AddLog("[Translator] Автоматический перевод на русский язык успешно завершен!");
            }

            // Сохраняем в кэш
            await _cacheManager.SaveGameAsync(metadata);

            // Обновляем список в UI
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var existing = Games.FirstOrDefault(g => g.AppId == appId);
                if (existing != null) Games.Remove(existing);
                Games.Insert(0, metadata);
                SelectedGame = metadata;
            });

            UpdateCacheSizeDisplay();
            AddLog($"[WebAPI] Игра '{metadata.Name}' успешно добавлена! Всего ачивок: {metadata.Achievements.Count}, получено пользователем: {metadata.UnlockedCount} ({metadata.CompletionPercentage}%), закэшировано иконок: {cachedIconsCount}");
            StatusMessage = $"Игра {metadata.Name} добавлена ({metadata.CompletionPercentage}%).";
        }
        catch (Exception ex)
        {
            AddLog($"[Error] Сбой добавления игры: {ex.Message}");
            StatusMessage = "Ошибка загрузки.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SyncGameAsync()
    {
        if (SelectedGame == null) return;

        IsLoading = true;
        StatusMessage = $"Синхронизация прогресса {SelectedGame.Name}...";
        AddLog($"[Sync] Запуск синхронизации достижений для {SelectedGame.Name} (AppID: {SelectedGame.AppId})...");

        try
        {
            bool success = await _steamApiClient.SyncUserAchievementsAsync(SelectedGame);
            if (success)
            {
                SelectedGame.NotifyStatsChanged();
                await _cacheManager.SaveGameAsync(SelectedGame);
                UpdateFilteredAchievements();
                OnPropertyChanged(nameof(SelectedGame));
                AddLog($"[Sync] Успешно синхронизировано: {SelectedGame.UnlockedCount} из {SelectedGame.TotalAchievements} ({SelectedGame.CompletionPercentage}% завершено).");
                StatusMessage = $"Синхронизировано ({SelectedGame.CompletionPercentage}%).";
            }
            else
            {
                AddLog("[Sync] Не удалось получить данные профиля Steam. Убедитесь, что Steam запущен или профиль открыт.");
                StatusMessage = "Синхронизация не удалась.";
            }
        }
        catch (Exception ex)
        {
            AddLog($"[Sync] Ошибка: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task TranslateGameAsync()
    {
        if (SelectedGame == null || SelectedGame.Achievements.Count == 0) return;

        IsLoading = true;
        StatusMessage = $"Перевод достижений {SelectedGame.Name} на русский язык...";
        AddLog($"[Translator] Запуск перевода {SelectedGame.Achievements.Count} достижений для игры '{SelectedGame.Name}'...");

        try
        {
            var progress = new Progress<int>(pct =>
            {
                StatusMessage = $"Перевод на русский: {pct}%...";
            });

            await _translationService.TranslateGameAchievementsAsync(SelectedGame, "ru", progress);
            SelectedGame.NotifyStatsChanged();
            await _cacheManager.SaveGameAsync(SelectedGame);
            UpdateFilteredAchievements();
            OnPropertyChanged(nameof(SelectedGame));

            AddLog($"[Translator] Перевод успешно завершен и сохранен в кэш! Все {SelectedGame.Achievements.Count} достижений теперь на русском языке.");
            StatusMessage = "Перевод успешно завершен.";
        }
        catch (Exception ex)
        {
            AddLog($"[Translator] Ошибка перевода: {ex.Message}");
            StatusMessage = "Ошибка перевода.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DeleteGameAsync()
    {
        if (SelectedGame == null) return;

        var game = SelectedGame;
        await _cacheManager.RemoveGameAsync(game.AppId);
        Games.Remove(game);
        SelectedGame = Games.FirstOrDefault();
        UpdateCacheSizeDisplay();
        AddLog($"[Cache] Игра '{game.Name}' удалена из трекера.");
    }

    private async Task ClearCacheAsync()
    {
        var result = MessageBox.Show(
            "Вы уверены, что хотите полностью очистить кэш данных и локальных изображений?\nВсе скачанные иконки будут удалены для освобождения места.",
            "Подтверждение очистки кэша",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            await _cacheManager.ClearAllCacheAsync();
            Games.Clear();
            FilteredAchievements.Clear();
            SelectedGame = null;
            UpdateCacheSizeDisplay();
            AddLog("[Cache] Кэш данных и изображений успешно полностью очищен.");
            StatusMessage = "Кэш очищен.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RefreshProcesses()
    {
        RunningProcesses.Clear();
        var procs = Process.GetProcesses()
            .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
            .OrderBy(p => p.ProcessName)
            .Select(p => new ProcessItem { Id = p.Id, Name = p.ProcessName });

        foreach (var p in procs)
        {
            RunningProcesses.Add(p);
        }

        if (RunningProcesses.Count > 0 && SelectedProcess == null)
        {
            SelectedProcess = RunningProcesses[0];
        }
        AddLog($"[ProcessManager] Обновлен список запущенных оконных процессов ({RunningProcesses.Count} процессов).");
    }

    private async Task EvaluateSafetyAsync()
    {
        if (SelectedProcess == null)
        {
            MessageBox.Show("Выберите целевой процесс из списка.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        uint appId = SelectedGame?.AppId ?? 0;
        AddLog($"[AntiCheatScanner] Запуск аудита безопасности для процесса {SelectedProcess.DisplayText}...");

        IsLoading = true;
        try
        {
            var result = await _antiCheatScanner.EvaluateProcessSafetyAsync(SelectedProcess.Id, appId);
            LastSafetyResult = result;

            if (result.IsSafe)
            {
                AddLog($"[VERDICT: SAFE] Процесс безопасен для инжекта. {result.Reason}");
            }
            else
            {
                AddLog($"[VERDICT: DANGEROUS] ОБНАРУЖЕНА УГРОЗА! {result.Reason} (Угроза: {result.DetectedThreat})");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task InjectHookAsync()
    {
        if (SelectedProcess == null)
        {
            MessageBox.Show("Выберите целевой процесс игры.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        uint appId = SelectedGame?.AppId ?? 0;
        AddLog($"[Tracker] Проверка безопасности процесса '{SelectedProcess.DisplayText}' (PID: {SelectedProcess.Id})...");

        var result = await _antiCheatScanner.EvaluateProcessSafetyAsync(SelectedProcess.Id, appId);
        LastSafetyResult = result;

        if (result.IsSafe)
        {
            AddLog($"[AntiCheatScanner] Процесс безопасен ({result.Reason}).");
        }
        else
        {
            AddLog($"[AntiCheatScanner] ВНИМАНИЕ: {result.Reason}. Активирован пассивный режим мониторинга Steam (0% вмешательства в память игры, 0% риск бана).");
        }

        // Активируем отслеживание сессии и локальный перехватчик событий Steam в реальном времени
        _sessionTracker.StartSession(appId, SelectedProcess.Id);
        _localWatcherService.StartWatching(appId);
        IsInjected = true;

        AddLog($"[Tracker] Отслеживание достижений в реальном времени УСПЕШНО АКТИВИРОВАНО!");
        AddLog($"[Tracker] Метод: Steam Client Realtime Engine (0% риск бана, 0% крашей, мгновенное появление оверлея и звука Xbox).");
        StatusMessage = $"Отслеживание активно: {SelectedProcess.Name}";
    }

    private void DetachHook()
    {
        if (SelectedGame != null)
        {
            _sessionTracker.EndSession(SelectedGame.AppId);
        }
        _localWatcherService.StopWatching();
        _steamApiHook.Detach();
        IsInjected = false;
        AddLog("[Tracker] Отслеживание игры успешно остановлено.");
        StatusMessage = "Отслеживание остановлено.";
    }

    private void OnLocalAchievementUnlocked(LocalAchievementUnlock unlock)
    {
        Application.Current?.Dispatcher?.Invoke(async () =>
        {
            var targetGame = (SelectedGame != null && SelectedGame.AppId == unlock.AppId)
                ? SelectedGame
                : Games.FirstOrDefault(g => g.AppId == unlock.AppId);

            Achievement? ach = targetGame?.Achievements.FirstOrDefault(a => a.ApiName.Equals(unlock.ApiName, StringComparison.OrdinalIgnoreCase));

            if (ach != null)
            {
                ach.IsUnlocked = true;
                ach.UnlockTime = unlock.UnlockTime;

                // Если у ачивки в трекере еще не было русского названия, но из клиента Steam пришло официальное русское имя
                if (!string.IsNullOrEmpty(unlock.DisplayName) && (ach.DisplayName == ach.ApiName || string.IsNullOrEmpty(ach.DisplayName) || !ContainsCyrillic(ach.DisplayName)))
                {
                    if (ContainsCyrillic(unlock.DisplayName))
                    {
                        ach.DisplayName = unlock.DisplayName;
                    }
                }
                if (!string.IsNullOrEmpty(unlock.Description) && (string.IsNullOrEmpty(ach.Description) || !ContainsCyrillic(ach.Description)))
                {
                    if (ContainsCyrillic(unlock.Description))
                    {
                        ach.Description = unlock.Description;
                    }
                }

                targetGame?.NotifyStatsChanged();
                UpdateFilteredAchievements();
                OnPropertyChanged(nameof(SelectedGame));
                if (targetGame != null) await _cacheManager.SaveGameAsync(targetGame);

                // Проверяем триггер Платины (100%)
                if (targetGame != null && targetGame.IsDiamondEarned)
                {
                    TriggerDiamondNotification(targetGame);
                    return;
                }
            }

            // Получаем текст: приоритет отдаем уже переведенному названию из списка игры
            string title = ach?.DisplayName ?? unlock.DisplayName ?? unlock.ApiName;
            string desc = ach?.Description ?? unlock.Description ?? "Достижение успешно получено!";

            // Если текст все еще на английском языке — переводим на русский на лету для оверлея
            if (!ContainsCyrillic(title))
            {
                title = await _translationService.TranslateAsync(title, "ru");
                if (ach != null)
                {
                    ach.DisplayName = title;
                    if (targetGame != null) await _cacheManager.SaveGameAsync(targetGame);
                }
            }
            if (!string.IsNullOrEmpty(desc) && !ContainsCyrillic(desc))
            {
                desc = await _translationService.TranslateAsync(desc, "ru");
                if (ach != null)
                {
                    ach.Description = desc;
                    if (targetGame != null) await _cacheManager.SaveGameAsync(targetGame);
                }
            }

            AddLog($"[REAL-TIME EVENT] ПОЛУЧЕНО ДОСТИЖЕНИЕ: '{title}'!");

            var payload = new NotificationPayload
            {
                AppId = unlock.AppId,
                AchievementApiName = unlock.ApiName,
                Title = title,
                Description = desc,
                Rank = ach?.Rank ?? AchievementRank.Gold,
                GlobalPercent = ach?.GlobalPercent ?? 10.0,
                IconPath = !string.IsNullOrEmpty(ach?.LocalIconPath) ? ach.LocalIconPath : (ach?.IconUrl ?? unlock.IconUrl)
            };

            _audioService.PlayRankSound(payload.Rank);
            _overlayRenderer.ShowNotification(payload);
            AddLog($"[Оверлей] Достижение: '{title}' ({payload.Rank.GetDisplayName()}, {payload.GlobalPercent:0.0}%). Воспроизведен звук.");
        });
    }

    private void OnInGameAchievementTriggered(string apiName)
    {
        Application.Current.Dispatcher.Invoke(async () =>
        {
            AddLog($"[IN-GAME EVENT] Игра вызвала SetAchievement(\"{apiName}\")!");

            var targetGame = SelectedGame ?? Games.FirstOrDefault();
            Achievement? ach = targetGame?.Achievements.FirstOrDefault(a => a.ApiName.Equals(apiName, StringComparison.OrdinalIgnoreCase));
            if (ach != null)
            {
                ach.IsUnlocked = true;
                ach.UnlockTime = DateTime.UtcNow;

                targetGame?.NotifyStatsChanged();
                UpdateFilteredAchievements();
                OnPropertyChanged(nameof(SelectedGame));
                if (targetGame != null) await _cacheManager.SaveGameAsync(targetGame);

                // Проверяем триггер Платины (100%)
                if (targetGame != null && targetGame.IsDiamondEarned)
                {
                    TriggerDiamondNotification(targetGame);
                    return;
                }
            }

            await TestAchievementAsync(ach);
        });
    }

    public void TestAchievement(Achievement? ach)
    {
        _ = TestAchievementAsync(ach);
    }

    public async Task TestAchievementAsync(Achievement? ach)
    {
        if (ach == null) return;

        string title = ach.DisplayName;
        string desc = string.IsNullOrEmpty(ach.Description) ? "Достижение успешно получено!" : ach.Description;

        // Если ачивка еще на английском языке — переводим ее на русский для показа в оверлее
        if (!ContainsCyrillic(title))
        {
            title = await _translationService.TranslateAsync(title, "ru");
            ach.DisplayName = title;
            if (SelectedGame != null) await _cacheManager.SaveGameAsync(SelectedGame);
        }
        if (!string.IsNullOrEmpty(desc) && !ContainsCyrillic(desc))
        {
            desc = await _translationService.TranslateAsync(desc, "ru");
            ach.Description = desc;
            if (SelectedGame != null) await _cacheManager.SaveGameAsync(SelectedGame);
        }

        var payload = new NotificationPayload
        {
            AppId = SelectedGame?.AppId ?? 0,
            AchievementApiName = ach.ApiName,
            Title = title,
            Description = desc,
            Rank = ach.Rank,
            GlobalPercent = ach.GlobalPercent,
            IconPath = !string.IsNullOrEmpty(ach.LocalIconPath) ? ach.LocalIconPath : ach.IconUrl
        };

        _audioService.PlayRankSound(payload.Rank);
        _overlayRenderer.ShowNotification(payload);
        AddLog($"[Оверлей] Тест достижения: '{title}' ({ach.Rank.GetDisplayName()}, {ach.GlobalPercent:0.0}%). Воспроизведен звук.");
    }

    private static bool ContainsCyrillic(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.Any(c => (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё');
    }

    public void TestRank(string? rankStr)
    {
        if (!Enum.TryParse<AchievementRank>(rankStr, out var rank))
        {
            rank = AchievementRank.Gold;
        }

        if (rank == AchievementRank.Diamond)
        {
            TriggerDiamondNotification(SelectedGame);
            return;
        }

        var samplePayload = new NotificationPayload
        {
            AppId = SelectedGame?.AppId ?? 413150,
            AchievementApiName = "TEST_SAMPLE",
            Title = $"Тестовая награда: {rank.GetDisplayName()}",
            Description = $"Демонстрация анимации Fade-in + Slide-down для ранга {rank.GetDisplayName()}",
            Rank = rank,
            GlobalPercent = rank switch
            {
                AchievementRank.Bronze => 68.4,
                AchievementRank.Silver => 34.2,
                _ => 8.1
            },
            IconPath = SelectedGame?.Achievements.FirstOrDefault()?.LocalIconPath
        };

        _audioService.PlayRankSound(rank);
        _overlayRenderer.ShowNotification(samplePayload);
        AddLog($"[Preview] Тест уведомления ранга {rank.GetDisplayName()}. Звук: standard_unlock.wav");
    }

    private void TriggerDiamondNotification(GameMetadata? game)
    {
        var diamondPayload = new NotificationPayload
        {
            AppId = game?.AppId ?? 0,
            AchievementApiName = "DIAMOND_100_PERCENT",
            Title = "БРИЛЛИАНТ: 100% ДОСТИЖЕНИЙ!",
            Description = $"Поздравляем! Все достижения в игре {game?.Name ?? "игры"} успешно получены!",
            Rank = AchievementRank.Diamond,
            GlobalPercent = 0.5,
            IconPath = game?.LocalHeaderImagePath ?? game?.Achievements.FirstOrDefault()?.LocalIconPath
        };

        _audioService.PlayRankSound(AchievementRank.Diamond);
        _overlayRenderer.ShowNotification(diamondPayload);
        AddLog($"[Diamond Trigger] 100% ДОСТИЖЕНИЙ! Запущена торжественная анимация и rare_platinum_unlock.wav!");
    }

    private void UpdateFilteredAchievements()
    {
        FilteredAchievements.Clear();
        if (SelectedGame != null)
        {
            foreach (var ach in SelectedGame.Achievements)
            {
                FilteredAchievements.Add(ach);
            }
        }
    }

    private void UpdateCacheSizeDisplay()
    {
        long bytes = _cacheManager.GetCacheSizeBytes();
        CacheSizeFormatted = FormatBytes(bytes);
    }

    private void AddLog(string msg)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Application.Current?.Dispatcher.Invoke(() =>
        {
            ConsoleLogs.Insert(0, timestamped);
            if (ConsoleLogs.Count > 150)
            {
                ConsoleLogs.RemoveAt(ConsoleLogs.Count - 1);
            }
        });
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int i = 0;
        double d = bytes;
        while (d >= 1024 && i < suffixes.Length - 1)
        {
            d /= 1024;
            i++;
        }
        return $"{d:0.##} {suffixes[i]}";
    }
}
