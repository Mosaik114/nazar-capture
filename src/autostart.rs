//! "Start with Windows": the per-user `Run` registry value.

use std::ffi::{OsStr, OsString};
use std::io;
use std::path::Path;
use std::ptr;

use windows_sys::Win32::Foundation::{ERROR_FILE_NOT_FOUND, ERROR_SUCCESS, WIN32_ERROR};
use windows_sys::Win32::System::Registry::{
    HKEY, HKEY_CURRENT_USER, KEY_SET_VALUE, REG_OPTION_NON_VOLATILE, REG_SZ, RegCloseKey,
    RegCreateKeyExW, RegDeleteValueW, RegSetValueExW,
};

use crate::wide::to_wide;

/// The per-user autostart key.
const RUN_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
/// Name of this app's value under [`RUN_KEY`]: the product name, which Windows shows
/// in the list of startup apps.
const VALUE_NAME: &str = crate::ui::PRODUCT;
/// Value names used by earlier versions; removed whenever autostart is written.
const LEGACY_VALUE_NAMES: [&str; 2] = ["Mosaik Capture", "Mosaiks Aufnahme"];

/// The command Windows runs at logon: the quoted executable plus `--background`.
pub fn command_line(exe: &Path) -> OsString {
    let mut command = OsString::from("\"");
    command.push(exe);
    command.push("\" --background");
    command
}

/// Adds or removes the autostart entry for `exe`. Removing a missing entry succeeds.
/// The entries of earlier versions are removed in both cases, so the app never starts
/// twice at logon.
pub fn set_enabled(enabled: bool, exe: &Path) -> io::Result<()> {
    apply(&RunKey::open()?, enabled, exe)
}

/// The values of a `Run` key, as far as autostart needs them.
trait RunValues {
    fn set(&self, name: &str, value: &OsStr) -> io::Result<()>;
    /// Removes a value; a missing one is success.
    fn delete(&self, name: &str) -> io::Result<()>;
}

/// Brings `key` to exactly one entry of this app (or none): the old names go first,
/// then the current one is written or removed.
fn apply(key: &impl RunValues, enabled: bool, exe: &Path) -> io::Result<()> {
    for legacy in LEGACY_VALUE_NAMES {
        key.delete(legacy)?;
    }
    if enabled { key.set(VALUE_NAME, &command_line(exe)) } else { key.delete(VALUE_NAME) }
}

/// An open handle to [`RUN_KEY`], closed on drop.
struct RunKey(HKEY);

impl RunKey {
    fn open() -> io::Result<Self> {
        let path = to_wide(RUN_KEY);
        let mut key: HKEY = ptr::null_mut();
        // SAFETY: `path` is NUL-terminated; `key` receives the handle on success; the
        // other pointers are documented as optional.
        let status = unsafe {
            RegCreateKeyExW(
                HKEY_CURRENT_USER,
                path.as_ptr(),
                0,
                ptr::null(),
                REG_OPTION_NON_VOLATILE,
                KEY_SET_VALUE,
                ptr::null(),
                &raw mut key,
                ptr::null_mut(),
            )
        };
        check(status)?;
        Ok(Self(key))
    }
}

impl RunValues for RunKey {
    fn set(&self, name: &str, value: &OsStr) -> io::Result<()> {
        let name = to_wide(name);
        let data: Vec<u8> = to_wide(value).iter().flat_map(|unit| unit.to_le_bytes()).collect();
        let size = u32::try_from(data.len()).map_err(|_| io::ErrorKind::InvalidInput)?;
        // SAFETY: `self.0` is an open key; `name` is NUL-terminated; `data` holds a
        // NUL-terminated REG_SZ string of exactly `size` bytes.
        let status =
            unsafe { RegSetValueExW(self.0, name.as_ptr(), 0, REG_SZ, data.as_ptr(), size) };
        check(status)
    }

    fn delete(&self, name: &str) -> io::Result<()> {
        let name = to_wide(name);
        // SAFETY: `self.0` is an open key; `name` is NUL-terminated.
        let status = unsafe { RegDeleteValueW(self.0, name.as_ptr()) };
        if status == ERROR_FILE_NOT_FOUND {
            return Ok(());
        }
        check(status)
    }
}

impl Drop for RunKey {
    fn drop(&mut self) {
        // SAFETY: the handle was opened by `RegCreateKeyExW` and is closed exactly once.
        unsafe { RegCloseKey(self.0) };
    }
}

fn check(status: WIN32_ERROR) -> io::Result<()> {
    if status == ERROR_SUCCESS {
        Ok(())
    } else {
        Err(io::Error::from_raw_os_error(status.cast_signed()))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::RefCell;
    use std::collections::BTreeMap;

    // The registry itself is deliberately not touched by tests.

    /// A `Run` key in memory.
    #[derive(Default)]
    struct FakeRunKey(RefCell<BTreeMap<String, OsString>>);

    impl RunValues for FakeRunKey {
        fn set(&self, name: &str, value: &OsStr) -> io::Result<()> {
            self.0.borrow_mut().insert(name.to_owned(), value.to_owned());
            Ok(())
        }
        fn delete(&self, name: &str) -> io::Result<()> {
            self.0.borrow_mut().remove(name);
            Ok(())
        }
    }

    #[test]
    fn an_update_keeps_exactly_one_entry_under_the_new_name() {
        let exe = Path::new(r"C:\Apps\MosaikCapture.exe");
        let key = FakeRunKey::default();
        key.set("Mosaik Capture", OsStr::new(r#""C:\Alt\MosaikCapture.exe" --background"#))
            .unwrap();
        key.set("Mosaiks Aufnahme", OsStr::new("alt")).unwrap();
        key.set("OneDrive", OsStr::new("fremd")).unwrap();

        apply(&key, true, exe).unwrap();
        let values = key.0.borrow().clone();
        assert_eq!(values.keys().collect::<Vec<_>>(), ["Nazar Capture", "OneDrive"]);
        assert_eq!(values["Nazar Capture"], command_line(exe));
        assert_eq!(values["OneDrive"], "fremd", "other programs are left alone");

        apply(&key, true, exe).unwrap();
        assert_eq!(key.0.borrow().len(), 2, "writing again changes nothing");
        key.set("Mosaik Capture", OsStr::new("alt")).unwrap();
        apply(&key, false, exe).unwrap();
        assert_eq!(key.0.borrow().keys().collect::<Vec<_>>(), ["OneDrive"], "off removes all");
    }

    #[test]
    fn command_line_quotes_executable() {
        let exe = Path::new(r"C:\Program Files\Mosaik Capture\MosaikCapture.exe");
        assert_eq!(
            command_line(exe),
            r#""C:\Program Files\Mosaik Capture\MosaikCapture.exe" --background"#
        );
    }

    #[test]
    fn check_maps_status_codes() {
        assert!(check(ERROR_SUCCESS).is_ok());
        let error = check(ERROR_FILE_NOT_FOUND).unwrap_err();
        assert_eq!(error.kind(), io::ErrorKind::NotFound);
    }

    #[test]
    fn value_names() {
        assert_eq!(VALUE_NAME, "Nazar Capture");
        assert_eq!(LEGACY_VALUE_NAMES, ["Mosaik Capture", "Mosaiks Aufnahme"]);
        assert_eq!(RUN_KEY, r"Software\Microsoft\Windows\CurrentVersion\Run");
    }
}
