namespace NazarCapture.Engine.Screenshot;

/// <summary>
/// Screenshot editor: the frozen screen stays dimmed, the crop is bright, has handles for
/// re-cropping and the toolbar below it. Related tools share a button; a second click on the
/// active button shows the choices.
/// </summary>
/// <remarks>
/// Keys: V move, A arrow, R rectangle, E ellipse, L line, P pen, M highlighter, S spotlight,
/// T text, B text with background, N number, X pixelate, W blur, 1-6 color, +/- or mouse wheel size,
/// Ctrl+Z / Ctrl+Y, Del, Ctrl+C copy, Ctrl+Shift+C copy text, Ctrl+S save as, Enter done, F3 pin, Esc cancel.
/// Pen: hold still at the end = clean shape; highlighter: snaps onto the text line it crosses.
/// This file holds the fields and the construction of the window and its toolbar; the other parts are
/// ScreenshotEditor.Mouse (open, layout, pointer), .Markup (placing markup), .Shapes (shape recognition)
/// and .Result (undo, keyboard, result).
/// The state lives in plain models: <see cref="EditorDocument"/> (markup items, crop, selection, text draft,
/// undo/redo), <see cref="EditorInteraction"/> (tool, gesture, discard question), <see cref="PenSnap"/> and
/// <see cref="InkStyle"/>. The window draws the document (one element per item, following its events) and
/// routes input to the models.
/// </remarks>
internal sealed partial class ScreenshotEditor : Window
{
    internal enum Tool { Move, Arrow, Box, Ellipse, Line, Pen, Marker, Spot, Text, Label, Number, Pixel, Blur }
    static readonly Key[] ToolKeys = { Key.V, Key.A, Key.R, Key.E, Key.L, Key.P, Key.M, Key.S, Key.T, Key.B, Key.N, Key.X, Key.W };

    sealed class ToolGroup
    {
        public ToolGroup(Tool[] tools)
        {
            Tools = tools;
            Current = tools[0];
        }

        public Tool[] Tools { get; }
        /// <summary>The tool picked last; its icon is shown on the button.</summary>
        public Tool Current { get; set; }
        public ToolButton Button { get; set; } = null!;   // created right after the group
        public Grid IconHost { get; } = new();
        public SolidColorBrush IconBrush { get; } = new(UiTokens.Fixed.OnAccentLegacy);
    }

    static readonly Cursor[] HandleCursors =
    {
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE, Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE
    };
    static readonly Point Offscreen = new(-1e6, -1e6);

    readonly Action<ScreenCapture, BitmapSource> onDone, onPinned;
    readonly Action<ScreenCapture> onCancelled;
    readonly IntPtr hwnd;
    readonly Canvas root, markup;
    readonly Image frozenImage;
    readonly Shapes.Path dimming;
    readonly Shapes.Rectangle frame;
    readonly Grid[] resizeHandles = new Grid[CropGeometry.HandleCount];
    readonly FloatingLabel sizeChip, tooltip, notice;
    readonly StoneChrome toolbar, toolMenu;
    readonly TextBlock sizeText, tooltipText, noticeText;
    readonly StackPanel toolbarRow, menuItems;
    readonly List<ToolGroup> groups = new();
    readonly List<Shapes.Ellipse> colorRings = new();
    readonly List<Shapes.Ellipse> colorDots = new();
    readonly List<KitToggleButton> widthButtons = new();
    readonly InkStyle style = new();

    // The model: items, crop, selection, text draft and undo live in the document; the pen-hold snap in
    // its own model. The window keeps only what it needs to draw and to route input.
    readonly EditorDocument document = new();
    readonly PenSnap snap = new();
    readonly Dictionary<MarkupItem, FrameworkElement> elements = new();
    readonly Dictionary<FrameworkElement, MarkupItem> itemsByElement = new();
    /// <summary>Pen or highlighter held still this long: the stroke snaps to a shape.</summary>
    static readonly TimeSpan HoldDelay = TimeSpan.FromMilliseconds(450);
    /// <summary>How long a notice (copied, saved, ...) stays.</summary>
    static readonly TimeSpan NoticeDuration = TimeSpan.FromMilliseconds(1800);
    /// <summary>A hard dark copy under the light dashes of the pixelate, blur and spotlight frame: one of the two always shows.</summary>
    static readonly System.Windows.Media.Effects.Effect RegionFrameOutline = MarkupLook.RegionOutline();

    readonly DispatcherTimer holdTimer = new() { Interval = HoldDelay };           // pen/highlighter held still
    readonly DispatcherTimer noticeTimer = new() { Interval = NoticeDuration };
    ToolGroup? openMenuGroup;
    Point holdPoint;
    Shapes.Path? spotlightShade;                  // dimming for the spotlights
    FrameworkElement? textHost;                   // the text box itself or the plate around it

    ScreenCapture capture = null!;                // set by Load(), cleared by HideAndReset()
    double scale = 1;                             // device pixels per WPF unit
    readonly EditorInteraction interaction = new();
    readonly EngineWork shown = new();            // the open editor holds unsaved markup: the engine must not quit
    FrameworkElement? numberPreview;             // semi-transparent preview of the next number at the pointer
    string? numberPreviewKey;
    Point pointer;
    Shapes.Path? stroke;
    PolyLineSegment strokeLine = null!;           // points of the current pen or highlighter stroke
    TextBox? textBox;

    public ScreenshotEditor(Action<ScreenCapture, BitmapSource> onDone, Action<ScreenCapture> onCancelled, Action<ScreenCapture, BitmapSource> onPinned)
    {
        this.onDone = onDone;
        this.onCancelled = onCancelled;
        this.onPinned = onPinned;
        ConfigureWindow();

        root = new Canvas { ClipToBounds = true };
        frozenImage = new Image { Stretch = Stretch.Fill };
        dimming = new Shapes.Path { Fill = Palette.Dimming, IsHitTestVisible = false };
        markup = new Canvas();
        frame = new Shapes.Rectangle { StrokeThickness = 2, IsHitTestVisible = false };
        frame.SetResourceReference(Shapes.Shape.StrokeProperty, UiTokens.Keys.AccentTint);
        sizeChip = BuildChip(out sizeText, shadow: true);
        sizeChip.Visibility = Visibility.Visible;
        toolbarRow = new StackPanel { Orientation = Orientation.Horizontal };
        tooltip = BuildChip(out tooltipText, shadow: true);
        toolbar = BuildToolbar();
        Localizer.Apply(toolbar);   // the tools from right to left in Arabic; the picture stays as it is
        menuItems = new StackPanel();
        toolMenu = BuildToolMenu();
        Localizer.Apply(toolMenu);
        notice = BuildChip(out noticeText, shadow: true);
        noticeText.FontWeight = FontWeights.SemiBold;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(notice, System.Windows.Automation.AutomationLiveSetting.Polite);
        ComposeSurface();

        hwnd = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.AddExtendedStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW);
        HookInput();
        FollowDocument();
    }

    // The markup layer mirrors the document: its children are the items' elements in document order,
    // the spotlight dimming right above the covers, and while drawing or typing one element on top.
    void FollowDocument()
    {
        document.Inserted += (item, index) =>
        {
            if (spotlightShade != null) markup.Children.Remove(spotlightShade);
            markup.Children.Insert(index, elements[item]);
            RefreshSpotlightShade(null);
        };
        document.Removed += (item, _) =>
        {
            markup.Children.Remove(elements[item]);
            RefreshSpotlightShade(null);
        };
        document.Moved += item =>
        {
            var offset = (TranslateTransform)elements[item].RenderTransform;
            offset.X = item.Offset.X;
            offset.Y = item.Offset.Y;
            if (item.Kind == MarkupKind.Spotlight) RefreshSpotlightShade(null);
        };
        document.CropChanged += LayoutCrop;
    }

    // Draws the markup of a new item and hands the item to the document.
    void AddMarkup(FrameworkElement element, MarkupKind kind)
    {
        var item = new MarkupItem(kind, AreaOf(element));
        elements[item] = element;
        itemsByElement[element] = item;
        document.Add(item);
    }

    static Rect AreaOf(FrameworkElement element)
    {
        double left = Canvas.GetLeft(element), top = Canvas.GetTop(element);
        left = double.IsNaN(left) ? 0 : left;
        top = double.IsNaN(top) ? 0 : top;
        if (element is Shapes.Path path && path.Data != null)
        {
            Rect bounds = path.Data.Bounds;
            if (!bounds.IsEmpty) bounds.Offset(left, top);
            return bounds;
        }
        return new Rect(left, top, double.IsNaN(element.Width) ? 0 : element.Width, double.IsNaN(element.Height) ? 0 : element.Height);
    }

    // A new screenshot or a closed editor: no markup elements left.
    void ClearMarkup()
    {
        markup.Children.Clear();
        elements.Clear();
        itemsByElement.Clear();
    }

    void ConfigureWindow()
    {
        Title = UiText.Titles.Editor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = UiTokens.Fixed.ScreenBackdropBrush;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Palette.Apply(Resources, PaintToolIcons);
    }

    // Layers from bottom to top: screenshot, dimming, markup, crop frame and handles, then the controls.
    void ComposeSurface()
    {
        root.Children.Add(frozenImage);
        root.Children.Add(dimming);
        root.Children.Add(markup);
        root.Children.Add(frame);
        AddResizeHandles();
        root.Children.Add(sizeChip);
        root.Children.Add(toolbar);
        root.Children.Add(toolMenu);
        root.Children.Add(tooltip);
        root.Children.Add(notice);
        Content = root;
    }

    // Every input handler is guarded: an error in one command must not leave the editor half-changed.
    void HookInput()
    {
        holdTimer.Tick += delegate { holdTimer.Stop(); Guarded("Snapping a stroke", PenHeld); };
        noticeTimer.Tick += delegate { noticeTimer.Stop(); notice.Visibility = Visibility.Hidden; interaction.DisarmDiscard(); };
        MouseLeftButtonDown += (_, e) => Guarded("Pressing in the editor", () => PointerDown(e.GetPosition(root)));
        MouseMove += (_, e) => Guarded("Moving in the editor", () => PointerMoved(e.GetPosition(root)));
        MouseLeftButtonUp += (_, _) => Guarded("Releasing in the editor", PointerUp);
        MouseLeave += delegate { pointer = Offscreen; UpdateNumberPreview(); };
        MouseWheel += (_, e) => Guarded("Changing the width", () => { if (!document.Typing && interaction.Idle) StepWidth(e.Delta > 0 ? 1 : -1); });
        PreviewKeyDown += (_, e) => Guarded("Editor command " + e.Key, () => OnKey(e));
    }

    /// <summary>Runs an input handler; on an unexpected error the current gesture is dropped and the user told.</summary>
    void Guarded(string what, Action action)
    {
        try { action(); }
        catch (Exception ex)   // last line of defence for one command: the editor stays usable
        {
            ErrorLog.Write(what, ex);
            if (IsMouseCaptured) ReleaseMouseCapture();
            holdTimer.Stop();
            interaction.Reset();
            ShowNotice(UiText.Editor.CommandFailed);
        }
    }

    /// <summary>After an error elsewhere in the screenshot flow: closes without reporting back.</summary>
    internal void Abandon()
    {
        shown.End();   // also when opening failed before the window was shown
        if (IsVisible) HideAndReset();
    }

    // ---------- Building ----------

    // Eight round handles around the crop; dragging one resizes the crop.
    void AddResizeHandles()
    {
        for (int i = 0; i < resizeHandles.Length; i++)
        {
            var dot = new Shapes.Ellipse { Width = Metrics.HandleSize, Height = Metrics.HandleSize, StrokeThickness = Metrics.HandleStroke };
            dot.SetResourceReference(Shapes.Shape.FillProperty, UiTokens.Keys.GroupSurface);
            dot.SetResourceReference(Shapes.Shape.StrokeProperty, UiTokens.Keys.AccentTint);
            var handle = new Grid { Width = Metrics.HandleHitSize, Height = Metrics.HandleHitSize, Background = Brushes.Transparent, Cursor = HandleCursors[i] };   // larger hit area
            handle.Children.Add(dot);
            int index = i;
            handle.MouseLeftButtonDown += (_, e) =>
            {
                CommitText();
                interaction.Begin(PointerMode.ResizeCrop);
                interaction.Handle = index;
                interaction.CropAtPress = document.Crop;
                CaptureMouse();
                e.Handled = true;
            };
            resizeHandles[i] = handle;
            root.Children.Add(handle);
        }
    }
}
