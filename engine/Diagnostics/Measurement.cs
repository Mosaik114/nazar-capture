namespace MosaikCapture.Engine.Diagnostics;

/// <summary>
/// <c>--measure &lt;dir&gt;</c>: the measuring run of the redesign (baseline and budgets). Takes
/// <see cref="Captures"/> screenshots through the real overlay, controller and card, as a hotkey would, and
/// writes measure-report.json:
/// <list type="bullet">
/// <item>coldMs: process start to the first visible overlay (the first hotkey after logon);</item>
/// <item>warmMs: median of hotkey (<see cref="Screenshots.StartSoon"/>) to visible overlay in the running engine;</item>
/// <item>privateIdleMb, privateAfterMb, privateAfterGcMb: private memory before the first capture, after the last
/// capture (as the engine is left) and after a full garbage collection.</item>
/// </list>
/// The screenshots go to a folder below &lt;dir&gt; and are deleted right after saving; the clipboard is not
/// touched. Run it under test isolation (build.ps1 -Measure does), on a desktop where it may open the overlay.
/// </summary>
internal static class Measurement
{
    internal const int Captures = 25, Pins = 5;

    /// <summary>
    /// Captures of this run: 25 by default (comparable with the 1.1.2 baseline), more with <c>--captures n</c>;
    /// <c>--prestarted</c> first warms up like an engine pre-started after logon and measures the first overlay from the
    /// hotkey command instead of from the process start.
    /// </summary>
    static int captures = Captures;
    static bool prestarted;
    static int pins;   // 0 by default: comparable with the 1.1.2 baseline; --pins 5 for the budget run

    internal static void Options(string[] args)
    {
        int index = Array.IndexOf(args, "--captures");
        if (index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count >= 2) captures = count;
        prestarted = Array.IndexOf(args, "--prestarted") >= 0;
        int pinIndex = Array.IndexOf(args, "--pins");
        if (pinIndex >= 0 && pinIndex + 1 < args.Length && int.TryParse(args[pinIndex + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int pinCount)) pins = pinCount;
    }
    static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(20);

    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        string shots = Path.Combine(directory, "shots");
        Directory.CreateDirectory(shots);
        string ini = Path.Combine(directory, "measure.ini");
        File.WriteAllText(ini, "[capture]\r\nscreenshot_folder=" + shots + "\r\n", Encoding.Unicode);
        CaptureSettings.UseFile(ini);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        bool substitute = UseSubstituteIfScreenCannotBeCopied();
        var store = new MeasuredStore();
        Screenshots.Use(new ScreenshotController(new WpfScreenshotUi(), store));

        void Fail(Exception error)
        {
            Write(directory, "{\"passed\":false,\"error\":\"" + Json(error.Message) + "\"}");
            Environment.ExitCode = 1;
            app.Shutdown();
        }

        async void Measure()
        {
            try
            {
                if (prestarted)
                {
                    WarmUp.Run();
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    MemoryRelief.CollectNow();
                }
                double idle = PrivateMegabytes();
                var warm = new List<double>();
                double cold = 0;
                BitmapSource? lastImage = null;
                for (int i = 0; i < captures; i++)
                {
                    var watch = Stopwatch.StartNew();
                    var overlay = await Reveal();
                    double milliseconds = watch.Elapsed.TotalMilliseconds;
                    if (i == 0) cold = prestarted ? milliseconds : (DateTime.Now - ProcessStart()).TotalMilliseconds;
                    else warm.Add(milliseconds);
                    var saved = store.NextDelivery();
                    overlay.Press(Key.Space);   // whole monitor, as with the Space key
                    await WithTimeout(saved, "the screenshot was not saved");
                    lastImage = store.LastImage;
                    await Task.Delay(50);
                }
                // Pins of a typical size (a dialog, a chart): each holds its own copy, never the frozen desktop.
                for (int i = 0; i < pins && lastImage != null; i++)
                {
                    var crop = new Int32Rect(0, 0, Math.Min(800, lastImage.PixelWidth), Math.Min(500, lastImage.PixelHeight));
                    PinnedScreenshot.Open(Screenshots.CopyOf(lastImage, crop), 40 * i, 40 * i, "pin.png");
                }
                await Task.Delay(TimeSpan.FromSeconds(2));   // let the card and the encoder settle
                double after = PrivateMegabytes();
                MemoryRelief.CollectNow();
                double afterGc = PrivateMegabytes();
                PinnedScreenshot.CloseAll();
                warm.Sort();
                Write(directory, "{\"passed\":true,\"captures\":" + captures + ",\"pins\":" + pins + ",\"prestarted\":" + (prestarted ? "true" : "false") +
                    ",\"coldMs\":" + Number(cold) +
                    ",\"warmMs\":" + Number(warm[warm.Count / 2]) +
                    ",\"warmMaxMs\":" + Number(warm[warm.Count - 1]) +
                    ",\"privateIdleMb\":" + Number(idle) +
                    ",\"privateAfterMb\":" + Number(after) +
                    ",\"privateAfterGcMb\":" + Number(afterGc) +
                    ",\"managedMb\":" + Number(GC.GetTotalMemory(true) / 1048576.0) +
                    ",\"screenCopy\":\"" + (substitute ? "substitute" : "gdi") + "\"" +
                    ",\"screen\":\"" + ScreenGrabber.VirtualScreen().Width + "x" + ScreenGrabber.VirtualScreen().Height + "\"}");
                app.Shutdown();
            }
            catch (Exception error) { Fail(error); }   // any failure fails the run
        }

        app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Fail(e.Exception); };
        app.Dispatcher.BeginInvoke(new Action(Measure), DispatcherPriority.ApplicationIdle);
        app.Run();
    }

    /// <summary>
    /// An invisible desktop (where the tests run) cannot be copied: GDI refuses. Then every capture gets a
    /// generated image of the full screen size instead, so everything after the copy is measured the same way;
    /// the report says "screenCopy":"substitute", and such runs are only compared with each other.
    /// </summary>
    static bool UseSubstituteIfScreenCannotBeCopied()
    {
        try
        {
            ScreenGrabber.Capture(0, 0, 1, 1, 96);
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            ScreenGrabber.Substitute = (_, _, width, height, dpi) =>
            {
                var pixels = new byte[width * height * 4];
                for (int i = 0; i < pixels.Length; i += 4) pixels[i] = pixels[i + 1] = pixels[i + 2] = (byte)(i / 4 % width * 255 / width);
                var image = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgr32, null, pixels, width * 4);
                image.Freeze();
                return image;
            };
            return true;
        }
    }

    /// <summary>Triggers a screenshot like the hotkey does and waits until the overlay is visible.</summary>
    static Task<SelectionOverlay> Reveal()
    {
        var revealed = new TaskCompletionSource<SelectionOverlay>();
        void Handler(SelectionOverlay overlay) => revealed.TrySetResult(overlay);
        SelectionOverlay.Revealed += Handler;
        _ = revealed.Task.ContinueWith(_ => SelectionOverlay.Revealed -= Handler, TaskScheduler.FromCurrentSynchronizationContext());
        Screenshots.Start();   // like the hotkey: no pause (the pause is only for the tray menu)
        return WithTimeout(revealed.Task, "the overlay did not appear");
    }

    static async Task<T> WithTimeout<T>(Task<T> task, string failure)
    {
        if (await Task.WhenAny(task, Task.Delay(StepTimeout)) != task) throw new TimeoutException(failure);
        return await task;
    }

    static DateTime ProcessStart()
    {
        using var self = Process.GetCurrentProcess();
        return self.StartTime;
    }

    static double PrivateMegabytes()
    {
        using var self = Process.GetCurrentProcess();
        return self.PrivateMemorySize64 / 1048576.0;
    }

    static string Number(double value) => Math.Round(value, 1).ToString(CultureInfo.InvariantCulture);

    static void Write(string directory, string json) => File.WriteAllText(Path.Combine(directory, "measure-report.json"), json, Encoding.UTF8);

    static string Json(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");

    /// <summary>The real PNG encoding and file write, then the file is removed again; no clipboard.</summary>
    sealed class MeasuredStore : IScreenshotStore
    {
        readonly ScreenshotStore real = new();
        TaskCompletionSource<bool> delivered = new();

        public Task<bool> NextDelivery()
        {
            delivered = new TaskCompletionSource<bool>();
            return delivered.Task;
        }

        public string NewPath(DateTime when) => real.NewPath(when);

        public async Task<byte[]> SaveAsync(BitmapSource image, string path)
        {
            byte[] png = await real.SaveAsync(image, path);
            File.Delete(path);
            return png;
        }

        /// <summary>The image of the latest capture (the pins of the run show it).</summary>
        public BitmapSource? LastImage { get; private set; }

        public Task<bool> CopyAsync(BitmapSource image, byte[] png)
        {
            LastImage = image;
            delivered.TrySetResult(true);
            return Task.FromResult(true);
        }
    }
}
