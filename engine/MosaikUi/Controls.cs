using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace MosaikCapture.Engine.MosaikUi;

/// <summary>Small factories of the kit that are no control of their own (tooltip, separator).</summary>
internal static class Kit
{
    [ThreadStatic] static ControlTemplate? toolTipTemplate;

    /// <summary>A tooltip on a small floating plate (Hams' tooltip.py) instead of the system's yellow box.</summary>
    public static ToolTip ToolTip(string text)
    {
        var tip = new ToolTip { Content = text, Template = toolTipTemplate ??= BuildToolTipTemplate(), HasDropShadow = false, Placement = PlacementMode.Top };
        return tip;
    }

    static ControlTemplate BuildToolTipTemplate()
    {
        var room = new FrameworkElementFactory(typeof(Border));
        room.SetValue(Border.PaddingProperty, new Thickness(Elevation.Margin / 2));
        var plate = new FrameworkElementFactory(typeof(StoneChrome));
        plate.SetResourceReference(StoneChrome.FillProperty, UiTokens.Keys.PopoverSurface);
        plate.SetResourceReference(StoneChrome.InkProperty, UiTokens.Keys.Ink);
        plate.SetValue(StoneChrome.RadiusProperty, UiTokens.Radius.Button);
        plate.SetValue(StoneChrome.PaddingProperty, new Thickness(10, 5, 10, 6));
        plate.SetValue(UIElement.EffectProperty, Elevation.Floating());
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        text.SetResourceReference(TextBlock.ForegroundProperty, UiTokens.Keys.TextPrimary);
        text.SetResourceReference(TextBlock.FontFamilyProperty, MuiTheme.BodyFontKey);
        text.SetValue(TextBlock.FontSizeProperty, TextStyle.Size(TextRole.Description));
        text.SetValue(TextBlock.MaxWidthProperty, 280.0);
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        plate.AppendChild(text);
        room.AppendChild(plate);
        var template = new ControlTemplate(typeof(ToolTip)) { VisualTree = room };
        template.Seal();
        return template;
    }

    /// <summary>A text-only content with <see cref="TextRole.Description"/> in the secondary colour.</summary>
    public static TextBlock Description(string text)
    {
        var block = TextStyle.Text(text, TextRole.Description, UiTokens.Keys.TextSecondary);
        block.TextWrapping = TextWrapping.Wrap;
        return block;
    }
}

/// <summary>The one separator of the kit: a hairline, horizontal (menus, setting groups) or vertical (toolbars).</summary>
internal sealed class KitSeparator : Separator
{
    [ThreadStatic] static ControlTemplate? template;

    public KitSeparator(Orientation orientation = Orientation.Horizontal, double length = double.NaN)
    {
        Template = template ??= Build();
        Focusable = false;
        IsTabStop = false;
        if (orientation == Orientation.Vertical)
        {
            Width = 1;
            Height = double.IsNaN(length) ? 22 : length;
            Margin = new Thickness(UiTokens.Space.Sm + UiTokens.Space.Xs, 0, UiTokens.Space.Sm + UiTokens.Space.Xs, 0);
            VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            Height = 1;
            if (!double.IsNaN(length)) Width = length;
            Margin = new Thickness(UiTokens.Space.Md, UiTokens.Space.Sm, UiTokens.Space.Md, UiTokens.Space.Sm);
        }
    }

    static ControlTemplate Build()
    {
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetResourceReference(Border.BackgroundProperty, UiTokens.Keys.Hairline);
        line.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        var t = new ControlTemplate(typeof(Separator)) { VisualTree = line };
        t.Seal();
        return t;
    }
}

/// <summary>Meaning of a status dot or a row's status line (Hams' StatusDot.COLOURS).</summary>
internal enum Status
{
    None,
    Idle,
    Ready,
    Recording,
    Processing,
    Warning,
    Error,
}

/// <summary>A coloured dot with a fixed meaning; next to a text it never stands for itself alone.</summary>
internal sealed class StatusDot : FrameworkElement
{
    public const double RowSize = 9, MainSize = 10;

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(StatusDot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    Status status;

    public StatusDot(Status status = Status.Idle, double size = RowSize)
    {
        Width = Height = size;
        VerticalAlignment = VerticalAlignment.Center;
        Show(status);
    }

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public Status Status => status;

    public static string ColorKey(Status status) => status switch
    {
        Status.Ready => UiTokens.Keys.Success,
        Status.Recording => UiTokens.Keys.Recording,
        Status.Processing => UiTokens.Keys.AccentTint,
        Status.Warning => UiTokens.Keys.Warning,
        Status.Error => UiTokens.Keys.Error,
        _ => UiTokens.Keys.TextTertiary,
    };

    public void Show(Status value)
    {
        status = value;
        SetResourceReference(FillProperty, ColorKey(value));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double r = Math.Min(ActualWidth, ActualHeight) / 2;
        dc.DrawEllipse(Fill, null, new Point(ActualWidth / 2, ActualHeight / 2), r, r);
    }
}

/// <summary>Look of a <see cref="Chip"/>.</summary>
internal enum ChipKind
{
    /// <summary>On paper: a control-surface pill with ink.</summary>
    Paper,
    /// <summary>On an image or video: a dark translucent pill with light text.</summary>
    OnImage,
    /// <summary>The accent tint (a chosen value, a counter).</summary>
    Accent,
}

/// <summary>A small pill with a short text (size of an image, length of a video, a state).</summary>
internal sealed class Chip : StoneChrome
{
    readonly TextBlock text;

    public Chip(string value, ChipKind kind = ChipKind.Paper)
    {
        IsSquircle = false;
        Padding = new Thickness(UiTokens.Space.Md, 2, UiTokens.Space.Md, 3);
        text = TextStyle.Apply(new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center }, TextRole.Shortcut);
        text.FontSize = 12;
        Child = text;
        switch (kind)
        {
            case ChipKind.OnImage:
                SetResourceReference(FillProperty, UiTokens.Keys.ImageChip);
                text.SetResourceReference(TextBlock.ForegroundProperty, UiTokens.Keys.LightOnDark);
                break;
            case ChipKind.Accent:
                SetResourceReference(FillProperty, UiTokens.Keys.AccentSurface);
                text.SetResourceReference(TextBlock.ForegroundProperty, UiTokens.Keys.OnAccent);
                break;
            default:
                SetResourceReference(FillProperty, UiTokens.Keys.ControlSurface);
                SetResourceReference(InkProperty, UiTokens.Keys.Ink);
                text.SetResourceReference(TextBlock.ForegroundProperty, UiTokens.Keys.TextPrimary);
                break;
        }
    }

    public string Text { get => text.Text; set => text.Text = value; }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Radius = UiTokens.Radius.Pill(arrangeSize.Height);
        return base.ArrangeOverride(arrangeSize);
    }
}

/// <summary>
/// A calm message on a plate: status dot, text and optionally keys or one action. Used for hints (overlay), problems
/// that stay until they are dismissed, and the update notice. Never a popup in the middle of work.
/// </summary>
internal sealed class Notice : StoneChrome
{
    readonly DockPanel row = new() { LastChildFill = true };
    readonly StatusDot dot;
    readonly TextBlock message;

    public Notice(string text, Status status = Status.None, UIElement? trailing = null, bool floating = false)
    {
        Radius = UiTokens.Radius.Button;
        Depth = floating ? 0 : UiTokens.Depth.Group;
        Padding = new Thickness(UiTokens.Space.Lg, UiTokens.Space.Md, UiTokens.Space.Lg, UiTokens.Space.Md);
        SetResourceReference(FillProperty, floating ? UiTokens.Keys.PopoverSurface : UiTokens.Keys.GroupSurface);
        if (!floating) SetResourceReference(SideProperty, UiTokens.Keys.GroupSide);
        SetResourceReference(InkProperty, UiTokens.Keys.Ink);
        if (floating) Effect = Elevation.Floating();
        dot = new StatusDot(status) { Margin = new Thickness(0, 0, UiTokens.Space.Md, 0), Visibility = status == Status.None ? Visibility.Collapsed : Visibility.Visible };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        if (trailing != null)
        {
            DockPanel.SetDock(trailing, Dock.Right);
            if (trailing is FrameworkElement fe) fe.Margin = new Thickness(UiTokens.Space.ControlGap, 0, 0, 0);
            row.Children.Add(trailing);
        }
        message = TextStyle.Text(text, TextRole.Description, UiTokens.Keys.TextPrimary);
        message.TextWrapping = TextWrapping.Wrap;
        message.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(message);
        Child = row;
        System.Windows.Automation.AutomationProperties.SetName(this, text);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(this, System.Windows.Automation.AutomationLiveSetting.Polite);
    }

    public void Show(string text, Status status)
    {
        message.Text = text;
        dot.Show(status);
        dot.Visibility = status == Status.None ? Visibility.Collapsed : Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetName(this, text);
    }
}

/// <summary>A thin bar (lifetime or progress of a card): hairline rail, accent fill scaled from the left.</summary>
internal sealed class ProgressLine : Grid
{
    public ScaleTransform Scale { get; } = new(1, 1);

    public ProgressLine()
    {
        Height = 3;
        var rail = new Border { CornerRadius = new CornerRadius(1.5) };
        rail.SetResourceReference(Border.BackgroundProperty, UiTokens.Keys.Hairline);
        var fill = new Border { CornerRadius = new CornerRadius(1.5), RenderTransform = Scale };
        fill.SetResourceReference(Border.BackgroundProperty, UiTokens.Keys.AccentSurface);
        Children.Add(rail);
        Children.Add(fill);
        IsHitTestVisible = false;
    }

    public Border Fill => (Border)Children[1];
}

/// <summary>
/// A switch with a sliding knob (Hams' ToggleSwitch): the state is coded twice, track colour and knob position. The value
/// jumps at once, the knob slides (170 ms). Saving happens at once; the movement is only feedback.
/// </summary>
internal sealed class ToggleSwitch : KitToggleButton
{
    public const double TrackWidth = 40, TrackHeight = 24, Inset = 2;
    public static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(170);

    [ThreadStatic] static ControlTemplate? template;

    public ToggleSwitch(string name, bool value = false)
    {
        Template = template ??= Build();
        Width = TrackWidth;
        Height = TrackHeight;
        IsChecked = value;
        System.Windows.Automation.AutomationProperties.SetName(this, name);
    }

    static ControlTemplate Build()
    {
        var chrome = new FrameworkElementFactory(typeof(SwitchChrome), "Chrome");
        var t = new ControlTemplate(typeof(ToggleButton)) { VisualTree = chrome };
        t.Seal();
        return t;
    }
}

/// <summary>The painted switch: a pill track (paper off, accent on) with a round knob.</summary>
internal sealed class SwitchChrome : RoleChrome
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(SwitchChrome),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KnobProperty = DependencyProperty.Register(nameof(Knob), typeof(Brush), typeof(SwitchChrome),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    bool? shown;

    public SwitchChrome()
    {
        IsSquircle = false;
        Depth = 0;
        Radius = ToggleSwitch.TrackHeight / 2;
    }

    /// <summary>Visible knob position from 0 (off) to 1 (on); differs from the value only while it slides.</summary>
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    public Brush? Knob { get => (Brush?)GetValue(KnobProperty); set => SetValue(KnobProperty, value); }

    public override void Refresh()
    {
        if (Owner is not ToggleButton toggle) return;
        State = StateOf(toggle);
        bool on = toggle.IsChecked == true;
        string fill = !toggle.IsEnabled ? UiTokens.Keys.ControlSurface
            : on ? UiTokens.Keys.AccentSurface
            : State == SurfaceState.Hover ? UiTokens.Keys.ControlHover : UiTokens.Keys.ControlSurface;
        Look = new Roles.Look(fill, UiTokens.Keys.TextPrimary, UiTokens.Keys.ControlSide);
        SetResourceReference(FillProperty, fill);
        SetResourceReference(InkProperty, UiTokens.Keys.Ink);
        SetResourceReference(FocusInkProperty, on ? UiTokens.Keys.OnAccent : UiTokens.Keys.Focus);
        SetResourceReference(KnobProperty, toggle.IsEnabled ? UiTokens.Keys.Knob : UiTokens.Keys.TextTertiary);
        ShowFocus = Roles.ShowsFocus(Mui.GetShowFocus(toggle), State);
        if (shown != on)
        {
            bool first = shown == null;
            shown = on;
            if (first || !IsLoaded) { BeginAnimation(ProgressProperty, null); Progress = on ? 1 : 0; }
            else BeginAnimation(ProgressProperty, new DoubleAnimation(on ? 1 : 0, new Duration(ToggleSwitch.SlideTime)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double height = RenderSize.Height, knob = height - 2 * ToggleSwitch.Inset;
        if (knob <= 0) return;
        double left = ToggleSwitch.Inset, right = RenderSize.Width - knob - ToggleSwitch.Inset;
        double x = left + (right - left) * Math.Max(0, Math.Min(1, Progress));
        var circle = new EllipseGeometry(new Rect(x, ToggleSwitch.Inset, knob, knob));
        dc.PushClip(circle);
        dc.DrawGeometry(Knob, Ink == null ? null : new Pen(Ink, InkWidth), circle);
        dc.Pop();
    }
}

/// <summary>
/// A choice from a list (Hams' Select): a secondary stone with the chosen value and a chevron; the list opens on a
/// floating plate with a check at the chosen entry. Keyboard as any ComboBox: arrows choose, Alt+Down or F4 open.
/// </summary>
internal sealed class Select : ComboBox
{
    [ThreadStatic] static ControlTemplate? template;

    public Select(string name)
    {
        Template = template ??= Build();
        FocusVisualStyle = null;
        Height = UiTokens.Height.Standard + UiTokens.Depth.Control;
        MinWidth = 160;
        Padding = new Thickness(UiTokens.Space.Lg, 0, UiTokens.Space.Md + UiTokens.Space.Xs, 0);
        HorizontalContentAlignment = HorizontalAlignment.Left;
        VerticalContentAlignment = VerticalAlignment.Center;
        TextStyle.Apply(this, TextRole.RowTitle);
        System.Windows.Automation.AutomationProperties.SetName(this, name);
        InputModality.Install();
    }

    /// <summary>Adds an entry; <paramref name="value"/> is what <see cref="Selector.SelectedValue"/> returns.</summary>
    public SelectItem Add(string label, object value)
    {
        var item = new SelectItem { Content = label, Tag = value };
        Items.Add(item);
        SelectedValuePath = "Tag";
        return item;
    }

    protected override DependencyObject GetContainerForItemOverride() => new SelectItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is SelectItem;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!IsDropDownOpen && IsEnabled)
        {
            Focus();
            IsDropDownOpen = true;
            e.Handled = true;
            return;
        }
        base.OnMouseLeftButtonDown(e);
    }

    static ControlTemplate Build()
    {
        var root = new FrameworkElementFactory(typeof(Grid));

        var chrome = new FrameworkElementFactory(typeof(RoleChrome), "Chrome");
        chrome.SetValue(StoneChrome.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var chevron = new FrameworkElementFactory(typeof(TextBlock));
        chevron.SetValue(TextBlock.TextProperty, IconGlyphs.ChevronDown);
        chevron.SetValue(TextBlock.FontFamilyProperty, Palette.IconFont);
        chevron.SetValue(TextBlock.FontSizeProperty, 12.0);
        chevron.SetValue(FrameworkElement.MarginProperty, new Thickness(UiTokens.Space.Md, 1, 0, 0));
        chevron.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        chevron.SetValue(DockPanel.DockProperty, Dock.Right);
        row.AppendChild(chevron);
        var value = new FrameworkElementFactory(typeof(ContentPresenter));
        value.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
        value.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
        value.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        value.SetValue(UIElement.IsHitTestVisibleProperty, false);
        row.AppendChild(value);
        chrome.AppendChild(row);
        root.AppendChild(chrome);

        var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
        popup.SetBinding(Popup.IsOpenProperty, new Binding(nameof(ComboBox.IsDropDownOpen)) { RelativeSource = RelativeSource.TemplatedParent, Mode = BindingMode.TwoWay });
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.FocusableProperty, false);
        popup.SetValue(Popup.VerticalOffsetProperty, -Elevation.Margin / 2 + UiTokens.Space.Sm);
        popup.SetValue(Popup.HorizontalOffsetProperty, -Elevation.Margin / 2);
        var room = new FrameworkElementFactory(typeof(Border));
        room.SetValue(Border.PaddingProperty, new Thickness(Elevation.Margin / 2));
        room.SetBinding(FrameworkElement.MinWidthProperty, new Binding(nameof(ActualWidth)) { RelativeSource = RelativeSource.TemplatedParent, Converter = new AddConverter(Elevation.Margin) });
        var plate = new FrameworkElementFactory(typeof(StoneChrome));
        plate.SetResourceReference(StoneChrome.FillProperty, UiTokens.Keys.PopoverSurface);
        plate.SetResourceReference(StoneChrome.InkProperty, UiTokens.Keys.Ink);
        plate.SetValue(StoneChrome.RadiusProperty, UiTokens.Radius.Group);
        plate.SetValue(StoneChrome.PaddingProperty, new Thickness(UiTokens.Space.Sm));
        plate.SetValue(UIElement.EffectProperty, Elevation.Floating());
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(FrameworkElement.MaxHeightProperty, new TemplateBindingExtension(ComboBox.MaxDropDownHeightProperty));
        var items = new FrameworkElementFactory(typeof(StackPanel));
        items.SetValue(Panel.IsItemsHostProperty, true);
        items.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Contained);
        scroll.AppendChild(items);
        plate.AppendChild(scroll);
        room.AppendChild(plate);
        popup.AppendChild(room);
        root.AppendChild(popup);

        var t = new ControlTemplate(typeof(ComboBox)) { VisualTree = root };
        t.Seal();
        return t;
    }

    sealed class AddConverter : IValueConverter
    {
        readonly double amount;

        public AddConverter(double amount) => this.amount = amount;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double d ? d + amount : 0.0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}

/// <summary>An entry of a <see cref="Select"/>: a plain row, highlighted like hover, with a check when chosen.</summary>
internal sealed class SelectItem : ComboBoxItem
{
    [ThreadStatic] static ControlTemplate? template;

    public SelectItem()
    {
        Template = template ??= Build();
        FocusVisualStyle = null;
        Mui.SetRole(this, MuiRole.Plain);
        Mui.SetDepth(this, 0);
        Mui.SetRadius(this, 8);
        Padding = new Thickness(UiTokens.Space.Md, 6, UiTokens.Space.Lg, 7);
        MinHeight = 32;
        TextStyle.Apply(this, TextRole.RowTitle);
    }

    static ControlTemplate Build()
    {
        var chrome = new FrameworkElementFactory(typeof(RoleChrome), "Chrome");
        chrome.SetValue(StoneChrome.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        chrome.SetValue(StoneChrome.RadiusProperty, new TemplateBindingExtension(Mui.RadiusProperty));
        chrome.SetValue(StoneChrome.DepthProperty, new TemplateBindingExtension(Mui.DepthProperty));
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var check = new FrameworkElementFactory(typeof(TextBlock), "Check");
        check.SetValue(TextBlock.TextProperty, IconGlyphs.Accept);
        check.SetValue(TextBlock.FontFamilyProperty, Palette.IconFont);
        check.SetValue(TextBlock.FontSizeProperty, 12.0);
        check.SetValue(FrameworkElement.WidthProperty, 22.0);
        check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        check.SetValue(UIElement.OpacityProperty, 0.0);
        check.SetResourceReference(TextBlock.ForegroundProperty, UiTokens.Keys.AccentTint);
        check.SetValue(DockPanel.DockProperty, Dock.Left);
        row.AppendChild(check);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(content);
        chrome.AppendChild(row);
        var t = new ControlTemplate(typeof(ComboBoxItem)) { VisualTree = chrome };
        var chosen = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        chosen.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "Check"));
        t.Triggers.Add(chosen);
        t.Seal();
        return t;
    }
}

/// <summary>
/// A menu on a floating plate (Hams' popover): arrow keys move, Enter chooses, Esc closes, Shift+F10 opens it on the
/// focused element, and the focus goes back to whatever opened it.
/// </summary>
internal sealed class KitMenu : ContextMenu
{
    [ThreadStatic] static ControlTemplate? template;

    public KitMenu()
    {
        Template = template ??= Build();
        HasDropShadow = false;
        FocusVisualStyle = null;
        TextStyle.Apply(this, TextRole.RowTitle);
        InputModality.Install();
    }

    /// <summary>Adds an entry with an optional icon and the keys that do the same (shown on the right).</summary>
    public KitMenuItem Add(string text, Action click, string? icon = null, string? keys = null)
    {
        var item = new KitMenuItem(text, icon, keys);
        item.Click += (_, _) => click();
        Items.Add(item);
        return item;
    }

    public void AddSeparator() => Items.Add(new KitSeparator());

    /// <summary>
    /// The look of an open menu as a plain element (sample sheets and pixel tests; a ContextMenu itself cannot sit in a
    /// window): the same plate with stand-alone entries.
    /// </summary>
    public static StoneChrome Preview(params (string Text, string? Icon, string? Keys)[] entries)
    {
        var plate = new StoneChrome { Radius = UiTokens.Radius.Group, Padding = new Thickness(UiTokens.Space.Sm), MinWidth = 200, Effect = Elevation.Floating(), Depth = 0 };
        plate.SetResourceReference(StoneChrome.FillProperty, UiTokens.Keys.PopoverSurface);
        plate.SetResourceReference(StoneChrome.InkProperty, UiTokens.Keys.Ink);
        var items = new StackPanel();
        TextStyle.Apply(items, TextRole.RowTitle);
        foreach (var (text, icon, keys) in entries)
            items.Children.Add(text.Length == 0 ? new KitSeparator() : new KitMenuItem(text, icon, keys));
        plate.Child = items;
        return plate;
    }

    protected override DependencyObject GetContainerForItemOverride() => new KitMenuItem("", null, null);

    protected override bool IsItemItsOwnContainerOverride(object item) => item is MenuItem or Separator;

    static ControlTemplate Build()
    {
        var room = new FrameworkElementFactory(typeof(Border));
        room.SetValue(Border.PaddingProperty, new Thickness(Elevation.Margin / 2));
        var plate = new FrameworkElementFactory(typeof(StoneChrome));
        plate.SetResourceReference(StoneChrome.FillProperty, UiTokens.Keys.PopoverSurface);
        plate.SetResourceReference(StoneChrome.InkProperty, UiTokens.Keys.Ink);
        plate.SetValue(StoneChrome.RadiusProperty, UiTokens.Radius.Group);
        plate.SetValue(StoneChrome.PaddingProperty, new Thickness(UiTokens.Space.Sm));
        plate.SetValue(UIElement.EffectProperty, Elevation.Floating());
        plate.SetValue(FrameworkElement.MinWidthProperty, 200.0);
        var items = new FrameworkElementFactory(typeof(StackPanel));
        items.SetValue(Panel.IsItemsHostProperty, true);
        items.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Cycle);
        plate.AppendChild(items);
        room.AppendChild(plate);
        var t = new ControlTemplate(typeof(ContextMenu)) { VisualTree = room };
        t.Seal();
        return t;
    }
}

/// <summary>An entry of a <see cref="KitMenu"/>: icon, text and keys on a plain row; keyboard highlight looks like hover.</summary>
internal sealed class KitMenuItem : MenuItem
{
    [ThreadStatic] static ControlTemplate? template;

    public KitMenuItem(string text, string? icon, string? keys)
    {
        Template = template ??= Build();
        FocusVisualStyle = null;
        Mui.SetRole(this, MuiRole.Plain);
        Mui.SetDepth(this, 0);
        Mui.SetRadius(this, 8);
        Padding = new Thickness(UiTokens.Space.Md, 7, UiTokens.Space.Lg, 8);
        Header = text;
        if (icon != null) Icon = TextStyle.Icon(icon, 15);
        if (keys != null) InputGestureText = keys;
        System.Windows.Automation.AutomationProperties.SetName(this, text);
    }

    static ControlTemplate Build()
    {
        var chrome = new FrameworkElementFactory(typeof(RoleChrome), "Chrome");
        chrome.SetValue(StoneChrome.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        chrome.SetValue(StoneChrome.RadiusProperty, new TemplateBindingExtension(Mui.RadiusProperty));
        chrome.SetValue(StoneChrome.DepthProperty, new TemplateBindingExtension(Mui.DepthProperty));
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var icon = new FrameworkElementFactory(typeof(ContentPresenter));
        icon.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(MenuItem.IconProperty));
        icon.SetValue(FrameworkElement.WidthProperty, 18.0);
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, UiTokens.Space.Md + UiTokens.Space.Xs, 0));
        icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        icon.SetValue(DockPanel.DockProperty, Dock.Left);
        row.AppendChild(icon);
        var keys = new FrameworkElementFactory(typeof(TextBlock));
        keys.SetValue(TextBlock.TextProperty, new TemplateBindingExtension(MenuItem.InputGestureTextProperty));
        keys.SetResourceReference(TextBlock.ForegroundProperty, UiTokens.Keys.TextSecondary);
        keys.SetResourceReference(TextBlock.FontFamilyProperty, MuiTheme.BodyFontKey);
        keys.SetValue(TextBlock.FontSizeProperty, TextStyle.Size(TextRole.Description));
        keys.SetValue(TextBlock.FontWeightProperty, FontWeights.Normal);
        keys.SetValue(FrameworkElement.MarginProperty, new Thickness(UiTokens.Space.Xl, 0, 0, 0));
        keys.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        keys.SetValue(DockPanel.DockProperty, Dock.Right);
        row.AppendChild(keys);
        var header = new FrameworkElementFactory(typeof(ContentPresenter));
        header.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(HeaderedItemsControl.HeaderProperty));
        header.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        header.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(header);
        chrome.AppendChild(row);
        var t = new ControlTemplate(typeof(MenuItem)) { VisualTree = chrome };
        t.Seal();
        return t;
    }
}

/// <summary>
/// A small floating label on a plate (sizes, captions, hints, short feedback): popover paper, ink edge, radius 10, the
/// desktop shadow when it floats over someone else's screen.
/// </summary>
internal sealed class FloatingLabel : StoneChrome
{
    public FloatingLabel(UIElement content, bool shadow = true)
    {
        Radius = UiTokens.Radius.Button;
        Depth = 0;
        Padding = new Thickness(10, 5, 10, 6);
        SetResourceReference(FillProperty, UiTokens.Keys.PopoverSurface);
        SetResourceReference(InkProperty, UiTokens.Keys.Ink);
        if (shadow) Effect = Elevation.Floating();
        Child = content;
        IsHitTestVisible = false;
    }

    /// <summary>A label with one text in the description role.</summary>
    public static FloatingLabel ForText(out TextBlock text, bool shadow = true, string colorKey = UiTokens.Keys.TextPrimary)
    {
        text = TextStyle.Text("", TextRole.Description, colorKey);
        text.VerticalAlignment = VerticalAlignment.Center;
        return new FloatingLabel(text, shadow);
    }
}

/// <summary>
/// A handle that moves its window (the grip of the recording bar): it reports the start, each move and the end of a drag
/// in screen pixels; the window moves itself.
/// </summary>
internal sealed class DragHandle : Decorator
{
    readonly Action begin;
    readonly Action move;
    bool dragging;

    public DragHandle(UIElement content, Action begin, Action move)
    {
        this.begin = begin;
        this.move = move;
        Child = content;
        Cursor = Cursors.SizeAll;
        System.Windows.Automation.AutomationProperties.SetName(this, UiText.Toolbar.Move);
    }

    protected override void OnRender(DrawingContext dc) => dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));   // the whole handle is hit-testable

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        dragging = true;
        begin();
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging) move();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) => dragging = false;
}
