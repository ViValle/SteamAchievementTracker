using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SteamAchievementTracker.Core.Interfaces;
using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Infrastructure.Steam;

public class SteamWebApiClient : ISteamApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IRankEvaluator _rankEvaluator;

    public SteamWebApiClient(HttpClient httpClient, IRankEvaluator rankEvaluator)
    {
        _httpClient = httpClient;
        _rankEvaluator = rankEvaluator;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public async Task<GameMetadata?> GetGameDetailsAsync(uint appId, string? apiKey = null, CancellationToken ct = default)
    {
        var game = new GameMetadata
        {
            AppId = appId,
            Name = $"App {appId}",
            HeaderImageUrl = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg",
            LastUpdated = DateTime.UtcNow
        };

        // 1. Получаем базовую информацию из Steam Store API с флагами cc=us&l=russian для получения официальной русской локализации
        var officialRussianTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var storeUrl = $"https://store.steampowered.com/api/appdetails?appids={appId}&cc=us&l=russian";
            var storeDoc = await _httpClient.GetFromJsonAsync<JsonElement>(storeUrl, ct);

            if (storeDoc.TryGetProperty(appId.ToString(), out var appProp) &&
                appProp.TryGetProperty("success", out var successProp) &&
                successProp.GetBoolean() &&
                appProp.TryGetProperty("data", out var dataProp))
            {
                if (dataProp.TryGetProperty("name", out var nameProp))
                    game.Name = nameProp.GetString() ?? game.Name;

                if (dataProp.TryGetProperty("header_image", out var headerProp))
                    game.HeaderImageUrl = headerProp.GetString() ?? game.HeaderImageUrl;

                // Официальные переведенные ачивки из раздела highlighted
                if (dataProp.TryGetProperty("achievements", out var achProp) &&
                    achProp.TryGetProperty("highlighted", out var highlightedProp))
                {
                    foreach (var h in highlightedProp.EnumerateArray())
                    {
                        var locName = h.TryGetProperty("localized_name", out var ln) ? ln.GetString() : null;
                        var iconPath = h.TryGetProperty("path", out var p) ? p.GetString() : null;
                        if (!string.IsNullOrEmpty(locName) && !string.IsNullOrEmpty(iconPath))
                        {
                            var iconFile = System.IO.Path.GetFileName(iconPath);
                            officialRussianTitles[iconFile] = locName;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ошибка Store API не блокирует загрузку ачивок
        }

        // 2. Получаем глобальный процент редкости достижений (открытый API)
        var globalPercentages = await GetGlobalAchievementPercentagesAsync(appId, ct);

        // 3. Получаем метаданные достижений (Имена, Описания, Иконки)
        var achievementMap = new Dictionary<string, Achievement>(StringComparer.OrdinalIgnoreCase);

        // Попытка 3а: Через официальный Web API Schema (если передан ключ)
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            await TryFetchViaOfficialSchemaAsync(appId, apiKey, achievementMap, ct);
        }

        // Попытка 3б: Через открытую страницу Community статистики
        if (achievementMap.Count == 0)
        {
            await TryFetchViaCommunityDataAsync(appId, achievementMap, ct);
        }

        // Попытка 3в: Если Community недоступно, строим из списка globalPercentages
        if (achievementMap.Count == 0 && globalPercentages.Count > 0)
        {
            foreach (var kvp in globalPercentages)
            {
                achievementMap[kvp.Key] = new Achievement
                {
                    ApiName = kvp.Key,
                    DisplayName = FormatApiNameToReadable(kvp.Key),
                    Description = "Достижение Steam",
                    GlobalPercent = kvp.Value,
                    Rank = _rankEvaluator.Evaluate(kvp.Value)
                };
            }
        }

        // Объединяем метаданные и проценты получения
        foreach (var ach in achievementMap.Values)
        {
            if (globalPercentages.TryGetValue(ach.ApiName, out var pct))
            {
                ach.GlobalPercent = Math.Round(pct, 1);
            }
            ach.Rank = _rankEvaluator.Evaluate(ach.GlobalPercent);
            game.Achievements.Add(ach);
        }

        // Если глобальные проценты дали ачивки, которых не было в схеме — добавляем их
        foreach (var kvp in globalPercentages)
        {
            if (!achievementMap.ContainsKey(kvp.Key))
            {
                game.Achievements.Add(new Achievement
                {
                    ApiName = kvp.Key,
                    DisplayName = FormatApiNameToReadable(kvp.Key),
                    Description = "Достижение Steam",
                    GlobalPercent = Math.Round(kvp.Value, 1),
                    Rank = _rankEvaluator.Evaluate(kvp.Value)
                });
            }
        }

        // 4. АВТОМАТИЧЕСКАЯ СИНХРОНИЗАЦИЯ ПОЛУЧЕННЫХ АЧИВОК ТЕКУЩЕГО ИГРОКА
        var activeSteamId64 = SteamUserDetector.DetectActiveSteamId64();
        if (activeSteamId64.HasValue)
        {
            await SyncUserAchievementsAsync(game, activeSteamId64.Value, ct);
        }

        // Применяем официальные русские названия из Store API (если получены)
        if (officialRussianTitles.Count > 0)
        {
            foreach (var ach in game.Achievements)
            {
                var iconFile = !string.IsNullOrEmpty(ach.IconUrl) ? System.IO.Path.GetFileName(ach.IconUrl) : "";
                if (!string.IsNullOrEmpty(iconFile) && officialRussianTitles.TryGetValue(iconFile, out var ruTitle))
                {
                    ach.DisplayName = ruTitle;
                }
            }
        }

        // Сортируем: редкие вверху
        game.Achievements = game.Achievements
            .OrderBy(a => a.GlobalPercent)
            .ToList();

        return game;
    }

    public async Task<bool> SyncUserAchievementsAsync(GameMetadata game, ulong? steamId64 = null, CancellationToken ct = default)
    {
        var targetSteamId = steamId64 ?? SteamUserDetector.DetectActiveSteamId64();
        int syncedUnlocked = 0;
        bool anySyncSuccess = false;

        // 1. Синхронизация через веб-профиль Steam Community XML
        if (targetSteamId.HasValue)
        {
            try
            {
                var userStatsUrl = $"https://steamcommunity.com/profiles/{targetSteamId.Value}/stats/{game.AppId}/?xml=1";
                var responseString = await _httpClient.GetStringAsync(userStatsUrl, ct);

                if (!string.IsNullOrWhiteSpace(responseString) && responseString.Contains("<achievements>"))
                {
                    var xdoc = XDocument.Parse(responseString);

                    // Если название игры еще дефолтное, берем его из XML профиля
                    var gameNameElem = xdoc.Descendants("gameName").FirstOrDefault();
                    if (gameNameElem != null && !string.IsNullOrWhiteSpace(gameNameElem.Value) && (game.Name.StartsWith("App ") || string.IsNullOrEmpty(game.Name)))
                    {
                        game.Name = gameNameElem.Value;
                    }

                    var gameLogoElem = xdoc.Descendants("gameLogo").FirstOrDefault();
                    if (gameLogoElem != null && !string.IsNullOrWhiteSpace(gameLogoElem.Value) && string.IsNullOrEmpty(game.HeaderImageUrl))
                    {
                        game.HeaderImageUrl = gameLogoElem.Value;
                    }

                    var achievementElements = xdoc.Descendants("achievement");
                    var achDict = game.Achievements.ToDictionary(a => a.ApiName, a => a, StringComparer.OrdinalIgnoreCase);

                    foreach (var elem in achievementElements)
                    {
                        var apiName = elem.Element("apiname")?.Value;
                        if (string.IsNullOrEmpty(apiName)) continue;

                        var isClosed = elem.Attribute("closed")?.Value == "1";
                        var unlockTsStr = elem.Element("unlockTimestamp")?.Value;
                        var iconClosed = elem.Element("iconClosed")?.Value;
                        var iconOpen = elem.Element("iconOpen")?.Value;
                        var name = elem.Element("name")?.Value;
                        var desc = elem.Element("description")?.Value;

                        if (achDict.TryGetValue(apiName, out var ach))
                        {
                            ach.IsUnlocked = isClosed;

                            if (!string.IsNullOrEmpty(name)) ach.DisplayName = name;
                            if (!string.IsNullOrEmpty(desc)) ach.Description = desc;
                            if (!string.IsNullOrEmpty(iconClosed)) ach.IconUrl = iconClosed;
                            if (!string.IsNullOrEmpty(iconOpen)) ach.IconGrayUrl = iconOpen;

                            if (isClosed)
                            {
                                syncedUnlocked++;
                                if (long.TryParse(unlockTsStr, out var ts) && ts > 0)
                                {
                                    ach.UnlockTime = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
                                }
                                else if (!ach.UnlockTime.HasValue)
                                {
                                    ach.UnlockTime = DateTime.UtcNow;
                                }
                            }
                        }
                        else
                        {
                            // Если ачивки не было в исходном списке
                            var newAch = new Achievement
                            {
                                ApiName = apiName,
                                DisplayName = name ?? apiName,
                                Description = desc ?? "",
                                IconUrl = iconClosed ?? "",
                                IconGrayUrl = iconOpen ?? "",
                                IsUnlocked = isClosed
                            };

                            if (isClosed)
                            {
                                syncedUnlocked++;
                                if (long.TryParse(unlockTsStr, out var ts) && ts > 0)
                                    newAch.UnlockTime = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
                            }

                            game.Achievements.Add(newAch);
                        }
                    }

                    anySyncSuccess = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SteamWebApiClient] Ошибка синхронизации веб-профиля Steam: {ex.Message}");
            }
        }

        // 2. Локальная синхронизация из хранилища клиента Steam (librarycache)
        // Гарантирует мгновенное обнаружение свежеполученных достижений, даже когда серверам Valve требуется 15-60 минут на кэш
        try
        {
            using var localWatcher = new SteamLocalWatcherService();
            var localUnlocks = localWatcher.ReadLocalAchievements(game.AppId);
            if (localUnlocks.Count > 0)
            {
                var achDict = game.Achievements.ToDictionary(a => a.ApiName, a => a, StringComparer.OrdinalIgnoreCase);
                foreach (var localAch in localUnlocks)
                {
                    if (achDict.TryGetValue(localAch.ApiName, out var existingAch))
                    {
                        if (!existingAch.IsUnlocked)
                        {
                            existingAch.IsUnlocked = true;
                            existingAch.UnlockTime = localAch.UnlockTime;
                            syncedUnlocked++;
                            Debug.WriteLine($"[SteamWebApiClient] Локальное разблокированное достижение: {localAch.ApiName} ('{localAch.DisplayName}')");
                        }

                        // Если есть официальное локализованное имя из клиента Steam, сохраняем его
                        if (!string.IsNullOrEmpty(localAch.DisplayName) && (string.IsNullOrEmpty(existingAch.DisplayName) || existingAch.DisplayName == existingAch.ApiName))
                        {
                            existingAch.DisplayName = localAch.DisplayName;
                        }
                        if (!string.IsNullOrEmpty(localAch.Description) && string.IsNullOrEmpty(existingAch.Description))
                        {
                            existingAch.Description = localAch.Description;
                        }
                    }
                }
                anySyncSuccess = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SteamWebApiClient] Ошибка локальной синхронизации: {ex.Message}");
        }

        game.NotifyStatsChanged();
        Debug.WriteLine($"[SteamWebApiClient] Всего синхронизировано {syncedUnlocked} разблокированных ачивок для {game.Name}!");
        return anySyncSuccess;
    }

    public async Task<Dictionary<string, double>> GetGlobalAchievementPercentagesAsync(uint appId, CancellationToken ct = default)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var url = $"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v002/?gameid={appId}";
            var response = await _httpClient.GetFromJsonAsync<JsonElement>(url, ct);

            if (response.TryGetProperty("achievementpercentages", out var root) &&
                root.TryGetProperty("achievements", out var achievementsArray))
            {
                foreach (var item in achievementsArray.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameProp) &&
                        item.TryGetProperty("percent", out var percentProp))
                    {
                        var name = nameProp.GetString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            double pct = 0.0;
                            if (percentProp.ValueKind == JsonValueKind.Number)
                            {
                                pct = percentProp.GetDouble();
                            }
                            else if (percentProp.ValueKind == JsonValueKind.String)
                            {
                                var strVal = percentProp.GetString();
                                double.TryParse(strVal, NumberStyles.Any, CultureInfo.InvariantCulture, out pct);
                            }
                            result[name] = pct;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ошибка API
        }

        return result;
    }

    public async Task<bool> IsVacSecuredAsync(uint appId, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=categories&cc=us";
            var response = await _httpClient.GetFromJsonAsync<JsonElement>(url, ct);

            if (response.TryGetProperty(appId.ToString(), out var appProp) &&
                appProp.TryGetProperty("success", out var succ) && succ.GetBoolean() &&
                appProp.TryGetProperty("data", out var data) &&
                data.TryGetProperty("categories", out var categories))
            {
                foreach (var cat in categories.EnumerateArray())
                {
                    if (cat.TryGetProperty("id", out var idProp) && idProp.GetInt32() == 8)
                    {
                        return true; // ID 8 = Valve Anti-Cheat enabled
                    }
                }
            }
        }
        catch
        {
            // Ошибка запроса
        }

        return false;
    }

    private async Task TryFetchViaOfficialSchemaAsync(uint appId, string apiKey, Dictionary<string, Achievement> map, CancellationToken ct)
    {
        try
        {
            var url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={apiKey}&appid={appId}&l=russian";
            var response = await _httpClient.GetFromJsonAsync<JsonElement>(url, ct);

            if (response.TryGetProperty("game", out var game) &&
                game.TryGetProperty("availableGameStats", out var stats) &&
                stats.TryGetProperty("achievements", out var achArray))
            {
                foreach (var item in achArray.EnumerateArray())
                {
                    var apiName = item.GetProperty("name").GetString();
                    if (string.IsNullOrEmpty(apiName)) continue;

                    map[apiName] = new Achievement
                    {
                        ApiName = apiName,
                        DisplayName = item.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? apiName : apiName,
                        Description = item.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                        IconUrl = item.TryGetProperty("icon", out var ic) ? ic.GetString() ?? "" : "",
                        IconGrayUrl = item.TryGetProperty("icongray", out var icg) ? icg.GetString() ?? "" : ""
                    };
                }
            }
        }
        catch
        {
            // Fallback
        }
    }

    private async Task TryFetchViaCommunityDataAsync(uint appId, Dictionary<string, Achievement> map, CancellationToken ct)
    {
        try
        {
            var url = $"https://steamcommunity.com/stats/{appId}/achievements/?l=english";
            var html = await _httpClient.GetStringAsync(url, ct);

            if (string.IsNullOrWhiteSpace(html))
                return;

            // 1. Попытка парсинга современного JSON блока SSR
            var regex = new Regex(@"\\""api_name\\"":\\""(?<api>[^\\""]+)\\"",\\""name\\"":\\""(?<name>[^\\""]+)\\"",\\""description\\"":\\""(?<desc>[^\\""]*)\\""(?:.*?\\""icon_achieved\\"":\\""(?<icon>[^\\""]*)\\"")?", RegexOptions.Compiled);
            var matches = regex.Matches(html);

            if (matches.Count > 0)
            {
                foreach (Match m in matches)
                {
                    var api = Regex.Unescape(m.Groups["api"].Value);
                    if (string.IsNullOrEmpty(api) || map.ContainsKey(api)) continue;

                    var name = WebUtility.HtmlDecode(Regex.Unescape(m.Groups["name"].Value));
                    var desc = WebUtility.HtmlDecode(Regex.Unescape(m.Groups["desc"].Value));
                    var icon = m.Groups["icon"].Success ? Regex.Unescape(m.Groups["icon"].Value) : "";

                    var iconUrl = "";
                    if (!string.IsNullOrEmpty(icon))
                    {
                        iconUrl = icon.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? icon
                            : $"https://shared.akamai.steamstatic.com/community_assets/images/apps/{appId}/{icon}";
                    }

                    map[api] = new Achievement
                    {
                        ApiName = api,
                        DisplayName = name,
                        Description = desc,
                        IconUrl = iconUrl
                    };
                }
            }

            // 2. Если regex не нашел, пробуем старый XML формат
            if (map.Count == 0 && html.Contains("<achievements>"))
            {
                var xdoc = XDocument.Parse(html);
                foreach (var elem in xdoc.Descendants("achievement"))
                {
                    var apiName = elem.Element("apiname")?.Value;
                    if (string.IsNullOrEmpty(apiName)) continue;

                    map[apiName] = new Achievement
                    {
                        ApiName = apiName,
                        DisplayName = elem.Element("name")?.Value ?? apiName,
                        Description = elem.Element("description")?.Value ?? "",
                        IconUrl = elem.Element("iconOpen")?.Value ?? "",
                        IconGrayUrl = elem.Element("iconClosed")?.Value ?? ""
                    };
                }
            }
        }
        catch
        {
            // Ошибка парсинга
        }
    }

    private static string FormatApiNameToReadable(string apiName)
    {
        return apiName
            .Replace("ACH_", "")
            .Replace("ACHIEVEMENT_", "")
            .Replace("_", " ")
            .Trim();
    }
}
