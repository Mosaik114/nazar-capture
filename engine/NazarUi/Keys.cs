namespace NazarCapture.Engine.NazarUi;

/// <summary>
/// A hotkey as a row of small key caps (Hams' KeyCombo): a display, not a control. Key caps read like the keyboard
/// itself; a raw value like "Shift+Alt+4" needs translating in the head.
/// </summary>
internal sealed class KeyCombo : StackPanel
{
    string combo = "";

    public KeyCombo(string combo = "")
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        SetCombo(combo);
    }

    public string Combo => combo;

    /// <summary>Shows the caps of <paramref name="value"/>; <paramref name="placeholder"/> (or "nicht belegt") when it is empty.</summary>
    public void SetCombo(string value, string? placeholder = null)
    {
        combo = value ?? "";
        Children.Clear();
        var labels = HotkeyText.Labels(combo);
        if (labels.Count == 0)
        {
            var empty = TextStyle.Text(placeholder ?? UiText.Hotkeys.Unset, TextRole.Description, UiTokens.Keys.TextSecondary);
            empty.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(empty);
        }
        foreach (string label in labels)
            Children.Add(Cap(label, Children.Count > 0));
        System.Windows.Automation.AutomationProperties.SetName(this, labels.Count == 0 ? placeholder ?? UiText.Hotkeys.Unset : HotkeyText.Spoken(combo));
    }

    /// <summary>One key cap: a small stone (radius 6, depth 1, 22 high) with the label in the shortcut role.</summary>
    public static StoneChrome Cap(string label, bool gap = false)
    {
        var cap = new StoneChrome
        {
            Depth = UiTokens.Depth.Keycap,
            Radius = UiTokens.Radius.Keycap,
            MinWidth = UiTokens.Height.Keycap,
            Padding = new Thickness(7, 0, 7, 0),
            Margin = new Thickness(gap ? UiTokens.Space.KeyGap : 0, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        cap.SetResourceReference(StoneChrome.FillProperty, UiTokens.Keys.ControlSurface);
        cap.SetResourceReference(StoneChrome.SideProperty, UiTokens.Keys.ControlSide);
        cap.SetResourceReference(StoneChrome.InkProperty, UiTokens.Keys.Ink);
        var text = TextStyle.Text(label, TextRole.Shortcut);
        text.FontSize = 12.5;
        text.Height = UiTokens.Height.Keycap;
        text.Padding = new Thickness(0, 2.5, 0, 0);
        text.TextAlignment = TextAlignment.Center;
        cap.Child = text;
        return cap;
    }
}

/// <summary>Answer of a hotkey check: usable, or why not (shown in the row's description).</summary>
internal readonly record struct HotkeyVerdict(bool Ok, string Combo, string Problem)
{
    public static HotkeyVerdict Good(string combo) => new(true, combo, "");

    public static HotkeyVerdict Bad(string problem) => new(false, "", problem);
}

/// <summary>
/// Checks a recorded hotkey for one action. The structural rules live in <see cref="HotkeyText"/>; whether another
/// program holds the keys only the host knows (phase 6 answers over IPC).
/// </summary>
internal interface IHotkeyChecker
{
    HotkeyVerdict Check(string combo);
}

/// <summary>The structural check only: parses and refuses a combination another action of this app already uses.</summary>
internal sealed class StructuralHotkeyChecker : IHotkeyChecker
{
    readonly Func<IEnumerable<(string Name, string Combo)>> others;

    public StructuralHotkeyChecker(Func<IEnumerable<(string Name, string Combo)>> others) => this.others = others;

    public HotkeyVerdict Check(string combo)
    {
        if (!HotkeyText.TryParse(combo, out var chord, out var problem)) return HotkeyVerdict.Bad(HotkeyText.Explain(problem));
        foreach (var (name, other) in others())
            if (HotkeyText.Same(other, combo)) return HotkeyVerdict.Bad(string.Format(CultureInfo.CurrentCulture, UiText.Hotkeys.Duplicate, name));
        return HotkeyVerdict.Good(HotkeyText.Format(chord));
    }
}

/// <summary>
/// A hotkey field like Hams' shortcut rows: key caps, then "Ändern" (and while recording "Standard" and a cross). While
/// recording the field holds the keyboard focus and takes every key itself (Esc cancels instead of closing the
/// window); the check runs at once, its answer goes to the row's description via <see cref="Message"/>. A rejected
/// combination keeps the old one and the recording open.
/// </summary>
internal sealed class HotkeyBox : DockPanel
{
    readonly KeyCombo keys = new();
    readonly SecondaryButton change, reset;
    readonly IconButton cancel;
    readonly HotkeyRecorder recorder = new();
    readonly IHotkeyChecker checker;
    readonly string defaultCombo;
    string combo;

    public HotkeyBox(string name, string current, string defaultCombo, IHotkeyChecker checker)
    {
        this.checker = checker;
        this.defaultCombo = defaultCombo;
        combo = current;
        Focusable = true;
        FocusVisualStyle = null;
        LastChildFill = false;
        System.Windows.Automation.AutomationProperties.SetName(this, name);

        change = new SecondaryButton(UiText.Hotkeys.Change, StartCapture) { MinWidth = ChangeWidth };
        reset = new SecondaryButton(UiText.Hotkeys.Default, () => Try(defaultCombo)) { MinWidth = DefaultWidth, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, UiTokens.Space.Xs, 0) };
        cancel = IconButton.Stone(LineIcon.Shape.Close, UiText.Hotkeys.Cancel, CancelCapture);
        cancel.Visibility = Visibility.Collapsed;
        foreach (var button in new FrameworkElement[] { change, cancel, reset })
        {
            SetDock(button, Dock.Right);
            Children.Add(button);
        }
        keys.Margin = new Thickness(0, 0, UiTokens.Space.ControlGap, 0);
        SetDock(keys, Dock.Left);
        Children.Add(keys);
        keys.SetCombo(combo);

        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        LostKeyboardFocus += (_, e) =>
        {
            if (Capturing && !(e.NewFocus is DependencyObject next && IsAncestorOf(next))) CancelCapture();
        };
    }

    /// <summary>Widths of "Ändern" and "Standard" (Hams' CHANGE_WIDTH and DEFAULT_WIDTH).</summary>
    public const double ChangeWidth = 96, DefaultWidth = 108;

    /// <summary>The keys held right now, or the current hotkey while none is held.</summary>
    void ShowHeld()
    {
        string held = recorder.HeldText();
        keys.SetCombo(held.Length > 0 ? held : combo);
    }

    /// <summary>The hotkey shown and applied.</summary>
    public string Combo => combo;

    public bool Capturing { get; private set; }

    /// <summary>A text for the row's description and its status (recording hint, saved, the reason of a refusal).</summary>
    public event Action<string, Status>? Message;

    /// <summary>A checked combination was taken; the owner saves it (and may answer with <see cref="Refuse"/>).</summary>
    public event Action<string>? Applied;

    /// <summary>Recording started or ended: the owner suspends and restores the global hotkeys meanwhile.</summary>
    public event Action<bool>? CaptureChanged;

    public void StartCapture()
    {
        if (Capturing) return;
        Capturing = true;
        recorder.Reset();
        change.Visibility = Visibility.Collapsed;
        cancel.Visibility = Visibility.Visible;
        reset.Visibility = HotkeyText.Same(combo, defaultCombo) ? Visibility.Collapsed : Visibility.Visible;
        keys.SetCombo(combo);   // as Hams: the old keys stay until new ones are held
        Message?.Invoke(UiText.Hotkeys.Capture, Status.Processing);
        Focus();
        CaptureChanged?.Invoke(true);
    }

    public void CancelCapture()
    {
        if (!Capturing) return;
        Finish();
        Message?.Invoke("", Status.None);
    }

    /// <summary>The owner could not apply the combination (e.g. the host found it taken): the old one stays.</summary>
    public void Refuse(string previous, string problem)
    {
        combo = previous;
        keys.SetCombo(combo);
        Message?.Invoke(problem, Status.Error);
    }

    /// <summary>Shows another hotkey without recording (the settings changed elsewhere).</summary>
    public void Show(string value)
    {
        combo = value;
        if (!Capturing) keys.SetCombo(combo);
    }

    void Finish()
    {
        Capturing = false;
        recorder.Reset();
        cancel.Visibility = Visibility.Collapsed;
        reset.Visibility = Visibility.Collapsed;
        change.Visibility = Visibility.Visible;
        keys.SetCombo(combo);
        CaptureChanged?.Invoke(false);
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!Capturing) return;
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Snapshot) return;   // Print comes as a release only (Windows keeps its press)
        Handle(recorder.Press(key, Keyboard.Modifiers));
    }

    void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (!Capturing) return;
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Snapshot) { Handle(recorder.Press(key, Keyboard.Modifiers)); return; }
        recorder.Release(key, Keyboard.Modifiers);
        ShowHeld();
    }

    /// <summary>Feeds one recorder outcome (also used by tests with a recorder of their own).</summary>
    internal void Handle(HotkeyRecorder.Outcome outcome)
    {
        switch (outcome)
        {
            case HotkeyRecorder.Outcome.Cancel:
                CancelCapture();
                break;
            case HotkeyRecorder.Outcome.Pending:
                ShowHeld();
                break;
            case HotkeyRecorder.Outcome.Unsupported:
                Message?.Invoke(UiText.Hotkeys.Unsupported, Status.Warning);
                break;
            default:
                Try(recorder.Result);
                break;
        }
    }

    /// <summary>Checks <paramref name="candidate"/>: a good one applies at once, otherwise the recording stays open.</summary>
    internal void Try(string candidate)
    {
        var verdict = checker.Check(candidate);
        if (!verdict.Ok)
        {
            recorder.Reset();
            keys.SetCombo(combo);   // the old keys again, the recording stays open
            Message?.Invoke(verdict.Problem, Status.Error);
            return;
        }
        combo = verdict.Combo;
        if (Capturing) Finish();
        else keys.SetCombo(combo);
        Message?.Invoke(UiText.Hotkeys.Saved, Status.Ready);
        Applied?.Invoke(combo);
    }
}
