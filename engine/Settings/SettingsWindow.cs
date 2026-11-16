using Microsoft.Win32;

namespace MosaikCapture.Engine.Settings;

/// <summary>
/// The settings window in the style of Hams: a <see cref="MosaikWindow"/> with three setting groups (Screenshot,
/// Aufnahme, Allgemein). Every change applies at once through the host (<see cref="IHostChannel"/>, the host stays the
/// only writer of settings.ini); there is no save button. A problem shows in its own row (status line in the error
/// colour), never in a shared status bar. Hotkeys are recorded by pressing them; while one is recorded, the host
/// releases both so the old one does not fire. The window grows with "Text vergrößern" (TextScaleFactor).
/// </summary>
internal sealed class SettingsWindow : MosaikWindow
{
    public const double BaseWidth = 780;
    const string ScreenshotHotkeyKey = "screenshot_hotkey", RecordingHotkeyKey = "recording_hotkey";

    static SettingsWindow? shared;

    readonly IHostChannel host;
    readonly Func<CaptureSettingsSnapshot> load;
    readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    readonly StackPanel page = new() { Margin = new Thickness(UiTokens.Space.Window, 0, UiTokens.Space.Window, UiTokens.Space.Window) };
    readonly SettingGroup screenshotGroup, recordingGroup, generalGroup;
    readonly HotkeyBox screenshotHotkey, recordingHotkey;
    readonly SettingRow screenshotHotkeyRow, recordingHotkeyRow, screenshotFolderRow, recordingFolderRow;
    readonly Select fps, countdown, appearance;
    readonly ToggleSwitch microphone, systemAudio, clicks, autostart, checkUpdates;
    readonly SettingRow versionRow;
    CaptureSettingsSnapshot current = new();
    bool populating;

    public SettingsWindow(IHostChannel host, Func<CaptureSettingsSnapshot> load, double textScale = 1) : base(UiText.Settings.Title)
    {
        this.host = host;
        this.load = load;
        Width = BaseWidth * textScale;
        SizeToContent = SizeToContent.Height;
        MinWidth = 520;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;

        HeadActions.Children.Add(new PrimaryButton(UiText.Settings.TakeScreenshot, () => { Hide(); Screenshots.StartSoon(); }, IconGlyphs.Edit) { Margin = new Thickness(0, 0, UiTokens.Space.Md, 0) });
        HeadActions.Children.Add(new SecondaryButton(UiText.Settings.StartRecording, () => { Hide(); ScreenRecorder.ToggleSoon(); }, IconGlyphs.Video, MuiRole.Recording) { Margin = new Thickness(0, 0, UiTokens.Space.Md, 0) });

        // Screenshot
        screenshotHotkey = new HotkeyBox(UiText.Settings.Hotkey + " " + UiText.Settings.ScreenshotGroup, CaptureSettingsSnapshot.DefaultScreenshotHotkey,
            CaptureSettingsSnapshot.DefaultScreenshotHotkey, new HostHotkeyChecker(host, ScreenshotHotkeyKey, UiText.Settings.Recording, () => recordingHotkey!.Combo));
        screenshotHotkeyRow = new SettingRow(UiText.Settings.Hotkey, UiText.Settings.ScreenshotHotkeyHint, screenshotHotkey, stacked: false);
        screenshotFolderRow = FolderRow("screenshot_folder", () => current.ScreenshotFolder);
        screenshotGroup = new SettingGroup(UiText.Settings.ScreenshotGroup);
        screenshotGroup.Add(screenshotHotkeyRow);
        screenshotGroup.Add(screenshotFolderRow);

        // Aufnahme
        recordingHotkey = new HotkeyBox(UiText.Settings.Hotkey + " " + UiText.Settings.RecordingGroup, CaptureSettingsSnapshot.DefaultRecordingHotkey,
            CaptureSettingsSnapshot.DefaultRecordingHotkey, new HostHotkeyChecker(host, RecordingHotkeyKey, UiText.Settings.Screenshot, () => screenshotHotkey.Combo));
        recordingHotkeyRow = new SettingRow(UiText.Settings.Hotkey, UiText.Settings.RecordingHotkeyHint, recordingHotkey);
        recordingFolderRow = FolderRow("recording_folder", () => current.RecordingFolder);
        fps = Choice(UiText.Settings.FrameRate, UiText.Settings.FrameRates, new object[] { 30, 60 });
        countdown = Choice(UiText.Settings.Countdown, UiText.Settings.Countdowns, new object[] { 0, 3, 5 });
        microphone = new ToggleSwitch(UiText.Settings.Microphone);
        systemAudio = new ToggleSwitch(UiText.Settings.SystemAudio);
        clicks = new ToggleSwitch(UiText.Settings.Clicks);
        recordingGroup = new SettingGroup(UiText.Settings.RecordingGroup) { Margin = new Thickness(0, UiTokens.Space.SectionGap, 0, 0) };
        recordingGroup.Add(recordingHotkeyRow);
        recordingGroup.Add(recordingFolderRow);
        recordingGroup.Add(new SettingRow(UiText.Settings.FrameRate, null, fps));
        recordingGroup.Add(new SettingRow(UiText.Settings.Countdown, UiText.Settings.CountdownHint, countdown));
        recordingGroup.Add(new SettingRow(UiText.Settings.Microphone, null, microphone));
        recordingGroup.Add(new SettingRow(UiText.Settings.SystemAudio, null, systemAudio));
        recordingGroup.Add(new SettingRow(UiText.Settings.Clicks, UiText.Settings.ClicksHint, clicks));

        // Allgemein
        appearance = Choice(UiText.Settings.Appearance, UiText.Settings.Appearances, new object[] { 0, 1, 2 });
        autostart = new ToggleSwitch(UiText.Settings.Autostart);
        checkUpdates = new ToggleSwitch(UiText.Settings.CheckUpdates);
        generalGroup = new SettingGroup(UiText.Settings.GeneralGroup) { Margin = new Thickness(0, UiTokens.Space.SectionGap, 0, 0) };
        generalGroup.Add(new SettingRow(UiText.Settings.Appearance, null, appearance));
        generalGroup.Add(new SettingRow(UiText.Settings.Autostart, null, autostart));
        generalGroup.Add(new SettingRow(UiText.Settings.CheckUpdates, UiText.Settings.CheckUpdatesHint, checkUpdates));
        versionRow = SettingRow.Display(UiText.Settings.Version, AppInfo.Version);
        generalGroup.Add(versionRow);

        page.Children.Add(screenshotGroup);
        page.Children.Add(recordingGroup);
        page.Children.Add(generalGroup);
        scroll.Content = page;
        Body = scroll;
        if (textScale > 1.01) ((FrameworkElement)Content).LayoutTransform = new ScaleTransform(textScale, textScale);

        HookUp();
        Populate();
    }

    /// <summary>The groups (tests).</summary>
    internal IEnumerable<SettingGroup> Groups => new[] { screenshotGroup, recordingGroup, generalGroup };

    internal HotkeyBox ScreenshotHotkey => screenshotHotkey;
    internal SettingRow ScreenshotHotkeyRow => screenshotHotkeyRow;
    internal SettingRow ScreenshotFolderRow => screenshotFolderRow;
    internal ToggleSwitch Microphone => microphone;
    internal Select Appearance => appearance;

    /// <summary>Opens (or brings back) the one settings window of the engine; <paramref name="recording"/> scrolls to the recording group.</summary>
    public static void ShowShared(bool recording)
    {
        shared ??= new SettingsWindow(new HostChannel(), CaptureSettings.Load, TextScaleFactor());
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
        if (!IsVisible)
        {
            RECT work = NativeMethods.WorkAreaAtCursor();
            double scale = NativeMethods.ScaleOfMonitor(work);
            MaxHeight = (work.bottom - work.top) / scale * 0.92;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = (work.left + (work.right - work.left) / 2.0) / scale - Width / 2;
            Top = (work.top / scale) + 40;
            Show();
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        ToolWindowBase.TakeFocus(this, new WindowInteropHelper(this).Handle);
        if (recording) recordingGroup.BringIntoView();
        (recording ? recordingHotkeyRow : screenshotHotkeyRow).Focus();
    }

    protected override void RequestClose()
    {
        screenshotHotkey.CancelCapture();
        recordingHotkey.CancelCapture();
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        e.Cancel = true;   // kept for the next time
        RequestClose();
    }

    /// <summary>Shows the settings as stored now, and which hotkeys the host could register.</summary>
    internal void Populate()
    {
        current = load();
        populating = true;
        try
        {
            screenshotHotkey.Show(current.ScreenshotHotkey);
            recordingHotkey.Show(current.RecordingHotkey);
            screenshotFolderRow.SetDescription(current.ScreenshotFolder);
            recordingFolderRow.SetDescription(current.RecordingFolder);
            fps.SelectedValue = current.Fps;
            countdown.SelectedValue = current.Countdown;
            appearance.SelectedValue = (int)current.Appearance;
            microphone.IsChecked = current.Microphone;
            systemAudio.IsChecked = current.SystemAudio;
            clicks.IsChecked = current.Clicks;
            autostart.IsChecked = current.Autostart;
            checkUpdates.IsChecked = current.CheckUpdates;
        }
        finally { populating = false; }
        ShowHotkeyState();
        ShowUpdate(Updates.UpdateNotice.Pending());
    }

    /// <summary>A newer version (as remembered by the daily check) shows in the version row with a button.</summary>
    internal void ShowUpdate(Updates.Release? pending)
    {
        if (pending == null) { versionRow.ShowStatus("", Status.None); return; }
        versionRow.ShowStatus(UiText.Update.Available(pending.Version), Status.Processing);
        if (versionRow.Control is not PrimaryButton)
            versionRow.SetControl(new PrimaryButton(UiText.Update.Now, () => Updates.UpdateNotice.Show(pending), IconGlyphs.Refresh));
    }

    void ShowHotkeyState()
    {
        var state = host.Probe();
        ShowHint(screenshotHotkeyRow, screenshotHotkey.Combo, state is { Screenshot: false });
        ShowHint(recordingHotkeyRow, recordingHotkey.Combo, state is { Recording: false });
    }

    static void ShowHint(SettingRow row, string combo, bool missing)
    {
        if (missing) { row.ShowStatus(UiText.Hotkeys.TakenRetrying, Status.Warning); return; }
        bool printAlone = HotkeyText.TryParse(combo, out var chord, out _) && chord.Key == 0x2C && (chord.Modifiers & ~HotkeyChord.Shift) == 0;
        row.ShowStatus(printAlone ? UiText.Hotkeys.PrintHint : "", Status.None);
    }

    void HookUp()
    {
        Hook(screenshotHotkey, screenshotHotkeyRow, ScreenshotHotkeyKey);
        Hook(recordingHotkey, recordingHotkeyRow, RecordingHotkeyKey);
        Bind(microphone, "microphone");
        Bind(systemAudio, "system_audio");
        Bind(clicks, "show_clicks");
        Bind(autostart, "autostart");
        Bind(checkUpdates, "check_updates");
        Bind(fps, "fps");
        Bind(countdown, "countdown");
        Bind(appearance, "appearance", value =>
        {
            CaptureSettings.UseAppearance(CaptureSettings.NormalizeAppearance((int)value));
            Palette.Apply(Resources);
        });
    }

    void Hook(HotkeyBox box, SettingRow row, string key)
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
            string before = key == ScreenshotHotkeyKey ? current.ScreenshotHotkey : current.RecordingHotkey;
            var reply = host.Set(key, combo);
            if (reply == HostReply.Ok) { current = load(); ShowHint(row, combo, false); return; }
            box.Refuse(before, Explain(reply));
        };
    }

    void Bind(ToggleSwitch toggle, string key)
    {
        toggle.Checked += delegate { Change(toggle, key, "1"); };
        toggle.Unchecked += delegate { Change(toggle, key, "0"); };
    }

    void Change(ToggleSwitch toggle, string key, string value)
    {
        if (populating) return;
        var row = (SettingRow)toggle.Parent;
        var reply = host.Set(key, value);
        if (reply == HostReply.Ok) { row.ShowStatus("", Status.None); current = load(); return; }
        populating = true;
        toggle.IsChecked = value != "1";   // back to what is stored
        populating = false;
        row.ShowStatus(Explain(reply), Status.Error);
    }

    void Bind(Select select, string key, Action<object>? applied = null)
    {
        select.SelectionChanged += delegate
        {
            if (populating || select.SelectedValue == null) return;
            var row = (SettingRow)select.Parent;
            var reply = host.Set(key, Convert.ToString(select.SelectedValue, CultureInfo.InvariantCulture)!);
            if (reply == HostReply.Ok) { row.ShowStatus("", Status.None); applied?.Invoke(select.SelectedValue); current = load(); return; }
            row.ShowStatus(Explain(reply), Status.Error);
            Populate();
        };
    }

    static Select Choice(string name, string[] labels, object[] values)
    {
        var select = new Select(name);
        for (int i = 0; i < labels.Length; i++) select.Add(labels[i], values[i]);
        return select;
    }

    SettingRow FolderRow(string key, Func<string> folder)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var row = new SettingRow(UiText.Settings.Folder, folder(), buttons);
        buttons.Children.Add(new SecondaryButton(UiText.Settings.Change, () => ChangeFolder(row, key, folder())) { Margin = new Thickness(0, 0, UiTokens.Space.Md, 0) });
        buttons.Children.Add(new SecondaryButton(UiText.Settings.Open, () => OpenFolder(row, folder()), IconGlyphs.Folder));
        return row;
    }

    void ChangeFolder(SettingRow row, string key, string before) =>
        ChooseFolder(row, key, FolderPicker.Pick(this, before));

    /// <summary>A folder was picked (or not, null): the host checks and keeps it (tests call this directly).</summary>
    internal void ChooseFolder(SettingRow row, string key, string? picked)
    {
        if (picked == null) return;
        var reply = host.Set(key, picked);
        if (reply == HostReply.Ok) { current = load(); row.SetDescription(picked); return; }
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
            HostReply.Duplicate => HotkeyVerdict.Bad(string.Format(CultureInfo.CurrentCulture, UiText.Hotkeys.Duplicate, otherName)),
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
