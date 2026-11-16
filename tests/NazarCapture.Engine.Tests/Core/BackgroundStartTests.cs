using System.Runtime.InteropServices;

namespace NazarCapture.Engine.Tests.Core;

/// <summary>
/// The start after logon is silent, as in Hams: the host starts with <c>--background</c> (tray icon only) and later
/// pre-starts the engine with <c>--idle</c>. The engine then builds its windows invisibly: no visible window, nothing in
/// the taskbar, no card, no focus taken. Checked on a real engine process (isolated, its own settings file).
/// </summary>
public sealed class BackgroundStartTests
{
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    /// <summary>Titles of the visible top-level windows of a process.</summary>
    internal static List<string> VisibleWindows(int processId)
    {
        var found = new List<string>();
        EnumWindows((hwnd, _) =>
        {
            if (GetWindowThreadProcessId(hwnd, out uint owner) != 0 && owner == processId && IsWindowVisible(hwnd))
            {
                var title = new StringBuilder(256);
                found.Add(GetWindowText(hwnd, title, title.Capacity) > 0 ? title.ToString() : "(ohne Titel)");
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    [Fact]
    public void TheIdleStartShowsNoWindow()
    {
        string engine = Path.Combine(Path.GetDirectoryName(typeof(CaptureBridge).Assembly.Location)!, "NazarCapture.Engine.exe");
        Assert.True(File.Exists(engine), engine);
        using var folder = new TempDirectory();
        string ini = folder.File("settings.ini");
        File.WriteAllText(ini, "[capture]\r\ncheck_updates=0\r\nautostart=0\r\n", Encoding.Unicode);
        string instance = "bg-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var start = new ProcessStartInfo(engine, "--idle") { UseShellExecute = false };
        start.EnvironmentVariables[TestIsolation.InstanceVariable] = instance;
        start.EnvironmentVariables[CaptureSettings.TestConfigVariable] = ini;
        using var process = Process.Start(start)!;
        try
        {
            string title = "NazarCapture.Engine" + TestIsolation.Infix + instance;
            var watch = Stopwatch.StartNew();
            IntPtr window = IntPtr.Zero;
            while (window == IntPtr.Zero && watch.Elapsed < TimeSpan.FromSeconds(20))
            {
                Thread.Sleep(100);
                window = FindWindow(null, title);
            }
            Assert.NotEqual(IntPtr.Zero, window);
            Thread.Sleep(3500);   // the warm-up runs one idle second after the start: overlay, card and kit built invisibly
            Assert.False(process.HasExited);
            Assert.Empty(VisibleWindows(process.Id));
            PostMessage(window, CaptureBridge.QuitMessage, IntPtr.Zero, IntPtr.Zero);
            Assert.True(process.WaitForExit(10000), "the idle engine quits when asked");
        }
        finally
        {
            if (!process.HasExited) process.Kill();
        }
    }
}
