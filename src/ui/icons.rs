//! The application icon at the exact size a DPI needs, loaded with
//! `LoadIconWithScaleDown` so Windows never has to stretch a smaller image.

use windows_sys::Win32::Foundation::HINSTANCE;
use windows_sys::Win32::UI::Controls::LoadIconWithScaleDown;
use windows_sys::Win32::UI::HiDpi::GetSystemMetricsForDpi;
use windows_sys::Win32::UI::WindowsAndMessaging::{DestroyIcon, HICON, SYSTEM_METRICS_INDEX};
use windows_sys::core::PCWSTR;

/// Resource id of the application icon group (see `build.rs`).
const APP_ICON_RESOURCE: u16 = 1;

/// `MAKEINTRESOURCEW(APP_ICON_RESOURCE)`.
fn app_icon_name() -> PCWSTR {
    usize::from(APP_ICON_RESOURCE) as PCWSTR
}

/// An icon owned by us, destroyed on drop. May be null if the resource is missing;
/// Windows then simply shows no (or the default) icon.
#[derive(Debug)]
pub struct OwnedIcon(HICON);

impl OwnedIcon {
    /// Loads the application icon at the size of `metric` (e.g. `SM_CXSMICON`) for
    /// `dpi`, scaling down from the next larger image if there is no exact match.
    pub fn load(instance: HINSTANCE, metric: SYSTEM_METRICS_INDEX, dpi: u32) -> Self {
        // SAFETY: no pointer arguments.
        let size = unsafe { GetSystemMetricsForDpi(metric, dpi) };
        let mut icon: HICON = std::ptr::null_mut();
        // SAFETY: loads a resource of our own module by id into `icon`, which outlives
        // the call; the icon is owned by us.
        let result =
            unsafe { LoadIconWithScaleDown(instance, app_icon_name(), size, size, &raw mut icon) };
        if result < 0 {
            icon = std::ptr::null_mut();
        }
        Self(icon)
    }

    /// The raw handle; valid as long as `self` lives.
    pub const fn handle(&self) -> HICON {
        self.0
    }
}

impl Drop for OwnedIcon {
    fn drop(&mut self) {
        if !self.0.is_null() {
            // SAFETY: the icon was created for us by LoadIconWithScaleDown.
            unsafe { DestroyIcon(self.0) };
        }
    }
}
