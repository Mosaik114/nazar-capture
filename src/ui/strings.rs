//! The host's user-visible texts, one catalog per interface language (German, English,
//! Russian, Spanish, French, Arabic, Turkish). The language is the one chosen in the
//! settings (`language` in settings.ini, shared with the engine); "auto" follows the
//! Windows display language, see [`Language::resolve`]. Every catalog has the same
//! fields (the compiler checks that), and a test checks that the placeholders agree.

use std::sync::atomic::{AtomicU8, Ordering};

use windows_sys::Win32::Globalization::GetUserDefaultUILanguage;

use crate::config::Language;
use crate::hotkey::KeyCaps;

/// The product name the user sees: tray tooltip, balloon and message-box titles, the
/// autostart entry, the Start-menu shortcut. Never translated. Technical names
/// (executables, window classes, mutexes, folders) stay `NazarCapture`.
pub const PRODUCT: &str = "Nazar Capture";

/// Problems shown as a balloon at the tray icon.
#[derive(Debug)]
pub struct Balloons {
    /// A stored hotkey cannot be parsed.
    pub invalid_hotkey: &'static str,
    /// A stored hotkey uses Shift as its only modifier.
    pub shift_only_hotkey: &'static str,
    /// Both actions have the same stored hotkey.
    pub duplicate_hotkey: &'static str,
    /// A stored folder path is incomplete or invalid.
    pub invalid_folder: &'static str,
    /// Registering a hotkey failed at start-up.
    pub hotkey_taken: &'static str,
    /// The path of this executable is unknown, so autostart was not written.
    pub exe_unknown: &'static str,
    /// Writing the settings failed.
    pub save_failed: &'static str,
    /// Writing the settings or the autostart entry failed at start-up.
    pub startup_save_failed: &'static str,
    /// The engine could not be reached or started.
    pub engine_failed: &'static str,
    /// Quitting is blocked by a running capture.
    pub busy: &'static str,
}

/// Messages of `--install` (placeholders in braces).
#[derive(Debug)]
pub struct InstallTexts {
    pub target_required: &'static str,
    pub no_local_app_data: &'static str,
    pub no_source: &'static str,
    /// `{name}`: the missing executable.
    pub incomplete: &'static str,
    pub busy: &'static str,
    pub still_running: &'static str,
    /// `{path}`, `{error}`.
    pub copy: &'static str,
    /// `{path}`.
    pub unicode_path: &'static str,
    /// `{error}`.
    pub autostart: &'static str,
    /// `{detail}`.
    pub shortcut: &'static str,
    /// The detail when the Start menu folder is unknown.
    pub no_start_menu: &'static str,
    /// `{error}`.
    pub start: &'static str,
}

/// Why the user interface could not be run (start-up message box).
#[derive(Debug)]
pub struct UiErrorTexts {
    pub register_class: &'static str,
    pub create_window: &'static str,
    pub message_loop: &'static str,
    /// `{step}`, `{code}`.
    pub with_code: &'static str,
}

/// One language's texts.
#[derive(Debug)]
pub struct Texts {
    /// Tooltip of the notification-area icon; it starts with [`PRODUCT`].
    pub tray_tooltip: &'static str,
    pub menu_screenshot: &'static str,
    pub menu_recording: &'static str,
    pub menu_settings: &'static str,
    pub menu_quit: &'static str,
    pub balloon: Balloons,
    pub install: InstallTexts,
    pub ui_error: UiErrorTexts,
    /// Key-cap names in the tray menu.
    pub keys: KeyCaps,
}

const ASCII_KEYS: KeyCaps = KeyCaps {
    ctrl: "Ctrl",
    shift: "Shift",
    space: "Space",
    print: "PrtSc",
    pause: "Pause",
    insert: "Ins",
    num_decimal: "Num .",
};

pub static GERMAN: Texts = Texts {
    tray_tooltip: "Nazar Capture – Screenshot und Bildschirmaufnahme",
    menu_screenshot: "Screenshot erstellen",
    menu_recording: "Aufnahme starten / beenden",
    menu_settings: "Einstellungen",
    menu_quit: "Beenden",
    balloon: Balloons {
        invalid_hotkey: "Ein gespeichertes Tastenkürzel ist ungültig. Bitte in den Einstellungen ein neues festlegen.",
        shift_only_hotkey: "Ein gespeichertes Tastenkürzel nutzt nur Umschalt. Es braucht Strg, Alt oder Win.",
        duplicate_hotkey: "Screenshot und Aufnahme haben dasselbe Tastenkürzel. Bitte in den Einstellungen ein anderes wählen.",
        invalid_folder: "Ein Speicherordner ist ungültig. Bitte in den Einstellungen einen anderen wählen.",
        hotkey_taken: "Ein Tastenkürzel ist bereits von einem anderen Programm belegt. Bitte in den Einstellungen ein anderes wählen.",
        exe_unknown: "Der Programmpfad ist unbekannt, daher wurde der Autostart nicht eingerichtet.",
        save_failed: "Die Einstellungen konnten nicht gespeichert werden.",
        startup_save_failed: "Die Einstellungen oder der Autostart konnten nicht gespeichert werden.",
        engine_failed: "Das Aufnahmeprogramm konnte nicht gestartet werden.",
        busy: "Bitte die laufende Aufnahme oder Bereichsauswahl zuerst beenden.",
    },
    install: InstallTexts {
        target_required: "Im Testmodus muss der Zielordner angegeben werden (--install <Ordner>).",
        no_local_app_data: "Der Ordner für lokale Anwendungsdaten wurde nicht gefunden.",
        no_source: "Der Ordner des Installationsprogramms ist unbekannt.",
        incomplete: "Die Installationsdateien sind unvollständig: {name} fehlt. Bitte das ZIP-Archiv vollständig entpacken.",
        busy: "Gerade läuft eine Aufnahme oder Bereichsauswahl. Bitte sie zuerst beenden und die Installation dann erneut starten.",
        still_running: "Die laufende Version hat sich nicht rechtzeitig beendet. Bitte sie über das Symbol im Infobereich beenden und es erneut versuchen.",
        copy: "{path} konnte nicht kopiert werden ({error}).",
        unicode_path: "Der Pfad {path} enthält ungültige Zeichen.",
        autostart: "Der Autostart konnte nicht eingerichtet werden ({error}).",
        shortcut: "Die Verknüpfung im Startmenü konnte nicht angelegt werden ({detail}).",
        no_start_menu: "kein Startmenü-Ordner",
        start: "Die installierte Version konnte nicht gestartet werden ({error}).",
    },
    ui_error: UiErrorTexts {
        register_class: "Die Fensterklasse konnte nicht registriert werden",
        create_window: "Das Programmfenster konnte nicht erstellt werden",
        message_loop: "Die Nachrichtenverarbeitung ist fehlgeschlagen",
        with_code: "{step} (Windows-Fehler {code}).",
    },
    keys: KeyCaps::GERMAN,
};

pub static ENGLISH: Texts = Texts {
    tray_tooltip: "Nazar Capture – screenshots and screen recording",
    menu_screenshot: "Take screenshot",
    menu_recording: "Start / stop recording",
    menu_settings: "Settings",
    menu_quit: "Quit",
    balloon: Balloons {
        invalid_hotkey: "A saved shortcut is invalid. Please set a new one in the settings.",
        shift_only_hotkey: "A saved shortcut uses only Shift. It needs Ctrl, Alt or Win.",
        duplicate_hotkey: "Screenshot and recording have the same shortcut. Please choose another one in the settings.",
        invalid_folder: "A save folder is invalid. Please choose another one in the settings.",
        hotkey_taken: "A shortcut is already used by another program. Please choose another one in the settings.",
        exe_unknown: "The program path is unknown, so autostart was not set up.",
        save_failed: "The settings could not be saved.",
        startup_save_failed: "The settings or autostart could not be saved.",
        engine_failed: "The capture program could not be started.",
        busy: "Please finish the running recording or area selection first.",
    },
    install: InstallTexts {
        target_required: "In test mode the target folder must be given (--install <folder>).",
        no_local_app_data: "The local application data folder was not found.",
        no_source: "The installer's folder is unknown.",
        incomplete: "The installation files are incomplete: {name} is missing. Please extract the whole ZIP archive.",
        busy: "A recording or area selection is running. Please finish it first, then start the installation again.",
        still_running: "The running version did not close in time. Please quit it from its tray icon and try again.",
        copy: "{path} could not be copied ({error}).",
        unicode_path: "The path {path} contains invalid characters.",
        autostart: "Autostart could not be set up ({error}).",
        shortcut: "The Start menu shortcut could not be created ({detail}).",
        no_start_menu: "no Start menu folder",
        start: "The installed version could not be started ({error}).",
    },
    ui_error: UiErrorTexts {
        register_class: "The window class could not be registered",
        create_window: "The program window could not be created",
        message_loop: "Message processing failed",
        with_code: "{step} (Windows error {code}).",
    },
    keys: ASCII_KEYS,
};

pub static RUSSIAN: Texts = Texts {
    tray_tooltip: "Nazar Capture – скриншоты и запись экрана",
    menu_screenshot: "Сделать скриншот",
    menu_recording: "Начать / остановить запись",
    menu_settings: "Настройки",
    menu_quit: "Выход",
    balloon: Balloons {
        invalid_hotkey: "Сохранённое сочетание клавиш недействительно. Задайте новое в настройках.",
        shift_only_hotkey: "Сохранённое сочетание использует только Shift. Нужны также Ctrl, Alt или Win.",
        duplicate_hotkey: "У скриншота и записи одинаковое сочетание клавиш. Выберите другое в настройках.",
        invalid_folder: "Папка для сохранения недействительна. Выберите другую в настройках.",
        hotkey_taken: "Сочетание клавиш уже занято другой программой. Выберите другое в настройках.",
        exe_unknown: "Путь к программе неизвестен, поэтому автозапуск не настроен.",
        save_failed: "Не удалось сохранить настройки.",
        startup_save_failed: "Не удалось сохранить настройки или автозапуск.",
        engine_failed: "Не удалось запустить модуль захвата.",
        busy: "Сначала завершите текущую запись или выбор области.",
    },
    install: InstallTexts {
        target_required: "В тестовом режиме нужно указать целевую папку (--install <папка>).",
        no_local_app_data: "Папка локальных данных приложений не найдена.",
        no_source: "Папка установщика неизвестна.",
        incomplete: "Файлы установки неполные: отсутствует {name}. Распакуйте ZIP-архив полностью.",
        busy: "Сейчас идёт запись или выбор области. Сначала завершите их, затем снова запустите установку.",
        still_running: "Запущенная версия не закрылась вовремя. Закройте её через значок в области уведомлений и повторите попытку.",
        copy: "Не удалось скопировать {path} ({error}).",
        unicode_path: "Путь {path} содержит недопустимые символы.",
        autostart: "Не удалось настроить автозапуск ({error}).",
        shortcut: "Не удалось создать ярлык в меню «Пуск» ({detail}).",
        no_start_menu: "нет папки меню «Пуск»",
        start: "Не удалось запустить установленную версию ({error}).",
    },
    ui_error: UiErrorTexts {
        register_class: "Не удалось зарегистрировать класс окна",
        create_window: "Не удалось создать окно программы",
        message_loop: "Сбой обработки сообщений",
        with_code: "{step} (ошибка Windows {code}).",
    },
    keys: KeyCaps { space: "Пробел", ..ASCII_KEYS },
};

pub static SPANISH: Texts = Texts {
    tray_tooltip: "Nazar Capture – capturas y grabación de pantalla",
    menu_screenshot: "Hacer captura",
    menu_recording: "Iniciar / detener grabación",
    menu_settings: "Configuración",
    menu_quit: "Salir",
    balloon: Balloons {
        invalid_hotkey: "Un atajo guardado no es válido. Define uno nuevo en la configuración.",
        shift_only_hotkey: "Un atajo guardado solo usa Mayús. Necesita Ctrl, Alt o Win.",
        duplicate_hotkey: "La captura y la grabación tienen el mismo atajo. Elige otro en la configuración.",
        invalid_folder: "Una carpeta de guardado no es válida. Elige otra en la configuración.",
        hotkey_taken: "Otro programa ya usa un atajo. Elige otro en la configuración.",
        exe_unknown: "Se desconoce la ruta del programa, así que no se configuró el inicio automático.",
        save_failed: "No se pudo guardar la configuración.",
        startup_save_failed: "No se pudo guardar la configuración ni el inicio automático.",
        engine_failed: "No se pudo iniciar el módulo de captura.",
        busy: "Termina primero la grabación o la selección de área en curso.",
    },
    install: InstallTexts {
        target_required: "En modo de prueba hay que indicar la carpeta de destino (--install <carpeta>).",
        no_local_app_data: "No se encontró la carpeta de datos locales de aplicaciones.",
        no_source: "Se desconoce la carpeta del instalador.",
        incomplete: "Los archivos de instalación están incompletos: falta {name}. Extrae el archivo ZIP completo.",
        busy: "Hay una grabación o selección de área en curso. Termínala primero y vuelve a iniciar la instalación.",
        still_running: "La versión en ejecución no se cerró a tiempo. Ciérrala desde su icono en la bandeja e inténtalo de nuevo.",
        copy: "No se pudo copiar {path} ({error}).",
        unicode_path: "La ruta {path} contiene caracteres no válidos.",
        autostart: "No se pudo configurar el inicio automático ({error}).",
        shortcut: "No se pudo crear el acceso directo del menú Inicio ({detail}).",
        no_start_menu: "no hay carpeta del menú Inicio",
        start: "No se pudo iniciar la versión instalada ({error}).",
    },
    ui_error: UiErrorTexts {
        register_class: "No se pudo registrar la clase de ventana",
        create_window: "No se pudo crear la ventana del programa",
        message_loop: "Falló el procesamiento de mensajes",
        with_code: "{step} (error de Windows {code}).",
    },
    keys: KeyCaps {
        shift: "Mayús",
        space: "Espacio",
        print: "ImprPant",
        pause: "Pausa",
        insert: "Insert",
        ..ASCII_KEYS
    },
};

pub static FRENCH: Texts = Texts {
    tray_tooltip: "Nazar Capture – captures et enregistrement d’écran",
    menu_screenshot: "Faire une capture",
    menu_recording: "Démarrer / arrêter l’enregistrement",
    menu_settings: "Paramètres",
    menu_quit: "Quitter",
    balloon: Balloons {
        invalid_hotkey: "Un raccourci enregistré n’est pas valide. Définissez-en un nouveau dans les paramètres.",
        shift_only_hotkey: "Un raccourci enregistré n’utilise que Maj. Il faut aussi Ctrl, Alt ou Win.",
        duplicate_hotkey: "La capture et l’enregistrement ont le même raccourci. Choisissez-en un autre dans les paramètres.",
        invalid_folder: "Un dossier d’enregistrement n’est pas valide. Choisissez-en un autre dans les paramètres.",
        hotkey_taken: "Un raccourci est déjà utilisé par un autre programme. Choisissez-en un autre dans les paramètres.",
        exe_unknown: "Le chemin du programme est inconnu ; le démarrage automatique n’a pas été configuré.",
        save_failed: "Impossible d’enregistrer les paramètres.",
        startup_save_failed: "Impossible d’enregistrer les paramètres ou le démarrage automatique.",
        engine_failed: "Impossible de démarrer le module de capture.",
        busy: "Terminez d’abord l’enregistrement ou la sélection de zone en cours.",
    },
    install: InstallTexts {
        target_required: "En mode test, il faut indiquer le dossier cible (--install <dossier>).",
        no_local_app_data: "Le dossier des données d’application locales est introuvable.",
        no_source: "Le dossier du programme d’installation est inconnu.",
        incomplete: "Les fichiers d’installation sont incomplets : {name} manque. Extrayez l’archive ZIP en entier.",
        busy: "Un enregistrement ou une sélection de zone est en cours. Terminez-le d’abord, puis relancez l’installation.",
        still_running: "La version en cours ne s’est pas fermée à temps. Quittez-la depuis son icône dans la zone de notification et réessayez.",
        copy: "Impossible de copier {path} ({error}).",
        unicode_path: "Le chemin {path} contient des caractères non valides.",
        autostart: "Impossible de configurer le démarrage automatique ({error}).",
        shortcut: "Impossible de créer le raccourci du menu Démarrer ({detail}).",
        no_start_menu: "aucun dossier du menu Démarrer",
        start: "Impossible de démarrer la version installée ({error}).",
    },
    ui_error: UiErrorTexts {
        register_class: "Impossible d’enregistrer la classe de fenêtre",
        create_window: "Impossible de créer la fenêtre du programme",
        message_loop: "Le traitement des messages a échoué",
        with_code: "{step} (erreur Windows {code}).",
    },
    keys: KeyCaps {
        shift: "Maj",
        space: "Espace",
        print: "Impr. écran",
        insert: "Inser",
        ..ASCII_KEYS
    },
};

pub static ARABIC: Texts = Texts {
    tray_tooltip: "Nazar Capture – لقطات الشاشة وتسجيلها",
    menu_screenshot: "التقاط لقطة شاشة",
    menu_recording: "بدء التسجيل / إيقافه",
    menu_settings: "الإعدادات",
    menu_quit: "إنهاء",
    balloon: Balloons {
        invalid_hotkey: "اختصار محفوظ غير صالح. يُرجى تعيين اختصار جديد في الإعدادات.",
        shift_only_hotkey: "يستخدم اختصار محفوظ مفتاح Shift وحده. يلزم معه Ctrl أو Alt أو Win.",
        duplicate_hotkey: "للقطة الشاشة والتسجيل الاختصار نفسه. يُرجى اختيار اختصار آخر في الإعدادات.",
        invalid_folder: "أحد مجلدات الحفظ غير صالح. يُرجى اختيار مجلد آخر في الإعدادات.",
        hotkey_taken: "اختصار مستخدم بالفعل من برنامج آخر. يُرجى اختيار اختصار آخر في الإعدادات.",
        exe_unknown: "مسار البرنامج غير معروف، لذلك لم يُضبط التشغيل التلقائي.",
        save_failed: "تعذّر حفظ الإعدادات.",
        startup_save_failed: "تعذّر حفظ الإعدادات أو التشغيل التلقائي.",
        engine_failed: "تعذّر تشغيل وحدة الالتقاط.",
        busy: "يُرجى إنهاء التسجيل الجاري أو تحديد المنطقة أولًا.",
    },
    install: InstallTexts {
        target_required: "في وضع الاختبار يجب تحديد المجلد الهدف (--install <مجلد>).",
        no_local_app_data: "لم يُعثر على مجلد بيانات التطبيقات المحلية.",
        no_source: "مجلد برنامج التثبيت غير معروف.",
        incomplete: "ملفات التثبيت ناقصة: الملف {name} مفقود. يُرجى فك ضغط أرشيف ZIP بالكامل.",
        busy: "يجري الآن تسجيل أو تحديد منطقة. يُرجى إنهاؤه أولًا ثم بدء التثبيت من جديد.",
        still_running: "لم يُغلق الإصدار العامل في الوقت المناسب. يُرجى إنهاؤه من أيقونته في منطقة الإشعارات والمحاولة مجددًا.",
        copy: "تعذّر نسخ {path} ({error}).",
        unicode_path: "يحتوي المسار {path} على أحرف غير صالحة.",
        autostart: "تعذّر إعداد التشغيل التلقائي ({error}).",
        shortcut: "تعذّر إنشاء اختصار في قائمة ابدأ ({detail}).",
        no_start_menu: "لا يوجد مجلد لقائمة ابدأ",
        start: "تعذّر تشغيل الإصدار المثبت ({error}).",
    },
    ui_error: UiErrorTexts {
        register_class: "تعذّر تسجيل فئة النافذة",
        create_window: "تعذّر إنشاء نافذة البرنامج",
        message_loop: "فشلت معالجة الرسائل",
        with_code: "{step} (خطأ Windows رقم {code}).",
    },
    keys: KeyCaps { space: "مسافة", ..ASCII_KEYS },
};

pub static TURKISH: Texts = Texts {
    tray_tooltip: "Nazar Capture – ekran görüntüsü ve ekran kaydı",
    menu_screenshot: "Ekran görüntüsü al",
    menu_recording: "Kaydı başlat / durdur",
    menu_settings: "Ayarlar",
    menu_quit: "Çıkış",
    balloon: Balloons {
        invalid_hotkey: "Kayıtlı bir kısayol geçersiz. Lütfen ayarlardan yeni bir kısayol belirleyin.",
        shift_only_hotkey: "Kayıtlı bir kısayol yalnızca Shift kullanıyor. Ctrl, Alt veya Win de gerekir.",
        duplicate_hotkey: "Ekran görüntüsü ve kayıt aynı kısayolu kullanıyor. Lütfen ayarlardan başka bir kısayol seçin.",
        invalid_folder: "Bir kayıt klasörü geçersiz. Lütfen ayarlardan başka bir klasör seçin.",
        hotkey_taken: "Bir kısayol başka bir program tarafından kullanılıyor. Lütfen ayarlardan başka bir kısayol seçin.",
        exe_unknown: "Program yolu bilinmiyor; bu yüzden otomatik başlatma ayarlanmadı.",
        save_failed: "Ayarlar kaydedilemedi.",
        startup_save_failed: "Ayarlar veya otomatik başlatma kaydedilemedi.",
        engine_failed: "Yakalama modülü başlatılamadı.",
        busy: "Lütfen önce süren kaydı veya alan seçimini bitirin.",
    },
    install: InstallTexts {
        target_required: "Test modunda hedef klasör belirtilmelidir (--install <klasör>).",
        no_local_app_data: "Yerel uygulama verileri klasörü bulunamadı.",
        no_source: "Yükleyicinin klasörü bilinmiyor.",
        incomplete: "Kurulum dosyaları eksik: {name} yok. Lütfen ZIP arşivini tamamen çıkarın.",
        busy: "Şu anda bir kayıt veya alan seçimi sürüyor. Lütfen önce bitirin, ardından kurulumu yeniden başlatın.",
        still_running: "Çalışan sürüm zamanında kapanmadı. Lütfen bildirim alanındaki simgesinden kapatıp yeniden deneyin.",
        copy: "{path} kopyalanamadı ({error}).",
        unicode_path: "{path} yolu geçersiz karakterler içeriyor.",
        autostart: "Otomatik başlatma ayarlanamadı ({error}).",
        shortcut: "Başlat menüsü kısayolu oluşturulamadı ({detail}).",
        no_start_menu: "Başlat menüsü klasörü yok",
        start: "Kurulan sürüm başlatılamadı ({error}).",
    },
    ui_error: UiErrorTexts {
        register_class: "Pencere sınıfı kaydedilemedi",
        create_window: "Program penceresi oluşturulamadı",
        message_loop: "İleti işleme başarısız oldu",
        with_code: "{step} (Windows hatası {code}).",
    },
    keys: KeyCaps { space: "Boşluk", ..ASCII_KEYS },
};

/// The texts of a concrete language (`Auto` must be resolved first; it falls back to
/// English here).
pub fn texts_of(language: Language) -> &'static Texts {
    match language {
        Language::German => &GERMAN,
        Language::Russian => &RUSSIAN,
        Language::Spanish => &SPANISH,
        Language::French => &FRENCH,
        Language::Arabic => &ARABIC,
        Language::Turkish => &TURKISH,
        Language::English | Language::Auto => &ENGLISH,
    }
}

/// The resolved language in use (index into [`Language::ALL`]); German until set.
static CURRENT: AtomicU8 = AtomicU8::new(1);

/// The primary language id of the Windows display language (`LANGID & 0x3FF`).
pub fn windows_language() -> u16 {
    // SAFETY: no arguments.
    unsafe { GetUserDefaultUILanguage() & 0x3FF }
}

/// Uses `setting` (resolving "auto" with the Windows display language) from now on.
pub fn set_language(setting: Language) {
    let resolved = setting.resolve(windows_language());
    let index = Language::ALL.iter().position(|&language| language == resolved).unwrap_or(1);
    CURRENT.store(u8::try_from(index).unwrap_or(1), Ordering::Relaxed);
}

/// The texts of the language in use.
pub fn current() -> &'static Texts {
    texts_of(Language::ALL[usize::from(CURRENT.load(Ordering::Relaxed)) % Language::ALL.len()])
}

/// All catalogs with their language (tests).
#[cfg(test)]
pub fn all() -> [(Language, &'static Texts); 7] {
    [
        (Language::German, &GERMAN),
        (Language::English, &ENGLISH),
        (Language::Russian, &RUSSIAN),
        (Language::Spanish, &SPANISH),
        (Language::French, &FRENCH),
        (Language::Arabic, &ARABIC),
        (Language::Turkish, &TURKISH),
    ]
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Every text of a catalog by name.
    fn fields(texts: &Texts) -> Vec<(&'static str, &'static str)> {
        let b = &texts.balloon;
        let i = &texts.install;
        let u = &texts.ui_error;
        vec![
            ("tray_tooltip", texts.tray_tooltip),
            ("menu_screenshot", texts.menu_screenshot),
            ("menu_recording", texts.menu_recording),
            ("menu_settings", texts.menu_settings),
            ("menu_quit", texts.menu_quit),
            ("invalid_hotkey", b.invalid_hotkey),
            ("shift_only_hotkey", b.shift_only_hotkey),
            ("duplicate_hotkey", b.duplicate_hotkey),
            ("invalid_folder", b.invalid_folder),
            ("hotkey_taken", b.hotkey_taken),
            ("exe_unknown", b.exe_unknown),
            ("save_failed", b.save_failed),
            ("startup_save_failed", b.startup_save_failed),
            ("engine_failed", b.engine_failed),
            ("busy", b.busy),
            ("target_required", i.target_required),
            ("no_local_app_data", i.no_local_app_data),
            ("no_source", i.no_source),
            ("incomplete", i.incomplete),
            ("install_busy", i.busy),
            ("still_running", i.still_running),
            ("copy", i.copy),
            ("unicode_path", i.unicode_path),
            ("autostart", i.autostart),
            ("shortcut", i.shortcut),
            ("no_start_menu", i.no_start_menu),
            ("start", i.start),
            ("register_class", u.register_class),
            ("create_window", u.create_window),
            ("message_loop", u.message_loop),
            ("with_code", u.with_code),
        ]
    }

    fn placeholders(text: &str) -> Vec<&str> {
        let mut found: Vec<&str> = text
            .split('{')
            .skip(1)
            .filter_map(|rest| rest.split_once('}').map(|(name, _)| name))
            .collect();
        found.sort_unstable();
        found
    }

    #[test]
    fn every_catalog_has_every_text_with_the_same_placeholders() {
        let german = fields(&GERMAN);
        for (language, texts) in all() {
            for ((name, text), (_, original)) in fields(texts).into_iter().zip(&german) {
                assert!(!text.trim().is_empty(), "{language:?}: {name} is empty");
                assert_eq!(placeholders(text), placeholders(original), "{language:?}: {name}");
                assert!(!text.contains("Mosaik"), "{language:?}: {name}");
            }
            assert!(texts.tray_tooltip.starts_with(PRODUCT), "{language:?}");
        }
    }

    #[test]
    fn languages_switch_and_auto_follows_windows() {
        set_language(Language::French);
        assert_eq!(current().menu_quit, "Quitter");
        set_language(Language::Arabic);
        assert_eq!(current().menu_settings, "الإعدادات");
        set_language(Language::Auto);
        let expected = texts_of(Language::Auto.resolve(windows_language()));
        assert!(std::ptr::eq(current(), expected));
        set_language(Language::German);
        assert_eq!(current().menu_quit, "Beenden");
    }
}
