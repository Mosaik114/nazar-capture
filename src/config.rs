//! The user's capture settings: defaults, normalisation, validation and the INI
//! serialisation format.
//!
//! Everything here is pure. Reading and writing the actual file lives in
//! [`crate::store`], which feeds raw values in through [`ProfileSource`].

use std::ffi::{OsStr, OsString};
use std::os::windows::ffi::OsStrExt;
use std::path::{Path, PathBuf};

use crate::hotkey::{Hotkey, ParseHotkeyError};

/// INI section holding all settings.
pub const SECTION: &str = "capture";

pub mod keys {
    pub const SCREENSHOT_HOTKEY: &str = "screenshot_hotkey";
    pub const RECORDING_HOTKEY: &str = "recording_hotkey";
    pub const SCREENSHOT_FOLDER: &str = "screenshot_folder";
    pub const RECORDING_FOLDER: &str = "recording_folder";
    pub const MICROPHONE: &str = "microphone";
    pub const SYSTEM_AUDIO: &str = "system_audio";
    /// Visualise mouse clicks in recordings.
    pub const SHOW_CLICKS: &str = "show_clicks";
    /// Start with Windows.
    pub const AUTOSTART: &str = "autostart";
    pub const FPS: &str = "fps";
    /// Countdown before a recording starts, in seconds.
    pub const COUNTDOWN: &str = "countdown";
    /// Light or dark (read by the engine).
    pub const APPEARANCE: &str = "appearance";
    /// Look for a newer version (read by the engine).
    pub const CHECK_UPDATES: &str = "check_updates";
    /// The language of the interface, `auto` or a code of [`super::Language`] (host and engine).
    pub const LANGUAGE: &str = "language";
    /// Every screenshot as a file in the folder; off: only to the clipboard (read by the engine).
    pub const SAVE_SCREENSHOTS: &str = "save_screenshots";
    /// Screenshots older than this many days are deleted (0: kept; read by the engine).
    pub const SCREENSHOT_KEEP_DAYS: &str = "screenshot_keep_days";
    /// Every recording as a file in the folder; off: only to the clipboard (read by the engine).
    pub const SAVE_RECORDINGS: &str = "save_recordings";
    /// Recordings older than this many days are deleted (0: kept; read by the engine).
    pub const RECORDING_KEEP_DAYS: &str = "recording_keep_days";
    /// The layout of the file (see [`super::SETTINGS_VERSION`]); only the host reads it.
    pub const SETTINGS_VERSION: &str = "settings_version";
}

/// The layout of `settings.ini` this host writes. A file without `settings_version`
/// (or with a smaller one) was written before 1.1.6 and is settled once at start-up,
/// see [`crate::store::SettingsStore::load_settled`].
pub const SETTINGS_VERSION: i32 = 2;

pub const DEFAULT_SCREENSHOT_HOTKEY: &str = "Shift+Alt+4";
pub const DEFAULT_RECORDING_HOTKEY: &str = "Shift+Alt+5";
/// Subfolders of the user's Pictures and Videos folders that hold the captures by default.
pub const SCREENSHOT_FOLDER_NAME: &str = "Nazar-Screenshots";
pub const RECORDING_FOLDER_NAME: &str = "Nazar-Aufnahmen";
/// The default subfolders of Mosaik Capture, the app's name up to 1.1.2. Settings copied
/// from it may still name them; such a folder becomes the new default once (the files
/// in it stay where they are).
pub const LEGACY_SCREENSHOT_FOLDER_NAME: &str = "Mosaiks-Screenshots";
pub const LEGACY_RECORDING_FOLDER_NAME: &str = "Mosaiks-Aufnahmen";

/// Longest folder path in UTF-16 units: the Windows limit for long paths, and what
/// the engine reads back.
pub const MAX_FOLDER_LEN: usize = 32_767;
/// Characters that may not appear anywhere in a folder path.
const FORBIDDEN_PATH_CHARS: &[u8] = b"\n\r\"*<>?|";

/// Recording frame rate. Anything other than 30 is treated as 60.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum FrameRate {
    Fps30,
    #[default]
    Fps60,
}

impl FrameRate {
    /// Interprets a stored value.
    pub const fn from_value(value: i32) -> Self {
        if value == 30 { Self::Fps30 } else { Self::Fps60 }
    }

    pub const fn value(self) -> i32 {
        match self {
            Self::Fps30 => 30,
            Self::Fps60 => 60,
        }
    }
}

/// Countdown before a recording starts. Unknown values fall back to three seconds.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Countdown {
    Off,
    #[default]
    Seconds3,
    Seconds5,
}

impl Countdown {
    /// Interprets a stored value.
    pub const fn from_value(value: i32) -> Self {
        match value {
            0 => Self::Off,
            5 => Self::Seconds5,
            _ => Self::Seconds3,
        }
    }

    /// The length in seconds, as stored.
    pub const fn value(self) -> i32 {
        match self {
            Self::Off => 0,
            Self::Seconds3 => 3,
            Self::Seconds5 => 5,
        }
    }
}

/// Which palette the windows show: the user chooses light or dark, as in Hams. Unknown
/// values mean light, the default for new users.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Appearance {
    #[default]
    Light,
    Dark,
}

impl Appearance {
    /// Interprets a stored value: 2 = dark, anything else = light.
    pub const fn from_value(value: i32) -> Self {
        if value == 2 { Self::Dark } else { Self::Light }
    }

    /// The value as stored.
    pub const fn value(self) -> i32 {
        match self {
            Self::Light => 1,
            Self::Dark => 2,
        }
    }

    /// Whether a stored value is one of the two choices. Versions up to 1.1.3 also knew
    /// 0 = "like Windows" (and showed any other value, or a missing key, that way); such
    /// a file is settled once, see [`crate::store::SettingsStore::load_settled`].
    pub const fn is_choice(value: i32) -> bool {
        matches!(value, 1 | 2)
    }

    /// The palette "like Windows" shows while Windows uses the given app theme.
    pub const fn like_windows(windows_light: bool) -> Self {
        if windows_light { Self::Light } else { Self::Dark }
    }
}

/// The language of the interface (tray, messages, the engine's windows). `Auto` follows
/// the Windows display language; unknown values mean `Auto`.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Language {
    #[default]
    Auto,
    German,
    English,
    Russian,
    Spanish,
    French,
    Arabic,
    Turkish,
}

impl Language {
    /// Every choice, in the order of the settings select.
    pub const ALL: [Self; 8] = [
        Self::Auto,
        Self::German,
        Self::English,
        Self::Russian,
        Self::Spanish,
        Self::French,
        Self::Arabic,
        Self::Turkish,
    ];

    /// The value as stored (`auto`, `de`, `en`, `ru`, `es`, `fr`, `ar`, `tr`).
    pub const fn code(self) -> &'static str {
        match self {
            Self::Auto => "auto",
            Self::German => "de",
            Self::English => "en",
            Self::Russian => "ru",
            Self::Spanish => "es",
            Self::French => "fr",
            Self::Arabic => "ar",
            Self::Turkish => "tr",
        }
    }

    /// Interprets a stored value: case and surrounding spaces do not matter; anything
    /// unknown is `Auto`.
    pub fn from_code(text: &str) -> Self {
        let text = text.trim();
        Self::ALL
            .into_iter()
            .find(|language| language.code().eq_ignore_ascii_case(text))
            .unwrap_or_default()
    }

    /// The concrete language for `Auto`: German for a German Windows display language,
    /// one of the others when Windows shows it, English for every other language.
    /// `primary` is the primary language id of a Windows `LANGID` (its low ten bits).
    pub const fn resolve(self, primary: u16) -> Self {
        if !matches!(self, Self::Auto) {
            return self;
        }
        match primary {
            0x07 => Self::German,
            0x19 => Self::Russian,
            0x0A => Self::Spanish,
            0x0C => Self::French,
            0x01 => Self::Arabic,
            0x1F => Self::Turkish,
            _ => Self::English,
        }
    }
}

/// How long the app keeps its own captures in their folder: 0 = unlimited, else days.
/// Only the choices of the settings select; anything else means unlimited, so a typo
/// never deletes anything.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct KeepDays(i32);

impl KeepDays {
    pub const CHOICES: [i32; 5] = [0, 7, 30, 90, 365];

    /// Interprets a stored value.
    pub fn from_value(value: i32) -> Self {
        Self(if Self::CHOICES.contains(&value) { value } else { 0 })
    }

    /// The value as stored.
    pub const fn value(self) -> i32 {
        self.0
    }
}

/// Default output folders, which depend on the user's known folders.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DefaultFolders {
    pub screenshots: PathBuf,
    pub recordings: PathBuf,
}

impl DefaultFolders {
    /// The app's subfolders of the user's "Pictures" and "Videos" folders.
    pub fn new(pictures: &Path, videos: &Path) -> Self {
        Self {
            screenshots: pictures.join(SCREENSHOT_FOLDER_NAME),
            recordings: videos.join(RECORDING_FOLDER_NAME),
        }
    }

    /// The default subfolders of Mosaik Capture below the same known folders.
    pub fn legacy(pictures: &Path, videos: &Path) -> Self {
        Self {
            screenshots: pictures.join(LEGACY_SCREENSHOT_FOLDER_NAME),
            recordings: videos.join(LEGACY_RECORDING_FOLDER_NAME),
        }
    }
}

/// Raw access to stored settings: a missing key yields the given default.
pub trait ProfileSource {
    /// Reads a string value exactly as stored (lossless UTF-16).
    fn string(&self, key: &str, default: &OsStr) -> OsString;
    /// Reads a decimal 32-bit integer; anything else yields `default`, exactly as the
    /// engine reads numbers.
    fn int(&self, key: &str, default: i32) -> i32;
}

/// The two hotkeys of a configuration, both valid and distinct.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Hotkeys {
    pub screenshot: Hotkey,
    pub recording: Hotkey,
}

/// Why a configuration cannot be applied.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ConfigError {
    InvalidHotkey(ParseHotkeyError),
    /// Both actions use the same hotkey.
    DuplicateHotkey,
    /// A folder is not a complete, valid path.
    InvalidFolder,
}

#[expect(
    clippy::struct_excessive_bools,
    reason = "the flags are independent settings that mirror the INI file one to one"
)]
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Config {
    /// As entered by the user.
    pub screenshot_hotkey: String,
    pub recording_hotkey: String,
    /// Kept as a path: any valid Windows name round-trips.
    pub screenshot_folder: PathBuf,
    pub recording_folder: PathBuf,
    pub microphone: bool,
    pub system_audio: bool,
    pub show_clicks: bool,
    pub autostart: bool,
    pub frame_rate: FrameRate,
    pub countdown: Countdown,
    pub appearance: Appearance,
    pub check_updates: bool,
    pub language: Language,
    pub save_screenshots: bool,
    pub screenshot_keep_days: KeepDays,
    pub save_recordings: bool,
    pub recording_keep_days: KeepDays,
}

impl Config {
    /// The settings used when nothing has been stored yet.
    pub fn defaults(folders: &DefaultFolders) -> Self {
        Self {
            screenshot_hotkey: DEFAULT_SCREENSHOT_HOTKEY.to_owned(),
            recording_hotkey: DEFAULT_RECORDING_HOTKEY.to_owned(),
            screenshot_folder: folders.screenshots.clone(),
            recording_folder: folders.recordings.clone(),
            microphone: false,
            system_audio: true,
            show_clicks: true,
            autostart: true,
            frame_rate: FrameRate::default(),
            countdown: Countdown::default(),
            appearance: Appearance::default(),
            check_updates: true,
            language: Language::default(),
            save_screenshots: true,
            screenshot_keep_days: KeepDays::default(),
            save_recordings: true,
            recording_keep_days: KeepDays::default(),
        }
    }

    /// Loads settings, falling back to [`Config::defaults`] for missing keys and
    /// normalising out-of-range numbers.
    pub fn load(source: &impl ProfileSource, folders: &DefaultFolders) -> Self {
        let defaults = Self::defaults(folders);
        // Hotkeys are text; anything that is not valid UTF-16 cannot parse anyway.
        let text = |key, default: &str| {
            source.string(key, OsStr::new(default)).to_string_lossy().into_owned()
        };
        let path = |key, default: &Path| PathBuf::from(source.string(key, default.as_os_str()));
        let flag = |key, default: bool| source.int(key, i32::from(default)) != 0;
        Self {
            screenshot_hotkey: text(keys::SCREENSHOT_HOTKEY, &defaults.screenshot_hotkey),
            recording_hotkey: text(keys::RECORDING_HOTKEY, &defaults.recording_hotkey),
            screenshot_folder: path(keys::SCREENSHOT_FOLDER, &defaults.screenshot_folder),
            recording_folder: path(keys::RECORDING_FOLDER, &defaults.recording_folder),
            microphone: flag(keys::MICROPHONE, defaults.microphone),
            system_audio: flag(keys::SYSTEM_AUDIO, defaults.system_audio),
            show_clicks: flag(keys::SHOW_CLICKS, defaults.show_clicks),
            autostart: flag(keys::AUTOSTART, defaults.autostart),
            frame_rate: FrameRate::from_value(source.int(keys::FPS, defaults.frame_rate.value())),
            countdown: Countdown::from_value(
                source.int(keys::COUNTDOWN, defaults.countdown.value()),
            ),
            appearance: Appearance::from_value(
                source.int(keys::APPEARANCE, defaults.appearance.value()),
            ),
            check_updates: flag(keys::CHECK_UPDATES, defaults.check_updates),
            language: Language::from_code(&text(keys::LANGUAGE, defaults.language.code())),
            save_screenshots: flag(keys::SAVE_SCREENSHOTS, defaults.save_screenshots),
            screenshot_keep_days: KeepDays::from_value(
                source.int(keys::SCREENSHOT_KEEP_DAYS, defaults.screenshot_keep_days.value()),
            ),
            save_recordings: flag(keys::SAVE_RECORDINGS, defaults.save_recordings),
            recording_keep_days: KeepDays::from_value(
                source.int(keys::RECORDING_KEEP_DAYS, defaults.recording_keep_days.value()),
            ),
        }
    }

    /// Serialises the settings in the exact layout `WritePrivateProfileStringW`
    /// produces for a fresh file, so the engine and older versions read it unchanged.
    /// Folders are written as stored, so the result is an `OsString`.
    pub fn to_ini(&self) -> OsString {
        let flag = |value: bool| OsString::from(if value { "1" } else { "0" });
        let entries = [
            (keys::SCREENSHOT_HOTKEY, OsString::from(&self.screenshot_hotkey)),
            (keys::RECORDING_HOTKEY, OsString::from(&self.recording_hotkey)),
            (keys::SCREENSHOT_FOLDER, self.screenshot_folder.clone().into_os_string()),
            (keys::RECORDING_FOLDER, self.recording_folder.clone().into_os_string()),
            (keys::MICROPHONE, flag(self.microphone)),
            (keys::SYSTEM_AUDIO, flag(self.system_audio)),
            (keys::SHOW_CLICKS, flag(self.show_clicks)),
            (keys::AUTOSTART, flag(self.autostart)),
            (keys::FPS, self.frame_rate.value().to_string().into()),
            (keys::COUNTDOWN, self.countdown.value().to_string().into()),
            (keys::APPEARANCE, self.appearance.value().to_string().into()),
            (keys::CHECK_UPDATES, flag(self.check_updates)),
            (keys::LANGUAGE, self.language.code().into()),
            (keys::SAVE_SCREENSHOTS, flag(self.save_screenshots)),
            (keys::SCREENSHOT_KEEP_DAYS, self.screenshot_keep_days.value().to_string().into()),
            (keys::SAVE_RECORDINGS, flag(self.save_recordings)),
            (keys::RECORDING_KEEP_DAYS, self.recording_keep_days.value().to_string().into()),
            (keys::SETTINGS_VERSION, SETTINGS_VERSION.to_string().into()),
        ];
        let mut ini = OsString::from(format!("\r\n[{SECTION}]\r\n"));
        for (key, value) in entries {
            ini.push(key);
            ini.push("=");
            ini.push(quote_if_needed(value));
            ini.push("\r\n");
        }
        ini
    }

    /// Points a folder that is still a default of Mosaik Capture (`legacy`) to the new
    /// default (`defaults`); a folder the user chose stays. `same_folder` decides whether
    /// two paths name the same folder (case, separators). Returns whether anything changed.
    /// Only the setting changes: no file is moved, the old folder may belong to another
    /// program by now.
    pub fn replace_legacy_folders(
        &mut self,
        legacy: &DefaultFolders,
        defaults: &DefaultFolders,
        same_folder: impl Fn(&Path, &Path) -> bool,
    ) -> bool {
        let mut changed = false;
        for (folder, old, new) in [
            (&mut self.screenshot_folder, &legacy.screenshots, &defaults.screenshots),
            (&mut self.recording_folder, &legacy.recordings, &defaults.recordings),
        ] {
            if same_folder(folder, old) {
                folder.clone_from(new);
                changed = true;
            }
        }
        changed
    }

    /// Parses both hotkeys and checks that they differ.
    pub fn hotkeys(&self) -> Result<Hotkeys, ConfigError> {
        let parse = |text: &str| text.parse::<Hotkey>().map_err(ConfigError::InvalidHotkey);
        let screenshot = parse(&self.screenshot_hotkey)?;
        let recording = parse(&self.recording_hotkey)?;
        if screenshot == recording {
            return Err(ConfigError::DuplicateHotkey);
        }
        Ok(Hotkeys { screenshot, recording })
    }

    /// Full validation: hotkeys first, then folders.
    pub fn validate(&self) -> Result<Hotkeys, ConfigError> {
        let hotkeys = self.hotkeys()?;
        if !is_valid_folder(&self.screenshot_folder) || !is_valid_folder(&self.recording_folder) {
            return Err(ConfigError::InvalidFolder);
        }
        Ok(hotkeys)
    }
}

/// Protects a value from the profile API's read-side cleanup.
///
/// `GetPrivateProfileStringW` trims spaces and tabs around a value and then removes
/// one pair of matching surrounding quotes (`"…"` or `'…'`). A value that would be
/// changed by that — leading or trailing whitespace, or already quoted — is written
/// in double quotes, so it reads back exactly as stored.
fn quote_if_needed(value: OsString) -> OsString {
    let units: Vec<u16> = value.encode_wide().collect();
    let is_blank = |unit: &u16| *unit == u16::from(b' ') || *unit == u16::from(b'\t');
    let is_quote = |unit: &u16| *unit == u16::from(b'"') || *unit == u16::from(b'\'');
    let padded = units.first().is_some_and(is_blank) || units.last().is_some_and(is_blank);
    let quoted =
        units.len() >= 2 && units.first().is_some_and(is_quote) && units.first() == units.last();
    if !padded && !quoted {
        return value;
    }
    let mut wrapped = OsString::from("\"");
    wrapped.push(value);
    wrapped.push("\"");
    wrapped
}

/// Whether `path` is an absolute drive path (`C:\…`, `C:/…`) or a UNC path (`\\…`)
/// of at most [`MAX_FOLDER_LEN`] units, without characters that Windows forbids in
/// file names.
pub fn is_valid_folder(path: &Path) -> bool {
    let units: Vec<u16> = path.as_os_str().encode_wide().collect();
    let is = |index: usize, c: u8| units.get(index) == Some(&u16::from(c));
    let drive = is(1, b':') && (is(2, b'\\') || is(2, b'/'));
    let unc = is(0, b'\\') && is(1, b'\\');
    let forbidden = |unit: &u16| FORBIDDEN_PATH_CHARS.iter().any(|&c| u16::from(c) == *unit);
    (3..=MAX_FOLDER_LEN).contains(&units.len()) && (drive || unc) && !units.iter().any(forbidden)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::self_test::FOLDER_CASES;
    use std::collections::HashMap;
    use std::os::windows::ffi::OsStringExt;

    fn folders() -> DefaultFolders {
        DefaultFolders::new(Path::new(r"C:\Users\Ada\Pictures"), Path::new(r"C:\Users\Ada\Videos"))
    }

    #[derive(Default)]
    struct MapSource {
        ints: HashMap<&'static str, i32>,
        strings: HashMap<&'static str, &'static str>,
    }

    impl ProfileSource for MapSource {
        fn string(&self, key: &str, default: &OsStr) -> OsString {
            self.strings.get(key).map_or_else(|| default.to_owned(), OsString::from)
        }
        fn int(&self, key: &str, default: i32) -> i32 {
            self.ints.get(key).copied().unwrap_or(default)
        }
    }

    #[test]
    fn default_folders_append_app_subfolders() {
        let folders = folders();
        assert_eq!(folders.screenshots, Path::new(r"C:\Users\Ada\Pictures\Nazar-Screenshots"));
        assert_eq!(folders.recordings, Path::new(r"C:\Users\Ada\Videos\Nazar-Aufnahmen"));
    }

    #[test]
    fn empty_profile_loads_defaults() {
        let config = Config::load(&MapSource::default(), &folders());
        assert_eq!(config, Config::defaults(&folders()));
        assert_eq!(config.screenshot_hotkey, "Shift+Alt+4");
        assert_eq!(config.recording_hotkey, "Shift+Alt+5");
        assert!(!config.microphone);
        assert!(config.system_audio && config.show_clicks && config.autostart);
        assert_eq!(config.frame_rate, FrameRate::Fps60);
        assert_eq!(config.countdown, Countdown::Seconds3);
        assert_eq!(config.appearance, Appearance::Light, "new users start light, as in Hams");
        assert!(config.check_updates);
        assert!(config.validate().is_ok());
    }

    #[test]
    fn stored_values_override_defaults() {
        let mut source = MapSource::default();
        source.strings.insert(keys::SCREENSHOT_HOTKEY, "Strg+F1");
        source.strings.insert(keys::RECORDING_FOLDER, "D:\\Videos");
        source.ints.insert(keys::MICROPHONE, 1);
        source.ints.insert(keys::SYSTEM_AUDIO, 0);
        source.ints.insert(keys::AUTOSTART, 7);
        source.ints.insert(keys::FPS, 30);
        source.ints.insert(keys::COUNTDOWN, 5);
        source.ints.insert(keys::APPEARANCE, 2);
        source.ints.insert(keys::CHECK_UPDATES, 0);
        let config = Config::load(&source, &folders());
        assert_eq!(config.screenshot_hotkey, "Strg+F1");
        assert_eq!(config.recording_folder, Path::new(r"D:\Videos"));
        assert!(config.microphone);
        assert!(!config.system_audio);
        assert!(config.autostart, "any non-zero value is true");
        assert_eq!(config.frame_rate, FrameRate::Fps30);
        assert_eq!(config.countdown, Countdown::Seconds5);
        assert_eq!(config.appearance, Appearance::Dark);
        assert!(!config.check_updates);
    }

    #[test]
    fn appearance_normalisation() {
        assert_eq!(Appearance::from_value(1), Appearance::Light);
        assert_eq!(Appearance::from_value(2), Appearance::Dark);
        for value in [0, 3, -1, 10, i32::MAX] {
            assert_eq!(Appearance::from_value(value), Appearance::Light, "{value}");
            assert!(!Appearance::is_choice(value), "{value}");
        }
        for appearance in [Appearance::Light, Appearance::Dark] {
            assert_eq!(Appearance::from_value(appearance.value()), appearance);
            assert!(Appearance::is_choice(appearance.value()));
        }
        assert_eq!(Appearance::like_windows(true), Appearance::Light);
        assert_eq!(Appearance::like_windows(false), Appearance::Dark);
    }

    #[test]
    fn language_codes_and_the_windows_language() {
        for language in Language::ALL {
            assert_eq!(Language::from_code(language.code()), language);
        }
        assert_eq!(Language::from_code(" EN "), Language::English);
        for unknown in ["", "deutsch", "en-US", "zz"] {
            assert_eq!(Language::from_code(unknown), Language::Auto, "{unknown:?}");
        }
        // Auto follows the Windows display language (primary language ids), else English.
        let auto = |primary| Language::Auto.resolve(primary);
        assert_eq!(auto(0x07), Language::German);
        assert_eq!(auto(0x19), Language::Russian);
        assert_eq!(auto(0x0A), Language::Spanish);
        assert_eq!(auto(0x0C), Language::French);
        assert_eq!(auto(0x01), Language::Arabic);
        assert_eq!(auto(0x1F), Language::Turkish);
        assert_eq!(auto(0x09), Language::English);
        assert_eq!(auto(0x11), Language::English, "Japanese: English");
        assert_eq!(Language::Turkish.resolve(0x07), Language::Turkish, "a choice stays");
    }

    #[test]
    fn keep_days_are_the_offered_choices_only() {
        for days in KeepDays::CHOICES {
            assert_eq!(KeepDays::from_value(days).value(), days);
        }
        for odd in [-1, 1, 6, 31, 366, i32::MAX] {
            assert_eq!(KeepDays::from_value(odd).value(), 0, "{odd}: unlimited, nothing deleted");
        }
    }

    #[test]
    fn frame_rate_normalisation() {
        assert_eq!(FrameRate::from_value(30), FrameRate::Fps30);
        for value in [0, 29, 31, 60, 120, -30, i32::MAX] {
            assert_eq!(FrameRate::from_value(value), FrameRate::Fps60, "{value}");
        }
        for rate in [FrameRate::Fps30, FrameRate::Fps60] {
            assert_eq!(FrameRate::from_value(rate.value()), rate);
        }
    }

    #[test]
    fn countdown_normalisation() {
        assert_eq!(Countdown::from_value(0), Countdown::Off);
        assert_eq!(Countdown::from_value(5), Countdown::Seconds5);
        for value in [1, 3, 4, 6, 10, -1, i32::MAX] {
            assert_eq!(Countdown::from_value(value), Countdown::Seconds3, "{value}");
        }
        for countdown in [Countdown::Off, Countdown::Seconds3, Countdown::Seconds5] {
            assert_eq!(Countdown::from_value(countdown.value()), countdown);
        }
    }

    #[test]
    fn to_ini_matches_profile_api_layout() {
        let mut config = Config::defaults(&folders());
        config.frame_rate = FrameRate::Fps30;
        config.countdown = Countdown::Off;
        let expected = "\r\n[capture]\r\n\
            screenshot_hotkey=Shift+Alt+4\r\n\
            recording_hotkey=Shift+Alt+5\r\n\
            screenshot_folder=C:\\Users\\Ada\\Pictures\\Nazar-Screenshots\r\n\
            recording_folder=C:\\Users\\Ada\\Videos\\Nazar-Aufnahmen\r\n\
            microphone=0\r\n\
            system_audio=1\r\n\
            show_clicks=1\r\n\
            autostart=1\r\n\
            fps=30\r\n\
            countdown=0\r\n\
            appearance=1\r\n\
            check_updates=1\r\n\
            language=auto\r\n\
            save_screenshots=1\r\n\
            screenshot_keep_days=0\r\n\
            save_recordings=1\r\n\
            recording_keep_days=0\r\n\
            settings_version=2\r\n";
        assert_eq!(config.to_ini(), expected);
    }

    #[test]
    fn old_default_folders_become_the_new_ones() {
        let legacy = DefaultFolders::legacy(
            Path::new(r"C:\Users\Ada\Pictures"),
            Path::new(r"C:\Users\Ada\Videos"),
        );
        assert_eq!(legacy.screenshots, Path::new(r"C:\Users\Ada\Pictures\Mosaiks-Screenshots"));
        assert_eq!(legacy.recordings, Path::new(r"C:\Users\Ada\Videos\Mosaiks-Aufnahmen"));
        let exact = |a: &Path, b: &Path| a == b;

        let mut config = Config::defaults(&folders());
        config.screenshot_folder.clone_from(&legacy.screenshots);
        config.recording_folder.clone_from(&legacy.recordings);
        assert!(config.replace_legacy_folders(&legacy, &folders(), exact));
        assert_eq!(config, Config::defaults(&folders()), "both point to the new defaults");
        assert!(!config.replace_legacy_folders(&legacy, &folders(), exact), "nothing left to do");

        let mut chosen = Config::defaults(&folders());
        chosen.screenshot_folder = PathBuf::from(r"D:\Mosaiks-Screenshots");
        chosen.recording_folder.clone_from(&legacy.recordings);
        assert!(chosen.replace_legacy_folders(&legacy, &folders(), exact));
        assert_eq!(chosen.screenshot_folder, Path::new(r"D:\Mosaiks-Screenshots"), "chosen: kept");
        assert_eq!(chosen.recording_folder, folders().recordings);
    }

    #[test]
    fn values_that_the_profile_api_would_alter_are_quoted() {
        let quote = |text: &str| quote_if_needed(OsString::from(text));
        assert_eq!(quote(r"C:\Bilder"), r"C:\Bilder", "ordinary values stay as they are");
        assert_eq!(quote(""), "");
        assert_eq!(quote(r"C:\Ordner "), r#""C:\Ordner ""#);
        assert_eq!(quote(" Alt+4"), r#"" Alt+4""#);
        assert_eq!(quote("\tx"), "\"\tx\"");
        assert_eq!(quote(r#""x""#), r#"""x"""#);
        assert_eq!(quote("'x'"), r#""'x'""#);
        assert_eq!(quote(r#""x"#), r#""x"#, "an unmatched quote is not removed on read");
        assert_eq!(quote("'"), "'");
    }

    #[test]
    fn hotkey_validation() {
        let mut config = Config::defaults(&folders());
        let hotkeys = config.hotkeys().unwrap();
        assert_eq!(hotkeys.screenshot.key, u16::from(b'4'));
        assert_eq!(hotkeys.recording.key, u16::from(b'5'));

        config.recording_hotkey = "Alt + Shift + 4".to_owned();
        assert_eq!(config.hotkeys(), Err(ConfigError::DuplicateHotkey));

        config.recording_hotkey = "Alt+".to_owned();
        assert!(matches!(config.hotkeys(), Err(ConfigError::InvalidHotkey(_))));
    }

    #[test]
    fn validation_checks_hotkeys_before_folders() {
        let mut config = Config::defaults(&folders());
        config.screenshot_folder = PathBuf::from("relative");
        assert_eq!(config.validate(), Err(ConfigError::InvalidFolder));
        config.screenshot_hotkey = "nonsense".to_owned();
        assert!(matches!(config.validate(), Err(ConfigError::InvalidHotkey(_))));
        config.screenshot_hotkey = DEFAULT_SCREENSHOT_HOTKEY.to_owned();
        config.screenshot_folder = folders().screenshots;
        config.recording_folder = PathBuf::from(r"C:\a|b");
        assert_eq!(config.validate(), Err(ConfigError::InvalidFolder));
    }

    #[test]
    fn folder_self_test_cases_hold() {
        for &(path, expected) in FOLDER_CASES {
            assert_eq!(is_valid_folder(Path::new(path)), expected, "{path:?}");
        }
    }

    #[test]
    fn folder_validation() {
        let valid = [
            "C:\\",
            "c:/",
            "D:\\Bilder",
            "Z:/Videos/Neu",
            "\\\\server\\share",
            "\\\\server",
            "C:\\Ordner mit Leerzeichen\\ä",
        ];
        for path in valid {
            assert!(is_valid_folder(Path::new(path)), "{path:?}");
        }
        let invalid = [
            "",
            "C:",
            "C:x",
            "\\\\",
            "\\Ordner",
            "/Ordner",
            "Ordner\\C:\\",
            "C:\\a\rb",
            "C:\\\"quoted\"",
            "C:\\*",
            "C:\\a<b",
            "C:\\a>b",
            "C:\\what?",
            "C:\\a|b",
        ];
        for path in invalid {
            assert!(!is_valid_folder(Path::new(path)), "{path:?}");
        }
        // Unpaired surrogates are legal in Windows file names and stay valid.
        let odd =
            OsString::from_wide(&[u16::from(b'C'), u16::from(b':'), u16::from(b'\\'), 0xD800]);
        assert!(is_valid_folder(Path::new(&odd)));
        let odd_forbidden =
            OsString::from_wide(&[u16::from(b'C'), u16::from(b':'), 0x5C, 0xD800, 0x3F]);
        assert!(!is_valid_folder(Path::new(&odd_forbidden)));
    }

    #[test]
    fn folder_length_is_capped_at_the_long_path_limit() {
        let longest = format!(r"C:\{}", "x".repeat(MAX_FOLDER_LEN - 3));
        assert!(is_valid_folder(Path::new(&longest)));
        assert!(!is_valid_folder(Path::new(&format!("{longest}x"))));
    }
}
