using System.Reflection;
using System.Text.RegularExpressions;

namespace MosaikCapture.Engine.Tests.MosaikUi;

/// <summary>
/// Rules about the engine's source that keep the look in one place: colours come from the design tokens
/// (engine/MosaikUi), glyphs from IconGlyphs. Diagnostics/ is exempt: its live test paints a stand-in for some
/// other application's window, not a part of this UI.
/// </summary>
public sealed class StructureTests
{
    static string Engine => Path.Combine(typeof(StructureTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(a => a.Key == "RepositoryDirectory").Value!, "engine");

    static IEnumerable<(string File, int Line, string Text)> SourceLines(Func<string, bool> include)
    {
        foreach (string file in Directory.EnumerateFiles(Engine, "*.cs", SearchOption.AllDirectories))
        {
            string relative = file.Substring(Engine.Length + 1).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal) || !include(relative)) continue;
            int number = 0;
            foreach (string line in File.ReadLines(file))
            {
                number++;
                yield return (relative, number, line);
            }
        }
    }

    // A literal colour: Color.FromRgb/FromArgb with numbers, System.Windows.Media.Colors/Brushes (Transparent is no
    // colour), a "#RRGGBB" string. Colours computed from data (pixels, the theme) are fine.
    static readonly Regex RawColor = new(
        @"Color\.From(?:Rgb|Argb|ScRgb)\(\s*(?:0x|\d)|(?<![.\w])Colors\.(?!Length\b)[A-Z]\w*|(?<![.\w])Brushes\.(?!Transparent\b)[A-Z]\w*|""#[0-9A-Fa-f]{3,8}""",
        RegexOptions.CultureInvariant);

    [Fact]
    public void NoRawColorsOutsideTheKit()
    {
        var found = SourceLines(file => !file.StartsWith("MosaikUi/", StringComparison.Ordinal) && !file.StartsWith("Diagnostics/", StringComparison.Ordinal))
            .Where(line => RawColor.IsMatch(line.Text))
            .Select(line => line.File + ":" + line.Line + "  " + line.Text.Trim())
            .ToList();
        Assert.True(found.Count == 0, "raw colours outside engine/MosaikUi (use UiTokens / a Mui.* resource):\n" + string.Join("\n", found));
    }

    [Fact]
    public void TheRuleRecognizesRawColors()
    {
        Assert.Matches(RawColor, "Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x24));");
        Assert.Matches(RawColor, "Foreground = Brushes.White,");
        Assert.Matches(RawColor, "Color = Colors.Black };");
        Assert.Matches(RawColor, "var c = \"#FAF9F5\";");
        Assert.DoesNotMatch(RawColor, "Background = Brushes.Transparent;");
        Assert.DoesNotMatch(RawColor, "for (int i = 0; i < InkStyle.Colors.Length; i++)");
        Assert.DoesNotMatch(RawColor, "swatch.Fill = new SolidColorBrush(Color.FromRgb(pixel[2], pixel[1], pixel[0]));");
    }

    [Fact]
    public void PrivateUseCharactersOnlyInIconGlyphs()
    {
        var found = SourceLines(file => file != "Core/IconGlyphs.cs")
            .Where(line => line.Text.Any(c => c >= '' && c <= ''))
            .Select(line => line.File + ":" + line.Line)
            .ToList();
        Assert.True(found.Count == 0, "invisible private-use glyphs outside IconGlyphs.cs (use IconGlyphs.*):\n" + string.Join("\n", found));
    }

    [Fact]
    public void EveryIconGlyphIsOnePrivateUseCharacter()
    {
        foreach (var field in typeof(IconGlyphs).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
        {
            string glyph = (string)field.GetValue(null)!;
            Assert.True(glyph.Length == 1 && glyph[0] >= '' && glyph[0] <= '', field.Name);
        }
    }
}
