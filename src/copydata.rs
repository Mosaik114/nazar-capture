//! The settings protocol between the engine's settings window and the host.
//!
//! The engine shows the settings, but the host stays the only writer of
//! `settings.ini` and the owner of the global hotkeys. Every change is sent as a
//! `WM_COPYDATA` (see `host.copydata` in the contract): `dwData` is the request, the
//! payload is UTF-16 text without terminator, and the reply is the message result.
//!
//! * [`SET`]: `key TAB value` — validate, apply at once, save shortly afterwards.
//! * [`CHECK_HOTKEY`]: `key TAB combination` — could this hotkey be used for that key?
//! * [`SUSPEND_HOTKEYS`]: `1` releases both hotkeys while the engine records a new
//!   one (so pressing the current combination does not start a capture), `0` takes
//!   them back.
//!
//! Everything here is pure: [`parse`] turns a message into a [`Request`], and
//! [`plan_set`] and [`plan_check`] decide what a request means for the configuration.
//! Registering hotkeys, writing the autostart entry and saving happen in the UI.

use std::ffi::OsString;
use std::os::windows::ffi::OsStringExt;
use std::path::PathBuf;

use crate::config::{Appearance, Config, Countdown, FrameRate, is_valid_folder, keys};
use crate::hotkey::Hotkey;

/// `dwData` of a request to change one setting.
pub const SET: usize = 1;
/// `dwData` of a request to check whether a hotkey could be used.
pub const CHECK_HOTKEY: usize = 2;
/// `dwData` of a request to release (`1`) or take back (`0`) both hotkeys.
pub const SUSPEND_HOTKEYS: usize = 3;
/// Separates key and value in the payload.
pub const SEPARATOR: char = '\t';

/// The answer to a request, returned as the message result.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Reply {
    /// Unknown request or key, malformed payload, or the host is still starting.
    NotUnderstood = 0,
    Ok = 1,
    /// The value cannot be read (e.g. a hotkey that does not parse).
    Invalid = 2,
    /// Another program has registered the hotkey; the previous one stays active.
    Taken = 3,
    /// The hotkey is the one of the other action.
    Duplicate = 4,
    /// The change is in effect, but writing `settings.ini` failed.
    SaveFailed = 5,
    /// The folder is not a complete, valid path.
    FolderUnusable = 6,
    /// The autostart entry could not be written; the setting is unchanged.
    AutostartFailed = 7,
}

impl Reply {
    /// The value returned from the window procedure.
    pub const fn to_lresult(self) -> isize {
        self as isize
    }
}

/// Which of the two hotkeys a request is about.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum HotkeyAction {
    Screenshot,
    /// Start or stop a recording.
    Recording,
}

impl HotkeyAction {
    pub const BOTH: [Self; 2] = [Self::Screenshot, Self::Recording];

    /// The other action, whose hotkey must differ.
    pub const fn other(self) -> Self {
        match self {
            Self::Screenshot => Self::Recording,
            Self::Recording => Self::Screenshot,
        }
    }

    /// The settings key holding this action's hotkey.
    pub const fn key(self) -> &'static str {
        match self {
            Self::Screenshot => keys::SCREENSHOT_HOTKEY,
            Self::Recording => keys::RECORDING_HOTKEY,
        }
    }

    /// This action's hotkey text in `config`.
    pub fn text(self, config: &Config) -> &str {
        match self {
            Self::Screenshot => &config.screenshot_hotkey,
            Self::Recording => &config.recording_hotkey,
        }
    }

    /// This action's hotkey in `config`, if it parses.
    pub fn parsed(self, config: &Config) -> Option<Hotkey> {
        self.text(config).parse().ok()
    }

    fn from_key(key: &str) -> Option<Self> {
        Self::BOTH.into_iter().find(|action| action.key() == key)
    }
}

/// One changed setting, already decoded but not yet validated.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Setting {
    Hotkey(HotkeyAction, String),
    ScreenshotFolder(PathBuf),
    RecordingFolder(PathBuf),
    Microphone(bool),
    SystemAudio(bool),
    ShowClicks(bool),
    Autostart(bool),
    CheckUpdates(bool),
    FrameRate(FrameRate),
    Countdown(Countdown),
    Appearance(Appearance),
}

/// A decoded `WM_COPYDATA` request.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Request {
    Set(Setting),
    CheckHotkey(HotkeyAction, String),
    /// `true`: release both hotkeys; `false`: register them again.
    SuspendHotkeys(bool),
}

/// Decodes a request from its `dwData` and payload. A malformed message is
/// [`Reply::NotUnderstood`]; a known key with an unreadable value is
/// [`Reply::Invalid`] (or [`Reply::FolderUnusable`] for a folder).
pub fn parse(request: usize, payload: &[u16]) -> Result<Request, Reply> {
    // Tolerate a terminator, although the contract sends none.
    let payload = payload.strip_suffix(&[0]).unwrap_or(payload);
    match request {
        SET => {
            let (key, value) = split(payload)?;
            parse_setting(&key, value).map(Request::Set)
        }
        CHECK_HOTKEY => {
            let (key, value) = split(payload)?;
            let action = HotkeyAction::from_key(&key).ok_or(Reply::NotUnderstood)?;
            Ok(Request::CheckHotkey(action, text(value)?))
        }
        SUSPEND_HOTKEYS => match payload {
            [unit] if *unit == u16::from(b'1') => Ok(Request::SuspendHotkeys(true)),
            [unit] if *unit == u16::from(b'0') => Ok(Request::SuspendHotkeys(false)),
            _ => Err(Reply::NotUnderstood),
        },
        _ => Err(Reply::NotUnderstood),
    }
}

/// Splits `key TAB value`; the key is ASCII, the value stays raw UTF-16.
fn split(payload: &[u16]) -> Result<(String, &[u16]), Reply> {
    let separator = u16::try_from(u32::from(SEPARATOR)).unwrap_or_default();
    let position =
        payload.iter().position(|&unit| unit == separator).ok_or(Reply::NotUnderstood)?;
    let key = String::from_utf16(&payload[..position]).map_err(|_| Reply::NotUnderstood)?;
    Ok((key, &payload[position + 1..]))
}

/// A value as text; invalid UTF-16 or an embedded NUL cannot be a valid value.
fn text(value: &[u16]) -> Result<String, Reply> {
    if value.contains(&0) {
        return Err(Reply::Invalid);
    }
    String::from_utf16(value).map_err(|_| Reply::Invalid)
}

/// A decimal 32-bit integer, read as the store reads numbers.
fn number(value: &[u16]) -> Result<i32, Reply> {
    text(value)?.parse().map_err(|_| Reply::Invalid)
}

fn parse_setting(key: &str, value: &[u16]) -> Result<Setting, Reply> {
    if let Some(action) = HotkeyAction::from_key(key) {
        return Ok(Setting::Hotkey(action, text(value)?));
    }
    let folder = || {
        // Kept losslessly: unpaired surrogates are legal in Windows names. A NUL
        // would cut the path short when it is written.
        if value.contains(&0) {
            return Err(Reply::FolderUnusable);
        }
        Ok(PathBuf::from(OsString::from_wide(value)))
    };
    let flag = || number(value).map(|n| n != 0);
    Ok(match key {
        keys::SCREENSHOT_FOLDER => Setting::ScreenshotFolder(folder()?),
        keys::RECORDING_FOLDER => Setting::RecordingFolder(folder()?),
        keys::MICROPHONE => Setting::Microphone(flag()?),
        keys::SYSTEM_AUDIO => Setting::SystemAudio(flag()?),
        keys::SHOW_CLICKS => Setting::ShowClicks(flag()?),
        keys::AUTOSTART => Setting::Autostart(flag()?),
        keys::CHECK_UPDATES => Setting::CheckUpdates(flag()?),
        keys::FPS => Setting::FrameRate(FrameRate::from_value(number(value)?)),
        keys::COUNTDOWN => Setting::Countdown(Countdown::from_value(number(value)?)),
        keys::APPEARANCE => Setting::Appearance(Appearance::from_value(number(value)?)),
        _ => return Err(Reply::NotUnderstood),
    })
}

/// What has to happen outside the configuration for a change to take effect.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Effect {
    /// Nothing: update the configuration and save.
    None,
    /// Register this hotkey for the action (unless it is already the registered one).
    Hotkey(HotkeyAction, Hotkey),
    /// Write (or remove) the autostart entry.
    Autostart(bool),
}

/// A validated change: the configuration after it and what else it needs.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Change {
    pub config: Config,
    pub effect: Effect,
}

/// Validates `setting` against `config` and returns the changed configuration.
///
/// Hotkeys must parse ([`Reply::Invalid`]) and differ from the other action's
/// ([`Reply::Duplicate`]); folders follow [`is_valid_folder`]
/// ([`Reply::FolderUnusable`]); numbers are normalised like stored ones.
pub fn plan_set(config: &Config, setting: Setting) -> Result<Change, Reply> {
    let mut changed = config.clone();
    let mut effect = Effect::None;
    match setting {
        Setting::Hotkey(action, text) => {
            let hotkey = text.parse::<Hotkey>().map_err(|_| Reply::Invalid)?;
            if action.other().parsed(config) == Some(hotkey) {
                return Err(Reply::Duplicate);
            }
            match action {
                HotkeyAction::Screenshot => changed.screenshot_hotkey = text,
                HotkeyAction::Recording => changed.recording_hotkey = text,
            }
            effect = Effect::Hotkey(action, hotkey);
        }
        Setting::ScreenshotFolder(folder) | Setting::RecordingFolder(folder)
            if !is_valid_folder(&folder) =>
        {
            return Err(Reply::FolderUnusable);
        }
        Setting::ScreenshotFolder(folder) => changed.screenshot_folder = folder,
        Setting::RecordingFolder(folder) => changed.recording_folder = folder,
        Setting::Microphone(on) => changed.microphone = on,
        Setting::SystemAudio(on) => changed.system_audio = on,
        Setting::ShowClicks(on) => changed.show_clicks = on,
        Setting::Autostart(on) => {
            changed.autostart = on;
            effect = Effect::Autostart(on);
        }
        Setting::CheckUpdates(on) => changed.check_updates = on,
        Setting::FrameRate(rate) => changed.frame_rate = rate,
        Setting::Countdown(countdown) => changed.countdown = countdown,
        Setting::Appearance(appearance) => changed.appearance = appearance,
    }
    Ok(Change { config: changed, effect })
}

/// The outcome of checking a hotkey before trying it with Windows.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Check {
    /// Answered without asking Windows.
    Done(Reply),
    /// Free unless another program has it: register it briefly to find out.
    TryRegister(Hotkey),
}

/// Checks `text` as the new hotkey of `action`: it must parse and differ from the
/// other action's hotkey; the hotkey this action has registered right now is fine.
pub fn plan_check(
    config: &Config,
    registered: Option<Hotkey>,
    action: HotkeyAction,
    text: &str,
) -> Check {
    let Ok(hotkey) = text.parse::<Hotkey>() else {
        return Check::Done(Reply::Invalid);
    };
    if action.other().parsed(config) == Some(hotkey) {
        Check::Done(Reply::Duplicate)
    } else if registered == Some(hotkey) {
        Check::Done(Reply::Ok)
    } else {
        Check::TryRegister(hotkey)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::DefaultFolders;
    use std::path::Path;

    fn wide(text: &str) -> Vec<u16> {
        text.encode_utf16().collect()
    }

    fn config() -> Config {
        Config::defaults(&DefaultFolders::new(Path::new(r"C:\Bilder"), Path::new(r"C:\Videos")))
    }

    fn set(payload: &str) -> Result<Request, Reply> {
        parse(SET, &wide(payload))
    }

    #[test]
    fn requests_are_decoded() {
        assert_eq!(
            set("screenshot_hotkey\tStrg+Druck"),
            Ok(Request::Set(Setting::Hotkey(HotkeyAction::Screenshot, "Strg+Druck".into())))
        );
        assert_eq!(
            parse(CHECK_HOTKEY, &wide("recording_hotkey\tAlt+F9")),
            Ok(Request::CheckHotkey(HotkeyAction::Recording, "Alt+F9".into()))
        );
        assert_eq!(parse(SUSPEND_HOTKEYS, &wide("1")), Ok(Request::SuspendHotkeys(true)));
        assert_eq!(parse(SUSPEND_HOTKEYS, &wide("0")), Ok(Request::SuspendHotkeys(false)));
        assert_eq!(set("fps\t30\0"), Ok(Request::Set(Setting::FrameRate(FrameRate::Fps30))));
    }

    #[test]
    fn every_settings_key_can_be_set() {
        let cases = [
            ("screenshot_hotkey\tAlt+1", Setting::Hotkey(HotkeyAction::Screenshot, "Alt+1".into())),
            ("recording_hotkey\tAlt+2", Setting::Hotkey(HotkeyAction::Recording, "Alt+2".into())),
            ("screenshot_folder\tD:\\Bilder", Setting::ScreenshotFolder(r"D:\Bilder".into())),
            ("recording_folder\t\\\\nas\\v", Setting::RecordingFolder(r"\\nas\v".into())),
            ("microphone\t1", Setting::Microphone(true)),
            ("system_audio\t0", Setting::SystemAudio(false)),
            ("show_clicks\t7", Setting::ShowClicks(true)),
            ("autostart\t0", Setting::Autostart(false)),
            ("check_updates\t0", Setting::CheckUpdates(false)),
            ("fps\t60", Setting::FrameRate(FrameRate::Fps60)),
            ("countdown\t5", Setting::Countdown(Countdown::Seconds5)),
            ("appearance\t2", Setting::Appearance(Appearance::Dark)),
        ];
        for (payload, setting) in cases {
            assert_eq!(set(payload), Ok(Request::Set(setting)), "{payload:?}");
        }
    }

    #[test]
    fn choices_are_normalised_like_stored_values() {
        assert_eq!(set("fps\t45"), Ok(Request::Set(Setting::FrameRate(FrameRate::Fps60))));
        assert_eq!(set("countdown\t4"), Ok(Request::Set(Setting::Countdown(Countdown::Seconds3))));
        assert_eq!(set("appearance\t9"), Ok(Request::Set(Setting::Appearance(Appearance::System))));
        assert_eq!(set("microphone\t+1"), Ok(Request::Set(Setting::Microphone(true))));
    }

    #[test]
    fn malformed_messages_are_not_understood() {
        let not_understood = Err(Reply::NotUnderstood);
        assert_eq!(parse(0, &wide("fps\t30")), not_understood);
        assert_eq!(parse(4, &wide("fps\t30")), not_understood);
        assert_eq!(set("fps=30"), not_understood, "no separator");
        assert_eq!(set(""), not_understood);
        assert_eq!(set("frame_rate\t30"), not_understood, "unknown key");
        assert_eq!(set("FPS\t30"), not_understood, "keys are exact");
        assert_eq!(parse(CHECK_HOTKEY, &wide("fps\t30")), not_understood, "not a hotkey key");
        assert_eq!(parse(SUSPEND_HOTKEYS, &wide("2")), not_understood);
        assert_eq!(parse(SUSPEND_HOTKEYS, &wide("")), not_understood);
        assert_eq!(parse(SET, &[0xD800, 0x09, 0x31]), not_understood, "key is not text");
    }

    #[test]
    fn unreadable_values_are_invalid() {
        for payload in ["fps\t", "fps\tsechzig", "countdown\t5.0", "microphone\tyes"] {
            assert_eq!(set(payload), Err(Reply::Invalid), "{payload:?}");
        }
        let mut broken = wide("screenshot_hotkey\t");
        broken.push(0xDC00);
        assert_eq!(parse(SET, &broken), Err(Reply::Invalid), "unpaired surrogate");
        assert_eq!(set("microphone\t1\u{0}2"), Err(Reply::Invalid), "embedded NUL");
        assert_eq!(set("screenshot_folder\tC:\\a\0b"), Err(Reply::FolderUnusable));
    }

    #[test]
    fn folders_keep_every_utf16_unit() {
        let mut payload = wide("screenshot_folder\tC:\\");
        payload.push(0xD800);
        let Ok(Request::Set(Setting::ScreenshotFolder(folder))) = parse(SET, &payload) else {
            panic!("folder not decoded");
        };
        assert_eq!(folder.as_os_str(), &OsString::from_wide(&payload[18..]));
    }

    #[test]
    fn hotkeys_are_validated() {
        let config = config();
        let hotkey = |action, text: &str| plan_set(&config, Setting::Hotkey(action, text.into()));
        assert_eq!(hotkey(HotkeyAction::Screenshot, "Alt+"), Err(Reply::Invalid));
        assert_eq!(hotkey(HotkeyAction::Screenshot, "Shift+R"), Err(Reply::Invalid));
        assert_eq!(hotkey(HotkeyAction::Screenshot, "Alt + Umschalt + 5"), Err(Reply::Duplicate));
        let change = hotkey(HotkeyAction::Recording, "Druck").unwrap();
        assert_eq!(change.config.recording_hotkey, "Druck", "stored as entered");
        assert_eq!(change.config.screenshot_hotkey, config.screenshot_hotkey);
        let print = "Druck".parse().unwrap();
        assert_eq!(change.effect, Effect::Hotkey(HotkeyAction::Recording, print));
        let same = hotkey(HotkeyAction::Screenshot, "Alt+Shift+4").unwrap();
        assert_eq!(same.config.screenshot_hotkey, "Alt+Shift+4", "a new spelling is kept");
    }

    #[test]
    fn folders_are_validated() {
        let config = config();
        for folder in ["relativ", r"C:\a|b", "", r"\Ordner"] {
            let setting = Setting::ScreenshotFolder(folder.into());
            assert_eq!(plan_set(&config, setting), Err(Reply::FolderUnusable), "{folder:?}");
            let setting = Setting::RecordingFolder(folder.into());
            assert_eq!(plan_set(&config, setting), Err(Reply::FolderUnusable), "{folder:?}");
        }
        let change = plan_set(&config, Setting::RecordingFolder(r"D:\Filme".into())).unwrap();
        assert_eq!(change.config.recording_folder, Path::new(r"D:\Filme"));
        assert_eq!(change.effect, Effect::None);
    }

    #[test]
    fn plain_settings_change_only_their_field() {
        type Edit = fn(&mut Config);
        let config = config();
        let cases: [(Setting, Edit); 8] = [
            (Setting::Microphone(true), |c| c.microphone = true),
            (Setting::SystemAudio(false), |c| c.system_audio = false),
            (Setting::ShowClicks(false), |c| c.show_clicks = false),
            (Setting::CheckUpdates(false), |c| c.check_updates = false),
            (Setting::FrameRate(FrameRate::Fps30), |c| c.frame_rate = FrameRate::Fps30),
            (Setting::Countdown(Countdown::Off), |c| c.countdown = Countdown::Off),
            (Setting::Appearance(Appearance::Light), |c| c.appearance = Appearance::Light),
            (Setting::ScreenshotFolder(r"E:\S".into()), |c| c.screenshot_folder = r"E:\S".into()),
        ];
        for (setting, edit) in cases {
            let mut expected = config.clone();
            edit(&mut expected);
            let change = plan_set(&config, setting.clone()).unwrap();
            assert_eq!(change, Change { config: expected, effect: Effect::None }, "{setting:?}");
        }
        let autostart = plan_set(&config, Setting::Autostart(false)).unwrap();
        assert!(!autostart.config.autostart);
        assert_eq!(autostart.effect, Effect::Autostart(false));
    }

    #[test]
    fn hotkey_checks() {
        let config = config();
        let screenshot: Hotkey = config.screenshot_hotkey.parse().unwrap();
        let check = |registered, action, text| plan_check(&config, registered, action, text);
        let shot = HotkeyAction::Screenshot;
        assert_eq!(check(Some(screenshot), shot, "Alt+"), Check::Done(Reply::Invalid));
        assert_eq!(check(Some(screenshot), shot, "Alt+Shift+5"), Check::Done(Reply::Duplicate));
        assert_eq!(check(Some(screenshot), shot, "Umschalt+Alt+4"), Check::Done(Reply::Ok));
        assert_eq!(
            check(None, shot, "Shift+Alt+4"),
            Check::TryRegister(screenshot),
            "not registered (taken at start-up): ask Windows again"
        );
        let other = "Win+Shift+S".parse().unwrap();
        assert_eq!(check(Some(screenshot), shot, "Win+Shift+S"), Check::TryRegister(other));
        let recording = HotkeyAction::Recording;
        assert_eq!(check(None, recording, "Shift+Alt+4"), Check::Done(Reply::Duplicate));
    }

    #[test]
    fn actions_and_replies() {
        assert_eq!(HotkeyAction::Screenshot.other(), HotkeyAction::Recording);
        assert_eq!(HotkeyAction::Recording.other(), HotkeyAction::Screenshot);
        assert_eq!(HotkeyAction::Recording.key(), "recording_hotkey");
        let codes = [
            Reply::NotUnderstood,
            Reply::Ok,
            Reply::Invalid,
            Reply::Taken,
            Reply::Duplicate,
            Reply::SaveFailed,
            Reply::FolderUnusable,
            Reply::AutostartFailed,
        ];
        for (code, reply) in codes.into_iter().enumerate() {
            assert_eq!(reply.to_lresult(), isize::try_from(code).unwrap(), "{reply:?}");
        }
    }
}
