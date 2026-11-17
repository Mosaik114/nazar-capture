using System.Text.RegularExpressions;

namespace NazarCapture.Engine.Core;

/// <summary>
/// What happens to the files of Nazar Capture after a capture (as Hams' "Transkripte speichern"):
/// <list type="bullet">
/// <item>With "Screenshots speichern" / "Aufnahmen speichern" off, a capture goes to the clipboard only. Its file (the
/// card still needs one to drag, open, trim or make a GIF) waits in <see cref="UnsavedFolder"/>, a private folder of the
/// app, until the user keeps it with "Speichern" on the card (<see cref="Keep"/>); otherwise it is deleted after a day
/// (<see cref="UnsavedLifetime"/>). Nothing is written to the screenshot or recording folder.</item>
/// <item>"Aufbewahren" deletes captures older than the chosen number of days from the configured folders
/// (<see cref="Prune"/>): only files Nazar Capture named itself (<see cref="ScreenshotName"/>,
/// <see cref="RecordingName"/>), only directly in that folder, never a subfolder, never another program's file.</item>
/// </list>
/// Both run when the engine starts and every few hours (<see cref="StartTidying"/>), off the UI thread.
/// </summary>
internal static class FileKeeping
{
    /// <summary>How long a capture that was not kept waits in <see cref="UnsavedFolder"/>.</summary>
    public static readonly TimeSpan UnsavedLifetime = TimeSpan.FromDays(1);

    /// <summary>How often the folders are tidied while the engine runs.</summary>
    public static readonly TimeSpan TidyInterval = TimeSpan.FromHours(6);

    /// <summary>The app's own folder for captures that were not kept (below %LOCALAPPDATA%\NazarCapture).</summary>
    public static string UnsavedFolder => UnsavedFolderOverride ?? Path.Combine(CaptureSettings.Home, "unsaved");

    /// <summary>Tests: another folder for captures that were not kept (never the user's).</summary>
    internal static string? UnsavedFolderOverride { get; set; }

    /// <summary>
    /// Where a capture goes when its own folder cannot be written: a link Windows refuses to follow (error 448, see
    /// <see cref="LinkSafety"/>), a drive that is gone, no access. Below %LOCALAPPDATA%\NazarCapture, never tidied:
    /// what lands here is the user's, and the card's "Ordner" shows it. Its name is in the interface language
    /// (<see cref="FallbackFolderIn"/>).
    /// </summary>
    public static string FallbackFolder => FallbackFolderIn(CaptureSettings.Home);

    /// <summary>
    /// The fallback folder below <paramref name="home"/>: one that already exists under any language's name (the first
    /// one, "Ersatzordner", came with 1.1.7 builds before the names were translated), so a change of the interface
    /// language never splits the captures into two folders; else the name in the interface language.
    /// </summary>
    internal static string FallbackFolderIn(string home)
    {
        foreach (string name in FallbackFolderNames)
        {
            string folder = Path.Combine(home, name);
            if (Directory.Exists(folder)) return folder;
        }
        return Path.Combine(home, FolderName(UiText.Folders.Fallback));
    }

    /// <summary>The fallback folder's name in every interface language, the current one first.</summary>
    internal static IEnumerable<string> FallbackFolderNames =>
        new[] { UiText.Folders.Fallback }
            .Concat(Catalogs.All.Select(catalog => catalog.Find("folders.fallback") ?? ""))
            .Select(FolderName)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>A translated name as a folder name: characters Windows does not allow there are left out.</summary>
    static string FolderName(string name) =>
        new string(name.Where(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0).ToArray()).Trim().TrimEnd('.');

    /// <summary>Whether <paramref name="path"/> lies in a fallback folder (under any language's name).</summary>
    public static bool IsInFallback(string? path) =>
        FallbackFolderNames.Any(name => IsUnsaved(path, Path.Combine(CaptureSettings.Home, name)));

    /// <summary>
    /// <paramref name="folder"/> if a file can be created in it (it is created if missing); else, after error 448, the
    /// folder's real path behind the refused link; else <see cref="FallbackFolder"/>. The reason is logged. The check
    /// writes and deletes an empty file, because a folder can exist and still refuse writing (error 448 comes only
    /// when something is written through the link).
    /// </summary>
    public static string WritableOrFallback(string folder) => WritableOrFallback(folder, FallbackFolder);

    internal static string WritableOrFallback(string folder, string fallbackFolder)
    {
        if (CanWrite(folder, out Exception? problem)) return folder;
        // A folder reached through a link (Pictures moved to D:\ with a junction) is still the user's folder: written
        // by its real path it works even where Windows refuses the link itself.
        string? real = LinkSafety.IsUntrustedMountPoint(problem) ? LinkSafety.ResolveFolder(folder) : null;
        if (real != null && !string.Equals(real, folder, StringComparison.OrdinalIgnoreCase) && CanWrite(real, out _))
        {
            ErrorLog.Write("The folder " + folder + " is reached through a link Windows refuses (error 448); using its real path " + real, null);
            return real;
        }
        ErrorLog.Write("The folder " + folder + " cannot be written"
            + (LinkSafety.IsUntrustedMountPoint(problem) ? " (Windows refuses a link on the way, error 448)" : "")
            + "; using " + fallbackFolder, problem);
        Directory.CreateDirectory(fallbackFolder);
        return fallbackFolder;
    }

    internal static bool CanWrite(string folder, out Exception? problem)
    {
        problem = null;
        try
        {
            Directory.CreateDirectory(folder);
            string probe = Path.Combine(folder, ".nazar-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            problem = ex;
            return false;
        }
    }

    /// <summary>
    /// The names <see cref="Screenshots.NewPath"/> gives ("Nazar-Screenshot 2026-11-17 14-03-12.png", also " (2)" from
    /// <see cref="UniqueFile"/>; JPEG for an edited copy saved that way).
    /// </summary>
    public static readonly Regex ScreenshotName = new(
        "^" + Regex.Escape(UiText.Files.ScreenshotPrefix) + @"\d{4}-\d{2}-\d{2} \d{2}-\d{2}-\d{2}(?: \(\d+\))?\.(?:png|jpg)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// The names of recordings and what is made of them: the video, " gekürzt" (trimmed, any number of times) and the GIF,
    /// each possibly with " (2)". Raw data kept after a failed save is a folder and never matches.
    /// </summary>
    public static readonly Regex RecordingName = new(
        "^" + Regex.Escape(UiText.Files.RecordingPrefix) + @"\d{4}-\d{2}-\d{2} \d{2}-\d{2}-\d{2}(?: \(\d+\))?(?:" + Regex.Escape(UiText.Files.TrimmedSuffix) + @"(?: \(\d+\))?)*\.(?:mp4|gif)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Whether <paramref name="path"/> is a capture that was not kept (it lies in <see cref="UnsavedFolder"/>).</summary>
    public static bool IsUnsaved(string? path) => IsUnsaved(path, UnsavedFolder);

    internal static bool IsUnsaved(string? path, string unsavedFolder)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            string folder = Path.GetFullPath(Path.GetDirectoryName(path)!).TrimEnd('\\');
            return string.Equals(folder, Path.GetFullPath(unsavedFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>
    /// "Speichern" on a card: moves a capture from <see cref="UnsavedFolder"/> into <paramref name="folder"/> (a free
    /// name, as every capture gets). Returns the new path.
    /// </summary>
    /// <exception cref="IOException">The folder cannot be written; the file stays where it was.</exception>
    /// <exception cref="UnauthorizedAccessException">Likewise.</exception>
    public static string Keep(string path, string folder)
    {
        Directory.CreateDirectory(folder);
        string target = UniqueFile.Next(Path.Combine(folder, Path.GetFileName(path)));
        File.Move(path, target);
        return target;
    }

    /// <summary>
    /// <see cref="Keep"/>, and if <paramref name="folder"/> cannot be written, into <see cref="FallbackFolder"/>, so a
    /// capture the user wants to keep is never left to the daily tidying. Throws only if both fail.
    /// </summary>
    public static string KeepOrFallback(string path, string folder) => KeepOrFallback(path, folder, FallbackFolder);

    internal static string KeepOrFallback(string path, string folder, string fallbackFolder)
    {
        try { return Keep(path, folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            string other = WritableOrFallback(folder, fallbackFolder);   // the real path behind a refused link, if that is it
            ErrorLog.Write("Keeping " + Path.GetFileName(path) + " in " + folder + " failed; using " + other, ex);
            return Keep(path, other);
        }
    }

    /// <summary>
    /// Deletes the files of Nazar Capture in <paramref name="folder"/> whose last change is more than
    /// <paramref name="keepDays"/> days before <paramref name="now"/> (0 keeps everything). Only names that match
    /// <paramref name="ownName"/>, only directly in the folder; a missing folder, a file in use or without access is
    /// skipped (logged). Returns how many went.
    /// </summary>
    public static int Prune(string folder, int keepDays, Regex ownName, DateTime now)
    {
        if (keepDays <= 0) return 0;
        return DeleteOlder(folder, now - TimeSpan.FromDays(keepDays), ownName);
    }

    /// <summary>Deletes what waited in <see cref="UnsavedFolder"/> longer than <see cref="UnsavedLifetime"/>.</summary>
    public static int PruneUnsaved(string unsavedFolder, DateTime now) =>
        DeleteOlder(unsavedFolder, now - UnsavedLifetime, null);

    static int DeleteOlder(string folder, DateTime cutoff, Regex? ownName)
    {
        IEnumerable<string> files;
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return 0;
            files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ErrorLog.Write("Tidying " + folder, ex);
            return 0;
        }
        int removed = 0;
        foreach (string file in files)
        {
            if (ownName != null && !ownName.IsMatch(Path.GetFileName(file))) continue;   // never another program's file
            try
            {
                var info = new FileInfo(file);
                if ((info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) continue;
                if (info.LastWriteTime >= cutoff) continue;
                info.Delete();
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ErrorLog.Write("Could not delete an old capture (in use?): " + Path.GetFileName(file), ex);
            }
        }
        if (removed > 0) ErrorLog.Write("Deleted " + removed + " old capture file(s) in " + folder, null);
        return removed;
    }

    /// <summary>Tidies now with the current settings: both folders by "Aufbewahren", and the unsaved captures.</summary>
    public static void TidyNow(CaptureSettingsSnapshot settings, DateTime now)
    {
        Prune(settings.ScreenshotFolder, settings.ScreenshotKeepDays, ScreenshotName, now);
        Prune(settings.RecordingFolder, settings.RecordingKeepDays, RecordingName, now);
        PruneUnsaved(UnsavedFolder, now);
    }

    static DispatcherTimer? timer;

    /// <summary>Tidies once in the background now and then every <see cref="TidyInterval"/> (the engine's UI thread).</summary>
    public static void StartTidying()
    {
        if (timer != null) return;
        TidySoon();
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TidyInterval };
        timer.Tick += delegate { TidySoon(); };
        timer.Start();
    }

    static void TidySoon() => Task.Run(() => TidyNow(CaptureSettings.Load(), DateTime.Now)).LogFailures("Tidying the capture folders");
}
