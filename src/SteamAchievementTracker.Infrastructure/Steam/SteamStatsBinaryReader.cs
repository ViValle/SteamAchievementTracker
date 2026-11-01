using System.Diagnostics;
using System.IO;
using System.Text;
using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Infrastructure.Steam;

/// <summary>
/// Парсер бинарных файлов статистики и достижений Steam Client (VDF Binary / BKV).
/// Считывает UserGameStatsSchema_{appId}.bin и UserGameStats_{steamId3}_{appId}.bin.
/// Обеспечивает мгновенный локальный оффлайн-трекинг достижений прямо из движка Steam.
/// </summary>
public static class SteamStatsBinaryReader
{
    public static Dictionary<(int statId, int bitId), string> LoadSchema(string schemaPath)
    {
        var map = new Dictionary<(int, int), string>();
        if (!File.Exists(schemaPath)) return map;

        try
        {
            var bytes = File.ReadAllBytes(schemaPath);
            using var ms = new MemoryStream(bytes);
            using var reader = new BinaryReader(ms);

            var stack = new List<string>();

            while (ms.Position < ms.Length)
            {
                byte type = reader.ReadByte();
                if (type == 8) // End of object
                {
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                string key = ReadNullTerminatedString(reader);

                if (type == 0) // Object start
                {
                    stack.Add(key);
                }
                else if (type == 1) // String value
                {
                    string val = ReadNullTerminatedString(reader);
                    // Ищем структуру: stats -> [statId] -> bits -> [bitId] -> name = 'ACH_...'
                    if (key.Equals("name", StringComparison.OrdinalIgnoreCase) &&
                        val.StartsWith("ACH_", StringComparison.OrdinalIgnoreCase) &&
                        !val.EndsWith("_PROGRESS", StringComparison.OrdinalIgnoreCase))
                    {
                        // В стеке: [..., stats, <statId>, bits, <bitId>]
                        if (stack.Count >= 4)
                        {
                            int bitIndex = stack.Count - 1;
                            int bitsIndex = stack.Count - 2;
                            int statIndex = stack.Count - 3;
                            int statsIndex = stack.Count - 4;

                            if (stack[bitsIndex].Equals("bits", StringComparison.OrdinalIgnoreCase) &&
                                stack[statsIndex].Equals("stats", StringComparison.OrdinalIgnoreCase) &&
                                int.TryParse(stack[statIndex], out int statId) &&
                                int.TryParse(stack[bitIndex], out int bitId))
                            {
                                map[(statId, bitId)] = val;
                            }
                        }
                    }
                }
                else if (type == 2) reader.ReadInt32();
                else if (type == 3) reader.ReadSingle();
                else if (type == 7) reader.ReadUInt64();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SteamStatsBinaryReader] Ошибка парсинга схемы {schemaPath}: {ex.Message}");
        }

        return map;
    }

    public static Dictionary<(int statId, int bitId), DateTime> ReadUserUnlockedAchievements(string statsPath)
    {
        var results = new Dictionary<(int, int), DateTime>();
        if (!File.Exists(statsPath)) return results;

        try
        {
            // Читаем со свободной разделяемой блокировкой (Steam может писать одновременно)
            using var stream = new FileStream(statsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream);

            var stack = new List<string>();

            while (stream.Position < stream.Length)
            {
                byte type = reader.ReadByte();
                if (type == 8) // End of object
                {
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                string key = ReadNullTerminatedString(reader);

                if (type == 0) // Object start
                {
                    stack.Add(key);
                }
                else if (type == 2) // Int32 value
                {
                    int val = reader.ReadInt32();
                    // В стеке: [cache, <statId>, AchievementTimes] -> <bitId> = timestamp
                    if (stack.Count >= 3 &&
                        stack[^1].Equals("AchievementTimes", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(stack[^2], out int statId) &&
                        int.TryParse(key, out int bitId) &&
                        val > 0)
                    {
                        var unlockTime = DateTimeOffset.FromUnixTimeSeconds(val).UtcDateTime;
                        results[(statId, bitId)] = unlockTime;
                    }
                }
                else if (type == 1) ReadNullTerminatedString(reader);
                else if (type == 3) reader.ReadSingle();
                else if (type == 7) reader.ReadUInt64();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SteamStatsBinaryReader] Ошибка чтения статистики {statsPath}: {ex.Message}");
        }

        return results;
    }

    public static IReadOnlyList<LocalAchievementUnlock> ReadLocalUnlockedAchievements(uint appId, uint steamId3, string steamPath)
    {
        var list = new List<LocalAchievementUnlock>();
        var schemaPath = Path.Combine(steamPath, "appcache", "stats", $"UserGameStatsSchema_{appId}.bin");
        var statsPath = Path.Combine(steamPath, "appcache", "stats", $"UserGameStats_{steamId3}_{appId}.bin");

        if (!File.Exists(statsPath))
            return list;

        var schemaMap = LoadSchema(schemaPath);
        var unlockedMap = ReadUserUnlockedAchievements(statsPath);

        foreach (var (key, unlockTime) in unlockedMap)
        {
            if (schemaMap.TryGetValue(key, out var apiName))
            {
                list.Add(new LocalAchievementUnlock
                {
                    AppId = appId,
                    ApiName = apiName,
                    UnlockTime = unlockTime
                });
            }
        }

        return list;
    }

    private static string ReadNullTerminatedString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            byte b = reader.ReadByte();
            if (b == 0) break;
            bytes.Add(b);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
