using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace NazarCapture.Engine.Tests.NazarUi;

/// <summary>
/// The design tokens: Hams' contrast rules (tests/test_tokens.py) with the same limits, run against the two token
/// dictionaries the windows use, and the agreement of the dictionaries with design-tokens.json and with Hams.
/// </summary>
public sealed class TokenTests
{
    public static TheoryData<bool> Modes => new() { true, false };

    static Dictionary<string, Color> Palette(bool light) => WpfThread.Run(() =>
    {
        var dictionary = MuiTheme.For(light);
        return UiTokens.ThemeTokenNames.ToDictionary(name => name, name => ((SolidColorBrush)dictionary["Mui." + Pascal(name)]).Color);
    });

    static string Pascal(string name) => string.Concat(name.ToLowerInvariant().Split('_').Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));

    internal static double Luminance(Color c)
    {
        // WCAG 2.1, with the 0.03928 threshold Hams uses
        static double Channel(byte value)
        {
            double v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    internal static double Contrast(Color a, Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    // Text on a surface: WCAG AA for normal text needs 4.5:1 (Hams TEXT_ON_SURFACE).
    static readonly (string Text, string Surface)[] TextOnSurface =
    {
        ("TEXT_PRIMARY", "WINDOW_BG"), ("TEXT_PRIMARY", "GROUP_SURFACE"), ("TEXT_PRIMARY", "CONTROL_SURFACE"),
        ("TEXT_PRIMARY", "CONTROL_HOVER"), ("TEXT_PRIMARY", "POPOVER_SURFACE"), ("TEXT_SECONDARY", "WINDOW_BG"),
        ("TEXT_SECONDARY", "GROUP_SURFACE"), ("TEXT_SECONDARY", "CONTROL_SURFACE"), ("TEXT_SECONDARY", "CONTROL_HOVER"),
        ("TEXT_SECONDARY", "POPOVER_SURFACE"), ("TEXT_TERTIARY", "WINDOW_BG"), ("TEXT_TERTIARY", "GROUP_SURFACE"),
        ("TEXT_DISABLED", "GROUP_SURFACE"), ("TEXT_PRIMARY", "CONTROL_PRESSED"), ("ACCENT_TINT", "WINDOW_BG"),
        ("AI", "WINDOW_BG"), ("ERROR", "WINDOW_BG"), ("ERROR", "GROUP_SURFACE"),
        ("ON_ACCENT", "ACCENT_SURFACE"), ("ON_ACCENT", "ACCENT_HOVER"), ("ON_ACCENT", "ACCENT_PRESSED"),
        ("ON_SUCCESS", "SUCCESS"), ("ON_FILL", "ERROR_SURFACE"), ("ON_FILL", "RECORDING_SURFACE"),
        ("ON_FILL", "RECORDING_HOVER"), ("ON_FILL", "RECORDING_PRESSED"), ("ON_FILL", "AI_SURFACE"),
        ("ON_FILL", "AI_HOVER"), ("ON_FILL", "AI_PRESSED"),
    };

    // Graphics (dots, bars, outlines): 3:1 after WCAG 1.4.11 (Hams GRAPHIC_ON_SURFACE), plus the cobalt focus edge.
    static readonly (string Graphic, string Surface)[] GraphicOnSurface =
    {
        ("SUCCESS", "WINDOW_BG"), ("WARNING", "WINDOW_BG"), ("ERROR", "WINDOW_BG"), ("RECORDING", "WINDOW_BG"),
        ("AI", "WINDOW_BG"), ("SUCCESS", "GROUP_SURFACE"), ("ERROR", "GROUP_SURFACE"), ("WARNING", "GROUP_SURFACE"),
        ("AI", "GROUP_SURFACE"), ("ACCENT_TINT", "GROUP_SURFACE"), ("CONTROL_OUTLINE", "GROUP_SURFACE"),
        ("FOCUS", "WINDOW_BG"), ("FOCUS", "GROUP_SURFACE"), ("FOCUS", "CONTROL_SURFACE"), ("FOCUS", "CONTROL_HOVER"),
        ("ACCENT_TINT", "CONTROL_SURFACE"),
    };

    [Theory]
    [MemberData(nameof(Modes))]
    public void TextMeetsWcagAa(bool light)
    {
        var p = Palette(light);
        var failing = TextOnSurface.Select(pair => (pair, ratio: Contrast(p[pair.Text], p[pair.Surface])))
            .Where(x => x.ratio < 4.5).Select(x => x.pair.Text + " on " + x.pair.Surface + ": " + x.ratio.ToString("0.00", CultureInfo.InvariantCulture));
        Assert.Empty(failing);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void GraphicsMeetWcagNonText(bool light)
    {
        var p = Palette(light);
        var failing = GraphicOnSurface.Select(pair => (pair, ratio: Contrast(p[pair.Graphic], p[pair.Surface])))
            .Where(x => x.ratio < 3).Select(x => x.pair.Graphic + " on " + x.pair.Surface + ": " + x.ratio.ToString("0.00", CultureInfo.InvariantCulture));
        Assert.Empty(failing);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void TertiaryTextStaysOnTheQuietSurfaces(bool light)
    {
        var p = Palette(light);
        foreach (string surface in new[] { "WINDOW_BG", "GROUP_SURFACE" }) Assert.True(Contrast(p["TEXT_TERTIARY"], p[surface]) >= 4.5, surface);
        foreach (string surface in new[] { "CONTROL_SURFACE", "CONTROL_HOVER", "POPOVER_SURFACE" }) Assert.True(Contrast(p["TEXT_SECONDARY"], p[surface]) >= 4.5, surface);
    }

    static readonly (string Text, string Base, string Hover, string Pressed)[] Fills =
    {
        ("ON_ACCENT", "ACCENT_SURFACE", "ACCENT_HOVER", "ACCENT_PRESSED"),
        ("ON_FILL", "RECORDING_SURFACE", "RECORDING_HOVER", "RECORDING_PRESSED"),
        ("ON_FILL", "AI_SURFACE", "AI_HOVER", "AI_PRESSED"),
    };

    [Theory]
    [MemberData(nameof(Modes))]
    public void HoverDarkensTheFillsAndNeverLowersTheTextContrast(bool light)
    {
        var p = Palette(light);
        foreach (var (text, fill, hover, pressed) in Fills)
        {
            Assert.True(Contrast(p[text], p[hover]) >= Contrast(p[text], p[fill]), fill);
            Assert.True(Luminance(p[fill]) > Luminance(p[hover]) && Luminance(p[hover]) > Luminance(p[pressed]), fill);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void InkAndSidesStandOut(bool light)
    {
        var p = Palette(light);
        foreach (string name in new[] { "INK", "GROUP_SIDE", "CONTROL_SIDE" })
            foreach (string ground in new[] { "WINDOW_BG", "GROUP_SURFACE" })
                Assert.True(Contrast(p[name], p[ground]) >= 1.3, name + " on " + ground);
        Assert.True(Luminance(p["CONTROL_PRESSED"]) < Luminance(p["CONTROL_SURFACE"]));
        Assert.True(Contrast(p["TEXT_DISABLED"], p["GROUP_SURFACE"]) < Contrast(p["TEXT_TERTIARY"], p["GROUP_SURFACE"]));
        // the accent side is a darker tone of the accent fill
        Assert.True(Luminance(p["ACCENT_SIDE"]) < Luminance(p["ACCENT_PRESSED"]));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void SurfaceLadderStepsAwayFromTheWindowInSmallVisibleSteps(bool light)
    {
        var p = Palette(light);
        double window = Luminance(p["WINDOW_BG"]), group = Luminance(p["GROUP_SURFACE"]), control = Luminance(p["CONTROL_SURFACE"]), hover = Luminance(p["CONTROL_HOVER"]);
        if (light) Assert.True(group > window && group > control && control > hover);
        else Assert.True(window < group && group < control && control < hover);
        Assert.True(Luminance(p["POPOVER_SURFACE"]) > group);
        Assert.True(Contrast(p["HAIRLINE"], p["GROUP_SURFACE"]) >= 1.2);
        Assert.True(Contrast(p["GROUP_SURFACE"], p["WINDOW_BG"]) < 1.5);
        Assert.True(Contrast(p["CONTROL_SURFACE"], p["GROUP_SURFACE"]) < 1.5);
        foreach (var (a, b) in new[] { ("GROUP_SURFACE", "WINDOW_BG"), ("CONTROL_SURFACE", "GROUP_SURFACE"), ("CONTROL_HOVER", "CONTROL_SURFACE") })
            Assert.True(Contrast(p[a], p[b]) >= 1.10, a + " to " + b);
    }

    [Fact]
    public void BothThemesCarryTheSameKeysFrozenAndTheyDiffer() => WpfThread.Run(() =>
    {
        var light = MuiTheme.Light;
        var dark = MuiTheme.Dark;
        var lightKeys = light.Keys.Cast<object>().Select(k => k.ToString()).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var darkKeys = dark.Keys.Cast<object>().Select(k => k.ToString()).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(lightKeys, darkKeys);
        foreach (object key in light.Keys)
            if (light[key] is Freezable freezable) Assert.True(freezable.IsFrozen, key + " should be frozen");
        var p = Palette(true);
        var q = Palette(false);
        var same = p.Keys.Where(name => p[name] == q[name]).ToList();
        Assert.True(same.Count <= 6, string.Join(", ", same));
    });

    /// <summary>Two tokens that share a value in one theme share it in the other (Hams' remap needs that).</summary>
    [Fact]
    public void SharedValuesFormTheSameGroupsInBothThemes()
    {
        static HashSet<string> Groups(Dictionary<string, Color> palette) => new(palette
            .Where(entry => entry.Key != "ON_SUCCESS")
            .GroupBy(entry => entry.Value)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join("+", group.Select(entry => entry.Key).OrderBy(k => k, StringComparer.Ordinal))));
        Assert.Equal(Groups(Palette(true)).OrderBy(g => g, StringComparer.Ordinal), Groups(Palette(false)).OrderBy(g => g, StringComparer.Ordinal));
    }

    [Fact]
    public void MeasuresFollowHamsRules()
    {
        foreach (double value in new[] { UiTokens.Space.Window, UiTokens.Space.SectionGap, UiTokens.Space.GroupGap, UiTokens.Space.ControlGap, UiTokens.Space.IconGap, UiTokens.Space.RowX })
            Assert.Equal(0, value % 2);
        double[] scale = { UiTokens.Space.Xs, UiTokens.Space.Sm, UiTokens.Space.Md, UiTokens.Space.Lg, UiTokens.Space.Xl, UiTokens.Space.Xxl };
        Assert.Equal(scale.OrderBy(v => v), scale);
        Assert.Equal(scale.Length, scale.Distinct().Count());
        Assert.Equal(UiTokens.Radius.Window, UiTokens.Radius.Popover);
        Assert.Equal(UiTokens.Radius.Control, UiTokens.Radius.Button);
        Assert.True(UiTokens.Radius.Segment < UiTokens.Radius.Control && UiTokens.Radius.Control < UiTokens.Radius.Group && UiTokens.Radius.Group < UiTokens.Radius.Window);
        Assert.Equal(12, UiTokens.Radius.Pill(24));
        Assert.Equal(1, UiTokens.Radius.Pill(1));
        Assert.True(UiTokens.Height.Compact < UiTokens.Height.Standard && UiTokens.Height.Standard < UiTokens.Height.Primary);
        Assert.True(UiTokens.Depth.Keycap < UiTokens.Depth.Control && UiTokens.Depth.Control < UiTokens.Depth.Group);
    }

    // ---------- design-tokens.json, the dictionaries and Hams ----------

    static Dictionary<string, object> Json(string file) =>
        new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)));

    static Dictionary<string, string> Section(Dictionary<string, object> root, params string[] path)
    {
        object node = root;
        foreach (string step in path) node = ((Dictionary<string, object>)node)[step];
        return ((Dictionary<string, object>)node).Where(e => e.Key[0] != '$').ToDictionary(e => e.Key, e => ((string)e.Value).ToUpperInvariant());
    }

    static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheDictionariesAreGeneratedFromTheCurrentTokens(bool light) => WpfThread.Run(() =>
    {
        var tokens = Json("design-tokens.json");
        string mode = light ? "light" : "dark";
        var expected = Section(tokens, "shared", mode);
        foreach (var own in Section(tokens, "apps", "capture", mode)) expected[own.Key] = own.Value;
        var dictionary = MuiTheme.For(light);

        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal), UiTokens.ThemeTokenNames.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var token in expected) Assert.Equal(Parse(token.Value), ((SolidColorBrush)dictionary["Mui." + Pascal(token.Key)]).Color);
        foreach (var token in Section(tokens, "fixed")) Assert.Equal(Parse(token.Value), ((SolidColorBrush)dictionary["Mui." + Pascal(token.Key)]).Color);
        foreach (var token in Section(tokens, "legacy", mode)) Assert.Equal(Parse(token.Value), ((SolidColorBrush)dictionary[token.Key]).Color);
    });

    [Fact]
    public void TheValuesForHamsAreTheSharedTokensWithHamsAccent()
    {
        var tokens = Json("design-tokens.json");
        var hams = Json("hams-theme-values.json");
        foreach (string mode in new[] { "light", "dark" })
        {
            var expected = Section(tokens, "shared", mode);
            foreach (var own in Section(tokens, "apps", "hams", mode)) expected[own.Key] = own.Value;
            Assert.Equal(expected.OrderBy(e => e.Key, StringComparer.Ordinal), Section(hams, mode).OrderBy(e => e.Key, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The shared tokens are Hams' tokens: compared with hams/ui_kit/theme.py when Hams is checked out next to this
    /// repository (or HAMS_THEME points to theme.py); only the accent may differ (cobalt here, turquoise in Hams).
    /// </summary>
    [Fact]
    public void SharedTokensEqualHams()
    {
        string repository = typeof(TokenTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().First(a => a.Key == "RepositoryDirectory").Value!;
        string theme = Environment.GetEnvironmentVariable("HAMS_THEME") is { Length: > 0 } set ? set
            : Path.Combine(Path.GetDirectoryName(repository)!, "Hams-GitHub", "hams", "ui_kit", "theme.py");
        Assert.SkipUnless(File.Exists(theme), "Hams' theme.py not found (set HAMS_THEME): " + theme);

        string source = File.ReadAllText(theme);
        var tokens = Json("design-tokens.json");
        foreach (var (mode, marker) in new[] { ("dark", "DARK: {"), ("light", "LIGHT: {") })
        {
            int start = source.IndexOf(marker, source.IndexOf("_PALETTES", StringComparison.Ordinal), StringComparison.Ordinal);
            string block = source.Substring(start, source.IndexOf('}', start) - start);
            var hams = Regex.Matches(block, "\"([A-Z_]+)\":\\s*\"(#[0-9A-Fa-f]{6})\"").Cast<Match>()
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToUpperInvariant());
            var expected = Section(tokens, "shared", mode);
            foreach (var own in Section(tokens, "apps", "hams", mode)) expected[own.Key] = own.Value;
            Assert.Equal(expected.OrderBy(e => e.Key, StringComparer.Ordinal), hams.OrderBy(e => e.Key, StringComparer.Ordinal));
            // and this app differs from Hams only in its accent
            var accent = Section(tokens, "apps", "capture", mode).Keys;
            Assert.Equal(accent.OrderBy(k => k, StringComparer.Ordinal), Section(tokens, "apps", "hams", mode).Keys.OrderBy(k => k, StringComparer.Ordinal));
        }
    }

    // ---------- Fonts ----------

    [Fact]
    public void NunitoIsEmbeddedWithItsWeights() => WpfThread.Run(() =>
    {
        var family = MuiTheme.RoundedFont;
        double Width(FontWeight weight)
        {
            var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
            Assert.True(typeface.TryGetGlyphTypeface(out var glyphs), "Nunito did not load");
            Assert.Contains("Nunito", glyphs.FamilyNames.Values);
            Assert.Equal(StyleSimulations.None, glyphs.StyleSimulations);
            return new FormattedText("Nazar Capture", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 40, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;
        }
        // a real bold instance of the variable font, not a simulated one
        Assert.True(Width(FontWeights.Bold) > Width(FontWeights.Normal) + 2);
        Assert.Same(family, MuiTheme.Light[MuiTheme.RoundedFontKey]);
        Assert.Same(MuiTheme.BodyFont, MuiTheme.Dark[MuiTheme.BodyFontKey]);
        Assert.NotNull(Application.GetResourceStream(new Uri("pack://application:,,,/NazarCapture.Engine;component/NazarUi/Fonts/OFL.txt")));
    });
}
