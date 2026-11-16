namespace MosaikCapture.Engine.MosaikUi;

/// <summary>
/// The buttons of the kit (layer 4), all a <see cref="KitButton"/> or <see cref="KitToggleButton"/> with the stone
/// template: only role, size and content differ. Rule of Hams: one main action (<see cref="PrimaryButton"/>) per
/// surface, everything else secondary or plain. Every height is the face; the side (depth 2) comes on top.
/// </summary>
internal static class ButtonContent
{
    /// <summary>Text, or icon and text side by side, in the button role's font.</summary>
    public static object Label(string text, string? icon)
    {
        var label = TextStyle.Apply(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }, TextRole.Button);
        if (icon == null) return label;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = TextStyle.Icon(icon, 15);
        glyph.Margin = new Thickness(0, 0, UiTokens.Space.Md, 0);
        row.Children.Add(glyph);
        row.Children.Add(label);
        return row;
    }

    /// <summary>The accessible name of a button that shows a <see cref="Label"/>: its text.</summary>
    public static void Name(FrameworkElement button, string text) => System.Windows.Automation.AutomationProperties.SetName(button, text);
}

/// <summary>The one main action of a surface: accent fill, 36 high.</summary>
internal sealed class PrimaryButton : KitButton
{
    public PrimaryButton(string text, Action? click = null, string? icon = null)
    {
        Mui.SetRole(this, MuiRole.Primary);
        Height = UiTokens.Height.Primary + UiTokens.Depth.Control;
        MinWidth = 96;
        Padding = new Thickness(18, 0, 18, 0);
        Content = ButtonContent.Label(text, icon);
        ButtonContent.Name(this, text);
        if (click != null) Click += (_, _) => click();
    }
}

/// <summary>Every other action: a paper stone, 32 high, optionally with an icon on the left.</summary>
internal sealed class SecondaryButton : KitButton
{
    public SecondaryButton(string text, Action? click = null, string? icon = null, MuiRole role = MuiRole.Secondary)
    {
        Mui.SetRole(this, role);
        Height = UiTokens.Height.Standard + UiTokens.Depth.Control;
        MinWidth = 72;
        Padding = new Thickness(14, 0, 14, 0);
        Content = ButtonContent.Label(text, icon);
        ButtonContent.Name(this, text);
        if (click != null) Click += (_, _) => click();
    }

    /// <summary>Changes the label (and the accessible name with it).</summary>
    public void SetText(string text, string? icon = null)
    {
        Content = ButtonContent.Label(text, icon);
        ButtonContent.Name(this, text);
    }
}

/// <summary>A round stone with one icon (close, more, cancel); no surface at rest. Needs a tooltip: it is its name.</summary>
internal sealed class IconButton : KitButton
{
    public IconButton(string glyph, string tooltip, Action? click = null, double size = UiTokens.Height.Standard, MuiRole role = MuiRole.Plain)
    {
        Mui.SetRole(this, role);
        Width = size;
        Height = size + UiTokens.Depth.Control;
        Mui.SetRadius(this, UiTokens.Radius.Pill(size));
        Padding = new Thickness(0);
        Content = TextStyle.Icon(glyph, Math.Round(size / 2));
        ToolTip = Kit.ToolTip(tooltip);
        System.Windows.Automation.AutomationProperties.SetName(this, tooltip);
        if (click != null) Click += (_, _) => click();
    }
}

/// <summary>
/// A tool of the editor or the recording bar: a square plain stone that stays down while its tool is chosen
/// (<c>IsChecked</c>). Not a toggle for the keyboard user: Space chooses the tool.
/// </summary>
internal sealed class ToolButton : KitToggleButton
{
    public const double Size = 36;

    public ToolButton(string glyph, string tooltip, Action? click = null)
    {
        Mui.SetRole(this, MuiRole.Plain);
        Width = Size;
        Height = Size + UiTokens.Depth.Control;
        Padding = new Thickness(0);
        Content = TextStyle.Icon(glyph, 16);
        ToolTip = Kit.ToolTip(tooltip);
        System.Windows.Automation.AutomationProperties.SetName(this, tooltip);
        if (click != null) Click += (_, _) => click();
    }
}

/// <summary>An action of a card: icon above a short caption, plain until hovered; the caption is the name.</summary>
internal sealed class ActionButton : KitButton
{
    readonly TextBlock caption;

    public ActionButton(string glyph, string text, Action? click = null)
    {
        Mui.SetRole(this, MuiRole.Plain);
        Mui.SetRadius(this, UiTokens.Radius.Button);
        Padding = new Thickness(UiTokens.Space.Sm, 7, UiTokens.Space.Sm, 6);
        Margin = new Thickness(UiTokens.Space.Xs, 0, UiTokens.Space.Xs, 0);
        var column = new StackPanel();
        column.Children.Add(TextStyle.Icon(glyph, 15));
        caption = TextStyle.Apply(new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, UiTokens.Space.Sm, 0, 0) }, TextRole.Caption);
        caption.FontSize = 11;
        column.Children.Add(caption);
        Content = column;
        System.Windows.Automation.AutomationProperties.SetName(this, text);
        if (click != null) Click += (_, _) => click();
    }

    /// <summary>Why the action is not available now (a disabled action says why in its tooltip, Hams' rule).</summary>
    public void SetAvailable(bool available, string? reason = null)
    {
        IsEnabled = available;
        ToolTip = available || reason == null ? null : Kit.ToolTip(reason);
        ToolTipService.SetShowOnDisabled(this, true);
    }
}
