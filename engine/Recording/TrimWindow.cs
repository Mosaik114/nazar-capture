namespace NazarCapture.Engine.Recording;

/// <summary>
/// Trim window: video preview, film strip with in/out handles, save as MP4 or GIF. A <see cref="NazarWindow"/> (own
/// frame on paper, head with mark and title, close cross) built from the kit.
/// Keys: Space play/pause, I/O set in/out to the playhead, arrows 0.1 s (1 s with Shift),
/// Home/End jump, Enter saves, Esc closes (or cancels a running export). Tab reaches every button.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The export cancellation is disposed when the export ends or the window is dismissed; the window lives as long as the engine.")]
internal sealed partial class TrimWindow : NazarWindow, IRetirable
{
    const double ContentWidth = 880, HandleWidth = 14, TrackHeight = 64, StripTop = 4, StripHeight = 56;
    const double MinSelection = 0.3;   // seconds
    const double StripWidth = ContentWidth - 2 * HandleWidth;

    enum DragTarget { None, Start, End, Playhead }

    readonly IntPtr hwnd;
    readonly StoneChrome videoBox, bigPlayButton, startHandle, endHandle;
    readonly IconButton playButton;
    readonly SecondaryButton soundButton;
    readonly FrameworkElement actionsRow, progressRow;
    readonly Shapes.Rectangle selectionTop, selectionBottom;
    readonly Shapes.Ellipse playheadKnob;
    readonly MediaElement media;
    readonly Image poster, strip;
    readonly TextBlock subtitle, timeText, selectionText, playGlyph, soundGlyph, progressText;
    readonly Canvas track;
    readonly Shapes.Rectangle dimBefore, dimAfter, playheadLine;
    readonly ProgressLine progressLine = new();
    readonly ScaleTransform progressScale;
    /// <summary>How often the playhead follows the playing video.</summary>
    static readonly TimeSpan PlaybackTick = TimeSpan.FromMilliseconds(30);
    /// <summary>Darkens the film strip outside the selection.</summary>
    static readonly SolidColorBrush StripShade = UiTokens.Fixed.FilmStripShadeBrush;

    readonly DispatcherTimer playbackTimer = new(DispatcherPriority.Render) { Interval = PlaybackTick };
    readonly EngineWork shown = new();   // the engine must not quit while the user trims
    RecordedVideo? video;
    RECT workArea;
    double duration = 1, selectionStart, selectionEnd = 1, playhead, grabOffset;
    bool playing, withSound, exporting, exported, retired;
    CancellationTokenSource? exportCancellation, stripCancellation;
    DragTarget dragging;
    int generation;

    public TrimWindow() : base(UiText.Titles.Trim)
    {
        // A normal window (taskbar, activation) unlike the other recording windows: the user works in it.
        ShowInTaskbar = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        CloseOnEscape = false;   // Esc first cancels a running export (OnKey)

        subtitle = TextStyle.Text("", TextRole.Description, UiTokens.Keys.TextSecondary);
        poster = new Image { Stretch = Stretch.Uniform };
        media = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, ScrubbingEnabled = true, Stretch = Stretch.Uniform };
        bigPlayButton = BuildBigPlayButton();
        videoBox = BuildVideo();
        Localizer.KeepLeftToRight(videoBox);   // the video is never mirrored

        playButton = new IconButton(IconGlyphs.PlaySolid, UiText.Trim.Play, () => TogglePlay(), size: 36, role: MuiRole.Secondary);
        playGlyph = (TextBlock)playButton.Content;
        playGlyph.FontSize = 14;
        timeText = TextStyle.Text("", TextRole.RowTitle);
        timeText.VerticalAlignment = VerticalAlignment.Center;
        selectionText = TextStyle.Text("", TextRole.RowTitle, UiTokens.Keys.TextSecondary);
        selectionText.VerticalAlignment = VerticalAlignment.Center;

        track = new Canvas { Width = ContentWidth, Height = TrackHeight, Margin = new Thickness(0, 16, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        Localizer.KeepLeftToRight(track);   // time runs from left to right in every language
        strip = new Image { Width = StripWidth, Height = StripHeight, Stretch = Stretch.UniformToFill, ClipToBounds = true };   // do not squash, crop the overflow
        dimBefore = new Shapes.Rectangle { Height = StripHeight, Fill = StripShade };
        dimAfter = new Shapes.Rectangle { Height = StripHeight, Fill = StripShade };
        selectionTop = SelectionEdge(0);
        selectionBottom = SelectionEdge(TrackHeight - StripTop);
        startHandle = TrimHandle();
        endHandle = TrimHandle();
        playheadLine = new Shapes.Rectangle { Width = 2, Height = TrackHeight + 12, IsHitTestVisible = false };
        playheadKnob = new Shapes.Ellipse { Width = 12, Height = 12, IsHitTestVisible = false, StrokeThickness = 2 };
        BuildTimeline();

        soundGlyph = TextStyle.Icon(IconGlyphs.Volume, 15);
        soundButton = BuildSoundButton();
        actionsRow = BuildActions();
        progressText = TextStyle.Text("", TextRole.RowTitle);
        progressScale = progressLine.Scale;
        progressScale.ScaleX = 0;
        progressRow = BuildProgress();

        Body = BuildBody();
        PreviewKeyDown += (_, e) => OnKey(e);
        Closing += (_, e) => { if (retired) return; e.Cancel = true; Dismiss(); };   // the window is kept for the next recording
        hwnd = new WindowInteropHelper(this).EnsureHandle();
        playbackTimer.Tick += delegate { FollowPlayback(); };
    }

    protected override void RequestClose() => Dismiss();

    /// <summary>Closes for good (the interface language changed; only ever while it is hidden).</summary>
    public void Retire()
    {
        retired = true;
        Close();
    }

    StackPanel BuildBody()
    {
        var layout = new StackPanel { Width = ContentWidth, Margin = new Thickness(UiTokens.Space.Window, 0, UiTokens.Space.Window, UiTokens.Space.Window) };
        layout.Children.Add(subtitle);
        layout.Children.Add(videoBox);
        layout.Children.Add(BuildTransport());
        layout.Children.Add(track);
        layout.Children.Add(BuildFooter());
        return layout;
    }

    /// <summary>Tests: build the content but never show the window.</summary>
    internal bool Offscreen { get; set; }

    /// <summary>Tests: the film strip arrived from ffmpeg.</summary>
    internal bool FilmStripLoaded => strip.Source != null;

    // ---------- Opening ----------

    public void Open(RecordedVideo recorded, RECT work)
    {
        video = recorded;
        workArea = work;
        duration = Math.Max(0.1, recorded.Duration);
        selectionStart = 0;
        selectionEnd = duration;
        playhead = 0;
        withSound = recorded.Audio;
        exporting = exported = false;
        playing = false;
        int openedAt = ++generation;
        Palette.Apply(Resources);
        subtitle.Text = UiText.Trim.VideoInfo(Path.GetFileName(recorded.Path), Formats.Clock(Math.Round(recorded.Duration)), recorded.Width, recorded.Height);
        double videoHeight = recorded.Width > 0 ? ContentWidth * recorded.Height / recorded.Width : ContentWidth * 9 / 16;
        videoBox.Height = Math.Round(Math.Max(180, Math.Min(495, videoHeight)));
        videoBox.Width = ContentWidth;
        videoBox.Clip = new RectangleGeometry(new Rect(0, 0, ContentWidth, videoBox.Height), 10, 10);
        poster.Source = recorded.Thumbnail;
        poster.Visibility = Visibility.Visible;
        media.IsMuted = !withSound;
        if (!Offscreen)
        {
            media.Source = new Uri(recorded.Path);
            media.Play();
            media.Pause();
        }
        soundButton.Visibility = recorded.Audio ? Visibility.Visible : Visibility.Collapsed;
        actionsRow.Visibility = Visibility.Visible;
        progressRow.Visibility = Visibility.Collapsed;
        strip.Source = null;
        Refresh();
        Draw();
        LoadFilmStrip(recorded, openedAt);

        if (Offscreen) return;
        shown.Begin();
        Left = -10000;
        Show();
        UpdateLayout();
        double scale = NativeMethods.ScaleOfMonitor(work);   // the target monitor's scale, not the one the window was measured on
        int width = (int)Math.Round(ActualWidth * scale), height = (int)Math.Round(ActualHeight * scale);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, (work.left + work.right - width) / 2, Math.Max(work.top, (work.top + work.bottom - height) / 2), 0, 0, NativeMethods.SWP_NOSIZE);
        ToolWindowBase.TakeFocus(this, hwnd);
    }

    void Dismiss()
    {
        shown.End();
        if (exporting) CancelExport();
        CancelFilmStrip();
        generation++;
        playbackTimer.Stop();
        playing = false;
        media.Stop();
        media.Close();
        media.Source = null;
        Hide();
        var closed = video;
        video = null;
        if (!exported && closed != null) ScreenRecorder.TrimWindowClosed(closed, workArea);
    }
}
