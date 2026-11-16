using System.Windows.Media.Effects;

namespace MosaikCapture.Engine.MosaikUi;

/// <summary>
/// The looks of what the user draws into a screenshot (number badges, text plates, region frames). They are part of
/// the picture, not of the interface, but their effects and radii live here as well, so the kit stays the only place
/// that makes shadows and corner radii.
/// </summary>
internal static class MarkupLook
{
    /// <summary>A hard dark copy under the light dashes of the pixelate, blur and spotlight frame: one of the two always shows.</summary>
    public static DropShadowEffect RegionOutline() => Frozen(new DropShadowEffect { Color = UiTokens.Fixed.Shadow, ShadowDepth = 1.5, Direction = 315, BlurRadius = 0, Opacity = 0.85 });

    /// <summary>The soft lift of a number badge from the picture.</summary>
    public static DropShadowEffect BadgeShadow() => Frozen(new DropShadowEffect { Color = UiTokens.Fixed.Shadow, BlurRadius = 7, ShadowDepth = 1.5, Direction = 270, Opacity = 0.38 });

    /// <summary>A halo that keeps plain text readable on any picture.</summary>
    public static DropShadowEffect TextHalo() => Frozen(new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.6, Color = UiTokens.Fixed.Shadow });

    /// <summary>The lift of a text plate.</summary>
    public static DropShadowEffect PlateShadow() => Frozen(new DropShadowEffect { Color = UiTokens.Fixed.Shadow, BlurRadius = 8, ShadowDepth = 1.5, Direction = 270, Opacity = 0.35 });

    /// <summary>Equal corners of a markup plate or badge.</summary>
    public static CornerRadius Corners(double radius) => new(radius);

    static DropShadowEffect Frozen(DropShadowEffect effect)
    {
        effect.Freeze();
        return effect;
    }
}
