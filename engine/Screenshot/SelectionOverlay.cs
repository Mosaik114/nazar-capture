namespace MosaikCapture.Engine.Screenshot;

/// <summary>
/// Full-screen window across all monitors showing the frozen screen, for picking a screenshot or
/// recording area: drag = area, click = window below, Space = whole monitor, C = copy color.
/// It is not a transparent window (too sluggish at 5120 x 1440 pixels); instead it is only shown
/// once it has been rendered (DWM cloak), then only the dimming fades in.
/// Without a mouse: the arrow keys move the pointer (1 px, 10 px with Shift), Enter sets the start and then the end.
/// The scale is read anew at every opening and follows a DPI change while open; all positions are device pixels.
/// </summary>
internal sealed class SelectionOverlay : Window
{
    const double LoupeSize = 130;
    const int LoupePixels = 13;              // pixels per row in the loupe
    const double LoupeCell = LoupeSize / LoupePixels;
    const double DragThreshold = 4;          // movement in device pixels that turns a click into a drag
    const double MinimumAreaSize = 3;

    readonly Action<ScreenCapture>? captured;
    readonly IntPtr hwnd;
    double scale = 1;                        // device pixels per WPF unit; read at every opening, follows DPI changes
    readonly Canvas root, decorations, loupe;
    readonly Image frozenScreen;
    readonly Shapes.Path dimming;
    readonly Shapes.Rectangle frame, loupeCenter, swatch;
    readonly Shapes.Line horizontalGuide, verticalGuide;
    readonly FloatingLabel sizeChip, colorChip, hint;
    readonly TextBlock sizeText, colorText, positionText;
    readonly StackPanel hintRow = new() { Orientation = Orientation.Horizontal };
    readonly List<Rect> windows = new();     // visible windows from top to bottom, relative to the virtual screen
    readonly List<RECT> monitors = new(), workAreas = new();
    readonly EngineWork shown = new();       // counts as busy while open: the engine must not quit under the user

    BitmapSource? screen;
    readonly byte[] pixel = new byte[4];                       // the pixel under the pointer, reused for every move
    readonly SolidColorBrush swatchBrush = new();              // its colour, reused
    readonly ImageBrush loupeBrush = new() { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
    readonly RectangleGeometry everythingArea = new(), holeArea = new();
    readonly GeometryGroup holes = new() { FillRule = FillRule.EvenOdd };
    Int32Rect virtualScreen;                 // in device pixels
    Point pressedAt;
    bool pressed, dragging;
    Point? keyboardPointer;                  // the pointer moved with the arrow keys (device pixels), null while the mouse leads
    bool keyboardDrag;                       // Enter set a start with the keyboard
    Rect selection = Rect.Empty;
    string color = "";
    bool forRecording;                       // picking an area for a screen recording instead of a screenshot

    static readonly SolidColorBrush GuideBrush = UiTokens.Fixed.GuideBrush;   // bright turquoise: drawn over the dimmed screen
    static readonly DoubleCollection GuideDashes = Freeze(new DoubleCollection(new double[] { 4, 4 }));

    static DoubleCollection Freeze(DoubleCollection dashes)
    {
        dashes.Freeze();
        return dashes;
    }

    public SelectionOverlay(Action<ScreenCapture>? onCaptured)
    {
        captured = onCaptured;
        ConfigureWindow();

        root = new Canvas();
        frozenScreen = new Image { Stretch = Stretch.Fill };
        decorations = new Canvas { IsHitTestVisible = false };
        dimming = new Shapes.Path { Fill = Palette.Dimming };
        horizontalGuide = BuildGuide();
        verticalGuide = BuildGuide();
        frame = new Shapes.Rectangle { StrokeThickness = 2 };
        frame.SetResourceReference(Shapes.Shape.StrokeProperty, UiTokens.Keys.AccentTint);
        sizeChip = FloatingLabel.ForText(out sizeText, shadow: false);
        sizeText.FontWeight = FontWeights.SemiBold;
        loupeCenter = new Shapes.Rectangle { Width = LoupeCell + 2, Height = LoupeCell + 2, StrokeThickness = 2 };
        loupe = BuildLoupe();
        swatch = new Shapes.Rectangle { Width = 11, Height = 11, RadiusX = 3, RadiusY = 3, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        colorText = TextStyle.Text("", TextRole.Description);
        colorText.FontWeight = FontWeights.SemiBold;
        positionText = TextStyle.Text("", TextRole.Description, UiTokens.Keys.TextSecondary);
        positionText.Margin = new Thickness(10, 0, 0, 0);
        colorChip = BuildColorChip();
        hint = new FloatingLabel(hintRow) { Padding = new Thickness(14, 8, 14, 8) };
        ShowHint(recording: false);
        ComposeLayers();

        hwnd = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.AddExtendedStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW);   // not in Alt+Tab
        scale = ToolWindows.Scale(hwnd);
        InputModality.Install();
        HookUpInput();
    }

    /// <summary>Recording: the selected area in device pixels (absolute), plus monitor and work area.</summary>
    public Action<Int32Rect, RECT, RECT>? Recorded { get; set; }
    public Action? Aborted { get; set; }

    /// <summary>Diagnostics (<c>--measure</c>): raised when an overlay has become visible (uncloaked).</summary>
    internal static event Action<SelectionOverlay>? Revealed;

    /// <summary>Diagnostics (<c>--measure</c>): handles a key as if it had been pressed.</summary>
    internal void Press(Key key) => OnKey(key);

    // ---------- Building ----------

    // Magnified pixels with a grid; the pixel under the pointer is outlined.
    Canvas BuildLoupe()
    {
        var container = new Canvas { Width = LoupeSize + 6, Height = LoupeSize + 6 };
        var background = StoneChrome.FloatingPlate((LoupeSize + 6) / 2);
        background.IsSquircle = false;
        background.Depth = 0;
        background.Width = background.Height = LoupeSize + 6;
        container.Children.Add(background);

        var cells = new Canvas { Width = LoupeSize, Height = LoupeSize, Clip = new EllipseGeometry(new Point(LoupeSize / 2, LoupeSize / 2), LoupeSize / 2, LoupeSize / 2) };
        holes.Children.Add(everythingArea);
        holes.Children.Add(holeArea);
        RenderOptions.SetBitmapScalingMode(loupeBrush, BitmapScalingMode.NearestNeighbor);
        var magnified = new Shapes.Rectangle { Width = LoupeSize, Height = LoupeSize, Fill = loupeBrush };
        cells.Children.Add(magnified);
        var grid = UiTokens.Fixed.LoupeGridBrush;
        for (int i = 1; i < LoupePixels; i++)
        {
            cells.Children.Add(new Shapes.Line { X1 = i * LoupeCell, X2 = i * LoupeCell, Y1 = 0, Y2 = LoupeSize, Stroke = grid, StrokeThickness = 1 });
            cells.Children.Add(new Shapes.Line { Y1 = i * LoupeCell, Y2 = i * LoupeCell, X1 = 0, X2 = LoupeSize, Stroke = grid, StrokeThickness = 1 });
        }
        loupeCenter.SetResourceReference(Shapes.Shape.StrokeProperty, UiTokens.Keys.AccentTint);
        cells.Children.Add(loupeCenter);
        Canvas.SetLeft(cells, 3);
        Canvas.SetTop(cells, 3);
        container.Children.Add(cells);

        var rim = new Shapes.Ellipse { Width = LoupeSize + 2, Height = LoupeSize + 2, StrokeThickness = 1 };
        rim.SetResourceReference(Shapes.Shape.StrokeProperty, UiTokens.Keys.Hairline);
        Canvas.SetLeft(rim, 2);
        Canvas.SetTop(rim, 2);
        container.Children.Add(rim);
        return container;
    }

    // Color under the pointer (swatch and hex) and the screen position.
    FloatingLabel BuildColorChip()
    {
        swatch.SetResourceReference(Shapes.Shape.StrokeProperty, UiTokens.Keys.Ink);
        swatch.Fill = swatchBrush;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(swatch);
        row.Children.Add(colorText);
        row.Children.Add(positionText);
        return new FloatingLabel(row);
    }

    /// <summary>The hint at the bottom: what mouse and keys do, the keys as key caps.</summary>
    void ShowHint(bool recording)
    {
        hintRow.Children.Clear();
        foreach (var (keys, text) in recording ? UiText.Overlay.RecordingHints : UiText.Overlay.ScreenshotHints)
        {
            if (hintRow.Children.Count > 0)
            {
                var dot = TextStyle.Text("·", TextRole.Description, UiTokens.Keys.TextTertiary);
                dot.Margin = new Thickness(UiTokens.Space.Md + UiTokens.Space.Xs, 0, UiTokens.Space.Md + UiTokens.Space.Xs, 0);
                dot.VerticalAlignment = VerticalAlignment.Center;
                hintRow.Children.Add(dot);
            }
            if (keys.Length > 0) hintRow.Children.Add(new KeyCombo(keys) { Margin = new Thickness(0, 0, UiTokens.Space.Sm + UiTokens.Space.Xs, 0) });
            var label = TextStyle.Text(text, TextRole.Description, UiTokens.Keys.TextSecondary);
            label.VerticalAlignment = VerticalAlignment.Center;
            hintRow.Children.Add(label);
        }
        System.Windows.Automation.AutomationProperties.SetName(hint, recording ? UiText.Overlay.RecordingHint : UiText.Overlay.ScreenshotHint);
    }

    void HookUpInput()
    {
        MouseMove += (_, e) => { keyboardPointer = null; PointerMoved(DevicePosition(e)); };
        MouseLeftButtonDown += (_, e) => { keyboardPointer = null; keyboardDrag = false; pressedAt = DevicePosition(e); pressed = true; dragging = false; CaptureMouse(); };
        MouseLeftButtonUp += (_, e) => Released(DevicePosition(e));
        MouseRightButtonDown += (_, e) =>
        {
            if (!dragging) { Cancel(); return; }
            pressed = dragging = false;   // discard the drag
            ReleaseMouseCapture();
            PointerMoved(DevicePosition(e));
        };
        PreviewKeyDown += (_, e) => { OnKey(e.Key); e.Handled = true; };
        // Another window took the focus (a notification, a program starting): while a drag is running the overlay stays
        // and takes the focus back on release; otherwise it cancels, as before.
        Deactivated += delegate { if (IsVisible && !pressed && !keyboardDrag) Cancel(); };
    }

    // Pointer position in whole device pixels relative to the virtual screen.
    Point DevicePosition(MouseEventArgs e)
    {
        Point position = e.GetPosition(root);
        return new Point(Math.Floor(position.X * scale), Math.Floor(position.Y * scale));
    }

    Point CursorPosition()
    {
        if (keyboardPointer is { } moved) return moved;
        NativeMethods.GetCursorPos(out var cursor);
        return new Point(cursor.x - virtualScreen.X, cursor.y - virtualScreen.Y);
    }

    /// <summary>Device pixels per WPF unit while open (tests).</summary>
    internal double Scale => scale;

    /// <summary>
    /// A new scale (DPI change while open, or a test): every WPF size is recomputed from the device pixels, so the
    /// frozen screen still lies pixel-exactly under the selection and chips and loupe keep their size on screen.
    /// </summary>
    internal void UseScale(double newScale)
    {
        if (newScale <= 0 || Math.Abs(newScale - scale) < 1e-9) return;
        scale = newScale;
        frozenScreen.Width = root.Width = virtualScreen.Width / scale;
        frozenScreen.Height = root.Height = virtualScreen.Height / scale;
        if (IsVisible) { Width = virtualScreen.Width / scale; Height = virtualScreen.Height / scale; }
        if (screen == null) return;
        Point cursor = CursorPosition();
        PlaceHint(MonitorAt(cursor, monitors));
        PointerMoved(cursor);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UseScale(newDpi.DpiScaleX);
    }

    // ---------- Opening ----------

    void ConfigureWindow()
    {
        Title = UiText.Titles.Overlay;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = UiTokens.Fixed.ScreenBackdropBrush;
        Cursor = Cursors.Cross;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Palette.Apply(Resources);
    }

    /// <summary>Dashed accent line through the pointer.</summary>
    static Shapes.Line BuildGuide() => new() { Stroke = GuideBrush, StrokeThickness = 1, StrokeDashArray = GuideDashes };

    // The frozen screen at the bottom, everything else above it and transparent to the mouse.
    void ComposeLayers()
    {
        root.Children.Add(frozenScreen);
        root.Children.Add(decorations);
        foreach (var element in new UIElement[] { dimming, horizontalGuide, verticalGuide, frame, sizeChip, loupe, colorChip, hint })
            decorations.Children.Add(element);
        Content = root;
    }

    /// <summary>Shows the window once, cloaked and tiny, so its first real opening finds everything rendered.</summary>
    internal void Prepare()
    {
        if (IsVisible) return;
        bool activated = ShowActivated;
        ShowActivated = false;   // never take the focus from what the user is doing
        NativeMethods.Cloak(hwnd, true);
        Width = Height = 1;
        Show();
        UpdateLayout();
        Hide();
        ShowActivated = activated;
    }

    public void Open() => Begin(recording: false);

    public void OpenForRecording() => Begin(recording: true);

    void Begin(bool recording)
    {
        if (IsVisible) return;
        forRecording = recording;
        ShowHint(recording);
        Palette.Apply(Resources);
        virtualScreen = ScreenGrabber.VirtualScreen();
        // The window will take the scale of the monitor holding most of the virtual screen; read it at every opening
        // (it may have changed since the last one), OnDpiChanged corrects it should Windows decide otherwise.
        scale = NativeMethods.ScaleOfMonitor(new RECT { left = virtualScreen.X, top = virtualScreen.Y, right = virtualScreen.X + virtualScreen.Width, bottom = virtualScreen.Y + virtualScreen.Height });
        keyboardPointer = null;
        keyboardDrag = false;
        screen = ScreenGrabber.Capture(virtualScreen.X, virtualScreen.Y, virtualScreen.Width, virtualScreen.Height, 96 * scale);   // before showing anything so none of our windows are captured
        ScreenGrabber.VisibleWindows(virtualScreen, windows);
        ScreenGrabber.Monitors(monitors, workAreas);

        frozenScreen.Source = loupeBrush.ImageSource = screen;
        frozenScreen.Width = root.Width = virtualScreen.Width / scale;
        frozenScreen.Height = root.Height = virtualScreen.Height / scale;
        dimming.Data = null;
        frame.Visibility = sizeChip.Visibility = Visibility.Hidden;
        pressed = dragging = false;
        selection = Rect.Empty;
        decorations.Opacity = 0;

        Point cursor = CursorPosition();
        PlaceHint(MonitorAt(cursor, monitors));
        PointerMoved(cursor);
        RevealWhenRendered();
    }

    // Centered near the bottom of the monitor with the pointer.
    void PlaceHint(RECT monitor)
    {
        hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(hint, ((monitor.left + monitor.right) / 2.0 - virtualScreen.X) / scale - hint.DesiredSize.Width / 2);
        Canvas.SetTop(hint, (monitor.bottom - virtualScreen.Y) / scale - 90);
    }

    // Show cloaked and let it render, then uncloak: no black flash.
    void RevealWhenRendered()
    {
        shown.Begin();
        NativeMethods.Cloak(hwnd, true);
        Width = virtualScreen.Width / scale;
        Height = virtualScreen.Height / scale;
        Show();
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, virtualScreen.X, virtualScreen.Y, virtualScreen.Width, virtualScreen.Height, NativeMethods.SWP_SHOWWINDOW);
        ToolWindowBase.TakeFocus(this, hwnd);
        var reveal = new DispatcherTimer(DispatcherPriority.Render) { Interval = Motion.RevealDelay };
        reveal.Tick += delegate
        {
            reveal.Stop();
            if (!IsVisible) return;
            NativeMethods.Cloak(hwnd, false);
            Revealed?.Invoke(this);
            decorations.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.FadeIn)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        };
        reveal.Start();
    }

    // ---------- Pointer ----------

    // Highlights the window below or shows the dragged area, and moves the loupe.
    void PointerMoved(Point position)
    {
        if (screen == null) return;
        if (pressed && !dragging && (Math.Abs(position.X - pressedAt.X) > DragThreshold || Math.Abs(position.Y - pressedAt.Y) > DragThreshold)) dragging = true;
        selection = dragging ? DraggedArea(pressedAt, position, (Keyboard.Modifiers & ModifierKeys.Shift) != 0) : WindowAt(position);
        ShowSelection(selection);

        horizontalGuide.X1 = 0;
        horizontalGuide.X2 = virtualScreen.Width / scale;
        horizontalGuide.Y1 = horizontalGuide.Y2 = (position.Y + 0.5) / scale;
        verticalGuide.Y1 = 0;
        verticalGuide.Y2 = virtualScreen.Height / scale;
        verticalGuide.X1 = verticalGuide.X2 = (position.X + 0.5) / scale;
        horizontalGuide.Visibility = verticalGuide.Visibility = selection.IsEmpty || dragging ? Visibility.Visible : Visibility.Hidden;
        UpdateLoupe(position);
    }

    /// <summary>The area between the press and the pointer, both pixels included; a square with Shift.</summary>
    internal static Rect DraggedArea(Point start, Point end, bool square)
    {
        double width = Math.Abs(end.X - start.X) + 1, height = Math.Abs(end.Y - start.Y) + 1;
        if (square) width = height = Math.Max(width, height);
        return new Rect(end.X < start.X ? start.X - width + 1 : start.X, end.Y < start.Y ? start.Y - height + 1 : start.Y, width, height);
    }

    // The topmost visible window under the point, or Rect.Empty.
    Rect WindowAt(Point position)
    {
        foreach (Rect window in windows)
            if (window.Contains(position)) return window;
        return Rect.Empty;
    }

    // Dimming with a hole, frame and size chip for an area (device pixels).
    void ShowSelection(Rect area)
    {
        // The geometries are made once and only get new rectangles: no garbage for every mouse move.
        everythingArea.Rect = new Rect(0, 0, virtualScreen.Width / scale, virtualScreen.Height / scale);
        if (area.IsEmpty)
        {
            holeArea.Rect = Rect.Empty;
            dimming.Data = holes;
            frame.Visibility = sizeChip.Visibility = Visibility.Hidden;
            return;
        }
        var shown = new Rect(area.X / scale, area.Y / scale, area.Width / scale, area.Height / scale);
        holeArea.Rect = shown;
        dimming.Data = holes;
        frame.Width = shown.Width + 2;
        frame.Height = shown.Height + 2;
        Canvas.SetLeft(frame, shown.X - 1);
        Canvas.SetTop(frame, shown.Y - 1);
        sizeText.Text = UiText.Common.Size((int)area.Width, (int)area.Height);
        Canvas.SetLeft(sizeChip, shown.X);
        Canvas.SetTop(sizeChip, shown.Y >= 36 ? shown.Y - 34 : shown.Y + 8);   // at the top edge: inside
        frame.Visibility = sizeChip.Visibility = Visibility.Visible;
    }

    void UpdateLoupe(Point position)
    {
        var image = screen!;
        int x = (int)position.X, y = (int)position.Y, half = LoupePixels / 2;
        int left = Math.Max(0, Math.Min(image.PixelWidth - LoupePixels, x - half));
        int top = Math.Max(0, Math.Min(image.PixelHeight - LoupePixels, y - half));
        loupeBrush.Viewbox = new Rect(left, top, LoupePixels, LoupePixels);   // one brush on the frozen screen, no new bitmap per move
        Canvas.SetLeft(loupeCenter, (x - left) * LoupeCell - 1);
        Canvas.SetTop(loupeCenter, (y - top) * LoupeCell - 1);

        image.CopyPixels(new Int32Rect(Math.Min(x, image.PixelWidth - 1), Math.Min(y, image.PixelHeight - 1), 1, 1), pixel, 4, 0);
        color = HexColor(pixel[2], pixel[1], pixel[0]);
        swatchBrush.Color = Color.FromRgb(pixel[2], pixel[1], pixel[0]);
        colorText.Text = color;
        positionText.Text = (x + virtualScreen.X) + ", " + (y + virtualScreen.Y);
        PlaceLoupe(position);
    }

    internal static string HexColor(byte red, byte green, byte blue) =>
        "#" + red.ToString("X2", CultureInfo.InvariantCulture) + green.ToString("X2", CultureInfo.InvariantCulture) + blue.ToString("X2", CultureInfo.InvariantCulture);

    // Below right of the pointer, flipped at the monitor edge.
    void PlaceLoupe(Point position)
    {
        RECT monitor = MonitorAt(position, monitors);
        colorChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double left = position.X / scale + 24, top = position.Y / scale + 24, totalHeight = LoupeSize + 12 + colorChip.DesiredSize.Height;
        if (left + LoupeSize + 6 > (monitor.right - virtualScreen.X) / scale) left = position.X / scale - 24 - LoupeSize - 6;
        if (top + totalHeight > (monitor.bottom - virtualScreen.Y) / scale) top = position.Y / scale - 24 - totalHeight;
        Canvas.SetLeft(loupe, left);
        Canvas.SetTop(loupe, top);
        Canvas.SetLeft(colorChip, left + (LoupeSize + 6 - colorChip.DesiredSize.Width) / 2);
        Canvas.SetTop(colorChip, top + LoupeSize + 12);
    }

    void Released(Point position)
    {
        if (!pressed) return;
        pressed = false;
        ReleaseMouseCapture();
        if (!IsActive && IsVisible) ToolWindowBase.TakeFocus(this, hwnd);   // a window had taken the focus during the drag
        if (dragging)
        {
            dragging = false;
            if (selection.Width >= MinimumAreaSize && selection.Height >= MinimumAreaSize) Finish(selection);
            return;
        }
        // plain click: the window below, otherwise the whole monitor
        Rect window = WindowAt(position);
        Finish(window.IsEmpty ? MonitorArea(MonitorAt(position, monitors)) : window);
    }

    void OnKey(Key key)
    {
        Point cursor = CursorPosition();
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (key)
        {
            case Key.Escape:
                Cancel();
                break;
            case Key.Space:
                Finish(MonitorArea(MonitorAt(cursor, monitors)));
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                MoveKeyboardPointer(cursor, key, shift ? 10 : 1);
                break;
            case Key.Enter when keyboardDrag:
                keyboardDrag = pressed = dragging = false;   // the second Enter: the end
                if (selection.Width >= 1 && selection.Height >= 1) Finish(selection);
                break;
            case Key.Enter when keyboardPointer != null:
                pressedAt = cursor;                        // the first Enter after moving with the keys: the start
                keyboardDrag = pressed = dragging = true;
                PointerMoved(cursor);
                break;
            case Key.Enter when !selection.IsEmpty && !dragging:
                Finish(selection);
                break;
            case Key.C when !forRecording:
                CopyColor(color);
                break;
            case Key.LeftShift:
            case Key.RightShift:
                PointerMoved(cursor);   // switch between rectangle and square right away
                break;
        }
    }

    /// <summary>Moves the keyboard pointer by <paramref name="step"/> device pixels, kept on the virtual screen.</summary>
    void MoveKeyboardPointer(Point from, Key key, int step)
    {
        double x = from.X + (key == Key.Left ? -step : key == Key.Right ? step : 0);
        double y = from.Y + (key == Key.Up ? -step : key == Key.Down ? step : 0);
        var moved = new Point(Math.Max(0, Math.Min(virtualScreen.Width - 1, x)), Math.Max(0, Math.Min(virtualScreen.Height - 1, y)));
        keyboardPointer = moved;
        PointerMoved(moved);
    }

    /// <summary>Tests: where the pointer is now (device pixels relative to the virtual screen) and the selection.</summary>
    internal Point Pointer => CursorPosition();

    internal Rect Selection => selection;

    /// <summary>Tests: opens the overlay over a given screen image instead of the real screen (never shown).</summary>
    internal void OpenOffscreen(BitmapSource image, Int32Rect virtualArea, IEnumerable<RECT> monitorRects, double atScale, bool recording = false)
    {
        forRecording = recording;
        virtualScreen = virtualArea;
        screen = image;
        monitors.Clear();
        workAreas.Clear();
        foreach (var monitor in monitorRects) { monitors.Add(monitor); workAreas.Add(monitor); }
        windows.Clear();
        scale = atScale;
        frozenScreen.Source = loupeBrush.ImageSource = screen;
        frozenScreen.Width = root.Width = virtualScreen.Width / scale;
        frozenScreen.Height = root.Height = virtualScreen.Height / scale;
        keyboardPointer = new Point(virtualScreen.Width / 2, virtualScreen.Height / 2);
        keyboardDrag = pressed = dragging = false;
        PlaceHint(MonitorAt(keyboardPointer.Value, monitors));
        PointerMoved(keyboardPointer.Value);
    }

    /// <summary>Tests: where the frame of the selection lies (WPF units on the overlay).</summary>
    internal Rect FrameBounds => new(Canvas.GetLeft(frame), Canvas.GetTop(frame), frame.Width, frame.Height);

    /// <summary>The monitor that contains the point (relative to the virtual screen), as an absolute rectangle.</summary>
    internal RECT MonitorAt(Point position) => MonitorAt(position, monitors);

    // ---------- Result ----------

    Rect MonitorArea(RECT monitor) => new(monitor.left - virtualScreen.X, monitor.top - virtualScreen.Y, monitor.Width, monitor.Height);

    void Finish(Rect area)
    {
        area.Intersect(new Rect(0, 0, virtualScreen.Width, virtualScreen.Height));
        if (area.IsEmpty || area.Width < 1 || area.Height < 1) { Cancel(); return; }
        var crop = new Int32Rect((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height);
        var center = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);   // the card goes on the monitor with the area
        RECT monitor = MonitorAt(center, monitors), workArea = MonitorAt(center, workAreas);
        if (forRecording)
        {
            Dismiss();
            Recorded?.Invoke(new Int32Rect(crop.X + virtualScreen.X, crop.Y + virtualScreen.Y, crop.Width, crop.Height), monitor, workArea);
            return;
        }
        var image = Screenshots.CopyOf(screen!, crop);
        var capture = new ScreenCapture
        {
            FullScreen = screen!,
            Image = image,
            Crop = crop,
            Monitor = monitor,
            WorkArea = workArea,
            VirtualLeft = virtualScreen.X,
            VirtualTop = virtualScreen.Y,
            VirtualWidth = virtualScreen.Width,
            VirtualHeight = virtualScreen.Height,
            Scale = scale
        };
        Dismiss();
        captured!(capture);
    }

    void CopyColor(string color) => CopyColorAsync(color).OnFailure("Copying a color", () => { if (IsVisible) colorText.Text = UiText.Common.CopyFailed; });

    async Task CopyColorAsync(string color)
    {
        bool copied = await ClipboardWriter.SetTextAsync(color);
        if (IsVisible) colorText.Text = copied ? color + UiText.Overlay.ColorCopiedSuffix : UiText.Common.CopyFailed;
    }

    /// <summary>Closes the overlay; while picking a recording area this reports "aborted".</summary>
    internal void Cancel()
    {
        Dismiss();
        if (forRecording) Aborted?.Invoke();
    }

    void Dismiss()
    {
        shown.End();
        NativeMethods.Cloak(hwnd, true);
        pressed = dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Hide();
        frozenScreen.Source = loupeBrush.ImageSource = null;
        screen = null;   // release the large image
        // A hidden window keeps the render buffers of its last size (the whole desktop, tens of MB each): shrink it.
        frozenScreen.Width = frozenScreen.Height = root.Width = root.Height = Width = Height = 1;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, virtualScreen.X, virtualScreen.Y, 1, 1, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        MemoryRelief.Soon();
    }

    // The entry of `list` for the monitor containing the point; the first monitor if none does.
    RECT MonitorAt(Point position, List<RECT> list)
    {
        double x = position.X + virtualScreen.X, y = position.Y + virtualScreen.Y;
        for (int i = 0; i < monitors.Count; i++)
        {
            RECT monitor = monitors[i];
            if (x >= monitor.left && x < monitor.right && y >= monitor.top && y < monitor.bottom) return list[i];
        }
        return list.Count > 0
            ? list[0]
            : new RECT { left = virtualScreen.X, top = virtualScreen.Y, right = virtualScreen.X + virtualScreen.Width, bottom = virtualScreen.Y + virtualScreen.Height };
    }
}
