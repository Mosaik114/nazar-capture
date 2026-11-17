namespace NazarCapture.Engine.Tests.Infrastructure;

/// <summary>Switches the interface language for a test and back to German (the language every other test expects).</summary>
internal sealed class LanguageScope : IDisposable
{
    public LanguageScope(UiLanguage language) => Localizer.Use(language);

    public LanguageScope(string code) : this(UiLanguages.ByCode(code)) { }

    public void Dispose() => Localizer.Use(UiLanguages.German);
}
