namespace NazarCapture.Engine.Core;

/// <summary>
/// Every text the user can see, in the interface language (<see cref="Localizer"/>): the members read the current
/// <see cref="TextCatalog"/> (engine/Core/Localization/Catalog.*.cs), so logic files never hold a literal. Names on disk
/// (default folders, file name prefixes) are not translated: a language change must not split a user's folder.
/// </summary>
static class UiText
{
    /// <summary>The product name, the one place it is written in the engine (the host has its own in strings.rs). Never translated.</summary>
    public const string Product = "Nazar Capture";

    static string T(string key) => Localizer.Text(key);

    static string F(string key, params object[] args) => Localizer.Format(key, args);

    static string Titled(string key) => Product + " – " + T(key);

    /// <summary>
    /// A file name or path as it is written, also inside right-to-left text: in Arabic it is embedded left to right
    /// (U+202A … U+202C), so "Nazar-Screenshot 2026-11-17 14-03-12.png" keeps its order and is shortened at its end.
    /// </summary>
    public static string Path(string path) => Localizer.Language.RightToLeft && path.Length > 0 ? "\u202A" + path + "\u202C" : path;

    /// <summary>
    /// Window titles, with the product name first: Alt+Tab, the taskbar and screen readers show them. The invisible
    /// helper windows of a recording (frame, countdown, toolbar, click ring) are never listed there, but screen readers
    /// and UI automation still read their titles, and tests and QA scripts find all windows by them.
    /// </summary>
    public static class Titles
    {
        public static string Overlay => Titled("titles.overlay");
        public static string ScreenshotCard => Titled("titles.screenshot_card");
        public static string Editor => Titled("titles.editor");
        public static string Pin => Titled("titles.pin");
        public static string RecordingFrame => Titled("titles.recording_frame");
        public static string Countdown => Titled("titles.countdown");
        public static string Toolbar => Titled("titles.toolbar");
        public static string RecordingCard => Titled("titles.recording_card");
        public static string Click => Titled("titles.click");
        /// <summary>Without the product name: the window (<see cref="NazarWindow"/>) adds it.</summary>
        public static string Trim => T("titles.trim");
        public static string KitDemo => Titled("titles.kit_demo");
        public static string Main => Product;
    }

    /// <summary>Names of the files the user gets; the names on disk stay the same in every language.</summary>
    public static class Files
    {
        /// <summary>Default folder below Pictures.</summary>
        public const string ScreenshotFolder = "Nazar-Screenshots";
        /// <summary>Default folder below Videos.</summary>
        public const string RecordingFolder = "Nazar-Aufnahmen";
        public const string ScreenshotPrefix = "Nazar-Screenshot ";
        public const string RecordingPrefix = "Nazar-Aufnahme ";
        public const string TrimmedSuffix = " gekürzt";
        /// <summary>Folder next to a recording that could not be saved: its raw segments and sound tracks.</summary>
        public const string RawDataSuffix = " (Rohdaten)";
        public static string SaveAsTitle => Titled("files.save_as_title");
        public static string SaveAsFilter => T("files.save_as_filter");
    }

    public static class Common
    {
        public static string Folder => T("common.folder");
        public static string Close => T("common.close");
        public static string DeleteFailed => T("common.delete_failed");
        public static string NotYetSaved => T("common.not_yet_saved");
        public static string Delete => T("common.delete");
        public static string Deleted => T("common.deleted");
        public static string InRecycleBin => T("common.in_recycle_bin");
        public static string CopiedToClipboard => T("common.copied_to_clipboard");
        public static string CouldNotSave => T("common.could_not_save");
        public static string ReadingText => T("common.reading_text");
        public static string NoTextFound => T("common.no_text_found");
        public static string TextRecognitionUnavailable => T("common.text_recognition_unavailable");
        public static string TextCopied => T("common.text_copied");
        public static string UnknownError => T("common.unknown_error");
        public static string CopyFailed => T("common.copy_failed");
        public static string FfmpegTimedOut => T("common.ffmpeg_timed_out");
        public static string Saving => T("common.saving");
        /// <summary>"Speichern" on a card: a capture that was not kept goes into its folder after all.</summary>
        public static string Keep => T("common.keep");
        public static string Kept => T("common.kept");
        /// <summary>The chosen folder could not be written; the capture is in <see cref="FileKeeping.FallbackFolder"/>.</summary>
        public static string SavedInFallback => T("common.saved_in_fallback");
        /// <summary>Under <see cref="SavedInFallback"/>: why, in a few plain words.</summary>
        public static string FolderUnreachable => T("common.folder_unreachable");
        /// <summary>A capture that was not kept ("Screenshots speichern" off).</summary>
        public static string OnlyInClipboard => T("common.only_in_clipboard");

        public static string Size(int width, int height) => F("common.size", width, height);

        /// <summary>"3 Zeilen · 42 Zeichen"</summary>
        public static string TextSummary(int lines, int characters) =>
            F(lines == 1 ? "common.lines.one" : "common.lines.other", lines) + " · " + F(characters == 1 ? "common.characters.one" : "common.characters.other", characters);
    }

    public static class Overlay
    {
        public static string ScreenshotHint => T("overlay.screenshot_hint");
        public static string RecordingHint => T("overlay.recording_hint");
        public static string ColorCopiedSuffix => T("overlay.color_copied_suffix");

        /// <summary>The hint as pairs of keys (shown as key caps, may be empty) and what they do.</summary>
        public static (string Keys, string Text)[] ScreenshotHints => new[]
        {
            ("", T("overlay.drag")),
            ("", T("overlay.click")),
            (T("keys.space"), T("overlay.whole_screen")),
            ("C", T("overlay.copy_color")),
            (T("overlay.arrows"), T("overlay.arrows_hint")),
            ("Esc", T("overlay.cancel")),
        };

        public static (string Keys, string Text)[] RecordingHints => new[]
        {
            ("", T("overlay.recording_drag")),
            ("", T("overlay.click")),
            (T("keys.space"), T("overlay.whole_screen")),
            (T("overlay.arrows"), T("overlay.arrows_hint")),
            ("Esc", T("overlay.cancel")),
        };
    }

    public static class ScreenshotCard
    {
        public static string Edit => T("screenshot_card.edit");
        public static string Pin => T("screenshot_card.pin");
        public static string Text => T("screenshot_card.text");
        public static string EditedAndCopied => T("screenshot_card.edited_and_copied");
        public static string NotSaved => T("screenshot_card.not_saved");
        public static string SavedNotCopied => T("screenshot_card.saved_not_copied");
        public static string Problem => T("screenshot_card.problem");
        public static string ScreenUnavailable => T("screenshot_card.screen_unavailable");
        public static string FolderNotWritable(string folder) => F("screenshot_card.folder_not_writable", Path(folder));
    }

    public static class Editor
    {
        static readonly string[] ToolKeys = { "move", "arrow", "rectangle", "circle", "line", "pen", "highlighter", "spotlight", "text", "text_box", "number", "pixelate", "blur" };

        /// <summary>Tool names in the order of <c>ScreenshotEditor.Tool</c>.</summary>
        public static string[] ToolNames => ToolKeys.Select(key => T("editor.tool." + key)).ToArray();
        public static string[] WidthNames => Enumerable.Range(0, 5).Select(i => T("editor.width." + i.ToString(CultureInfo.InvariantCulture))).ToArray();
        public static string MoreHint => T("editor.more_hint");
        public static string Undo => T("editor.undo");
        public static string Redo => T("editor.redo");
        public static string SaveAs => T("editor.save_as");
        public static string Copy => T("editor.copy");
        public static string CopyText => T("editor.copy_text");
        public static string Cancel => T("editor.cancel");
        public static string Done => T("editor.done");
        public static string Pin => T("editor.pin");
        public static string WidthHint => T("editor.width_hint");
        public static string ConfirmDiscard => T("editor.confirm_discard");
        public static string CommandFailed => T("editor.command_failed");

        public static string ColorTip(int number) => F("editor.color_tip", number);
        public static string SizeNote(string widthName) => F("editor.size_note", widthName);
        public static string Saved(string fileName) => F("editor.saved", Path(fileName));
        public static string TextCopied(string summary) => Common.TextCopied + " · " + summary;
    }

    public static class Pin
    {
        public static string Copy => T("pin.copy");
        public static string SaveAs => T("pin.save_as");
        public static string OriginalSize => T("pin.original_size");
        public static string Close => T("pin.close");
        public static string CloseTip => T("pin.close_tip");
        public static string CloseAll => T("pin.close_all");
        public static string CopyShortcut => T("pin.copy_shortcut");
        public static string SaveAsShortcut => T("pin.save_as_shortcut");
        public static string Copied => T("pin.copied");
        public static string Saved => T("pin.saved");
        public static string Opacity(double percent) => F("pin.opacity", percent);
        public static string Zoom(double percent) => F("pin.zoom", percent);
    }

    public static class Recording
    {
        public static string FfmpegMissing => T("recording.ffmpeg_missing");
        public static string DiskFull => T("recording.disk_full");
        public static string DiskFullDetail(string free) => F("recording.disk_full_detail", free);
        public static string FfmpegInstallHint => T("recording.ffmpeg_install_hint");
        public static string NotPossible => T("recording.not_possible");
        public static string MonitorUnreachable => T("recording.monitor_unreachable");
        public static string MonitorOnOtherGpu => T("recording.monitor_on_other_gpu");
        public static string Failed => T("recording.failed");
        public static string Saved => T("recording.saved");
        public static string SavedWithoutAudio => T("recording.saved_without_audio");
        /// <summary>Not kept ("Aufnahmen speichern" off), in the clipboard.</summary>
        public static string Copied => T("recording.copied");
        public static string NoFrames => T("recording.no_frames");
        public static string StoppedUnexpectedly => T("recording.stopped_unexpectedly");
        public static string AudioLost => T("recording.audio_lost");
        public static string CreatingGif => T("recording.creating_gif");
        public static string GifFailed => T("recording.gif_failed");
        public static string GifSaved => T("recording.gif_saved");
        public static string GifSavedNotCopied => T("recording.gif_saved_not_copied");
        public static string TrimmedNotCopied => T("recording.trimmed_not_copied");
        public static string SavedNotCopied => T("recording.saved_not_copied");
        public static string Aborted => T("recording.aborted");
        public static string OpenRawData => T("recording.open_raw_data");
        public static string TrimmedAndCopied => T("recording.trimmed_and_copied");
        public static string Trim => T("recording.trim");
        public static string Gif => T("recording.gif");
        public static string CountdownCancels => T("recording.countdown_cancels");
        public static string CountdownHint => T("recording.countdown_hint");

        public static string Recorded(string duration) => F("recording.recorded", duration);
        public static string FileInfo(string fileName, string size) => Path(fileName) + " · " + size;
        public static string Length(bool gif, string duration) => (gif ? Gif + " · " : "") + duration;
    }

    public static class Toolbar
    {
        public static string Move => T("toolbar.move");
        public static string MicrophoneOn => T("toolbar.microphone_on");
        public static string MicrophoneOff => T("toolbar.microphone_off");
        public static string SoundOn => T("toolbar.sound_on");
        public static string SoundOff => T("toolbar.sound_off");
        public static string ClicksOn => T("toolbar.clicks_on");
        public static string ClicksOff => T("toolbar.clicks_off");
        public static string Resume => T("toolbar.resume");
        public static string Pause => T("toolbar.pause");
        public static string Stop => T("toolbar.stop");
        public static string Discard => T("toolbar.discard");
        public static string ConfirmDiscard => T("toolbar.confirm_discard");
        public static string ConfirmDiscardShort => T("toolbar.confirm_discard_short");
    }

    public static class Trim
    {
        public static string KeyHint => T("trim.key_hint");
        public static string Sound => T("trim.sound");
        public static string Play => T("trim.play");
        public static string AsGif => T("trim.as_gif");
        public static string Save => T("trim.save");

        public static string Selection(string length) => F("trim.selection", length);
        public static string NotSaved(string reason) => F("trim.not_saved", reason);
        public static string Progress(string label, double fraction) => label + "  " + (int)Math.Round(fraction * 100) + " %";
        public static string VideoInfo(string fileName, string duration, int width, int height) => Path(fileName) + " · " + duration + " · " + Common.Size(width, height);
    }

    /// <summary>The hotkey field (key caps, recording, the reasons of a refusal).</summary>
    public static class Hotkeys
    {
        public static string Change => T("hotkeys.change");
        public static string Default => T("hotkeys.default");
        public static string Cancel => T("hotkeys.cancel");
        public static string Unset => T("hotkeys.unset");
        public static string PressKeys => T("hotkeys.press_keys");
        public static string Capture => T("hotkeys.capture");
        public static string Saved => T("hotkeys.saved");
        public static string ShiftOnly => T("hotkeys.shift_only");
        public static string MissingModifier => T("hotkeys.missing_modifier");
        public static string MissingKey => T("hotkeys.missing_key");
        public static string Unsupported => T("hotkeys.unsupported");
        /// <summary>The other action holds these keys (<see cref="Settings.Recording"/>, <see cref="Settings.Screenshot"/>).</summary>
        public static string Duplicate(string otherAction) => F("hotkeys.duplicate", otherAction);
        public static string Taken => T("hotkeys.taken");
        public static string TakenRetrying => T("hotkeys.taken_retrying");
        public static string PrintHint => T("hotkeys.print_hint");
    }

    /// <summary>Key-cap names that differ between languages (letters, digits, F keys, Alt and Win do not).</summary>
    public static class Keys
    {
        public static string Ctrl => T("keys.ctrl");
        public static string Shift => T("keys.shift");
        public static string Space => T("keys.space");
        public static string Print => T("keys.print");
        public static string Insert => T("keys.insert");
        public static string Pause => T("keys.pause");
        public static string NumDecimal => T("keys.num_decimal");
    }

    /// <summary>The settings window.</summary>
    public static class Settings
    {
        public static string Title => T("settings.title");
        public static string ScreenshotGroup => T("settings.screenshot_group");
        public static string RecordingGroup => T("settings.recording_group");
        public static string GeneralGroup => T("settings.general_group");
        public static string InfoGroup => T("settings.info_group");
        public static string SoundGroup => T("settings.sound_group");
        public static string Tabs => T("settings.tabs");
        public static string TabChosen => T("settings.tab_chosen");
        public static string MicrophoneHint => T("settings.microphone_hint");
        public static string SystemAudioHint => T("settings.system_audio_hint");
        public static string AutostartHint => T("settings.autostart_hint");
        public static string ProjectPage => T("settings.project_page");
        public static string ProjectPageHint => T("settings.project_page_hint");
        public static string ReportProblem => T("settings.report_problem");
        public static string ReportProblemHint => T("settings.report_problem_hint");
        public static string License => T("settings.license");
        public static string LicenseHint => T("settings.license_hint");
        public static string View => T("settings.view");
        public static string UpdateTitle => T("settings.update_title");
        public static string UpdateDismiss => T("settings.update_dismiss");
        public static string UpdateHint(string version, string current) => F("settings.update_hint", version, current);
        public static string VersionValue(string version) => Product + " " + version;
        public static string Hotkey => T("settings.hotkey");
        public static string ScreenshotHotkeyHint => T("settings.screenshot_hotkey_hint");
        public static string RecordingHotkeyHint => T("settings.recording_hotkey_hint");
        public static string Folder => T("settings.folder");
        public static string Change => T("settings.change");
        public static string Open => T("settings.open");
        public static string FolderMissing => T("settings.folder_missing");
        public static string FolderUnusable => T("settings.folder_unusable");
        public static string FrameRate => T("settings.frame_rate");
        public static string Countdown => T("settings.countdown");
        public static string CountdownHint => T("settings.countdown_hint");
        public static string Microphone => T("settings.microphone");
        public static string SystemAudio => T("settings.system_audio");
        public static string Clicks => T("settings.clicks");
        public static string ClicksHint => T("settings.clicks_hint");
        public static string Appearance => T("settings.appearance");
        public static string Language => T("settings.language");
        public static string LanguageHint => T("settings.language_hint");
        public static string LanguageAuto => T("settings.language_auto");
        public static string Autostart => T("settings.autostart");
        public static string CheckUpdates => T("settings.check_updates");
        public static string CheckUpdatesHint => T("settings.check_updates_hint");
        public static string UpdatesTitle => T("settings.updates_title");
        public static string UpdatesInstalled(string version) => F("settings.updates_installed", version);
        public static string SearchUpdates => T("settings.search_updates");
        public static string Searching => T("settings.searching");
        public static string UpToDate(string version) => F("settings.up_to_date", version);
        public static string SearchFailed => T("settings.search_failed");
        public static string SaveScreenshots => T("settings.save_screenshots");
        public static string SaveScreenshotsHint => T("settings.save_screenshots_hint");
        public static string SaveScreenshotsOffHint => T("settings.save_screenshots_off_hint");
        public static string SaveRecordings => T("settings.save_recordings");
        public static string SaveRecordingsHint => T("settings.save_recordings_hint");
        public static string SaveRecordingsOffHint => T("settings.save_recordings_off_hint");
        public static string KeepFor => T("settings.keep_for");
        public static string KeepScreenshotsHint => T("settings.keep_screenshots_hint");
        public static string KeepRecordingsHint => T("settings.keep_recordings_hint");
        public static string Version => T("settings.version");
        public static string NoHost => T("settings.no_host");
        public static string SaveFailed => T("settings.save_failed");
        public static string AutostartFailed => T("settings.autostart_failed");
        /// <summary>The recording as the object of <see cref="Hotkeys.Duplicate(string)"/> ("Schon für die Aufnahme vergeben").</summary>
        public static string Recording => T("settings.recording");
        public static string Screenshot => T("settings.screenshot");
        public static string[] FrameRates => new[] { T("settings.frame_rates.30"), T("settings.frame_rates.60") };
        public static string[] Countdowns => new[] { T("settings.countdowns.0"), T("settings.countdowns.3"), T("settings.countdowns.5") };
        public static string[] Appearances => new[] { T("settings.appearances.light"), T("settings.appearances.dark") };
        /// <summary>The choices of "Aufbewahren", in the order of <see cref="CaptureSettings.KeepDayChoices"/>.</summary>
        public static string[] KeepChoices => CaptureSettings.KeepDayChoices.Select(days => T("settings.keep." + days.ToString(CultureInfo.InvariantCulture))).ToArray();
    }

    /// <summary>The main window (Hams' main window: head, status, the two actions, the hotkeys as keys).</summary>
    public static class Main
    {
        public static string SettingsTooltip => T("main.settings_tooltip");
        public static string HideTooltip => T("main.hide_tooltip");
        public static string Screenshot => T("main.screenshot");
        public static string Recording => T("main.recording");
        public static string StopRecording => T("main.stop_recording");
        public static string Ready => T("main.ready");
        public static string RecordingRunning => T("main.recording_running");
    }

    /// <summary>The update notice and "Jetzt aktualisieren".</summary>
    public static class Update
    {
        public static string Title => Titled("update.title");
        public static string Now => T("update.now");
        public static string Later => T("update.later");
        public static string WhatsNew => T("update.whats_new");
        public static string Downloading => T("update.downloading");
        public static string Installing => T("update.installing");
        public static string Failed => T("update.failed");
        public static string Available(string version) => F("update.available", version);
        public static string Running(string version) => F("update.running", version);
        public static string Reason(Updates.UpdateProblem problem) => problem switch
        {
            Updates.UpdateProblem.NoDownload => T("update.reason.no_download"),
            Updates.UpdateProblem.NoChecksum => T("update.reason.no_checksum"),
            Updates.UpdateProblem.ChecksumMismatch => T("update.reason.checksum_mismatch"),
            Updates.UpdateProblem.DownloadFailed => T("update.reason.download_failed"),
            Updates.UpdateProblem.InstallerMissing => T("update.reason.installer_missing"),
            Updates.UpdateProblem.UntrustedSource => T("update.reason.untrusted_source"),
            Updates.UpdateProblem.Busy => T("update.reason.busy"),
            _ => T("update.reason.start_failed"),
        };
    }
}
