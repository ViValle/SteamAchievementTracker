using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SteamAchievementTracker.Infrastructure.Steam;

public static class SteamUserDetector
{
    // Базовая константа для перевода SteamID3 в SteamID64: 76561197960265728
    public const ulong SteamId64Base = 76561197960265728UL;

    public static string? GetSteamPath()
    {
        try
        {
            using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (steamKey?.GetValue("SteamPath") is string steamPath && !string.IsNullOrEmpty(steamPath))
            {
                return steamPath.Replace('/', '\\');
            }

            using var hklmKey = Registry.LocalMachine.OpenSubKey(@"Software\Valve\Steam") 
                ?? Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Valve\Steam");
            if (hklmKey?.GetValue("InstallPath") is string installPath && !string.IsNullOrEmpty(installPath))
            {
                return installPath.Replace('/', '\\');
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SteamUserDetector] Ошибка при получении пути Steam: {ex.Message}");
        }

        return null;
    }

    public static uint? DetectActiveSteamId3()
    {
        try
        {
            using var activeKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            if (activeKey != null)
            {
                var activeUserVal = activeKey.GetValue("ActiveUser");
                if (activeUserVal is int activeUserId && activeUserId > 0)
                {
                    return (uint)activeUserId;
                }
            }

            var steamPath = GetSteamPath();
            if (!string.IsNullOrEmpty(steamPath))
            {
                var userdataDir = Path.Combine(steamPath, "userdata");
                if (Directory.Exists(userdataDir))
                {
                    var userFolders = Directory.GetDirectories(userdataDir)
                        .Select(Path.GetFileName)
                        .Where(name => uint.TryParse(name, out var id) && id > 0)
                        .Select(name => new DirectoryInfo(Path.Combine(userdataDir, name!)))
                        .OrderByDescending(d => d.LastWriteTime)
                        .ToList();

                    if (userFolders.Count > 0 && uint.TryParse(userFolders[0].Name, out var id))
                    {
                        return id;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SteamUserDetector] Ошибка при автодетекте SteamID3: {ex.Message}");
        }

        return null;
    }

    public static ulong? DetectActiveSteamId64()
    {
        var id3 = DetectActiveSteamId3();
        if (id3.HasValue)
        {
            var id64 = SteamId64Base + id3.Value;
            Debug.WriteLine($"[SteamUserDetector] Обнаружен активный пользователь: {id64} (SteamID3: {id3.Value})");
            return id64;
        }

        return null;
    }

    public static string? GetLibraryCachePath(uint appId)
    {
        var steamPath = GetSteamPath();
        var steamId3 = DetectActiveSteamId3();
        if (string.IsNullOrEmpty(steamPath) || !steamId3.HasValue)
            return null;

        return Path.Combine(steamPath, "userdata", steamId3.Value.ToString(), "config", "librarycache", $"{appId}.json");
    }

    public static string? GetUserGameStatsPath(uint appId)
    {
        var steamPath = GetSteamPath();
        var steamId3 = DetectActiveSteamId3();
        if (string.IsNullOrEmpty(steamPath) || !steamId3.HasValue)
            return null;

        return Path.Combine(steamPath, "appcache", "stats", $"UserGameStats_{steamId3.Value}_{appId}.bin");
    }

    public static string? GetAchievementProgressPath()
    {
        var steamPath = GetSteamPath();
        var steamId3 = DetectActiveSteamId3();
        if (string.IsNullOrEmpty(steamPath) || !steamId3.HasValue)
            return null;

        return Path.Combine(steamPath, "userdata", steamId3.Value.ToString(), "config", "librarycache", "achievement_progress.json");
    }
}
