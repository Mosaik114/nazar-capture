namespace MosaikCapture.Engine.Diagnostics;

/// <summary>
/// The idle start after logon (<c>--idle</c> from the host): builds the overlay, a card and the kit once, invisibly,
/// so types, templates, fonts and the JIT are ready when the first hotkey comes. Nothing is shown.
/// </summary>
internal static class WarmUp
{
    public static void Soon()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += delegate { timer.Stop(); Run(); };
        timer.Start();
    }

    internal static void Run()
    {
        try
        {
            CaptureSettings.Load();
            WpfScreenshotUi.Current?.Prepare();   // the very windows the first hotkey uses
            var card = new ScreenshotCard(() => { }, () => { }, () => { }) { Offscreen = true };
            Lay((FrameworkElement)card.Content);
            card.Close();
            Lay(KitSamples.Components());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            ErrorLog.Write("Warming up", ex);
        }
        MemoryRelief.Soon();
    }

    static void Lay(FrameworkElement element)
    {
        element.Measure(new Size(1200, 900));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
    }
}
