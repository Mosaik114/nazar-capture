using System.Windows.Controls.Primitives;

namespace NazarCapture.Engine.NazarUi;

/// <summary>
/// Pressing like a Windows button, for every kit control the same (Hams' Pressable in states.py): the press holds until
/// release. If the pointer slides out, the surface rises; if it comes back, it sinks again. It fires on release over
/// the control, and only if the press started there. A disabled or loading control does not arm.
/// </summary>
internal sealed class PressTracker
{
    /// <summary>A press started on the control and has not been released or cancelled.</summary>
    public bool Armed { get; private set; }

    /// <summary>The surface shows pressed: armed and the pointer inside.</summary>
    public bool Pressed { get; private set; }

    public bool Down(bool interactive)
    {
        Armed = interactive;
        Pressed = interactive;
        return Pressed;
    }

    /// <summary>The pointer moved; returns whether the surface shows pressed now.</summary>
    public bool Move(bool inside)
    {
        Pressed = Armed && inside;
        return Pressed;
    }

    /// <summary>Released; returns whether this completes a click.</summary>
    public bool Up(bool inside, bool interactive)
    {
        bool click = Armed && inside && interactive;
        Armed = Pressed = false;
        return click;
    }

    /// <summary>Capture lost (another window, Esc): no click.</summary>
    public void Cancel() => Armed = Pressed = false;
}

/// <summary>
/// The press behaviour of the kit's buttons on top of WPF's ButtonBase: the mouse part goes through one
/// <see cref="PressTracker"/>, Space and Enter stay WPF's (they press and release like a click, as in Windows).
/// Where the pointer is comes from the event; tests may replace it (<see cref="PointerOverride"/>), because synthetic
/// mouse events carry no position of their own.
/// </summary>
internal static class PressBehavior
{
    /// <summary>
    /// The pointer press of a kit button. ButtonBase.IsPressed cannot be set from outside WPF; it stays the keyboard's
    /// (Space), this one is the pointer's, and the chrome shows either.
    /// </summary>
    public static readonly DependencyProperty PressedProperty = DependencyProperty.RegisterAttached(
        "Pressed", typeof(bool), typeof(PressBehavior), new FrameworkPropertyMetadata(false));

    public static bool GetPressed(DependencyObject element) => (bool)element.GetValue(PressedProperty);

    internal static void SetPressed(DependencyObject element, bool value) => element.SetValue(PressedProperty, value);

    /// <summary>Whether the control shows pressed, by pointer or keyboard.</summary>
    public static bool IsPressed(Control control) => GetPressed(control) || control is ButtonBase { IsPressed: true };

    /// <summary>Tests only: the pointer position relative to a control.</summary>
    internal static Func<UIElement, Point>? PointerOverride;

    static bool Inside(ButtonBase button, MouseEventArgs e)
    {
        var p = PointerOverride?.Invoke(button) ?? e.GetPosition(button);
        return p.X >= 0 && p.Y >= 0 && p.X <= button.ActualWidth && p.Y <= button.ActualHeight;
    }

    static bool Interactive(ButtonBase button) => button.IsEnabled && !Mui.GetIsLoading(button);

    public static bool Down(ButtonBase button, PressTracker tracker, MouseButtonEventArgs e, Action<bool> setPressed)
    {
        if (button.ClickMode != ClickMode.Release) return false;
        e.Handled = true;
        button.Focus();
        if (!tracker.Down(Interactive(button))) return true;
        button.CaptureMouse();
        setPressed(true);
        return true;
    }

    public static void Move(ButtonBase button, PressTracker tracker, MouseEventArgs e, Action<bool> setPressed)
    {
        if (!tracker.Armed) return;
        bool before = tracker.Pressed;
        if (tracker.Move(Inside(button, e)) != before) setPressed(tracker.Pressed);
    }

    /// <summary>Returns whether the release completes a click.</summary>
    public static bool Up(ButtonBase button, PressTracker tracker, MouseButtonEventArgs e, Action<bool> setPressed)
    {
        if (!tracker.Armed) return false;
        e.Handled = true;
        bool click = tracker.Up(Inside(button, e), Interactive(button));
        if (button.IsMouseCaptured) button.ReleaseMouseCapture();
        setPressed(false);
        return click;
    }
}

/// <summary>The kit's button (layer 3 behaviour): roles via <see cref="Mui"/>, the press of <see cref="PressTracker"/>.</summary>
internal class KitButton : Button
{
    readonly PressTracker press = new();

    void Show(bool pressed) => PressBehavior.SetPressed(this, pressed);

    public KitButton()
    {
        FocusVisualStyle = null;
        Template = KitTemplates.Stone;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        InputModality.Install();
        Loaded += (_, _) => Mui.EnsureAutomationName(this, Content);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!PressBehavior.Down(this, press, e, Show)) base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (press.Armed) PressBehavior.Move(this, press, e, Show);
        else base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!press.Armed) { base.OnMouseLeftButtonUp(e); return; }
        if (PressBehavior.Up(this, press, e, Show)) OnClick();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (press.Armed && !IsMouseCaptured) { press.Cancel(); Show(false); }
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
    {
        Mui.EnsureAutomationName(this, Content);
        return base.OnCreateAutomationPeer();
    }
}

/// <summary>The kit's toggle (tool buttons, switches): same press as <see cref="KitButton"/>.</summary>
internal class KitToggleButton : ToggleButton
{
    readonly PressTracker press = new();

    void Show(bool pressed) => PressBehavior.SetPressed(this, pressed);

    public KitToggleButton()
    {
        FocusVisualStyle = null;
        Template = KitTemplates.Stone;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        InputModality.Install();
        Loaded += (_, _) => Mui.EnsureAutomationName(this, Content);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!PressBehavior.Down(this, press, e, Show)) base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (press.Armed) PressBehavior.Move(this, press, e, Show);
        else base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!press.Armed) { base.OnMouseLeftButtonUp(e); return; }
        if (PressBehavior.Up(this, press, e, Show)) OnClick();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (press.Armed && !IsMouseCaptured) { press.Cancel(); Show(false); }
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
    {
        Mui.EnsureAutomationName(this, Content);
        return base.OnCreateAutomationPeer();
    }
}
