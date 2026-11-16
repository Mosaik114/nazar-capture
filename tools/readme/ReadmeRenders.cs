// Renders the raw pictures for the README (docs/images) with the app's own drawing code, at 200 %.
// Compiled into the test project only with -p:NazarReadme=true and runs only with NAZAR_README_OUT set;
// tools/readme/render.ps1 does both and then composes the pictures with the Python scripts next to this file.
using System.Reflection;
using System.Windows.Controls;
using NazarCapture.Engine.Settings;

namespace NazarCapture.Engine.Tests;

public sealed class ReadmeRenders
{
    static string Out => Environment.GetEnvironmentVariable("NAZAR_README_OUT") ?? "";
    const double S = 2.0;                       // device pixels per WPF unit
    const int W = 1280, H = 800;                // the mock screen in WPF units

    static void Save(BitmapSource image, string name)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(Out, name));
        encoder.Save(stream);
    }

    static void Lay(FrameworkElement e, double? width = null, double? height = null)
    {
        e.Measure(new Size(width ?? double.PositiveInfinity, height ?? double.PositiveInfinity));
        e.Arrange(new Rect(0, 0, width ?? e.DesiredSize.Width, height ?? e.DesiredSize.Height));
        e.UpdateLayout();
    }

    /// <summary>The element at 2x on a transparent background.</summary>
    static BitmapSource Draw(FrameworkElement e, double? width = null, double? height = null)
    {
        Lay(e, width, height);
        WpfThread.Pump(80);
        Lay(e, width, height);
        double w = Math.Ceiling(width ?? e.DesiredSize.Width), h = Math.Ceiling(height ?? e.DesiredSize.Height);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(w * S), (int)Math.Ceiling(h * S), 96 * S, 96 * S, PixelFormats.Pbgra32);
        bitmap.Render(e);
        bitmap.Freeze();
        return bitmap;
    }

    static readonly Typeface Regular = new(new FontFamily("Segoe UI Variable Text"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    static readonly Typeface Semi = new(new FontFamily("Segoe UI Variable Text"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly Typeface Bold = new(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    static SolidColorBrush B(uint rgb) => new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    static FormattedText T(string s, double size, Brush brush, Typeface? face = null) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face ?? Regular, size, brush, S);

    /// <summary>Window of the mock app (in WPF units on the mock screen).</summary>
    internal static readonly Rect AppWindow = new(300, 70, 640, 500);

    /// <summary>A plain light desktop with one window of a made-up app ("Account settings" of a web page).</summary>
    static BitmapSource MockScreen()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0xDD, 0xE6, 0xF0), Color.FromRgb(0xEF, 0xE9, 0xDE), 35), null, new Rect(0, 0, W, H));
            // taskbar
            dc.DrawRectangle(B(0xF3F3F3), null, new Rect(0, H - 40, W, 40));
            dc.DrawRectangle(B(0xE0E0E0), null, new Rect(0, H - 40, W, 1));
            for (int i = 0; i < 5; i++)
                dc.DrawRoundedRectangle(i == 2 ? B(0x2F6FDB) : B(0xC9CED6), null, new Rect(W / 2 - 110 + i * 46, H - 30, 22, 22), 5, 5);
            dc.DrawText(T("10:24", 11, B(0x333333)), new Point(W - 52, H - 27));

            var a = AppWindow;
            dc.PushTransform(new TranslateTransform(0, 6));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0)), null, Inflate(a, 2), 10, 10);
            dc.Pop();
            dc.DrawRoundedRectangle(Brushes.White, new Pen(B(0xD5D9DF), 1), a, 8, 8);
            // title bar
            dc.DrawRoundedRectangle(B(0xF1F3F6), null, new Rect(a.X, a.Y, a.Width, 36), 8, 8);
            dc.DrawRectangle(B(0xF1F3F6), null, new Rect(a.X, a.Y + 20, a.Width, 16));
            dc.DrawRectangle(B(0xE1E4E9), null, new Rect(a.X, a.Y + 36, a.Width, 1));
            dc.DrawRoundedRectangle(B(0x2F6FDB), null, new Rect(a.X + 14, a.Y + 11, 14, 14), 4, 4);
            dc.DrawText(T("Northwind · Account", 12, B(0x3A4250), Semi), new Point(a.X + 36, a.Y + 10));
            foreach (var (x, glyph) in new[] { (a.Right - 96, "—"), (a.Right - 62, "☐"), (a.Right - 30, "✕") })
                dc.DrawText(T(glyph, 11, B(0x59606C)), new Point(x, a.Y + 10));
            // side navigation
            double ny = a.Y + 60;
            foreach (var (item, on) in new[] { ("Profile", true), ("Billing", false), ("Team", false), ("Security", false), ("Notifications", false) })
            {
                if (on) dc.DrawRoundedRectangle(B(0xE8EEFB), null, new Rect(a.X + 12, ny - 6, 132, 28), 6, 6);
                dc.DrawText(T(item, 13, on ? B(0x1F4FB0) : B(0x4A5160), on ? Semi : Regular), new Point(a.X + 26, ny));
                ny += 34;
            }
            dc.DrawRectangle(B(0xE6E8EC), null, new Rect(a.X + 158, a.Y + 37, 1, a.Height - 37));
            // content
            double x0 = a.X + 184, y = a.Y + 58;
            dc.DrawText(T("Account settings", 22, B(0x1B2230), Bold), new Point(x0, y));
            dc.DrawText(T("How others see you and how we reach you.", 13, B(0x6A7180)), new Point(x0, y + 34));
            y += 74;
            void Field(string label, string value, bool dropdown = false)
            {
                dc.DrawText(T(label, 12, B(0x4A5160), Semi), new Point(x0, y));
                var box = new Rect(x0, y + 20, 370, 34);
                dc.DrawRoundedRectangle(Brushes.White, new Pen(B(0xC9CED6), 1), box, 6, 6);
                dc.DrawText(T(value, 14, B(0x1B2230)), new Point(box.X + 12, box.Y + 8));
                if (dropdown) dc.DrawText(T("⌄", 16, B(0x59606C)), new Point(box.Right - 24, box.Y + 3));
                y += 70;
            }
            Field("Display name", "Alex Morgan");
            Field("Email", "alex.morgan@northwind.example");
            Field("Plan", "Team · 5 seats", dropdown: true);
            dc.DrawText(T("Changes to your plan apply from the next billing month.", 13, B(0x3A4250)), new Point(x0, y - 2));
            y += 34;
            var cancel = new Rect(x0 + 138, y, 96, 36);
            dc.DrawRoundedRectangle(Brushes.White, new Pen(B(0xC9CED6), 1), cancel, 6, 6);
            dc.DrawText(T("Cancel", 14, B(0x1B2230), Semi), new Point(cancel.X + 24, cancel.Y + 8));
            var save = SaveButton;
            dc.DrawRoundedRectangle(B(0x2F6FDB), null, save, 6, 6);
            dc.DrawText(T("Save changes", 14, Brushes.White, Semi), new Point(save.X + 18, save.Y + 8));
        }
        var bitmap = new RenderTargetBitmap((int)(W * S), (int)(H * S), 96 * S, 96 * S, PixelFormats.Pbgra32);
        bitmap.Render(dv);
        bitmap.Freeze();
        return bitmap;
    }

    static Rect SaveButton => new(AppWindow.X + 184 + 242, AppWindow.Y + 58 + 74 + 210 + 34, 128, 36);

    static Rect Inflate(Rect r, double d) { r.Inflate(d, d); return r; }

    static ScreenCapture Capture(BitmapSource screen, Rect crop) => new()
    {
        FullScreen = screen,
        Image = Screenshots.CopyOf(screen, Pixels(crop)),
        Scale = S,
        VirtualWidth = screen.PixelWidth,
        VirtualHeight = screen.PixelHeight,
        Crop = Pixels(crop),
        Monitor = new RECT { left = 0, top = 0, right = screen.PixelWidth, bottom = screen.PixelHeight },
        WorkArea = new RECT { left = 0, top = 0, right = screen.PixelWidth, bottom = screen.PixelHeight - (int)(40 * S) },
        Path = @"C:\Bilder\Nazar-Screenshot 2026-11-16 10-24-31.png",
    };

    static Int32Rect Pixels(Rect r) => new((int)(r.X * S), (int)(r.Y * S), (int)(r.Width * S), (int)(r.Height * S));

    static void Stroke(ScreenshotEditor editor, ScreenshotEditor.Tool tool, List<Point> points)
    {
        editor.SetTool(tool);
        editor.PointerDown(points[0]);
        for (int i = 1; i < points.Count; i++) editor.PointerMoved(points[i]);
        editor.PointerUp();
    }

    static void SetField(object o, string name, object? value) =>
        o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);

    static void Call(object o, string name, params object[] args) =>
        o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, args.Select(a => a.GetType()).ToArray(), null)!.Invoke(o, args);

    [Fact]
    public void Render() => WpfThread.Run(() =>
    {
        Assert.SkipWhen(Out.Length == 0, "NAZAR_README_OUT not set");
        Directory.CreateDirectory(Out);
        var keepPalette = Palette.Force;
        Palette.Force = true;
        try
        {
            RenderWindows();
            var screen = MockScreen();
            Save(screen, "mock-screen.png");
            RenderEditor(screen);
            RenderOverlayFrames(screen);
            RenderCards(screen);
            RenderBits();
        }
        finally { Palette.Force = keepPalette; }
    });

    const string Ini = "[capture]\r\nscreenshot_hotkey=Shift+Alt+4\r\nrecording_hotkey=Shift+Alt+5\r\nscreenshot_folder=D:\\Bilder\\Nazar-Screenshots\r\n" +
        "recording_folder=D:\\Videos\\Nazar-Aufnahmen\r\nmicrophone=1\r\nsystem_audio=1\r\nshow_clicks=1\r\nautostart=1\r\nfps=60\r\ncountdown=3\r\nappearance=1\r\n";

    static void RenderWindows()
    {
        using var settings = new TestSettings(Ini);
        var main = new MainWindow(settings.Load, () => false);
        MuiTheme.Apply(main.Resources, true);
        var content = (FrameworkElement)main.Content;
        Save(Draw(content, MainWindow.WindowWidth), "main-window.png");
        main.Close();

        var window = new SettingsWindow(new NazarUi.SettingsWindowTests.FakeHost(), settings.Load, pending: () => null);
        MuiTheme.Apply(window.Resources, true);
        var page = (FrameworkElement)window.Content;
        for (int i = 0; i < 4; i++)
        {
            window.SelectPage(i);
            Save(Draw(page, SettingsWindow.BaseWidth, SettingsWindow.BaseHeight), "settings-" + i + ".png");
        }
        window.Close();
    }

    static void RenderEditor(BitmapSource screen)
    {
        var editor = new ScreenshotEditor((_, _) => { }, _ => { }, (_, _) => { });
        var root = editor.Surface;
        var crop = Inflate(AppWindow, 10);
        editor.Load(Capture(screen, crop));
        Lay(root, W, H);
        var a = AppWindow;
        double x0 = a.X + 184, y0 = a.Y + 58 + 74;
        editor.SetColor(0);
        editor.SetWidth(2);
        // numbered steps beside the fields
        editor.SetTool(ScreenshotEditor.Tool.Number);
        foreach (double fy in new[] { y0 + 37, y0 + 177 })
        {
            editor.PointerDown(new Point(x0 + 404, fy));
            editor.PointerUp();
        }
        editor.PointerDown(new Point(x0 + 404, SaveButton.Y + 18));
        editor.PointerUp();
        // pixelate the e-mail address
        Stroke(editor, ScreenshotEditor.Tool.Pixel, Strokes.Line(new Point(x0 + 8, y0 + 70 + 24), new Point(x0 + 236, y0 + 70 + 50), 5));
        // highlighter on the note
        editor.SetColor(2);
        Stroke(editor, ScreenshotEditor.Tool.Marker, Strokes.Line(new Point(x0 + 2, y0 + 210 - 2 + 10), new Point(x0 + 336, y0 + 210 - 2 + 11), 30));
        // a rough pen ellipse around the plan field, held still: becomes a clean ellipse
        editor.SetColor(1);
        var held = Strokes.Wobble(Strokes.Ellipse(new Point(x0 + 185, y0 + 177), 194, 28, 0.12), 2.0, 11);
        editor.SetTool(ScreenshotEditor.Tool.Pen);
        editor.PointerDown(held[0]);
        for (int i = 1; i < held.Count; i++) editor.PointerMoved(held[i]);
        editor.PenHeld();
        editor.PointerMoved(new Point(held[held.Count - 1].X + 8, held[held.Count - 1].Y));
        editor.PointerUp();
        // arrow to the save button, with a label at its tail
        editor.SetColor(0);
        Stroke(editor, ScreenshotEditor.Tool.Arrow, Strokes.Line(new Point(x0 + 112, SaveButton.Bottom + 52), new Point(SaveButton.X + 26, SaveButton.Bottom + 4), 6));
        editor.SetWidth(1);
        editor.SetTool(ScreenshotEditor.Tool.Label);
        editor.PointerDown(new Point(x0 - 2, SaveButton.Bottom + 38));
        editor.TypeText("Then save");
        Lay(root, W, H);
        editor.CommitText();
        editor.SetTool(ScreenshotEditor.Tool.Move);
        Lay(root, W, H);
        WpfThread.Pump(100);
        Lay(root, W, H);
        var bitmap = new RenderTargetBitmap((int)(W * S), (int)(H * S), 96 * S, 96 * S, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        Save(bitmap, "editor-full.png");
        Save(editor.Render(), "editor-result.png");
        var bar = editor.Toolbar;
        File.WriteAllText(Path.Combine(Out, "editor-layout.txt"),
            string.Join(",", Canvas.GetLeft(bar), Canvas.GetTop(bar), bar.ActualWidth, bar.ActualHeight, crop.X, crop.Y, crop.Width, crop.Height));
    }

    /// <summary>The selection overlay over the mock screen: hover, then a drag across the app window, frame by frame.</summary>
    static void RenderOverlayFrames(BitmapSource screen)
    {
        var overlay = new SelectionOverlay(_ => { });
        overlay.OpenOffscreen(screen, new Int32Rect(0, 0, screen.PixelWidth, screen.PixelHeight), new[] { new RECT { left = 0, top = 0, right = screen.PixelWidth, bottom = screen.PixelHeight } }, S);
        var root = (FrameworkElement)overlay.Content;
        var lines = new List<string>();
        int n = 0;
        void Frame(Point devicePoint)
        {
            SetField(overlay, "keyboardPointer", (Point?)devicePoint);
            Call(overlay, "PointerMoved", devicePoint);
            Lay(root, W, H);
            var bitmap = new RenderTargetBitmap((int)(W * S), (int)(H * S), 96 * S, 96 * S, PixelFormats.Pbgra32);
            bitmap.Render(root);
            bitmap.Freeze();
            string name = "overlay-" + n.ToString("D2", CultureInfo.InvariantCulture) + ".png";
            Save(bitmap, name);
            lines.Add(name + "," + devicePoint.X + "," + devicePoint.Y);
            n++;
        }
        var start = new Point(Math.Round((AppWindow.X - 6) * S), Math.Round((AppWindow.Y - 6) * S));
        var end = new Point(Math.Round((AppWindow.Right + 6) * S), Math.Round((AppWindow.Bottom + 6) * S));
        // the pointer glides to the start
        var from = new Point(start.X + 260, start.Y + 180);
        for (int i = 0; i <= 8; i++)
        {
            double t = Ease(i / 8.0);
            Frame(new Point(Math.Round(from.X + (start.X - from.X) * t), Math.Round(from.Y + (start.Y - from.Y) * t)));
        }
        // press and drag
        SetField(overlay, "pressedAt", start);
        SetField(overlay, "pressed", true);
        SetField(overlay, "dragging", true);
        for (int i = 1; i <= 16; i++)
        {
            double t = Ease(i / 16.0);
            Frame(new Point(Math.Round(start.X + (end.X - start.X) * t), Math.Round(start.Y + (end.Y - start.Y) * t)));
        }
        File.WriteAllLines(Path.Combine(Out, "overlay-frames.txt"), lines);
        overlay.Close();
    }

    static double Ease(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    static void RenderCards(BitmapSource screen)
    {
        var crop = new Rect(AppWindow.X - 6, AppWindow.Y - 6, AppWindow.Width + 13, AppWindow.Height + 13);
        foreach (var bar in new[] { 1.0, 0.85, 0.7, 0.55, 0.4 })
        {
            var card = new ScreenshotCard(() => { }, () => { }, () => { }) { Offscreen = true };
            card.Present(Capture(screen, crop), "In Zwischenablage kopiert");
            card.Saved();
            card.SetBar(bar);
            Save(Draw((FrameworkElement)card.Content), "card-" + ((int)(bar * 100)).ToString(CultureInfo.InvariantCulture) + ".png");
        }
    }

    static void RenderBits()
    {
        bool mic = ScreenRecorder.Mic, sound = ScreenRecorder.Sound, clicks = ScreenRecorder.Clicks;
        try
        {
            var bar = new RecordingToolbar();
            ScreenRecorder.Mic = true;
            ScreenRecorder.Sound = true;
            ScreenRecorder.Clicks = true;
            bar.Refresh();
            bar.TimeText.Text = "0:42";
            Save(Draw((FrameworkElement)bar.Content), "recording-bar.png");
        }
        finally
        {
            ScreenRecorder.Mic = mic;
            ScreenRecorder.Sound = sound;
            ScreenRecorder.Clicks = clicks;
        }

        // the hotkey as key caps on a floating label (as the overlay's hint draws them)
        var holder = new Border { Child = new FloatingLabel(new KeyCombo("Shift+Alt+4")) { Padding = new Thickness(12, 8, 12, 8) }, Padding = new Thickness(24) };
        MuiTheme.Apply(holder.Resources, true);
        Save(Draw(holder), "hotkey.png");

        // the settings banner (viewfinder) for the social preview
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            new ViewfinderMotif().Paint(dc, 620, 76, Color.FromRgb(0x31, 0x5C, 0x99), Color.FromRgb(0x33, 0x60, 0x9F));
        var banner = new RenderTargetBitmap((int)(620 * S), (int)(76 * S), 96 * S, 96 * S, PixelFormats.Pbgra32);
        banner.Render(visual);
        banner.Freeze();
        Save(banner, "banner.png");
    }
}
