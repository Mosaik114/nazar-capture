using System.Windows.Controls;
using System.Windows.Input;
using MosaikCapture.Engine.Settings;

namespace MosaikCapture.Engine.Tests.MosaikUi;

/// <summary>
/// The settings window end to end without a host process: built as Hams' (stage, tab row, one page per tab, groups on
/// plates), it reads settings.ini (here a file of version 1.1.2), sends every change to the host at once, shows
/// refusals in the row, records hotkeys with the host's hotkeys suspended, and grows with the text scale.
/// </summary>
public sealed class SettingsWindowTests
{
    /// <summary>settings.ini as version 1.1.2 wrote it.</summary>
    internal const string Version112 =
        "[capture]\r\nscreenshot_hotkey=Shift+Alt+4\r\nrecording_hotkey=Shift+Alt+5\r\nscreenshot_folder=C:\\Bilder\\Shots\r\n" +
        "recording_folder=C:\\Videos\\Takes\r\nmicrophone=1\r\nsystem_audio=0\r\nshow_clicks=1\r\nautostart=1\r\nfps=30\r\ncountdown=5\r\n";

    /// <summary>The page names of the pixel references, in tab order (the first keeps the old name).</summary>
    static readonly string[] PageNames = { "settings_window", "settings_window_recording", "settings_window_general", "settings_window_info" };

    internal sealed class FakeHost : IHostChannel
    {
        public readonly List<string> Calls = new();
        public HostReply SetReply = HostReply.Ok, CheckReply = HostReply.Ok;
        public (bool, bool)? State = (true, true);

        public HostReply Set(string key, string value) { Calls.Add("set " + key + "=" + value); return SetReply; }

        public HostReply CheckHotkey(string key, string combo) { Calls.Add("check " + key + "=" + combo); return CheckReply; }

        public void SuspendHotkeys(bool suspend) => Calls.Add("suspend " + suspend);

        public (bool Screenshot, bool Recording)? Probe() => State;
    }

    static SettingsWindow Window(FakeHost host, TestSettings settings, double scale = 1, WindowPlaces? places = null, Updates.Release? pending = null)
    {
        var window = new SettingsWindow(host, settings.Load, scale, places, pending: () => pending);
        MuiTheme.Apply(window.Resources, true);
        Lay(window, scale);
        return window;
    }

    static void Lay(SettingsWindow window, double scale = 1)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(SettingsWindow.BaseWidth * scale, SettingsWindow.BaseHeight * scale));
        content.Arrange(new Rect(0, 0, SettingsWindow.BaseWidth * scale, SettingsWindow.BaseHeight * scale));
        content.UpdateLayout();
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
        Assert.Equal(30, ((Select)window.Row("fps").Control!).SelectedValue);
        window.Close();
    });

    [Fact]
    public void TheWindowIsBuiltLikeHams() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        var window = Window(new FakeHost(), settings);
        Assert.NotNull(window.StageHead);
        Assert.Equal(UiText.Settings.Title, window.StageHead!.Title);
        Assert.Equal(Stage.StageHeight, window.StageHead.ActualHeight);
        Assert.Equal(IconButton.StoneSize, window.StageHead.CloseButton.ActualWidth);
        Assert.True(window.StageHead.Banner.Painted > 0, "the banner has room between title and cross");
        Assert.Equal(new[] { UiText.Settings.ScreenshotGroup, UiText.Settings.RecordingGroup, UiText.Settings.GeneralGroup, UiText.Settings.InfoGroup },
            window.Tabs.Buttons.Select(b => System.Windows.Automation.AutomationProperties.GetName(b)).ToArray());
        Assert.InRange(window.Tabs.Buttons.Select(b => b.TranslatePoint(new Point(), window.Tabs).Y).Distinct().Count(), 1, 2);   // wraps like Hams' tabs at 440
        Assert.Equal(4, window.Pages.Children.Count);
        Assert.Equal(5, window.Groups.Count);   // the recording page has a second group, "Ton und Klicks"
        Assert.Empty(Descendants<PrimaryButton>(window.Content as DependencyObject));   // no main action: the window is for settings
        Assert.Equal(SettingsWindow.MinimumWidth, window.MinWidth);
        Assert.Equal(SettingsWindow.MinimumHeight, window.MinHeight);
        Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
        window.Close();
    });

    [Fact]
    public void TheTabsAreOneTabStopAndTheArrowsSwitchThePage() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        var window = Window(new FakeHost(), settings);
        var tabs = window.Tabs;
        Assert.Equal(0, tabs.Selected);
        Assert.Equal(1, tabs.Buttons.Count(b => b.IsTabStop));
        Assert.Equal(MuiRole.TabSelected, Mui.GetRole(tabs.Buttons[0]));
        Assert.Equal(MuiRole.Tab, Mui.GetRole(tabs.Buttons[1]));

        tabs.Jump(2);   // what Right, Right does
        Assert.Equal(2, tabs.Selected);
        Assert.Equal(2, window.Pages.Selected);
        Assert.Equal(Visibility.Visible, window.Pages.Children[2].Visibility);
        Assert.Equal(Visibility.Hidden, window.Pages.Children[0].Visibility);
        Assert.True(tabs.Buttons[2].IsTabStop);
        Assert.False(tabs.Buttons[0].IsTabStop);
        Assert.Equal(KeyboardNavigationMode.Once, KeyboardNavigation.GetTabNavigation(tabs));
        tabs.Jump(99);
        Assert.Equal(3, tabs.Selected);
        window.Close();
    });

    [Fact]
    public void HiddenPagesAreLaidOutAhead() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        var window = Window(new FakeHost(), settings);
        foreach (FrameworkElement page in window.Pages.Children)
            Assert.True(page.ActualWidth > 300 && page.ActualHeight > 50, "every page is measured and arranged at the window's width");
        window.Close();
    });

    [Fact]
    public void TheLongPageScrollsWithAVisibleBarAndAnEdgeBelow() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        var window = Window(new FakeHost(), settings);
        window.SelectPage(NazarSettings.RecordingPage);
        Lay(window);
        Assert.NotNull(window.Scroll.Bar.ThumbPlace());
        Assert.True(window.Scroll.BottomEdgeShown);
        Assert.False(window.Scroll.TopEdgeShown);
        window.Scroll.Bar.PageBy(1);
        Lay(window);
        Assert.True(window.Scroll.TopEdgeShown);
        window.SelectPage(0);   // a new page starts at the top
        Lay(window);
        Assert.False(window.Scroll.TopEdgeShown);
        Assert.Null(window.Scroll.Bar.ThumbPlace());   // the short page fits: no bar, the gutter stays
        window.Close();
    });

    [Fact]
    public void TheUpdateNoticeComesAndGoesAboveTheUpdateSwitch() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        var window = Window(new FakeHost(), settings, pending: new Updates.Release("9.9.9", "https://example.invalid", null, null));
        Assert.Equal(Visibility.Visible, window.UpdateRow!.Visibility);
        Assert.Equal(UiText.Settings.UpdateHint("9.9.9", AppInfo.Version), window.UpdateRow.Description);
        window.ShowUpdate(null);
        Assert.Equal(Visibility.Collapsed, window.UpdateRow.Visibility);
        window.Close();
    });

    [Fact]
    public void TheSizeIsRememberedAndNeverBelowTheMinimum() => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        using var folder = new TempDirectory();
        var places = new WindowPlaces(Path.Combine(folder.Path, WindowPlaces.FileName));
        places.Write("settings", 520, 300);
        var window = Window(new FakeHost(), settings, places: places);
        Assert.Equal(520, window.Width);
        Assert.Equal(SettingsWindow.MinimumHeight, window.Height);
        Assert.Equal((520.0, 300.0), places.Read("settings"));
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
        Assert.True(window.ScreenshotHotkeyRow.Stacked);   // keys below the text, as Hams' shortcut rows

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
    public void TheBannerComesInStepsAndOnlyWithRoom()
    {
        Assert.Equal(0, StageBanner.TrackWidth(63));
        Assert.Equal(64, StageBanner.TrackWidth(70));
        Assert.Equal(160, StageBanner.TrackWidth(175.9));
        Assert.Equal(288, StageBanner.TrackWidth(900));   // capped at 300, in steps of 16
        Assert.Equal(0, BannerMotif.Fade(0), 3);
        Assert.Equal(0, BannerMotif.Fade(1), 3);
        Assert.Equal(1, BannerMotif.Fade(0.5), 3);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void TheWindowGrowsWithTheTextScaleAndCutsNoText(double scale) => WpfThread.Run(() =>
    {
        using var settings = new TestSettings(Version112);
        var window = Window(new FakeHost(), settings, scale);
        Assert.Equal(SettingsWindow.BaseWidth * scale, window.Width, 3);
        Assert.Equal(SettingsWindow.MinimumWidth * scale, window.MinWidth, 3);
        for (int page = 0; page < window.Pages.Children.Count; page++)
        {
            window.SelectPage(page);
            Lay(window, scale);
            foreach (var text in Texts((DependencyObject)window.Pages.Children[page]))
            {
                if (text.TextWrapping == TextWrapping.Wrap || text.ActualWidth == 0 || text.FontFamily == Palette.IconFont) continue;   // icons sit in fixed boxes
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(text.DesiredSize.Width - text.Margin.Left - text.Margin.Right <= text.ActualWidth + 1, "cut: " + text.Text);
            }
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
        var window = new SettingsWindow(new FakeHost(), settings.Load, pending: () => null);
        MuiTheme.Apply(window.Resources, light);
        var content = (FrameworkElement)window.Content;
        content.Width = SettingsWindow.BaseWidth;
        content.Height = SettingsWindow.BaseHeight;
        var check = new PixelComparison();
        for (int page = 0; page < PageNames.Length; page++)
        {
            window.SelectPage(page);
            check.Check(PageNames[page] + "." + (light ? "light" : "dark") + "." + ((int)Math.Round(scale * 100)).ToString(CultureInfo.InvariantCulture),
                MosaikCapture.Engine.Tests.Pixel.PixelMatrixTests.Draw(content, scale));
        }
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

    internal static IEnumerable<T> Descendants<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }
}
