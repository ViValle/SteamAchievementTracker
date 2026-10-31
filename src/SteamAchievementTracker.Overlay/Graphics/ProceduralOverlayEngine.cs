using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Overlay.Graphics;

public enum AnimationPhase
{
    Idle,
    SlideIn,
    Display,
    SlideOut
}

public record OverlayRenderFrame(
    bool IsVisible,
    float X,
    float Y,
    float Width,
    float Height,
    float Alpha,
    float GlowIntensity,
    (byte R, byte G, byte B) GlowColor,
    string Title,
    string Description,
    double GlobalPercent,
    string RankText,
    string? IconPath);

public class ProceduralOverlayEngine
{
    public const float BannerWidth = 420.0f;
    public const float BannerHeight = 84.0f;
    public const float TargetTopY = 32.0f; // Top-Center отступ от верхней границы экрана

    private const float SlideDuration = 0.42f;
    private const float DisplayDuration = 4.2f;

    private AnimationPhase _phase = AnimationPhase.Idle;
    private float _phaseTime;

    private NotificationPayload? _currentPayload;

    public bool IsActive => _phase != AnimationPhase.Idle;

    public void Trigger(NotificationPayload payload)
    {
        _currentPayload = payload;
        _phase = AnimationPhase.SlideIn;
        _phaseTime = 0.0f;
    }

    public OverlayRenderFrame Update(float deltaTimeSeconds, int screenWidth, int screenHeight)
    {
        if (_phase == AnimationPhase.Idle || _currentPayload == null)
        {
            return new OverlayRenderFrame(
                IsVisible: false, 0, 0, BannerWidth, BannerHeight, 0, 0, (0, 0, 0),
                string.Empty, string.Empty, 0, string.Empty, null);
        }

        _phaseTime += deltaTimeSeconds;
        AdvanceStateMachine();

        if (_phase == AnimationPhase.Idle || _currentPayload == null)
        {
            return new OverlayRenderFrame(
                IsVisible: false, 0, 0, BannerWidth, BannerHeight, 0, 0, (0, 0, 0),
                string.Empty, string.Empty, 0, string.Empty, null);
        }

        float progress = 0.0f;
        float alpha = 1.0f;
        float currentY = -BannerHeight;
        float glowIntensity = 1.0f;

        switch (_phase)
        {
            case AnimationPhase.SlideIn:
                progress = Math.Clamp(_phaseTime / SlideDuration, 0.0f, 1.0f);
                alpha = progress;
                // Fade-in + Slide-down с кривой замедления EaseOutCubic
                currentY = -BannerHeight + (TargetTopY + BannerHeight) * EaseOutCubic(progress);
                glowIntensity = 1.2f * progress;
                break;

            case AnimationPhase.Display:
                alpha = 1.0f;
                currentY = TargetTopY;
                // Нежное мерцание свечения (пульсация)
                glowIntensity = 0.9f + 0.15f * MathF.Sin(_phaseTime * 3.5f);
                break;

            case AnimationPhase.SlideOut:
                progress = Math.Clamp(_phaseTime / SlideDuration, 0.0f, 1.0f);
                alpha = 1.0f - progress;
                // Slide-up + Fade-out с кривой ускорения EaseInCubic
                currentY = TargetTopY - (TargetTopY + BannerHeight) * EaseInCubic(progress);
                glowIntensity = 1.0f - progress;
                break;
        }

        float centerX = (screenWidth - BannerWidth) / 2.0f;
        var rgb = _currentPayload.Rank.GetRgbColor();

        return new OverlayRenderFrame(
            IsVisible: true,
            X: centerX,
            Y: currentY,
            Width: BannerWidth,
            Height: BannerHeight,
            Alpha: alpha,
            GlowIntensity: glowIntensity,
            GlowColor: rgb,
            Title: _currentPayload.Title,
            Description: _currentPayload.Description,
            GlobalPercent: _currentPayload.GlobalPercent,
            RankText: _currentPayload.Rank.GetDisplayName().ToUpperInvariant(),
            IconPath: _currentPayload.IconPath);
    }

    private void AdvanceStateMachine()
    {
        switch (_phase)
        {
            case AnimationPhase.SlideIn when _phaseTime >= SlideDuration:
                _phase = AnimationPhase.Display;
                _phaseTime = 0.0f;
                break;

            case AnimationPhase.Display when _phaseTime >= DisplayDuration:
                _phase = AnimationPhase.SlideOut;
                _phaseTime = 0.0f;
                break;

            case AnimationPhase.SlideOut when _phaseTime >= SlideDuration:
                _phase = AnimationPhase.Idle;
                _phaseTime = 0.0f;
                _currentPayload = null;
                break;
        }
    }

    private static float EaseOutCubic(float t)
    {
        float f = 1.0f - t;
        return 1.0f - f * f * f;
    }

    private static float EaseInCubic(float t)
    {
        return t * t * t;
    }
}
