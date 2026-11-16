using System.Windows.Controls;
using MosaikCapture.Engine.Settings;

namespace MosaikCapture.Engine.Tests.MosaikUi;

/// <summary>
/// Phase 6, end to end without a host process: the settings window reads settings.ini (here a file of version 1.1.2),
/// sends every change to the host at once, shows refusals in the row, records hotkeys with the host's hotkeys
/// suspended, and grows with the text scale.
/// </summary>
public sealed class SettingsWindowTests
{
    /// <summary>settings.ini as version 1.1.2 wrote it.</summary>
    const string Version112 =
        "[capture]\r\nscreenshot_hotkey=Shift+Alt+4\r\nrecording_hotkey=Shift+Alt+5\r\nscreenshot_folder=C:\\Bilder\\Shots\r\n" +
        "recording_folder=C:\\Videos\\Takes\r\nmicrophone=1\r\nsystem_audio=0\r\nshow_clicks=1\r\nautostart=1\r\nfps=30\r\ncountdown=5\r\n";

    sealed class FakeHost : IHostChannel
    {
        public readonly List<string> Calls = new();
        public HostReply SetReply = HostReply.Ok, CheckReply = HostReply.Ok;
        public (bool, bool)? State = (true, true);

        public HostReply Set(string key, string value) { Calls.Add("set " + key + "=" + value); return SetReply; }

        public HostReply CheckHotkey(string key, string combo) { Calls.Add("check " + key + "=" + combo); return CheckReply; }

        public void SuspendHotkeys(bool suspend) => Calls.Add("suspend " + suspend);

        public (bool Screenshot, bool Recording)? Probe() => State;
    }

    static SettingsWindow Window(FakeHost host, TestSettings settings, double scale = 1)
    {
        var window = new SettingsWindow(host, settings.Load, scale);
        MuiTheme.Apply(window.Resources, true);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(SettingsWindow.BaseWidth * scale, double.PositiveInfinity));
        content.Arrange(new Rect(content.DesiredSize));
        content.UpdateLayout();
        return window;
    }

    [Fact]
    public void ASettingsFileOfVersion112IsShownWithoutLoss() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        var window = Window(new FakeHost(), settings);
        Assert.Equal("Shift+Alt+4", window.ScreenshotHotkey.Combo);
        Assert.Equal("C:\\Bilder\\Shots", window.ScreenshotFolderRow.Description);
        Assert.True(window.Microphone.IsChecked);
        Assert.Equal(0, window.Appearance.SelectedValue);   // new key: like Windows
        Assert.Equal(3, window.Groups.Count());
        window.Close();
    });

    [Fact]
    public void ASwitchSavesAtOnceAndARefusalStaysInItsRow() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        var host = new FakeHost();
        var window = Window(host, settings);
        window.Microphone.IsChecked = false;
        Assert.Contains("set microphone=0", host.Calls);

        host.SetReply = HostReply.NoAnswer;
        window.Microphone.IsChecked = true;
        Assert.False(window.Microphone.IsChecked);   // back to what is stored
        var row = (SettingRow)window.Microphone.Parent;
        Assert.Equal(Status.Error, row.Status);
        Assert.Equal(UiText.Settings.NoHost, row.Description);
        window.Close();
    });

    [Fact]
    public void ATakenHotkeyShowsInTheRowAndTheOldOneStays() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        var host = new FakeHost();
        var window = Window(host, settings);
        var box = window.ScreenshotHotkey;

        box.StartCapture();
        Assert.Contains("suspend True", host.Calls);
        host.CheckReply = HostReply.Taken;
        box.Try("Ctrl+Print");
        Assert.True(box.Capturing);
        Assert.Equal(UiText.Hotkeys.Taken, window.ScreenshotHotkeyRow.Description);
        Assert.Equal(Status.Error, window.ScreenshotHotkeyRow.Status);
        Assert.Equal("Shift+Alt+4", box.Combo);

        host.CheckReply = HostReply.Ok;
        box.Try("Print");   // the print key alone: free now
        Assert.False(box.Capturing);
        Assert.Contains("set screenshot_hotkey=Print", host.Calls);
        Assert.Contains("suspend False", host.Calls);
        Assert.Equal(UiText.Hotkeys.PrintHint, window.ScreenshotHotkeyRow.Description);   // where to switch off the Snipping Tool

        host.SetReply = HostReply.Taken;   // the host could not register it after all: the old one stays
        box.StartCapture();
        box.Try("Ctrl+Alt+S");
        Assert.Equal("Shift+Alt+4", box.Combo);
        Assert.Equal(Status.Error, window.ScreenshotHotkeyRow.Status);
        window.Close();
    });

    [Fact]
    public void AHotkeyTheHostCouldNotRegisterSaysItIsRetried() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        var window = Window(new FakeHost { State = (false, true) }, settings);
        Assert.Equal(UiText.Hotkeys.TakenRetrying, window.ScreenshotHotkeyRow.Description);
        Assert.Equal(Status.Warning, window.ScreenshotHotkeyRow.Status);
        window.Close();
    });

    [Fact]
    public void AnUnusableFolderKeepsTheOldOneAndSaysSo() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        var host = new FakeHost { SetReply = HostReply.FolderUnusable };
        var window = Window(host, settings);
        window.ChooseFolder(window.ScreenshotFolderRow, "screenshot_folder", "Z:\\gibt es nicht");
        Assert.Equal(Status.Error, window.ScreenshotFolderRow.Status);
        Assert.Equal(UiText.Settings.FolderUnusable, window.ScreenshotFolderRow.Description);
        SettingsWindow.OpenFolder(window.ScreenshotFolderRow, "Z:\\gibt es nicht");
        Assert.Equal(UiText.Settings.FolderMissing, window.ScreenshotFolderRow.Description);
        window.Close();
    });

    [Fact]
    public void TheAppearanceSwitchesTheWindowAtOnce() => WpfThread.Run(() =>
    {
        bool? force = Palette.Force;
        try
        {
            using var settings = new TestSettings(Version112);
            var host = new FakeHost();
            var window = Window(host, settings);
            Palette.Force = null;
            CaptureSettings.UseAppearance(Appearance.Light);
            window.Appearance.SelectedValue = 2;
            Assert.Contains("set appearance=2", host.Calls);
            Assert.Equal(Appearance.Dark, CaptureSettings.Appearance);
            Assert.Contains(MuiTheme.Dark, window.Resources.MergedDictionaries);
            window.Close();
        }
        finally
        {
            Palette.Force = force;
            CaptureSettings.UseAppearance(Appearance.System);
        }
    });

    [Fact]
    public void OnlyOnePrimaryActionInTheHead() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        var window = Window(new FakeHost(), settings);
        Assert.Single(window.HeadActions.Children.OfType<PrimaryButton>());
        window.Close();
    });

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void TheWindowGrowsWithTheTextScaleAndCutsNoText(double scale) => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        var window = Window(new FakeHost(), settings, scale);
        var content = (FrameworkElement)window.Content;
        Assert.InRange(content.DesiredSize.Width, 0, SettingsWindow.BaseWidth * scale + 1);
        foreach (var text in Texts(content))
        {
            if (text.TextWrapping == TextWrapping.Wrap || text.ActualWidth == 0 || text.FontFamily == Palette.IconFont) continue;   // icons sit in fixed boxes
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(text.DesiredSize.Width - text.Margin.Left - text.Margin.Right <= text.ActualWidth + 1, "cut: " + text.Text);
        }
        window.Close();
    });

    [Theory]
    [MemberData(nameof(MosaikCapture.Engine.Tests.Pixel.PixelMatrixTests.Variants), MemberType = typeof(MosaikCapture.Engine.Tests.Pixel.PixelMatrixTests))]
    [Trait("Category", "Pixel")]
    public void SettingsWindowLooks(bool light, double scale) => WpfThread.Run(() =>
    {
        PixelComparison.RequireFonts();
        using var settings = new TestSettings(Version112);
        var window = new SettingsWindow(new FakeHost(), settings.Load);
        MuiTheme.Apply(window.Resources, light);
        var content = (FrameworkElement)window.Content;
        content.Width = SettingsWindow.BaseWidth;
        var check = new PixelComparison();
        check.Check("settings_window." + (light ? "light" : "dark") + "." + ((int)Math.Round(scale * 100)).ToString(CultureInfo.InvariantCulture),
            MosaikCapture.Engine.Tests.Pixel.PixelMatrixTests.Draw(content, scale));
        check.AssertAllMatch();
        window.Close();
    });

    static IEnumerable<TextBlock> Texts(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text) yield return text;
            foreach (var deeper in Texts(child)) yield return deeper;
        }
    }
}
