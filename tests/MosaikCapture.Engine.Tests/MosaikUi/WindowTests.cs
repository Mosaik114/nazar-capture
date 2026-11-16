using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MosaikCapture.Engine.Tests.MosaikUi;

/// <summary>
/// Layer 5: the windows are built from the kit. Every action of card, recording bar, editor and pinned image is a real
/// button or menu entry (reachable with Tab, pressed with Space/Enter, named for screen readers); the overlay works in
/// device pixels at any scale, follows a DPI change and can be used without a mouse.
/// </summary>
public sealed class WindowTests
{
    static IEnumerable<T> Visual<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var deeper in Visual<T>(child)) yield return deeper;
        }
    }

    static void Lay(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
    }

    static void AssertKeyboardReachable(IEnumerable<ButtonBase> buttons, int atLeast, string where)
    {
        var list = buttons.ToList();
        Assert.True(list.Count >= atLeast, where + ": only " + list.Count + " buttons");
        foreach (var button in list)
        {
            string name = UIElementAutomationPeer.CreatePeerForElement(button).GetName();
            Assert.False(string.IsNullOrWhiteSpace(name), where + ": a button without a name");
            Assert.DoesNotContain(name, c => c >= (char)0xE000 && c <= (char)0xF8FF);
            Assert.True(button.Focusable && KeyboardNavigation.GetIsTabStop(button), where + ": " + name + " is not reachable with Tab");
            Assert.NotNull(UIElementAutomationPeer.CreatePeerForElement(button).GetPattern(button is ToggleButton ? PatternInterface.Toggle : PatternInterface.Invoke));
            Assert.Null(button.FocusVisualStyle);   // the kit's focus edge, no dotted rectangle
        }
    }

    [Fact]
    public void CardActionsAreKeyboardButtons() => WpfThread.Run(() =>
    {
        var card = new ScreenshotCard(() => { }, () => { }, () => { }) { Offscreen = true };
        Lay((FrameworkElement)card.Content);
        AssertKeyboardReachable(Visual<ButtonBase>(card.Plate).Where(b => b.Visibility == Visibility.Visible), 5, "screenshot card");
        card.Close();
    });

    [Fact]
    public void RecordingBarActionsAreKeyboardButtons() => WpfThread.Run(() =>
    {
        var bar = new RecordingToolbar();
        bar.Refresh();
        Lay((FrameworkElement)bar.Content);
        AssertKeyboardReachable(bar.Buttons, 6, "recording bar");
        bar.Close();
    });

    [Fact]
    public void DiscardAsksWithWords() => WpfThread.Run(() =>
    {
        var bar = new RecordingToolbar();
        bar.Refresh();
        Assert.Equal(MuiRole.Plain, Mui.GetRole(bar.DiscardButton));
        bar.ConfirmingDiscard = true;
        bar.Refresh();
        Assert.Equal(MuiRole.Recording, Mui.GetRole(bar.DiscardButton));
        Lay((FrameworkElement)bar.Content);
        Assert.Contains(Visual<TextBlock>(bar.DiscardButton), t => t.Text == UiText.Toolbar.ConfirmDiscardShort);
        bar.Close();
    });

    [Fact]
    public void EditorToolbarIsAllKeyboardButtons() => WpfThread.Run(() =>
    {
        var editor = new ScreenshotEditor((_, _) => { }, _ => { }, (_, _) => { });
        Lay((FrameworkElement)editor.Toolbar);
        AssertKeyboardReachable(editor.ToolbarButtons, 20, "editor toolbar");
        Assert.Single(editor.ToolbarButtons, b => Mui.GetRole(b) == MuiRole.Primary);   // one main action
        editor.Close();
    });

    [Fact]
    public void PinnedImageMenuIsAKeyboardMenu() => WpfThread.Run(() =>
    {
        var image = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[64], 16);
        var pin = new PinnedScreenshot(image, "a.png");
        var entries = pin.Menu.Items.OfType<MenuItem>().ToList();
        Assert.True(entries.Count >= 5);
        foreach (var entry in entries)
            Assert.False(string.IsNullOrWhiteSpace(UIElementAutomationPeer.CreatePeerForElement(entry).GetName()));
        Assert.Same(pin.Menu, ((FrameworkElement)pin.Content).ContextMenu);   // right click, Shift+F10 and the menu key
        pin.Close();
    });

    [Fact]
    public void FailedCardsStayAndShowTheirStateInWords() => WpfThread.Run(() =>
    {
        var card = new ScreenshotCard(() => { }, () => { }, () => { }) { Offscreen = true };
        card.ShowProblem("Screenshot fehlgeschlagen", "Bildschirm nicht verfügbar", new RECT { left = 0, top = 0, right = 1920, bottom = 1040 });
        Assert.True(card.Plate.IsError);
        Assert.Equal(1, card.Plate.Opacity);
        WpfThread.Pump(300);
        Assert.True(card.Plate.IsError);   // no lifetime: it stays until closed
        Assert.Contains(Visual<ButtonBase>(card.Plate), b => b.Visibility == Visibility.Visible && UIElementAutomationPeer.CreatePeerForElement(b).GetName() == UiText.Common.Close);
        card.Close();
    });

    [Fact]
    public void ActionsWaitingForTheFileSayWhy() => WpfThread.Run(() =>
    {
        var card = new ScreenshotCard(() => { }, () => { }, () => { }) { Offscreen = true };
        var image = BitmapSource.Create(40, 20, 96, 96, PixelFormats.Bgra32, null, new byte[40 * 20 * 4], 160);
        card.Present(new ScreenCapture { Image = image, FullScreen = image, Path = "x.png", WorkArea = new RECT { right = 1920, bottom = 1040 } }, "Kopiert");
        var edit = Visual<ActionButton>(card.Plate).First();
        Assert.False(edit.IsEnabled);
        Assert.Equal(UiText.Common.NotYetSaved, ((ToolTip)edit.ToolTip).Content);
        card.Saved();
        Assert.True(edit.IsEnabled);
        card.Close();
    });

    [Fact]
    public void TheKeyboardWayToACardIsTheScreenshotHotkeyWithCtrl()
    {
        var chord = ToolWindowBase.KeyboardChord("Shift+Alt+4")!.Value;
        Assert.Equal(HotkeyChord.Control | HotkeyChord.Shift | HotkeyChord.Alt, chord.Modifiers);
        Assert.Equal('4', chord.Key);
        Assert.Equal(HotkeyChord.Control | HotkeyChord.Shift, ToolWindowBase.KeyboardChord("Ctrl+Print")!.Value.Modifiers);
        Assert.Null(ToolWindowBase.KeyboardChord("Ctrl+Shift+A"));
        Assert.Null(ToolWindowBase.KeyboardChord("kaputt"));
    }

    // ---------- Overlay ----------

    /// <summary>Two monitors: one left of the main monitor (negative coordinates) and the main one.</summary>
    static readonly RECT LeftMonitor = new() { left = -1600, top = 0, right = 0, bottom = 900 };
    static readonly RECT MainMonitor = new() { left = 0, top = 0, right = 2400, bottom = 1350 };
    static readonly Int32Rect Virtual = new(-1600, 0, 4000, 1350);

    static BitmapSource Screen()
    {
        var pixels = new byte[Virtual.Width * Virtual.Height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 0x40; pixels[i + 1] = 0x80; pixels[i + 2] = 0xC0; pixels[i + 3] = 0xFF; }
        var image = BitmapSource.Create(Virtual.Width, Virtual.Height, 96, 96, PixelFormats.Bgra32, null, pixels, Virtual.Width * 4);
        image.Freeze();
        return image;
    }

    public static TheoryData<double> Scales => new() { 1.0, 1.25, 1.5 };

    [Theory]
    [MemberData(nameof(Scales))]
    public void KeyboardSelectionGivesTheSameDevicePixelsAtEveryScale(double scale) => WpfThread.Run(() =>
    {
        ScreenCapture? result = null;
        var overlay = new SelectionOverlay(capture => result = capture);
        overlay.OpenOffscreen(Screen(), Virtual, new[] { LeftMonitor, MainMonitor }, scale);
        var start = overlay.Pointer;   // the middle of the virtual screen
        overlay.Press(Key.Enter);      // start
        for (int i = 0; i < 12; i++) overlay.Press(Key.Right);
        for (int i = 0; i < 7; i++) overlay.Press(Key.Down);
        var frame = overlay.FrameBounds;
        Assert.InRange(Math.Abs((frame.X + 1) * scale - start.X), 0, 0.01);   // the frame sits one unit outside the area
        Assert.InRange(Math.Abs((frame.Width - 2) * scale - 13), 0, 0.01);
        overlay.Press(Key.Enter);      // end
        Assert.NotNull(result);
        Assert.Equal(new Int32Rect((int)start.X, (int)start.Y, 13, 8), result!.Crop);
        Assert.Equal(scale, result.Scale);
        overlay.Close();
    });

    [Fact]
    public void AfterADpiChangeTheSelectionStaysOnTheSamePixels() => WpfThread.Run(() =>
    {
        var overlay = new SelectionOverlay(_ => { });
        overlay.OpenOffscreen(Screen(), Virtual, new[] { LeftMonitor, MainMonitor }, 1.0);
        overlay.Press(Key.Enter);
        for (int i = 0; i < 3; i++) overlay.Press(Key.Left);
        var selection = overlay.Selection;
        var before = overlay.FrameBounds;
        overlay.UseScale(1.5);
        Assert.Equal(1.5, overlay.Scale);
        Assert.Equal(selection, overlay.Selection);
        var after = overlay.FrameBounds;
        Assert.InRange(Math.Abs((after.X + 1) * 1.5 - (before.X + 1)), 0, 0.01);
        overlay.Close();
    });

    [Fact]
    public void ArrowKeysMoveOnePixelOrTenWithShiftAndTheLeftMonitorIsFound() => WpfThread.Run(() =>
    {
        var overlay = new SelectionOverlay(_ => { });
        overlay.OpenOffscreen(Screen(), Virtual, new[] { LeftMonitor, MainMonitor }, 1.25);
        var start = overlay.Pointer;
        overlay.Press(Key.Left);
        Assert.Equal(start.X - 1, overlay.Pointer.X);
        for (int i = 0; i < 2000; i++) overlay.Press(Key.Left);
        Assert.Equal(0, overlay.Pointer.X);   // kept on the virtual screen
        Assert.Equal(LeftMonitor.left, overlay.MonitorAt(overlay.Pointer).left);   // negative coordinates
        Assert.Equal(MainMonitor.left, overlay.MonitorAt(new Point(1700, 10)).left);
        overlay.Close();
    });
}
