namespace MosaikCapture.Engine.Core;

/// <summary>
/// Every text the user can see. The app's UI language is German; logic files reference these
/// members instead of literals so the texts are in one place (and could be localized later).
/// </summary>
static class UiText
{
    /// <summary>The product name, the one place it is written in the engine (the host has its own in strings.rs).</summary>
    public const string Product = "Nazar Capture";

    /// <summary>
    /// Window titles. They are part of the German UI and carry the product name (Product.Name):
    /// Alt+Tab, the taskbar and screen readers show them. The invisible helper windows of a recording
    /// (frame, countdown, toolbar, click ring) are never listed there, but screen readers and UI automation
    /// still read their titles, and tests and QA scripts find all windows by them, so they follow the same scheme.
    /// </summary>
    public static class Titles
    {
        public const string Overlay = "Nazar Capture – Screenshot";
        public const string ScreenshotCard = "Nazar Capture – Screenshot-Karte";
        public const string Editor = "Nazar Capture – Screenshot bearbeiten";
        public const string Pin = "Nazar Capture – angepinnter Screenshot";
        public const string RecordingFrame = "Nazar Capture – Aufnahmerahmen";
        public const string Countdown = "Nazar Capture – Countdown";
        public const string Toolbar = "Nazar Capture – Aufnahmeleiste";
        public const string RecordingCard = "Nazar Capture – Aufnahme-Karte";
        public const string Click = "Nazar Capture – Klick";
        public const string Trim = "Aufnahme kürzen";
        public const string KitDemo = "Nazar Capture – Bausteine";
        public const string Settings = "Nazar Capture – Einstellungen";
    }

    /// <summary>Names of the files the user gets.</summary>
    public static class Files
    {
        /// <summary>Default folder below Pictures (user-visible, so German).</summary>
        public const string ScreenshotFolder = "Mosaiks-Screenshots";
        /// <summary>Default folder below Videos.</summary>
        public const string RecordingFolder = "Mosaiks-Aufnahmen";
        public const string ScreenshotPrefix = "Nazar-Screenshot ";
        public const string RecordingPrefix = "Nazar-Aufnahme ";
        public const string TrimmedSuffix = " gekürzt";
        /// <summary>Folder next to a recording that could not be saved: its raw segments and sound tracks.</summary>
        public const string RawDataSuffix = " (Rohdaten)";
        public const string SaveAsTitle = "Nazar Capture – Screenshot speichern unter";
        public const string SaveAsFilter = "PNG-Bild (*.png)|*.png|JPEG-Bild (*.jpg)|*.jpg";
    }

    public static class Common
    {
        public const string Folder = "Ordner";
        public const string Close = "Schließen (Esc)";
        public const string DeleteFailed = "Löschen nicht möglich";
        public const string NotYetSaved = "Wird noch gespeichert …";
        public const string Delete = "Löschen";
        public const string Deleted = "Gelöscht";
        public const string InRecycleBin = "Liegt im Papierkorb";
        public const string CopiedToClipboard = "In Zwischenablage kopiert";
        public const string CouldNotSave = "Konnte nicht speichern";
        public const string ReadingText = "Text wird gelesen …";
        public const string NoTextFound = "Kein Text gefunden";
        public const string TextRecognitionUnavailable = "Texterkennung nicht möglich";
        public const string TextCopied = "Text kopiert";
        public const string UnknownError = "Unbekannter Fehler";
        public const string CopyFailed = "Kopieren nicht möglich";
        public const string FfmpegTimedOut = "ffmpeg hat nicht rechtzeitig geantwortet";
        public const string Saving = "Wird gespeichert …";

        public static string Size(int width, int height) => width + " × " + height;

        /// <summary>"3 Zeilen · 42 Zeichen"</summary>
        public static string TextSummary(int lines, int characters) => lines + (lines == 1 ? " Zeile · " : " Zeilen · ") + characters + " Zeichen";
    }

    public static class Overlay
    {
        public const string ScreenshotHint = "Ziehen = Bereich   ·   Klick = Fenster   ·   Leertaste = ganzer Bildschirm   ·   C = Farbe kopieren   ·   Esc = abbrechen";
        public const string RecordingHint = "Aufnahme:   Ziehen = Bereich   ·   Klick = Fenster   ·   Leertaste = ganzer Bildschirm   ·   Esc = abbrechen";
        public const string ColorCopiedSuffix = "  kopiert";

        /// <summary>The hint as pairs of keys (shown as key caps, may be empty) and what they do.</summary>
        public static readonly (string Keys, string Text)[] ScreenshotHints =
        {
            ("", "Ziehen = Bereich"),
            ("", "Klick = Fenster"),
            ("Leertaste", "ganzer Bildschirm"),
            ("C", "Farbe kopieren"),
            ("Pfeiltasten", "Zeiger, Enter = Start und Ende"),
            ("Esc", "abbrechen"),
        };

        public static readonly (string Keys, string Text)[] RecordingHints =
        {
            ("", "Aufnahme: Ziehen = Bereich"),
            ("", "Klick = Fenster"),
            ("Leertaste", "ganzer Bildschirm"),
            ("Pfeiltasten", "Zeiger, Enter = Start und Ende"),
            ("Esc", "abbrechen"),
        };
    }

    public static class ScreenshotCard
    {
        public const string Edit = "Bearbeiten";
        public const string Pin = "Anpinnen";
        public const string Text = "Text";
        public const string EditedAndCopied = "Bearbeitet und kopiert";
        public const string NotSaved = "Nicht gespeichert";
        public const string SavedNotCopied = "Gespeichert, nicht kopiert";
        public const string Problem = "Screenshot fehlgeschlagen";
        public const string ScreenUnavailable = "Der Bildschirm lässt sich gerade nicht lesen (z. B. gesperrt)";
        public static string FolderNotWritable(string folder) => "Ordner nicht beschreibbar: " + folder;
    }

    public static class Editor
    {
        /// <summary>Tool names in the order of <c>ScreenshotEditor.Tool</c>.</summary>
        public static readonly string[] ToolNames =
        {
            "Verschieben", "Pfeil", "Rechteck", "Kreis", "Linie", "Stift", "Textmarker", "Scheinwerfer",
            "Text", "Text mit Hintergrund", "Nummer", "Verpixeln", "Weichzeichnen"
        };
        public static readonly string[] WidthNames = { "Sehr fein", "Fein", "Mittel", "Dick", "Sehr dick" };
        public const string MoreHint = "   ·   Klick: mehr";
        public const string Undo = "Rückgängig   Strg+Z";
        public const string Redo = "Wiederholen   Strg+Y";
        public const string SaveAs = "Speichern unter   Strg+S";
        public const string Copy = "Kopieren   Strg+C";
        public const string CopyText = "Text kopieren   Strg+Umschalt+C";
        public const string Cancel = "Abbrechen   Esc";
        public const string Done = "Fertig   Enter";
        public const string Pin = "Anpinnen   F3";
        public const string WidthHint = "   + / −";
        public const string ConfirmDiscard = "Nochmal Esc = Änderungen verwerfen";
        public const string CommandFailed = "Das hat nicht geklappt";

        public static string ColorTip(int number) => "Farbe   " + number;
        public static string SizeNote(string widthName) => "Größe: " + widthName;
        public static string Saved(string fileName) => "Gespeichert: " + fileName;
        public static string TextCopied(string summary) => Common.TextCopied + " · " + summary;
    }

    public static class Pin
    {
        public const string Copy = "Kopieren";
        public const string SaveAs = "Speichern unter …";
        public const string OriginalSize = "Originalgröße";
        public const string Close = "Schließen";
        public const string CloseTip = "Schließen   Esc";
        public const string CloseAll = "Alle Pins schließen";
        public const string CopyShortcut = "Strg+C";
        public const string SaveAsShortcut = "Strg+S";
        public const string Copied = "Kopiert";
        public const string Saved = "Gespeichert";
        public static string Opacity(double percent) => "Deckkraft " + percent + " %";
        public static string Zoom(double percent) => percent + " %";
    }

    public static class Recording
    {
        public const string FfmpegMissing = "ffmpeg fehlt";
        public const string DiskFull = "Zu wenig Speicherplatz";
        public static string DiskFullDetail(string free) => "Nur noch " + free + " frei; eine Aufnahme braucht mindestens 2 GB.";
        public const string FfmpegInstallHint = "Für Aufnahmen: winget install ffmpeg";
        public const string NotPossible = "Aufnahme nicht möglich";
        public const string MonitorUnreachable = "Dieser Monitor ist für die Aufnahme nicht erreichbar";
        public const string MonitorOnOtherGpu = "Dieser Monitor hängt an einer anderen Grafikkarte; aufnehmen lässt sich nur der Hauptgrafikkarten-Monitor";
        public const string Failed = "Aufnahme fehlgeschlagen";
        public const string Saved = "Aufnahme gespeichert";
        public const string SavedWithoutAudio = "Aufnahme ohne Ton gespeichert";
        public const string NoFrames = "Es wurde kein Bild aufgenommen.";
        public const string StoppedUnexpectedly = "Die Aufnahme wurde unerwartet beendet.";
        public const string AudioLost = "Der Ton konnte nicht aufgenommen werden.";
        public const string CreatingGif = "GIF wird erstellt …";
        public const string GifFailed = "GIF fehlgeschlagen";
        public const string GifSaved = "GIF gespeichert und kopiert";
        public const string GifSavedNotCopied = "GIF gespeichert, nicht kopiert";
        public const string TrimmedNotCopied = "Gekürzt gespeichert, nicht kopiert";
        public const string SavedNotCopied = "Aufnahme gespeichert, nicht kopiert";
        public const string Aborted = "Aufnahme abgebrochen";
        public const string OpenRawData = "Rohdaten öffnen";
        public const string TrimmedAndCopied = "Gekürzt und kopiert";
        public const string Trim = "Kürzen";
        public const string Gif = "GIF";
        public const string CountdownCancels = "bricht ab";
        public const string CountdownHint = "Esc = abbrechen";

        public static string Recorded(string duration) => duration + " aufgenommen";
        public static string FileInfo(string fileName, string size) => fileName + " · " + size;
        public static string Length(bool gif, string duration) => (gif ? "GIF · " : "") + duration;
    }

    public static class Toolbar
    {
        public const string Move = "Verschieben";
        public const string MicrophoneOn = "Mikro wird aufgenommen";
        public const string MicrophoneOff = "Mikro aus";
        public const string SoundOn = "Systemton wird aufgenommen";
        public const string SoundOff = "Systemton aus";
        public const string ClicksOn = "Klicks werden gezeigt";
        public const string ClicksOff = "Klicks nicht zeigen";
        public const string Resume = "Fortsetzen";
        public const string Pause = "Pause";
        public const string Stop = "Beenden und speichern";
        public const string Discard = "Verwerfen";
        public const string ConfirmDiscard = "Nochmal klicken = verwerfen";
        public const string ConfirmDiscardShort = "Wirklich verwerfen?";
    }

    public static class Trim
    {
        public const string KeyHint = "Leertaste abspielen  ·  I / O Anfang / Ende  ·  ← → Schritt  ·  Enter speichern";
        public const string Sound = "Ton";
        public const string Play = "Abspielen oder anhalten (Leertaste)";
        public const string AsGif = "Als GIF";
        public const string Save = "Speichern";

        public static string Selection(string length) => "Auswahl " + length;
        public static string NotSaved(string reason) => "Nicht gespeichert: " + reason;
        public static string Progress(string label, double fraction) => label + "  " + (int)Math.Round(fraction * 100) + " %";
        public static string VideoInfo(string fileName, string duration, int width, int height) => fileName + " · " + duration + " · " + Common.Size(width, height);
    }
    /// <summary>The hotkey field (key caps, recording, the reasons of a refusal).</summary>
    public static class Hotkeys
    {
        public const string Change = "Ändern";
        public const string Default = "Standard";
        public const string Cancel = "Aufnahme abbrechen (Esc)";
        public const string Unset = "nicht belegt";
        public const string PressKeys = "Tasten drücken …";
        public const string Capture = "Drücke die neue Tastenkombination. Esc bricht ab.";
        public const string Saved = "Übernommen.";
        public const string ShiftOnly = "Umschalt allein reicht nicht. Nimm Strg, Alt oder Win dazu.";
        public const string MissingModifier = "Dazu gehört Strg, Alt oder Win (nur Druck und Pause gehen allein).";
        public const string MissingKey = "Es fehlt eine Taste neben Strg, Alt oder Umschalt.";
        public const string Unsupported = "Diese Taste geht nicht als Kurzbefehl.";
        /// <summary>{0}: the other action, e.g. "die Aufnahme".</summary>
        public const string Duplicate = "Schon für {0} vergeben.";
        public const string Taken = "Belegt von einem anderen Programm. Das bisherige Kürzel bleibt aktiv.";
        public const string TakenRetrying = "Belegt von einem anderen Programm, wird alle 30 Sekunden erneut versucht.";
        public const string PrintHint = "Druck allein: in Windows unter Einstellungen > Barrierefreiheit > Tastatur „Druck-Taste zum Öffnen des Bildschirmausschnitts verwenden“ ausschalten.";
    }
    /// <summary>The settings window.</summary>
    public static class Settings
    {
        public const string Title = "Einstellungen";
        public const string TakeScreenshot = "Screenshot erstellen";
        public const string StartRecording = "Aufnahme starten";
        public const string ScreenshotGroup = "Screenshot";
        public const string RecordingGroup = "Aufnahme";
        public const string GeneralGroup = "Allgemein";
        public const string Hotkey = "Kurzbefehl";
        public const string ScreenshotHotkeyHint = "Startet die Auswahl auf allen Bildschirmen.";
        public const string RecordingHotkeyHint = "Startet und beendet eine Bildschirmaufnahme.";
        public const string Folder = "Speicherort";
        public const string Change = "Ändern";
        public const string Open = "Öffnen";
        public const string FolderMissing = "Der Ordner fehlt. Mit „Ändern“ einen anderen wählen.";
        public const string FolderUnusable = "Dieser Ordner lässt sich nicht nutzen. Das bisherige bleibt.";
        public const string FrameRate = "Bildrate";
        public const string Countdown = "Countdown";
        public const string CountdownHint = "Zeit bis zum Start, Esc bricht ab.";
        public const string Microphone = "Mikrofon aufnehmen";
        public const string SystemAudio = "Systemton aufnehmen";
        public const string Clicks = "Klicks zeigen";
        public const string ClicksHint = "Ein Kreis an jedem Mausklick im Video.";
        public const string Appearance = "Darstellung";
        public const string Autostart = "Mit Windows starten";
        public const string CheckUpdates = "Nach Updates suchen";
        public const string CheckUpdatesHint = "Einmal am Tag, ohne Konto; installiert wird nur nach deiner Bestätigung.";
        public const string Version = "Version";
        public const string NoHost = "Das Programm im Infobereich antwortet nicht. Nicht gespeichert.";
        public const string SaveFailed = "Konnte nicht gespeichert werden.";
        public const string AutostartFailed = "Der Autostart-Eintrag ließ sich nicht schreiben.";
        public const string Recording = "die Aufnahme";
        public const string Screenshot = "den Screenshot";
        public static readonly string[] FrameRates = { "30 Bilder pro Sekunde", "60 Bilder pro Sekunde" };
        public static readonly string[] Countdowns = { "Aus", "3 Sekunden", "5 Sekunden" };
        public static readonly string[] Appearances = { "Wie Windows", "Hell", "Dunkel" };
    }
    /// <summary>The update notice.</summary>
    public static class Update
    {
        public const string Title = "Nazar Capture – Update";
        public const string Now = "Jetzt aktualisieren";
        public const string Later = "Später";
        public const string WhatsNew = "Was ist neu";
        public const string Downloading = "Wird heruntergeladen und geprüft …";
        public const string Installing = "Wird installiert, das Programm startet neu.";
        public const string Failed = "Update nicht möglich";
        public static string Available(string version) => "Version " + version + " ist da";
        public static string Running(string version) => "Du hast " + version + ".";
        public static string Reason(Engine.Updates.UpdateProblem problem) => problem switch
        {
            Engine.Updates.UpdateProblem.NoDownload => "Das Release enthält keine Windows-Datei.",
            Engine.Updates.UpdateProblem.NoChecksum => "Für den Download gibt es keine Prüfsumme; ohne sie wird nichts installiert.",
            Engine.Updates.UpdateProblem.ChecksumMismatch => "Die Prüfsumme stimmt nicht; der Download wurde verworfen.",
            Engine.Updates.UpdateProblem.DownloadFailed => "Der Download ist fehlgeschlagen. Später noch einmal versuchen.",
            Engine.Updates.UpdateProblem.InstallerMissing => "Im Download fehlt das Programm.",
            _ => "Das Installationsprogramm ließ sich nicht starten.",
        };
    }
}
