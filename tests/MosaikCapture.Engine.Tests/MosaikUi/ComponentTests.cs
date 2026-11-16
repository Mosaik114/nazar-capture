using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Interop;
using MosaikCapture.Engine.Tests.Pixel;

namespace MosaikCapture.Engine.Tests.MosaikUi;

/// <summary>Layer 4: every component fits its text, centres it, knows its states and keys and has a name.</summary>
public sealed class ComponentTests : IDisposable
{
    public void Dispose() => PressBehavior.PointerOverride = null;

    static T Lay<T>(T element, bool light = true) where T : FrameworkElement
    {
        var host = new Border { Child = element };
        MuiTheme.Apply(host.Resources, light);
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        return element;
    }

    static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var deeper in Find<T>(child)) yield return deeper;
        }
    }

    [Fact]
    public void ButtonsAreAsWideAsTheirTextAndCentreIt() => WpfThread.Run(() =>
    {
        var button = Lay(new PrimaryButton("Screenshot erstellen"));
        var text = Find<TextBlock>(button).Single();
        Assert.Same(MuiTheme.Light[UiTokens.Keys.OnAccent], text.Foreground);   // the label follows the role's text colour
        Assert.Equal(Math.Max(button.MinWidth, Math.Ceiling(text.ActualWidth) + 36), button.ActualWidth, 0);
        var centre = text.TranslatePoint(new Point(text.ActualWidth / 2, text.ActualHeight / 2), button);
        Assert.InRange(Math.Abs(centre.X - button.ActualWidth / 2), 0, 1);
        double faceCentre = UiTokens.Height.Primary / 2;   // centred on the face, not on face and side
        Assert.InRange(Math.Abs(centre.Y - faceCentre), 0, 1);
        Assert.Equal(UiTokens.Height.Primary + UiTokens.Depth.Control, button.ActualHeight);

        var longer = Lay(new SecondaryButton("Ein deutlich längerer Text auf dem Knopf"));
        Assert.True(longer.ActualWidth > button.ActualWidth);
    });

    [Fact]
    public void RolesOfTheButtons() => WpfThread.Run(() =>
    {
        Assert.Equal(MuiRole.Primary, Mui.GetRole(new PrimaryButton("a")));
        Assert.Equal(MuiRole.Secondary, Mui.GetRole(new SecondaryButton("a")));
        Assert.Equal(MuiRole.Recording, Mui.GetRole(new SecondaryButton("a", role: MuiRole.Recording)));
        Assert.Equal(MuiRole.Plain, Mui.GetRole(new IconButton(IconGlyphs.ChromeClose, "Schließen")));
        Assert.Equal(MuiRole.Plain, Mui.GetRole(new ToolButton(IconGlyphs.Pen, "Stift")));
        var tool = Lay(new ToolButton(IconGlyphs.Pen, "Stift") { IsChecked = true });
        var chrome = Find<RoleChrome>(tool).Single();
        Assert.Equal(SurfaceState.Selected, chrome.State);
        Assert.Same(MuiTheme.Light[UiTokens.Keys.ControlSurface], chrome.Fill);
    });

    /// <summary>An invisible window source around <paramref name="content"/>: key events need a presentation source.</summary>
    static (HwndSource Window, PresentationSource Source) Host(UIElement content)
    {
        var root = new Border { Child = content };
        MuiTheme.Apply(root.Resources, true);
        var source = new HwndSource(new HwndSourceParameters("kit test") { WindowStyle = 0, Width = 400, Height = 200 }) { RootVisual = root };
        root.Measure(new Size(400, 200));
        root.Arrange(new Rect(0, 0, 400, 200));
        root.UpdateLayout();
        return (source, source);
    }

    static void Key(UIElement target, PresentationSource source, Key key, bool up = false) =>
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = up ? Keyboard.KeyUpEvent : Keyboard.KeyDownEvent });

    [Fact]
    public void EnterAndSpaceClickAKitButton() => WpfThread.Run(() =>
    {
        var button = new SecondaryButton("Speichern");
        var (window, source) = Host(button);
        try
        {
            int clicks = 0;
            button.Click += (_, _) => clicks++;
            Key(button, source, System.Windows.Input.Key.Enter);
            Assert.Equal(1, clicks);
            var peer = (IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke);
            peer.Invoke();
            WpfThread.Pump(50);
            Assert.Equal(2, clicks);
        }
        finally { window.Dispose(); }
    });

    [Fact]
    public void SwitchFlipsAndItsRowIsATrigger() => WpfThread.Run(() =>
    {
        var toggle = new ToggleSwitch("Kopieren", false);
        var row = Lay(new SettingRow("In die Zwischenablage kopieren", null, toggle));
        var chrome = Find<SwitchChrome>(toggle).Single();
        Assert.Equal(0, chrome.Progress);
        PressBehavior.PointerOverride = _ => new Point(5, 5);
        toggle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        toggle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        Assert.True(toggle.IsChecked);
        Assert.Same(MuiTheme.Light[UiTokens.Keys.AccentSurface], chrome.Fill);

        row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        Assert.False(toggle.IsChecked);   // a click on the row (not on the switch) flips it, too
        Assert.True(row.ActualHeight + row.Margin.Top + row.Margin.Bottom >= UiTokens.Height.RowMin);
        Assert.Equal("Kopieren", UIElementAutomationPeer.CreatePeerForElement(toggle).GetName());
    });

    [Fact]
    public void SelectChoosesWithArrowKeysAndShowsTheValue() => WpfThread.Run(() =>
    {
        var select = new Select("Format");
        select.Add("PNG", 0);
        select.Add("JPEG", 1);
        select.SelectedIndex = 0;
        var (window, source) = Host(select);
        try
        {
            Key(select, source, System.Windows.Input.Key.Down);
            Assert.Equal(1, select.SelectedValue);
            Key(select, source, System.Windows.Input.Key.Up);
            Assert.Equal(0, select.SelectedValue);
            Assert.Equal("Format", UIElementAutomationPeer.CreatePeerForElement(select).GetName());
        }
        finally { window.Dispose(); }
    });

    [Fact]
    public void MenuEntriesHaveNamesKeysAndOneSeparator() => WpfThread.Run(() =>
    {
        var menu = new KitMenu();
        var copy = menu.Add("Kopieren", () => { }, IconGlyphs.Copy, "Strg+C");
        menu.AddSeparator();
        menu.Add("Schließen", () => { });
        Assert.Equal(3, menu.Items.Count);
        Assert.IsType<KitSeparator>(menu.Items[1]);
        Assert.Equal("Strg+C", copy.InputGestureText);
        Assert.Equal("Kopieren", UIElementAutomationPeer.CreatePeerForElement(copy).GetName());
        var alone = Lay(new KitMenuItem("Kopieren", IconGlyphs.Copy, "Strg+C"));
        var chrome = Find<RoleChrome>(alone).Single();
        Assert.Null(chrome.Fill);   // plain at rest
        Assert.Contains(Find<TextBlock>(alone), t => t.Text == "Strg+C");
        Assert.Contains(Find<TextBlock>(alone), t => t.Text == "Kopieren");
    });

    [Theory]
    [InlineData("Shift+Alt+4", "Umschalt|Alt|4")]
    [InlineData("alt + shift + 4", "Umschalt|Alt|4")]
    [InlineData("Ctrl+Print", "Strg|Druck")]
    [InlineData("Druck", "Druck")]
    [InlineData("Strg+Leertaste", "Strg|Leertaste")]
    [InlineData("Ctrl+Num5", "Strg|Num 5")]
    [InlineData("Win+Shift+S", "Umschalt|Win|S")]
    public void KeyCapsReadLikeAGermanKeyboard(string combo, string labels) =>
        Assert.Equal(labels, string.Join("|", HotkeyText.Labels(combo)));

    [Theory]
    [InlineData("Shift+Alt+4", true, "None")]
    [InlineData("Druck", true, "None")]
    [InlineData("Shift+Print", true, "None")]
    [InlineData("Pause", true, "None")]
    [InlineData("Ctrl+Einfg", true, "None")]
    [InlineData("Alt+Num0", true, "None")]
    [InlineData("Alt+F24", true, "None")]
    [InlineData("Shift+A", false, "ShiftOnly")]
    [InlineData("A", false, "MissingModifier")]
    [InlineData("Ctrl+Alt", false, "MissingKey")]
    [InlineData("Ctrl+A+B", false, "MultipleKeys")]
    [InlineData("Alt+Alt+4", false, "DuplicateModifier")]
    [InlineData("Alt+F25", false, "UnknownKey")]
    [InlineData("", false, "Empty")]
    public void HotkeyTextFollowsTheHostsRules(string text, bool ok, string problem)
    {
        Assert.Equal(ok, HotkeyText.TryParse(text, out _, out var actual));
        Assert.Equal(problem, actual.ToString());
    }

    [Fact]
    public void CanonicalTextAndSameness()
    {
        Assert.True(HotkeyText.TryParse("umschalt + strg + druck", out var chord, out _));
        Assert.Equal("Ctrl+Shift+Print", HotkeyText.Format(chord));
        Assert.True(HotkeyText.Same("Shift+Alt+4", "Alt+Shift+4"));
        Assert.False(HotkeyText.Same("Shift+Alt+4", "Shift+Alt+5"));
    }

    [Fact]
    public void RecorderWaitsForAKeyCancelsOnEscAndTakesPrint()
    {
        var recorder = new HotkeyRecorder();
        Assert.Equal(HotkeyRecorder.Outcome.Pending, recorder.Press(System.Windows.Input.Key.LeftShift, ModifierKeys.Shift));
        Assert.Equal("Shift", recorder.HeldText());
        Assert.Equal(HotkeyRecorder.Outcome.Done, recorder.Press(System.Windows.Input.Key.D4, ModifierKeys.Shift | ModifierKeys.Alt));
        Assert.Equal("Shift+Alt+4", recorder.Result);
        Assert.Equal(HotkeyRecorder.Outcome.Cancel, recorder.Press(System.Windows.Input.Key.Escape, ModifierKeys.None));
        Assert.Equal(HotkeyRecorder.Outcome.Done, recorder.Press(System.Windows.Input.Key.Snapshot, ModifierKeys.None));
        Assert.Equal("Print", recorder.Result);
        Assert.Equal(HotkeyRecorder.Outcome.Done, recorder.Press(System.Windows.Input.Key.NumPad7, ModifierKeys.Control));
        Assert.Equal("Ctrl+Num7", recorder.Result);
        Assert.Equal(HotkeyRecorder.Outcome.Unsupported, recorder.Press(System.Windows.Input.Key.OemComma, ModifierKeys.Control));
    }

    [Fact]
    public void HotkeyBoxRecordsChecksAtOnceAndKeepsTheOldOneOnRefusal() => WpfThread.Run(() =>
    {
        var checker = new StructuralHotkeyChecker(() => new[] { ("die Aufnahme", "Shift+Alt+5") });
        var box = Lay(new HotkeyBox("Kurzbefehl", "Shift+Alt+4", "Shift+Alt+4", checker));
        var messages = new List<(string, Status)>();
        var applied = new List<string>();
        var capture = new List<bool>();
        box.Message += (text, status) => messages.Add((text, status));
        box.Applied += applied.Add;
        box.CaptureChanged += capture.Add;

        box.StartCapture();
        Assert.True(box.Capturing);
        Assert.Equal(UiText.Hotkeys.Capture, messages.Last().Item1);
        box.Try("Alt+Shift+5");   // the other action's keys
        Assert.True(box.Capturing);
        Assert.Equal(Status.Error, messages.Last().Item2);
        Assert.Contains("Aufnahme", messages.Last().Item1);
        Assert.Equal("Shift+Alt+4", box.Combo);
        box.Try("Shift+B");
        Assert.Equal(UiText.Hotkeys.ShiftOnly, messages.Last().Item1);

        box.Try("Print");
        Assert.False(box.Capturing);
        Assert.Equal(new[] { "Print" }, applied);
        Assert.Equal("Print", box.Combo);
        Assert.Equal(new[] { true, false }, capture);

        box.Refuse("Shift+Alt+4", UiText.Hotkeys.Taken);   // the host could not register it: the old one stays
        Assert.Equal("Shift+Alt+4", box.Combo);
        Assert.Equal(Status.Error, messages.Last().Item2);

        box.StartCapture();
        box.Handle(HotkeyRecorder.Outcome.Cancel);   // Esc
        Assert.False(box.Capturing);
    });

    [Fact]
    public void SettingRowShowsItsErrorInPlace() => WpfThread.Run(() =>
    {
        var row = Lay(new SettingRow("Speicherort", "C:\\Bilder", new SecondaryButton("Ändern")));
        row.ShowStatus("Ordner fehlt", Status.Error);
        var description = Find<TextBlock>(row).Single(t => t.Text == "Ordner fehlt");
        Assert.Same(MuiTheme.Light[UiTokens.Keys.Error], description.Foreground);
        Assert.Equal(Status.Error, row.Status);
        row.ShowStatus("", Status.None);
        Assert.Equal("C:\\Bilder", row.Description);
        Assert.True(row.ActualHeight + row.Margin.Top + row.Margin.Bottom >= UiTokens.Height.RowMin);
    });

    [Fact]
    public void CardKeepsItsErrorState() => WpfThread.Run(() =>
    {
        var card = Lay(new Card());
        card.Body.Children.Add(card.Header);
        card.ShowError("Nicht gespeichert", "Ordner fehlt");
        Assert.True(card.IsError);
        Assert.True(card.Mark.Warning);
        Assert.Same(MuiTheme.Light[UiTokens.Keys.Error], card.Title.Foreground);
        card.ShowNormal(IconGlyphs.Accept, "Gespeichert", "a.png");
        Assert.False(card.IsError);
    });

    /// <summary>Every kit control has a name for screen readers; a new control type must be added here.</summary>
    [Fact]
    public void NoKitControlWithoutAScreenReaderName() => WpfThread.Run(() =>
    {
        var samples = new List<Control>
        {
            new PrimaryButton("Speichern"), new SecondaryButton("Ändern", icon: IconGlyphs.Folder), new IconButton(IconGlyphs.ChromeClose, "Schließen"),
            new ToolButton(IconGlyphs.Pen, "Stift"), new ActionButton(IconGlyphs.Edit, "Bearbeiten"), new ToggleSwitch("Kopieren"),
            new Select("Format"), new KitMenuItem("Kopieren", null, null), new KitButton { Content = "Text" }, new KitToggleButton { Content = "Text" },
            new SelectItem { Content = "PNG" },
        };
        foreach (var control in samples)
        {
            Lay(control);
            string name = UIElementAutomationPeer.CreatePeerForElement(control).GetName();
            Assert.False(string.IsNullOrWhiteSpace(name), control.GetType().Name + " has no name");
            Assert.DoesNotContain(name, c => c >= (char)0xE000 && c <= (char)0xF8FF);
        }
        var covered = new HashSet<Type>(samples.Select(s => s.GetType())) { typeof(KitMenu), typeof(KitSeparator) };
        var kit = typeof(StoneChrome).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(StoneChrome).Namespace && typeof(Control).IsAssignableFrom(t) && !typeof(Window).IsAssignableFrom(t) && !t.IsAbstract && !t.IsNested);
        Assert.DoesNotContain(kit, t => !covered.Contains(t));
        Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(new KeyCombo("Shift+Alt+4"))));
        Assert.Equal("Umschalt+Alt+4", AutomationProperties.GetName(new KeyCombo("Shift+Alt+4")));
    });

    [Theory]
    [MemberData(nameof(PixelMatrixTests.Variants), MemberType = typeof(PixelMatrixTests))]
    [Trait("Category", "Pixel")]
    public void ComponentSheet(bool light, double scale) => WpfThread.Run(() =>
    {
        PixelComparison.RequireFonts();
        var check = new PixelComparison();
        var sheet = KitSamples.Components();
        MuiTheme.Apply(sheet.Resources, light);
        check.Check("kit_components." + (light ? "light" : "dark") + "." + ((int)Math.Round(scale * 100)).ToString(CultureInfo.InvariantCulture),
            PixelMatrixTests.Draw(sheet, scale));
        check.AssertAllMatch();
    });
}
