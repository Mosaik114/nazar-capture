namespace NazarCapture.Engine.Core;

/// <summary>A hotkey as the host registers it: <c>MOD_*</c> bits and a virtual-key code.</summary>
internal readonly record struct HotkeyChord(uint Modifiers, int Key)
{
    public const uint Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8;
}

/// <summary>
/// The text form of a hotkey (<c>Shift+Alt+4</c>, <c>Strg+Leertaste</c>, <c>Druck</c>), the same rules as the host's
/// <c>src/hotkey.rs</c>: tokens separated by <c>+</c>, case and spaces ignored, German and English names. A hotkey
/// needs Ctrl, Alt or Win; Shift alone is not enough (Shift+A would swallow every capital A). Print and Pause are the
/// exception: they type nothing, so they may stand alone, as the screenshot key Windows users know.
/// </summary>
internal static class HotkeyText
{
    /// <summary>Why a text is no hotkey (the host's ParseHotkeyError).</summary>
    internal enum Problem
    {
        None,
        Empty,
        UnknownKey,
        DuplicateModifier,
        MultipleKeys,
        MissingKey,
        MissingModifier,
        ShiftOnly,
    }

    const int VkSpace = 0x20, VkF1 = 0x70, VkSnapshot = 0x2C, VkPause = 0x13, VkInsert = 0x2D, VkNumpad0 = 0x60;
    const int VkMultiply = 0x6A, VkAdd = 0x6B, VkSubtract = 0x6D, VkDecimal = 0x6E, VkDivide = 0x6F;

    /// <summary>Key names of the canonical text and the virtual keys they stand for (beyond letters, digits and F keys).</summary>
    static readonly (string Name, int Key)[] Named =
    {
        ("Space", VkSpace),
        ("Print", VkSnapshot),
        ("Pause", VkPause),
        ("Insert", VkInsert),
        ("NumMultiply", VkMultiply),
        ("NumAdd", VkAdd),
        ("NumSubtract", VkSubtract),
        ("NumDecimal", VkDecimal),
        ("NumDivide", VkDivide),
    };

    /// <summary>Other spellings, among them the German key caps.</summary>
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Leertaste"] = "Space",
        ["Druck"] = "Print",
        ["PrintScreen"] = "Print",
        ["PrtSc"] = "Print",
        ["Snapshot"] = "Print",
        ["Einfg"] = "Insert",
        ["Ins"] = "Insert",
        ["Untbr"] = "Pause",
    };

    public static bool StandsAlone(int key) => key is VkSnapshot or VkPause;

    /// <summary>Parses <paramref name="text"/>; on failure <paramref name="problem"/> says why.</summary>
    public static bool TryParse(string? text, out HotkeyChord chord, out Problem problem)
    {
        chord = default;
        string compact = new((text ?? "").Where(c => c != ' ').ToArray());
        if (compact.Length == 0) { problem = Problem.Empty; return false; }
        uint modifiers = 0;
        int? key = null;
        foreach (string token in compact.Split('+'))
        {
            if (token.Length == 0 || token.Length > 24) { problem = token.Length == 0 ? Problem.Empty : Problem.UnknownKey; return false; }
            uint modifier = Modifier(token);
            if (modifier != 0)
            {
                if ((modifiers & modifier) != 0) { problem = Problem.DuplicateModifier; return false; }
                modifiers |= modifier;
            }
            else if (key != null) { problem = Problem.MultipleKeys; return false; }
            else
            {
                int parsed = Key(token);
                if (parsed == 0) { problem = Problem.UnknownKey; return false; }
                key = parsed;
            }
        }
        if (key == null) { problem = Problem.MissingKey; return false; }
        if (!StandsAlone(key.Value))
        {
            if (modifiers == 0) { problem = Problem.MissingModifier; return false; }
            if (modifiers == HotkeyChord.Shift) { problem = Problem.ShiftOnly; return false; }
        }
        chord = new HotkeyChord(modifiers, key.Value);
        problem = Problem.None;
        return true;
    }

    static uint Modifier(string token) => token.ToUpperInvariant() switch
    {
        "SHIFT" or "UMSCHALT" => HotkeyChord.Shift,
        "ALT" => HotkeyChord.Alt,
        "CTRL" or "CONTROL" or "STRG" => HotkeyChord.Control,
        "WIN" or "WINDOWS" => HotkeyChord.Win,
        _ => 0,
    };

    static int Key(string token)
    {
        if (token.Length == 1 && token[0] < 128 && char.IsLetterOrDigit(token[0])) return char.ToUpperInvariant(token[0]);
        if (Aliases.TryGetValue(token, out var alias)) token = alias;
        foreach (var (name, key) in Named)
            if (string.Equals(name, token, StringComparison.OrdinalIgnoreCase)) return key;
        if (token.Length >= 4 && token.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && token.Length == 4 && char.IsDigit(token[3])) return VkNumpad0 + (token[3] - '0');
        if ((token[0] == 'F' || token[0] == 'f') && token.Length > 1 && token.Skip(1).All(char.IsDigit)
            && int.TryParse(token.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= 24)
            return VkF1 + n - 1;
        return 0;
    }

    /// <summary>The canonical text: modifiers in the order Ctrl, Shift, Alt, Win, then the key (English names).</summary>
    public static string Format(HotkeyChord chord)
    {
        var parts = new List<string>(5);
        if ((chord.Modifiers & HotkeyChord.Control) != 0) parts.Add("Ctrl");
        if ((chord.Modifiers & HotkeyChord.Shift) != 0) parts.Add("Shift");
        if ((chord.Modifiers & HotkeyChord.Alt) != 0) parts.Add("Alt");
        if ((chord.Modifiers & HotkeyChord.Win) != 0) parts.Add("Win");
        parts.Add(KeyName(chord.Key));
        return string.Join("+", parts);
    }

    public static string KeyName(int key)
    {
        if (key is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)key).ToString();
        if (key is >= VkF1 and <= VkF1 + 23) return "F" + (key - VkF1 + 1).ToString(CultureInfo.InvariantCulture);
        if (key is >= VkNumpad0 and <= VkNumpad0 + 9) return "Num" + (key - VkNumpad0).ToString(CultureInfo.InvariantCulture);
        foreach (var (name, k) in Named)
            if (k == key) return name;
        return "";
    }

    /// <summary>Whether two texts mean the same hotkey (spelling and order do not matter).</summary>
    public static bool Same(string? a, string? b) =>
        TryParse(a, out var x, out _) && TryParse(b, out var y, out _) ? x == y : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The German explanation of a problem, for the setting row.</summary>
    public static string Explain(Problem problem) => problem switch
    {
        Problem.ShiftOnly => UiText.Hotkeys.ShiftOnly,
        Problem.MissingModifier => UiText.Hotkeys.MissingModifier,
        Problem.MissingKey => UiText.Hotkeys.MissingKey,
        Problem.None => "",
        _ => UiText.Hotkeys.Unsupported,
    };

    /// <summary>
    /// The key-cap labels of a hotkey as they stand on a German keyboard (Hams' key_labels): <c>Strg</c>,
    /// <c>Umschalt</c>, <c>Alt</c>, <c>Win</c>, <c>Leertaste</c>, <c>Druck</c> ... Unknown parts stay as written.
    /// </summary>
    public static IReadOnlyList<string> Labels(string? text)
    {
        var labels = new List<string>();
        foreach (string raw in (text ?? "").Split('+'))
        {
            string part = raw.Trim();
            if (part.Length == 0) continue;
            uint modifier = Modifier(part);
            if (modifier != 0) { labels.Add(ModifierLabel(modifier)); continue; }
            int key = Key(part.Replace(" ", ""));
            labels.Add(key == 0 ? part : KeyLabel(key));
        }
        // Modifiers first, in the canonical order; the key last.
        string[] order = { "Strg", "Umschalt", "Alt", "Win" };
        return labels.OrderBy(l => Array.IndexOf(order, l) is int i and >= 0 ? i : order.Length).ToList();
    }

    static string ModifierLabel(uint modifier) => modifier switch
    {
        HotkeyChord.Control => "Strg",
        HotkeyChord.Shift => "Umschalt",
        HotkeyChord.Alt => "Alt",
        _ => "Win",
    };

    public static string KeyLabel(int key) => key switch
    {
        VkSpace => "Leertaste",
        VkSnapshot => "Druck",
        VkPause => "Pause",
        VkInsert => "Einfg",
        VkMultiply => "Num *",
        VkAdd => "Num +",
        VkSubtract => "Num -",
        VkDecimal => "Num ,",
        VkDivide => "Num /",
        >= VkNumpad0 and <= VkNumpad0 + 9 => "Num " + (key - VkNumpad0).ToString(CultureInfo.InvariantCulture),
        _ => KeyName(key),
    };

    /// <summary>The labels joined for a screen reader or a tray menu: "Umschalt+Alt+4".</summary>
    public static string Spoken(string? text) => string.Join("+", Labels(text));
}

/// <summary>
/// Records a hotkey from key presses (Hams' hotkey_editor recorder): modifiers alone keep it open, Esc alone cancels,
/// the first other key completes it. Print arrives only as a key release in Windows, so the caller passes it then.
/// </summary>
internal sealed class HotkeyRecorder
{
    internal enum Outcome
    {
        Pending,
        Cancel,
        Unsupported,
        Done,
    }

    /// <summary>The modifiers held now (as <see cref="HotkeyChord"/> bits).</summary>
    public uint Held { get; private set; }

    /// <summary>The recorded text after <see cref="Outcome.Done"/>.</summary>
    public string Result { get; private set; } = "";

    public void Reset()
    {
        Held = 0;
        Result = "";
    }

    /// <summary>The held modifiers as a text for the key caps while recording (e.g. "Ctrl+Shift").</summary>
    public string HeldText()
    {
        var parts = new List<string>();
        if ((Held & HotkeyChord.Control) != 0) parts.Add("Ctrl");
        if ((Held & HotkeyChord.Shift) != 0) parts.Add("Shift");
        if ((Held & HotkeyChord.Alt) != 0) parts.Add("Alt");
        if ((Held & HotkeyChord.Win) != 0) parts.Add("Win");
        return string.Join("+", parts);
    }

    /// <summary>A key went down (<paramref name="key"/> already resolved from Key.System) with these modifiers.</summary>
    public Outcome Press(Key key, ModifierKeys modifiers)
    {
        Held = Bits(modifiers) | ModifierOf(key);
        if (ModifierOf(key) != 0) return Outcome.Pending;
        if (key == Key.Escape && Held == 0) return Outcome.Cancel;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (HotkeyText.KeyName(vk).Length == 0) return Outcome.Unsupported;
        Result = HotkeyText.Format(new HotkeyChord(Held, vk));
        return Outcome.Done;
    }

    /// <summary>A key went up: a released modifier is no longer held.</summary>
    public void Release(Key key, ModifierKeys modifiers) => Held = Bits(modifiers) & ~ModifierOf(key);

    static uint Bits(ModifierKeys modifiers) =>
        ((modifiers & ModifierKeys.Control) != 0 ? HotkeyChord.Control : 0)
        | ((modifiers & ModifierKeys.Shift) != 0 ? HotkeyChord.Shift : 0)
        | ((modifiers & ModifierKeys.Alt) != 0 ? HotkeyChord.Alt : 0)
        | ((modifiers & ModifierKeys.Windows) != 0 ? HotkeyChord.Win : 0);

    static uint ModifierOf(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => HotkeyChord.Control,
        Key.LeftShift or Key.RightShift => HotkeyChord.Shift,
        Key.LeftAlt or Key.RightAlt => HotkeyChord.Alt,
        Key.LWin or Key.RWin => HotkeyChord.Win,
        _ => 0,
    };
}
