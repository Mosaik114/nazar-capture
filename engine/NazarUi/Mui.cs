using System.Windows.Automation;

namespace NazarCapture.Engine.NazarUi;

/// <summary>The role of a control: which fills, sides and text colours its states use (Hams' ControlStyle roles).</summary>
internal enum MuiRole
{
    /// <summary>A stone in the paper colour: the default for every button.</summary>
    Secondary,
    /// <summary>The one main action of a surface, in the accent colour.</summary>
    Primary,
    /// <summary>No surface at rest (icon buttons, menu items, tool buttons); hover and press show a stone.</summary>
    Plain,
    /// <summary>Recording and destructive actions (stop, discard), in the recording red.</summary>
    Recording,
    /// <summary>Text recognition and other assisted actions.</summary>
    Ai,
    /// <summary>A tab: a word on the paper; hover and press show a sheet (Hams' role "tab").</summary>
    Tab,
    /// <summary>The chosen tab: a paper sheet with ink edge, its word in the accent colour (Hams' "tab_selected").</summary>
    TabSelected,
}

/// <summary>
/// The attached properties of the kit (layer 3), usable on any element: role, visible keyboard focus, error and
/// loading state. Controls read them in their templates; nothing else sets colours by hand.
/// </summary>
internal static class Mui
{
    public static readonly DependencyProperty RoleProperty = DependencyProperty.RegisterAttached(
        "Role", typeof(MuiRole), typeof(Mui), new FrameworkPropertyMetadata(MuiRole.Secondary, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Set by <see cref="InputModality"/> on the focused element while the focus came from the keyboard.</summary>
    public static readonly DependencyProperty ShowFocusProperty = DependencyProperty.RegisterAttached(
        "ShowFocus", typeof(bool), typeof(Mui), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsErrorProperty = DependencyProperty.RegisterAttached(
        "IsError", typeof(bool), typeof(Mui), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.RegisterAttached(
        "IsLoading", typeof(bool), typeof(Mui), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Corner radius of the control's stone (default: <see cref="UiTokens.Radius.Control"/>).</summary>
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(double), typeof(Mui), new FrameworkPropertyMetadata(UiTokens.Radius.Control, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Depth of the control's side (default: <see cref="UiTokens.Depth.Control"/>).</summary>
    public static readonly DependencyProperty DepthProperty = DependencyProperty.RegisterAttached(
        "Depth", typeof(double), typeof(Mui), new FrameworkPropertyMetadata(UiTokens.Depth.Control, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Whether the control's stone is a squircle (default) or a plain rounded shape (round icon stones).</summary>
    public static readonly DependencyProperty SquircleProperty = DependencyProperty.RegisterAttached(
        "Squircle", typeof(bool), typeof(Mui), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static bool GetSquircle(DependencyObject element) => (bool)element.GetValue(SquircleProperty);
    public static void SetSquircle(DependencyObject element, bool value) => element.SetValue(SquircleProperty, value);
    public static double GetRadius(DependencyObject element) => (double)element.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject element, double value) => element.SetValue(RadiusProperty, value);
    public static double GetDepth(DependencyObject element) => (double)element.GetValue(DepthProperty);
    public static void SetDepth(DependencyObject element, double value) => element.SetValue(DepthProperty, value);
    public static MuiRole GetRole(DependencyObject element) => (MuiRole)element.GetValue(RoleProperty);
    public static void SetRole(DependencyObject element, MuiRole value) => element.SetValue(RoleProperty, value);
    public static bool GetShowFocus(DependencyObject element) => (bool)element.GetValue(ShowFocusProperty);
    public static void SetShowFocus(DependencyObject element, bool value) => element.SetValue(ShowFocusProperty, value);
    public static bool GetIsError(DependencyObject element) => (bool)element.GetValue(IsErrorProperty);
    public static void SetIsError(DependencyObject element, bool value) => element.SetValue(IsErrorProperty, value);
    public static bool GetIsLoading(DependencyObject element) => (bool)element.GetValue(IsLoadingProperty);
    public static void SetIsLoading(DependencyObject element, bool value) => element.SetValue(IsLoadingProperty, value);

    /// <summary>
    /// The rule of the kit: every control has a name for screen readers. Without an explicit one it takes the
    /// tooltip, else its visible text; icon glyphs (private-use characters) never count as a name.
    /// </summary>
    public static void EnsureAutomationName(FrameworkElement element, object? label = null)
    {
        if (!string.IsNullOrEmpty(AutomationProperties.GetName(element))) return;
        string? name = Readable(element.ToolTip) ?? Readable(label);
        if (name != null) AutomationProperties.SetName(element, name);
    }

    /// <summary>A text a screen reader can say, or null (empty, a glyph, not text).</summary>
    internal static string? Readable(object? value)
    {
        string? text = value switch
        {
            string s => s,
            TextBlock block => block.Text,
            ToolTip { Content: var content } => Readable(content),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(text)) return null;
        string cleaned = new(text!.Where(c => c < (char)0xE000 || c > (char)0xF8FF).ToArray());   // no private-use glyphs
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned.Trim();
    }
}
