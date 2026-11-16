namespace NazarCapture.Engine.Screenshot;

/// <summary>
/// Card in the bottom right after a capture (the frame is shared with the recording card, see
/// <see cref="CardWindow"/>): preview, file name, edit / pin / text / folder / delete.
/// Click the preview = open the image, drag the preview = drop the file into another app.
/// Never takes focus by itself (the keyboard way is the screenshot hotkey plus Ctrl); disappears after 5 seconds (not
/// while the mouse is over it). A problem stays until it is closed.
/// </summary>
internal sealed class ScreenshotCard : CardWindow
{
    const int LifetimeMs = 5000;
    readonly TextBlock sizeText;
    readonly ActionButton editButton, folderButton, deleteButton;
    readonly Action edit, pin, closed;
    string path = "";
    bool saved, pressing;
    int generation;   // invalidates pending timers and animations of an earlier state
    Point pressedAt;
    BitmapSource? shownImage;

    public ScreenshotCard(Action onEdit, Action onPin, Action onClosed) : base(UiText.Titles.ScreenshotCard)
    {
        edit = onEdit;
        pin = onPin;
        closed = onClosed;

        var sizeChip = new Chip("", ChipKind.OnImage)
        {
            Margin = new Thickness(UiTokens.Space.Md),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
        };
        sizeText = (TextBlock)sizeChip.Child;
        BuildThumbnailBox(thumbnail, sizeChip);

        editButton = new ActionButton(IconGlyphs.Edit, UiText.ScreenshotCard.Edit, delegate { if (saved) edit(); });
        folderButton = new ActionButton(IconGlyphs.Folder, UiText.Common.Folder, delegate { if (saved) Shell.Reveal(path); });
        deleteButton = new ActionButton(IconGlyphs.Delete, UiText.Common.Delete, MoveToRecycleBin);
        buttons.Children.Add(editButton);
        buttons.Children.Add(new ActionButton(IconGlyphs.Pin, UiText.ScreenshotCard.Pin, delegate { pin(); FadeOut(); }));
        buttons.Children.Add(new ActionButton(IconGlyphs.TextRecognition, UiText.ScreenshotCard.Text, ReadText));
        buttons.Children.Add(folderButton);
        buttons.Children.Add(deleteButton);

        card.Body.Children.Add(thumbnailBox);
        card.Body.Children.Add(card.Header);
        card.Body.Children.Add(buttons);
        card.Body.Children.Add(bar);

        HookUpMouse();
        CreateHandle(NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }

    /// <summary>Tests: stops the lifetime bar at a fixed width so images do not depend on timing.</summary>
    internal void SetBar(double fraction) => StopBar(fraction);

    protected override void Dismiss() => FadeOut();

    /// <summary>Edit, folder and delete wait for the file; until then they say so instead of doing nothing.</summary>
    void ShowSaved(bool done)
    {
        saved = done;
        foreach (var button in new[] { editButton, folderButton, deleteButton })
            button.SetAvailable(done, UiText.Common.NotYetSaved);
    }

    void HookUpMouse()
    {
        // The card stays while the mouse is over it
        card.MouseEnter += delegate { if (!card.IsError) StopBar(1); };
        card.MouseLeave += delegate { if (IsVisible && !pressing && !card.IsError) StartLifetime(); };
        card.MouseRightButtonUp += delegate { FadeOut(); };

        thumbnail.MouseLeftButtonDown += (_, e) => { pressedAt = e.GetPosition(this); pressing = true; thumbnail.CaptureMouse(); e.Handled = true; };
        thumbnail.MouseMove += (_, e) =>
        {
            if (!pressing || !saved) return;
            Vector moved = e.GetPosition(this) - pressedAt;
            if (Math.Abs(moved.X) < 6 && Math.Abs(moved.Y) < 6) return;
            pressing = false;
            thumbnail.ReleaseMouseCapture();
            DragDrop.DoDragDrop(thumbnail, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);   // drag into chat, mail, Explorer ...
            if (!card.IsMouseOver) StartLifetime();
        };
        thumbnail.MouseLeftButtonUp += (_, _) =>
        {
            if (!pressing) return;
            pressing = false;
            thumbnail.ReleaseMouseCapture();
            if (saved) { Shell.Open(path); FadeOut(); }
        };
    }

    // ---------- States ----------

    public void Present(ScreenCapture capture, string heading)
    {
        BitmapSource image = capture.Image;
        Palette.Apply(Resources);
        path = capture.Path;
        ShowSaved(false);
        generation++;
        thumbnailBox.Visibility = Visibility.Visible;
        ShowThumbnail(image);
        shownImage = image;
        sizeText.Text = UiText.Common.Size(image.PixelWidth, image.PixelHeight);
        ShowNormal(IconGlyphs.Accept, heading, Path.GetFileName(capture.Path));
        buttons.Visibility = Visibility.Visible;
        SlideIn(capture.WorkArea);
        if (!card.IsMouseOver) StartLifetime();
    }

    public void Saved() => ShowSaved(true);

    /// <summary>The clipboard took longer than allowed, but the image got there in the end.</summary>
    public void CopiedAfterAll()
    {
        if (IsVisible && card.Title.Text == UiText.ScreenshotCard.SavedNotCopied) card.Title.Text = UiText.Common.CopiedToClipboard;
    }

    /// <summary>Saved, but the clipboard was busy: the heading must not claim it was copied.</summary>
    public void SavedNotCopied()
    {
        ShowSaved(true);
        card.Title.Text = UiText.ScreenshotCard.SavedNotCopied;
    }

    /// <summary>An unexpected error in the screenshot flow, shown without an image; it stays until it is closed.</summary>
    public void ShowProblem(string heading, string detail, RECT workArea)
    {
        Palette.Apply(Resources);
        path = "";
        ShowSaved(false);
        generation++;
        thumbnail.Source = null;
        shownImage = null;
        thumbnailBox.Visibility = Visibility.Collapsed;
        ShowError(heading, detail);
        buttons.Visibility = Visibility.Collapsed;
        StopBar(0);
        SlideIn(workArea);
    }

    // Recognizes and copies the text in the image; the result is shown on the card.
    void ReadText() => ReadTextAsync().OnFailure("Recognizing text on the card", () => card.Title.Text = UiText.Common.TextRecognitionUnavailable);

    async Task ReadTextAsync()
    {
        var image = shownImage;
        if (image == null) return;
        int startedAt = generation;
        card.Title.Text = UiText.Common.ReadingText;
        var result = await TextRecognition.ReadAsync(image);
        if (startedAt != generation) return;
        if (result.Status != TextRecognitionStatus.Recognized)
        {
            card.Title.Text = result.FailureMessage;
            return;
        }
        bool copied = await ClipboardWriter.SetTextAsync(result.Text);
        if (startedAt != generation) return;
        card.Title.Text = copied ? UiText.Common.TextCopied : UiText.Common.CopyFailed;
        card.Subtitle.Text = TextRecognition.Summary(result.Text);
    }

    /// <summary>Hides immediately for the editor without releasing the capture.</summary>
    public void HideNow()
    {
        generation++;
        StopBar(1);
        Hide();
    }

    /// <summary>Saving failed: the card says why and stays until it is closed.</summary>
    public void Failed()
    {
        ShowError(UiText.ScreenshotCard.NotSaved, UiText.ScreenshotCard.FolderNotWritable(Screenshots.Folder));
        buttons.Visibility = Visibility.Collapsed;
        generation++;
        StopBar(0);
    }

    // The bar runs down; at the end the card fades out (not while the mouse is over it).
    void StartLifetime()
    {
        if (card.IsError) return;
        int started = generation;
        RunDown(LifetimeMs, delegate { if (started == generation && !card.IsMouseOver && bar.Scale.ScaleX <= 0.001) FadeOut(); });
    }

    // Moves the file to the recycle bin so it can be restored; a failure says so on the card.
    void MoveToRecycleBin()
    {
        if (!saved) return;
        if (!NativeMethods.MoveToRecycleBin(path))
        {
            card.Title.Text = UiText.Common.DeleteFailed;
            return;
        }
        ShowSaved(false);
        ShowNormal(IconGlyphs.Delete, UiText.Common.Deleted, UiText.Common.InRecycleBin);
        buttons.Visibility = Visibility.Collapsed;
        generation++;
        var timer = new DispatcherTimer { Interval = Motion.LingerAfterLeave };
        timer.Tick += delegate { timer.Stop(); FadeOut(); };
        timer.Start();
    }

    void FadeOut()
    {
        int fadingAt = ++generation;
        StopBar(bar.Scale.ScaleX);
        FadeCard(delegate
        {
            if (fadingAt != generation) return;
            Hide();
            thumbnail.Source = null;
            closed();
            MemoryRelief.Soon();
        });
    }
}
