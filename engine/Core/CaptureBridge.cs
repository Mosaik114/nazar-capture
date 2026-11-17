using System.ComponentModel;
namespace NazarCapture.Engine.Core;

/// <summary>
/// Process entry and IPC with the host (the Rust tray app).
/// </summary>
/// <remarks>
/// Command line: <c>--screenshot</c> (default), <c>--recording</c>, <c>--idle</c>,
/// <c>--self-test &lt;report&gt;</c>, <c>--live-test &lt;dir&gt;</c>, <c>--measure &lt;dir&gt;</c>, <c>--kit-demo</c>. The engine adds
/// <c>--after &lt;pid&gt;</c> when it restarts itself after a crash: the new process waits for the old
/// one to be gone before it takes the mutex (the host never sends this flag).
/// Only one engine runs per session (mutex <see cref="MutexName"/>). A second start forwards its
/// command to the running instance and exits. The running instance owns a hidden window titled
/// <see cref="Title"/> that accepts:
/// <list type="table">
/// <item><term>0x8001</term><description>take a screenshot</description></item>
/// <item><term>0x8002</term><description>start / stop a screen recording</description></item>
/// <item><term>0x8003</term><description>quit (ignored while <see cref="EngineActivity.Busy"/>: a recording, the overlay, editor or trim window, a save or an export)</description></item>
/// <item><term>0x800A</term><description>query state: bit 0 = busy (the same <see cref="EngineActivity.Busy"/>), bit 1 = live (counting down or recording)</description></item>
/// </list>
/// <para>
/// Trust model: the window is found by its title, so any process of the same user in the same session can
/// post these messages (window messages do not cross sessions, and UIPI keeps lower-integrity processes out).
/// Such a process can already do everything the user can, and nothing here is privileged: the worst case
/// is that a capture overlay opens or an idle engine quits. The host verifies the engine's process image
/// before it relies on it. Capture commands arriving faster than <see cref="CommandsPerSecond"/> per
/// second are ignored, so a flood cannot keep reopening the overlay.
/// </para>
/// </remarks>
internal static class CaptureBridge
{
    // Suffixed only under test isolation (see TestIsolation), so a test engine never meets a real one.
    internal static readonly string Title = TestIsolation.Isolate("NazarCapture.Engine");
    internal static readonly string MutexName = TestIsolation.Isolate("Local\\NazarCapture.Engine");
    internal const int CommandBase = 0x8000;
    internal const int ScreenshotMessage = 0x8001, RecordingMessage = 0x8002, QuitMessage = 0x8003, SettingsMessage = 0x8004, RecordingSettingsMessage = 0x8005, MainMessage = 0x8006, StateMessage = 0x800A;
    internal const string AfterArgument = "--after";

    /// <summary>More capture commands per second than this are ignored (a person presses a hotkey far less often).</summary>
    internal const int CommandsPerSecond = 10;

    static readonly CommandRateLimit commandLimit = new(CommandsPerSecond, TimeSpan.FromSeconds(1));
    static readonly LogThrottle rejectionLog = new(TimeSpan.FromSeconds(1));
    const int ForwardAttempts = 100, ForwardDelayMs = 50;

    /// <summary>How long a restarted engine waits for the crashed one to exit.</summary>
    static readonly TimeSpan ParentExitTimeout = TimeSpan.FromSeconds(10);

    enum Command { Idle = 0, Screenshot = 1, Recording = 2, Settings = 4, RecordingSettings = 5, Main = 6 }

    public static void Run(string[] args)
    {
        EngineComposition.Compose();
        if (args.Length > 0 && args[0] == "--self-test") { Diagnostics.SelfTest.Run(args.Length > 1 ? args[1] : "capture-test.json"); return; }
        if (args.Length > 1 && args[0] == "--live-test") { Diagnostics.LiveTest.Run(args[1]); return; }
        if (args.Length > 1 && args[0] == "--measure") { Diagnostics.Measurement.Options(args); Diagnostics.Measurement.Run(args[1]); return; }
        if (args.Length > 0 && args[0] == "--kit-demo") { KitSamples.ShowDemo(); return; }
        int command = ParseCommand(args);
        if (ParseParent(args) is int parent) WaitForExit(parent, ParentExitTimeout);
        using var single = new Mutex(false, MutexName);
        if (!TryAcquire(single))
        {
            ForwardToRunningInstance(command);
            return;
        }
        try
        {
            RunMessageLoop(command);
        }
        finally
        {
            single.ReleaseMutex();
        }
    }

    /// <summary>Takes the single-instance mutex without waiting; an abandoned one (crashed owner) counts as taken.</summary>
    internal static bool TryAcquire(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException ex)
        {
            ErrorLog.Write("The previous engine ended without releasing its mutex", ex);
            return true;   // the wait succeeded: this process owns the mutex now
        }
    }

    /// <summary>The process id after <c>--after</c>, if any.</summary>
    internal static int? ParseParent(string[] args)
    {
        int index = Array.IndexOf(args, AfterArgument);
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) ? pid : null;
    }

    /// <summary>Waits until the process has exited (or is unknown); false after the timeout.</summary>
    internal static bool WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException) { return true; }            // already gone
        catch (InvalidOperationException) { return true; }    // exited while we looked
        catch (Win32Exception ex)                             // no access: fall back to the mutex alone
        {
            ErrorLog.Write("Waiting for the previous engine", ex);
            return false;
        }
    }

    // Another engine owns the mutex: hand the command over (its window may still be starting up).
    static void ForwardToRunningInstance(int command)
    {
        for (int attempt = 0; attempt < ForwardAttempts; attempt++)
        {
            IntPtr window = NativeMethods.FindWindow(null, Title);
            if (window != IntPtr.Zero)
            {
                if (!NativeMethods.PostMessage(window, CommandBase + command, IntPtr.Zero, IntPtr.Zero))
                    ErrorLog.Write("Forwarding a command to the running engine", new Win32Exception(Marshal.GetLastWin32Error()));
                return;
            }
            Thread.Sleep(ForwardDelayMs);
        }
        ErrorLog.Write("The running engine's window did not appear; the command was dropped", null);
    }

    /// <summary>
    /// Multicore JIT: the methods compiled during the last run are compiled again on a background core while this one
    /// starts, so the first overlay after logon is not slowed down by the JIT.
    /// </summary>
    static void StartJitProfile()
    {
        try
        {
            Directory.CreateDirectory(CaptureSettings.Home);
            System.Runtime.ProfileOptimization.SetProfileRoot(CaptureSettings.Home);
            System.Runtime.ProfileOptimization.StartProfile("engine.jitprofile");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static void RunMessageLoop(int command)
    {
        NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        StartJitProfile();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Program.Protect(app);
        var parameters = new HwndSourceParameters(Title) { Width = 0, Height = 0, WindowStyle = unchecked((int)NativeMethods.WS_POPUP) };   // never shown
        using var window = new HwndSource(parameters);
        window.AddHook(OnMessage);
        TestIsolation.Hook(window);
        Localizer.Use(CaptureSettings.Load().Language);   // the interface language of every window from here on
        app.Dispatcher.BeginInvoke(new Action(() => Execute(command)), DispatcherPriority.ApplicationIdle);
        // the captures of the last days that Nazar Capture may delete ("Aufbewahren", not kept), off the UI thread
        app.Dispatcher.BeginInvoke(new Action(FileKeeping.StartTidying), DispatcherPriority.ApplicationIdle);
        // a copy that inherited RedirectionGuard (started by an older setup) says so in the log, for reports of error 448
        app.Dispatcher.BeginInvoke(new Action(LinkSafety.LogState), DispatcherPriority.ApplicationIdle);
        app.Run();
    }

    /// <summary>
    /// 0 = idle, 1 = screenshot, 2 = recording, 4 = settings, 5 = settings at the recording page, 6 = the main window; added to <see cref="CommandBase"/> when forwarded.
    /// No argument means a screenshot (a plain start of the exe). An unknown first argument is logged and
    /// treated as idle: starting the engine must never take a screenshot nobody asked for.
    /// </summary>
    internal static int ParseCommand(string[] args)
    {
        if (args.Length == 0) return (int)Command.Screenshot;
        switch (args[0])
        {
            case "--screenshot": return (int)Command.Screenshot;
            case "--recording": return (int)Command.Recording;
            case "--idle": return (int)Command.Idle;
            case "--settings": return (int)Command.Settings;
            case "--recording-settings": return (int)Command.RecordingSettings;
            case "--main": return (int)Command.Main;
            default:
                ErrorLog.Write("Unknown command line argument " + args[0] + "; starting idle", null);
                return (int)Command.Idle;
        }
    }

    /// <summary>A pre-started engine runs below normal priority until the user wants something from it.</summary>
    static void RaisePriority()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            if (self.PriorityClass == ProcessPriorityClass.BelowNormal) self.PriorityClass = ProcessPriorityClass.Normal;
        }
        catch (Win32Exception) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>Reply to <see cref="StateMessage"/>.</summary>
    internal static int StateFlags(bool busy, bool live) => (busy ? 1 : 0) | (live ? 2 : 0);

    internal static string RejectionMessage(int skipped) =>
        "Ignored a capture command: too many per second" + (skipped > 0 ? " (" + skipped + " more since the last entry)" : "");

    /// <summary>The engine's state as replied to <see cref="StateMessage"/>.</summary>
    internal static int State() => StateFlags(EngineActivity.Busy, ScreenRecorder.Live);

    /// <summary><see cref="QuitMessage"/>: shuts down unless that would lose work; true if it did.</summary>
    internal static bool Quit(Action shutdown)
    {
        if (EngineActivity.Busy) return false;
        shutdown();
        return true;
    }

    static void Execute(int command, bool fromMenu = false)
    {
        if (command != (int)Command.Idle) RaisePriority();
        // A hotkey opens the overlay at once; after the tray menu a short pause lets the menu leave the screen first.
        if (command == (int)Command.Screenshot) { if (fromMenu) Screenshots.StartSoon(); else Screenshots.Start(); }
        else if (command == (int)Command.Recording) { if (fromMenu) ScreenRecorder.ToggleSoon(); else ScreenRecorder.Toggle(); }
        else if (command == (int)Command.Idle) Diagnostics.WarmUp.Soon();
        else if (command is (int)Command.Settings or (int)Command.RecordingSettings) Settings.SettingsWindow.ShowShared(command == (int)Command.RecordingSettings);
        else if (command == (int)Command.Main) Settings.MainWindow.ShowShared();
    }

    static IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case ScreenshotMessage:
            case RecordingMessage:
                handled = true;
                long now = Stopwatch.GetTimestamp();
                if (commandLimit.Allow(now)) Execute(message - CommandBase, wParam != IntPtr.Zero);
                else if (rejectionLog.Allow(now, out int skipped)) ErrorLog.Write(RejectionMessage(skipped), null);
                break;
            case SettingsMessage:
            case RecordingSettingsMessage:
            case MainMessage:
                handled = true;
                Execute(message - CommandBase);
                break;
            case QuitMessage:
                handled = true;
                Quit(() => Application.Current.Shutdown());
                break;
            case StateMessage:
                handled = true;
                return new IntPtr(State());
        }
        return IntPtr.Zero;
    }
}
