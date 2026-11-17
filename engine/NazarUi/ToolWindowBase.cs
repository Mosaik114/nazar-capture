using System.Windows.Shell;

namespace NazarCapture.Engine.NazarUi;

/// <summary>
/// Layer 5, the floating windows (cards, recording bar, countdown, frame, pinned image, overlay): borderless,
/// transparent, topmost, not activated on show, themed from the tokens. Placement happens in device pixels with the
/// scale of the <em>target</em> monitor, read before the window moves there, and again after Windows reports a new DPI,
/// so a card never lands misplaced on a monitor with another scale.
/// </summary>
internal abstract class ToolWindowBase : Window
{
    Func<Size, (int X, int Y)?>? placement;

    protected ToolWindowBase(string title)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Palette.Apply(Resources);
        InputModality.Install();
    }

    /// <summary>The window handle (created hidden by <see cref="CreateHandle"/>).</summary>
    public IntPtr Hwnd { get; private set; }

    /// <summary>Creates the hidden handle and adds extended styles (WS_EX_*).</summary>
    protected IntPtr CreateHandle(int extendedStyles)
    {
        Hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (extendedStyles != 0) NativeMethods.AddExtendedStyle(Hwnd, extendedStyles);
        return Hwnd;
    }

    /// <summary>
    /// The corner of a window of <paramref name="size"/> (device pixels) placed at the bottom right of
    /// <paramref name="workArea"/>.
    /// </summary>
    public static (int X, int Y) BottomRight(RECT workArea, Size size) =>
        (workArea.right - (int)Math.Round(size.Width), workArea.bottom - (int)Math.Round(size.Height));

    /// <summary>The window's size in device pixels at <paramref name="scale"/>.</summary>
    public Size DeviceSize(double scale) => new(Math.Round(ActualWidth * scale), Math.Round(ActualHeight * scale));

    /// <summary>
    /// Shows the window (if needed) and puts it at the bottom right of <paramref name="workArea"/>: measured first, sized
    /// with the scale of the monitor of the work area, and placed again whenever that monitor's DPI arrives later.
    /// </summary>
    public void ShowAtBottomRight(RECT workArea)
    {
        PlaceWith(device => BottomRight(workArea, device), NativeMethods.ScaleOfMonitor(workArea));
    }

    /// <summary>Shows and places the window with a rule that maps its device size to a position.</summary>
    protected void PlaceWith(Func<Size, (int X, int Y)?> rule, double targetScale)
    {
        placement = rule;
        if (!IsVisible)
        {
            Left = -10000;   // measure first, then move to the target
            Show();
        }
        UpdateLayout();
        var corner = rule(DeviceSize(targetScale));
        if (corner is { } p) NativeMethods.MoveTopmost(Hwnd, p.X, p.Y);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (placement == null || Hwnd == IntPtr.Zero || !IsVisible) return;
        UpdateLayout();
        if (placement(DeviceSize(newDpi.DpiScaleX)) is { } p) NativeMethods.MoveTopmost(Hwnd, p.X, p.Y);
    }

    /// <summary>
    /// The hotkey that brings a non-activating window to the keyboard: the app's own hotkey for it plus Ctrl (plus Shift
    /// when Ctrl is in it already); null when that is not possible.
    /// </summary>
    internal static HotkeyChord? KeyboardChord(string hotkey)
    {
        if (!HotkeyText.TryParse(hotkey, out var chord, out _)) return null;
        uint extra = (chord.Modifiers & HotkeyChord.Control) == 0 ? HotkeyChord.Control : HotkeyChord.Shift;
        return (chord.Modifiers & extra) != 0 ? null : chord with { Modifiers = chord.Modifiers | extra };
    }

    /// <summary>
    /// Activates the window. If Windows refuses, the input of the foreground thread is attached briefly; never to a hung
    /// program (Windows would block this engine with it) — then the window simply stays inactive. The host also allows
    /// the engine to take the foreground before it sends a capture command (AllowSetForegroundWindow).
    /// </summary>
    public static void TakeFocus(Window window, IntPtr hwnd)
    {
        window.Activate();
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground != hwnd && foreground != IntPtr.Zero && !NativeMethods.IsHungAppWindow(foreground))
        {
            uint foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, IntPtr.Zero);
            uint ownThread = NativeMethods.GetCurrentThreadId();
            if (foregroundThread != ownThread && NativeMethods.AttachThreadInput(ownThread, foregroundThread, true))
            {
                NativeMethods.SetForegroundWindow(hwnd);
                NativeMethods.AttachThreadInput(ownThread, foregroundThread, false);
            }
        }
        window.Focus();
        Keyboard.Focus(window);
    }
}

/// <summary>
/// A hotkey of one window while it is shown (Esc for the countdown, the key that brings the last card to the keyboard):
/// RegisterHotKey on the window's handle, WM_HOTKEY instead of polling the keyboard. Released with <see cref="Dispose"/>.
/// </summary>
internal sealed class WindowHotkey : IDisposable
{
    static int nextId = 0x5A00;
    readonly IntPtr hwnd;
    readonly int id;
    readonly Action pressed;
    HwndSource? source;

    WindowHotkey(IntPtr hwnd, int id, Action pressed)
    {
        this.hwnd = hwnd;
        this.id = id;
        this.pressed = pressed;
    }

    /// <summary>Registers <paramref name="key"/> with <paramref name="modifiers"/>; null when Windows refuses (taken).</summary>
    public static WindowHotkey? Register(IntPtr hwnd, uint modifiers, int key, Action pressed)
    {
        var source = HwndSource.FromHwnd(hwnd);
        if (source == null) return null;
        int id = Interlocked.Increment(ref nextId);
        if (!NativeMethods.RegisterHotKey(hwnd, id, modifiers | NativeMethods.MOD_NOREPEAT, (uint)key)) return null;
        var hotkey = new WindowHotkey(hwnd, id, pressed) { source = source };
        source.AddHook(hotkey.Hook);
        return hotkey;
    }

    IntPtr Hook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WM_HOTKEY && wParam.ToInt32() == id)
        {
            handled = true;
            pressed();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (source == null) return;
        source.RemoveHook(Hook);
        source = null;
        NativeMethods.UnregisterHotKey(hwnd, id);
    }
}

/// <summary>
/// Layer 5, the ordinary windows (trim window, settings): own frame on the paper colour, a head, a close cross,
/// draggable by the head and the free paper, Esc closes. Resizable through the system frame (WindowChrome), rounded
/// corners on Windows 11. Two heads: the compact one (mark, product name, caption, plain cross; the trim window) and,
/// with a <see cref="BannerMotif"/>, Hams' <see cref="Stage"/> (mark, title, painted banner, round close stone).
/// </summary>
internal class NazarWindow : Window
{
    public const double HeadHeight = 52;

    readonly DockPanel frame = new();
    readonly ContentControl body = new() { Focusable = false };
    readonly TextBlock? caption;

    public NazarWindow(string title, BannerMotif? stage = null)
    {
        Title = UiText.Product + " – " + title;
        Localizer.Apply(this);   // Arabic: the whole window from right to left
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = stage != null ? Stage.StageHeight : HeadHeight,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        Palette.Apply(Resources);
        SetResourceReference(BackgroundProperty, UiTokens.Keys.WindowBg);
        InputModality.Install();
        HeadActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        FrameworkElement head;
        if (stage != null)
        {
            StageHead = new Stage(title, () => RequestClose(), UiText.Common.Close, stage);
            head = StageHead;
        }
        else
        {
            var compact = new DockPanel { Height = HeadHeight, Margin = new Thickness(UiTokens.Space.Window, 0, UiTokens.Space.Md, 0) };
            var close = new IconButton(IconGlyphs.ChromeClose, UiText.Common.Close, () => RequestClose()) { VerticalAlignment = VerticalAlignment.Center };
            WindowChrome.SetIsHitTestVisibleInChrome(close, true);
            DockPanel.SetDock(close, Dock.Right);
            compact.Children.Add(close);
            WindowChrome.SetIsHitTestVisibleInChrome(HeadActions, true);
            DockPanel.SetDock(HeadActions, Dock.Right);
            compact.Children.Add(HeadActions);
            var mark = new AppMark { Margin = new Thickness(0, 0, UiTokens.Space.Md + UiTokens.Space.Xs, 0) };
            DockPanel.SetDock(mark, Dock.Left);
            compact.Children.Add(mark);
            var name = TextStyle.Text(UiText.Product, TextRole.WindowTitle);
            name.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(name, Dock.Left);
            compact.Children.Add(name);
            caption = TextStyle.Text(title, TextRole.Section, UiTokens.Keys.TextSecondary);
            caption.VerticalAlignment = VerticalAlignment.Center;
            caption.Margin = new Thickness(UiTokens.Space.Md + UiTokens.Space.Xs, 2, 0, 0);
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            compact.Children.Add(caption);
            head = compact;
        }
        DockPanel.SetDock(head, Dock.Top);
        frame.Children.Add(head);
        frame.Children.Add(body);
        frame.SetResourceReference(Panel.BackgroundProperty, UiTokens.Keys.WindowBg);   // the paper is part of the content (pixel tests render it)
        WindowDrag.SetIsSurface(frame, true);
        Content = frame;
        WindowDrag.Install(this);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !e.Handled && CloseOnEscape) { e.Handled = true; RequestClose(); }
        };
        SourceInitialized += (_, _) =>
        {
            int round = NativeMethods.DWMWCP_ROUND;
            _ = NativeMethods.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        };
    }

    /// <summary>What the cross and Esc do: close (a reused window hides instead).</summary>
    protected virtual void RequestClose() => Close();

    /// <summary>Room in the compact head for the window's main actions (left of the cross).</summary>
    public StackPanel HeadActions { get; }

    /// <summary>The painted head (windows built with a banner motif).</summary>
    public Stage? StageHead { get; }

    /// <summary>Whether Esc closes the window (a control may keep Esc, e.g. a hotkey field while it records).</summary>
    public bool CloseOnEscape { get; set; } = true;

    /// <summary>The window's content below the head.</summary>
    public UIElement? Body
    {
        get => body.Content as UIElement;
        set => body.Content = value;
    }

    public string Caption
    {
        get => StageHead?.Title ?? caption!.Text;
        set
        {
            if (StageHead != null) StageHead.Title = value;
            else caption!.Text = value;
            Title = UiText.Product + " – " + value;
        }
    }
}

/// <summary>
/// The app's mark in a window head: the "Fokus" logo (lens ring of one stroke, a small square corner, the point of
/// focus) in cobalt ink and watercolour, from design/logo/nazar_logo.py, shown small and smooth.
/// </summary>
internal sealed class AppMark : Image
{
    public const double Size = 24;
    /// <summary>The mark on a stage and in the main window head (Hams' MARK_SIZE).</summary>
    public const double StageSize = 26;

    static readonly Lazy<BitmapSource> logo = new(() =>
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;   // registers the pack:// scheme
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/NazarCapture.Engine;component/NazarUi/Logo/nazar_mark.png");
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 96;   // enough for 24 px at 400 %
        image.EndInit();
        image.Freeze();
        return image;
    });

    public AppMark(double size = Size)
    {
        Width = Height = size;
        Source = logo.Value;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        FlowDirection = FlowDirection.LeftToRight;   // a logo is never mirrored
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        System.Windows.Automation.AutomationProperties.SetName(this, UiText.Product);
    }
}
