namespace MosaikCapture.Engine.Settings;

// The settings of Nazar Capture as data. The window (SettingsWindow) is the family's, as Hams' settings_window.py: a
// stage, a tab row, one page per tab, groups on paper plates with rows. What is Nazar's own is only this list: which
// pages, which groups and which rows, with their keys in settings.ini. A new row is one line here, not a new window.

/// <summary>One row of a settings page: its title and help text; the kind decides the control.</summary>
internal abstract record RowSpec(string Title, string? Hint);

/// <summary>A switch for a flag in settings.ini; a click anywhere on the row flips it.</summary>
internal sealed record SwitchSpec(string Title, string? Hint, string Key, Func<CaptureSettingsSnapshot, bool> Value) : RowSpec(Title, Hint);

/// <summary>
/// A choice from a short list (Hams' Select): beside its text 160 wide, or <paramref name="Stacked"/> below the text in
/// full width when the values are long.
/// </summary>
internal sealed record ChoiceSpec(string Title, string? Hint, string Key, string[] Labels, int[] Values, Func<CaptureSettingsSnapshot, int> Value, bool Stacked = false)
    : RowSpec(Title, Hint);

/// <summary>A hotkey as key caps with "Ändern" below the text (Hams' shortcut rows); recorded by pressing it.</summary>
internal sealed record HotkeySpec(string Title, string Hint, string Key, string Default, string OtherKey, string OtherName, Func<CaptureSettingsSnapshot, string> Value)
    : RowSpec(Title, Hint);

/// <summary>A folder: its path as the help text, "Ändern" and "Öffnen" below it.</summary>
internal sealed record FolderSpec(string Title, string Key, Func<CaptureSettingsSnapshot, string> Value) : RowSpec(Title, null);

/// <summary>A row that opens a web page (project page, issues, licence).</summary>
internal sealed record LinkSpec(string Title, string Hint, string Button, string Url) : RowSpec(Title, Hint);

/// <summary>A row that only shows a value (Hams' "display" rows: the version).</summary>
internal sealed record DisplaySpec(string Title, string Value) : RowSpec(Title, null);

/// <summary>The calm notice of a new version, shown only while one is waiting: "Ansehen" and a cross to hide it.</summary>
internal sealed record UpdateNoticeSpec() : RowSpec(UiText.Settings.UpdateTitle, null);

/// <summary>A group of rows on one plate; the first group of a page has no heading (the tab is its heading).</summary>
internal sealed record GroupSpec(string? Heading, params RowSpec[] Rows);

/// <summary>One tab and its page.</summary>
internal sealed record PageSpec(string Title, params GroupSpec[] Groups);

/// <summary>The pages of Nazar Capture's settings.</summary>
internal static class NazarSettings
{
    public const string ScreenshotHotkey = "screenshot_hotkey", RecordingHotkey = "recording_hotkey";
    public const string ScreenshotFolder = "screenshot_folder", RecordingFolder = "recording_folder";
    public const string Appearance = "appearance";

    public const string ProjectUrl = "https://github.com/" + Updates.UpdateCheck.Repository;
    public const string IssuesUrl = ProjectUrl + "/issues";
    public const string LicenseUrl = ProjectUrl + "/blob/main/LICENSE";

    static readonly int[] FrameRates = { 30, 60 }, Countdowns = { 0, 3, 5 }, Appearances = { 0, 1, 2 };

    /// <summary>Index of the recording page (the tray's "recording settings" open it).</summary>
    public const int RecordingPage = 1;

    public static IReadOnlyList<PageSpec> Pages() => new[]
    {
        new PageSpec(UiText.Settings.ScreenshotGroup,
            new GroupSpec(null,
                new HotkeySpec(UiText.Settings.Hotkey, UiText.Settings.ScreenshotHotkeyHint, ScreenshotHotkey, CaptureSettingsSnapshot.DefaultScreenshotHotkey,
                    RecordingHotkey, UiText.Settings.Recording, s => s.ScreenshotHotkey),
                new FolderSpec(UiText.Settings.Folder, ScreenshotFolder, s => s.ScreenshotFolder))),
        new PageSpec(UiText.Settings.RecordingGroup,
            new GroupSpec(null,
                new HotkeySpec(UiText.Settings.Hotkey, UiText.Settings.RecordingHotkeyHint, RecordingHotkey, CaptureSettingsSnapshot.DefaultRecordingHotkey,
                    ScreenshotHotkey, UiText.Settings.Screenshot, s => s.RecordingHotkey),
                new FolderSpec(UiText.Settings.Folder, RecordingFolder, s => s.RecordingFolder),
                new ChoiceSpec(UiText.Settings.FrameRate, null, "fps", UiText.Settings.FrameRates, FrameRates, s => s.Fps, Stacked: true),
                new ChoiceSpec(UiText.Settings.Countdown, UiText.Settings.CountdownHint, "countdown", UiText.Settings.Countdowns, Countdowns, s => s.Countdown)),
            new GroupSpec(UiText.Settings.SoundGroup,
                new SwitchSpec(UiText.Settings.Microphone, UiText.Settings.MicrophoneHint, "microphone", s => s.Microphone),
                new SwitchSpec(UiText.Settings.SystemAudio, UiText.Settings.SystemAudioHint, "system_audio", s => s.SystemAudio),
                new SwitchSpec(UiText.Settings.Clicks, UiText.Settings.ClicksHint, "show_clicks", s => s.Clicks))),
        new PageSpec(UiText.Settings.GeneralGroup,
            new GroupSpec(null,
                new ChoiceSpec(UiText.Settings.Appearance, null, Appearance, UiText.Settings.Appearances, Appearances, s => (int)s.Appearance),
                new SwitchSpec(UiText.Settings.Autostart, UiText.Settings.AutostartHint, "autostart", s => s.Autostart),
                new UpdateNoticeSpec(),
                new SwitchSpec(UiText.Settings.CheckUpdates, UiText.Settings.CheckUpdatesHint, "check_updates", s => s.CheckUpdates))),
        new PageSpec(UiText.Settings.InfoGroup,
            new GroupSpec(null,
                new DisplaySpec(UiText.Settings.Version, UiText.Settings.VersionValue(AppInfo.Version)),
                new LinkSpec(UiText.Settings.ProjectPage, UiText.Settings.ProjectPageHint, UiText.Settings.Open, ProjectUrl),
                new LinkSpec(UiText.Settings.ReportProblem, UiText.Settings.ReportProblemHint, UiText.Settings.Open, IssuesUrl),
                new LinkSpec(UiText.Settings.License, UiText.Settings.LicenseHint, UiText.Settings.View, LicenseUrl))),
    };
}
