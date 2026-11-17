// Renders the windows in every interface language (light, 100 %) for a visual check of the translations, the writing
// direction of Arabic, the fonts and long texts. Compiled into the test project only with -p:NazarReadme=true, as the
// README renders, and runs only with NAZAR_LANGUAGE_OUT set: <out>/<code>/<window>.png.
using System.Windows.Controls;
using NazarCapture.Engine.Settings;

namespace NazarCapture.Engine.Tests;

public sealed class LanguageRenders
{
    static string Out => Environment.GetEnvironmentVariable("NAZAR_LANGUAGE_OUT") ?? "";

    const string Ini = "[capture]\r\nscreenshot_hotkey=Shift+Alt+4\r\nrecording_hotkey=Shift+Alt+5\r\nscreenshot_folder=D:\\Bilder\\Nazar-Screenshots\r\n" +
        "recording_folder=D:\\Videos\\Nazar-Aufnahmen\r\nmicrophone=1\r\nsystem_audio=1\r\nshow_clicks=1\r\nautostart=1\r\nfps=60\r\ncountdown=3\r\nappearance=1\r\n";

    static void Save(BitmapSource image, string folder, string name)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(folder, name));
        encoder.Save(stream);
    }

    static BitmapSource Draw(FrameworkElement e, double? width = null, double? height = null)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            e.Measure(new Size(width ?? double.PositiveInfinity, height ?? double.PositiveInfinity));
            e.Arrange(new Rect(0, 0, width ?? e.DesiredSize.Width, height ?? e.DesiredSize.Height));
            e.UpdateLayout();
            WpfThread.Pump(60);
        }
        double w = Math.Ceiling(width ?? e.DesiredSize.Width), h = Math.Ceiling(height ?? e.DesiredSize.Height);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD2)), null, new Rect(0, 0, w, h));
        var bitmap = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(background);
        bitmap.Render(e);
        bitmap.Freeze();
        return bitmap;
    }

    static BitmapSource Picture()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x2B, 0x3A, 0x55), Color.FromRgb(0x2A, 0xC8, 0xA6), 20), null, new Rect(0, 0, 960, 540));
            dc.DrawRectangle(Brushes.White, null, new Rect(80, 80, 400, 260));
            // an "R" that shows whether the picture was mirrored
            dc.DrawText(new FormattedText("R →", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 140, Brushes.Black, 1), new Point(110, 120));
        }
        var bitmap = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(dv);
        bitmap.Freeze();
        return bitmap;
    }

    [Fact]
    public void Render() => WpfThread.Run(() =>
    {
        Assert.SkipWhen(Out.Length == 0, "NAZAR_LANGUAGE_OUT not set");
        var keepPalette = Palette.Force;
        Palette.Force = true;
        try
        {
            foreach (var language in UiLanguages.All)
            {
                using var scope = new LanguageScope(language);
                string folder = Path.Combine(Out, language.Code);
                Directory.CreateDirectory(folder);
                RenderLanguage(folder, language.Code);
            }
        }
        finally { Palette.Force = keepPalette; }
    });

    static void RenderLanguage(string folder, string code)
    {
        using var settings = new TestSettings(Ini + "language=" + code + "\r\n");
        var window = new SettingsWindow(new NazarUi.SettingsWindowTests.FakeHost(), settings.Load, pending: () => new Updates.Release("1.1.7", "p", null, null));
        MuiTheme.Apply(window.Resources, true);
        var content = (FrameworkElement)window.Content;
        for (int i = 0; i < window.Pages.Children.Count; i++)
        {
            window.SelectPage(i);
            Save(Draw(content, SettingsWindow.BaseWidth, 760), folder, "settings-" + i + ".png");
        }
        window.Close();

        var main = new MainWindow(settings.Load, () => false);
        MuiTheme.Apply(main.Resources, true);
        Save(Draw((FrameworkElement)main.Content, MainWindow.WindowWidth), folder, "main-window.png");
        main.Close();

        var card = new ScreenshotCard(() => { }, () => { }, () => { }) { Offscreen = true };
        card.Present(new ScreenCapture { Image = Picture(), Path = @"D:\Bilder\Nazar-Screenshots\Nazar-Screenshot 2026-11-17 14-03-12.png", WorkArea = new RECT { right = 1920, bottom = 1040 } }, UiText.Common.CopiedToClipboard);
        card.Saved();
        card.SetBar(0.6);
        Save(Draw((FrameworkElement)card.Content), folder, "screenshot-card.png");
        card.Close();

        var recording = new RecordingCard { Offscreen = true };
        recording.Present(new RecordedVideo { Path = @"D:\Videos\Nazar-Aufnahmen\Nazar-Aufnahme 2026-11-17 14-03-12.mp4", Duration = 42, Bytes = 3_400_000, Thumbnail = Picture() },
            UiText.Recording.Saved, new RECT { right = 1920, bottom = 1040 });
        recording.SetBar(0.6);
        Save(Draw((FrameworkElement)recording.Content), folder, "recording-card.png");
        recording.Close();

        var update = new Updates.UpdateCard(new Updates.UpdateCheck(Path.Combine(folder, "state.json"), () => null, () => 1, "1.1.6")) { Offscreen = true };
        update.Present(new Updates.Release("1.1.7", "p", null, null), new RECT { right = 1920, bottom = 1040 });
        Save(Draw((FrameworkElement)update.Content), folder, "update-card.png");
        update.Close();
        File.Delete(Path.Combine(folder, "state.json"));

        var editor = new ScreenshotEditor((_, _) => { }, _ => { }, (_, _) => { });
        var root = editor.Surface;
        root.Width = 1200;
        root.Height = 700;
        var shot = Picture();
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
        editor.SetTool(ScreenshotEditor.Tool.Box);
        editor.OpenGroupMenu(2);
        Save(Draw(root, 1200, 700), folder, "editor.png");
        editor.CloseMenu();
        editor.Close();

        var trim = new TrimWindow();
        MuiTheme.Apply(trim.Resources, true);
        Save(Draw((FrameworkElement)trim.Content), folder, "trim-window.png");
        trim.Retire();
    }
}
