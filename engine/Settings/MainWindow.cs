using System.Windows.Controls.Primitives;
using System.Windows.Shell;

namespace NazarCapture.Engine.Settings;

/// <summary>
/// The small main window, as Hams' (main_window.py): fixed zones that keep their place in every state, so the window
/// never jumps. 1. head: mark and name (draggable), the gear stone (settings) and the close stone; 2. the one status
/// line; 3. the actions: "Screenshot" as the main action in the accent, "Aufnahme" beside it as a secondary stone with
/// the recording dot; 4. both hotkeys as key caps. Same width, margins and behaviour as Hams': dragged by its free
/// paper, its position remembered, Esc and the cross hide it. The tray icon opens it; the tray menu stays.
/// </summary>
internal sealed class MainWindow : Window
{
    /// <summary>Width (Hams' WINDOW_WIDTH).</summary>
    public const double WindowWidth = 360;
    /// <summary>Distance from the corner of the work area when no position is remembered.</summary>
    public const double CornerGap = 24;
    const string PlaceKey = "main";

    static MainWindow? shared;

    readonly Func<CaptureSettingsSnapshot> load;
    readonly Func<bool> recording;
    readonly WindowPlaces? places;
    readonly DispatcherTimer positionSaver;
    readonly StatusDot statusDot = new(Status.Ready, StatusDot.MainSize);
    readonly TextBlock statusText;
    readonly SecondaryButton recordButton;
    readonly KeyCombo screenshotKeys = new(), recordingKeys = new();

    public MainWindow(Func<CaptureSettingsSnapshot> load, Func<bool> recording, WindowPlaces? places = null)
    {
        this.load = load;
        this.recording = recording;
        this.places = places;
        Title = UiText.Titles.Main;
        Localizer.Apply(this);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = WindowWidth;
        ShowInTaskbar = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        Palette.Apply(Resources);
        SetResourceReference(BackgroundProperty, UiTokens.Keys.WindowBg);
        InputModality.Install();

        var card = new StackPanel { Margin = new Thickness(UiTokens.Space.Window) };

        // 1. head: mark and name, gear and cross (creation order is the Tab order: gear, then cross)
        var head = new DockPanel { LastChildFill = true };
        Gear = IconButton.Stone(LineIcon.Shape.Settings, UiText.Main.SettingsTooltip, () => SettingsWindow.ShowShared(false));
        CloseButton = IconButton.Stone(LineIcon.Shape.Close, UiText.Main.HideTooltip, HideWindow, UiTokens.Keys.Error);
        DockPanel.SetDock(CloseButton, Dock.Right);
        DockPanel.SetDock(Gear, Dock.Right);
        Gear.Margin = new Thickness(0, 0, UiTokens.Space.Xs, 0);
        var mark = new AppMark(AppMark.StageSize) { Margin = new Thickness(0, 0, UiTokens.Space.Md, 0) };
        DockPanel.SetDock(mark, Dock.Left);
        var name = TextStyle.Text(UiText.Product, TextRole.WindowTitle);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.IsHitTestVisible = false;
        head.Children.Add(CloseButton);
        head.Children.Add(Gear);
        head.Children.Add(mark);
        head.Children.Add(name);
        card.Children.Add(head);

        // 2. the status line
        var status = new DockPanel { Margin = new Thickness(0, UiTokens.Space.Xl, 0, 0) };
        statusDot.Margin = new Thickness(0, UiTokens.Space.Sm, UiTokens.Space.Md, UiTokens.Space.Sm);
        DockPanel.SetDock(statusDot, Dock.Left);
        status.Children.Add(statusDot);
        statusText = TextStyle.Text(UiText.Main.Ready, TextRole.RowTitle);
        statusText.FontWeight = FontWeights.Normal;   // Hams' "status": the body face, 12 pt
        statusText.VerticalAlignment = VerticalAlignment.Center;
        statusText.IsHitTestVisible = false;
        status.Children.Add(statusText);
        card.Children.Add(status);

        // 3. the actions
        var actions = new UniformGrid { Rows = 1, Columns = 2, Margin = new Thickness(0, UiTokens.Space.Lg, 0, 0) };
        ScreenshotButton = new PrimaryButton(UiText.Main.Screenshot, TakeScreenshot) { Margin = new Thickness(0, 0, UiTokens.Space.Md / 2, 0) };
        recordButton = new SecondaryButton(UiText.Main.Recording, ToggleRecording)
        {
            Height = UiTokens.Height.Primary + UiTokens.Depth.Control,
            Margin = new Thickness(UiTokens.Space.Md / 2, 0, 0, 0),
        };
        SetRecordLabel(UiText.Main.Recording);
        actions.Children.Add(ScreenshotButton);
        actions.Children.Add(recordButton);
        card.Children.Add(actions);

        // 4. both hotkeys as keys
        var footer = new Grid { Margin = new Thickness(0, UiTokens.Space.Lg, 0, 0), IsHitTestVisible = false };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition());
        footer.RowDefinitions.Add(new RowDefinition());
        AddShortcut(footer, 0, UiText.Main.Screenshot, screenshotKeys);
        AddShortcut(footer, 1, UiText.Main.Recording, recordingKeys);
        card.Children.Add(footer);

        var frame = new Border { Child = card };
        frame.SetResourceReference(Border.BackgroundProperty, UiTokens.Keys.WindowBg);
        WindowDrag.SetIsSurface(frame, true);
        WindowDrag.SetIsSurface(head, true);
        WindowDrag.SetIsSurface(status, true);
        head.Background = Brushes.Transparent;
        status.Background = Brushes.Transparent;
        Content = frame;
        WindowDrag.Install(this);

        positionSaver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        positionSaver.Tick += delegate { positionSaver.Stop(); RememberPosition(); };
        LocationChanged += delegate { if (IsVisible) { positionSaver.Stop(); positionSaver.Start(); } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !e.Handled) { e.Handled = true; HideWindow(); } };
        SourceInitialized += (_, _) =>
        {
            int round = NativeMethods.DWMWCP_ROUND;
            _ = NativeMethods.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        };
        Refresh();
    }

    public IconButton Gear { get; }

    public IconButton CloseButton { get; }

    public PrimaryButton ScreenshotButton { get; }

    public SecondaryButton RecordButton => recordButton;

    public KeyCombo ScreenshotKeys => screenshotKeys;

    public KeyCombo RecordingKeys => recordingKeys;

    public string StatusText => statusText.Text;

    /// <summary>Opens (or brings back) the one main window of the engine.</summary>
    public static void ShowShared()
    {
        shared ??= new MainWindow(CaptureSettings.Load, () => ScreenRecorder.Busy, WindowPlaces.User());
        shared.Open();
    }

    public void Open()
    {
        Refresh();
        Palette.Apply(Resources);
        if (!IsVisible)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;   // measured first, then placed
            Show();
            UpdateLayout();
            Place();
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        ToolWindowBase.TakeFocus(this, new WindowInteropHelper(this).Handle);
    }

    /// <summary>The remembered position if it still lies on a screen, otherwise bottom right above the tray.</summary>
    void Place()
    {
        if (places?.Read(PlaceKey) is { } spot && OnSomeScreen(spot.A, spot.B))
        {
            Left = spot.A;
            Top = spot.B;
            return;
        }
        RECT work = NativeMethods.WorkAreaAtCursor();
        double scale = NativeMethods.ScaleOfMonitor(work);
        Left = work.right / scale - ActualWidth - CornerGap;
        Top = work.bottom / scale - ActualHeight - CornerGap;
    }

    static bool OnSomeScreen(double left, double top)
    {
        var corner = new Point(left + 40, top + 20);
        return corner.X >= SystemParameters.VirtualScreenLeft && corner.Y >= SystemParameters.VirtualScreenTop
            && corner.X <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && corner.Y <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
    }

    /// <summary>Takes over the current hotkeys and whether a recording runs.</summary>
    internal void Refresh()
    {
        var settings = load();
        screenshotKeys.SetCombo(settings.ScreenshotHotkey);
        recordingKeys.SetCombo(settings.RecordingHotkey);
        bool running = recording();
        statusDot.Show(running ? Status.Recording : Status.Ready);
        statusText.Text = running ? UiText.Main.RecordingRunning : UiText.Main.Ready;
        SetRecordLabel(running ? UiText.Main.StopRecording : UiText.Main.Recording);
    }

    void SetRecordLabel(string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new StatusDot(Status.Recording, StatusDot.MainSize) { Margin = new Thickness(0, 0, UiTokens.Space.Md, 0) };
        row.Children.Add(dot);
        row.Children.Add((UIElement)ButtonContent.Label(text, null));
        recordButton.Content = row;
        ButtonContent.Name(recordButton, text);
    }

    static void AddShortcut(Grid footer, int row, string label, KeyCombo keys)
    {
        var gap = new Thickness(0, row > 0 ? UiTokens.Space.Sm : 0, 0, 0);
        var text = TextStyle.Text(label, TextRole.Description, UiTokens.Keys.TextTertiary);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = gap;
        Grid.SetRow(text, row);
        footer.Children.Add(text);
        keys.Margin = gap;
        keys.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(keys, row);
        Grid.SetColumn(keys, 1);
        footer.Children.Add(keys);
    }

    void TakeScreenshot()
    {
        HideWindow();
        Screenshots.StartSoon();
    }

    void ToggleRecording()
    {
        HideWindow();
        ScreenRecorder.ToggleSoon();
    }

    internal void HideWindow()
    {
        if (positionSaver.IsEnabled) positionSaver.Stop();
        if (IsVisible) RememberPosition();
        Hide();
    }

    void RememberPosition()
    {
        if (places == null || WindowState != WindowState.Normal || Left < -10000) return;
        places.Write(PlaceKey, Left, Top);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (retired) return;
        e.Cancel = true;   // kept for the next time
        HideWindow();
    }

    bool retired;

    /// <summary>Closes for good (the language changed: a new window takes its place).</summary>
    public void Retire()
    {
        if (IsVisible) RememberPosition();
        retired = true;
        Close();
    }

    /// <summary>The language changed: the shared window is built anew; a shown one is shown again in the same place.</summary>
    internal static void Relocalize()
    {
        if (shared == null) return;
        var old = shared;
        bool visible = old.IsVisible;
        old.Retire();
        shared = new MainWindow(CaptureSettings.Load, () => ScreenRecorder.Busy, WindowPlaces.User());
        if (visible) shared.Open();
    }
}
