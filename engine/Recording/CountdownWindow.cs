namespace NazarCapture.Engine.Recording;

/// <summary>
/// 3-2-1 countdown before the recording starts. Esc cancels: the window registers Esc as a hotkey while it is shown
/// (WM_HOTKEY), so the keyboard is never polled; the hint shows Esc as a key cap.
/// </summary>
internal sealed class CountdownWindow : ToolWindowBase
{
    const double CircleSize = 132, RingWidth = 3;
    const double LaunchLead = 0.25;   // seconds before zero at which ffmpeg is started
    readonly TextBlock number;
    readonly Shapes.Path arc;
    readonly ScaleTransform pop = new(1, 1);
    readonly DispatcherTimer timer;
    Action? launch, finished, cancelled;
    WindowHotkey? escape;
    long startTimestamp;
    int shownNumber;
    bool launched;
    int duration = 3;

    public CountdownWindow() : base(UiText.Titles.Countdown)
    {
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        number = TextStyle.Text("", TextRole.WindowTitle);
        number.FontSize = 60;
        number.HorizontalAlignment = HorizontalAlignment.Center;
        number.VerticalAlignment = VerticalAlignment.Center;
        number.Margin = new Thickness(0, 0, 0, 6);
        number.RenderTransform = pop;
        number.RenderTransformOrigin = new Point(0.5, 0.5);
        arc = new Shapes.Path { StrokeThickness = RingWidth, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        arc.SetResourceReference(Shapes.Shape.StrokeProperty, UiTokens.Keys.AccentTint);

        var layout = new StackPanel { Margin = new Thickness(Elevation.Margin) };
        layout.Children.Add(BuildCircle());
        layout.Children.Add(BuildHint());
        Content = layout;

        CreateHandle(NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT);
        ToolWindows.ExcludeFromCapture(Hwnd);
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = Motion.CountdownFrame };
        timer.Tick += delegate { OnTick(); };
    }

    Grid BuildCircle()
    {
        var circle = new Grid { Width = CircleSize, Height = CircleSize };
        var plate = StoneChrome.FloatingPlate(CircleSize / 2);
        plate.IsSquircle = false;
        plate.Depth = 0;
        circle.Children.Add(plate);
        var rail = new Shapes.Ellipse { Margin = new Thickness(10 - RingWidth / 2), StrokeThickness = RingWidth };
        rail.SetResourceReference(Shapes.Shape.StrokeProperty, UiTokens.Keys.Hairline);
        circle.Children.Add(rail);
        circle.Children.Add(arc);
        circle.Children.Add(number);
        return circle;
    }

    static FloatingLabel BuildHint()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new KeyCombo("Esc") { Margin = new Thickness(0, 0, UiTokens.Space.Sm + UiTokens.Space.Xs, 0) });
        var text = TextStyle.Text(UiText.Recording.CountdownCancels, TextRole.Description, UiTokens.Keys.TextSecondary);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return new FloatingLabel(row) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
    }

    /// <summary>
    /// Counts down the configured seconds centered on <paramref name="area"/>. With a countdown of 0
    /// the callbacks run right away and nothing is shown.
    /// </summary>
    public void Run(Int32Rect area, RECT monitor, int seconds, Action onLaunch, Action onFinished, Action onCancelled)
    {
        launch = onLaunch;
        finished = onFinished;
        cancelled = onCancelled;
        launched = false;
        shownNumber = 0;
        Palette.Apply(Resources);
        duration = seconds;
        if (duration == 0) { onLaunch(); onFinished(); return; }
        ShowFrame(duration, 0);
        PlaceWith(device => PlaceOver(area, monitor, device), NativeMethods.ScaleOfMonitor(monitor));
        escape ??= WindowHotkey.Register(Hwnd, 0, NativeMethods.VK_ESCAPE, Cancel);
        startTimestamp = Stopwatch.GetTimestamp();
        timer.Start();
    }

    // Centered on the area, kept on the monitor (the shadow room may hang over the edge).
    (int X, int Y)? PlaceOver(Int32Rect area, RECT monitor, Size device)
    {
        double scale = ActualWidth > 0 ? device.Width / ActualWidth : 1;
        int shadow = (int)Math.Round(Elevation.Margin * scale), circle = (int)Math.Round(CircleSize * scale);
        int width = (int)device.Width, height = (int)device.Height;
        int centerX = area.X + area.Width / 2, centerY = area.Y + area.Height / 2;
        int x = Math.Max(monitor.left - shadow, Math.Min(monitor.right - width + shadow, centerX - width / 2));
        int y = Math.Max(monitor.top - shadow, Math.Min(monitor.bottom - height + shadow, centerY - shadow - circle / 2));
        return (x, y);
    }

    public void Stop()
    {
        timer.Stop();
        escape?.Dispose();
        escape = null;
        Hide();
    }

    /// <summary>Esc while counting: stop and report the cancellation.</summary>
    internal void Cancel()
    {
        if (!timer.IsEnabled) return;
        Stop();
        cancelled!();
    }

    void OnTick()
    {
        double elapsed = (Stopwatch.GetTimestamp() - startTimestamp) / (double)Stopwatch.Frequency;
        if (!launched && elapsed >= duration - LaunchLead) { launched = true; launch!(); }
        if (elapsed >= duration) { Stop(); finished!(); return; }
        ShowFrame(duration - (int)elapsed, 1 - (elapsed - Math.Floor(elapsed)));
    }

    /// <summary>The number (with a small pop on change) and an arc that runs down once per second.</summary>
    internal void ShowFrame(int secondsLeft, double fractionOfSecondLeft)
    {
        if (secondsLeft != shownNumber)
        {
            shownNumber = secondsLeft;
            number.Text = secondsLeft.ToString(CultureInfo.InvariantCulture);
            var pulse = Motion.CountdownPulse;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.28, 1, pulse) { EasingFunction = ease });
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.28, 1, pulse) { EasingFunction = ease });
            number.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, pulse) { EasingFunction = ease });
        }
        double radius = CircleSize / 2 - 10, center = CircleSize / 2;
        if (fractionOfSecondLeft >= 0.999) { arc.Data = new EllipseGeometry(new Point(center, center), radius, radius); return; }
        if (fractionOfSecondLeft <= 0.001) { arc.Data = null; return; }
        double angle = fractionOfSecondLeft * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Point(center, center - radius) };
        figure.Segments.Add(new ArcSegment(new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle)),
            new Size(radius, radius), 0, fractionOfSecondLeft > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        arc.Data = geometry;
    }
}
