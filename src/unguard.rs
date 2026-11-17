//! Windows' `RedirectionGuard`, inherited from the program that started us, and the way out of it.
//!
//! A process with this mitigation cannot follow a junction or symbolic link that a user without admin
//! rights created (error 448, "untrusted mount point"). Nazar Capture never turns it on itself, but a
//! process inherits it from its parent: Inno Setup 6.7 and later started the app that way after an
//! install or an update, and an older copy started the next update setup with it, too. Such a copy
//! keeps the guard until it ends, and so does the engine it starts, and the update setup the engine
//! starts.
//!
//! So a copy that starts with the guard and finds no other copy running asks the Windows shell to
//! start it once more and ends. The shell (explorer.exe) starts programs without the guard. This uses
//! the documented shell automation interfaces, the way Raymond Chen shows for starting an unelevated
//! program from an elevated one: `IShellWindows::FindWindowSW(SWC_DESKTOP)`, the desktop's
//! `IShellBrowser` and `IShellView`, its `IShellFolderViewDual`, and `IShellDispatch2::ShellExecute`.
//! The new process is a real child of explorer.exe; nothing pretends to be another parent.
//!
//! The restarted copy carries [`FROM_SHELL`] on its command line and never restarts again, so there is
//! no loop. The old copy ends only once the new one holds the single-instance mutex ([`handed_over`]);
//! without a shell (a service session, another desktop, Explorer not running), when the shell refuses,
//! or when no new copy shows up in time, it simply runs on with the guard, and the engine copes with
//! that (`engine/Core/LinkSafety.cs`, the fallback). A test instance never restarts: the shell would
//! start it without its isolation variables, as the real app.

use std::ffi::{OsStr, OsString, c_void};
use std::path::Path;
use std::ptr;
use std::time::{Duration, Instant};

use windows_sys::Win32::Foundation::{SysAllocString, SysFreeString};
use windows_sys::Win32::System::Com::{
    CLSCTX_LOCAL_SERVER, COINIT_APARTMENTTHREADED, CoCreateInstance, CoInitializeEx, CoUninitialize,
};
use windows_sys::Win32::System::Threading::{GetCurrentProcess, GetProcessMitigationPolicy};
use windows_sys::core::{BSTR, GUID, HRESULT};

use crate::wide::to_wide;

/// The marker a copy restarted through the shell carries; it never restarts again. Ignored by [`crate::cli`].
pub const FROM_SHELL: &str = "--from-shell";

/// `ProcessRedirectionTrustPolicy` in `PROCESS_MITIGATION_POLICY`.
const PROCESS_REDIRECTION_TRUST_POLICY: i32 = 16;
/// `EnforceRedirectionTrust` in `PROCESS_MITIGATION_REDIRECTION_TRUST_POLICY`.
const ENFORCE_REDIRECTION_TRUST: u32 = 1;

/// Whether this process runs with `RedirectionGuard` enforced; `None` if Windows cannot tell (before
/// Windows 11 22H2 the policy does not exist).
pub fn enforced() -> Option<bool> {
    let mut flags = 0u32;
    // SAFETY: `flags` is the 4-byte policy structure the call fills; the pseudo handle needs no closing.
    let ok = unsafe {
        GetProcessMitigationPolicy(
            GetCurrentProcess(),
            PROCESS_REDIRECTION_TRUST_POLICY,
            (&raw mut flags).cast(),
            size_of::<u32>(),
        )
    };
    (ok != 0).then_some(flags & ENFORCE_REDIRECTION_TRUST != 0)
}

/// Whether a copy that found no other copy running should restart itself through the shell.
pub fn restart_wanted(guard: Option<bool>, isolated: bool, args: &[OsString]) -> bool {
    guard == Some(true) && !isolated && !args.iter().any(|arg| arg == FROM_SHELL)
}

/// How long a copy that asked the shell to start it again waits for the new copy to take the instance over.
/// The shell accepting the request is no proof: Explorer may still drop it. Generous for a slow start at sign-in.
pub const HANDOVER_WAIT: Duration = Duration::from_secs(10);
/// How often the waiting copy looks.
pub const HANDOVER_POLL: Duration = Duration::from_millis(100);

/// Whether `taken_over` turns true within `wait`, asked every `poll`. The waiting copy ends only then; otherwise it
/// runs on itself (with the guard), so Nazar Capture never just disappears. Should the new copy come later after
/// all, it finds this one running and hands its action over (single instance) instead of running twice.
pub fn handed_over(mut taken_over: impl FnMut() -> bool, wait: Duration, poll: Duration) -> bool {
    let deadline = Instant::now() + wait;
    loop {
        if taken_over() {
            return true;
        }
        if Instant::now() >= deadline {
            return false;
        }
        std::thread::sleep(poll);
    }
}

/// The command line of the restarted copy: the same arguments plus [`FROM_SHELL`], each quoted as
/// `CommandLineToArgvW` reads it back.
pub fn restart_arguments(args: &[OsString]) -> OsString {
    command_line(args.iter().map(OsString::as_os_str).chain([OsStr::new(FROM_SHELL)]))
}

/// `args` joined into one command line, each quoted as `CommandLineToArgvW` reads it back.
fn command_line<'a>(args: impl IntoIterator<Item = &'a OsStr>) -> OsString {
    let mut line = OsString::new();
    for arg in args {
        if !line.is_empty() {
            line.push(" ");
        }
        line.push(quoted(arg));
    }
    line
}

/// `arg` as one `CommandLineToArgvW` argument: unchanged unless it is empty or holds a space, tab or quote.
fn quoted(arg: &OsStr) -> OsString {
    let text = arg.to_string_lossy();
    if !text.is_empty() && !text.contains([' ', '\t', '"']) {
        return arg.to_owned();
    }
    let mut out = String::from("\"");
    let mut backslashes = 0;
    for c in text.chars() {
        match c {
            '\\' => backslashes += 1,
            '"' => {
                out.push_str(&"\\".repeat(backslashes * 2 + 1));
                out.push('"');
                backslashes = 0;
            }
            _ => {
                out.push_str(&"\\".repeat(backslashes));
                out.push(c);
                backslashes = 0;
            }
        }
    }
    out.push_str(&"\\".repeat(backslashes * 2));
    out.push('"');
    out.into()
}

/// Asks the shell (explorer.exe) to start `file` with `arguments` in `directory`. `false` when there
/// is no shell to ask or it refused; nothing was started then.
pub fn shell_execute(file: &Path, arguments: &OsStr, directory: &Path, show: i32) -> bool {
    // SAFETY: COM is initialised for this thread here and uninitialised below only if this call did it.
    let init = unsafe { CoInitializeEx(ptr::null(), COINIT_APARTMENTTHREADED as u32) };
    // SAFETY: plain COM calls on interfaces obtained here; every pointer is checked before use and
    // released by `Com`'s drop.
    let started = unsafe { desktop_shell_execute(file, arguments, directory, show) }.is_ok();
    if init >= 0 {
        // SAFETY: balances the successful CoInitializeEx above (S_OK or S_FALSE).
        unsafe { CoUninitialize() };
    }
    started
}

// --------------------------------------------------------------------------- COM plumbing

type Raw = *mut c_void;

const CLSID_SHELL_WINDOWS: GUID = GUID::from_u128(0x9ba0_5972_f6a8_11cf_a442_00a0_c90a_8f39);
const IID_ISHELL_WINDOWS: GUID = GUID::from_u128(0x85cb_6900_4d95_11cf_960c_0080_c7f4_ee85);
const IID_ISERVICE_PROVIDER: GUID = GUID::from_u128(0x6d51_40c1_7436_11ce_8034_00aa_0060_09fa);
const SID_STOP_LEVEL_BROWSER: GUID = GUID::from_u128(0x4c96_be40_915c_11cf_99d3_00aa_004a_e837);
const IID_ISHELL_BROWSER: GUID = GUID::from_u128(0x0002_14e2_0000_0000_c000_0000_0000_0046);
const IID_IDISPATCH: GUID = GUID::from_u128(0x0002_0400_0000_0000_c000_0000_0000_0046);
const IID_ISHELL_FOLDER_VIEW_DUAL: GUID =
    GUID::from_u128(0xe7a1_af80_4d96_11cf_960c_0080_c7f4_ee85);
const IID_ISHELL_DISPATCH2: GUID = GUID::from_u128(0xa4c6_892c_3ba9_11d2_9dea_00c0_4fb1_6162);

const CSIDL_DESKTOP: i32 = 0;
const SWC_DESKTOP: i32 = 8;
const SWFO_NEEDDISPATCH: i32 = 1;
const SVGIO_BACKGROUND: u32 = 0;
const VT_I4: u16 = 3;
const VT_BSTR: u16 = 8;

/// Vtable slots (`IUnknown` 0–2, `IDispatch` 3–6, `IOleWindow` 3–4), from `exdisp.idl`, `shobjidl.idl` and `shldisp.idl`.
const QUERY_INTERFACE: usize = 0;
const RELEASE: usize = 2;
const SHELL_WINDOWS_FIND_WINDOW_SW: usize = 15;
const SERVICE_PROVIDER_QUERY_SERVICE: usize = 3;
const SHELL_BROWSER_QUERY_ACTIVE_SHELL_VIEW: usize = 15;
const SHELL_VIEW_GET_ITEM_OBJECT: usize = 15;
const FOLDER_VIEW_DUAL_GET_APPLICATION: usize = 7;
const SHELL_DISPATCH2_SHELL_EXECUTE: usize = 31;

/// A `VARIANT` of the two kinds used here (`VT_I4`, `VT_BSTR`) or empty: 16 bytes on x86, 24 on x64.
#[repr(C)]
#[derive(Clone, Copy, Debug)]
struct Variant {
    vt: u16,
    reserved: [u16; 3],
    data: [usize; 2],
}

impl Variant {
    const EMPTY: Self = Self { vt: 0, reserved: [0; 3], data: [0; 2] };

    fn int(value: i32) -> Self {
        // The 32-bit value sits in the first bytes of the union.
        Self { vt: VT_I4, data: [value.cast_unsigned() as usize, 0], ..Self::EMPTY }
    }

    fn string(value: &Bstr) -> Self {
        Self { vt: VT_BSTR, data: [value.0 as usize, 0], ..Self::EMPTY }
    }
}

/// An owned `BSTR`, freed on drop.
struct Bstr(BSTR);

impl Bstr {
    fn new(text: &OsStr) -> Result<Self, ()> {
        let wide = to_wide(text);
        // SAFETY: `wide` is NUL-terminated and outlives the call, which copies it.
        let value = unsafe { SysAllocString(wide.as_ptr()) };
        if value.is_null() { Err(()) } else { Ok(Self(value)) }
    }
}

impl Drop for Bstr {
    fn drop(&mut self) {
        // SAFETY: allocated by SysAllocString and freed once.
        unsafe { SysFreeString(self.0) };
    }
}

/// An owned COM interface pointer, released on drop.
struct Com(Raw);

impl Com {
    /// Takes ownership of a pointer an interface call returned; an error if the call failed or gave none.
    fn take(hr: HRESULT, raw: Raw) -> Result<Self, ()> {
        if hr >= 0 && !raw.is_null() {
            Ok(Self(raw))
        } else {
            if !raw.is_null() {
                drop(Self(raw));
            }
            Err(())
        }
    }

    /// The function in vtable slot `index`.
    ///
    /// # Safety
    ///
    /// The interface must have at least `index + 1` slots, and the caller must call it with its
    /// real signature.
    unsafe fn slot(&self, index: usize) -> *const c_void {
        // SAFETY: a COM object starts with a pointer to its vtable; the caller vouches for `index`.
        unsafe { *(*self.0.cast::<*const *const c_void>()).add(index) }
    }

    /// # Safety
    ///
    /// `self` must be a live interface pointer.
    unsafe fn query(&self, iid: &GUID) -> Result<Self, ()> {
        type QueryInterface = unsafe extern "system" fn(Raw, *const GUID, *mut Raw) -> HRESULT;
        let mut out: Raw = ptr::null_mut();
        // SAFETY: slot 0 of every interface is IUnknown::QueryInterface.
        let hr = unsafe {
            let call: QueryInterface = std::mem::transmute(self.slot(QUERY_INTERFACE));
            call(self.0, iid, &raw mut out)
        };
        Self::take(hr, out)
    }
}

impl Drop for Com {
    fn drop(&mut self) {
        type Release = unsafe extern "system" fn(Raw) -> u32;
        // SAFETY: slot 2 of every interface is IUnknown::Release; this owns one reference.
        unsafe {
            let call: Release = std::mem::transmute(self.slot(RELEASE));
            call(self.0);
        }
    }
}

/// The desktop's `IShellDispatch2`, then its `ShellExecute`.
///
/// # Safety
///
/// COM must be initialised on this thread.
unsafe fn desktop_shell_execute(
    file: &Path,
    arguments: &OsStr,
    directory: &Path,
    show: i32,
) -> Result<(), ()> {
    type FindWindowSw = unsafe extern "system" fn(
        Raw,
        *const Variant,
        *const Variant,
        i32,
        *mut i32,
        i32,
        *mut Raw,
    ) -> HRESULT;
    type QueryService =
        unsafe extern "system" fn(Raw, *const GUID, *const GUID, *mut Raw) -> HRESULT;
    type QueryActiveShellView = unsafe extern "system" fn(Raw, *mut Raw) -> HRESULT;
    type GetItemObject = unsafe extern "system" fn(Raw, u32, *const GUID, *mut Raw) -> HRESULT;
    type GetApplication = unsafe extern "system" fn(Raw, *mut Raw) -> HRESULT;
    type ShellExecute =
        unsafe extern "system" fn(Raw, BSTR, Variant, Variant, Variant, Variant) -> HRESULT;

    // SAFETY: (whole body) each call goes through the slot the interface's IDL gives it, with that
    // signature, on a pointer `Com::take` checked; out pointers are local.
    unsafe {
        let mut raw: Raw = ptr::null_mut();
        let hr = CoCreateInstance(
            &CLSID_SHELL_WINDOWS,
            ptr::null_mut(),
            CLSCTX_LOCAL_SERVER,
            &IID_ISHELL_WINDOWS,
            &raw mut raw,
        );
        let windows = Com::take(hr, raw)?;

        let location = Variant::int(CSIDL_DESKTOP);
        let root = Variant::EMPTY;
        let mut hwnd = 0i32;
        let mut raw: Raw = ptr::null_mut();
        let call: FindWindowSw = std::mem::transmute(windows.slot(SHELL_WINDOWS_FIND_WINDOW_SW));
        // S_FALSE with no object when there is no desktop window: `take` turns that into an error.
        let hr = call(
            windows.0,
            &raw const location,
            &raw const root,
            SWC_DESKTOP,
            &raw mut hwnd,
            SWFO_NEEDDISPATCH,
            &raw mut raw,
        );
        let desktop = Com::take(hr, raw)?;

        let provider = desktop.query(&IID_ISERVICE_PROVIDER)?;
        let mut raw: Raw = ptr::null_mut();
        let call: QueryService = std::mem::transmute(provider.slot(SERVICE_PROVIDER_QUERY_SERVICE));
        let hr = call(provider.0, &SID_STOP_LEVEL_BROWSER, &IID_ISHELL_BROWSER, &raw mut raw);
        let browser = Com::take(hr, raw)?;

        let mut raw: Raw = ptr::null_mut();
        let call: QueryActiveShellView =
            std::mem::transmute(browser.slot(SHELL_BROWSER_QUERY_ACTIVE_SHELL_VIEW));
        let hr = call(browser.0, &raw mut raw);
        let view = Com::take(hr, raw)?;

        let mut raw: Raw = ptr::null_mut();
        let call: GetItemObject = std::mem::transmute(view.slot(SHELL_VIEW_GET_ITEM_OBJECT));
        let hr = call(view.0, SVGIO_BACKGROUND, &IID_IDISPATCH, &raw mut raw);
        let background = Com::take(hr, raw)?;

        let folder_view = background.query(&IID_ISHELL_FOLDER_VIEW_DUAL)?;
        let mut raw: Raw = ptr::null_mut();
        let call: GetApplication =
            std::mem::transmute(folder_view.slot(FOLDER_VIEW_DUAL_GET_APPLICATION));
        let hr = call(folder_view.0, &raw mut raw);
        let application = Com::take(hr, raw)?;
        let shell = application.query(&IID_ISHELL_DISPATCH2)?;

        let file = Bstr::new(file.as_os_str())?;
        let arguments = Bstr::new(arguments)?;
        let directory = Bstr::new(directory.as_os_str())?;
        let call: ShellExecute = std::mem::transmute(shell.slot(SHELL_DISPATCH2_SHELL_EXECUTE));
        let hr = call(
            shell.0,
            file.0,
            Variant::string(&arguments),
            Variant::string(&directory),
            Variant::EMPTY, // the default verb
            Variant::int(show),
        );
        if hr >= 0 { Ok(()) } else { Err(()) }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn args(list: &[&str]) -> Vec<OsString> {
        list.iter().map(OsString::from).collect()
    }

    #[test]
    fn restart_only_with_the_guard_outside_tests_and_once() {
        assert!(restart_wanted(Some(true), false, &args(&["--background"])));
        assert!(
            !restart_wanted(Some(false), false, &args(&["--background"])),
            "no guard, no restart"
        );
        assert!(!restart_wanted(None, false, &[]), "Windows cannot tell: run on");
        assert!(!restart_wanted(Some(true), true, &[]), "a test instance would lose its isolation");
        assert!(
            !restart_wanted(Some(true), false, &args(&["--background", FROM_SHELL])),
            "the restarted copy never restarts again"
        );
    }

    #[test]
    fn restart_keeps_the_arguments_and_adds_the_marker() {
        assert_eq!(restart_arguments(&[]), OsString::from(FROM_SHELL));
        assert_eq!(
            restart_arguments(&args(&["--background"])),
            OsString::from("--background --from-shell")
        );
        assert_eq!(
            restart_arguments(&args(&["--install", r"C:\Program Files\Nazar"])),
            OsString::from(r#"--install "C:\Program Files\Nazar" --from-shell"#)
        );
        assert_eq!(restart_arguments(&args(&[""])), OsString::from(r#""" --from-shell"#));
        assert_eq!(
            restart_arguments(&args(&[r#"a "b" c\"#])),
            OsString::from(r#""a \"b\" c\\" --from-shell"#)
        );
    }

    #[test]
    fn the_old_copy_ends_only_when_the_new_one_took_over() {
        let mut asked = 0;
        assert!(
            handed_over(
                || {
                    asked += 1;
                    asked == 3
                },
                Duration::from_secs(5),
                Duration::from_millis(1)
            ),
            "the new copy shows up on the third look"
        );
        assert_eq!(asked, 3);

        let started = Instant::now();
        assert!(
            !handed_over(|| false, Duration::from_millis(50), Duration::from_millis(5)),
            "no new copy: the old one runs on"
        );
        assert!(started.elapsed() >= Duration::from_millis(50), "it waited the whole time");
    }

    #[test]
    fn the_marker_is_no_action() {
        use crate::cli::{Action, Command, parse};
        assert_eq!(parse(args(&["--background", FROM_SHELL])), Command::Run(Action::Background));
        assert_eq!(parse(args(&[FROM_SHELL])), Command::Run(Action::ShowMain));
    }

    #[test]
    fn variants_have_the_windows_layout() {
        assert_eq!(size_of::<Variant>(), if cfg!(target_pointer_width = "64") { 24 } else { 16 });
        assert_eq!(Variant::int(-1).data[0] & 0xFFFF_FFFF, 0xFFFF_FFFF);
    }

    /// Measures the way out on the real desktop: this test process turns `RedirectionGuard` on for
    /// itself; a child it starts directly inherits the guard, a child the shell starts has none.
    /// Needs Windows 11 22H2+ with Explorer running and starts two hidden PowerShell processes, so it
    /// is not part of the normal run: `cargo test unguard -- --ignored --nocapture`.
    #[test]
    #[ignore = "needs the real desktop's Explorer; changes this test process's mitigation for good"]
    fn the_shell_starts_children_without_the_guard() {
        use std::os::windows::process::CommandExt;
        use std::time::{Duration, Instant};
        use windows_sys::Win32::System::Threading::{CREATE_NO_WINDOW, SetProcessMitigationPolicy};

        let policy = ENFORCE_REDIRECTION_TRUST;
        // SAFETY: a 4-byte policy structure for this process.
        let ok = unsafe {
            SetProcessMitigationPolicy(
                PROCESS_REDIRECTION_TRUST_POLICY,
                (&raw const policy).cast(),
                size_of::<u32>(),
            )
        };
        assert_ne!(ok, 0, "turning the guard on for the test process");
        assert_eq!(enforced(), Some(true));

        let folder = std::env::temp_dir().join(format!("nazar-unguard-{}", std::process::id()));
        std::fs::create_dir_all(&folder).unwrap();
        let probe = |name: &str| {
            let out = folder.join(name);
            let text = format!(
                "Add-Type -Namespace P -Name M -MemberDefinition '[DllImport(\"kernel32.dll\")] public static extern bool \
                 GetProcessMitigationPolicy(System.IntPtr p, int k, out uint f, System.UIntPtr l); [DllImport(\"kernel32.dll\")] \
                 public static extern System.IntPtr GetCurrentProcess();'; $f = 0; \
                 $ok = [P.M]::GetProcessMitigationPolicy([P.M]::GetCurrentProcess(), 16, [ref]$f, [System.UIntPtr]::new(4)); \
                 Set-Content -LiteralPath '{}' -Value $(if ($ok) {{ $f -band 1 }} else {{ 'failed' }})",
                out.display()
            );
            // -EncodedCommand: UTF-16LE in Base64, so no quoting rule of PowerShell's command line applies.
            let bytes: Vec<u8> = text.encode_utf16().flat_map(u16::to_le_bytes).collect();
            (out, base64(&bytes))
        };
        let read = |out: &Path| {
            let deadline = Instant::now() + Duration::from_secs(60);
            loop {
                if let Ok(text) = std::fs::read_to_string(out) {
                    if !text.trim().is_empty() {
                        return text.trim().to_owned();
                    }
                }
                assert!(Instant::now() < deadline, "no answer in {}", out.display());
                std::thread::sleep(Duration::from_millis(200));
            }
        };
        let powershell = Path::new(&std::env::var_os("SystemRoot").unwrap())
            .join(r"System32\WindowsPowerShell\v1.0\powershell.exe");

        let (direct_out, script) = probe("direct.txt");
        std::process::Command::new(&powershell)
            .args(["-NoProfile", "-NonInteractive", "-EncodedCommand", &script])
            .creation_flags(CREATE_NO_WINDOW)
            .status()
            .unwrap();
        assert_eq!(read(&direct_out), "1", "a child started directly inherits the guard");

        let (shell_out, script) = probe("shell.txt");
        let arguments = command_line(
            ["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", &script]
                .map(OsStr::new),
        );
        assert!(shell_execute(&powershell, &arguments, &folder, 0), "the shell took the start");
        assert_eq!(read(&shell_out), "0", "a child the shell started has no guard");
        let _ = std::fs::remove_dir_all(&folder);
    }

    fn base64(bytes: &[u8]) -> String {
        const DIGITS: &[u8; 64] =
            b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        let mut out = String::new();
        for chunk in bytes.chunks(3) {
            let n =
                chunk.iter().enumerate().fold(0u32, |n, (i, &b)| n | u32::from(b) << (16 - 8 * i));
            for i in 0..4 {
                out.push(if i <= chunk.len() {
                    char::from(DIGITS[(n >> (18 - 6 * i) & 63) as usize])
                } else {
                    '='
                });
            }
        }
        out
    }

    #[test]
    fn base64_of_the_probe() {
        assert_eq!(base64(b"Man"), "TWFu");
        assert_eq!(base64(b"Ma"), "TWE=");
        assert_eq!(base64(b"M"), "TQ==");
    }
}
