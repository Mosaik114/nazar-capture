using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace NazarCapture.Engine.Tests.Infrastructure;

/// <summary>
/// Runs a test body on its own STA thread on a desktop of its own that nobody sees: windows and popups there can be
/// shown, laid out, loaded and clicked like real ones, and none of them ever appears on the user's screen. Input comes
/// in as the raw reports that WPF's Win32 providers make of mouse messages (<see cref="RealMouse"/>), so the whole
/// input pipeline runs: hit testing, capture, class handlers, routed events.
/// </summary>
internal static class HiddenDesktop
{
    const uint DesktopAll = 0x01FF;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr attributes);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetThreadDesktop(IntPtr desktop);

    [DllImport("user32.dll")]
    static extern bool CloseDesktop(IntPtr desktop);

    const int ApartmentThreaded = 2;

    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr reserved, int flags);

    [DllImport("ole32.dll")]
    static extern void CoUninitialize();

    public static void Run(Action body)
    {
        IntPtr desktop = CreateDesktop("nazar-test-" + Guid.NewGuid().ToString("N"), IntPtr.Zero, IntPtr.Zero, 0, DesktopAll, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new InvalidOperationException("CreateDesktop failed: " + Marshal.GetLastWin32Error());
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // A thread with a window cannot change its desktop, and entering the STA gave this one COM's hidden
                // window: leave the apartment for the switch and enter it again on the new desktop.
                CoUninitialize();
                bool moved = SetThreadDesktop(desktop);
                int error = Marshal.GetLastWin32Error();
                Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, ApartmentThreaded));
                if (!moved) throw new InvalidOperationException("SetThreadDesktop failed: " + error);
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                body();
            }
            catch (Exception e) { failure = e; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        })
        { IsBackground = true, Name = "hidden desktop test" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool done = thread.Join(TimeSpan.FromSeconds(60));
        if (done) CloseDesktop(desktop);
        if (!done) throw new TimeoutException("the test on the hidden desktop did not finish");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

/// <summary>
/// The mouse as Windows delivers it: each move, press and release goes to the window under the pointer, or to the
/// window holding the capture, in its client pixels, and enters WPF as the raw report HwndMouseInputProvider makes of
/// that message. Only the provider's own check of the cursor is left out (on a desktop nobody sees there is no cursor
/// position): it is marked active as it marks itself when the pointer enters its window. Activation, capture, hit
/// testing, class handlers and routing are all WPF's own.
/// </summary>
internal static class RealMouse
{
    [DllImport("user32.dll")]
    static extern IntPtr GetCapture();

    [DllImport("user32.dll")]
    static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;

    /// <summary>Moves to the middle of <paramref name="element"/>, presses and releases the left button there.</summary>
    public static void Click(UIElement element)
    {
        var source = (HwndSource?)PresentationSource.FromVisual(element) ?? throw new InvalidOperationException(element + " is not shown");
        var size = element.RenderSize;
        var screen = element.PointToScreen(new Point(size.Width / 2, size.Height / 2));
        Send(source, "AbsoluteMove", screen);
        Send(source, "Button1Press", screen);
        Send(source, "Button1Release", screen);
    }

    static void Send(HwndSource under, string actions, Point screen)
    {
        IntPtr capture = GetCapture();
        var source = capture != IntPtr.Zero && HwndSource.FromHwnd(capture) is HwndSource captured ? captured : under;
        var client = new POINT { X = (int)Math.Round(screen.X), Y = (int)Math.Round(screen.Y) };
        ScreenToClient(source.Handle, ref client);

        var holder = typeof(HwndSource).GetField("_mouse", Hidden)!.GetValue(source);
        var provider = holder?.GetType().GetField("_value", Hidden)!.GetValue(holder);
        var active = provider?.GetType().GetField("_active", Hidden)!;
        if (provider != null && !(bool)active!.GetValue(provider)!)
        {
            actions = "Activate, " + actions;   // as the provider reports the first message in its window
            active.SetValue(provider, true);
        }

        var core = typeof(Mouse).Assembly;
        var report = Activator.CreateInstance(core.GetType("System.Windows.Input.RawMouseInputReport", true)!, Hidden | System.Reflection.BindingFlags.Public, null,
            new object[] { InputMode.Foreground, Environment.TickCount, source, Enum.Parse(core.GetType("System.Windows.Input.RawMouseActions", true)!, actions), client.X, client.Y, 0, IntPtr.Zero },
            CultureInfo.InvariantCulture)!;
        var args = (InputEventArgs)Activator.CreateInstance(core.GetType("System.Windows.Input.InputReportEventArgs", true)!, Hidden | System.Reflection.BindingFlags.Public, null,
            new object[] { Mouse.PrimaryDevice, report }, CultureInfo.InvariantCulture)!;
        args.RoutedEvent = (RoutedEvent)typeof(InputManager).GetField("PreviewInputReportEvent", Hidden)!.GetValue(null)!;
        InputManager.Current.ProcessInput(args);
        WpfThread.Pump(30);
    }
}
