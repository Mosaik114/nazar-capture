namespace MosaikCapture.Engine.Screenshot;

/// <summary>
/// A pinned screenshot: floats above everything, exactly where it was captured.
/// Drag = move, mouse wheel = zoom, Ctrl+wheel = opacity, double click / Esc = close,
/// right click, Shift+F10 or the menu key = menu (copy, save as, original size, close), all with the keyboard, too.
/// </summary>
internal sealed class PinnedScreenshot : ToolWindowBase
{
    const double ShadowRoom = Elevation.Margin;
    const double MinZoom = 0.1, MaxZoom = 5, ZoomStep = 1.1;
    const uint MoveFlags = NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE;
    static readonly List<PinnedScreenshot> open = new();

    readonly BitmapSource image;
    readonly string fileName;
    readonly IntPtr hwnd;
    readonly Image view;
    readonly Grid frame;
    readonly IconButton closeButton;
    readonly FloatingLabel chip;
    readonly TextBlock chipText;
    readonly KitMenu menu;
    readonly DispatcherTimer chipTimer;
    double scale = 1, zoom = 1;
    bool placed;

    public static void Open(BitmapSource image, int x, int y, string fileName)
    {
        var pin = new PinnedScreenshot(image, fileName);
        open.Add(pin);
        pin.PlaceAt(x, y);
    }

    public static void CloseAll()
    {
        foreach (var pin in open.ToArray()) pin.Close();
    }

    internal PinnedScreenshot(BitmapSource pinnedImage, string file) : base(UiText.Titles.Pin)
    {
        image = pinnedImage;
        fileName = file;
        SizeToContent = SizeToContent.WidthAndHeight;

        view = new Image { Source = image, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.HighQuality);
        closeButton = BuildCloseButton();
        chip = BuildChip(out chipText);
        chipTimer = new DispatcherTimer { Interval = Motion.FeedbackChip };
        chipTimer.Tick += delegate { chipTimer.Stop(); chip.Visibility = Visibility.Collapsed; };

        // The ink edge lies on top of the image instead of around it so the image sits pixel-exactly where it was.
        var edge = new StoneChrome { Radius = 0, IsSquircle = false, Depth = 0, IsHitTestVisible = false, InkWidth = 2 };
        edge.SetResourceReference(StoneChrome.InkProperty, UiTokens.Keys.Ink);
        frame = new Grid
        {
            Margin = new Thickness(ShadowRoom),
            Cursor = Cursors.SizeAll,
            Effect = Elevation.Floating(),
            Focusable = true,
            FocusVisualStyle = null,
        };
        frame.Children.Add(view);
        frame.Children.Add(edge);
        frame.Children.Add(closeButton);
        frame.Children.Add(chip);
        System.Windows.Automation.AutomationProperties.SetName(frame, UiText.Titles.Pin);
        Content = frame;
        menu = BuildMenu();
        frame.ContextMenu = menu;

        HookUpInput();
        Closed += delegate { open.Remove(this); view.Source = null; };
        hwnd = CreateHandle(NativeMethods.WS_EX_TOOLWINDOW);   // not in Alt+Tab
    }

    /// <summary>The menu (tests: every entry is reachable with the keyboard and named).</summary>
    internal KitMenu Menu => menu;

    // ---------- Building ----------

    IconButton BuildCloseButton()
    {
        // A paper stone on the image: readable on any picture, appears with the pointer or the keyboard focus.
        var button = new IconButton(IconGlyphs.Cancel, UiText.Pin.CloseTip, Close, size: 24, role: MuiRole.Secondary)
        {
            Margin = new Thickness(UiTokens.Space.Sm + UiTokens.Space.Xs),
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
        };
        ((TextBlock)button.Content).FontSize = 10;
        button.GotKeyboardFocus += delegate { FadeCloseButton(1); };
        return button;
    }

    // Short feedback in the bottom left: zoom, opacity, "copied".
    static FloatingLabel BuildChip(out TextBlock text)
    {
        var feedback = FloatingLabel.ForText(out text, shadow: false);
        feedback.Margin = new Thickness(UiTokens.Space.Md);
        feedback.HorizontalAlignment = HorizontalAlignment.Left;
        feedback.VerticalAlignment = VerticalAlignment.Bottom;
        feedback.Visibility = Visibility.Collapsed;
        return feedback;
    }

    KitMenu BuildMenu()
    {
        var items = new KitMenu();
        items.Add(UiText.Pin.Copy, Copy, IconGlyphs.Copy, UiText.Pin.CopyShortcut);
        items.Add(UiText.Pin.SaveAs, SaveAs, IconGlyphs.Save, UiText.Pin.SaveAsShortcut);
        items.Add(UiText.Pin.OriginalSize, delegate { ZoomTo(1, atPointer: false); }, IconGlyphs.ZoomOriginal, "0");
        items.AddSeparator();
        items.Add(UiText.Pin.Close, Close, IconGlyphs.Cancel, "Esc");
        items.Add(UiText.Pin.CloseAll, CloseAll, IconGlyphs.ChromeClose);
        return items;
    }

    void HookUpInput()
    {
        frame.MouseEnter += delegate { FadeCloseButton(1); };
        frame.MouseLeave += delegate { if (!menu.IsOpen) FadeCloseButton(0); };
        frame.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { Close(); return; }
            Activate();
            frame.Focus();
            try { DragMove(); }
            catch (InvalidOperationException) { }   // mouse button already released
        };
        frame.MouseRightButtonDown += delegate { Activate(); frame.Focus(); };
        menu.Opened += delegate { FadeCloseButton(1); };
        menu.Closed += delegate { if (!frame.IsMouseOver) FadeCloseButton(0); };
        Activated += delegate { if (!frame.IsKeyboardFocusWithin) frame.Focus(); };
        MouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) ChangeOpacity(e.Delta > 0 ? 0.1 : -0.1);
            else ZoomTo(zoom * (e.Delta > 0 ? ZoomStep : 1 / ZoomStep), atPointer: true);
        };
        KeyDown += (_, e) => { if (OnKey(e.Key, (Keyboard.Modifiers & ModifierKeys.Control) != 0)) e.Handled = true; };
    }

    bool OnKey(Key key, bool control)
    {
        if (key == Key.Escape) Close();
        else if (control && key == Key.C) Copy();
        else if (control && key == Key.S) SaveAs();
        else if (key is Key.D0 or Key.NumPad0) ZoomTo(1, atPointer: false);
        else if (key is Key.OemPlus or Key.Add) ZoomTo(zoom * ZoomStep, atPointer: false);
        else if (key is Key.OemMinus or Key.Subtract) ZoomTo(zoom / ZoomStep, atPointer: false);
        else return false;
        return true;
    }

    void FadeCloseButton(double opacity) =>
        closeButton.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, Motion.HoverFade));

    // ---------- Placement and zoom ----------

    // (x, y) = top-left corner of the image in device pixels.
    void PlaceAt(int x, int y)
    {
        Left = -20000;
        Top = -20000;
        view.Width = image.PixelWidth;
        view.Height = image.PixelHeight;
        Show();
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, x, y, 0, 0, MoveFlags);   // move to the target monitor first so the scale is right
        scale = ToolWindows.Scale(hwnd);
        ApplyZoom();
        placed = true;
        UpdateLayout();
        int shadow = (int)Math.Round(ShadowRoom * scale);
        NativeMethods.MoveTopmost(hwnd, x - shadow, y - shadow);
        frame.Opacity = 0;
        frame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.FadeIn));
    }

    /// <summary>Moved to a monitor with another scale (or the scale changed): one image pixel stays one screen pixel.</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (!placed) return;
        scale = newDpi.DpiScaleX;
        ApplyZoom();
    }

    // 100 % = one image pixel per screen pixel, as captured.
    void ApplyZoom()
    {
        view.Width = image.PixelWidth * zoom / scale;
        view.Height = image.PixelHeight * zoom / scale;
    }

    // With the pointer, the point under it stays put; otherwise the top-left corner does.
    void ZoomTo(double target, bool atPointer)
    {
        target = ClampZoom(target);
        NativeMethods.GetWindowRect(hwnd, out RECT window);
        int shadow = (int)Math.Round(ShadowRoom * scale);
        double imageLeft = window.left + shadow, imageTop = window.top + shadow, anchorX = imageLeft, anchorY = imageTop;
        if (atPointer)
        {
            NativeMethods.GetCursorPos(out var pointer);
            anchorX = pointer.x;
            anchorY = pointer.y;
        }
        double factor = target / zoom;
        zoom = target;
        ApplyZoom();
        UpdateLayout();
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, (int)Math.Round(anchorX - (anchorX - imageLeft) * factor) - shadow,
            (int)Math.Round(anchorY - (anchorY - imageTop) * factor) - shadow, 0, 0, MoveFlags);
        ShowFeedback(UiText.Pin.Zoom(Math.Round(zoom * 100)));
    }

    /// <summary>Limits the zoom to 10 %..500 % and snaps to 100 % when close.</summary>
    internal static double ClampZoom(double zoom)
    {
        zoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));
        return Math.Abs(zoom - 1) < 0.05 ? 1 : zoom;
    }

    /// <summary>Opacity in steps of 10 %, at least 20 %.</summary>
    internal static double StepOpacity(double opacity, double step) => Math.Max(0.2, Math.Min(1, Math.Round((opacity + step) * 10) / 10));

    void ChangeOpacity(double step)
    {
        frame.Opacity = StepOpacity(frame.Opacity, step);
        ShowFeedback(UiText.Pin.Opacity(Math.Round(frame.Opacity * 100)));
    }

    void ShowFeedback(string text)
    {
        chipText.Text = text;
        chip.Visibility = Visibility.Visible;
        chipTimer.Stop();
        chipTimer.Start();
    }

    void Copy() => CopyAsync().OnFailure("Copying a pinned screenshot", () => ShowFeedback(UiText.Common.CopyFailed));

    async Task CopyAsync()
    {
        bool copied = await ClipboardWriter.SetImageAsync(image, Screenshots.Encode(image, false));
        ShowFeedback(copied ? UiText.Pin.Copied : UiText.Common.CopyFailed);
    }

    void SaveAs()
    {
        var result = Screenshots.SaveAs(this, image, fileName, out _);
        if (result == SaveAsResult.Saved) ShowFeedback(UiText.Pin.Saved);
        else if (result == SaveAsResult.Failed) ShowFeedback(UiText.Common.CouldNotSave);
    }
}
