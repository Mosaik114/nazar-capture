using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;

namespace MosaikCapture.Engine.MosaikUi;

/// <summary>The states a control knows (Hams' State). Exactly one decides the surface.</summary>
internal enum SurfaceState
{
    Default,
    Hover,
    Pressed,
    Selected,
    Loading,
    Disabled,
    Error,
}

/// <summary>
/// The colour of every control state at one place (Hams' ControlStyle): role and state give fill, text and side as
/// token keys. Keys, not colours: they resolve through the window's token dictionary and so follow a theme change.
/// </summary>
internal static class Roles
{
    /// <summary>Fill (null: no surface of its own, the paper shows) and text of a role in a state.</summary>
    internal readonly record struct Look(string? Fill, string Text, string Side);

    /// <summary>
    /// Highest rank first (Hams' SURFACE_ORDER): disabled never looks like hover, pressed beats hover. Focus is not a
    /// surface state; it is the edge and shows on top of any of these.
    /// </summary>
    public static SurfaceState State(bool enabled, bool error, bool loading, bool pressed, bool hover, bool selected) =>
        !enabled ? SurfaceState.Disabled
        : error ? SurfaceState.Error
        : loading ? SurfaceState.Loading
        : pressed ? SurfaceState.Pressed
        : hover ? SurfaceState.Hover
        : selected ? SurfaceState.Selected
        : SurfaceState.Default;

    /// <summary>Whether a control in this state reacts to input (Hams' is_interactive).</summary>
    public static bool IsInteractive(SurfaceState state) => state is not (SurfaceState.Disabled or SurfaceState.Loading);

    /// <summary>Whether the focus edge shows: only for an operable control.</summary>
    public static bool ShowsFocus(bool focused, SurfaceState state) => focused && state != SurfaceState.Disabled;

    public static Look Resolve(MuiRole role, SurfaceState state)
    {
        var (fill, text) = Table(role, state);
        return new Look(fill, text, Side(role, state));
    }

    /// <summary>
    /// The focus edge's colour: <see cref="UiTokens.Keys.Focus"/>, but on a filled role (accent, red, violet) the focus
    /// colour would vanish (1.07:1 on the accent), so the edge takes the role's text colour there (5:1 and more).
    /// </summary>
    public static string FocusInk(Look look) =>
        look.Fill != null && look.Text is UiTokens.Keys.OnAccent or UiTokens.Keys.OnFill ? look.Text : UiTokens.Keys.Focus;

    static (string? Fill, string Text) Table(MuiRole role, SurfaceState state) => role switch
    {
        MuiRole.Primary => state switch
        {
            SurfaceState.Hover => (UiTokens.Keys.AccentHover, UiTokens.Keys.OnAccent),
            SurfaceState.Pressed => (UiTokens.Keys.AccentPressed, UiTokens.Keys.OnAccent),
            SurfaceState.Loading => (UiTokens.Keys.ControlSurface, UiTokens.Keys.TextSecondary),
            SurfaceState.Disabled => (UiTokens.Keys.ControlSurface, UiTokens.Keys.TextDisabled),
            SurfaceState.Error => (UiTokens.Keys.ErrorSurface, UiTokens.Keys.OnFill),
            _ => (UiTokens.Keys.AccentSurface, UiTokens.Keys.OnAccent),
        },
        MuiRole.Plain => state switch
        {
            SurfaceState.Hover => (UiTokens.Keys.ControlSurface, UiTokens.Keys.TextPrimary),
            SurfaceState.Pressed => (UiTokens.Keys.ControlPressed, UiTokens.Keys.TextPrimary),
            SurfaceState.Selected => (UiTokens.Keys.ControlSurface, UiTokens.Keys.TextPrimary),
            SurfaceState.Loading => (null, UiTokens.Keys.TextTertiary),
            SurfaceState.Disabled => (null, UiTokens.Keys.TextDisabled),
            SurfaceState.Error => (null, UiTokens.Keys.Error),
            _ => (null, UiTokens.Keys.TextSecondary),
        },
        // A tab says where you are, not what to do: a word on the paper, the chosen one on a sheet of its own.
        MuiRole.Tab => state switch
        {
            SurfaceState.Hover => (UiTokens.Keys.ControlSurface, UiTokens.Keys.TextPrimary),
            SurfaceState.Pressed => (UiTokens.Keys.ControlPressed, UiTokens.Keys.TextPrimary),
            SurfaceState.Selected => (UiTokens.Keys.ControlSurface, UiTokens.Keys.TextPrimary),
            SurfaceState.Loading => (null, UiTokens.Keys.TextTertiary),
            SurfaceState.Disabled => (null, UiTokens.Keys.TextDisabled),
            SurfaceState.Error => (null, UiTokens.Keys.Error),
            _ => (null, UiTokens.Keys.TextSecondary),
        },
        MuiRole.TabSelected => state switch
        {
            SurfaceState.Loading => (UiTokens.Keys.GroupSurface, UiTokens.Keys.TextSecondary),
            SurfaceState.Disabled => (UiTokens.Keys.GroupSurface, UiTokens.Keys.TextDisabled),
            SurfaceState.Error => (UiTokens.Keys.ErrorSurface, UiTokens.Keys.OnFill),
            _ => (UiTokens.Keys.GroupSurface, UiTokens.Keys.AccentTint),
        },
        MuiRole.Recording => Filled(state, UiTokens.Keys.RecordingSurface, UiTokens.Keys.RecordingHover, UiTokens.Keys.RecordingPressed),
        MuiRole.Ai => Filled(state, UiTokens.Keys.AiSurface, UiTokens.Keys.AiHover, UiTokens.Keys.AiPressed),
        _ => state switch
        {
            SurfaceState.Hover => (UiTokens.Keys.ControlHover, UiTokens.Keys.TextPrimary),
            SurfaceState.Pressed => (UiTokens.Keys.ControlPressed, UiTokens.Keys.TextPrimary),
            SurfaceState.Selected => (UiTokens.Keys.ControlHover, UiTokens.Keys.TextPrimary),
            SurfaceState.Loading => (UiTokens.Keys.ControlSurface, UiTokens.Keys.TextSecondary),
            SurfaceState.Disabled => (UiTokens.Keys.ControlSurface, UiTokens.Keys.TextDisabled),
            SurfaceState.Error => (UiTokens.Keys.ErrorSurface, UiTokens.Keys.OnFill),
            _ => (UiTokens.Keys.ControlSurface, UiTokens.Keys.TextPrimary),
        },
    };

    static (string? Fill, string Text) Filled(SurfaceState state, string surface, string hover, string pressed) => state switch
    {
        SurfaceState.Hover => (hover, UiTokens.Keys.OnFill),
        SurfaceState.Pressed => (pressed, UiTokens.Keys.OnFill),
        SurfaceState.Loading => (UiTokens.Keys.ControlSurface, UiTokens.Keys.TextSecondary),
        SurfaceState.Disabled => (UiTokens.Keys.ControlSurface, UiTokens.Keys.TextDisabled),
        SurfaceState.Error => (UiTokens.Keys.ErrorSurface, UiTokens.Keys.OnFill),
        _ => (surface, UiTokens.Keys.OnFill),
    };

    static string Side(MuiRole role, SurfaceState state) => state switch
    {
        SurfaceState.Error => UiTokens.Keys.ErrorSide,
        SurfaceState.Loading or SurfaceState.Disabled => UiTokens.Keys.ControlSide,
        _ => role switch
        {
            MuiRole.Primary => UiTokens.Keys.AccentSide,
            MuiRole.Recording => UiTokens.Keys.RecordingSide,
            MuiRole.Ai => UiTokens.Keys.AiSide,
            MuiRole.Tab or MuiRole.TabSelected => UiTokens.Keys.WindowBg,   // tabs lie flat: no stone edge shows
            _ => UiTokens.Keys.ControlSide,
        },
    };
}

/// <summary>
/// Layer 3 on top of the painter: a <see cref="StoneChrome"/> that follows the control it decorates. It reads the
/// control's state (hover, pressed, enabled, checked, <see cref="Mui"/> role, error, loading, visible focus), asks
/// <see cref="Roles"/> for the look and sets fill, side, ink, focus colour and the text colour of its content (as
/// resource references). Every kit control's template is this chrome around its content; no control colours itself.
/// </summary>
internal class RoleChrome : StoneChrome
{
    Control? owner;

    // The owner's properties the look depends on; the type says on which controls a property exists (null: attached).
    static readonly (DependencyProperty Property, Type? On)[] Watched =
    {
        (UIElement.IsMouseOverProperty, null), (UIElement.IsEnabledProperty, null), (ButtonBase.IsPressedProperty, typeof(ButtonBase)),
        (PressBehavior.PressedProperty, null), (ToggleButton.IsCheckedProperty, typeof(ToggleButton)), (Mui.RoleProperty, null),
        (Mui.ShowFocusProperty, null), (Mui.IsErrorProperty, null), (Mui.IsLoadingProperty, null), (Selector.IsSelectedProperty, null),
        (MenuItem.IsHighlightedProperty, typeof(MenuItem)), (ComboBoxItem.IsHighlightedProperty, typeof(ComboBoxItem)),
        (ComboBox.IsDropDownOpenProperty, typeof(ComboBox)),
    };

    // One mirror property per watched owner property, bound one way: bindings listen weakly, so the chrome never keeps
    // its control alive (a DependencyPropertyDescriptor.AddValueChanged would, until removed).
    static readonly DependencyProperty[] Mirrors = Watched.Select((_, i) => DependencyProperty.Register(
        "Mirror" + i.ToString(CultureInfo.InvariantCulture), typeof(object), typeof(RoleChrome),
        new PropertyMetadata(null, (d, _) => ((RoleChrome)d).Refresh()))).ToArray();

    /// <summary>The look shown now (tests and the sample sheet read it).</summary>
    public Roles.Look Look { get; protected set; }

    public SurfaceState State { get; protected set; }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        if (owner == null && TemplatedParent is Control control) Follow(control);
    }

    /// <summary>Follows <paramref name="control"/> (in templates the templated parent is used automatically).</summary>
    public void Follow(Control control)
    {
        owner = control;
        for (int i = 0; i < Watched.Length; i++)
        {
            var (property, on) = Watched[i];
            if (on != null && !on.IsInstanceOfType(control)) continue;
            BindingOperations.SetBinding(this, Mirrors[i], new System.Windows.Data.Binding { Path = new PropertyPath(property), Source = control, Mode = System.Windows.Data.BindingMode.OneWay });
        }
        Refresh();
    }

    /// <summary>The control this chrome follows.</summary>
    protected Control? Owner => owner;

    /// <summary>The surface state of a control: hover includes keyboard highlight in menus and lists, an open list is selected.</summary>
    public static SurfaceState StateOf(Control control)
    {
        bool pressed = PressBehavior.IsPressed(control);
        bool hover = control.IsMouseOver || control is MenuItem { IsHighlighted: true } || control is ComboBoxItem { IsHighlighted: true };
        bool selected = control is ToggleButton { IsChecked: true } || Selector.GetIsSelected(control) || control is ComboBox { IsDropDownOpen: true };
        return Roles.State(control.IsEnabled, Mui.GetIsError(control), Mui.GetIsLoading(control), pressed, hover, selected);
    }

    /// <summary>Recomputes the look from the owner's state.</summary>
    public virtual void Refresh()
    {
        var control = owner;
        if (control == null) return;
        State = StateOf(control);
        Look = Roles.Resolve(Mui.GetRole(control), State);

        if (Look.Fill == null)
        {
            Fill = null;
            Ink = null;
            Side = Brushes.Transparent;   // keeps the room of the side, so the content does not jump on hover
        }
        else
        {
            SetResourceReference(FillProperty, Look.Fill);
            SetResourceReference(InkProperty, UiTokens.Keys.Ink);
            SetResourceReference(SideProperty, Look.Side);
        }
        SetResourceReference(FocusInkProperty, Roles.FocusInk(Look));
        SetResourceReference(TextElement.ForegroundProperty, Look.Text);
        // The content is the control's logical child and inherits from the control, not from this chrome.
        control.SetResourceReference(Control.ForegroundProperty, Look.Text);
        IsPressed = State == SurfaceState.Pressed;
        ShowFocus = Roles.ShowsFocus(Mui.GetShowFocus(control), State);
    }
}
