using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using NazarCapture.Engine.Settings;

namespace NazarCapture.Engine.Tests.NazarUi;

/// <summary>
/// The choices of the settings window driven as a user does, in a shown window on a desktop nobody sees: the mouse
/// through WPF's whole input pipeline, the keyboard on the focused element, and UI Automation. A press on a Select
/// used to only focus it (ComboBox's own class handler took the press before the Select could open its list), so
/// "Hell"/"Dunkel", "Bildrate" and "Countdown" could not be chosen with the mouse at all.
/// </summary>
public sealed class SelectMouseTests
{
    static readonly string[] ChoiceKeys = { NazarSettings.Appearance, "fps", "countdown" };

    static void WithSettings(string ini, Action<SettingsWindow, SettingsWindowTests.FakeHost> body)
    {
        bool? force = Palette.Force;
        var before = CaptureSettings.Appearance;
        try
        {
            HiddenDesktop.Run(() =>
            {
                using var settings = new TestSettings(ini);
                var host = new SettingsWindowTests.FakeHost();
                Palette.Force = null;
                CaptureSettings.UseAppearance(settings.Load().Appearance);
                var window = new SettingsWindow(host, settings.Load, pending: () => null) { Left = 40, Top = 40, ShowActivated = true };
                Palette.Apply(window.Resources);
                window.Show();
                window.Activate();
                WpfThread.Pump(200);
                body(window, host);
                window.Close();
            });
        }
        finally
        {
            Palette.Force = force;
            CaptureSettings.UseAppearance(before);
        }
    }

    static Select Choice(SettingsWindow window, string key)
    {
        for (int page = 0; page < window.Pages.Children.Count; page++)
        {
            window.SelectPage(page);
            WpfThread.Pump(60);
            var select = (Select)window.Row(key).Control!;
            if (select.IsVisible)
            {
                // On a desktop nobody sees, Windows finds no layered window under a point (it has no pixels there), so
                // the open list would never be hit. Opaque, it is found like on the screen; it looks and works the same.
                ((System.Windows.Controls.Primitives.Popup)select.Template.FindName("PART_Popup", select)).AllowsTransparency = false;
                select.BringIntoView();
                WpfThread.Pump(60);
                return select;
            }
        }
        throw new InvalidOperationException(key + " is on no page");
    }

    static SelectItem Entry(Select select, int index) => (SelectItem)select.Items[index];

    [Fact]
    public void AClickOpensTheAppearanceAndAClickedEntrySwitchesTheTheme() => WithSettings(SettingsWindowTests.Version112 + "appearance=2\r\n", (window, host) =>
    {
        var select = Choice(window, NazarSettings.Appearance);
        Assert.Equal(2, select.SelectedValue);
        Assert.Contains(MuiTheme.Dark, window.Resources.MergedDictionaries);

        RealMouse.Click(select);
        Assert.True(select.IsDropDownOpen, "a click on the face opens the list");
        Assert.True(Entry(select, 0).IsVisible, "the entries are shown");

        RealMouse.Click(Entry(select, 0));   // "Hell"
        Assert.False(select.IsDropDownOpen, "a click on an entry closes the list");
        Assert.Equal(1, select.SelectedValue);
        Assert.Contains("set appearance=1", host.Calls);
        Assert.Equal(Appearance.Light, CaptureSettings.Appearance);
        Assert.Contains(MuiTheme.Light, window.Resources.MergedDictionaries);   // at once, without reopening
        Assert.DoesNotContain(MuiTheme.Dark, window.Resources.MergedDictionaries);

        RealMouse.Click(select);
        RealMouse.Click(Entry(select, 1));   // "Dunkel"
        Assert.Equal(2, select.SelectedValue);
        Assert.Equal("set appearance=2", host.Calls.Last());
        Assert.Contains(MuiTheme.Dark, window.Resources.MergedDictionaries);
    });

    [Fact]
    public void EveryChoiceOpensByMouseAndASecondClickClosesIt() => WithSettings(SettingsWindowTests.Version112, (window, host) =>
    {
        foreach (var key in ChoiceKeys)
        {
            var select = Choice(window, key);
            RealMouse.Click(select);
            Assert.True(select.IsDropDownOpen, key + " opens");
            RealMouse.Click(select);
            Assert.False(select.IsDropDownOpen, key + " closes on a second click");

            RealMouse.Click(select);
            var other = Entry(select, select.SelectedIndex == 0 ? 1 : 0);
            RealMouse.Click(other);
            Assert.False(select.IsDropDownOpen, key);
            Assert.Same(other, select.SelectedItem);
            Assert.Equal("set " + key + "=" + Convert.ToString(other.Tag, CultureInfo.InvariantCulture), host.Calls.Last());
        }
    });

    [Fact]
    public void SpaceOpensTheListAndTheArrowsAndSpaceChoose() => WithSettings(SettingsWindowTests.Version112 + "appearance=1\r\n", (window, host) =>
    {
        var select = Choice(window, NazarSettings.Appearance);
        Assert.True(select.Focus());
        WpfThread.Pump(50);
        Press(Key.Space);
        Assert.True(select.IsDropDownOpen, "Space opens the list");
        Press(Key.Down);
        Press(Key.Space);
        Assert.False(select.IsDropDownOpen);
        Assert.Equal(2, select.SelectedValue);
        Assert.Contains("set appearance=2", host.Calls);
        Assert.True(select.IsKeyboardFocused, "the focus goes back to the choice");

        Press(Key.Up);   // closed: the arrows choose at once
        Assert.Equal(1, select.SelectedValue);
        Assert.Equal("set appearance=1", host.Calls.Last());
    });

    [Fact]
    public void UiAutomationExpandsTheListAndSelectsAnEntry() => WithSettings(SettingsWindowTests.Version112 + "appearance=1\r\n", (window, host) =>
    {
        var select = Choice(window, NazarSettings.Appearance);
        var peer = UIElementAutomationPeer.CreatePeerForElement(select);
        var expand = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);
        expand.Expand();
        WpfThread.Pump(50);
        Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
        var dark = peer.GetChildren().Single(child => child.GetName() == "Dunkel");
        ((ISelectionItemProvider)dark.GetPattern(PatternInterface.SelectionItem)).Select();
        WpfThread.Pump(50);
        Assert.Equal(2, select.SelectedValue);
        Assert.Contains("set appearance=2", host.Calls);
    });

    static void Press(Key key)
    {
        var target = (UIElement)Keyboard.FocusedElement;
        var source = PresentationSource.FromVisual(target)!;
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled) target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
        WpfThread.Pump(50);
    }
}
