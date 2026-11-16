using System.Windows.Controls;

namespace NazarCapture.Engine.Tests.Pixel;

/// <summary>
/// The safety net of the redesign: every floating surface in both themes (light and dark) and at two display
/// scales (100 % and 150 %), compared with References/&lt;name&gt;.&lt;theme&gt;.&lt;scale&gt;.png. The references hold the
/// look of the windows as they are; a change of the windows must show up here as a reviewed new reference.
/// <see cref="PixelTests"/> keeps its detailed views (double resolution, the full editor scene).
/// </summary>
[Trait("Category", "Pixel")]
public sealed class PixelMatrixTests : IDisposable
{
    static readonly RECT WorkArea = new() { left = 0, top = 0, right = 1920, bottom = 1040 };
    readonly bool? palette = Palette.Force;

    public void Dispose() => Palette.Force = palette;

    /// <summary>Theme (true = light) and display scale.</summary>
    public static TheoryData<bool, double> Variants => new()
    {
        { true, 1.0 },
        { true, 1.5 },
        { false, 1.0 },
        { false, 1.5 },
    };

    static string Name(string surface, bool light, double scale) =>
        surface + "." + (light ? "light" : "dark") + "." + ((int)Math.Round(scale * 100)).ToString(CultureInfo.InvariantCulture);

    static void Lay(FrameworkElement e)
    {
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        e.Arrange(new Rect(e.DesiredSize));
        e.UpdateLayout();
    }

    /// <summary>The element at the given display scale on a calm gradient (shows transparency and shadows).</summary>
    internal static BitmapSource Draw(FrameworkElement e, double scale)
    {
        Lay(e);
        WpfThread.Pump(50);
        Lay(e);
        double w = Math.Ceiling(e.DesiredSize.Width), h = Math.Ceiling(e.DesiredSize.Height);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x5A, 0x62, 0x6E), Color.FromRgb(0x9A, 0x8C, 0x7E), 30), null, new Rect(0, 0, w, h));
        bitmap.Render(background);
        bitmap.Render(e);
        bitmap.Freeze();
        return bitmap;
    }

    static BitmapSource Thumbnail()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x2B, 0x3A, 0x55), Color.FromRgb(0x2A, 0xC8, 0xA6), 20), null, new Rect(0, 0, 960, 540));
            dc.DrawRectangle(Brushes.White, null, new Rect(80, 80, 400, 260));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), null, new Point(700, 300), 120, 120);
        }
        var bitmap = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(dv);
        bitmap.Freeze();
        return bitmap;
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void EditorToolbarAndMenu(bool light, double scale) => WpfThread.Run(() =>
    {
        PixelComparison.RequireFonts();
        Palette.Force = light;
        var check = new PixelComparison();
        var editor = new ScreenshotEditor((_, _) => { }, _ => { }, (_, _) => { });
        var root = editor.Surface;
        root.Width = 1600;
        root.Height = 900;
        Lay(root);
        var shot = Thumbnail();
        editor.Load(new ScreenCapture
        {
            FullScreen = shot,
            Image = shot,
            Scale = 1,
            VirtualWidth = shot.PixelWidth,
            VirtualHeight = shot.PixelHeight,
            Crop = new Int32Rect(0, 0, shot.PixelWidth, shot.PixelHeight),
            Monitor = new RECT { left = 0, top = 0, right = shot.PixelWidth, bottom = shot.PixelHeight },
        });
        Lay(root);
        editor.SetTool(ScreenshotEditor.Tool.Box);
        editor.OpenGroupMenu(2);
        Lay(root);
        var bar = editor.Toolbar;
        var menu = editor.ToolMenu;
        Rect area = Rect.Union(new Rect(Canvas.GetLeft(bar), Canvas.GetTop(bar), bar.ActualWidth, bar.ActualHeight),
                               new Rect(Canvas.GetLeft(menu), Canvas.GetTop(menu), menu.ActualWidth, menu.ActualHeight));
        area.Inflate(16, 16);
        check.Check(Name("editor_toolbar_menu", light, scale), Cutout(root, area, scale));
        editor.CloseMenu();
        check.AssertAllMatch();
    });

    static BitmapSource Cutout(Visual visual, Rect area, double scale)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x5A, 0x62, 0x6E), Color.FromRgb(0x9A, 0x8C, 0x7E), 30), null, new Rect(0, 0, area.Width, area.Height));
            dc.PushTransform(new TranslateTransform(-area.X, -area.Y));
            dc.DrawRectangle(new VisualBrush(visual)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = area,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = area
            }, null, area);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(area.Width * scale), (int)Math.Ceiling(area.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(dv);
        bitmap.Freeze();
        return bitmap;
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void ScreenshotCards(bool light, double scale) => WpfThread.Run(() =>
    {
        PixelComparison.RequireFonts();
        Palette.Force = light;
        var check = new PixelComparison();
        var card = new ScreenshotCard(() => { }, () => { }, () => { }) { Offscreen = true };
        card.Present(new ScreenCapture { Image = Thumbnail(), Path = @"C:\Bilder\Nazar-Screenshot 2026-09-26 14-03-12.png", WorkArea = WorkArea }, "In Zwischenablage kopiert");
        card.Saved();
        card.SetBar(1);
        check.Check(Name("screenshot_card", light, scale), Draw((FrameworkElement)card.Content, scale));

        card = new ScreenshotCard(() => { }, () => { }, () => { }) { Offscreen = true };
        card.ShowProblem("Screenshot fehlgeschlagen", "Der Bildschirm lässt sich gerade nicht lesen (z. B. gesperrt)", WorkArea);
        card.SetBar(0.4);
        check.Check(Name("screenshot_card_error", light, scale), Draw((FrameworkElement)card.Content, scale));
        check.AssertAllMatch();
    });

    [Theory]
    [MemberData(nameof(Variants))]
    public void RecordingCards(bool light, double scale) => WpfThread.Run(() =>
    {
        PixelComparison.RequireFonts();
        Palette.Force = light;
        var check = new PixelComparison();
        var card = new RecordingCard { Offscreen = true };
        card.Present(new RecordedVideo
        {
            Path = @"C:\Videos\Nazar-Aufnahme 2026-09-26 14-03-12.mp4",
            Duration = 42.4,
            Width = 1920,
            Height = 1080,
            Audio = true,
            Bytes = 18350080,
            Thumbnail = Thumbnail()
        }, "Aufnahme gespeichert", WorkArea);
        card.SetBar(1);
        check.Check(Name("recording_card", light, scale), Draw((FrameworkElement)card.Content, scale));

        card = new RecordingCard { Offscreen = true };
        card.Problem("Aufnahme fehlgeschlagen", "Dieser Monitor ist für die Aufnahme nicht erreichbar", WorkArea);
        card.SetBar(1);
        check.Check(Name("recording_card_error", light, scale), Draw((FrameworkElement)card.Content, scale));
        check.AssertAllMatch();
    });

    [Theory]
    [MemberData(nameof(Variants))]
    public void RecordingBarAndCountdown(bool light, double scale) => WpfThread.Run(() =>
    {
        PixelComparison.RequireFonts();
        Palette.Force = light;
        var check = new PixelComparison();
        bool mic = ScreenRecorder.Mic, sound = ScreenRecorder.Sound, clicks = ScreenRecorder.Clicks;
        try
        {
            var bar = new RecordingToolbar();
            ScreenRecorder.Mic = false;
            ScreenRecorder.Sound = true;
            ScreenRecorder.Clicks = true;
            bar.Refresh();
            bar.TimeText.Text = "0:42";
            check.Check(Name("recording_bar", light, scale), Draw((FrameworkElement)bar.Content, scale));
        }
        finally
        {
            ScreenRecorder.Mic = mic;
            ScreenRecorder.Sound = sound;
            ScreenRecorder.Clicks = clicks;
        }

        var countdown = new CountdownWindow();
        countdown.ShowFrame(2, 0.6);
        WpfThread.Pump(400);   // wait for the number to fade in
        check.Check(Name("recording_countdown", light, scale), Draw((FrameworkElement)countdown.Content, scale));
        check.AssertAllMatch();
    });
}
