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
