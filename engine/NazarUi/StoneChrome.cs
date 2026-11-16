namespace NazarCapture.Engine.NazarUi;

/// <summary>
/// The one painter of the kit: every surface (button, field, key cap, card, toolbar, menu) is a StoneChrome.
/// Ink and watercolour as in Hams' <c>render_surface</c> (ui_kit/surfaces.py):
/// <list type="bullet">
/// <item>the face: a squircle in <see cref="Fill"/>, with an ink edge (<see cref="Ink"/>, <see cref="InkWidth"/>)
/// drawn on the outline and clipped to the shape, so it never grows the surface;</item>
/// <item>the side: the same shape moved down by <see cref="Depth"/>, in <see cref="Side"/>, without an outline of its
/// own (with ink all round it read as a double line); the face is <see cref="Depth"/> lower than the element;</item>
/// <item>pressed (<see cref="IsPressed"/>): the face has sunk onto its side, the side is gone, the child moves down;</item>
/// <item>keyboard focus (<see cref="ShowFocus"/>): the ink edge becomes twice as strong in <see cref="FocusInk"/>; no ring
/// around the surface, which would cost room and shift edges.</item>
/// </list>
/// Hover, disabled and the roles only choose other brushes (the role chrome of layer 3); the painter knows no states.
/// </summary>
internal class StoneChrome : Decorator
{
    /// <summary>How much stronger the edge is while it shows the keyboard focus (Hams' FOCUS_WIDTH).</summary>
    public const double FocusWidthFactor = 2.0;

    public static readonly DependencyProperty FillProperty = Register(nameof(Fill), typeof(Brush), null, render: true);
    public static readonly DependencyProperty SideProperty = Register(nameof(Side), typeof(Brush), null, measure: true);
    public static readonly DependencyProperty InkProperty = Register(nameof(Ink), typeof(Brush), null, render: true);
    public static readonly DependencyProperty FocusInkProperty = Register(nameof(FocusInk), typeof(Brush), null, render: true);
    public static readonly DependencyProperty InkWidthProperty = Register(nameof(InkWidth), typeof(double), UiTokens.Depth.Ink, render: true);
    public static readonly DependencyProperty DepthProperty = Register(nameof(Depth), typeof(double), UiTokens.Depth.Control, measure: true);
    public static readonly DependencyProperty RadiusProperty = Register(nameof(Radius), typeof(double), UiTokens.Radius.Control, render: true);
    public static readonly DependencyProperty IsPressedProperty = Register(nameof(IsPressed), typeof(bool), false, arrange: true);
    public static readonly DependencyProperty ShowFocusProperty = Register(nameof(ShowFocus), typeof(bool), false, render: true);
    public static readonly DependencyProperty IsSquircleProperty = Register(nameof(IsSquircle), typeof(bool), true, render: true);
    public static readonly DependencyProperty PaddingProperty = Register(nameof(Padding), typeof(Thickness), default(Thickness), measure: true);

    static DependencyProperty Register(string name, Type type, object? value, bool render = false, bool measure = false, bool arrange = false)
    {
        var options = FrameworkPropertyMetadataOptions.None;
        if (render) options |= FrameworkPropertyMetadataOptions.AffectsRender;
        if (measure) options |= FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender;
        if (arrange) options |= FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender;
        return DependencyProperty.Register(name, type, typeof(StoneChrome), new FrameworkPropertyMetadata(value, options));
    }

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Side { get => (Brush?)GetValue(SideProperty); set => SetValue(SideProperty, value); }
    public Brush? Ink { get => (Brush?)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public Brush? FocusInk { get => (Brush?)GetValue(FocusInkProperty); set => SetValue(FocusInkProperty, value); }
    public double InkWidth { get => (double)GetValue(InkWidthProperty); set => SetValue(InkWidthProperty, value); }
    public double Depth { get => (double)GetValue(DepthProperty); set => SetValue(DepthProperty, value); }
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public bool IsPressed { get => (bool)GetValue(IsPressedProperty); set => SetValue(IsPressedProperty, value); }
    public bool ShowFocus { get => (bool)GetValue(ShowFocusProperty); set => SetValue(ShowFocusProperty, value); }
    public bool IsSquircle { get => (bool)GetValue(IsSquircleProperty); set => SetValue(IsSquircleProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    /// <summary>The side is only there when it has a colour.</summary>
    double Lift => Side != null ? Math.Max(0, Depth) : 0;

    /// <summary>A stone: a control surface (fill, side and ink of a secondary control, depth 2, radius 10).</summary>
    public static StoneChrome Stone()
    {
        var chrome = new StoneChrome();
        chrome.SetResourceReference(FillProperty, UiTokens.Keys.ControlSurface);
        chrome.SetResourceReference(SideProperty, UiTokens.Keys.ControlSide);
        chrome.SetResourceReference(InkProperty, UiTokens.Keys.Ink);
        chrome.SetResourceReference(FocusInkProperty, UiTokens.Keys.Focus);
        return chrome;
    }

    /// <summary>A paper plate: the group surface on its side (depth 3, radius 14), as Hams' setting groups.</summary>
    public static StoneChrome Plate()
    {
        var chrome = new StoneChrome { Depth = UiTokens.Depth.Group, Radius = UiTokens.Radius.Group };
        chrome.SetResourceReference(FillProperty, UiTokens.Keys.GroupSurface);
        chrome.SetResourceReference(SideProperty, UiTokens.Keys.GroupSide);
        chrome.SetResourceReference(InkProperty, UiTokens.Keys.Ink);
        chrome.SetResourceReference(FocusInkProperty, UiTokens.Keys.Focus);
        return chrome;
    }

    /// <summary>
    /// A plate that floats over somebody else's desktop (card, toolbar, menu): the plate plus the one, quiet
    /// desktop shadow of the kit (<see cref="Elevation.Floating"/>), so it stays apart from any background.
    /// </summary>
    public static StoneChrome FloatingPlate(double radius = UiTokens.Radius.Group)
    {
        var chrome = Plate();
        chrome.Radius = radius;
        chrome.Effect = Elevation.Floating();
        return chrome;
    }

    /// <summary>The face's outline in local coordinates (what the ink is drawn on and the child is clipped to).</summary>
    public Geometry FaceGeometry() => Face(RenderSize.Width, RenderSize.Height, IsPressed);

    Geometry Face(double width, double height, bool pressed)
    {
        double lift = Lift, faceHeight = Math.Max(0, height - lift), top = pressed ? lift : 0;
        return IsSquircle ? Squircle.Geometry(width, faceHeight, Radius, top) : Squircle.Rounded(width, faceHeight, Radius, top);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var padding = Padding;
        double extraW = padding.Left + padding.Right, extraH = padding.Top + padding.Bottom + Lift;
        if (Child == null) return new Size(extraW, extraH);
        Child.Measure(new Size(Math.Max(0, constraint.Width - extraW), Math.Max(0, constraint.Height - extraH)));
        return new Size(Child.DesiredSize.Width + extraW, Child.DesiredSize.Height + extraH);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child != null)
        {
            var padding = Padding;
            double lift = Lift, top = IsPressed ? lift : 0;
            Child.Arrange(new Rect(
                padding.Left,
                top + padding.Top,
                Math.Max(0, arrangeSize.Width - padding.Left - padding.Right),
                Math.Max(0, arrangeSize.Height - lift - padding.Top - padding.Bottom)));
        }
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = RenderSize.Width, height = RenderSize.Height, lift = Lift;
        if (width <= 0 || height <= lift) return;
        if (lift > 0 && !IsPressed)
            dc.DrawGeometry(Side, null, Face(width, height, pressed: true));   // the side: the face's shape, lift lower

        var face = Face(width, height, IsPressed);
        var edge = ShowFocus && FocusInk != null ? FocusInk : Ink;
        double edgeWidth = ShowFocus && FocusInk != null ? Math.Max(InkWidth, 1.0) * FocusWidthFactor : InkWidth;
        if (Fill == null && edge == null) return;
        dc.PushClip(face);   // the edge lies on the outline; only its inner half shows (as Hams masks it)
        dc.DrawGeometry(Fill, edge == null || edgeWidth <= 0 ? null : new Pen(edge, edgeWidth), face);
        dc.Pop();
    }
}

/// <summary>
/// The kit's only shadow: floating windows (cards, toolbars, menus, chips) lie on a foreign desktop and need a
/// quiet lift from it. Everything inside a window is ink and side, never a shadow.
/// </summary>
internal static class Elevation
{
    public const double Blur = 18, Offset = 3, Opacity = 0.28;

    /// <summary>Room around a floating surface so the shadow is not cut off by its window.</summary>
    public const double Margin = 22;

    public static System.Windows.Media.Effects.DropShadowEffect Floating()
    {
        var effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = Blur,
            ShadowDepth = Offset,
            Direction = 270,
            Opacity = Opacity,
            Color = UiTokens.Fixed.Shadow,
            RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality,
        };
        effect.Freeze();
        return effect;
    }
}
