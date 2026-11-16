//! User-visible German texts. Each doc comment says what the text means in English.

/// The product name the user sees: tray tooltip, balloon and message-box titles, the
/// autostart entry, the Start-menu shortcut. Technical names (executables, window
/// classes, mutexes, folders) stay `MosaikCapture`.
pub const PRODUCT: &str = "Nazar Capture";
/// Tooltip of the notification-area icon ("… – screenshot and screen recording"); it
/// starts with [`PRODUCT`].
pub const TRAY_TOOLTIP: &str = "Nazar Capture – Screenshot und Bildschirmaufnahme";

/// Tray menu: "Take screenshot".
pub const MENU_SCREENSHOT: &str = "Screenshot erstellen";
/// Tray menu: "Start / stop recording".
pub const MENU_RECORDING: &str = "Aufnahme starten / beenden";
/// Tray menu: "Settings".
pub const MENU_SETTINGS: &str = "Einstellungen";
/// Tray menu: "Quit".
pub const MENU_QUIT: &str = "Beenden";

/// Problems shown as a balloon at the tray icon.
pub mod balloon {
    /// A stored hotkey cannot be parsed ("A saved shortcut is invalid. Please set a
    /// new one in the settings.").
    pub const INVALID_HOTKEY: &str = "Ein gespeichertes Tastenkürzel ist ungültig. \
        Bitte in den Einstellungen ein neues festlegen.";
    /// A stored hotkey uses Shift as its only modifier ("A saved shortcut uses only
    /// Shift; it needs Ctrl, Alt or Win.").
    pub const SHIFT_ONLY_HOTKEY: &str =
        "Ein gespeichertes Tastenkürzel nutzt nur Umschalt. Es braucht Strg, Alt oder Win.";
    /// Both actions have the same stored hotkey.
    pub const DUPLICATE_HOTKEY: &str = "Screenshot und Aufnahme haben dasselbe Tastenkürzel. \
        Bitte in den Einstellungen ein anderes wählen.";
    /// A stored folder path is incomplete or invalid.
    pub const INVALID_FOLDER: &str =
        "Ein Speicherordner ist ungültig. Bitte in den Einstellungen einen anderen wählen.";
    /// Registering a hotkey failed at start-up ("A shortcut is already used by another
    /// program. Please choose another one in the settings.").
    pub const HOTKEY_TAKEN: &str = "Ein Tastenkürzel ist bereits von einem anderen Programm \
        belegt. Bitte in den Einstellungen ein anderes wählen.";
    /// The path of this executable is unknown, so autostart was not written ("The
    /// program path is unknown, so autostart was not set up.").
    pub const EXE_UNKNOWN: &str =
        "Der Programmpfad ist unbekannt, daher wurde der Autostart nicht eingerichtet.";
    /// Writing the settings failed.
    pub const SAVE_FAILED: &str = "Die Einstellungen konnten nicht gespeichert werden.";
    /// Writing the settings or the autostart entry failed at start-up.
    pub const STARTUP_SAVE_FAILED: &str =
        "Die Einstellungen oder der Autostart konnten nicht gespeichert werden.";
    /// The engine could not be reached or started.
    pub const ENGINE_FAILED: &str = "Das Aufnahmeprogramm konnte nicht gestartet werden.";
    /// Quitting is blocked by a running capture.
    pub const BUSY: &str = "Bitte die laufende Aufnahme oder Bereichsauswahl zuerst beenden.";
}
