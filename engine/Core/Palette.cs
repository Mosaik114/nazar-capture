using Microsoft.Win32;

namespace MosaikCapture.Engine.Core;

/// <summary>
/// Look of all windows: the theme colors (warm cream or warm dark gray, turquoise accent) as
/// resources that follow the Windows light/dark setting, plus the shared fonts and fixed brushes.
/// The accent comes in two roles: <see cref="Accent"/> for lines, icons, bars and frames (dark
/// turquoise on cream, bright turquoise on dark gray, both at least 3:1 against the background) and
/// <see cref="AccentFill"/> for surfaces that carry light text or icons (at least 4.5:1 with
/// <see cref="Light"/> in both themes).
/// </summary>
static class Palette
{
    // ---------- Resource keys (theme colors) ----------

    public const string Background = "Bg";
    public const string Selected = "Sel";
    public const string Hover = "Hover";
    public const string Line = "Line";
    public const string Text = "Text";
    public const string Muted = "Muted";
    public const string Accent = "Accent";
    public const string Chip = "Chip";
    public const string AccentSoft = "AccentSoft";
    /// <summary>Accent surface under light text or icons (primary buttons, trim handles).</summary>
    public const string AccentFill = "AccentFill";
    /// <summary><see cref="AccentFill"/> while hovered or pressed: darker, so the text stays readable.</summary>
    public const string AccentFillHover = "AccentFillHover";
    /// <summary>
    /// Recording red for the toolbar dot, "off" icons and error text: a clear red (hue 355-358°),
    /// at least 4.5:1 against the background in both themes.
    /// </summary>
    public const string RecordingRed = "RecordingRed";
    /// <summary>Red surface under light icons (the discard button while it waits for confirmation).</summary>
    public const string RecordingRedFill = "RecordingRedFill";
    /// <summary><see cref="RecordingRed"/> as a subtle fill (background of the failure cards' status chip).</summary>
    public const string WarningSoft = "WarningSoft";

    // ---------- Fonts ----------

    public static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily DigitFont = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily TitleFont = new("Georgia");

    // ---------- Fixed brushes (same in light and dark, from the design tokens) ----------

    /// <summary>Light foreground on dark or accent surfaces.</summary>
    public static SolidColorBrush Light => UiTokens.Fixed.LightOnDarkBrush;
    /// <summary>Dimming outside the selected area.</summary>
    public static SolidColorBrush Dimming => UiTokens.Fixed.DimmingBrush;
    /// <summary>Darkened chip on top of an image (size, duration, play button).</summary>
    public static SolidColorBrush ImageChip => UiTokens.Fixed.ImageChipBrush;
    /// <summary>Translucent accent filling the circle of a left click in recordings.</summary>
    public static SolidColorBrush ClickFill => UiTokens.Fixed.ClickFillBrush;

    internal static bool? Force;   // tests only: light/dark regardless of the settings and Windows

    /// <summary>
    /// Puts the theme colors into <paramref name="resources"/>: the "appearance" setting (light, dark or like
    /// Windows) as read with the latest settings, see <see cref="CaptureSettings.Appearance"/>.
    /// </summary>
    public static void Apply(ResourceDictionary resources) =>
        Apply(resources, Force ?? MuiTheme.IsLight(CaptureSettings.Appearance, WindowsUsesLightTheme));

    /// <summary>The token dictionary of the theme (Mui.* tokens, fixed tones and the legacy keys above).</summary>
    public static void Apply(ResourceDictionary resources, bool light) => MuiTheme.Apply(resources, light);

    static bool WindowsUsesLightTheme()
    {
        try
        {
            object? value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0);
            return value is int light && light != 0;
        }
        catch (System.Security.SecurityException) { return false; }   // setting not readable: stay dark
        catch (IOException) { return false; }
    }
}
