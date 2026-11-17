//! `NazarCapture.exe --install [<folder>]`: installs or updates the app from the
//! folder of the running executable (an extracted release ZIP).
//!
//! 1. The running instance is asked to quit through the normal quit protocol
//!    ([`ipc::QUIT`], which also quits the engine); it refuses while a capture runs,
//!    and installing stops there. Processes are never killed.
//! 2. Every file of the source folder (not its subfolders) is copied into the target
//!    folder, by default `%LOCALAPPDATA%\Programs\NazarCapture`. A file the old
//!    process still holds is retried briefly. The settings are never touched.
//! 3. The autostart entry follows the stored setting, a Start-menu shortcut is
//!    written (an older one is removed), and the installed app starts with
//!    `--background`.
//!
//! Under test isolation step 3 is skipped entirely (no registry, no Start menu, no
//! process), and the target folder must be given.

use std::ffi::OsStr;
use std::fs;
use std::io;
use std::os::windows::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::{Duration, Instant};
use std::{env, ptr, thread};

use windows_sys::Win32::Foundation::{ERROR_LOCK_VIOLATION, ERROR_SHARING_VIOLATION, HWND};
use windows_sys::Win32::System::Threading::CREATE_NO_WINDOW;
use windows_sys::Win32::UI::Shell::{FOLDERID_LocalAppData, FOLDERID_Programs};
use windows_sys::Win32::UI::WindowsAndMessaging::{FindWindowExW, PostMessageW};

use crate::cli::Action;
use crate::isolation::{Isolation, isolated};
use crate::single_instance::{self, Forwarded, InstanceLock, Target};
use crate::store::{self, SettingsStore};
use crate::wide::to_wide;
use crate::{autostart, engine, ipc, ui};

/// File name of the host executable in a release.
pub const HOST_EXE_NAME: &str = "NazarCapture.exe";
/// The default target below `%LOCALAPPDATA%`.
const DEFAULT_TARGET: [&str; 2] = ["Programs", "NazarCapture"];
/// The Start-menu shortcut, named after the product.
const SHORTCUT_NAME: &str = "Nazar Capture.lnk";
/// Shortcuts of earlier versions, removed when the new one is written.
const LEGACY_SHORTCUT_NAMES: [&str; 1] = ["Mosaik Capture.lnk"];
/// How long the running instance (host and engine) may take to exit.
const QUIT_TIMEOUT: Duration = Duration::from_secs(15);
/// How often a file the old process still holds is tried, and the pause between.
const COPY_ATTEMPTS: u32 = 20;
const COPY_RETRY_DELAY: Duration = Duration::from_millis(250);
const POLL_INTERVAL: Duration = Duration::from_millis(100);

/// Why installing failed; [`InstallError::message`] is the German text for the user.
#[derive(Debug)]
pub enum InstallError {
    /// Isolated, but no target folder was given.
    TargetRequired,
    /// `%LOCALAPPDATA%` could not be found.
    NoLocalAppData,
    /// The folder of the running executable is unknown.
    NoSource,
    /// The source folder lacks one of the two executables.
    Incomplete(&'static str),
    /// The running instance refused to quit: a capture or recording is in progress.
    Busy,
    /// The running instance (or its engine) did not exit in time.
    StillRunning,
    Copy(PathBuf, io::Error),
    /// A path cannot be passed to the shortcut script (not valid Unicode).
    UnicodePath(PathBuf),
    Autostart(io::Error),
    Shortcut(String),
    Start(io::Error),
}

impl InstallError {
    /// The message for the user, in the interface language.
    pub fn message(&self) -> String {
        self.message_in(&crate::ui::current_texts().install)
    }

    fn message_in(&self, t: &crate::ui::InstallTexts) -> String {
        match self {
            Self::TargetRequired => t.target_required.to_owned(),
            Self::NoLocalAppData => t.no_local_app_data.to_owned(),
            Self::NoSource => t.no_source.to_owned(),
            Self::Incomplete(name) => t.incomplete.replace("{name}", name),
            Self::Busy => t.busy.to_owned(),
            Self::StillRunning => t.still_running.to_owned(),
            Self::Copy(path, error) => t
                .copy
                .replace("{path}", &path.display().to_string())
                .replace("{error}", &error.to_string()),
            Self::UnicodePath(path) => {
                t.unicode_path.replace("{path}", &path.display().to_string())
            }
            Self::Autostart(error) => t.autostart.replace("{error}", &error.to_string()),
            Self::Shortcut(detail) => t.shortcut.replace("{detail}", detail),
            Self::Start(error) => t.start.replace("{error}", &error.to_string()),
        }
    }
    /// Whether this is a refusal because of a running capture (its own exit code).
    pub const fn is_busy(&self) -> bool {
        matches!(self, Self::Busy)
    }
}

/// Where to install: the given folder (made absolute), else the default below
/// `%LOCALAPPDATA%`, which an isolated run must never use.
fn resolve_target(
    explicit: Option<&Path>,
    local_app_data: Option<&Path>,
    isolated: bool,
) -> Result<PathBuf, InstallError> {
    if let Some(folder) = explicit {
        return Ok(std::path::absolute(folder).unwrap_or_else(|_| folder.to_path_buf()));
    }
    if isolated {
        return Err(InstallError::TargetRequired);
    }
    let base = local_app_data.filter(|path| path.is_absolute());
    let base = base.ok_or(InstallError::NoLocalAppData)?;
    Ok(DEFAULT_TARGET.iter().fold(base.to_path_buf(), |path, part| path.join(part)))
}

/// `%LOCALAPPDATA%`, without the temp-folder fallback of the settings store: an app
/// must never be installed into the temp folder.
fn local_app_data() -> Option<PathBuf> {
    env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .filter(|path| path.is_absolute())
        .or_else(|| store::known_folder(&FOLDERID_LocalAppData))
}

/// The regular files directly in `folder` (no subfolders, no links), sorted.
fn files_in(folder: &Path) -> io::Result<Vec<PathBuf>> {
    let mut files = Vec::new();
    for entry in fs::read_dir(folder)? {
        let entry = entry?;
        if entry.file_type()?.is_file() {
            files.push(entry.path());
        }
    }
    files.sort();
    Ok(files)
}

/// The files to install; both executables must be among them.
fn release_files(source: &Path) -> Result<Vec<PathBuf>, InstallError> {
    let files = files_in(source).map_err(|error| InstallError::Copy(source.into(), error))?;
    for required in [HOST_EXE_NAME, engine::EXE_NAME] {
        let present = files.iter().any(|file| {
            file.file_name().is_some_and(|name| name.eq_ignore_ascii_case(OsStr::new(required)))
        });
        if !present {
            return Err(InstallError::Incomplete(required));
        }
    }
    Ok(files)
}

/// Whether two folders are the same (both must exist to be compared).
fn same_folder(a: &Path, b: &Path) -> bool {
    match (fs::canonicalize(a), fs::canonicalize(b)) {
        (Ok(a), Ok(b)) => a == b,
        _ => false,
    }
}

fn is_sharing_violation(error: &io::Error) -> bool {
    let code = error.raw_os_error().and_then(|code| u32::try_from(code).ok());
    matches!(code, Some(ERROR_SHARING_VIOLATION | ERROR_LOCK_VIOLATION))
        || error.kind() == io::ErrorKind::PermissionDenied
}

/// Copies `files` into `target`, retrying a file the exiting process still holds.
fn copy_files(files: &[PathBuf], target: &Path) -> Result<(), InstallError> {
    fs::create_dir_all(target).map_err(|error| InstallError::Copy(target.into(), error))?;
    for file in files {
        let Some(name) = file.file_name() else {
            continue;
        };
        let destination = target.join(name);
        let mut attempt = 1;
        loop {
            match fs::copy(file, &destination) {
                Ok(_) => break,
                Err(error) if attempt < COPY_ATTEMPTS && is_sharing_violation(&error) => {
                    attempt += 1;
                    thread::sleep(COPY_RETRY_DELAY);
                }
                Err(error) => return Err(InstallError::Copy(destination, error)),
            }
        }
    }
    Ok(())
}

/// The first top-level window with `class` or `title` (any process).
fn find_window(class: Option<&str>, title: Option<&str>) -> Option<HWND> {
    let class = class.map(to_wide);
    let title = title.map(to_wide);
    let pointer = |text: &Option<Vec<u16>>| text.as_ref().map_or(ptr::null(), Vec::as_ptr);
    // SAFETY: both strings are NUL-terminated or null; null parent and child mean the
    // first matching top-level window.
    let window = unsafe {
        FindWindowExW(ptr::null_mut(), ptr::null_mut(), pointer(&class), pointer(&title))
    };
    (!window.is_null()).then_some(window)
}

/// Whether the host (its window or its single-instance mutex) or an engine window of
/// this isolation is still around.
fn anything_running(isolation: Option<&Isolation>) -> bool {
    let host_window = find_window(Some(&isolated(ipc::WINDOW_CLASS, isolation)), None);
    let engine_window = find_window(None, Some(&isolated(engine::WINDOW_TITLE, isolation)));
    // The mutex outlives the window until the process has exited; a new instance
    // started before that would only forward to a window that is gone.
    let mutex_held = InstanceLock::acquire(&isolated(single_instance::MUTEX_NAME, isolation))
        .is_ok_and(|lock| lock.already_running);
    host_window.is_some() || engine_window.is_some() || mutex_held
}

/// Asks a running instance (wherever it was started from) to quit and waits until
/// host and engine are gone. A lone engine (no host) is asked directly.
fn stop_running(isolation: Option<&Isolation>) -> Result<(), InstallError> {
    let class = isolated(ipc::WINDOW_CLASS, isolation);
    if find_window(Some(&class), None).is_some() {
        // Any copy of this app counts, not only one running from the target folder.
        let target = Target { image: None, attempts: 1, ..Target::primary(&class) };
        if single_instance::forward_to(&target, Action::Quit)
            == Forwarded::Delivered(ipc::QUIT_REFUSED)
        {
            return Err(InstallError::Busy);
        }
    } else if let Some(engine) = find_window(None, Some(&isolated(engine::WINDOW_TITLE, isolation)))
    {
        // SAFETY: posting to a window handle has no memory-safety preconditions; a
        // busy engine ignores it.
        unsafe { PostMessageW(engine, engine::MSG_QUIT, 0, 0) };
    }
    let started = Instant::now();
    while anything_running(isolation) {
        if started.elapsed() > QUIT_TIMEOUT {
            return Err(InstallError::StillRunning);
        }
        thread::sleep(POLL_INTERVAL);
    }
    Ok(())
}

/// A PowerShell single-quoted string literal. PowerShell also takes the typographic
/// single quotes as quote characters, so all of them are doubled.
fn powershell_literal(text: &str) -> String {
    let mut literal = String::from("'");
    for c in text.chars() {
        if matches!(c, '\'' | '\u{2018}' | '\u{2019}' | '\u{201A}' | '\u{201B}') {
            literal.push(c);
        }
        literal.push(c);
    }
    literal.push('\'');
    literal
}

/// The PowerShell script that writes the shortcut `link` to `exe`.
fn shortcut_script(link: &str, exe: &str, folder: &str, description: &str) -> String {
    format!(
        "$s = (New-Object -ComObject WScript.Shell).CreateShortcut({}); \
         $s.TargetPath = {}; $s.WorkingDirectory = {}; $s.Description = {}; $s.Save()",
        powershell_literal(link),
        powershell_literal(exe),
        powershell_literal(folder),
        powershell_literal(description),
    )
}

/// Standard base64 (with padding), for PowerShell's `-EncodedCommand`.
fn base64(bytes: &[u8]) -> String {
    const ALPHABET: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::with_capacity(bytes.len().div_ceil(3) * 4);
    for chunk in bytes.chunks(3) {
        let b = [chunk[0], *chunk.get(1).unwrap_or(&0), *chunk.get(2).unwrap_or(&0)];
        let n = (u32::from(b[0]) << 16) | (u32::from(b[1]) << 8) | u32::from(b[2]);
        for (i, shift) in [18, 12, 6, 0].into_iter().enumerate() {
            if i <= chunk.len() {
                out.push(char::from(ALPHABET[usize::try_from((n >> shift) & 63).unwrap_or(0)]));
            } else {
                out.push('=');
            }
        }
    }
    out
}

/// `powershell.exe -NoProfile -NonInteractive -EncodedCommand <script>` from the
/// Windows folder (never from `PATH`), without a console window. The encoded form
/// (base64 of UTF-16LE) needs no quoting, whatever the paths contain.
fn powershell_command(script: &str) -> Command {
    let root =
        env::var_os("SystemRoot").map_or_else(|| PathBuf::from(r"C:\Windows"), PathBuf::from);
    let exe = root.join(r"System32\WindowsPowerShell\v1.0\powershell.exe");
    let bytes: Vec<u8> = script.encode_utf16().flat_map(u16::to_le_bytes).collect();
    let mut command = Command::new(exe);
    command
        .args(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand"])
        .arg(base64(&bytes))
        .creation_flags(CREATE_NO_WINDOW);
    command
}

/// The user's Start-menu programs folder.
fn start_menu_programs() -> Option<PathBuf> {
    store::known_folder(&FOLDERID_Programs).or_else(|| {
        env::var_os("APPDATA")
            .map(|app_data| Path::new(&app_data).join(r"Microsoft\Windows\Start Menu\Programs"))
    })
}

/// `path` as text for the shortcut script.
fn text(path: &Path) -> Result<&str, InstallError> {
    path.to_str().ok_or_else(|| InstallError::UnicodePath(path.into()))
}

/// Writes the Start-menu shortcut to `exe` and removes those of earlier versions.
fn write_shortcut(exe: &Path) -> Result<(), InstallError> {
    let programs = start_menu_programs().ok_or_else(|| {
        InstallError::Shortcut(crate::ui::current_texts().install.no_start_menu.into())
    })?;
    let link = programs.join(SHORTCUT_NAME);
    let folder = exe.parent().unwrap_or(exe);
    let script = shortcut_script(text(&link)?, text(exe)?, text(folder)?, ui::PRODUCT);
    let status = powershell_command(&script)
        .status()
        .map_err(|error| InstallError::Shortcut(error.to_string()))?;
    if !status.success() || !link.is_file() {
        return Err(InstallError::Shortcut(format!("PowerShell: {status}")));
    }
    for legacy in LEGACY_SHORTCUT_NAMES {
        // Ours; a missing one is fine.
        let _ = fs::remove_file(programs.join(legacy));
    }
    Ok(())
}

/// Copies the release from `source` into the target folder after stopping a running
/// instance; outside test isolation also sets up autostart and the Start menu and
/// starts the installed app. Returns the installed host executable.
pub fn install(
    source: &Path,
    target: Option<&Path>,
    isolation: Option<&Isolation>,
) -> Result<PathBuf, InstallError> {
    let target = resolve_target(target, local_app_data().as_deref(), isolation.is_some())?;
    let files = release_files(source)?;
    stop_running(isolation)?;
    if !same_folder(source, &target) {
        copy_files(&files, &target)?;
    }
    let exe = target.join(HOST_EXE_NAME);
    if isolation.is_some() {
        return Ok(exe);
    }

    // The settings are only read; a missing file means the default (on).
    let enabled = SettingsStore::user_default().load().autostart;
    autostart::set_enabled(enabled, &exe).map_err(InstallError::Autostart)?;
    write_shortcut(&exe)?;
    Command::new(&exe)
        .arg("--background")
        .current_dir(&target)
        .spawn()
        .map_err(InstallError::Start)?;
    Ok(exe)
}

/// `--install`: installs from the folder of this executable.
pub fn run(target: Option<&Path>, isolation: Option<&Isolation>) -> Result<PathBuf, InstallError> {
    let exe = env::current_exe().map_err(|_| InstallError::NoSource)?;
    let source = exe.parent().ok_or(InstallError::NoSource)?;
    install(source, target, isolation)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::store::tests::TempDir;

    #[test]
    fn target_folder_resolution() {
        let local = Path::new(r"C:\Users\Ada\AppData\Local");
        let default = resolve_target(None, Some(local), false).unwrap();
        assert_eq!(default, Path::new(r"C:\Users\Ada\AppData\Local\Programs\NazarCapture"));
        let explicit = resolve_target(Some(Path::new(r"D:\Apps\Nazar")), Some(local), true);
        assert_eq!(explicit.unwrap(), Path::new(r"D:\Apps\Nazar"), "given folder always wins");
        let relative = resolve_target(Some(Path::new("ziel")), None, false).unwrap();
        assert!(relative.is_absolute() && relative.ends_with("ziel"));
        assert!(matches!(
            resolve_target(None, Some(local), true),
            Err(InstallError::TargetRequired)
        ));
        assert!(matches!(resolve_target(None, None, false), Err(InstallError::NoLocalAppData)));
        let relative_base = resolve_target(None, Some(Path::new("Local")), false);
        assert!(matches!(relative_base, Err(InstallError::NoLocalAppData)));
    }

    #[test]
    fn file_list_is_flat_and_needs_both_executables() {
        let dir = TempDir::new();
        fs::write(dir.file("NazarCapture.exe"), b"host").unwrap();
        fs::write(dir.file("LIESMICH.txt"), b"text").unwrap();
        fs::create_dir(dir.file("sub")).unwrap();
        fs::write(dir.file("sub").join("nested.dll"), b"x").unwrap();
        assert!(matches!(
            release_files(&dir.0),
            Err(InstallError::Incomplete("NazarCapture.Engine.exe"))
        ));
        fs::write(dir.file("nazarcapture.engine.EXE"), b"engine").unwrap();
        let files = release_files(&dir.0).unwrap();
        let names: Vec<_> = files.iter().map(|f| f.file_name().unwrap().to_owned()).collect();
        assert_eq!(names, ["LIESMICH.txt", "NazarCapture.exe", "nazarcapture.engine.EXE"]);
    }

    #[test]
    fn shortcut_script_quotes_every_path() {
        let script = shortcut_script(
            r"C:\Users\O'Neil\Start Menu\Nazar Capture.lnk",
            r"C:\Apps\Nazar Capture\NazarCapture.exe",
            r"C:\Apps\Nazar Capture",
            "Nazar Capture",
        );
        assert_eq!(
            script,
            "$s = (New-Object -ComObject WScript.Shell).CreateShortcut(\
             'C:\\Users\\O''Neil\\Start Menu\\Nazar Capture.lnk'); \
             $s.TargetPath = 'C:\\Apps\\Nazar Capture\\NazarCapture.exe'; \
             $s.WorkingDirectory = 'C:\\Apps\\Nazar Capture'; \
             $s.Description = 'Nazar Capture'; $s.Save()"
        );
        assert_eq!(powershell_literal("a\u{2019}b$c"), "'a\u{2019}\u{2019}b$c'");
        assert_eq!(powershell_literal(""), "''");
    }

    #[test]
    fn powershell_runs_hidden_with_an_encoded_script() {
        let command = powershell_command("Write-Output 'ä'");
        assert!(Path::new(command.get_program()).ends_with("powershell.exe"));
        let args: Vec<_> = command.get_args().map(|a| a.to_str().unwrap().to_owned()).collect();
        assert_eq!(
            args[..5],
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand"]
        );
        let utf16le: Vec<u8> =
            "Write-Output 'ä'".encode_utf16().flat_map(u16::to_le_bytes).collect();
        assert_eq!(args[5], base64(&utf16le));
    }

    #[test]
    fn base64_matches_the_standard() {
        let cases: [(&[u8], &str); 7] = [
            (b"", ""),
            (b"f", "Zg=="),
            (b"fo", "Zm8="),
            (b"foo", "Zm9v"),
            (b"foob", "Zm9vYg=="),
            (b"fooba", "Zm9vYmE="),
            (b"foobar", "Zm9vYmFy"),
        ];
        for (input, expected) in cases {
            assert_eq!(base64(input), expected);
        }
        assert_eq!(base64(&[0xFF, 0xFE, 0x00]), "//4A");
    }

    #[test]
    fn errors_have_messages_in_every_language() {
        let german = &crate::ui::texts(crate::config::Language::German).install;
        assert!(InstallError::Busy.message_in(german).contains("Aufnahme"));
        assert!(InstallError::Busy.is_busy());
        assert!(!InstallError::StillRunning.is_busy());
        assert!(InstallError::Incomplete("X.exe").message_in(german).contains("X.exe fehlt"));
        assert!(InstallError::TargetRequired.message_in(german).contains("--install <Ordner>"));
        let english = &crate::ui::texts(crate::config::Language::English).install;
        assert!(InstallError::Incomplete("X.exe").message_in(english).contains("X.exe is missing"));
        let error = InstallError::Copy(PathBuf::from(r"C:\a"), io::ErrorKind::NotFound.into());
        let arabic = &crate::ui::texts(crate::config::Language::Arabic).install;
        assert!(error.message_in(arabic).contains(r"C:\a"), "{}", error.message_in(arabic));
    }

    /// Installs a fake release into a temp folder under test isolation: no instance
    /// runs, nothing is started, no registry or Start menu is touched. An update over
    /// an older install replaces its files and leaves foreign files alone.
    #[test]
    fn isolated_install_copies_the_release() {
        let dir = TempDir::new();
        let source = dir.file("release");
        let target = dir.file("ziel");
        fs::create_dir_all(source.join("sub")).unwrap();
        fs::write(source.join(HOST_EXE_NAME), b"host 1.1.3").unwrap();
        fs::write(source.join(engine::EXE_NAME), b"engine 1.1.3").unwrap();
        fs::write(source.join("engine.dll"), b"lib").unwrap();
        fs::write(source.join("sub").join("skip.txt"), b"nested").unwrap();
        fs::create_dir_all(&target).unwrap();
        fs::write(target.join(HOST_EXE_NAME), b"host 1.1.2").unwrap();
        fs::write(target.join("settings-backup.ini"), b"mine").unwrap();

        let isolation = Isolation::for_test(&format!("install-{}", std::process::id()), &dir.0);
        assert!(matches!(
            install(&source, None, Some(&isolation)),
            Err(InstallError::TargetRequired)
        ));
        let exe = install(&source, Some(&target), Some(&isolation)).unwrap();
        assert_eq!(exe, target.join(HOST_EXE_NAME));
        assert_eq!(fs::read(target.join(HOST_EXE_NAME)).unwrap(), b"host 1.1.3");
        assert_eq!(fs::read(target.join(engine::EXE_NAME)).unwrap(), b"engine 1.1.3");
        assert_eq!(fs::read(target.join("engine.dll")).unwrap(), b"lib");
        assert!(!target.join("sub").exists(), "subfolders are not copied");
        assert_eq!(fs::read(target.join("settings-backup.ini")).unwrap(), b"mine");
        assert!(!isolation.settings.exists(), "the settings are never touched");

        // Installing from the target folder itself only refreshes the rest.
        assert_eq!(install(&target, Some(&target), Some(&isolation)).unwrap(), exe);
        assert!(same_folder(&target, &target.join(".")));
        assert!(!same_folder(&source, &target));
    }
}
