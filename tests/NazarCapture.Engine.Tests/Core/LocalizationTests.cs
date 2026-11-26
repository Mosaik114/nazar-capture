using System.Reflection;

namespace NazarCapture.Engine.Tests.Core;

/// <summary>
/// The interface languages: every catalog has exactly the keys of the German one with the same placeholders, nothing is
/// empty or left in German by accident, the product name stays, "auto" follows Windows, and every text of
/// <see cref="UiText"/> can be read in every language.
/// </summary>
public sealed class LocalizationTests
{
    public static TheoryData<string> Codes => new(UiLanguages.All.Select(language => language.Code));

    [Theory]
    [MemberData(nameof(Codes))]
    public void EveryCatalogHasTheSameKeysAndPlaceholders(string code)
    {
        var german = Catalogs.German;
        var catalog = Catalogs.Of(UiLanguages.ByCode(code));
        Assert.Equal(code, catalog.Language.Code);
        Assert.Equal(german.Keys.OrderBy(k => k, StringComparer.Ordinal), catalog.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (string key in german.Keys)
        {
            string text = catalog.Find(key)!, original = german.Find(key)!;
            Assert.False(string.IsNullOrWhiteSpace(text), code + ": " + key + " is empty");
            Assert.Equal(TextCatalog.PlaceholdersOf(original), TextCatalog.PlaceholdersOf(text));
            Assert.Equal(original.Contains(UiText.Product), text.Contains(UiText.Product));   // the product name is never translated
            Assert.Equal(text.Trim(' ') == text, original.Trim(' ') == original);   // leading spaces are layout ("   ·   Klick: mehr")
        }
    }

    [Fact]
    public void TranslationsAreNotGermanLeftovers()
    {
        // texts that may read the same in German and another language (names, symbols, short shared words)
        string[] same = { "common.size", "pin.zoom", "pin.copy_shortcut", "pin.save_as_shortcut", "editor.width_hint", "recording.gif", "keys.pause", "settings.version", "settings.updates_title", "titles.countdown", "update.title", "settings.countdown", "titles.overlay", "settings.screenshot_group", "main.screenshot", "editor.tool.text", "screenshot_card.text", "keys.ctrl", "keys.shift", "settings.license", "trim.as_gif", "settings.general_group", "toolbar.pause", "recording.ffmpeg_install_hint", "editor.tool.rectangle", "update.reason.download_failed", "editor.color_tip", "editor.tool.blur" };
        foreach (var catalog in Catalogs.All.Where(c => c != Catalogs.German))
        {
            var leftovers = Catalogs.German.Keys
                .Where(key => !same.Contains(key) && catalog.Find(key) == Catalogs.German.Find(key) && Catalogs.German.Find(key)!.Any(char.IsLetter))
                .ToList();
            Assert.True(leftovers.Count == 0, catalog.Language.Code + ": still German: " + string.Join(", ", leftovers));
        }
    }

    [Theory]
    [InlineData(0x07, "de")]
    [InlineData(0x09, "en")]
    [InlineData(0x19, "ru")]
    [InlineData(0x0A, "es")]
    [InlineData(0x0C, "fr")]
    [InlineData(0x01, "ar")]
    [InlineData(0x1F, "tr")]
    [InlineData(0x11, "en")]   // Japanese: English
    [InlineData(0x15, "en")]   // Polish: English
    public void AutoFollowsTheWindowsLanguage(int windowsPrimary, string expected)
    {
        Assert.Equal(expected, UiLanguages.Resolve("auto", windowsPrimary).Code);
        Assert.Equal(expected, UiLanguages.Resolve("unbekannt", windowsPrimary).Code);
        Assert.Equal("tr", UiLanguages.Resolve(" TR ", windowsPrimary).Code);   // a choice stays a choice
    }

    [Fact]
    public void TheSettingsAreAutoAndTheSevenCodes()
    {
        Assert.Equal(new[] { "auto", "de", "en", "ru", "es", "fr", "ar", "tr" }, UiLanguages.Settings);
        Assert.Equal(new[] { "Deutsch", "English", "Русский", "Español", "Français", "العربية", "Türkçe" }, UiLanguages.All.Select(l => l.NativeName));
        Assert.Equal(new[] { "ar" }, UiLanguages.All.Where(l => l.RightToLeft).Select(l => l.Code));
        Assert.Equal("auto", UiLanguages.Normalize(null));
        Assert.Equal("fr", UiLanguages.Normalize("Fr"));
    }

    /// <summary>Every member of UiText, read in every language: a missing key or a broken placeholder throws here.</summary>
    [Theory]
    [MemberData(nameof(Codes))]
    public void EveryTextOfUiTextReadsInEveryLanguage(string code)
    {
        using var language = new LanguageScope(code);
        Assert.Equal(code, Localizer.Language.Code);
        var read = new List<string>();
        foreach (var type in typeof(UiText).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Prepend(typeof(UiText)))
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                object? value = property.GetValue(null);
                if (value is string text) read.Add(text);
                else if (value is string[] list) read.AddRange(list);
                else if (value is (string, string)[] hints) read.AddRange(hints.Select(h => h.Item1 + h.Item2));
            }
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(string) && !m.IsSpecialName))
            {
                var arguments = method.GetParameters().Select(p => p.ParameterType == typeof(string) ? "x" : p.ParameterType == typeof(int) ? 2
                    : p.ParameterType == typeof(double) ? 50.0 : p.ParameterType == typeof(bool) ? true : p.ParameterType.IsEnum ? Enum.GetValues(p.ParameterType).GetValue(0) : null).ToArray();
                read.Add((string)method.Invoke(null, arguments)!);
            }
        }
        foreach (Updates.UpdateProblem problem in Enum.GetValues(typeof(Updates.UpdateProblem))) read.Add(UiText.Update.Reason(problem));
        Assert.True(read.Count > 250, "read " + read.Count);
        Assert.DoesNotContain(read, text => text.Contains('{') || text.Contains("settings.") || text.Contains("common."));
        Assert.Equal(13, UiText.Editor.ToolNames.Length);
        Assert.Equal(5, UiText.Settings.KeepChoices.Length);
    }

    [Fact]
    public void ArabicIsWrittenFromRightToLeftAndTheOthersFromLeftToRight()
    {
        using (new LanguageScope("ar"))
        {
            Assert.Equal(FlowDirection.RightToLeft, Localizer.FlowDirection);
            Assert.Equal("الإعدادات", UiText.Settings.Title);
        }
        Assert.Equal(FlowDirection.LeftToRight, Localizer.FlowDirection);
        Assert.Equal("Einstellungen", UiText.Settings.Title);
    }

    [Fact]
    public void AChangeCountsAndTellsOnlyWhenTheLanguageReallyChanges()
    {
        int told = 0, before = Localizer.Generation;
        void Count() => told++;
        Localizer.Changed += Count;
        try
        {
            Localizer.Use(UiLanguages.German);   // already German: nothing happens
            Assert.Equal(0, told);
            using (new LanguageScope("es")) Assert.Equal("Configuración", UiText.Settings.Title);
            Assert.Equal(2, told);   // to Spanish and back
            Assert.Equal(before + 2, Localizer.Generation);
        }
        finally { Localizer.Changed -= Count; }
    }

    [Fact]
    public void KeyCapsFollowTheLanguage()
    {
        Assert.Equal(new[] { "Strg", "Umschalt", "Alt", "Leertaste" }, HotkeyText.Labels("Alt+Space+Shift+Ctrl"));
        using (new LanguageScope("en")) Assert.Equal(new[] { "Ctrl", "Shift", "Alt", "Space" }, HotkeyText.Labels("Alt+Leertaste+Umschalt+Strg"));
        using (new LanguageScope("fr")) Assert.Equal(new[] { "Maj", "Impr. écran" }, HotkeyText.Labels("Shift+Print"));
        using (new LanguageScope("es")) Assert.Equal("Ctrl+Mayús+4", HotkeyText.Spoken("Shift+Ctrl+4"));
        Assert.Equal("Strg+Bild ↑", HotkeyText.Spoken("Ctrl+PageUp"));
        Assert.Equal("Alt+Pos1", HotkeyText.Spoken("Alt+Home"));
        Assert.Equal("Rollen", HotkeyText.Spoken("ScrollLock"));
        using (new LanguageScope("en")) Assert.Equal("Ctrl+PgUp", HotkeyText.Spoken("Strg+Bild↑"));
        using (new LanguageScope("fr")) Assert.Equal("Ctrl+Suppr", HotkeyText.Spoken("Ctrl+Entf"));
        Assert.Equal("Strg+Alt+←", HotkeyText.Spoken("Ctrl+Alt+Left"));
    }

    [Fact]
    public void ThePlaceholderCheckSeesEveryPlaceholder()
    {
        Assert.Equal(new[] { "{0}", "{1}" }, TextCatalog.PlaceholdersOf("{1} und {0} und {0}"));
        Assert.Empty(TextCatalog.PlaceholdersOf("ohne"));
    }
}
