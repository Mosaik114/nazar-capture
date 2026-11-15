using System.Windows.Controls;
using MosaikCapture.Engine.Tests.Pixel;

namespace MosaikCapture.Engine.Tests.MosaikUi;

/// <summary>Switching between light and dark at run time, and the "appearance" setting.</summary>
public sealed class ThemeTests : IDisposable
{
    static readonly RECT WorkArea = new() { left = 0, top = 0, right = 1920, bottom = 1040 };
    readonly bool? palette = Palette.Force;

    public void Dispose() => Palette.Force = palette;

    static Color Background(Border border) => ((SolidColorBrush)border.Background).Color;

    [Fact]
    public void SwitchingThemesLeavesNoColorBehind() => WpfThread.Run(() =>
    {
        var border = new Border();
        border.SetResourceReference(Border.BackgroundProperty, UiTokens.Keys.WindowBg);
        var legacy = new TextBlock();
        legacy.SetResourceReference(TextBlock.ForegroundProperty, Palette.Text);
        var root = new StackPanel { Children = { border, legacy } };

        Palette.Apply(root.Resources, true);
        Assert.Equal(((SolidColorBrush)MuiTheme.Light[UiTokens.Keys.WindowBg]).Color, Background(border));
        Palette.Apply(root.Resources, false);
        Assert.Equal(((SolidColorBrush)MuiTheme.Dark[UiTokens.Keys.WindowBg]).Color, Background(border));
        Assert.Equal(((SolidColorBrush)MuiTheme.Dark[Palette.Text]).Color, ((SolidColorBrush)legacy.Foreground).Color);
        Palette.Apply(root.Resources, true);
        Assert.Equal(((SolidColorBrush)MuiTheme.Light[UiTokens.Keys.WindowBg]).Color, Background(border));
        Assert.Equal(((SolidColorBrush)MuiTheme.Light[Palette.Text]).Color, ((SolidColorBrush)legacy.Foreground).Color);
        Assert.Single(root.Resources.MergedDictionaries);   // swapped, not stacked
    });

    [Fact]
    public void ApplyingTheSameThemeTwiceKeepsOneDictionary() => WpfThread.Run(() =>
    {
        var resources = new ResourceDictionary();
        var own = new ResourceDictionary();
        resources.MergedDictionaries.Add(own);
        Palette.Apply(resources, false);
        Palette.Apply(resources, false);
        Assert.Equal(2, resources.MergedDictionaries.Count);
        Assert.Same(own, resources.MergedDictionaries[0]);
        Assert.Same(MuiTheme.Dark, resources.MergedDictionaries[1]);
    });

    /// <summary>Light, then dark, then light again on the same card: the same pixels as a card built in that theme.</summary>
    [Fact]
    [Trait("Category", "Pixel")]
    public void ACardSwitchedLightDarkLightLooksLikeAFreshOne() => WpfThread.Run(() =>
    {
        PixelComparison.RequireFonts();
        var check = new PixelComparison();
        var card = new ScreenshotCard(() => { }, () => { }, () => { }) { Offscreen = true };
        var capture = new ScreenCapture { Image = Thumbnail(), Path = @"C:\Bilder\Mosaiks-Screenshot 2026-09-26 14-03-12.png", WorkArea = WorkArea };
        foreach (bool light in new[] { true, false, true })
        {
            Palette.Force = light;
            card.Present(capture, "In Zwischenablage kopiert");
            card.Saved();
            card.SetBar(1);
            check.Check("screenshot_card." + (light ? "light" : "dark") + ".100", PixelMatrixTests.Draw((FrameworkElement)card.Content, 1));
        }
        check.AssertAllMatch();
    });

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

    [Fact]
    public void AppearanceChoosesThePalette()
    {
        Assert.True(MuiTheme.IsLight(Appearance.Light, () => false));
        Assert.False(MuiTheme.IsLight(Appearance.Dark, () => true));
        Assert.True(MuiTheme.IsLight(Appearance.System, () => true));
        Assert.False(MuiTheme.IsLight(Appearance.System, () => false));
    }

    [Fact]
    public void AppearanceIsReadFromTheSettingsAndNormalized()
    {
        Assert.Equal(Appearance.Light, CaptureSettings.NormalizeAppearance(1));
        Assert.Equal(Appearance.Dark, CaptureSettings.NormalizeAppearance(2));
        foreach (int value in new[] { 0, 3, -1, 99 }) Assert.Equal(Appearance.System, CaptureSettings.NormalizeAppearance(value));
        using var settings = new TestSettings("[capture]\r\nappearance=2\r\n");
        Assert.Equal(Appearance.Dark, settings.Load().Appearance);
    }
}
