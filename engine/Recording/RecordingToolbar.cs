using System.Windows.Controls.Primitives;

namespace NazarCapture.Engine.Recording;

/// <summary>
/// Toolbar shown while recording: time, microphone, system sound, click highlights, pause, stop and discard. Built from
/// the kit: a floating plate with plain stones, the stop button in the accent, discard asks with words ("Wirklich
/// verwerfen?") in the recording red. It can be dragged by its grip, is invisible in the video, and comes to the keyboard
/// with the recording hotkey plus Ctrl (Tab between the buttons, Esc gives the keyboard back).
/// </summary>
internal sealed class RecordingToolbar : ToolWindowBase
{
    const double CaptionRowHeight = 36;
    readonly StoneChrome pill;
    readonly DragHandle grip;
    readonly FloatingLabel caption;
    readonly IconButton microphoneButton, clickButton, pauseButton;
    readonly KitButton stopButton;
    readonly TextBlock captionText, microphoneGlyph, soundGlyph, clickGlyph, pauseGlyph;
    readonly Shapes.Ellipse dot;
    readonly Canvas captionRow;
    readonly ScaleTransform levelScale = new(0, 1);
    /// <summary>Time and microphone level refresh.</summary>
    static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(80);
    /// <summary>How long the discard button waits for the confirming second click.</summary>
    static readonly TimeSpan DiscardConfirmTime = TimeSpan.FromSeconds(3);

    readonly DispatcherTimer refreshTimer = new() { Interval = RefreshInterval };
    readonly DispatcherTimer confirmTimer = new() { Interval = DiscardConfirmTime };
    readonly Dictionary<FrameworkElement, Func<string>> captions = new();
    int dragStartX, dragStartY, windowStartX, windowStartY;
    FrameworkElement? hovered;
    WindowHotkey? keyboardWay;
    bool? discardShown;

    public RecordingToolbar() : base(UiText.Titles.Toolbar)
    {
        SizeToContent = SizeToContent.WidthAndHeight;

        dot = new Shapes.Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        TimeText = TextStyle.Text("", TextRole.RowTitle);
        TimeText.VerticalAlignment = VerticalAlignment.Center;
        grip = BuildGrip();

        microphoneGlyph = TextStyle.Icon(IconGlyphs.Microphone, 16);   // Refresh shows the current state
        microphoneButton = AddButton(BuildMicrophoneIcon(), () => ScreenRecorder.Mic ? UiText.Toolbar.MicrophoneOn : UiText.Toolbar.MicrophoneOff,
            delegate { ScreenRecorder.SetMic(!ScreenRecorder.Mic); Refresh(); });
        soundGlyph = TextStyle.Icon(IconGlyphs.Volume, 16);
        SoundButton = AddButton(soundGlyph, () => ScreenRecorder.Sound ? UiText.Toolbar.SoundOn : UiText.Toolbar.SoundOff,
            delegate { ScreenRecorder.SetSound(!ScreenRecorder.Sound); Refresh(); });
        clickGlyph = TextStyle.Icon(IconGlyphs.Touch, 16);
        clickButton = AddButton(clickGlyph, () => ScreenRecorder.Clicks ? UiText.Toolbar.ClicksOn : UiText.Toolbar.ClicksOff,
            delegate { ScreenRecorder.SetClicks(!ScreenRecorder.Clicks); Refresh(); });
        pauseGlyph = TextStyle.Icon(IconGlyphs.Pause, 16);
        pauseButton = AddButton(pauseGlyph, () => ScreenRecorder.Paused ? UiText.Toolbar.Resume : UiText.Toolbar.Pause, ScreenRecorder.TogglePause);
        stopButton = AddButton(BuildStopSquare(), () => UiText.Toolbar.Stop, ScreenRecorder.Stop);
        Mui.SetRole(stopButton, MuiRole.Primary);
        DiscardButton = new KitButton { Height = ToolButton.Size + UiTokens.Depth.Control, MinWidth = ToolButton.Size, Margin = new Thickness(1, 0, 1, 0), Padding = new Thickness(0) };
        Mui.SetRadius(DiscardButton, UiTokens.Radius.Button);
        Hook(DiscardButton, () => ConfirmingDiscard ? UiText.Toolbar.ConfirmDiscard : UiText.Toolbar.Discard, Discard);

        pill = BuildPill();
        caption = FloatingLabel.ForText(out captionText);
        caption.Visibility = Visibility.Hidden;
        captionRow = new Canvas { Height = CaptionRowHeight };
        captionRow.Children.Add(caption);
        Content = BuildLayout();

        CreateHandle(NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        ToolWindows.ExcludeFromCapture(Hwnd);
        refreshTimer.Tick += delegate { UpdateTimeAndLevel(); };
        confirmTimer.Tick += delegate { confirmTimer.Stop(); ConfirmingDiscard = false; Refresh(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (ConfirmingDiscard) { ConfirmingDiscard = false; Refresh(); }
            else NativeMethods.SetForegroundWindow(NativeMethods.GetShellWindow());   // give the keyboard back
        };
    }

    /// <summary>The light square on the stop button.</summary>
    static Shapes.Rectangle BuildStopSquare()
    {
        var square = new Shapes.Rectangle
        {
            Width = 12,
            Height = 12,
            RadiusX = 2.5,
            RadiusY = 2.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        square.SetResourceReference(Shapes.Shape.FillProperty, UiTokens.Keys.OnAccent);
        return square;
    }

    // Exposed for the pixel tests.
    internal TextBlock TimeText { get; }
    internal IconButton SoundButton { get; }
    internal KitButton DiscardButton { get; }
    /// <summary>The discard button was clicked once and waits for the confirming second click.</summary>
    internal bool ConfirmingDiscard { get; set; }

    /// <summary>Every button of the bar (tests: all are reachable by keyboard and named).</summary>
    internal IEnumerable<ButtonBase> Buttons => new ButtonBase[] { microphoneButton, SoundButton, clickButton, pauseButton, stopButton, DiscardButton };

    // ---------- Building ----------

    // Red dot and time; drag here to move the toolbar.
    DragHandle BuildGrip()
    {
        TimeText.MinWidth = 40;
        System.Windows.Documents.Typography.SetNumeralAlignment(TimeText, FontNumeralAlignment.Tabular);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 10, 0) };
        content.Children.Add(dot);
        content.Children.Add(TimeText);
        var handle = new DragHandle(content, StartDrag, DragTo);
        captions[handle] = () => UiText.Toolbar.Move;
        handle.MouseEnter += delegate { Hover(handle); };
        handle.MouseLeave += delegate { Hover(null); };
        return handle;
    }

    // Microphone with a small level meter below it.
    Grid BuildMicrophoneIcon()
    {
        var level = new Shapes.Rectangle
        {
            Width = 14,
            Height = 2,
            RadiusX = 1,
            RadiusY = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 5),
            RenderTransform = levelScale,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        level.SetResourceReference(Shapes.Shape.FillProperty, UiTokens.Keys.AccentTint);
        var icon = new Grid { Width = 36, Height = 36 };
        icon.Children.Add(microphoneGlyph);
        icon.Children.Add(level);
        return icon;
    }

    StoneChrome BuildPill()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var element in new UIElement[] { grip, Line(), microphoneButton, SoundButton, clickButton, Line(), pauseButton, stopButton, Line(), DiscardButton })
            row.Children.Add(element);
        var plate = StoneChrome.FloatingPlate();
        plate.Padding = new Thickness(5);
        plate.Child = row;
        KeyboardNavigation.SetTabNavigation(plate, KeyboardNavigationMode.Cycle);
        return plate;
    }

    static KitSeparator Line() => new(Orientation.Vertical);

    // Button captions appear above the toolbar (inside the window, so also not in the video).
    Grid BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(Elevation.Margin, 0, Elevation.Margin, Elevation.Margin) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CaptionRowHeight) });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(captionRow);
        Grid.SetRow(pill, 1);
        root.Children.Add(pill);
        return root;
    }

    IconButton AddButton(UIElement content, Func<string> captionFor, Action click)
    {
        var button = new IconButton(IconGlyphs.Pause, captionFor(), size: ToolButton.Size) { Content = content, Margin = new Thickness(1, 0, 1, 0), ToolTip = null };
        Mui.SetRadius(button, UiTokens.Radius.Button);
        Hook(button, captionFor, click);
        return button;
    }

    void Hook(ButtonBase button, Func<string> captionFor, Action click)
    {
        captions[button] = captionFor;
        System.Windows.Automation.AutomationProperties.SetName(button, captionFor());
        button.MouseEnter += delegate { Hover(button); };
        button.MouseLeave += delegate { Hover(null); };
        button.GotKeyboardFocus += delegate { Hover(button); };
        button.LostKeyboardFocus += delegate { if (hovered == button) Hover(null); };
        button.Click += delegate { click(); if (hovered == button) Hover(button); };
    }

    // ---------- State ----------

    /// <summary>Shows the caption of a button (or the grip) above it.</summary>
    internal void Hover(FrameworkElement? element)
    {
        hovered = element;
        Refresh();
        if (element == null) { caption.Visibility = Visibility.Hidden; return; }
        captionText.Text = captions[element]();
        caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Point anchor = element.TranslatePoint(new Point(element.ActualWidth / 2, 0), captionRow);
        double width = caption.DesiredSize.Width, maxLeft = captionRow.ActualWidth - width;
        Canvas.SetLeft(caption, Math.Round(Math.Max(0, Math.Min(maxLeft, anchor.X - width / 2))));
        Canvas.SetTop(caption, CaptionRowHeight - caption.DesiredSize.Height - 6);
        caption.Visibility = Visibility.Visible;
    }

    public void Present(Int32Rect area, RECT work)
    {
        Palette.Apply(Resources);
        ConfirmingDiscard = false;
        hovered = null;
        caption.Visibility = Visibility.Hidden;
        Refresh();
        UpdateTimeAndLevel();
        PlaceWith(device => PlaceNear(area, work, device), NativeMethods.ScaleOfMonitor(work));
        refreshTimer.Start();
        if (keyboardWay == null && KeyboardChord(CaptureSettings.Load().RecordingHotkey) is { } chord)
            keyboardWay = WindowHotkey.Register(Hwnd, chord.Modifiers, chord.Key, TakeKeyboard);
    }

    /// <summary>The bar gets the keyboard: activated, the pause button focused with the visible edge.</summary>
    internal void TakeKeyboard()
    {
        InputModality.NoteKey();
        TakeFocus(this, Hwnd);
        pauseButton.Focus();
    }

    // Below the area, else above it, else inside along its bottom edge (full screen).
    (int X, int Y)? PlaceNear(Int32Rect area, RECT work, Size device)
    {
        double scale = ActualWidth > 0 ? device.Width / ActualWidth : 1;
        int pillWidth = (int)Math.Round(pill.ActualWidth * scale), pillHeight = (int)Math.Round(pill.ActualHeight * scale);
        int gap = (int)Math.Round(14 * scale), margin = (int)Math.Round(10 * scale);
        int x = area.X + (area.Width - pillWidth) / 2, y = area.Y + area.Height + gap;
        if (y + pillHeight > work.bottom - margin)
        {
            y = area.Y - gap - pillHeight;
            if (y < work.top + margin) y = Math.Min(area.Y + area.Height, work.bottom) - (int)Math.Round(24 * scale) - pillHeight;
        }
        x = Math.Max(work.left + margin, Math.Min(work.right - margin - pillWidth, x));
        return (x - (int)Math.Round(Elevation.Margin * scale), y - (int)Math.Round(CaptionRowHeight * scale));
    }

    public new void Hide()
    {
        refreshTimer.Stop();
        confirmTimer.Stop();
        keyboardWay?.Dispose();
        keyboardWay = null;
        dot.BeginAnimation(OpacityProperty, null);
        base.Hide();
    }

    /// <summary>Updates icons and highlights from the recorder's state.</summary>
    public void Refresh()
    {
        bool paused = ScreenRecorder.Paused;
        microphoneGlyph.Text = ScreenRecorder.Mic ? IconGlyphs.Microphone : IconGlyphs.MicrophoneOff;
        soundGlyph.Text = ScreenRecorder.Sound ? IconGlyphs.Volume : IconGlyphs.Mute;
        pauseGlyph.Text = paused ? IconGlyphs.Play : IconGlyphs.Pause;
        PaintGlyph(microphoneGlyph, ScreenRecorder.Mic ? UiTokens.Keys.TextPrimary : null);
        PaintGlyph(soundGlyph, ScreenRecorder.Sound ? UiTokens.Keys.TextPrimary : null);
        PaintGlyph(clickGlyph, ScreenRecorder.Clicks ? UiTokens.Keys.TextPrimary : UiTokens.Keys.TextSecondary);
        foreach (var pair in captions)
            if (pair.Key is ButtonBase) System.Windows.Automation.AutomationProperties.SetName(pair.Key, pair.Value());
        if (discardShown != ConfirmingDiscard) ShowDiscard(ConfirmingDiscard);
        RefreshDot(paused);
        if (hovered != null && caption.Visibility == Visibility.Visible) captionText.Text = captions[hovered]();
    }

    void ShowDiscard(bool confirming)
    {
        discardShown = confirming;
        if (confirming)
        {
            // Asks with words, in the recording red (Hams' set_role): no second meaning hidden in a colour.
            Mui.SetRole(DiscardButton, MuiRole.Recording);
            DiscardButton.Padding = new Thickness(UiTokens.Space.Lg, 0, UiTokens.Space.Lg, 0);
            DiscardButton.Content = ButtonContent.Label(UiText.Toolbar.ConfirmDiscardShort, IconGlyphs.Delete);
        }
        else
        {
            Mui.SetRole(DiscardButton, MuiRole.Plain);
            DiscardButton.Padding = new Thickness(0);
            DiscardButton.Content = TextStyle.Icon(IconGlyphs.Delete, 16);
        }
    }

    // The dot pulses while recording; when paused it stays still and muted.
    void RefreshDot(bool paused)
    {
        if (paused)
        {
            dot.BeginAnimation(OpacityProperty, null);
            dot.Opacity = 1;
            dot.SetResourceReference(Shapes.Shape.FillProperty, UiTokens.Keys.TextSecondary);
            TimeText.SetResourceReference(TextBlock.ForegroundProperty, UiTokens.Keys.TextSecondary);
            return;
        }
        dot.SetResourceReference(Shapes.Shape.FillProperty, UiTokens.Keys.Recording);
        TimeText.SetResourceReference(TextBlock.ForegroundProperty, UiTokens.Keys.TextPrimary);
        if (IsVisible && !dot.HasAnimatedProperties)
            dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.3, Motion.RecordingPulse)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
    }

    // null = "off" in the recording red
    static void PaintGlyph(TextBlock glyph, string? brushKey) =>
        glyph.SetResourceReference(TextBlock.ForegroundProperty, brushKey ?? UiTokens.Keys.Error);

    void UpdateTimeAndLevel()
    {
        TimeText.Text = Formats.Clock(ScreenRecorder.Elapsed);
        double level = ScreenRecorder.Mic && !ScreenRecorder.Paused ? Math.Min(1, Math.Sqrt(ScreenRecorder.MicLevel) * 1.2) : 0;
        levelScale.ScaleX = level < 0.04 ? 0 : level;
        if (!ScreenRecorder.Paused && !dot.HasAnimatedProperties) Refresh();
    }

    // First click: ask in words; second click within 3 s: discard.
    void Discard()
    {
        if (ConfirmingDiscard)
        {
            ConfirmingDiscard = false;
            confirmTimer.Stop();
            ScreenRecorder.Discard();
            return;
        }
        ConfirmingDiscard = true;
        confirmTimer.Stop();
        confirmTimer.Start();
        Refresh();
    }

    void StartDrag()
    {
        NativeMethods.GetCursorPos(out var cursor);
        NativeMethods.GetWindowRect(Hwnd, out RECT window);
        dragStartX = cursor.x;
        dragStartY = cursor.y;
        windowStartX = window.left;
        windowStartY = window.top;
    }

    void DragTo()
    {
        NativeMethods.GetCursorPos(out var cursor);
        NativeMethods.MoveTopmost(Hwnd, windowStartX + cursor.x - dragStartX, windowStartY + cursor.y - dragStartY);
    }
}
