using System.Text.RegularExpressions;

namespace NazarCapture.Engine.Core.Localization;

/// <summary>
/// The texts of one language, keyed by a stable English identifier ("settings.title"). Placeholders are
/// <c>{0}</c>, <c>{1}</c> ... (<see cref="string.Format(IFormatProvider, string, object[])"/>). Every catalog has the same
/// keys and, per key, the same placeholders as the German one (a test checks that).
/// </summary>
internal sealed class TextCatalog
{
    static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.CultureInvariant);

    readonly IReadOnlyDictionary<string, string> texts;

    public TextCatalog(UiLanguage language, IReadOnlyDictionary<string, string> texts)
    {
        Language = language;
        this.texts = texts;
    }

    public UiLanguage Language { get; }

    public IEnumerable<string> Keys => texts.Keys;

    public bool Has(string key) => texts.ContainsKey(key);

    /// <summary>The text, or null if this catalog lacks the key.</summary>
    public string? Find(string key) => texts.TryGetValue(key, out var text) ? text : null;

    /// <summary>The placeholders a text uses, sorted ("{0}", "{1}").</summary>
    public static IReadOnlyList<string> PlaceholdersOf(string text) =>
        Placeholder.Matches(text).Cast<Match>().Select(m => m.Value).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
}
