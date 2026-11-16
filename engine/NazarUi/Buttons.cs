using System.Windows.Documents;

namespace NazarCapture.Engine.NazarUi;

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

/// <summary>
/// A round button with one icon. Two looks: plain (no surface at rest; tool bars and cards) and, with
/// <see cref="Stone"/>, Hams' IconButton: a round paper stone (34, ink edge, side)
/// with a drawn line icon, the icon in the secondary text colour, under the pointer in the primary one or in its own
/// hover colour (red for the close cross). Needs a tooltip: it is its name.
/// </summary>
internal sealed class IconButton : KitButton
{
    /// <summary>Diameter of the stone in window heads and rows (Hams' IconButton.SIZE).</summary>
    public const double StoneSize = 34;

    readonly FrameworkElement icon;
    string? hoverInk;

    public IconButton(string glyph, string tooltip, Action? click = null, double size = UiTokens.Height.Standard, MuiRole role = MuiRole.Plain)
        : this(TextStyle.Icon(glyph, Math.Round(size / 2)), tooltip, click, size, role)
    {
    }

    IconButton(FrameworkElement icon, string tooltip, Action? click, double size, MuiRole role)
    {
        this.icon = icon;
        Mui.SetRole(this, role);
        Width = size;
        Height = size + UiTokens.Depth.Control;
        Mui.SetRadius(this, UiTokens.Radius.Pill(size));
        Padding = new Thickness(0);
        Content = icon;
        ToolTip = Kit.ToolTip(tooltip);
        System.Windows.Automation.AutomationProperties.SetName(this, tooltip);
        if (click != null) Click += (_, _) => click();
    }

    /// <summary>Hams' IconButton: a round paper stone with a line icon (settings gear, close cross).</summary>
    public static IconButton Stone(LineIcon.Shape shape, string tooltip, Action? click = null, string? hoverInk = null)
    {
        var button = new IconButton(new LineIcon(shape), tooltip, click, StoneSize, MuiRole.Secondary) { hoverInk = hoverInk };
        Mui.SetSquircle(button, false);
        button.IsMouseDirectlyOverChanged += delegate { button.Tint(); };
        button.MouseEnter += delegate { button.Tint(); };
        button.MouseLeave += delegate { button.Tint(); };
        button.IsEnabledChanged += delegate { button.Tint(); };
        button.Tint();
        return button;
    }

    /// <summary>The icon colour of a stone: secondary at rest, primary (or the hover colour) under the pointer.</summary>
    internal string IconInk => !IsEnabled ? UiTokens.Keys.TextDisabled : IsMouseOver ? hoverInk ?? UiTokens.Keys.TextPrimary : UiTokens.Keys.TextSecondary;

    void Tint() => icon.SetResourceReference(TextElement.ForegroundProperty, IconInk);
}

/// <summary>
/// The drawn line icons of Hams (ui_kit/icons.py) on its 16 px grid with a stroke of 1.8 and round ends: the gear (six
/// broad teeth with soft flanks and a clear hole) and the close cross. Drawn as vectors in the inherited text colour.
/// </summary>
internal sealed class LineIcon : FrameworkElement
{
    public enum Shape { Close, Settings }

    public const double Box = 16, Stroke = 1.8;
    const int GearTeeth = 6;
    const double GearRoot = 4.3, GearTip = 6.0, GearHole = 2.0;

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(LineIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public LineIcon(Shape shape)
    {
        Kind = shape;
        Width = Height = Box;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
    }

    public Shape Kind { get; }

    public Brush? Foreground { get => (Brush?)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>The gear's outline (Hams' gear_outline): tooth tips on 6.0, roots on 4.3, smoothstep flanks.</summary>
    internal static List<Point> GearOutline(double cx, double cy, int steps = 360)
    {
        double period = 2 * Math.PI / GearTeeth, top = 0.5 * 0.75, bottom = 0.5;
        var points = new List<Point>(steps);
        for (int step = 0; step < steps; step++)
        {
            double angle = step / (double)steps * 2 * Math.PI;
            double offset = Math.Abs((((angle + Math.PI / 2 + period / 2) % period) / period) - 0.5) * 2;
            double radius;
            if (offset <= top) radius = GearTip;
            else if (offset >= bottom) radius = GearRoot;
            else
            {
                double k = (offset - top) / (bottom - top);
                radius = GearTip + (GearRoot - GearTip) * k * k * (3 - 2 * k);
            }
            points.Add(new Point(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle)));
        }
        return points;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var ink = Foreground;
        if (ink == null) return;
        var pen = new Pen(ink, Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (Kind == Shape.Close)
        {
            dc.DrawLine(pen, new Point(4.6, 4.6), new Point(11.4, 11.4));
            dc.DrawLine(pen, new Point(11.4, 4.6), new Point(4.6, 11.4));
            return;
        }
        var outline = GearOutline(8, 8);
        var gear = new StreamGeometry();
        using (var g = gear.Open())
        {
            g.BeginFigure(outline[0], false, true);
            g.PolyLineTo(outline.Skip(1).ToList(), true, true);
        }
        gear.Freeze();
        dc.DrawGeometry(null, pen, gear);
        // Hams draws the hole's outline inward from 2.0 + stroke/2, so the middle of its stroke lies on 2.0.
        dc.DrawEllipse(null, pen, new Point(8, 8), GearHole, GearHole);
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
