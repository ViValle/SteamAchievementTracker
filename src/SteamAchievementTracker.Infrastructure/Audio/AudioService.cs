using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.Wave;
using SteamAchievementTracker.Core.Interfaces;
using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Infrastructure.Audio;

public class AudioService : IAudioService
{
    private readonly string _soundsDirectory;
    private string _standardPath = string.Empty;
    private string _platinumPath = string.Empty;

    // WinMM fallback
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_FILENAME = 0x00020000;

    // Громкость от 0.0 до 1.0 (по умолчанию 0.45 = 45%)
    private double _volume = 0.45;

    public double Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0, 1.0);
    }

    public AudioService(string? customSoundsDirectory = null)
    {
        _soundsDirectory = customSoundsDirectory ?? Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Sounds");

        EnsureAudioAssets();
    }

    public void EnsureAudioAssets()
    {
        if (!Directory.Exists(_soundsDirectory))
            Directory.CreateDirectory(_soundsDirectory);

        _standardPath = Path.Combine(_soundsDirectory, "standard_unlock.wav");
        _platinumPath = Path.Combine(_soundsDirectory, "rare_platinum_unlock.wav");

        // Если WAV отсутствует, проверяем MP3 вариант
        if (!File.Exists(_standardPath))
        {
            var mp3 = Path.Combine(_soundsDirectory, "standard_unlock.mp3");
            if (File.Exists(mp3)) _standardPath = mp3;
            else GenerateFallbackTone(_standardPath, 1046.5, 0.8);
        }

        if (!File.Exists(_platinumPath))
        {
            var mp3 = Path.Combine(_soundsDirectory, "rare_platinum_unlock.mp3");
            if (File.Exists(mp3)) _platinumPath = mp3;
            else GenerateFallbackTone(_platinumPath, 523.25, 2.0);
        }
    }

    public void PlayRankSound(AchievementRank rank)
    {
        var targetFile = rank == AchievementRank.Diamond ? _platinumPath : _standardPath;

        if (string.IsNullOrEmpty(targetFile) || !File.Exists(targetFile))
            return;

        // Воспроизведение через NAudio в фоновом потоке с точной регулировкой громкости
        Task.Run(() =>
        {
            try
            {
                var reader = new AudioFileReader(targetFile)
                {
                    Volume = (float)Math.Clamp(_volume, 0.0, 1.0)
                };

                var waveOut = new WaveOutEvent();
                waveOut.Init(reader);

                waveOut.PlaybackStopped += (_, _) =>
                {
                    try
                    {
                        waveOut.Dispose();
                        reader.Dispose();
                    }
                    catch
                    {
                        // Игнорируем исключения при очистке
                    }
                };

                waveOut.Play();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService NAudio] Сбой воспроизведения: {ex.Message}. Переключение на fallback PlaySound.");
                // Надежный WinAPI Fallback
                try
                {
                    PlaySound(targetFile, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT);
                }
                catch
                {
                    // Игнорируем ошибки при отсутствии аудиоустройства
                }
            }
        });
    }

    private static void GenerateFallbackTone(string filePath, double baseFreq, double duration)
    {
        const int sampleRate = 44100;
        int totalSamples = (int)(sampleRate * duration);
        var samples = new short[totalSamples];

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;
            double envelope = Math.Exp(-3.5 * t);
            double sampleVal = Math.Sin(2.0 * Math.PI * baseFreq * t) * envelope * 0.5;
            samples[i] = (short)(Math.Clamp(sampleVal, -1.0, 1.0) * short.MaxValue);
        }

        using var stream = File.Create(filePath);
        using var writer = new BinaryWriter(stream);
        int subChunk2Size = samples.Length * 2;
        int chunkSize = 36 + subChunk2Size;

        writer.Write("RIFF"u8.ToArray());
        writer.Write(chunkSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(subChunk2Size);

        for (int i = 0; i < samples.Length; i++)
        {
            writer.Write(samples[i]);
        }
    }
}
