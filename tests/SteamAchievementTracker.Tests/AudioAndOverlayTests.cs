using SteamAchievementTracker.Core.Models;
using SteamAchievementTracker.Infrastructure.Audio;
using SteamAchievementTracker.Overlay.Graphics;
using Xunit;

namespace SteamAchievementTracker.Tests;

public class AudioAndOverlayTests : IDisposable
{
    private readonly string _testAudioDir;

    public AudioAndOverlayTests()
    {
        _testAudioDir = Path.Combine(Path.GetTempPath(), "SAT_AudioTest_" + Guid.NewGuid());
    }

    [Fact]
    public void AudioService_Generates_Valid_Pcm_Wav_Files()
    {
        var service = new AudioService(_testAudioDir);
        service.EnsureAudioAssets();

        var standard = Path.Combine(_testAudioDir, "standard_unlock.wav");
        var platinum = Path.Combine(_testAudioDir, "rare_platinum_unlock.wav");

        Assert.True(File.Exists(standard));
        Assert.True(File.Exists(platinum));

        // Проверяем валидность RIFF WAVE заголовка
        using var stream = File.OpenRead(standard);
        using var reader = new BinaryReader(stream);

        var riff = new string(reader.ReadChars(4));
        Assert.Equal("RIFF", riff);

        var chunkSize = reader.ReadInt32();
        Assert.True(chunkSize > 44);

        var wave = new string(reader.ReadChars(4));
        Assert.Equal("WAVE", wave);
    }

    [Fact]
    public void ProceduralOverlayEngine_Transitions_Correctly()
    {
        var engine = new ProceduralOverlayEngine();
        Assert.False(engine.IsActive);

        var payload = new NotificationPayload
        {
            AppId = 413150,
            Title = "Master of Secrets",
            Description = "Find all secret notes",
            Rank = AchievementRank.Gold,
            GlobalPercent = 11.2
        };

        engine.Trigger(payload);
        Assert.True(engine.IsActive);

        // Кадр 1: Входная анимация (Slide-in)
        var frame1 = engine.Update(0.1f, 1920, 1080);
        Assert.True(frame1.IsVisible);
        Assert.Equal("Master of Secrets", frame1.Title);
        Assert.Equal((255, 215, 0), frame1.GlowColor); // Gold RGB
        Assert.True(frame1.Y < ProceduralOverlayEngine.TargetTopY); // Еще выезжает сверху

        // Перемотка на фазу Display (0.5 сек вперед)
        var frame2 = engine.Update(0.5f, 1920, 1080);
        Assert.True(frame2.IsVisible);
        Assert.Equal(ProceduralOverlayEngine.TargetTopY, frame2.Y); // Достигла фиксации
        Assert.Equal(1.0f, frame2.Alpha);

        // Перемотка через Display (4.5 сек вперед) -> должна уйти в SlideOut
        var frame3 = engine.Update(4.5f, 1920, 1080);
        Assert.True(frame3.IsVisible);

        // Перемотка до полного завершения (1.0 сек вперед) -> Idle
        var frame4 = engine.Update(1.0f, 1920, 1080);
        Assert.False(frame4.IsVisible);
        Assert.False(engine.IsActive);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testAudioDir))
        {
            try { Directory.Delete(_testAudioDir, true); } catch { }
        }
    }
}
