namespace NazarCapture.Engine.Recording;

/// <summary>
/// The real recording windows. Each window is created on first use, and anew after the interface language changed
/// (<see cref="PerLanguage{T}"/>; the click ring has no text).
/// </summary>
internal sealed class WpfRecordingUi : IRecordingUi
{
    readonly PerLanguage<SelectionOverlay> overlays = new(() => new SelectionOverlay(null));
    readonly PerLanguage<RecordingFrame> frames = new(() => new RecordingFrame());
    readonly PerLanguage<RecordingToolbar> toolbars = new(() => new RecordingToolbar());
    readonly PerLanguage<CountdownWindow> countdowns = new(() => new CountdownWindow());
    readonly PerLanguage<RecordingCard> cards = new(() => new RecordingCard());
    readonly PerLanguage<TrimWindow> trimWindows = new(() => new TrimWindow());
    ClickHighlighter? clicks;

    public RecordingCard Card => cards.Value;
    public TrimWindow TrimWindow => trimWindows.Value;
    RecordingFrame Frame => frames.Value;
    RecordingToolbar Toolbar => toolbars.Value;
    CountdownWindow Countdown => countdowns.Value;
    ClickHighlighter Clicks => clicks ??= new ClickHighlighter(() => ScreenRecorder.Live && !ScreenRecorder.Paused);

    public RECT WorkAreaAtCursor() => NativeMethods.WorkAreaAtCursor();

    public void PickArea(Action<Int32Rect, RECT, RECT> picked, Action cancelled)
    {
        var overlay = overlays.Value;
        overlay.Recorded = picked;
        overlay.Aborted = cancelled;
        overlay.OpenForRecording();
    }

    public void ShowFrame(Int32Rect area, RECT monitor) => Frame.Present(area, monitor);
    public void HideFrame() => Frame.Hide();
    public void SetFramePaused(bool paused) => Frame.SetPaused(paused);

    public void StartCountdown(Int32Rect area, RECT monitor, int seconds, Action launch, Action finished, Action cancelled) =>
        Countdown.Run(area, monitor, seconds, launch, finished, cancelled);

    public void StopCountdown() => Countdown.Stop();

    public void ShowToolbar(Int32Rect area, RECT work) => Toolbar.Present(area, work);
    public void RefreshToolbar() => Toolbar.Refresh();
    public void HideToolbar() => Toolbar.Hide();

    public void StartClickHighlights(Int32Rect area, bool enabled)
    {
        Clicks.Enabled = enabled;
        Clicks.Start(area, Toolbar.Hwnd);
    }

    public void SetClickHighlights(bool enabled) => Clicks.Enabled = enabled;
    public void StopClickHighlights() => Clicks.Stop();

    public void ShowSaving(double seconds, RECT work) => Card.Saving(seconds, work);
    public void ShowProblem(string heading, string reason, RECT work) => Card.Problem(heading, reason, work);
    public void ShowSaveFailed(string reason, string? rawDataFolder, RECT work) => Card.SaveFailed(reason, rawDataFolder, work);
    public void ShowRecording(RecordedVideo video, string heading, RECT work) => Card.Present(video, heading, work);

    public Task<bool> CopyFileToClipboardAsync(string path) => ClipboardWriter.SetFileAsync(path);

    public void CloseLiveWindows()
    {
        Attempt("Stopping the countdown", () => countdowns.Existing?.Stop());
        Attempt("Stopping the click highlights", () => clicks?.Stop());
        Attempt("Hiding the toolbar", () => toolbars.Existing?.Hide());
        Attempt("Hiding the frame", () => frames.Existing?.Hide());
    }

    static void Attempt(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { ErrorLog.Write(what, ex); }   // recovery continues with the next window
    }

    public void Post(Action action) => Application.Current.Dispatcher.BeginInvoke(action);
}

/// <summary>The real system side of a recording: ffmpeg, DXGI, settings and work folders.</summary>
internal sealed class RecordingBackend : IRecordingBackend
{
    static readonly TimeSpan LeftoverAge = TimeSpan.FromHours(1);

    public bool FfmpegAvailable => Ffmpeg.Available;

    public void PrepareEncoder() => _ = Ffmpeg.DetectEncoderAsync();

    public OutputLookup FindOutput(RECT monitor, out int output, out RECT desktop) => Dxgi.Find(monitor, out output, out desktop);

    public IRecordingSession StartSession(Int32Rect area, int output, RECT desktop, AudioSettings audio, int fps)
    {
        var session = new RecordingSession(area, output, desktop, audio, fps);
        session.StartAudio();
        return session;
    }

    public string WritableOrFallback(string folder) => FileKeeping.WritableOrFallback(folder);

    public bool IsInFallback(string path) => FileKeeping.IsInFallback(path);

    public string NewRecordingPath(DateTime begun, string folder)
    {
        Directory.CreateDirectory(folder);
        return UniqueFile.Next(Path.Combine(folder, FileName(begun)));
    }

    /// <summary><see cref="UiText.Files.RecordingPrefix"/> plus the start time, e.g. "&lt;prefix&gt;2026-09-26 14-03-12.mp4".</summary>
    internal static string FileName(DateTime begun) =>
        UiText.Files.RecordingPrefix + begun.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture) + ".mp4";

    public CaptureSettingsSnapshot LoadSettings() => CaptureSettings.Load();

    public void SaveSettings(AudioSettings settings) => CaptureSettings.SaveAudio(settings.Microphone, settings.SystemSound, settings.ShowClicks);

    // Leftovers of crashed recordings, also in the folder older versions used. Raw data kept after a failed
    // save is the user's to rescue and never removed here (see RecordingSession.RawDataFolder).
    public void RemoveLeftovers() => Task.Run(RemoveLeftoversNow).LogFailures("Removing old recording work folders");   // file system work, never on the UI thread

    static void RemoveLeftoversNow()
    {
        RemoveOldWorkFolders(CaptureSettings.RecordingWorkRoot);
        RemoveOldWorkFolders(CaptureSettings.LegacyRecordingWorkRoot);
        try { if (Directory.Exists(CaptureSettings.LegacyRecordingWorkRoot)) Directory.Delete(CaptureSettings.LegacyRecordingWorkRoot); }
        catch (IOException) { }   // not empty yet (an older version may still be recording): next time
        catch (UnauthorizedAccessException) { }
    }

    internal static void RemoveOldWorkFolders(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (string folder in Directory.GetDirectories(root))
            {
                if (DateTime.Now - Directory.GetCreationTime(folder) <= LeftoverAge) continue;   // may still be recording
                if (File.Exists(Path.Combine(folder, RecordingSession.KeptMarkerName))) continue;
                try { Directory.Delete(folder, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }   // still in use: next time
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Removing old recording work folders", ex);
        }
    }
}
