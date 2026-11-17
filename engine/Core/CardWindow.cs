using System.Windows.Controls.Primitives;

namespace NazarCapture.Engine.Core;

/// <summary>
/// What the screenshot card and the recording card share, built from the kit: the <see cref="Card"/> plate with header
/// and close cross, the thumbnail box, the row of <see cref="ActionButton"/>s, the bar at the bottom, how the card slides
/// in at the bottom right of the work area (with the target monitor's scale) and fades out, and the keyboard way in:
/// while a card is shown, the screenshot hotkey with Ctrl added brings it to the keyboard (Tab between the actions, Esc
/// closes). Each card keeps its own content and states. A failed card stays until it is closed.
/// </summary>
internal abstract class CardWindow : ToolWindowBase
{
    public const double ContentWidth = Card.ContentWidth;
    public const double MaxThumbnailHeight = 176, MinThumbnailHeight = 90, ThumbnailRadius = 8;

    protected readonly Card card = new();
    protected readonly Image thumbnail = new() { Stretch = Stretch.Fill, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    protected readonly StoneChrome thumbnailBox = new() { Radius = ThumbnailRadius, Depth = 0, Width = ContentWidth };
    protected readonly UniformGrid buttons = Card.ActionRow();
    protected readonly ProgressLine bar = new();
    protected readonly TranslateTransform barMove = new();
    protected readonly IconButton close;
    readonly TranslateTransform slide = new();
    WindowHotkey? keyboardWay;

    protected CardWindow(string title) : base(title)
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        Localizer.Apply(this);   // Arabic: texts and actions from right to left
        Localizer.KeepLeftToRight(thumbnailBox);   // the picture is never mirrored
        thumbnailBox.SetResourceReference(StoneChrome.FillProperty, UiTokens.Keys.ControlSurface);
        close = new IconButton(IconGlyphs.ChromeClose, UiText.Common.Close, () => Dismiss(), size: 24) { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(close, Dock.Right);
        card.Header.Children.Insert(0, close);
        var transforms = new TransformGroup();
        transforms.Children.Add(bar.Scale);
        transforms.Children.Add(barMove);
        bar.Fill.RenderTransform = transforms;
        bar.ClipToBounds = true;   // the indeterminate bar sweeps beyond both ends
        bar.Margin = new Thickness(UiTokens.Space.Xs, UiTokens.Space.Lg, UiTokens.Space.Xs, 0);
        card.Opacity = 0;
        card.RenderTransform = slide;
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Dismiss(); }
        };
        Content = card;
    }

    /// <summary>Tests: build the content but never show the window.</summary>
    internal bool Offscreen { get; set; }

    /// <summary>The card plate (tests).</summary>
    internal Card Plate => card;

    /// <summary>Closes the card now (cross, Esc, right click).</summary>
    protected abstract void Dismiss();

    /// <summary>The thumbnail with its chips on top, in the rounded box.</summary>
    protected void BuildThumbnailBox(params UIElement[] layers)
    {
        var grid = new Grid();
        foreach (var layer in layers) grid.Children.Add(layer);
        thumbnailBox.Child = grid;
    }

    /// <summary>
    /// Shows <paramref name="image"/> in its aspect ratio with rounded corners; tall images get narrower instead of
    /// cropped. Without an image the box keeps its minimum height.
    /// </summary>
    protected void ShowThumbnail(BitmapSource? image)
    {
        double width = ContentWidth, height = MinThumbnailHeight;
        if (image != null && image.PixelWidth > 0)
        {
            height = ContentWidth * image.PixelHeight / image.PixelWidth;
            if (height > MaxThumbnailHeight) { width = width * MaxThumbnailHeight / height; height = MaxThumbnailHeight; }
        }
        thumbnail.Source = image;
        thumbnail.Width = width;
        thumbnail.Height = height;
        thumbnail.Clip = new RectangleGeometry(new Rect(0, 0, width, height), ThumbnailRadius, ThumbnailRadius);
        thumbnailBox.Height = Math.Max(height, MinThumbnailHeight);
    }

    /// <summary>The normal card: mark, title, subtitle; the close cross only for problems.</summary>
    protected void ShowNormal(string symbol, string title, string subtitle)
    {
        card.ShowNormal(symbol, title, subtitle);
        close.Visibility = Visibility.Collapsed;
        bar.Visibility = Visibility.Visible;
    }

    /// <summary>A failed card: warning mark, title in the error colour, close cross; it stays until closed.</summary>
    protected void ShowError(string title, string subtitle)
    {
        card.ShowError(title, subtitle);
        close.Visibility = Visibility.Visible;
        bar.Visibility = Visibility.Collapsed;   // no lifetime: a problem stays until it is closed
    }

    /// <summary>Lets the bar run down from full to empty; <paramref name="ended"/> runs when it got there.</summary>
    protected void RunDown(int milliseconds, Action ended)
    {
        var run = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(milliseconds)));
        run.Completed += delegate { ended(); };
        bar.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, run);
    }

    protected void StopBar(double fraction)
    {
        barMove.BeginAnimation(TranslateTransform.XProperty, null);
        barMove.X = 0;
        bar.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        bar.Scale.ScaleX = fraction;
    }

    /// <summary>Places the card at the bottom right of the work area; slides in softly when it was hidden.</summary>
    protected void SlideIn(RECT workArea)
    {
        if (Offscreen) { card.Opacity = 1; return; }
        bool appearing = !IsVisible;
        ShowAtBottomRight(workArea);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(1, Motion.SlideIn) { EasingFunction = ease };
        var rise = new DoubleAnimation(0, Motion.SlideIn) { EasingFunction = ease };
        if (appearing) { fade.From = 0; rise.From = 12; }
        card.BeginAnimation(OpacityProperty, fade);
        slide.BeginAnimation(TranslateTransform.YProperty, rise);
        OfferKeyboard();
    }

    /// <summary>Fades the card out; <paramref name="faded"/> runs at the end (the card decides whether that still counts).</summary>
    protected void FadeCard(Action faded)
    {
        ReleaseKeyboard();
        var fade = new DoubleAnimation(0, Motion.FadeOut) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += delegate { faded(); };
        card.BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnClosed(EventArgs e)
    {
        ReleaseKeyboard();
        base.OnClosed(e);
    }

    void OfferKeyboard()
    {
        if (keyboardWay != null || Hwnd == IntPtr.Zero) return;
        if (KeyboardChord(CaptureSettings.Load().ScreenshotHotkey) is not { } chord) return;
        keyboardWay = WindowHotkey.Register(Hwnd, chord.Modifiers, chord.Key, TakeKeyboard);
    }

    void ReleaseKeyboard()
    {
        keyboardWay?.Dispose();
        keyboardWay = null;
    }

    /// <summary>The card gets the keyboard: activated, the first available action focused (with the visible edge).</summary>
    internal void TakeKeyboard()
    {
        InputModality.NoteKey();
        if (!Offscreen) TakeFocus(this, Hwnd);
        var first = buttons.Children.OfType<UIElement>().FirstOrDefault(b => b.Visibility == Visibility.Visible && b.IsEnabled && buttons.IsVisible)
            ?? (close.Visibility == Visibility.Visible ? close : null);
        first?.Focus();
    }
}
