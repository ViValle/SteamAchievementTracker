using System.Net.Http;
using SteamAchievementTracker.Core.Interfaces;

namespace SteamAchievementTracker.Infrastructure.Storage;

public class ImageCacheService : IImageCacheService
{
    private readonly string _cacheDirectory;
    private readonly HttpClient _httpClient;

    public ImageCacheService(HttpClient httpClient, string? customDirectory = null)
    {
        _httpClient = httpClient;
        _cacheDirectory = customDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamAchievementTracker", "Cache", "Images");

        if (!Directory.Exists(_cacheDirectory))
            Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<string?> CacheImageAsync(uint appId, string key, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            var appFolder = Path.Combine(_cacheDirectory, appId.ToString());
            if (!Directory.Exists(appFolder))
                Directory.CreateDirectory(appFolder);

            var safeKey = SanitizeFileName(key);
            var extension = Path.GetExtension(new Uri(url).AbsolutePath);
            if (string.IsNullOrEmpty(extension)) extension = ".jpg";

            var filePath = Path.Combine(appFolder, $"{safeKey}{extension}");

            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
                return filePath;

            var bytes = await _httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(filePath, bytes);

            return filePath;
        }
        catch
        {
            return null;
        }
    }

    public void ClearImageCache()
    {
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                Directory.Delete(_cacheDirectory, recursive: true);
                Directory.CreateDirectory(_cacheDirectory);
            }
        }
        catch
        {
            // Игнорируем блокировки файлов
        }
    }

    public long GetImageCacheSizeBytes()
    {
        if (!Directory.Exists(_cacheDirectory))
            return 0;

        return Directory.GetFiles(_cacheDirectory, "*.*", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f).Length)
            .Sum();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
