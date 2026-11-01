using System.Text.Json;
using SteamAchievementTracker.Core.Interfaces;
using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Infrastructure.Storage;

public class LocalCacheManager : ICacheManager
{
    private readonly string _dataDirectory;
    private readonly IImageCacheService _imageCache;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public LocalCacheManager(IImageCacheService imageCache, string? customDirectory = null)
    {
        _imageCache = imageCache;
        _dataDirectory = customDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamAchievementTracker", "Cache", "Data");

        if (!Directory.Exists(_dataDirectory))
            Directory.CreateDirectory(_dataDirectory);
    }

    public async Task<GameMetadata?> LoadGameAsync(uint appId)
    {
        await _lock.WaitAsync();
        try
        {
            var filePath = Path.Combine(_dataDirectory, $"{appId}.json");
            if (!File.Exists(filePath))
                return null;

            using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<GameMetadata>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveGameAsync(GameMetadata game)
    {
        await _lock.WaitAsync();
        try
        {
            var filePath = Path.Combine(_dataDirectory, $"{game.AppId}.json");
            var tempFilePath = Path.Combine(_dataDirectory, $"{game.AppId}.json.tmp");
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        await JsonSerializer.SerializeAsync(stream, game, JsonOptions);
                    }
                    File.Move(tempFilePath, filePath, overwrite: true);
                    break;
                }
                catch when (attempt < 2)
                {
                    await Task.Delay(50);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<GameMetadata>> GetAllCachedGamesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var result = new List<GameMetadata>();
            if (!Directory.Exists(_dataDirectory))
                return result;

            var files = Directory.GetFiles(_dataDirectory, "*.json");
            foreach (var file in files)
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    var game = await JsonSerializer.DeserializeAsync<GameMetadata>(stream, JsonOptions);
                    if (game != null)
                        result.Add(game);
                }
                catch
                {
                    // Пропуск поврежденных файлов
                }
            }

            return result.OrderBy(g => g.Name).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveGameAsync(uint appId)
    {
        await _lock.WaitAsync();
        try
        {
            var filePath = Path.Combine(_dataDirectory, $"{appId}.json");
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAllCacheAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (Directory.Exists(_dataDirectory))
            {
                Directory.Delete(_dataDirectory, recursive: true);
                Directory.CreateDirectory(_dataDirectory);
            }
            _imageCache.ClearImageCache();
        }
        finally
        {
            _lock.Release();
        }
    }

    public long GetCacheSizeBytes()
    {
        long dataSize = 0;
        if (Directory.Exists(_dataDirectory))
        {
            dataSize = Directory.GetFiles(_dataDirectory, "*.json")
                .Select(f => new FileInfo(f).Length)
                .Sum();
        }
        return dataSize + _imageCache.GetImageCacheSizeBytes();
    }
}
