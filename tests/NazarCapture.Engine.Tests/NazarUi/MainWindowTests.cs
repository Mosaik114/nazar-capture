using NazarCapture.Engine.Settings;

namespace NazarCapture.Engine.Tests.NazarUi;

/// <summary>
/// The main window as Hams': head with mark, name, gear and cross; the status line; "Screenshot" as the one main action
/// and "Aufnahme" beside it; both hotkeys as key caps. Same width and margins as Hams', position remembered.
/// </summary>
public sealed class MainWindowTests
{
    static MainWindow Window(TestSettings settings, bool recording = false, WindowPlaces? places = null, bool light = true)
    {
        var window = new MainWindow(settings.Load, () => recording, places);
        MuiTheme.Apply(window.Resources, light);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(MainWindow.WindowWidth, double.PositiveInfinity));
        content.Arrange(new Rect(0, 0, MainWindow.WindowWidth, content.DesiredSize.Height));
        content.UpdateLayout();
        return window;
    }

    [Fact]
    public void TheZonesAreHams() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(SettingsWindowTests.Version112.Replace("Shift+Alt+5", "Ctrl+Shift+R"));
        var window = Window(settings);
        Assert.Equal(MainWindow.WindowWidth, window.Width);
        Assert.Equal(LineIcon.Shape.Settings, ((LineIcon)window.Gear.Content).Kind);
        Assert.Equal(LineIcon.Shape.Close, ((LineIcon)window.CloseButton.Content).Kind);
        Assert.Equal(IconButton.StoneSize, window.Gear.ActualWidth);
        Assert.Equal(MuiRole.Primary, Mui.GetRole(window.ScreenshotButton));
        Assert.Equal(MuiRole.Secondary, Mui.GetRole(window.RecordButton));
        Assert.Single(SettingsWindowTests.Descendants<PrimaryButton>((DependencyObject)window.Content));   // one main action
        Assert.Equal(window.ScreenshotButton.ActualHeight, window.RecordButton.ActualHeight);
        Assert.Equal("Shift+Alt+4", window.ScreenshotKeys.Combo);
        Assert.Equal("Ctrl+Shift+R", window.RecordingKeys.Combo);
        Assert.Equal(UiText.Main.Ready, window.StatusText);
        var content = (FrameworkElement)window.Content;
        Assert.InRange(content.DesiredSize.Height, 200, 260);   // Hams' main window is about 250 high
        window.Close();
    });

    [Fact]
    public void ARunningRecordingShowsInTheStatusAndTheButton() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(SettingsWindowTests.Version112);
        var window = Window(settings, recording: true);
        Assert.Equal(UiText.Main.RecordingRunning, window.StatusText);
        Assert.Equal(UiText.Main.StopRecording, System.Windows.Automation.AutomationProperties.GetName(window.RecordButton));
        window.Close();
    });

    [Fact]
    public void TheGearAndTheCrossAreRoundStonesWithAHoverColour() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(SettingsWindowTests.Version112);
        var window = Window(settings);
        Assert.False(Mui.GetSquircle(window.CloseButton));
        Assert.Equal(UiTokens.Keys.TextSecondary, window.CloseButton.IconInk);
        Assert.Equal(UiTokens.Keys.TextSecondary, window.Gear.IconInk);
        window.Close();
    });

    [Fact]
    public void TheGearOutlineIsHams()
    {
        var outline = LineIcon.GearOutline(8, 8);
        double Radius(Point p) => Math.Sqrt((p.X - 8) * (p.X - 8) + (p.Y - 8) * (p.Y - 8));
        Assert.Equal(6.0, outline.Max(Radius), 3);
        Assert.Equal(4.3, outline.Min(Radius), 3);
        Assert.Equal(6.0, Radius(outline[270]), 3);   // a tooth stands on top
    }

    [Theory]
    [MemberData(nameof(Pixel.PixelMatrixTests.Variants), MemberType = typeof(Pixel.PixelMatrixTests))]
    [Trait("Category", "Pixel")]
    public void MainWindowLooks(bool light, double scale) => WpfThread.Run(() =>
    {
        PixelComparison.RequireFonts();
        using var settings = new TestSettings(SettingsWindowTests.Version112);
        var window = new MainWindow(settings.Load, () => false);
        MuiTheme.Apply(window.Resources, light);
        var content = (FrameworkElement)window.Content;
        content.Width = MainWindow.WindowWidth;
        var check = new PixelComparison();
        check.Check("main_window." + (light ? "light" : "dark") + "." + ((int)Math.Round(scale * 100)).ToString(CultureInfo.InvariantCulture),
            Pixel.PixelMatrixTests.Draw(content, scale));
        check.AssertAllMatch();
        window.Close();
    });
}
