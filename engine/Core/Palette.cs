using System.Runtime.CompilerServices;

namespace NazarCapture.Engine.Core;

/// <summary>
/// Look of all windows: the theme colors (warm cream or warm dark gray, turquoise accent) as
/// resources that follow the "appearance" setting (light or dark), plus the shared fonts and fixed brushes.
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

    /// <summary>Whether the windows show the light theme: the "appearance" setting, see <see cref="CaptureSettings.Appearance"/>.</summary>
    static bool LightTheme => Force ?? MuiTheme.IsLight(CaptureSettings.Appearance);

    // Every dictionary that shows the setting's theme, with the thread it belongs to (the token dictionaries are
    // per thread), so a change reaches all open windows. Weak: a closed window is not kept alive.
    static readonly List<(WeakReference<ResourceDictionary> Resources, Dispatcher Dispatcher)> following = new();
    static readonly ConditionalWeakTable<ResourceDictionary, Action> afterSwitch = new();

    /// <summary>
    /// Puts the theme colors into <paramref name="resources"/>: the "appearance" setting as read with the latest
    /// settings. From then on the dictionary follows every change at once, see <see cref="ApplyEverywhere"/>;
    /// <paramref name="switched"/> then runs for what copies a color instead of referencing it.
    /// </summary>
    public static void Apply(ResourceDictionary resources, Action? switched = null)
    {
        Follow(resources, switched);
        Apply(resources, LightTheme);
    }

    /// <summary>The token dictionary of the theme (Mui.* tokens, fixed tones and the legacy keys above).</summary>
    public static void Apply(ResourceDictionary resources, bool light) => MuiTheme.Apply(resources, light);

    /// <summary>
    /// The appearance changed: every open window (main window, settings, cards, bars, overlays) shows it at once,
    /// those of the calling thread before this returns, those of other threads on their own thread.
    /// </summary>
    public static void ApplyEverywhere()
    {
        bool light = LightTheme;
        (WeakReference<ResourceDictionary> Resources, Dispatcher Dispatcher)[] all;
        lock (following) all = following.ToArray();
        foreach (var (reference, dispatcher) in all)
        {
            if (!reference.TryGetTarget(out var resources)) continue;
            if (dispatcher.CheckAccess()) Switch(resources, light);
            else if (dispatcher.Thread.IsAlive && !dispatcher.HasShutdownStarted)
                dispatcher.BeginInvoke(new Action(() => Switch(resources, light)));
        }
    }

    static void Switch(ResourceDictionary resources, bool light)
    {
        Apply(resources, light);
        if (afterSwitch.TryGetValue(resources, out var switched)) switched();
    }

    /// <summary>Whether <paramref name="resources"/> follows the setting (tests).</summary>
    internal static bool Follows(ResourceDictionary resources)
    {
        lock (following) return following.Any(entry => entry.Resources.TryGetTarget(out var known) && ReferenceEquals(known, resources));
    }

    static void Follow(ResourceDictionary resources, Action? switched)
    {
        if (switched != null)
        {
            afterSwitch.Remove(resources);
            afterSwitch.Add(resources, switched);
        }
        lock (following)
        {
            following.RemoveAll(entry => !entry.Resources.TryGetTarget(out _));
            if (following.Any(entry => entry.Resources.TryGetTarget(out var known) && ReferenceEquals(known, resources))) return;
            following.Add((new WeakReference<ResourceDictionary>(resources), Dispatcher.CurrentDispatcher));
        }
    }
}
