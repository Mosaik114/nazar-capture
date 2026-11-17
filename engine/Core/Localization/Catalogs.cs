namespace NazarCapture.Engine.Core.Localization;

/// <summary>The catalogs of every interface language (one file each: Catalog.De.cs, Catalog.En.cs ...).</summary>
internal static partial class Catalogs
{
    /// <summary>Every catalog, in the order of <see cref="UiLanguages.All"/>.</summary>
    public static IReadOnlyList<TextCatalog> All => new[] { German, English, Russian, Spanish, French, Arabic, Turkish };

    public static TextCatalog Of(UiLanguage language) => All.FirstOrDefault(catalog => catalog.Language.Code == language.Code) ?? English;
}
