using System.Reflection;

namespace NazarCapture.Engine.Tests.NazarUi;

/// <summary>Phase 8: one product name, and the old one is gone from every text a user sees.</summary>
public sealed class NameTests
{
    /// <summary>Texts that may keep the old word: none since the technical rename (existing users keep their folders,
    /// because settings.ini stores them).</summary>
    static readonly string[] Kept = Array.Empty<string>();

    static IEnumerable<(string Name, string Text)> Texts(Type type)
    {
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is string text) yield return (type.Name + "." + field.Name, text);
            if (field.GetValue(null) is string[] list) foreach (var item in list) yield return (type.Name + "." + field.Name, item);
        }
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            foreach (var entry in Texts(nested)) yield return entry;
    }

    [Fact]
    public void TheOldNameIsNotInAnyVisibleText()
    {
        var found = Texts(typeof(UiText))
            .Where(t => t.Text.IndexOf("Mosaik", StringComparison.OrdinalIgnoreCase) >= 0 && !Kept.Any(k => t.Name.EndsWith("." + k, StringComparison.Ordinal)))
            .Select(t => t.Name + " = " + t.Text)
            .ToList();
        Assert.True(found.Count == 0, "old name in visible texts:\n" + string.Join("\n", found));
        Assert.Equal("Nazar Capture", UiText.Product);
        foreach (var (name, text) in Texts(typeof(UiText.Titles)))
            Assert.True(text.StartsWith(UiText.Product, StringComparison.Ordinal) || name.EndsWith(".Trim", StringComparison.Ordinal), name + " = " + text);
    }

    [Fact]
    public void TheEngineCarriesTheProductNameAndVersion()
    {
        var assembly = typeof(UiText).Assembly;
        Assert.Equal("Nazar Capture", assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
        Assert.Equal("1.1.4", Settings.AppInfo.Version);
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
