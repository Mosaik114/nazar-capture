namespace NazarCapture.Engine.Core.Localization;

/// <summary>An interface language: its code in settings.ini, its own name (shown in the select) and its writing direction.</summary>
internal sealed record UiLanguage(string Code, string NativeName, bool RightToLeft)
{
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Code);
}

/// <summary>
/// The interface languages of Nazar Capture (the same as Hams Dictate's), and how "auto" becomes one of them: the Windows
/// display language when it is one of these, English for every other (German for German, as the app began). The host
/// resolves "auto" the same way (Language::resolve in src/config.rs).
/// </summary>
internal static class UiLanguages
{
    /// <summary>The setting that follows Windows.</summary>
    public const string Auto = "auto";

    public static readonly UiLanguage German = new("de", "Deutsch", false);
    public static readonly UiLanguage English = new("en", "English", false);
    public static readonly UiLanguage Russian = new("ru", "Русский", false);
    public static readonly UiLanguage Spanish = new("es", "Español", false);
    public static readonly UiLanguage French = new("fr", "Français", false);
    public static readonly UiLanguage Arabic = new("ar", "العربية", true);
    public static readonly UiLanguage Turkish = new("tr", "Türkçe", false);

    /// <summary>Every language, in the order of the settings select (after "Automatisch").</summary>
    public static readonly IReadOnlyList<UiLanguage> All = new[] { German, English, Russian, Spanish, French, Arabic, Turkish };

    /// <summary>The values of the setting: "auto" and every code.</summary>
    public static IEnumerable<string> Settings => new[] { Auto }.Concat(All.Select(language => language.Code));

    /// <summary>A valid setting: case and spaces do not matter; anything unknown is "auto".</summary>
    public static string Normalize(string? setting)
    {
        string text = (setting ?? "").Trim().ToLowerInvariant();
        return All.Any(language => language.Code == text) ? text : Auto;
    }

    public static UiLanguage ByCode(string code) => All.FirstOrDefault(language => language.Code == code) ?? English;

    /// <summary>The language for a setting; "auto" asks <paramref name="windowsPrimary"/> (a primary LANGID, its low ten bits).</summary>
    public static UiLanguage Resolve(string? setting, int windowsPrimary)
    {
        string normalized = Normalize(setting);
        if (normalized != Auto) return ByCode(normalized);
        return windowsPrimary switch
        {
            0x07 => German,
            0x19 => Russian,
            0x0A => Spanish,
            0x0C => French,
            0x01 => Arabic,
            0x1F => Turkish,
            _ => English,
        };
    }
}
