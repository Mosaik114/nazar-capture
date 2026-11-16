namespace NazarCapture.Engine.NazarUi;

/// <summary>The light token dictionary (generated Tokens.Light.xaml) plus the fonts.</summary>
internal partial class TokensLight : ResourceDictionary
{
    public TokensLight()
    {
        InitializeComponent();
        MuiTheme.AddFonts(this);
    }
}

/// <summary>The dark token dictionary (generated Tokens.Dark.xaml) plus the fonts.</summary>
internal partial class TokensDark : ResourceDictionary
{
    public TokensDark()
    {
        InitializeComponent();
        MuiTheme.AddFonts(this);
    }
}

/// <summary>
/// Which palette the windows show, chosen by the user as in Hams; stored as a number in settings.ini (key
/// "appearance"). Versions up to 1.1.3 also had 0 = like Windows, which the host settles once at start-up.
/// </summary>
internal enum Appearance
{
    Light = 1,
    Dark = 2,
}

/// <summary>
/// The look in one place: puts the light or dark token dictionary into a window's resources. Switching swaps
/// the merged dictionary, so everything bound with DynamicResource / SetResourceReference follows at once and no
/// copied colour stays behind. Both dictionaries carry the same keys (Hams' token names as Mui.*, the fixed tones
/// and, until the windows are rebuilt, the legacy keys).
/// </summary>
internal static class MuiTheme
{
    /// <summary>Resource keys of the two font roles.</summary>
    public const string RoundedFontKey = "Mui.Font.Rounded", BodyFontKey = "Mui.Font.Body";

    // One pair per UI thread: dictionaries are shared by the windows of their thread only.
    [ThreadStatic] static ResourceDictionary? light, dark;

    static readonly Lazy<FontFamily> rounded = new(() =>
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;   // registers the pack:// scheme
        var family = new FontFamily(new Uri("pack://application:,,,/NazarCapture.Engine;component/NazarUi/Fonts/"),
            "./#" + UiTokens.FontNames.Rounded + ", " + UiTokens.FontNames.RoundedFallback);
        return family;
    });

    static readonly FontFamily body = new(UiTokens.FontNames.Body);

    /// <summary>Nunito (embedded, SIL OFL) with Segoe UI Variable Display as fallback: titles, buttons, key caps.</summary>
    public static FontFamily RoundedFont => rounded.Value;

    /// <summary>Segoe UI Variable Text: running text and descriptions.</summary>
    public static FontFamily BodyFont => body;

    public static ResourceDictionary Light => light ??= new TokensLight();

    public static ResourceDictionary Dark => dark ??= new TokensDark();

    /// <summary>The dictionary of one theme.</summary>
    public static ResourceDictionary For(bool lightTheme) => lightTheme ? Light : Dark;

    /// <summary>Makes <paramref name="resources"/> show the given theme (replaces the other one if present).</summary>
    public static void Apply(ResourceDictionary resources, bool lightTheme)
    {
        var wanted = For(lightTheme);
        var merged = resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
            if (merged[i] is TokensLight or TokensDark && !ReferenceEquals(merged[i], wanted)) merged.RemoveAt(i);
        if (!merged.Contains(wanted)) merged.Add(wanted);
    }

    /// <summary>Whether <paramref name="appearance"/> means the light palette.</summary>
    public static bool IsLight(Appearance appearance) => appearance != Appearance.Dark;

    internal static void AddFonts(ResourceDictionary dictionary)
    {
        dictionary[RoundedFontKey] = RoundedFont;
        dictionary[BodyFontKey] = BodyFont;
    }
}

internal static partial class UiTokens
{
    internal static partial class Radius
    {
        /// <summary>A pill: half the height (chips and switches only).</summary>
        public static double Pill(double height) => Math.Max(1, Math.Floor(height / 2));
    }
}
