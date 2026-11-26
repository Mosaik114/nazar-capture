using System.Reflection;

namespace NazarCapture.Engine.Tests.NazarUi;

/// <summary>Phase 8: one product name, and the old one is gone from every text a user sees.</summary>
public sealed class NameTests
{
    /// <summary>Every text of every catalog.</summary>
    static IEnumerable<(string Name, string Text)> Texts() =>
        Catalogs.All.SelectMany(catalog => catalog.Keys.Select(key => (catalog.Language.Code + ":" + key, catalog.Find(key)!)));

    static IEnumerable<(string Name, string Text)> Titles() =>
        typeof(UiText.Titles).GetProperties(BindingFlags.Public | BindingFlags.Static).Select(p => (p.Name, (string)p.GetValue(null)!));

    [Fact]
    public void TheOldNameIsNotInAnyVisibleText()
    {
        var found = Texts()
            .Where(t => t.Text.IndexOf("Mosaik", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(t => t.Name + " = " + t.Text)
            .ToList();
        Assert.True(found.Count == 0, "old name in visible texts:\n" + string.Join("\n", found));
        Assert.Equal("Nazar Capture", UiText.Product);
        foreach (var catalog in Catalogs.All)
        {
            using var language = new LanguageScope(catalog.Language);
            foreach (var (name, text) in Titles())
                Assert.True(text.StartsWith(UiText.Product, StringComparison.Ordinal) || name == "Trim", catalog.Language.Code + ": " + name + " = " + text);
        }
    }
    [Fact]
    public void TheEngineCarriesTheProductNameAndVersion()
    {
        var assembly = typeof(UiText).Assembly;
        Assert.Equal("Nazar Capture", assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
        Assert.Equal("1.1.8", Settings.AppInfo.Version);
    }

    [Fact]
    public void TheWindowHeadShowsTheLogoAndTheName() => WpfThread.Run(() =>
    {
        var mark = new AppMark();
        Assert.NotNull(mark.Source);
        Assert.Equal(UiText.Product, System.Windows.Automation.AutomationProperties.GetName(mark));
        var window = new NazarWindow("Einstellungen");
        Assert.Equal("Nazar Capture – Einstellungen", window.Title);
        window.Close();
    });
}
