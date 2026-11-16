using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace NazarCapture.Engine.Tests.NazarUi;

/// <summary>Layer 3: roles and their state order, the visible keyboard focus, pressing, and names for screen readers.</summary>
public sealed class BehaviorTests : IDisposable
{
    public void Dispose()
    {
        PressBehavior.PointerOverride = null;
        InputModality.Reset(true);
    }

    [Fact]
    public void DisabledBeatsEverythingAndPressedBeatsHover()
    {
        Assert.Equal(SurfaceState.Disabled, Roles.State(enabled: false, error: true, loading: true, pressed: true, hover: true, selected: true));
        Assert.Equal(SurfaceState.Error, Roles.State(true, true, true, true, true, true));
        Assert.Equal(SurfaceState.Loading, Roles.State(true, false, true, true, true, true));
        Assert.Equal(SurfaceState.Pressed, Roles.State(true, false, false, true, true, true));
        Assert.Equal(SurfaceState.Hover, Roles.State(true, false, false, false, true, true));
        Assert.Equal(SurfaceState.Selected, Roles.State(true, false, false, false, false, true));
        Assert.Equal(SurfaceState.Default, Roles.State(true, false, false, false, false, false));
        Assert.False(Roles.ShowsFocus(true, SurfaceState.Disabled));
        Assert.True(Roles.ShowsFocus(true, SurfaceState.Hover));
        Assert.False(Roles.IsInteractive(SurfaceState.Loading));
    }

    [Fact]
    public void EveryRoleAndStateResolvesToTokensOfBothThemes() => WpfThread.Run(() =>
    {
        foreach (MuiRole role in Enum.GetValues(typeof(MuiRole)))
            foreach (SurfaceState state in Enum.GetValues(typeof(SurfaceState)))
            {
                var look = Roles.Resolve(role, state);
                foreach (var key in new[] { look.Fill, look.Text, look.Side, Roles.FocusInk(look) })
                {
                    if (key == null) continue;
                    Assert.True(MuiTheme.Light.Contains(key) && MuiTheme.Dark.Contains(key), role + "/" + state + ": " + key);
                }
            }
        // On a filled role the focus colour would vanish: the edge takes the text colour.
        Assert.Equal(UiTokens.Keys.OnAccent, Roles.FocusInk(Roles.Resolve(MuiRole.Primary, SurfaceState.Default)));
        Assert.Equal(UiTokens.Keys.Focus, Roles.FocusInk(Roles.Resolve(MuiRole.Secondary, SurfaceState.Default)));
        Assert.Null(Roles.Resolve(MuiRole.Plain, SurfaceState.Default).Fill);
    });

    static (KitButton Button, RoleChrome Chrome) Laid(MuiRole role = MuiRole.Secondary)
    {
        var button = new KitButton { Content = "Speichern", Width = 120, Height = 38 };
        Mui.SetRole(button, role);
        var host = new Border { Child = button };
        MuiTheme.Apply(host.Resources, true);
        host.Measure(new Size(200, 100));
        host.Arrange(new Rect(0, 0, 200, 100));
        host.UpdateLayout();
        var chrome = (RoleChrome)VisualTreeHelper.GetChild(button, 0);
        return (button, chrome);
    }

    [Fact]
    public void TheChromeFollowsRoleAndState() => WpfThread.Run(() =>
    {
        var (button, chrome) = Laid(MuiRole.Primary);
        Assert.Same(MuiTheme.Light[UiTokens.Keys.AccentSurface], chrome.Fill);
        Assert.Same(MuiTheme.Light[UiTokens.Keys.AccentSide], chrome.Side);
        Assert.Same(MuiTheme.Light[UiTokens.Keys.OnAccent], chrome.GetValue(TextElement.ForegroundProperty));

        Mui.SetIsError(button, true);
        Assert.Same(MuiTheme.Light[UiTokens.Keys.ErrorSurface], chrome.Fill);
        Mui.SetIsError(button, false);

        Mui.SetShowFocus(button, true);
        Assert.True(chrome.ShowFocus);
        button.IsEnabled = false;   // disabled: no hover, no press, no focus edge, the grey stone
        Assert.Equal(SurfaceState.Disabled, chrome.State);
        Assert.False(chrome.ShowFocus);
        Assert.Same(MuiTheme.Light[UiTokens.Keys.ControlSurface], chrome.Fill);
        Assert.Same(MuiTheme.Light[UiTokens.Keys.TextDisabled], chrome.GetValue(TextElement.ForegroundProperty));

        Mui.SetRole(button, MuiRole.Plain);
        button.IsEnabled = true;
        Assert.Null(chrome.Fill);   // plain at rest: only the paper
        Assert.Equal(button.ActualHeight, chrome.ActualHeight);
    });

    [Fact]
    public void TabShowsTheFocusAClickHidesItAlsoAfterAWindowSwitch() => WpfThread.Run(() =>
    {
        var (button, _) = Laid();
        InputModality.Install();
        void FocusIn() => button.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, button) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
        void FocusOut() => button.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, button, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });

        InputModality.NoteKey();   // Tab
        FocusIn();
        Assert.True(Mui.GetShowFocus(button));
        FocusOut();
        Assert.False(Mui.GetShowFocus(button));

        InputModality.NotePointer();   // a click, then the window is activated again: the focus returns without an edge
        FocusIn();
        Assert.False(Mui.GetShowFocus(button));
        FocusOut();

        InputModality.NoteKey();
        FocusIn();
        Assert.True(Mui.GetShowFocus(button));
    });

    static void Mouse(UIElement target, RoutedEvent routed)
    {
        MouseEventArgs args = routed == UIElement.MouseMoveEvent
            ? new MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
            : new MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, MouseButton.Left);
        args.RoutedEvent = routed;
        target.RaiseEvent(args);
    }

    [Fact]
    public void PressSlideOutReleaseDoesNotClickSlideBackDoes() => WpfThread.Run(() =>
    {
        var (button, chrome) = Laid();
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        var pointer = new Point(10, 10);
        PressBehavior.PointerOverride = _ => pointer;

        Mouse(button, UIElement.MouseLeftButtonDownEvent);
        Assert.True(PressBehavior.IsPressed(button));
        Assert.True(chrome.IsPressed);
        pointer = new Point(300, 10);
        Mouse(button, UIElement.MouseMoveEvent);
        Assert.False(PressBehavior.IsPressed(button));   // slid out: the surface rises
        Mouse(button, UIElement.MouseLeftButtonUpEvent);
        Assert.Equal(0, clicks);

        pointer = new Point(10, 10);
        Mouse(button, UIElement.MouseLeftButtonDownEvent);
        pointer = new Point(300, 10);
        Mouse(button, UIElement.MouseMoveEvent);
        pointer = new Point(20, 12);
        Mouse(button, UIElement.MouseMoveEvent);
        Assert.True(PressBehavior.IsPressed(button));    // back in: it sinks again
        Mouse(button, UIElement.MouseLeftButtonUpEvent);
        Assert.Equal(1, clicks);
        Assert.False(PressBehavior.IsPressed(button));

        Mouse(button, UIElement.MouseLeftButtonUpEvent);   // a release that started elsewhere never clicks
        Assert.Equal(1, clicks);
        button.IsEnabled = false;
        Mouse(button, UIElement.MouseLeftButtonDownEvent);
        Mouse(button, UIElement.MouseLeftButtonUpEvent);
        Assert.Equal(1, clicks);
    });

    [Fact]
    public void PressTrackerRules()
    {
        var press = new PressTracker();
        Assert.False(press.Down(interactive: false));
        Assert.False(press.Up(inside: true, interactive: true));
        Assert.True(press.Down(true));
        Assert.False(press.Move(false));
        Assert.True(press.Move(true));
        press.Cancel();
        Assert.False(press.Up(true, true));
    }

    [Fact]
    public void KitButtonsAlwaysHaveAScreenReaderName() => WpfThread.Run(() =>
    {
        var text = new KitButton { Content = "Speichern" };
        Assert.Equal("Speichern", new ButtonAutomationPeer(text).GetName());
        var icon = new KitButton { Content = IconGlyphs.Copy, ToolTip = "Kopieren" };
        Assert.Equal("Kopieren", UIElementAutomationPeer.CreatePeerForElement(icon).GetName());
        var toggle = new KitToggleButton { Content = IconGlyphs.Copy, ToolTip = new ToolTip { Content = "Stift" } };
        Assert.Equal("Stift", UIElementAutomationPeer.CreatePeerForElement(toggle).GetName());
        var glyphOnly = new KitButton { Content = IconGlyphs.Copy };
        Mui.EnsureAutomationName(glyphOnly, glyphOnly.Content);
        Assert.True(string.IsNullOrEmpty(AutomationProperties.GetName(glyphOnly)), "a glyph is no name");
        Assert.Null(glyphOnly.FocusVisualStyle);
    });
}
