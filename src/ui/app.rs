//! Application state and the behaviour behind every window message.
//!
//! [`App`] is boxed by [`super::run`] for the whole message loop (so its address is
//! stable) and reached from the window procedure through `GWLP_USERDATA`. Window
//! procedures are re-entered (e.g. `SendMessage` inside a handler, the tray menu's
//! modal loop), so all methods take `&self`; mutable state sits in `Cell`/`RefCell`,
//! and `RefCell` borrows are not held across Win32 calls that can dispatch messages to
//! this window.
//!
//! The window is hidden and never shown: the settings window belongs to the engine,
//! which sends every change here (`WM_COPYDATA`, see [`crate::copydata`]). Changes
//! take effect at once and are written to `settings.ini` [`SAVE_DELAY_MS`] after the
//! last one, so a burst of changes is one write. Pending changes are written before a
//! request goes to the engine (which reads the file) and when the app ends.
//!
//! Session end: `WM_QUERYENDSESSION` is left to the default (logoff is never
//! blocked). On `WM_ENDSESSION` pending settings are written and the hotkeys and the
//! tray icon are released; the engine handles the session end of its own process.

use std::cell::{Cell, RefCell};
use std::io;
use std::path::{Path, PathBuf};
use std::{ptr, slice};

use windows_sys::Win32::Foundation::{HINSTANCE, HWND, LPARAM, LRESULT, WPARAM};
use windows_sys::Win32::System::DataExchange::COPYDATASTRUCT;
use windows_sys::Win32::UI::HiDpi::GetDpiForSystem;
use windows_sys::Win32::UI::Input::KeyboardAndMouse::{
    MOD_NOREPEAT, RegisterHotKey, UnregisterHotKey,
};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    DestroyWindow, IsWindow, KillTimer, PostQuitMessage, RegisterWindowMessageW, SM_CXSMICON,
    SetTimer, WM_COPYDATA, WM_DESTROY, WM_ENDSESSION, WM_HOTKEY, WM_TIMER,
};
use windows_sys::w;

use super::icons::OwnedIcon;
use super::strings::{self, Balloons};
use super::tray::{self, MenuCommand, MenuHotkeys, TrayEvent, TrayState};
use crate::autostart;
use crate::cli::Action;
use crate::config::{Config, ConfigError};
use crate::copydata::{self, Check, Effect, HotkeyAction, Reply, Request};
use crate::engine::{Engine, EngineRequest};
use crate::hotkey::{Hotkey, ParseHotkeyError};
use crate::ipc::{self, AudioSettings, ProbeResponse};
use crate::isolation::Isolation;
use crate::store::SettingsStore;

/// How often a configured hotkey that could not be registered is tried again.
pub const HOTKEY_RETRY_SECONDS: u32 = 30;
/// How long after the last change the settings are written.
pub const SAVE_DELAY_MS: u32 = 300;

/// `RegisterHotKey` ids (they arrive as `WM_HOTKEY`'s `wParam`).
const HOTKEY_SCREENSHOT: i32 = 1;
const HOTKEY_RECORDING: i32 = 2;
/// Registered only for a moment, to find out whether a hotkey is free.
const HOTKEY_CHECK: i32 = 3;
/// Timer that retries adding the tray icon while the taskbar is not ready.
const TRAY_RETRY_TIMER: usize = 1;
const TRAY_RETRY_MS: u32 = 2000;
/// How often adding the tray icon is retried (about a minute in total).
const TRAY_RETRIES: u32 = 30;
/// Timer that writes pending settings.
pub(super) const SAVE_TIMER: usize = 2;
/// Timer that retries registering hotkeys another program had taken.
pub(super) const HOTKEY_RETRY_TIMER: usize = 3;
/// One-shot timer that starts an idle engine a few seconds after the host started (a fast first screenshot).
pub(super) const PRESTART_TIMER: usize = 4;
/// Delay of [`PRESTART_TIMER`]: logon has calmed down, and the first hotkey usually comes later.
const PRESTART_MS: u32 = 5000;

/// The hotkey id of an action.
const fn hotkey_id(action: HotkeyAction) -> i32 {
    match action {
        HotkeyAction::Screenshot => HOTKEY_SCREENSHOT,
        HotkeyAction::Recording => HOTKEY_RECORDING,
    }
}

/// Which hotkey is registered for each action right now.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
struct Registered {
    screenshot: Option<Hotkey>,
    recording: Option<Hotkey>,
}

impl Registered {
    const fn get(self, action: HotkeyAction) -> Option<Hotkey> {
        match action {
            HotkeyAction::Screenshot => self.screenshot,
            HotkeyAction::Recording => self.recording,
        }
    }

    const fn with(mut self, action: HotkeyAction, hotkey: Option<Hotkey>) -> Self {
        match action {
            HotkeyAction::Screenshot => self.screenshot = hotkey,
            HotkeyAction::Recording => self.recording = hotkey,
        }
        self
    }

    /// The answer to [`ipc::PROBE`]: the real state, not the configured one.
    const fn probe(self) -> ProbeResponse {
        ProbeResponse {
            screenshot_hotkey: self.screenshot.is_some(),
            recording_hotkey: self.recording.is_some(),
        }
    }
}

/// The configured hotkeys that are not registered, while hotkeys are not suspended.
/// A configuration with an invalid or duplicate hotkey registers nothing (the user is
/// told at start-up and fixes it in the settings window).
fn missing_hotkeys(
    config: &Config,
    registered: Registered,
    suspended: bool,
) -> Vec<(HotkeyAction, Hotkey)> {
    if suspended {
        return Vec::new();
    }
    let Ok(hotkeys) = config.hotkeys() else {
        return Vec::new();
    };
    [(HotkeyAction::Screenshot, hotkeys.screenshot), (HotkeyAction::Recording, hotkeys.recording)]
        .into_iter()
        .filter(|&(action, hotkey)| registered.get(action) != Some(hotkey))
        .collect()
}

/// Decodes the payload of a `WM_COPYDATA` (UTF-16LE bytes, possibly unaligned);
/// `None` for an odd number of bytes.
fn utf16_units(bytes: &[u8]) -> Option<Vec<u16>> {
    bytes
        .len()
        .is_multiple_of(2)
        .then(|| bytes.chunks_exact(2).map(|pair| u16::from_le_bytes([pair[0], pair[1]])).collect())
}

#[derive(Debug)]
pub struct App {
    window: Cell<HWND>,
    instance: HINSTANCE,
    /// Notification-area icon, sized for the taskbar (system DPI).
    tray_icon: OwnedIcon,
    tray_retries: Cell<u32>,
    tray_state: Cell<TrayState>,
    /// A message for the user that waits for the tray icon.
    pending_balloon: Cell<Option<&'static str>>,
    store: SettingsStore,
    /// This executable, for the autostart entry; `None` if Windows could not tell.
    exe: Option<PathBuf>,
    engine: Engine,
    /// Test isolation: autostart is never touched.
    isolated: bool,
    /// The settings in effect (possibly not written yet, see `save_pending`).
    config: RefCell<Config>,
    /// Which hotkeys are currently registered.
    registered: Cell<Registered>,
    /// The engine is recording a hotkey: both are released and not retried.
    suspended: Cell<bool>,
    /// `config` has changes that are not written yet.
    save_pending: Cell<bool>,
    /// The last delayed write failed; the next change is written at once, so the
    /// settings window learns about it.
    save_failed: Cell<bool>,
    /// Message id of Explorer's `TaskbarCreated` broadcast.
    taskbar_created: Cell<u32>,
    /// Start-up has finished; requests from outside are handled only from then on.
    started: Cell<bool>,
    /// Number of modal loops (the tray menu) currently running.
    modal_depth: Cell<u32>,
    /// A request for the engine that came in while a modal loop ran; delivered when
    /// it ends.
    pending_request: Cell<Option<EngineRequest>>,
}

/// What the engine shows when the host starts with `action`; `healthy` is false when
/// the settings are unusable or a hotkey is taken. A silent start (`--background`, logon)
/// never shows anything: only the tray icon, no window, no focus taken (as Hams).
const fn startup_request(action: Action, healthy: bool) -> Option<EngineRequest> {
    match action {
        Action::Background | Action::Quit | Action::Probe => None,
        Action::ShowRecordingSettings => Some(EngineRequest::RecordingSettings),
        Action::ShowSettings => Some(EngineRequest::Settings),
        _ if !healthy => Some(EngineRequest::Settings),
        Action::ShowMain => Some(EngineRequest::Main),
        Action::TakeScreenshot => Some(EngineRequest::Screenshot),
        Action::ToggleRecording => Some(EngineRequest::Recording),
    }
}

impl App {
    /// Creates the state; the window is attached later by [`App::attach`].
    pub fn new(
        instance: HINSTANCE,
        store: SettingsStore,
        config: Config,
        exe: Option<PathBuf>,
        isolation: Option<&Isolation>,
    ) -> Self {
        let exe_dir = exe.as_deref().and_then(Path::parent).map(PathBuf::from).unwrap_or_default();
        Self {
            window: Cell::new(ptr::null_mut()),
            instance,
            // SAFETY: no arguments.
            tray_icon: OwnedIcon::load(instance, SM_CXSMICON, unsafe { GetDpiForSystem() }),
            tray_retries: Cell::new(TRAY_RETRIES),
            tray_state: Cell::new(TrayState::Missing),
            pending_balloon: Cell::new(None),
            store,
            exe,
            engine: Engine::new(&exe_dir, isolation),
            isolated: isolation.is_some(),
            config: RefCell::new(config),
            registered: Cell::new(Registered::default()),
            suspended: Cell::new(false),
            save_pending: Cell::new(false),
            save_failed: Cell::new(false),
            taskbar_created: Cell::new(0),
            started: Cell::new(false),
            modal_depth: Cell::new(0),
            pending_request: Cell::new(None),
        }
    }

    /// Records the window handle (called while the window is being created).
    pub fn attach(&self, window: HWND) {
        self.window.set(window);
    }

    /// Forgets the window handle (called on `WM_NCDESTROY`, when it becomes invalid).
    pub fn detach(&self) {
        self.window.set(ptr::null_mut());
    }

    pub fn window(&self) -> HWND {
        self.window.get()
    }

    pub const fn instance(&self) -> HINSTANCE {
        self.instance
    }

    /// The settings in effect.
    #[cfg(test)]
    pub fn config(&self) -> Config {
        self.config.borrow().clone()
    }

    /// Lets requests in without the rest of start-up (no hotkeys, tray or files), so
    /// tests can drive the message handling without global side effects.
    #[cfg(test)]
    pub fn accept_requests(&self) {
        self.started.set(true);
    }

    /// Registers the hotkeys, writes the (normalised) settings back, adds the tray
    /// icon and performs the start-up `action`. If the stored settings are unusable or
    /// a hotkey is taken, the user is told and the settings window opens, except at a
    /// silent start (`--background`, logon): then only the tray icon appears, as in Hams;
    /// the settings show the problem in its row when they are opened.
    ///
    /// Requests from other processes that arrive meanwhile are answered with 0 (see
    /// [`ipc::needs_startup`]); they are accepted once this returns.
    pub fn start(&self, action: Action) {
        let config = self.config.borrow().clone();
        let valid = config.validate();
        let all_registered = self.register_missing() && config.hotkeys().is_ok();
        let autostart = self.write_autostart(config.autostart);
        let saved = self.store.save(&config).is_ok() && !matches!(autostart, Some(Err(_)));

        // SAFETY: the literal is NUL-terminated.
        self.taskbar_created.set(unsafe { RegisterWindowMessageW(w!("TaskbarCreated")) });
        self.add_tray_icon();
        self.started.set(true);

        let problem = if let Err(error) = valid {
            Some(config_error_message(error, &strings::current().balloon))
        } else if !all_registered {
            Some(strings::current().balloon.hotkey_taken)
        } else if !saved {
            Some(strings::current().balloon.startup_save_failed)
        } else if autostart.is_none() {
            Some(strings::current().balloon.exe_unknown)
        } else {
            None
        };
        let silent = matches!(action, Action::Background);
        if let Some(text) = problem.filter(|_| !silent) {
            self.notify(text);
        }

        if matches!(action, Action::Background) && !self.isolated {
            // SAFETY: no pointer arguments; the timer belongs to our window.
            unsafe { SetTimer(self.window(), PRESTART_TIMER, PRESTART_MS, None) };
        }
        if let Some(request) = startup_request(action, all_registered && valid.is_ok()) {
            self.deliver(request);
        }
    }

    /// Adds the tray icon; while the taskbar is not ready (e.g. early during logon),
    /// a timer retries every few seconds. A message that waited for the icon is shown
    /// once it is there.
    fn add_tray_icon(&self) {
        // Read once: if the window is destroyed during the shell call, `self.window()`
        // is detached (null), but the icon is registered under this handle.
        let window = self.window();
        let state = tray::advance(window, self.tray_icon.handle(), self.tray_state.get());
        // Shell_NotifyIconW dispatches sent messages; if one of them destroyed the
        // window meanwhile, an icon added afterwards would be orphaned.
        // SAFETY: no pointer arguments.
        if unsafe { IsWindow(window) } == 0 {
            tray::remove(window);
            return;
        }
        self.tray_state.set(state);
        let retry = state != TrayState::Ready && self.tray_retries.get() > 0;
        if retry {
            self.tray_retries.set(self.tray_retries.get() - 1);
        }
        // SAFETY: no pointer arguments; the timer belongs to our window.
        unsafe {
            if retry {
                SetTimer(window, TRAY_RETRY_TIMER, TRAY_RETRY_MS, None);
            } else {
                KillTimer(window, TRAY_RETRY_TIMER);
            }
        }
        if state == TrayState::Ready {
            if let Some(text) = self.pending_balloon.take() {
                tray::balloon(window, text);
            }
        }
    }

    /// Tells the user about a problem with a tray balloon; until the tray icon is
    /// ready, the (latest) message waits for it.
    fn notify(&self, text: &'static str) {
        let shown = self.tray_state.get() == TrayState::Ready && tray::balloon(self.window(), text);
        if !shown {
            self.pending_balloon.set(Some(text));
        }
    }

    /// Runs the tray menu's modal loop. Meanwhile requests for the engine are queued
    /// and quitting is refused; a queued request is delivered afterwards.
    fn run_modal<T>(&self, modal: impl FnOnce() -> T) -> T {
        let result = self.modal_section(modal);
        if self.modal_depth.get() == 0 {
            if let Some(request) = self.pending_request.take() {
                self.deliver(request);
            }
        }
        result
    }

    /// Runs `section` as modal (requests are queued, quitting is refused) without
    /// delivering a queued request afterwards; the caller decides.
    fn modal_section<T>(&self, section: impl FnOnce() -> T) -> T {
        self.modal_depth.set(self.modal_depth.get() + 1);
        let result = section();
        self.modal_depth.set(self.modal_depth.get() - 1);
        result
    }

    fn in_modal(&self) -> bool {
        self.modal_depth.get() > 0
    }

    /// Handles a window message; `None` lets `DefWindowProcW` handle it.
    pub fn handle_message(&self, message: u32, wparam: WPARAM, lparam: LPARAM) -> Option<LRESULT> {
        if (ipc::needs_startup(message) || message == WM_HOTKEY) && !self.started.get() {
            return Some(0);
        }
        let taskbar_created = self.taskbar_created.get();
        if taskbar_created != 0 && message == taskbar_created {
            // Explorer restarted: the icon has to be added again.
            self.tray_retries.set(TRAY_RETRIES);
            self.tray_state.set(TrayState::Missing);
            self.add_tray_icon();
            return Some(0);
        }
        let result = match message {
            WM_ENDSESSION if wparam != 0 => {
                self.flush_settings_quietly();
                self.unregister_hotkeys();
                tray::remove(self.window());
                0
            }
            WM_DESTROY => {
                self.flush_settings_quietly();
                self.unregister_hotkeys();
                tray::remove(self.window());
                // SAFETY: no arguments besides the exit code.
                unsafe { PostQuitMessage(0) };
                0
            }
            WM_TIMER => {
                match wparam {
                    TRAY_RETRY_TIMER => self.add_tray_icon(),
                    PRESTART_TIMER => {
                        // SAFETY: no pointer arguments; the timer belongs to our window.
                        unsafe { KillTimer(self.window(), PRESTART_TIMER) };
                        let _ = self.engine.prestart();
                    }
                    SAVE_TIMER => {
                        if self.flush_settings().is_err() {
                            self.notify(strings::current().balloon.save_failed);
                        }
                    }
                    HOTKEY_RETRY_TIMER => {
                        self.register_missing();
                    }
                    _ => return None,
                }
                0
            }
            WM_COPYDATA => {
                // SAFETY: for WM_COPYDATA, lParam is null or points to a COPYDATASTRUCT
                // whose data stays valid for the duration of the message.
                let data = unsafe { (lparam as *const COPYDATASTRUCT).as_ref() };
                data.map_or(Reply::NotUnderstood, |data| self.copy_data(data)).to_lresult()
            }
            _ => return self.handle_request(message, wparam, lparam),
        };
        Some(result)
    }

    /// Handles hotkeys and requests from other processes and the tray; `None` for
    /// anything else.
    fn handle_request(&self, message: u32, wparam: WPARAM, lparam: LPARAM) -> Option<LRESULT> {
        let result = match message {
            WM_HOTKEY => {
                match i32::try_from(wparam) {
                    Ok(HOTKEY_SCREENSHOT) => self.deliver(EngineRequest::Screenshot),
                    Ok(HOTKEY_RECORDING) => self.deliver(EngineRequest::Recording),
                    _ => {}
                }
                0
            }
            ipc::SHOW_SETTINGS => {
                self.deliver(EngineRequest::Settings);
                1
            }
            ipc::SHOW_MAIN => {
                self.deliver(EngineRequest::Main);
                1
            }
            ipc::SHOW_RECORDING_SETTINGS => {
                self.deliver(EngineRequest::RecordingSettings);
                1
            }
            ipc::TAKE_SCREENSHOT => {
                self.deliver(EngineRequest::Screenshot);
                1
            }
            ipc::TOGGLE_RECORDING => {
                self.deliver(EngineRequest::Recording);
                1
            }
            ipc::QUIT => {
                if self.quit() {
                    ipc::QUIT_ACCEPTED
                } else {
                    ipc::QUIT_REFUSED
                }
            }
            ipc::PROBE => self.registered.get().probe().to_lresult(),
            ipc::AUDIO_SETTINGS => {
                self.apply_audio_settings(AudioSettings::from_wparam(wparam));
                1
            }
            ipc::TRAY_CALLBACK => {
                if let Some(event) = TrayEvent::decode(wparam, lparam) {
                    self.tray_event(event);
                }
                0
            }
            _ => return None,
        };
        Some(result)
    }

    /// Registers `hotkey` for `action` (replacing whatever that action had).
    fn register(&self, action: HotkeyAction, hotkey: Hotkey) -> bool {
        self.unregister(action);
        let modifiers = hotkey.modifiers.bits() | MOD_NOREPEAT;
        let id = hotkey_id(action);
        // SAFETY: no pointer arguments.
        let ok =
            unsafe { RegisterHotKey(self.window(), id, modifiers, u32::from(hotkey.key)) } != 0;
        if ok {
            self.registered.set(self.registered.get().with(action, Some(hotkey)));
        }
        ok
    }

    fn unregister(&self, action: HotkeyAction) {
        // SAFETY: no pointer arguments; unregistering an unknown id fails harmlessly.
        unsafe { UnregisterHotKey(self.window(), hotkey_id(action)) };
        self.registered.set(self.registered.get().with(action, None));
    }

    /// Releases both hotkeys; `--probe` reports them as missing from now on (also
    /// while the window is being torn down).
    fn unregister_hotkeys(&self) {
        for action in HotkeyAction::BOTH {
            self.unregister(action);
        }
        self.schedule_hotkey_retry();
    }

    /// Whether no other program has registered `hotkey`: registers it briefly under
    /// a spare id.
    fn is_free(&self, hotkey: Hotkey) -> bool {
        let modifiers = hotkey.modifiers.bits() | MOD_NOREPEAT;
        // SAFETY: no pointer arguments.
        unsafe {
            let free =
                RegisterHotKey(self.window(), HOTKEY_CHECK, modifiers, u32::from(hotkey.key));
            UnregisterHotKey(self.window(), HOTKEY_CHECK);
            free != 0
        }
    }

    /// Registers every configured hotkey that is not registered yet (unless
    /// suspended). Returns `true` if none is missing afterwards; otherwise the retry
    /// timer runs.
    fn register_missing(&self) -> bool {
        let missing =
            missing_hotkeys(&self.config.borrow(), self.registered.get(), self.suspended.get());
        let mut all = true;
        for (action, hotkey) in missing {
            all &= self.register(action, hotkey);
        }
        self.schedule_hotkey_retry();
        all
    }

    /// Runs the retry timer exactly while a configured hotkey is missing.
    fn schedule_hotkey_retry(&self) {
        let missing =
            missing_hotkeys(&self.config.borrow(), self.registered.get(), self.suspended.get());
        // SAFETY: no pointer arguments; the timer belongs to our window.
        unsafe {
            if missing.is_empty() {
                KillTimer(self.window(), HOTKEY_RETRY_TIMER);
            } else {
                SetTimer(self.window(), HOTKEY_RETRY_TIMER, HOTKEY_RETRY_SECONDS * 1000, None);
            }
        }
    }

    /// Writes (or removes) the autostart entry. An isolated instance never touches it
    /// and reports success; `None` means nothing was written because the path of this
    /// executable is unknown (an empty path must never end up in the Run key).
    fn write_autostart(&self, enabled: bool) -> Option<io::Result<()>> {
        if self.isolated {
            return Some(Ok(()));
        }
        self.exe.as_deref().map(|exe| autostart::set_enabled(enabled, exe))
    }

    /// Writes the settings [`SAVE_DELAY_MS`] after the last change; a new change
    /// restarts the delay.
    fn schedule_save(&self) {
        self.save_pending.set(true);
        // SAFETY: no pointer arguments; the timer belongs to our window.
        unsafe { SetTimer(self.window(), SAVE_TIMER, SAVE_DELAY_MS, None) };
    }

    /// Writes pending changes now. Nothing pending is success.
    fn flush_settings(&self) -> io::Result<()> {
        // SAFETY: no pointer arguments; killing a timer that does not run is harmless.
        unsafe { KillTimer(self.window(), SAVE_TIMER) };
        if !self.save_pending.replace(false) {
            return Ok(());
        }
        let config = self.config.borrow().clone();
        let result = self.store.save(&config);
        self.save_failed.set(result.is_err());
        result
    }

    /// Writes pending changes before the engine reads the file; a failure is shown
    /// but does not stop the request.
    fn flush_settings_or_notify(&self) {
        if self.flush_settings().is_err() {
            self.notify(strings::current().balloon.save_failed);
        }
    }

    /// Writes pending changes while the app ends, when nobody could be told anyway.
    fn flush_settings_quietly(&self) {
        let _ = self.flush_settings();
    }

    /// Adopts a changed configuration: in effect at once, written shortly. After a
    /// failed write it is written now, and a failure is the reply.
    fn adopt(&self, config: Config) -> Reply {
        *self.config.borrow_mut() = config;
        self.schedule_save();
        if self.save_failed.get() && self.flush_settings().is_err() {
            return Reply::SaveFailed;
        }
        Reply::Ok
    }

    /// Handles a `WM_COPYDATA` request from the engine's settings window.
    fn copy_data(&self, data: &COPYDATASTRUCT) -> Reply {
        let Ok(length) = usize::try_from(data.cbData) else {
            return Reply::NotUnderstood;
        };
        let bytes: &[u8] = if length == 0 || data.lpData.is_null() {
            &[]
        } else {
            // SAFETY: the sender's `cbData` bytes at `lpData` were copied into our
            // process by Windows and stay valid while the message is handled.
            unsafe { slice::from_raw_parts(data.lpData.cast::<u8>(), length) }
        };
        let Some(payload) = utf16_units(bytes) else {
            return Reply::NotUnderstood;
        };
        match copydata::parse(data.dwData, &payload) {
            Ok(request) => self.request(request),
            Err(reply) => reply,
        }
    }

    /// Carries out a decoded settings request.
    fn request(&self, request: Request) -> Reply {
        match request {
            Request::Set(setting) => {
                let change = copydata::plan_set(&self.config.borrow(), setting);
                match change {
                    Ok(change) => self.apply(change),
                    Err(reply) => reply,
                }
            }
            Request::CheckHotkey(action, text) => {
                let registered = self.registered.get().get(action);
                let check = copydata::plan_check(&self.config.borrow(), registered, action, &text);
                match check {
                    Check::Done(reply) => reply,
                    Check::TryRegister(hotkey) if self.is_free(hotkey) => Reply::Ok,
                    Check::TryRegister(_) => Reply::Taken,
                }
            }
            Request::SuspendHotkeys(true) => {
                self.suspended.set(true);
                self.unregister_hotkeys();
                Reply::Ok
            }
            Request::SuspendHotkeys(false) => {
                self.suspended.set(false);
                if self.register_missing() { Reply::Ok } else { Reply::Taken }
            }
        }
    }

    /// Puts a validated change into effect: registers a new hotkey (keeping the old
    /// one if Windows refuses), writes the autostart entry, then adopts the settings.
    fn apply(&self, change: copydata::Change) -> Reply {
        match change.effect {
            Effect::None => {}
            Effect::Hotkey(action, hotkey) => {
                let old = self.registered.get().get(action);
                let usable = if self.suspended.get() {
                    // Registered when the suspension ends; it must be free by then.
                    self.is_free(hotkey)
                } else {
                    old == Some(hotkey) || self.register(action, hotkey)
                };
                if !usable {
                    if let Some(old) = old {
                        self.register(action, old);
                    }
                    return Reply::Taken;
                }
            }
            Effect::Autostart(enabled) => {
                if !matches!(self.write_autostart(enabled), Some(Ok(()))) {
                    return Reply::AutostartFailed;
                }
            }
            Effect::Language(language) => {
                strings::set_language(language);
                if self.tray_state.get() == TrayState::Ready {
                    tray::refresh_tooltip(self.window(), self.tray_icon.handle());
                }
            }
        }
        let language = matches!(change.effect, Effect::Language(_));
        let mut reply = self.adopt(change.config);
        // The engine follows at once and reads the file: written now, not after the delay.
        if language && reply == Reply::Ok && self.flush_settings().is_err() {
            reply = Reply::SaveFailed;
        }
        // A fixed hotkey may complete the configuration (e.g. a duplicate is gone).
        self.register_missing();
        reply
    }

    /// Hands a request to the engine after writing pending settings. While the tray
    /// menu is open, the request waits for it to close.
    fn deliver(&self, request: EngineRequest) {
        self.deliver_with(request, false);
    }

    /// [`App::deliver`] for a command chosen in the tray menu: the engine waits until the menu is gone.
    fn deliver_with(&self, request: EngineRequest, from_menu: bool) {
        if self.in_modal() {
            self.pending_request.set(Some(request));
            return;
        }
        self.flush_settings_or_notify();
        if self.engine.deliver_with(request, from_menu).failed() {
            self.notify(strings::current().balloon.engine_failed);
        }
    }

    /// Quits app and engine. Refused (returns `false`) while the tray menu is open or
    /// the engine is in the middle of a capture (the user is told).
    fn quit(&self) -> bool {
        if self.in_modal() {
            return false;
        }
        // The state query waits for the engine and dispatches incoming sent messages
        // meanwhile; as a modal section, a nested quit is refused and requests wait.
        let busy = self.modal_section(|| self.engine.is_busy());
        let queued = self.pending_request.take();
        if busy {
            self.notify(strings::current().balloon.busy);
            return false;
        }
        if let Some(request) = queued {
            // The user asked for something while we checked: do it, keep running.
            self.deliver(request);
            return false;
        }
        self.flush_settings_quietly();
        self.engine.request_quit();
        // SAFETY: destroys our own window; WM_DESTROY then ends the message loop.
        unsafe { DestroyWindow(self.window()) };
        true
    }

    /// Adopts audio options changed in the engine's recording toolbar.
    fn apply_audio_settings(&self, audio: AudioSettings) {
        let mut updated = self.config.borrow().clone();
        updated.microphone = audio.microphone;
        updated.system_audio = audio.system_audio;
        updated.show_clicks = audio.show_clicks;
        *self.config.borrow_mut() = updated;
        self.schedule_save();
    }

    fn tray_event(&self, event: TrayEvent) {
        match event {
            TrayEvent::Select => self.deliver(EngineRequest::Main),
            TrayEvent::ContextMenu { x, y } => {
                let registered = self.registered.get();
                let caps = strings::current().keys;
                let hotkeys = MenuHotkeys {
                    screenshot: registered.screenshot.map(|hotkey| hotkey.label_with(&caps)),
                    recording: registered.recording.map(|hotkey| hotkey.label_with(&caps)),
                };
                match self.run_modal(|| tray::show_menu(self.window(), x, y, &hotkeys)) {
                    Some(MenuCommand::Screenshot) => {
                        self.deliver_with(EngineRequest::Screenshot, true);
                    }
                    Some(MenuCommand::Recording) => {
                        self.deliver_with(EngineRequest::Recording, true);
                    }
                    Some(MenuCommand::Settings) => self.deliver(EngineRequest::Settings),
                    Some(MenuCommand::Quit) => {
                        // A refusal is shown as a balloon.
                        let _ = self.quit();
                    }
                    None => {}
                }
            }
        }
    }
}

/// The message explaining why the stored settings cannot be used.
const fn config_error_message(error: ConfigError, balloon: &Balloons) -> &'static str {
    match error {
        ConfigError::InvalidHotkey(ParseHotkeyError::ShiftOnly) => balloon.shift_only_hotkey,
        ConfigError::InvalidHotkey(_) => balloon.invalid_hotkey,
        ConfigError::DuplicateHotkey => balloon.duplicate_hotkey,
        ConfigError::InvalidFolder => balloon.invalid_folder,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::DefaultFolders;

    fn config() -> Config {
        Config::defaults(&DefaultFolders::new(Path::new(r"C:\Bilder"), Path::new(r"C:\Videos")))
    }

    #[test]
    fn a_silent_start_shows_nothing() {
        for healthy in [true, false] {
            assert_eq!(
                startup_request(Action::Background, healthy),
                None,
                "--background, also with a taken hotkey"
            );
        }
        assert_eq!(startup_request(Action::ShowMain, true), Some(EngineRequest::Main));
        assert_eq!(startup_request(Action::ShowMain, false), Some(EngineRequest::Settings));
        assert_eq!(startup_request(Action::ShowSettings, true), Some(EngineRequest::Settings));
        assert_eq!(startup_request(Action::TakeScreenshot, true), Some(EngineRequest::Screenshot));
        assert_eq!(startup_request(Action::Quit, false), None);
    }

    fn hotkey(text: &str) -> Hotkey {
        text.parse().unwrap()
    }

    #[test]
    fn config_errors_have_messages() {
        let balloon = &strings::GERMAN.balloon;
        let shift_only = ConfigError::InvalidHotkey(ParseHotkeyError::ShiftOnly);
        assert_eq!(config_error_message(shift_only, balloon), balloon.shift_only_hotkey);
        let unknown = ConfigError::InvalidHotkey(ParseHotkeyError::UnknownKey);
        assert_eq!(config_error_message(unknown, balloon), balloon.invalid_hotkey);
        let duplicate = ConfigError::DuplicateHotkey;
        assert_eq!(config_error_message(duplicate, balloon), balloon.duplicate_hotkey);
        let folder = ConfigError::InvalidFolder;
        assert_eq!(config_error_message(folder, balloon), balloon.invalid_folder);
    }

    #[test]
    fn retry_targets_the_configured_hotkeys_that_are_missing() {
        let config = config();
        let (shot, rec) = (hotkey("Shift+Alt+4"), hotkey("Shift+Alt+5"));
        let none = Registered::default();
        assert_eq!(
            missing_hotkeys(&config, none, false),
            [(HotkeyAction::Screenshot, shot), (HotkeyAction::Recording, rec)]
        );
        let screenshot_only = none.with(HotkeyAction::Screenshot, Some(shot));
        assert_eq!(
            missing_hotkeys(&config, screenshot_only, false),
            [(HotkeyAction::Recording, rec)]
        );
        let both = screenshot_only.with(HotkeyAction::Recording, Some(rec));
        assert!(missing_hotkeys(&config, both, false).is_empty(), "nothing to retry");
        assert!(missing_hotkeys(&config, none, true).is_empty(), "not while suspended");

        let stale = both.with(HotkeyAction::Recording, Some(hotkey("Alt+9")));
        assert_eq!(missing_hotkeys(&config, stale, false), [(HotkeyAction::Recording, rec)]);

        let mut broken = config.clone();
        broken.recording_hotkey = broken.screenshot_hotkey.clone();
        assert!(missing_hotkeys(&broken, none, false).is_empty(), "duplicate: nothing");
        broken.recording_hotkey = "Shift+R".to_owned();
        assert!(missing_hotkeys(&broken, none, false).is_empty(), "invalid: nothing");
    }

    #[test]
    fn probe_reports_what_is_registered() {
        let none = Registered::default();
        assert_eq!(none.probe(), ProbeResponse::default());
        let shot = none.with(HotkeyAction::Screenshot, Some(hotkey("Alt+1")));
        assert_eq!(
            shot.probe(),
            ProbeResponse { screenshot_hotkey: true, recording_hotkey: false }
        );
        assert_eq!(shot.get(HotkeyAction::Recording), None);
        let both = shot.with(HotkeyAction::Recording, Some(hotkey("Alt+2")));
        assert_eq!(both.probe().to_lresult(), ProbeResponse::HEALTHY);
        assert_eq!(both.with(HotkeyAction::Screenshot, None).probe().to_lresult(), 5);
    }

    #[test]
    fn hotkey_ids_are_distinct() {
        assert_eq!(hotkey_id(HotkeyAction::Screenshot), HOTKEY_SCREENSHOT);
        assert_eq!(hotkey_id(HotkeyAction::Recording), HOTKEY_RECORDING);
        assert!(![HOTKEY_SCREENSHOT, HOTKEY_RECORDING].contains(&HOTKEY_CHECK));
        let timers = [TRAY_RETRY_TIMER, SAVE_TIMER, HOTKEY_RETRY_TIMER];
        assert!(timers.iter().enumerate().all(|(i, t)| !timers[..i].contains(t)));
    }

    #[test]
    fn copydata_payload_is_utf16le() {
        assert_eq!(utf16_units(&[]), Some(Vec::new()));
        assert_eq!(utf16_units(&[0x31, 0x00, 0x09, 0x00]), Some(vec![0x31, 0x09]));
        assert_eq!(utf16_units(&[0x3D, 0xD8]), Some(vec![0xD83D]));
        assert_eq!(utf16_units(&[0x31, 0x00, 0x09]), None, "odd length");
    }
}
