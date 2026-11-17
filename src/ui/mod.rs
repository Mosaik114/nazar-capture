//! The primary instance: a hidden window for the tray icon, the hotkeys and the
//! requests of other processes, and the message loop.
//!
//! The window is never shown. The settings window belongs to the engine, which sends
//! changes back with `WM_COPYDATA` (see [`crate::copydata`]); problems are shown as
//! balloons at the tray icon.

mod app;
mod icons;
mod strings;
mod tray;

use std::{env, mem, ptr};

use windows_sys::Win32::Foundation::{GetLastError, HINSTANCE, HWND, LPARAM, LRESULT, WPARAM};
use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
use windows_sys::Win32::UI::HiDpi::{
    DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2, SetProcessDpiAwarenessContext,
};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    CREATESTRUCTW, CreateWindowExW, DefWindowProcW, DestroyWindow, DispatchMessageW, GWLP_USERDATA,
    GetMessageW, GetWindowLongPtrW, IsWindow, MB_ICONERROR, MB_OK, MSG, MessageBoxW,
    RegisterClassW, SetWindowLongPtrW, TranslateMessage, UnregisterClassW, WM_NCCREATE,
    WM_NCDESTROY, WNDCLASSW, WS_EX_TOOLWINDOW, WS_POPUP,
};
use windows_sys::core::PCWSTR;

use crate::cli::Action;
use crate::ipc;
use crate::isolation::{Isolation, isolated};
use crate::store::SettingsStore;
use crate::wide::to_wide;
use app::App;
#[cfg(test)]
pub use app::{HOTKEY_RETRY_SECONDS, SAVE_DELAY_MS};
#[cfg(test)]
pub use strings::texts_of as texts;
pub use strings::{InstallTexts, PRODUCT, current as current_texts, set_language};

/// Which step of running the user interface failed.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum UiErrorKind {
    RegisterClass,
    CreateWindow,
    MessageLoop,
}

/// Why the user interface could not be run: the failed step and the Windows error
/// code (`GetLastError`) at that moment.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct UiError {
    pub kind: UiErrorKind,
    pub code: u32,
}

impl UiError {
    /// Captures the calling thread's last error for `kind`. Call it directly after
    /// the failed API call.
    pub(crate) fn last(kind: UiErrorKind) -> Self {
        // SAFETY: reads the calling thread's last-error value.
        Self { kind, code: unsafe { GetLastError() } }
    }

    /// The message for the user, in the interface language.
    pub fn message(self) -> String {
        self.message_in(strings::current())
    }

    fn message_in(self, texts: &strings::Texts) -> String {
        let step = match self.kind {
            UiErrorKind::RegisterClass => texts.ui_error.register_class,
            UiErrorKind::CreateWindow => texts.ui_error.create_window,
            UiErrorKind::MessageLoop => texts.ui_error.message_loop,
        };
        texts.ui_error.with_code.replace("{step}", step).replace("{code}", &self.code.to_string())
    }
}

/// Tells the user why the app could not start (for interactive starts; a background
/// start at logon only sets the exit code).
pub fn report(error: UiError) {
    report_message(&error.message());
}

/// Shows an error message (start-up or `--install`) in a message box.
pub fn report_message(text: &str) {
    let text = to_wide(text);
    let title = to_wide(PRODUCT);
    // SAFETY: both strings are NUL-terminated and outlive the modal call; no owner.
    unsafe { MessageBoxW(ptr::null_mut(), text.as_ptr(), title.as_ptr(), MB_OK | MB_ICONERROR) };
}

/// Runs the primary instance until the user quits.
pub fn run(action: Action, isolation: Option<&Isolation>) -> Result<(), UiError> {
    // SAFETY: a process-wide setting without pointer arguments, set before any window.
    // The manifest requests the same; this covers hosts that ignore it. It gives the
    // tray icon the taskbar's real size.
    unsafe { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) };

    let store = isolation.map_or_else(SettingsStore::user_default, |isolation| {
        SettingsStore::at(isolation.settings.clone())
    });
    // Earlier versions could follow the Windows theme; that becomes the palette shown now.
    let mut config = store.load_settled(crate::store::windows_uses_light_theme);
    strings::set_language(config.language);
    if isolation.is_some() {
        config.autostart = false;
    }

    // SAFETY: a null name returns the handle of the executable itself.
    let instance = unsafe { GetModuleHandleW(ptr::null()) };
    let exe = env::current_exe().ok();
    // Boxed: the window procedure keeps the address, so it must never move.
    let app = Box::new(App::new(instance, store, config, exe, isolation));

    // Unregistered when this guard drops, after the window is gone.
    let class = register_class(&app, &isolated(ipc::WINDOW_CLASS, isolation))?;
    let window = create_window(&app, &class)?;

    app.start(action);
    let result = run_message_loop();
    // Normally the window is gone already. If not (an error), destroy it while `app`
    // is alive; WM_NCDESTROY then detaches `app` from the window.
    // SAFETY: no pointer arguments; `window` is our window or already invalid.
    if unsafe { IsWindow(window) } != 0 {
        // SAFETY: see above.
        unsafe { DestroyWindow(window) };
    }
    result
}

/// A registered window class, unregistered on drop.
#[derive(Debug)]
struct WindowClass {
    name: Vec<u16>,
    instance: HINSTANCE,
}

impl WindowClass {
    const fn name(&self) -> PCWSTR {
        self.name.as_ptr()
    }
}

impl Drop for WindowClass {
    fn drop(&mut self) {
        // SAFETY: the name is NUL-terminated; fails harmlessly if a window of the class
        // still exists.
        unsafe { UnregisterClassW(self.name.as_ptr(), self.instance) };
    }
}

/// Registers the window class of the hidden window: no icon, cursor or background,
/// since it is never shown.
fn register_class(app: &App, name: &str) -> Result<WindowClass, UiError> {
    let class_name = to_wide(name);
    let class = WNDCLASSW {
        style: 0,
        lpfnWndProc: Some(window_proc),
        cbClsExtra: 0,
        cbWndExtra: 0,
        hInstance: app.instance(),
        hIcon: ptr::null_mut(),
        hCursor: ptr::null_mut(),
        hbrBackground: ptr::null_mut(),
        lpszMenuName: ptr::null(),
        lpszClassName: class_name.as_ptr(),
    };
    // SAFETY: `class` and the class name it points to outlive the call.
    if unsafe { RegisterClassW(&raw const class) } == 0 {
        return Err(UiError::last(UiErrorKind::RegisterClass));
    }
    Ok(WindowClass { name: class_name, instance: app.instance() })
}

/// Creates the hidden window. It is a top-level window (not message-only), so it
/// receives Explorer's `TaskbarCreated` broadcast, and a tool window, so it would stay
/// off the taskbar and Alt+Tab even if something showed it.
fn create_window(app: &App, class: &WindowClass) -> Result<HWND, UiError> {
    let title = to_wide(PRODUCT);
    // SAFETY: class name and title are NUL-terminated and outlive the call. `app`
    // outlives the window (see `run`), so handing its address to WM_NCCREATE is sound.
    let window = unsafe {
        CreateWindowExW(
            WS_EX_TOOLWINDOW,
            class.name(),
            title.as_ptr(),
            WS_POPUP,
            0,
            0,
            0,
            0,
            ptr::null_mut(),
            ptr::null_mut(),
            app.instance(),
            ptr::from_ref(app).cast(),
        )
    };
    if window.is_null() {
        return Err(UiError::last(UiErrorKind::CreateWindow));
    }
    Ok(window)
}

/// Pumps messages until `WM_QUIT`.
fn run_message_loop() -> Result<(), UiError> {
    // SAFETY: MSG is a plain C struct for which all-zero is valid.
    let mut message: MSG = unsafe { mem::zeroed() };
    loop {
        // SAFETY: `message` outlives every call that reads or writes it.
        unsafe {
            match GetMessageW(&raw mut message, ptr::null_mut(), 0, 0) {
                0 => return Ok(()),
                -1 => return Err(UiError::last(UiErrorKind::MessageLoop)),
                _ => {
                    TranslateMessage(&raw const message);
                    DispatchMessageW(&raw const message);
                }
            }
        }
    }
}

/// Window procedure: finds the [`App`] stored in `GWLP_USERDATA` and delegates.
unsafe extern "system" fn window_proc(
    window: HWND,
    message: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    if message == WM_NCCREATE {
        // SAFETY: for WM_NCCREATE, lParam is null or points to the window's
        // CREATESTRUCTW, valid for the duration of the message.
        let create = unsafe { (lparam as *const CREATESTRUCTW).as_ref() };
        let params = create.map_or(ptr::null_mut(), |create| create.lpCreateParams);
        // SAFETY: `create_window` passes the boxed `App`, which outlives the window;
        // a null parameter (someone else creating our class) is simply not attached.
        if let Some(app) = unsafe { params.cast::<App>().as_ref() } {
            // SAFETY: storing a pointer in our own window's user data is plain data.
            unsafe { SetWindowLongPtrW(window, GWLP_USERDATA, ptr::from_ref(app) as isize) };
            app.attach(window);
        }
    }

    // SAFETY: reads plain data from our own window.
    let app = unsafe { GetWindowLongPtrW(window, GWLP_USERDATA) } as *const App;
    // SAFETY: a non-null value was stored above and points to the boxed App, which
    // outlives the window: it is cleared on WM_NCDESTROY, and `run` destroys the
    // window before the App is dropped.
    let result =
        unsafe { app.as_ref() }.and_then(|app| app.handle_message(message, wparam, lparam));
    if message == WM_NCDESTROY {
        // SAFETY: see above.
        if let Some(app) = unsafe { app.as_ref() } {
            app.detach();
        }
        // SAFETY: clearing our own window's user data is plain data.
        unsafe { SetWindowLongPtrW(window, GWLP_USERDATA, 0) };
    }
    // SAFETY: default handling for everything the app does not handle itself.
    result.unwrap_or_else(|| unsafe { DefWindowProcW(window, message, wparam, lparam) })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::{Appearance, Config, Countdown, DefaultFolders, FrameRate, ProfileSource};
    use crate::copydata::{self, Reply};
    use crate::store::IniFile;
    use std::ffi::OsStr;
    use std::path::{Path, PathBuf};
    use std::time::{Duration, Instant};
    use windows_sys::Win32::System::DataExchange::COPYDATASTRUCT;
    use windows_sys::Win32::UI::Input::KeyboardAndMouse::{
        MOD_ALT, MOD_CONTROL, MOD_SHIFT, RegisterHotKey, UnregisterHotKey, VK_F20, VK_F21,
    };
    use windows_sys::Win32::UI::WindowsAndMessaging::{
        HWND_MESSAGE, IsWindowVisible, PM_REMOVE, PeekMessageW, SendMessageW, WM_COPYDATA, WM_TIMER,
    };

    /// A message-only window (never visible, no hotkeys) of `class`, created with
    /// `params` as its creation parameter.
    fn message_only_window(app: &App, class: &WindowClass, params: *const App) -> HWND {
        // SAFETY: the class name outlives the call; HWND_MESSAGE makes the window
        // message-only; `params` is null or the boxed App, which outlives the window.
        unsafe {
            CreateWindowExW(
                0,
                class.name(),
                ptr::null(),
                0,
                0,
                0,
                0,
                0,
                HWND_MESSAGE,
                ptr::null_mut(),
                app.instance(),
                params.cast(),
            )
        }
    }

    fn user_data(window: HWND) -> isize {
        // SAFETY: reads plain data of a window of this thread.
        unsafe { GetWindowLongPtrW(window, GWLP_USERDATA) }
    }

    fn send(window: HWND, message: u32, wparam: WPARAM) -> LRESULT {
        // SAFETY: the messages used with this helper carry no pointers.
        unsafe { SendMessageW(window, message, wparam, 0) }
    }

    /// Sends a settings request as the engine does: UTF-16LE without terminator.
    fn copy_data(window: HWND, request: usize, text: &str) -> LRESULT {
        let bytes: Vec<u8> = text.encode_utf16().flat_map(u16::to_le_bytes).collect();
        let data = COPYDATASTRUCT {
            dwData: request,
            cbData: u32::try_from(bytes.len()).unwrap(),
            lpData: bytes.as_ptr().cast_mut().cast(),
        };
        // SAFETY: `data` and the bytes it points to outlive the synchronous call.
        unsafe { SendMessageW(window, WM_COPYDATA, 0, (&raw const data) as LPARAM) }
    }

    fn set(window: HWND, key: &str, value: &str) -> LRESULT {
        copy_data(window, copydata::SET, &format!("{key}\t{value}"))
    }

    fn check(window: HWND, key: &str, value: &str) -> LRESULT {
        copy_data(window, copydata::CHECK_HOTKEY, &format!("{key}\t{value}"))
    }

    fn reply(reply: Reply) -> LRESULT {
        reply.to_lresult()
    }

    /// Dispatches this thread's messages (timers) until `done` holds or `timeout`
    /// passes; returns whether `done` held.
    fn pump_until(timeout: Duration, mut done: impl FnMut() -> bool) -> bool {
        let started = Instant::now();
        // SAFETY: MSG is a plain C struct for which all-zero is valid.
        let mut message: MSG = unsafe { mem::zeroed() };
        loop {
            // SAFETY: `message` outlives the calls.
            while unsafe { PeekMessageW(&raw mut message, ptr::null_mut(), 0, 0, PM_REMOVE) } != 0 {
                // SAFETY: see above.
                unsafe { DispatchMessageW(&raw const message) };
            }
            if done() {
                return true;
            }
            if started.elapsed() > timeout {
                return false;
            }
            std::thread::sleep(Duration::from_millis(10));
        }
    }

    fn folders() -> DefaultFolders {
        DefaultFolders::new(Path::new(r"C:\Bilder"), Path::new(r"C:\Videos"))
    }

    /// Destroys the window (unregistering any hotkeys) and removes the temp folder,
    /// even if an assertion fails.
    struct Cleanup {
        window: HWND,
        dir: PathBuf,
    }

    impl Drop for Cleanup {
        fn drop(&mut self) {
            // SAFETY: destroys the test window while its `App` is still alive.
            unsafe { DestroyWindow(self.window) };
            let _ = std::fs::remove_dir_all(&self.dir);
        }
    }

    #[test]
    fn ui_errors_have_messages() {
        let error = UiError { kind: UiErrorKind::CreateWindow, code: 1400 };
        assert_eq!(
            error.message_in(&strings::GERMAN),
            "Das Programmfenster konnte nicht erstellt werden (Windows-Fehler 1400)."
        );
        assert_eq!(
            error.message_in(&strings::ENGLISH),
            "The program window could not be created (Windows error 1400)."
        );
    }

    /// The window procedure's plumbing, without anything visible or global: the App
    /// is attached on `WM_NCCREATE` and detached on `WM_NCDESTROY`, a window created
    /// without an App stays unattached, and requests before start-up are refused.
    #[test]
    fn window_procedure_attaches_and_detaches_the_app() {
        let dir = env::temp_dir();
        let ini = dir.join(format!("nazar-capture-msgonly-{}.ini", std::process::id()));
        let isolation = Isolation::for_test("msgonly", &dir);
        // SAFETY: a null name returns the handle of the executable itself.
        let instance = unsafe { GetModuleHandleW(ptr::null()) };
        let app = Box::new(App::new(
            instance,
            SettingsStore::at(ini.clone()),
            Config::defaults(&folders()),
            None,
            Some(&isolation),
        ));
        let class_name = format!("NazarCapture.Host.MessageOnly.{}", std::process::id());
        let class = register_class(&app, &class_name).unwrap();

        let window = message_only_window(&app, &class, ptr::from_ref(&*app));
        assert!(!window.is_null());
        assert_eq!(app.window(), window, "attached on WM_NCCREATE");
        assert_eq!(user_data(window), ptr::from_ref(&*app) as isize);

        assert_eq!(send(window, ipc::PROBE, 0), 0, "not started: probe says no");
        assert_eq!(send(window, ipc::QUIT, 0), ipc::QUIT_REFUSED);
        assert_eq!(send(window, ipc::SHOW_SETTINGS, 0), 0);
        assert_eq!(send(window, ipc::TAKE_SCREENSHOT, 0), 0);
        assert_eq!(set(window, "fps", "30"), 0, "settings wait for start-up, too");

        let stranger = message_only_window(&app, &class, ptr::null());
        assert!(!stranger.is_null());
        assert_eq!(user_data(stranger), 0, "no creation parameter, nothing attached");
        assert_eq!(app.window(), window, "the App still belongs to the first window");
        assert_eq!(send(stranger, ipc::PROBE, 0), 0, "handled by DefWindowProc");

        // SAFETY: destroys the test windows while the App is alive.
        unsafe {
            DestroyWindow(stranger);
            DestroyWindow(window);
        }
        assert!(app.window().is_null(), "detached on WM_NCDESTROY");
        // SAFETY: no pointer arguments.
        assert_eq!(unsafe { IsWindow(window) }, 0);
        assert!(!ini.exists(), "nothing was saved before start-up");
        assert_eq!(app.config().frame_rate, FrameRate::Fps60, "nothing was applied");
    }

    /// The settings protocol without global side effects (a message-only window, no
    /// hotkeys, no tray icon, test isolation with a temp settings file): replies,
    /// validation, every kind of setting, the engine's audio message, and saving once
    /// shortly after a burst of changes.
    #[test]
    fn settings_requests_are_applied_and_saved_once() {
        let dir = env::temp_dir().join(format!("nazar-capture-copydata-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let ini = dir.join("settings.ini");
        let isolation = Isolation::for_test(&format!("copydata-{}", std::process::id()), &dir);
        // SAFETY: a null name returns the handle of the executable itself.
        let instance = unsafe { GetModuleHandleW(ptr::null()) };
        let app = Box::new(App::new(
            instance,
            SettingsStore::at(ini.clone()),
            Config::defaults(&folders()),
            env::current_exe().ok(),
            Some(&isolation),
        ));
        let class_name = format!("NazarCapture.Host.CopyData.{}", std::process::id());
        let class = register_class(&app, &class_name).unwrap();
        let window = message_only_window(&app, &class, ptr::from_ref(&*app));
        let _cleanup = Cleanup { window, dir };
        app.accept_requests();

        assert_eq!(set(window, "fps", "30"), reply(Reply::Ok));
        assert_eq!(set(window, "countdown", "5"), reply(Reply::Ok));
        assert_eq!(set(window, "appearance", "2"), reply(Reply::Ok));
        assert_eq!(set(window, "check_updates", "0"), reply(Reply::Ok));
        assert_eq!(set(window, "microphone", "1"), reply(Reply::Ok));
        assert_eq!(set(window, "recording_folder", r"D:\Filme"), reply(Reply::Ok));
        assert_eq!(set(window, "autostart", "0"), reply(Reply::Ok), "isolated: no registry");
        let applied = app.config();
        assert_eq!(applied.frame_rate, FrameRate::Fps30, "in effect at once");
        assert_eq!(applied.countdown, Countdown::Seconds5);
        assert_eq!(applied.appearance, Appearance::Dark);
        assert!(!applied.check_updates && applied.microphone && !applied.autostart);
        assert_eq!(applied.recording_folder, Path::new(r"D:\Filme"));
        assert!(!ini.exists(), "written only after the delay");

        // Refused changes leave everything as it was.
        assert_eq!(set(window, "screenshot_folder", "relativ"), reply(Reply::FolderUnusable));
        assert_eq!(set(window, "screenshot_hotkey", "Alt+"), reply(Reply::Invalid));
        assert_eq!(set(window, "screenshot_hotkey", "Shift+R"), reply(Reply::Invalid));
        assert_eq!(set(window, "recording_hotkey", "Alt+Shift+4"), reply(Reply::Duplicate));
        assert_eq!(set(window, "fps", "schnell"), reply(Reply::Invalid));
        assert_eq!(set(window, "unknown", "1"), reply(Reply::NotUnderstood));
        assert_eq!(copy_data(window, 99, "fps\t30"), reply(Reply::NotUnderstood));
        assert_eq!(check(window, "recording_hotkey", "Alt+"), reply(Reply::Invalid));
        assert_eq!(check(window, "recording_hotkey", "Umschalt+Alt+4"), reply(Reply::Duplicate));
        assert_eq!(app.config(), applied);

        assert_eq!(send(window, ipc::AUDIO_SETTINGS, 2 | 4), 1);
        let audio = app.config();
        assert!(!audio.microphone && audio.system_audio && audio.show_clicks);
        // Alive; the hotkey bits depend on whether Shift+Alt+4/5 are free on this machine.
        assert_eq!(send(window, ipc::PROBE, 0) & 1, 1, "alive");

        // One write, shortly after the last change.
        assert!(pump_until(Duration::from_secs(5), || ini.exists()), "saved after the delay");
        let source = IniFile::new(&ini);
        assert_eq!(Config::load(&source, &folders()), audio);
        std::fs::remove_file(&ini).unwrap();
        let delay = Duration::from_millis(u64::from(SAVE_DELAY_MS) * 2);
        assert!(!pump_until(delay, || ini.exists()), "the burst was written once");

        // A pending change is written when its timer fires.
        assert_eq!(set(window, "show_clicks", "0"), reply(Reply::Ok));
        assert_eq!(send(window, WM_TIMER, app::SAVE_TIMER), 0);
        assert_eq!(source.int("show_clicks", 1), 0);
        assert_eq!(source.string("recording_folder", OsStr::new("")), r"D:\Filme");
    }

    /// Drives the real hidden window: start-up, probe, hotkey changes (also one that
    /// another registration holds), hotkey checks, suspending while the engine records
    /// a hotkey, the retry of a taken hotkey, and quitting (which writes the settings).
    ///
    /// It only touches this process (a private window class, test isolation with a
    /// temp settings file, no engine, no registry), but it registers unusual global
    /// hotkeys (Ctrl+Alt+Shift+F20…F24) and adds a tray icon, so it is ignored by
    /// default. CI runs it with `cargo test -- --include-ignored`; locally use
    /// `cargo test -- --ignored host_window` (on a spare desktop if you like).
    #[test]
    #[ignore = "registers global hotkeys and a tray icon; CI runs it with --include-ignored"]
    fn host_window_end_to_end() {
        let dir = env::temp_dir().join(format!("nazar-capture-ui-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let ini = dir.join("settings.ini");
        let mut config = Config::defaults(&folders());
        config.screenshot_hotkey = "Strg+Alt+Shift+F23".to_owned();
        config.recording_hotkey = "Strg+Alt+Shift+F24".to_owned();
        config.autostart = false;

        // SAFETY: a null name returns the handle of the executable itself.
        let instance = unsafe { GetModuleHandleW(ptr::null()) };
        let isolation = Isolation::for_test(&format!("e2e-{}", std::process::id()), &dir);
        let app = Box::new(App::new(
            instance,
            SettingsStore::at(ini.clone()),
            config.clone(),
            env::current_exe().ok(),
            Some(&isolation),
        ));
        let class = register_class(&app, "NazarCapture.Host.Test").unwrap();
        let window = create_window(&app, &class).unwrap();
        let _cleanup = Cleanup { window, dir };

        // Requests from outside are not handled before start-up has finished.
        assert_eq!(send(window, ipc::PROBE, 0), 0, "probe before start");
        assert_eq!(send(window, ipc::QUIT, 0), ipc::QUIT_REFUSED, "quit before start");
        assert_eq!(check(window, "screenshot_hotkey", "Alt+1"), 0);

        app.start(Action::Background);
        assert_eq!(send(window, ipc::PROBE, 0), ipc::ProbeResponse::HEALTHY);
        // SAFETY: no pointer arguments.
        assert_eq!(unsafe { IsWindowVisible(window) }, 0, "never shown");
        let source = IniFile::new(&ini);
        assert_eq!(Config::load(&source, &folders()), config, "settings written at start");

        let shot = "screenshot_hotkey";
        assert_eq!(set(window, shot, "Strg+Alt+Shift+F22"), reply(Reply::Ok));
        assert_eq!(send(window, ipc::PROBE, 0), ipc::ProbeResponse::HEALTHY);
        assert_eq!(app.config().screenshot_hotkey, "Strg+Alt+Shift+F22");

        // Another registration holds Ctrl+Alt+Shift+F21 (posted to this thread).
        let modifiers = MOD_CONTROL | MOD_ALT | MOD_SHIFT;
        let foreign_id = 0x0100;
        // SAFETY: no pointer arguments; a null window means this thread.
        let held =
            unsafe { RegisterHotKey(ptr::null_mut(), foreign_id, modifiers, u32::from(VK_F21)) };
        assert_ne!(held, 0, "Ctrl+Alt+Shift+F21 is free on this desktop");
        assert_eq!(set(window, shot, "Ctrl+Alt+Shift+F21"), reply(Reply::Taken));
        assert_eq!(app.config().screenshot_hotkey, "Strg+Alt+Shift+F22", "old one kept");
        assert_eq!(send(window, ipc::PROBE, 0), ipc::ProbeResponse::HEALTHY, "and registered");
        assert_eq!(check(window, shot, "Strg+Alt+Shift+F21"), reply(Reply::Taken));
        assert_eq!(check(window, shot, "Shift+Ctrl+Alt+F22"), reply(Reply::Ok), "its own");
        assert_eq!(check(window, shot, "Strg+Alt+Shift+F20"), reply(Reply::Ok));
        assert_eq!(
            check(window, "recording_hotkey", "Strg+Alt+Shift+F22"),
            reply(Reply::Duplicate)
        );
        // SAFETY: no pointer arguments.
        unsafe { UnregisterHotKey(ptr::null_mut(), foreign_id) };

        // While the engine records a hotkey, both are released.
        assert_eq!(copy_data(window, copydata::SUSPEND_HOTKEYS, "1"), reply(Reply::Ok));
        assert_eq!(send(window, ipc::PROBE, 0), 1, "alive, no hotkeys");
        assert_eq!(set(window, shot, "Strg+Alt+Shift+F20"), reply(Reply::Ok));
        assert_eq!(send(window, ipc::PROBE, 0), 1, "still suspended");
        assert_eq!(check(window, shot, "Strg+Alt+Shift+F20"), reply(Reply::Ok));
        assert_eq!(copy_data(window, copydata::SUSPEND_HOTKEYS, "0"), reply(Reply::Ok));
        assert_eq!(send(window, ipc::PROBE, 0), ipc::ProbeResponse::HEALTHY);

        // A hotkey taken while suspended is retried until it is free again.
        assert_eq!(copy_data(window, copydata::SUSPEND_HOTKEYS, "1"), reply(Reply::Ok));
        // SAFETY: no pointer arguments; a null window means this thread.
        let held =
            unsafe { RegisterHotKey(ptr::null_mut(), foreign_id, modifiers, u32::from(VK_F20)) };
        assert_ne!(held, 0);
        assert_eq!(copy_data(window, copydata::SUSPEND_HOTKEYS, "0"), reply(Reply::Taken));
        assert_eq!(send(window, ipc::PROBE, 0), 1 | 4, "only the recording hotkey");
        assert_eq!(send(window, WM_TIMER, app::HOTKEY_RETRY_TIMER), 0);
        assert_eq!(send(window, ipc::PROBE, 0), 1 | 4, "still taken");
        // SAFETY: no pointer arguments.
        unsafe { UnregisterHotKey(ptr::null_mut(), foreign_id) };
        assert_eq!(send(window, WM_TIMER, app::HOTKEY_RETRY_TIMER), 0);
        assert_eq!(send(window, ipc::PROBE, 0), ipc::ProbeResponse::HEALTHY, "retried");

        // No engine runs next to the test executable, so nothing blocks quitting; the
        // pending change is written on the way out.
        assert_eq!(send(window, ipc::QUIT, 0), ipc::QUIT_ACCEPTED);
        // SAFETY: no pointer arguments.
        assert_eq!(unsafe { IsWindow(window) }, 0, "quit destroys the window");
        assert_eq!(source.string(shot, OsStr::new("")), "Strg+Alt+Shift+F20");
    }
}
