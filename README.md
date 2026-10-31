# Steam Achievement Tracker & In-Game Overlay (.NET 8)

[![Release](https://img.shields.io/badge/release-v0.1.0-blue.svg)](https://github.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-25%20passed-brightgreen.svg)](tests/)
[![SemVer](https://img.shields.io/badge/semver-2.0.0-orange.svg)](https://semver.org/)

Легковесное Windows-приложение на C# (.NET 8) для отслеживания статистики и вывода анимированных оверлеев достижений Steam поверх однопользовательских игр в эксклюзивном полноэкранном режиме (DirectX Exclusive Fullscreen) с нулевой нагрузкой на систему и многоуровневой защитой от банов (Anti-Ban Engine).

---

## 🏗 Архитектура решения (Clean Architecture)

Решение строго изолировано по слоям:

```text
├── .github/workflows/       # CI/CD пайплайны (проверка коммитов и бинарные релизы)
├── configs/                 # Шаблоны конфигураций
├── scripts/                 # Скрипты локальной сборки и аварийного отката
├── src/                     # Исходный код (Core, Infrastructure, Injector, Overlay, UI)
├── tests/                   # Модульные и интеграционные тесты (xUnit)
├── dist/                    # Артефакты сборки (в .gitignore)
├── Directory.Build.props    # Глобальные метаданные, SemVer и детерминизм сборки
├── CHANGELOG.md             # Журнал изменений по стандарту Keep a Changelog
├── .env.example             # Шаблон переменных окружения
└── README.md
```

### Модули:
1. **`SteamAchievementTracker.Core`**
   - **Доменные модели**: `Achievement`, `GameMetadata`, `AchievementRank`, `AntiCheatCheckResult`, `NotificationPayload`, `SessionInfo`.
   - **Интерфейсы контрактов**: `ISteamApiClient`, `ICacheManager`, `IImageCacheService`, `IRankEvaluator`, `IAntiCheatScanner`, `IAudioService`, `IInjectorEngine`, `ISteamApiHookService`, `ISessionTracker`, `IOverlayRenderer`, `IIpcService`.

2. **`SteamAchievementTracker.Infrastructure`**
   - **Steam Web API Client**: парсинг открытого Steam Web API и Community XML (`GetSchemaForGame`, `GetGlobalAchievementPercentagesForApp`, Store Details).
   - **Storage & Cache**: локальное хранение метаданных (JSON) и иконок (`ImageCacheService`) с возможностью полной очистки кэша.
   - **Dynamic Sound Engine**: прямое Win32 `winmm.dll` воспроизведение аудио без накладных расходов. Автоматическая программная генерация чистого 16-bit 44.1kHz звука (`standard_unlock.wav` и `rare_platinum_unlock.wav`).
   - **Градация редкости (`RankEvaluator`)**:
     - 🥉 **Бронза**: > 50% игроков
     - 🥈 **Серебро**: от 15% до 50% игроков
     - 🥇 **Золото**: < 15% игроков
     - 💎 **Бриллиант (Платина)**: 100% полученных достижений в конкретной игре.
   - **IPC Служба**: асинхронный Named Pipe сервер для обмена данными между хуком и UI.

3. **`SteamAchievementTracker.Injector`**
   - **Anti-Ban Engine (`AntiCheatScanner`)**:
     - Аудит запущенных служб Windows (`EasyAntiCheat`, `EasyAntiCheat_EOS`, `BEService`, `vgk`, etc.).
     - Сканирование загруженных модулей процесса (`EnumProcessModulesEx` / `Process.Modules`).
     - Проверка флага защиты Valve Anti-Cheat (VAC) по AppID игры через Steam Store API.
     - При обнаружении любой угрозы инжект **жестко блокируется** с предупреждением в лог.
   - **WinAPI Injector (`InjectorEngine`)**: безопасное внедрение DLL через `CreateRemoteThread` / `LoadLibraryW` с валидацией прав.

4. **`SteamAchievementTracker.Overlay`**
   - **DirectX Hook (`DxgiHook`)**: перехват виртуальной таблицы `IDXGISwapChain::Present` (VTable Index 8) для отрисовки поверх Exclusive Fullscreen.
   - **Steam API Hook (`SteamApiHook`)**: перехват `SteamAPI_ISteamUserStats_SetAchievement` из `steam_api64.dll` / `steam_api.dll`.
   - **Clean Unhook Protocol**: атомарный счетчик вызовов `Interlocked`, ожидание завершения потоков, восстановление оригинальных байт пролога, сброс кэша инструкций процессора (`FlushInstructionCache`).
   - **WPF Top-Center Overlay Window**: прозрачное, сквозное (`WS_EX_TRANSPARENT`), не перехватывающее фокус (`WS_EX_NOACTIVATE`) аппаратное окно оверлея с кинематографичной анимацией (Fade-in + Slide-down).

5. **`SteamAchievementTracker.UI`**
   - Современный минималистичный интерфейс на WPF (.NET 8).
   - Каталог игр, просмотр достижений, аудит процессов, песочница тестирования звуков и анимаций.

---

## 🚀 Запуск, тестирование и сборка

### Тестирование
```powershell
dotnet test
```

### Локальная автоматическая сборка релиза
Сборка единого независимого исполняемого файла (`win-x64`), упаковка в ZIP и расчет контрольной суммы `SHA256`:
```powershell
pwsh -File scripts/build-release.ps1
```
Артефакты сохраняются в каталог `dist/`:
- `SteamAchievementTracker-v0.1.0-win-x64.zip`
- `SHA256SUMS.txt`

### Экстренный откат (Fast Rollback)
Для быстрого отката бинарника в случае инцидента:
```powershell
pwsh -File scripts/rollback.ps1
```

---

## 📦 CI/CD & Релизы

* **CI (`.github/workflows/ci.yml`)**: Автоматическая проверка коммитов и PR (восстановление в locked-режиме, билд Release, прогон 25 тестов).
* **Release (`.github/workflows/release.yml`)**: Публикация GitHub Release при создании аннотированного тега `v*.*.*` с генерацией `SHA256SUMS.txt`.
