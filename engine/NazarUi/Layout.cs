namespace NazarCapture.Engine.NazarUi;

/// <summary>
/// Free paper of a window that moves the window when dragged (Hams' <c>drag_surface</c>): the frame, the tab row and
/// the paper around the groups. Controls, rows and plates are no surface, so pressing them never moves the window.
/// </summary>
internal static class WindowDrag
{
    public static readonly DependencyProperty IsSurfaceProperty = DependencyProperty.RegisterAttached(
        "IsSurface", typeof(bool), typeof(WindowDrag), new FrameworkPropertyMetadata(false));

    public static bool GetIsSurface(DependencyObject element) => (bool)element.GetValue(IsSurfaceProperty);

    public static void SetIsSurface(DependencyObject element, bool value) => element.SetValue(IsSurfaceProperty, value);

    /// <summary>Lets <paramref name="window"/> be dragged by its free surfaces.</summary>
    public static void Install(Window window)
    {
        window.MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled || e.ButtonState != MouseButtonState.Pressed || e.OriginalSource is not DependencyObject source || !GetIsSurface(source)) return;
            e.Handled = true;
            try { window.DragMove(); }
            catch (InvalidOperationException) { }   // the button was released before Windows took over
        };
    }
}

/// <summary>
/// A scroll area like Hams' ScrollArea (ui_kit/layout.py): the content beside a slim self-drawn bar in a gutter that is
/// always kept (so the right margin never jumps), and a hairline above while content lies scrolled away and below while
/// more content follows. The bar is visible (<c>SCROLL_THUMB</c>) and can be operated: the thumb is dragged, a click
/// in the gutter turns a page, under the pointer it grows wider.
/// </summary>
internal sealed class ScrollArea : Grid
{
    public const double BarWidth = 4, BarHoverWidth = 6, BarInset = 3;
    /// <summary>Distance of the bar from the window edge: there the edge grip for resizing lies.</summary>
    public const double EdgeGap = UiTokens.Space.Sm;

    [ThreadStatic] static ControlTemplate? viewerTemplate;

    readonly Border top = new() { Height = 1, IsHitTestVisible = false }, bottom = new() { Height = 1, IsHitTestVisible = false };

    public ScrollArea()
    {
        Background = Brushes.Transparent;   // the paper between the plates: hit-testable, a drag surface
        WindowDrag.SetIsSurface(this, true);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GutterWidth) });

        Viewer = new ScrollViewer
        {
            Template = viewerTemplate ??= BuildViewerTemplate(),
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,   // scrolls, the bar is ours
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        SetRow(Viewer, 1);
        Children.Add(Viewer);
        Bar = new ScrollRail(Viewer);
        SetRow(Bar, 1);
        SetColumn(Bar, 1);
        Children.Add(Bar);
        SetColumnSpan(top, 2);
        SetColumnSpan(bottom, 2);
        SetRow(bottom, 2);
        Children.Add(top);
        Children.Add(bottom);
        Viewer.ScrollChanged += (_, _) => UpdateEdges();
        UpdateEdges();
    }

    /// <summary>Width of the bar's gutter (Hams' gutter_width).</summary>
    public static double GutterWidth => BarWidth + 2 * BarInset;

    public ScrollViewer Viewer { get; }

    public ScrollRail Bar { get; }

    public object? Content
    {
        get => Viewer.Content;
        set => Viewer.Content = value;
    }

    /// <summary>Whether the line above shows (content scrolled away).</summary>
    public bool TopEdgeShown { get; private set; }

    /// <summary>Whether the line below shows (more content follows).</summary>
    public bool BottomEdgeShown { get; private set; }

    public void ScrollTo(double offset) => Viewer.ScrollToVerticalOffset(offset);

    /// <summary>A line above where content scrolled away, a line below where more content follows.</summary>
    internal void UpdateEdges()
    {
        TopEdgeShown = Viewer.VerticalOffset > 0.5;
        BottomEdgeShown = Viewer.ScrollableHeight - Viewer.VerticalOffset > 0.5;
        Edge(top, TopEdgeShown);
        Edge(bottom, BottomEdgeShown);
        Bar.InvalidateVisual();
    }

    static void Edge(Border line, bool shown)
    {
        if (shown) line.SetResourceReference(Border.BackgroundProperty, UiTokens.Keys.Hairline);
        else line.Background = Brushes.Transparent;
    }

    static ControlTemplate BuildViewerTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ScrollContentPresenter), "PART_ScrollContentPresenter");
        presenter.SetValue(ScrollContentPresenter.CanContentScrollProperty, false);
        var template = new ControlTemplate(typeof(ScrollViewer)) { VisualTree = presenter };
        template.Seal();
        return template;
    }
}

/// <summary>The bar of a <see cref="ScrollArea"/>: a pill thumb in the gutter, drawn, dragged and clicked like Hams'.</summary>
internal sealed class ScrollRail : FrameworkElement
{
    public const double MinThumb = 20;

    public static readonly DependencyProperty ThumbProperty = DependencyProperty.Register(nameof(Thumb), typeof(Brush), typeof(ScrollRail),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThumbHoverProperty = DependencyProperty.Register(nameof(ThumbHover), typeof(Brush), typeof(ScrollRail),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    readonly ScrollViewer viewer;
    (double PointerY, double Offset)? drag;

    public ScrollRail(ScrollViewer viewer)
    {
        this.viewer = viewer;
        SetResourceReference(ThumbProperty, UiTokens.Keys.ScrollThumb);
        SetResourceReference(ThumbHoverProperty, UiTokens.Keys.TextTertiary);
        Cursor = Cursors.Arrow;
        viewer.ScrollChanged += (_, _) => InvalidateVisual();
    }

    public Brush? Thumb { get => (Brush?)GetValue(ThumbProperty); set => SetValue(ThumbProperty, value); }

    public Brush? ThumbHover { get => (Brush?)GetValue(ThumbHoverProperty); set => SetValue(ThumbHoverProperty, value); }

    /// <summary>Wide and darker under the pointer or while dragged.</summary>
    public bool Wide => IsMouseOver || drag != null;

    /// <summary>(top, length) of the thumb, or null when everything fits.</summary>
    public (double Top, double Length)? ThumbPlace()
    {
        double view = viewer.ViewportHeight, span = viewer.ExtentHeight, rest = viewer.ScrollableHeight;
        if (span <= view + 0.5 || view <= 1) return null;
        double length = Math.Max(MinThumb, view * view / span);
        double top = (view - length) * (rest > 0 ? viewer.VerticalOffset / rest : 0);
        return (top, length);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hit) =>
        ThumbPlace() == null ? null : new PointHitTestResult(this, hit.HitPoint);   // without a bar the gutter is paper

    protected override void OnRender(DrawingContext dc)
    {
        if (ThumbPlace() is not { } thumb) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));   // the whole gutter takes the pointer
        double width = Wide ? ScrollArea.BarHoverWidth : ScrollArea.BarWidth;
        double left = (ActualWidth - width) / 2;
        double radius = width / 2;
        dc.DrawRoundedRectangle(Wide ? ThumbHover : Thumb, null, new Rect(left, thumb.Top, width, thumb.Length), radius, radius);
    }

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); InvalidateVisual(); }

    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); InvalidateVisual(); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (ThumbPlace() is not { } thumb) return;
        e.Handled = true;
        double y = e.GetPosition(this).Y;
        if (y >= thumb.Top && y < thumb.Top + thumb.Length)
        {
            drag = (y, viewer.VerticalOffset);
            CaptureMouse();
        }
        else PageBy(y < thumb.Top ? -1 : 1);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (drag is not { } start || ThumbPlace() is not { } thumb) return;
        double room = Math.Max(1, viewer.ViewportHeight - thumb.Length);
        viewer.ScrollToVerticalOffset(start.Offset + (e.GetPosition(this).Y - start.PointerY) * viewer.ScrollableHeight / room);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (drag == null) return;
        drag = null;
        ReleaseMouseCapture();
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        drag = null;
        InvalidateVisual();
    }

    /// <summary>One page up (-1) or down (1), a step less than the view (Hams' scroll_page).</summary>
    public void PageBy(int direction) =>
        viewer.ScrollToVerticalOffset(viewer.VerticalOffset + direction * Math.Max(1, viewer.ViewportHeight - 40));
}

/// <summary>
/// The pages of a window with tabs (Hams' settings pages): every page is built and laid out ahead, only the chosen one
/// shows. The others are measured and arranged too, but hidden, so a switch draws a page that is already laid out at
/// the current width; the host's height is the chosen page's.
/// </summary>
internal sealed class PageHost : Panel
{
    int selected;

    public int Selected
    {
        get => selected;
        set
        {
            if (value < 0 || value >= Children.Count) return;
            selected = value;
            for (int i = 0; i < Children.Count; i++) Children[i].Visibility = i == selected ? Visibility.Visible : Visibility.Hidden;
            InvalidateMeasure();
        }
    }

    public UIElement? Current => selected < Children.Count ? Children[selected] : null;

    public void Add(UIElement page)
    {
        page.Visibility = Children.Count == selected ? Visibility.Visible : Visibility.Hidden;
        Children.Add(page);
    }

    protected override Size MeasureOverride(Size available)
    {
        var size = new Size();
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(available);
            if (i == selected) size = Children[i].DesiredSize;
        }
        return size;
    }

    protected override Size ArrangeOverride(Size final)
    {
        foreach (UIElement page in Children) page.Arrange(new Rect(0, 0, final.Width, Math.Max(final.Height, page.DesiredSize.Height)));
        return final;
    }
}
