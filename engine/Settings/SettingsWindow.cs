using Microsoft.Win32;

namespace NazarCapture.Engine.Settings;

/// <summary>
/// The settings window as Hams' (settings_window.py): the <see cref="Stage"/> (mark, title, painted banner, round close
/// stone), a <see cref="TabBar"/>, one page per tab in a <see cref="ScrollArea"/>, each page groups on paper plates with
/// <see cref="SettingRow"/>s. The pages come as data (<see cref="NazarSettings.Pages"/>); every page is built and laid
/// out ahead (<see cref="PageHost"/>), so a switch shows a finished page. The window is built once and then only shown
/// and hidden; its size can be dragged (minimum as Hams') and is remembered.
/// Every change applies at once through the host (<see cref="IHostChannel"/>, the host stays the only writer of
/// settings.ini); there is no save button. A problem shows in its own row. Hotkeys are recorded by pressing them; while
/// one is recorded, the host releases both so the old one does not fire. The window grows with "Text vergrößern".
/// </summary>
internal sealed class SettingsWindow : NazarWindow
{
    /// <summary>Size and smallest size (Hams' SETTINGS_WIDTH/HEIGHT and SETTINGS_MIN_WIDTH/HEIGHT).</summary>
    public const double BaseWidth = 440, BaseHeight = 680, MinimumWidth = 400, MinimumHeight = 420;
    /// <summary>Width of a choice beside its text (Hams' CONTROL_WIDTH) and of the buttons in rows.</summary>
    public const double ControlWidth = 160, RowButtonWidth = 96;
    const string PlaceKey = "settings";

    static SettingsWindow? shared;

    readonly IHostChannel host;
    readonly Func<CaptureSettingsSnapshot> load;
    readonly WindowPlaces? places;
    readonly IReadOnlyList<PageSpec> specs;
    readonly TabBar tabs;
    readonly ScrollArea scroll = new() { Margin = new Thickness(UiTokens.Space.Window, 0, ScrollArea.EdgeGap, UiTokens.Space.Window) };
    readonly PageHost pages = new();
    readonly List<SettingGroup> groups = new();
    readonly Dictionary<string, SettingRow> rows = new();
    readonly Dictionary<string, ToggleSwitch> switches = new();
    readonly Dictionary<string, Select> selects = new();
    readonly Dictionary<string, HotkeyBox> hotkeys = new();
    readonly List<(SettingRow Row, Func<CaptureSettingsSnapshot, string> Value)> folders = new();
    readonly DispatcherTimer sizeSaver;
    readonly Func<Updates.Release?> pending;
    readonly Func<Updates.UpdateCheck> updateCheck;
    UpdateRow? updates;
    readonly List<SwitchSpec> switchSpecs = new();
    readonly List<ChoiceSpec> choiceSpecs = new();
    CaptureSettingsSnapshot current = new();
    bool populating, retired;

    public SettingsWindow(IHostChannel host, Func<CaptureSettingsSnapshot> load, double textScale = 1, WindowPlaces? places = null, IReadOnlyList<PageSpec>? pageSpecs = null,
        Func<Updates.Release?>? pending = null, Func<Updates.UpdateCheck>? updateCheck = null)
        : base(UiText.Settings.Title, new ViewfinderMotif())
    {
        this.host = host;
        this.load = load;
        this.places = places;
        specs = pageSpecs ?? NazarSettings.Pages();
        this.pending = pending ?? Updates.UpdateNotice.Pending;
        this.updateCheck = updateCheck ?? (() => new Updates.UpdateCheck());
        MinWidth = MinimumWidth * textScale;
        MinHeight = MinimumHeight * textScale;
        Width = BaseWidth * textScale;
        Height = BaseHeight * textScale;
        if (places?.Read(PlaceKey) is { } size)
        {
            Width = Math.Max(MinWidth, size.A);
            Height = Math.Max(MinHeight, size.B);
        }
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;

        current = load();
        foreach (var spec in specs) pages.Add(BuildPage(spec));
        tabs = new TabBar(specs.Select(spec => spec.Title)) { Margin = new Thickness(UiTokens.Space.Window, 0, UiTokens.Space.Window, UiTokens.Space.Md - TabBar.Gap) };
        tabs.Chosen += ShowPage;
        scroll.Content = pages;
        var body = new DockPanel();
        DockPanel.SetDock(tabs, Dock.Top);
        body.Children.Add(tabs);
        body.Children.Add(scroll);
        Body = body;
        if (textScale > 1.01) ((FrameworkElement)Content).LayoutTransform = new ScaleTransform(textScale, textScale);

        sizeSaver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        sizeSaver.Tick += delegate { sizeSaver.Stop(); RememberSize(); };
        SizeChanged += delegate { if (IsVisible && places != null) { sizeSaver.Stop(); sizeSaver.Start(); } };

        Populate();
    }

    // ---------------------------------------------------------- what tests and the host see

    internal IReadOnlyList<SettingGroup> Groups => groups;
    internal TabBar Tabs => tabs;
    internal PageHost Pages => pages;
    internal ScrollArea Scroll => scroll;
    internal HotkeyBox ScreenshotHotkey => hotkeys[NazarSettings.ScreenshotHotkey];
    internal SettingRow ScreenshotHotkeyRow => rows[NazarSettings.ScreenshotHotkey];
    internal SettingRow ScreenshotFolderRow => rows[NazarSettings.ScreenshotFolder];
    internal ToggleSwitch Microphone => switches["microphone"];
    internal Select Appearance => selects[NazarSettings.Appearance];
    internal UpdateRow? UpdateRow => updates;
    internal SettingRow Row(string key) => rows[key];

    /// <summary>Opens (or brings back) the one settings window of the engine; <paramref name="recording"/> shows the recording page.</summary>
    public static void ShowShared(bool recording)
    {
        shared ??= new SettingsWindow(new HostChannel(), CaptureSettings.Load, TextScaleFactor(), WindowPlaces.User());
        shared.Open(recording);
    }

    /// <summary>"Text vergrößern" in Windows (100..225 %), which WPF on .NET Framework does not apply by itself.</summary>
    internal static double TextScaleFactor()
    {
        try
        {
            object? value = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Accessibility", "TextScaleFactor", 100);
            return value is int percent && percent is >= 100 and <= 225 ? percent / 100.0 : 1;
        }
        catch (System.Security.SecurityException) { return 1; }
        catch (IOException) { return 1; }
    }

    public void Open(bool recording)
    {
        Populate();
        Palette.Apply(Resources);
        if (recording) SelectPage(NazarSettings.RecordingPage);
        if (!IsVisible)
        {
            RECT work = NativeMethods.WorkAreaAtCursor();
            double scale = NativeMethods.ScaleOfMonitor(work);
            MaxHeight = (work.bottom - work.top) / scale * 0.92;
            Height = Math.Min(Height, MaxHeight);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = (work.left + (work.right - work.left) / 2.0) / scale - Width / 2;
            Top = (work.top / scale) + 40;
            scroll.ScrollTo(0);
            Show();
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        ToolWindowBase.TakeFocus(this, new WindowInteropHelper(this).Handle);
        tabs.Buttons[tabs.Selected].Focus();
    }

    /// <summary>Shows a page as if its tab had been chosen.</summary>
    internal void SelectPage(int index)
    {
        tabs.Show(index);
        ShowPage(index);
    }

    void ShowPage(int index)
    {
        foreach (var box in hotkeys.Values) box.CancelCapture();
        pages.Selected = index;
        scroll.ScrollTo(0);
    }

    protected override void RequestClose()
    {
        foreach (var box in hotkeys.Values) box.CancelCapture();
        if (sizeSaver.IsEnabled) { sizeSaver.Stop(); RememberSize(); }
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (retired) return;
        e.Cancel = true;   // kept for the next time
        RequestClose();
    }

    /// <summary>Closes for good (the language changed: a new window takes its place).</summary>
    public void Retire()
    {
        foreach (var box in hotkeys.Values) box.CancelCapture();
        retired = true;
        Close();
    }

    /// <summary>
    /// The language changed: the shared window is built anew in it, and shown where and as large as the old one, on the
    /// same page (the old one closes). Pages are built from the texts once, so this is the way to a new language.
    /// </summary>
    internal static void Relocalize()
    {
        if (shared == null) return;
        var old = shared;
        shared = new SettingsWindow(new HostChannel(), CaptureSettings.Load, TextScaleFactor(), WindowPlaces.User());
        shared.TakePlaceOf(old);
    }

    /// <summary>Shows this window where <paramref name="old"/> is (if it is shown), on its page, and retires it.</summary>
    internal void TakePlaceOf(SettingsWindow old)
    {
        int page = old.tabs.Selected;
        SelectPage(page);
        if (old.IsVisible)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = old.Left;
            Top = old.Top;
            Width = old.ActualWidth > 0 ? old.ActualWidth : old.Width;
            Height = old.ActualHeight > 0 ? old.ActualHeight : old.Height;
            MaxHeight = old.MaxHeight;
            Show();
            ToolWindowBase.TakeFocus(this, new WindowInteropHelper(this).Handle);
            tabs.Buttons[tabs.Selected].Focus();
        }
        old.Retire();
    }

    void RememberSize()
    {
        if (places == null || WindowState != WindowState.Normal || ActualWidth < 1) return;
        places.Write(PlaceKey, ActualWidth, ActualHeight);
    }

    // ---------------------------------------------------------- building from the page data

    FrameworkElement BuildPage(PageSpec spec)
    {
        // On the right the groups end one scroll gutter earlier, so both window margins are equally wide (Hams' group()).
        var page = new StackPanel { Margin = new Thickness(0, 0, UiTokens.Space.Window - ScrollArea.GutterWidth - ScrollArea.EdgeGap, 0) };
        System.Windows.Automation.AutomationProperties.SetName(page, spec.Title);
        bool first = true;
        foreach (var groupSpec in spec.Groups)
        {
            var group = new SettingGroup(first ? null : groupSpec.Heading);
            System.Windows.Automation.AutomationProperties.SetName(group, groupSpec.Heading ?? spec.Title);
            if (!first) group.Margin = new Thickness(0, UiTokens.Space.SectionGap, 0, 0);
            foreach (var row in groupSpec.Rows) group.Add(BuildRow(row, group));
            groups.Add(group);
            page.Children.Add(group);
            first = false;
        }
        return page;
    }

    SettingRow BuildRow(RowSpec spec, SettingGroup group)
    {
        switch (spec)
        {
            case SwitchSpec s:
                {
                    var toggle = new ToggleSwitch(s.Title);
                    var row = new SettingRow(s.Title, s.Hint, toggle);
                    switches[s.Key] = toggle;
                    rows[s.Key] = row;
                    switchSpecs.Add(s);
                    toggle.Checked += delegate { Change(toggle, s.Key, "1"); };
                    toggle.Unchecked += delegate { Change(toggle, s.Key, "0"); };
                    return row;
                }
            case ChoiceSpec c:
                {
                    var select = new Select(c.Title);
                    for (int i = 0; i < c.Labels.Length; i++) select.Add(c.Labels[i], c.Values[i]);
                    if (!c.Stacked) select.Width = ControlWidth;
                    var row = new SettingRow(c.Title, c.Hint, select, stacked: c.Stacked);
                    selects[c.Key] = select;
                    rows[c.Key] = row;
                    choiceSpecs.Add(c);
                    select.SelectionChanged += delegate { Chosen(select, c.Key); };
                    return row;
                }
            case HotkeySpec h:
                {
                    var box = new HotkeyBox(h.Title + " " + PageOf(h), h.Value(current), h.Default,
                        new HostHotkeyChecker(host, h.Key, h.OtherName, () => hotkeys.TryGetValue(h.OtherKey, out var other) ? other.Combo : ""));
                    var row = new SettingRow(h.Title, h.Hint, box, stacked: true);
                    hotkeys[h.Key] = box;
                    rows[h.Key] = row;
                    Hook(box, row, h.Key, h.Value);
                    return row;
                }
            case FolderSpec f:
                {
                    var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                    var row = new SettingRow(f.Title, UiText.Path(f.Value(current)), buttons, stacked: true);
                    buttons.Children.Add(new SecondaryButton(UiText.Settings.Change, () => ChangeFolder(row, f.Key, f.Value(current))) { MinWidth = RowButtonWidth, Margin = new Thickness(0, 0, UiTokens.Space.Md, 0) });
                    buttons.Children.Add(new SecondaryButton(UiText.Settings.Open, () => OpenFolder(row, f.Value(current)), IconGlyphs.Folder) { MinWidth = RowButtonWidth });
                    rows[f.Key] = row;
                    folders.Add((row, f.Value));
                    return row;
                }
            case LinkSpec l:
                return new SettingRow(l.Title, l.Hint, new SecondaryButton(l.Button, () => Shell.Open(l.Url)) { MinWidth = RowButtonWidth });
            case DisplaySpec d:
                {
                    var row = SettingRow.Display(d.Title, d.Value);
                    if (row.Control is FrameworkElement value) Localizer.KeepLeftToRight(value);   // "Nazar Capture 1.1.6" in that order, also in Arabic
                    return row;
                }
            case UpdatesSpec:
                updates = new UpdateRow(updateCheck, Updates.UpdateNotice.Show);
                return updates.Row;
            default:
                throw new ArgumentException("Unknown row: " + spec.GetType().Name, nameof(spec));
        }
    }

    string PageOf(RowSpec spec) => specs.First(page => page.Groups.Any(g => g.Rows.Contains(spec))).Title;

    // ---------------------------------------------------------- state

    /// <summary>Shows the settings as stored now, and which hotkeys the host could register.</summary>
    internal void Populate()
    {
        current = load();
        populating = true;
        try
        {
            foreach (var page in specs)
                foreach (var group in page.Groups)
                    foreach (var spec in group.Rows)
                        switch (spec)
                        {
                            case SwitchSpec s: switches[s.Key].IsChecked = s.Value(current); break;
                            case ChoiceSpec c: selects[c.Key].SelectedValue = c.Value(current); break;
                            case HotkeySpec h: hotkeys[h.Key].Show(h.Value(current)); break;
                        }
            foreach (var (row, value) in folders) row.SetDescription(UiText.Path(value(current)));
        }
        finally { populating = false; }
        ShowDependents();
        ShowHotkeyState();
        ShowUpdate(pending());
    }

    /// <summary>A newer version (as remembered by the daily check) shows in the update row with "Jetzt aktualisieren".</summary>
    internal void ShowUpdate(Updates.Release? release) => updates?.Show(release);

    /// <summary>
    /// What follows from a switch: its help text while it is off ("Screenshots speichern" off: only to the clipboard), and
    /// the choices that mean nothing without it ("Aufbewahren"), greyed out.
    /// </summary>
    void ShowDependents()
    {
        foreach (var s in switchSpecs.Where(s => s.OffHint != null))
            rows[s.Key].SetDescription(switches[s.Key].IsChecked == true ? s.Hint ?? "" : s.OffHint!);
        foreach (var c in choiceSpecs.Where(c => c.EnabledBy != null))
            selects[c.Key].IsEnabled = switches.TryGetValue(c.EnabledBy!, out var toggle) && toggle.IsChecked == true;
    }

    void ShowHotkeyState()
    {
        var state = host.Probe();
        if (rows.TryGetValue(NazarSettings.ScreenshotHotkey, out var shot)) ShowHint(shot, ScreenshotHotkey.Combo, state is { Screenshot: false });
        if (rows.TryGetValue(NazarSettings.RecordingHotkey, out var take)) ShowHint(take, hotkeys[NazarSettings.RecordingHotkey].Combo, state is { Recording: false });
    }

    static void ShowHint(SettingRow row, string combo, bool missing)
    {
        if (missing) { row.ShowStatus(UiText.Hotkeys.TakenRetrying, Status.Warning); return; }
        bool printAlone = HotkeyText.TryParse(combo, out var chord, out _) && chord.Key == 0x2C && (chord.Modifiers & ~HotkeyChord.Shift) == 0;
        row.ShowStatus(printAlone ? UiText.Hotkeys.PrintHint : "", Status.None);
    }

    // ---------------------------------------------------------- changes

    void Hook(HotkeyBox box, SettingRow row, string key, Func<CaptureSettingsSnapshot, string> value)
    {
        box.Message += (text, status) => row.ShowStatus(text, status);
        box.CaptureChanged += capturing =>
        {
            CloseOnEscape = !capturing;   // Esc cancels the recording first
            host.SuspendHotkeys(capturing);
            if (!capturing) ShowHint(row, box.Combo, false);
        };
        box.Applied += combo =>
        {
            string before = value(current);
            var reply = host.Set(key, combo);
            if (reply == HostReply.Ok) { current = load(); ShowHint(row, combo, false); return; }
            box.Refuse(before, Explain(reply));
        };
    }

    void Change(ToggleSwitch toggle, string key, string value)
    {
        if (populating) return;
        var row = (SettingRow)toggle.Parent;
        var reply = host.Set(key, value);
        if (reply == HostReply.Ok) { row.ShowStatus("", Status.None); current = load(); ShowDependents(); return; }
        populating = true;
        toggle.IsChecked = value != "1";   // back to what is stored
        populating = false;
        row.ShowStatus(Explain(reply), Status.Error);
    }

    void Chosen(Select select, string key)
    {
        if (populating || select.SelectedValue == null) return;
        var row = (SettingRow)select.Parent;
        var reply = host.Set(key, Convert.ToString(select.SelectedValue, CultureInfo.InvariantCulture)!);
        if (reply == HostReply.Ok)
        {
            row.ShowStatus("", Status.None);
            if (key == NazarSettings.Appearance)
            {
                // works at once in every open window, and in every window opened from now on
                CaptureSettings.UseAppearance(CaptureSettings.NormalizeAppearance((int)select.SelectedValue));
                Palette.ApplyEverywhere();
            }
            current = load();
            if (key == NazarSettings.Language)
            {
                // at once: this window and the main window are built anew in the new language (after this event), every
                // other window the next time it is needed
                Localizer.Use((string)select.SelectedValue);
                Dispatcher.BeginInvoke(new Action(LanguageChanged), DispatcherPriority.Background);
            }
            return;
        }
        row.ShowStatus(Explain(reply), Status.Error);
        Populate();
    }

    /// <summary>Called after the language changed (tests replace it; the real one rebuilds the shared windows).</summary>
    internal Action LanguageChanged { get; set; } = () =>
    {
        Relocalize();
        MainWindow.Relocalize();
    };

    void ChangeFolder(SettingRow row, string key, string before) =>
        ChooseFolder(row, key, FolderPicker.Pick(this, before));

    /// <summary>A folder was picked (or not, null): the host checks and keeps it (tests call this directly).</summary>
    internal void ChooseFolder(SettingRow row, string key, string? picked)
    {
        if (picked == null) return;
        var reply = host.Set(key, picked);
        if (reply == HostReply.Ok) { current = load(); row.SetDescription(UiText.Path(picked)); return; }
        row.ShowStatus(reply == HostReply.FolderUnusable ? UiText.Settings.FolderUnusable : Explain(reply), Status.Error);
    }

    /// <summary>Opens the folder the row shows (the stored one, the same the row's text names).</summary>
    internal static void OpenFolder(SettingRow row, string folder)
    {
        if (!Directory.Exists(folder)) { row.ShowStatus(UiText.Settings.FolderMissing, Status.Error); return; }
        Shell.Open("explorer.exe", "\"" + folder + "\"");
    }

    internal static string Explain(HostReply reply) => reply switch
    {
        HostReply.Taken => UiText.Hotkeys.Taken,
        HostReply.Invalid => UiText.Hotkeys.Unsupported,
        HostReply.SaveFailed => UiText.Settings.SaveFailed,
        HostReply.FolderUnusable => UiText.Settings.FolderUnusable,
        HostReply.AutostartFailed => UiText.Settings.AutostartFailed,
        HostReply.Duplicate => UiText.Hotkeys.Unsupported,
        _ => UiText.Settings.NoHost,
    };
}

/// <summary>
/// The hotkey check of the settings window: the rules of <see cref="HotkeyText"/> and the other action's keys here, then
/// the host, which alone knows whether another program holds the keys.
/// </summary>
internal sealed class HostHotkeyChecker : IHotkeyChecker
{
    readonly IHostChannel host;
    readonly string key, otherName;
    readonly Func<string> other;

    public HostHotkeyChecker(IHostChannel host, string key, string otherName, Func<string> other)
    {
        this.host = host;
        this.key = key;
        this.otherName = otherName;
        this.other = other;
    }

    public HotkeyVerdict Check(string combo)
    {
        var local = new StructuralHotkeyChecker(() => new[] { (otherName, other()) }).Check(combo);
        if (!local.Ok) return local;
        return host.CheckHotkey(key, local.Combo) switch
        {
            HostReply.Taken => HotkeyVerdict.Bad(UiText.Hotkeys.Taken),
            HostReply.Duplicate => HotkeyVerdict.Bad(UiText.Hotkeys.Duplicate(otherName)),
            HostReply.Invalid => HotkeyVerdict.Bad(UiText.Hotkeys.Unsupported),
            _ => local,   // free, or no host to ask: setting it will tell
        };
    }
}

/// <summary>Name and version of the app as shown to the user.</summary>
internal static class AppInfo
{
    /// <summary>Major.minor.patch of this build.</summary>
    public static string Version
    {
        get
        {
            var version = typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return version.ToString(3);
        }
    }
}
