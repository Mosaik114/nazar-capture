namespace NazarCapture.Engine.Recording;

/// <summary>
/// Card in the bottom right after a recording (like the screenshot card, see <see cref="CardWindow"/>):
/// thumbnail, file name and size, trim / GIF / folder / delete. Also shows "saving…", GIF progress and errors;
/// an error stays until it is closed.
/// </summary>
internal sealed class RecordingCard : CardWindow
{
    const int LifetimeMs = 8000;
    readonly StoneChrome playChip;
    readonly Chip lengthChip;
    readonly TextBlock lengthText, placeholderGlyph;
    RecordedVideo? video;
    RECT workArea;
    bool ready, pressing, counting;
    int generation;   // invalidates pending timers and animations of an earlier state
    Point pressedAt;

    public RecordingCard() : base(UiText.Titles.RecordingCard)
    {
        playChip = BuildPlayChip();
        lengthChip = new Chip("", ChipKind.OnImage)
        {
            Margin = new Thickness(UiTokens.Space.Md),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
        };
        lengthText = (TextBlock)lengthChip.Child;
        System.Windows.Documents.Typography.SetNumeralAlignment(lengthText, FontNumeralAlignment.Tabular);
        placeholderGlyph = TextStyle.Icon(IconGlyphs.Video, 26);
        placeholderGlyph.SetResourceReference(TextBlock.ForegroundProperty, UiTokens.Keys.TextSecondary);
        BuildThumbnailBox(placeholderGlyph, thumbnail, playChip, lengthChip);

        card.Body.Children.Add(thumbnailBox);
        card.Body.Children.Add(card.Header);
        card.Body.Children.Add(buttons);
        card.Body.Children.Add(bar);   // life time countdown, indeterminate "saving" bar or GIF progress

        HookUpMouse();
        CreateHandle(NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }

    // ---------- Building ----------

    static StoneChrome BuildPlayChip()
    {
        var play = TextStyle.Icon(IconGlyphs.PlaySolid, 16);
        play.SetResourceReference(TextBlock.ForegroundProperty, UiTokens.Keys.LightOnDark);
        play.Margin = new Thickness(3, 0, 0, 0);   // optically center the triangle
        var chip = new StoneChrome
        {
            Width = 44,
            Height = 44,
            Radius = 22,
            Depth = 0,
            IsSquircle = false,
            Child = play,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        chip.SetResourceReference(StoneChrome.FillProperty, UiTokens.Keys.PlayBackdrop);
        return chip;
    }

    void HookUpMouse()
    {
        // The card stays while the mouse is over it
        card.MouseEnter += delegate { if (counting) StopBar(1); };
        card.MouseLeave += delegate { if (IsVisible && !pressing && counting) StartLifetime(); };
        card.MouseRightButtonUp += delegate { Dismiss(); };

        thumbnail.MouseLeftButtonDown += (_, e) => { pressedAt = e.GetPosition(this); pressing = true; thumbnail.CaptureMouse(); e.Handled = true; };
        thumbnail.MouseMove += (_, e) =>
        {
            if (!pressing || !ready) return;
            Vector moved = e.GetPosition(this) - pressedAt;
            if (Math.Abs(moved.X) < 6 && Math.Abs(moved.Y) < 6) return;
            pressing = false;
            thumbnail.ReleaseMouseCapture();
            DragDrop.DoDragDrop(thumbnail, new DataObject(DataFormats.FileDrop, new[] { video!.Path }), DragDropEffects.Copy);   // drag into chat, mail, Explorer ...
            if (!card.IsMouseOver && counting) StartLifetime();
        };
        thumbnail.MouseLeftButtonUp += (_, _) =>
        {
            if (!pressing) return;
            pressing = false;
            thumbnail.ReleaseMouseCapture();
            if (ready) { Shell.Open(video!.Path); FadeOut(); }
        };
    }

    protected override void Dismiss()
    {
        if (ready || counting || card.IsError) FadeOut();
    }

    // Only while the buttons are shown (not during a GIF export or after deleting).
    ActionButton Action(string glyph, string label, Action click) =>
        new(glyph, label, delegate { if (buttons.Visibility == Visibility.Visible) click(); });

    // ---------- States ----------

    /// <summary>While ffmpeg assembles the video: placeholder and indeterminate bar.</summary>
    public void Saving(double seconds, RECT work)
    {
        ShowPlaceholder(work);
        ShowNormal(IconGlyphs.Accept, UiText.Common.Saving, UiText.Recording.Recorded(Formats.Clock(seconds)));
        card.Mark.Visibility = Visibility.Collapsed;
        StartIndeterminateBar();
        Place();
    }

    /// <summary>The recording could not start or ended early: the reason, until the card is closed.</summary>
    public void Problem(string heading, string reason, RECT work)
    {
        ShowPlaceholder(work);
        ShowError(heading, reason);
        card.Mark.Visibility = Visibility.Visible;
        counting = false;
        StopBar(0);
        Place();
    }

    /// <summary>Saving failed: the reason, and the kept raw data to open (if any) so the recording can be rescued.</summary>
    public void SaveFailed(string reason, string? rawDataFolder, RECT work)
    {
        ShowPlaceholder(work);
        ShowError(UiText.Recording.Failed, reason);
        card.Mark.Visibility = Visibility.Visible;
        if (rawDataFolder != null)
        {
            buttons.Children.Clear();
            buttons.Children.Add(Action(IconGlyphs.Folder, UiText.Recording.OpenRawData, delegate { Shell.Open(rawDataFolder); }));
            buttons.Visibility = Visibility.Visible;
        }
        counting = false;
        StopBar(0);
        Place();
    }

    void ShowPlaceholder(RECT work)
    {
        workArea = work;
        video = null;
        ready = false;
        generation++;
        Palette.Apply(Resources);
        thumbnail.Source = null;
        thumbnail.Visibility = playChip.Visibility = lengthChip.Visibility = Visibility.Collapsed;
        placeholderGlyph.Visibility = Visibility.Visible;
        thumbnailBox.Height = MinThumbnailHeight;
        buttons.Visibility = Visibility.Collapsed;
    }

    public void Present(RecordedVideo recorded, string heading, RECT work)
    {
        workArea = work;
        video = recorded;
        ready = true;
        generation++;
        Palette.Apply(Resources);
        ShowThumbnail(recorded.Thumbnail);
        thumbnail.Visibility = lengthChip.Visibility = playChip.Visibility = Visibility.Visible;
        placeholderGlyph.Visibility = recorded.Thumbnail == null ? Visibility.Visible : Visibility.Collapsed;
        lengthText.Text = UiText.Recording.Length(recorded.Gif, Formats.Clock(Math.Round(recorded.Duration)));
        bool unsaved = FileKeeping.IsUnsaved(recorded.Path);
        ShowNormal(IconGlyphs.Accept, heading,
            UiText.Recording.FileInfo(unsaved ? UiText.Common.OnlyInClipboard : Path.GetFileName(recorded.Path), Formats.FileSize(recorded.Bytes)));
        card.SubtitleIsFileName(!unsaved);
        card.Mark.Visibility = Visibility.Visible;

        buttons.Children.Clear();
        if (!recorded.Gif)
        {
            buttons.Children.Add(Action(IconGlyphs.Trim, UiText.Recording.Trim, delegate { ScreenRecorder.Trim(video!, workArea); }));
            buttons.Children.Add(Action(IconGlyphs.Gif, UiText.Recording.Gif, delegate { ScreenRecorder.MakeGif(video!, workArea); }));
        }
        // Not kept ("Aufnahmen speichern" off): "Speichern" puts it into the recording folder instead of "Ordner".
        if (unsaved) buttons.Children.Add(Action(IconGlyphs.Save, UiText.Common.Keep, KeepInFolder));
        else buttons.Children.Add(Action(IconGlyphs.Folder, UiText.Common.Folder, delegate { Shell.Reveal(video!.Path); }));
        buttons.Children.Add(Action(IconGlyphs.Delete, UiText.Common.Delete, MoveToRecycleBin));
        buttons.Visibility = Visibility.Visible;
        Place();
        if (!card.IsMouseOver) StartLifetime(); else counting = true;
    }

    /// <summary>GIF export in progress: hide the buttons, the bar shows progress.</summary>
    public void Busy(string heading)
    {
        ready = false;
        generation++;
        card.Title.Text = heading;
        buttons.Visibility = Visibility.Collapsed;
        counting = false;
        SetBar(0);
    }

    public void Progress(double fraction)
    {
        if (!ready) bar.Scale.ScaleX = fraction;
    }

    public void HideNow()
    {
        generation++;
        counting = false;
        StopBar(1);
        Hide();
    }

    /// <summary>Tests: stops the bar animations at a fixed width so images do not depend on timing.</summary>
    internal void SetBar(double fraction) => StopBar(fraction);

    void Place() => SlideIn(workArea);

    void StartIndeterminateBar()
    {
        counting = false;
        StopBar(0.3);
        double trackWidth = ContentWidth - 6;
        barMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-0.3 * trackWidth, trackWidth, Motion.IndeterminateSweep)
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    // The bar runs down; at the end the card fades out (not while the mouse is over it).
    void StartLifetime()
    {
        counting = true;
        int started = generation;
        barMove.BeginAnimation(TranslateTransform.XProperty, null);
        barMove.X = 0;
        RunDown(LifetimeMs, delegate { if (started == generation && !card.IsMouseOver && bar.Scale.ScaleX <= 0.001) FadeOut(); });
    }

    /// <summary>"Speichern": the recording moves into the recording folder and the card shows it there.</summary>
    internal void KeepInFolder() => KeepInFolder(CaptureSettings.Load().RecordingFolder);

    internal void KeepInFolder(string folder)
    {
        if (video == null || !ready || !FileKeeping.IsUnsaved(video.Path)) return;
        string kept;
        try { kept = FileKeeping.Keep(video.Path, folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            ErrorLog.Write("Keeping a recording", ex);
            card.Title.Text = UiText.Common.CouldNotSave;
            return;
        }
        Present(video.MovedTo(kept), UiText.Common.Kept, workArea);
    }

    internal RecordedVideo? Shown => video;

    // Moves the file to the recycle bin so it can be restored; a failure says so on the card.
    void MoveToRecycleBin()
    {
        if (!NativeMethods.MoveToRecycleBin(video!.Path))
        {
            card.Title.Text = UiText.Common.DeleteFailed;
            return;
        }
        ready = false;
        ShowNormal(IconGlyphs.Delete, UiText.Common.Deleted, UiText.Common.InRecycleBin);
        buttons.Visibility = Visibility.Collapsed;
        int deletedAt = ++generation;
        var timer = new DispatcherTimer { Interval = Motion.LingerAfterLeave };
        timer.Tick += delegate { timer.Stop(); if (deletedAt == generation) FadeOut(); };
        timer.Start();
    }

    void FadeOut()
    {
        int fadingAt = ++generation;
        counting = false;
        StopBar(bar.Scale.ScaleX);
        FadeCard(delegate
        {
            if (fadingAt != generation) return;
            Hide();
            thumbnail.Source = null;
            video = null;
        });
    }
}
