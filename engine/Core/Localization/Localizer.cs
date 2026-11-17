using System.Windows.Markup;

namespace NazarCapture.Engine.Core.Localization;

/// <summary>
/// The interface language of the engine: which <see cref="TextCatalog"/> <see cref="UiText"/> reads. German until the
/// settings say otherwise (the engine reads them at start, <see cref="Use(string)"/>); changed in the settings, it applies at once:
/// <see cref="Changed"/> lets the settings and the main window rebuild themselves, and every other window is built anew
/// the next time it is needed (<see cref="PerLanguage{T}"/>). A key a catalog lacks falls back to German (never shown
/// in practice: the catalog test forbids it).
/// </summary>
internal static class Localizer
{
    static TextCatalog current = Catalogs.German;
    static int generation;

    public static TextCatalog Current => Volatile.Read(ref current);

    public static UiLanguage Language => Current.Language;

    /// <summary>Counts the changes of the language; a window built for an older one is rebuilt.</summary>
    public static int Generation => Volatile.Read(ref generation);

    /// <summary>The language changed (on the thread that changed it).</summary>
    public static event Action? Changed;

    /// <summary>Arabic is written from right to left; windows and their text follow.</summary>
    public static FlowDirection FlowDirection => Language.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Formats numbers in the placeholders like the interface language does.</summary>
    public static CultureInfo Culture => Language.Culture;

    public static string Text(string key) => Current.Find(key) ?? Catalogs.German.Find(key) ?? key;

    public static string Format(string key, params object[] args) => string.Format(Culture, Text(key), args);

    /// <summary>Uses the language of <paramref name="setting"/> ("auto" follows Windows) from now on.</summary>
    public static void Use(string? setting) => Use(UiLanguages.Resolve(setting, NativeMethods.WindowsUiLanguage()));

    public static void Use(UiLanguage language)
    {
        var catalog = Catalogs.Of(language);
        if (ReferenceEquals(Interlocked.Exchange(ref current, catalog), catalog)) return;
        Interlocked.Increment(ref generation);
        Changed?.Invoke();
    }

    /// <summary>Gives a window (or another root element) the writing direction and language of the interface.</summary>
    public static void Apply(FrameworkElement element)
    {
        element.FlowDirection = FlowDirection;
        element.Language = XmlLanguage.GetLanguage(Language.Culture.IetfLanguageTag);
    }

    /// <summary>Keeps an element left to right in every language (images, the screen, a timeline, numbers).</summary>
    public static void KeepLeftToRight(FrameworkElement element) => element.FlowDirection = FlowDirection.LeftToRight;
}

/// <summary>
/// A window that is built for the language in use: <see cref="Value"/> builds it on first use and builds it anew when the
/// language changed meanwhile and the old one is hidden (a shown one stays until it is hidden again).
/// </summary>
internal sealed class PerLanguage<T> where T : Window
{
    readonly Func<T> create;
    T? window;
    int generation;

    public PerLanguage(Func<T> create) => this.create = create;

    /// <summary>The window if it was built (and not retired); never builds one.</summary>
    public T? Existing => window;

    public T Value
    {
        get
        {
            if (window != null && generation != Localizer.Generation && !window.IsVisible)
            {
                Retire(window);
                window = null;
            }
            if (window == null)
            {
                window = create();
                generation = Localizer.Generation;
            }
            return window;
        }
    }

    static void Retire(T window)
    {
        try
        {
            if (window is IRetirable retirable) retirable.Retire();
            else window.Close();
        }
        catch (InvalidOperationException ex) { ErrorLog.Write("Closing a window of the previous language", ex); }
    }
}

/// <summary>A window that is normally kept when it is closed, and must really close when its language is gone.</summary>
internal interface IRetirable
{
    void Retire();
}
