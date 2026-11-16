using System.Windows.Controls.Primitives;

namespace NazarCapture.Engine.Screenshot;

/// <summary>
/// The editor's toolbar, built from the kit: a floating plate with tool buttons (a group shows the tool picked last and
/// opens its menu on a second click or with the down arrow), colour and width choices, undo/redo and the result actions,
/// "Fertig" as the one primary action. Every button is a real button: Tab reaches it, Space and Enter press it, screen
/// readers read its name. Tooltips, notices and the size chip are floating labels of the kit.
/// </summary>
internal sealed partial class ScreenshotEditor
{
    static FloatingLabel BuildChip(out TextBlock text, bool shadow)
    {
        var chip = FloatingLabel.ForText(out text, shadow);
        chip.Visibility = Visibility.Hidden;
        return chip;
    }

    StoneChrome BuildToolbar()
    {
        AddToolGroup(Tool.Move);
        AddSeparator();
        AddToolGroup(Tool.Arrow);
        AddToolGroup(Tool.Box, Tool.Ellipse, Tool.Line);
        AddToolGroup(Tool.Pen);
        AddToolGroup(Tool.Marker, Tool.Spot);
        AddToolGroup(Tool.Text, Tool.Label);
        AddToolGroup(Tool.Number);
        AddToolGroup(Tool.Pixel, Tool.Blur);
        AddSeparator();
        AddColorButtons();
        AddWidthButtons();
        AddSeparator();
        toolbarRow.Children.Add(ActionButton(IconGlyphs.Undo, UiText.Editor.Undo, Undo));
        toolbarRow.Children.Add(ActionButton(IconGlyphs.Redo, UiText.Editor.Redo, Redo));
        AddSeparator();
        toolbarRow.Children.Add(ActionButton(IconGlyphs.Save, UiText.Editor.SaveAs, SaveAs));
        toolbarRow.Children.Add(ActionButton(IconGlyphs.Copy, UiText.Editor.Copy, CopyNow));
        toolbarRow.Children.Add(ActionButton(IconGlyphs.TextRecognition, UiText.Editor.CopyText, CopyText));
        toolbarRow.Children.Add(ActionButton(IconGlyphs.Pin, UiText.Editor.Pin, delegate { Finish(pin: true); }));
        toolbarRow.Children.Add(ActionButton(IconGlyphs.Cancel, UiText.Editor.Cancel, Cancel));
        var done = ActionButton(IconGlyphs.Accept, UiText.Editor.Done, delegate { Finish(pin: false); });
        Mui.SetRole(done, MuiRole.Primary);   // the one main action of the bar
        toolbarRow.Children.Add(done);
        var bar = StoneChrome.FloatingPlate();
        bar.Padding = new Thickness(UiTokens.Space.GroupInset);
        bar.Child = toolbarRow;
        bar.Cursor = Cursors.Arrow;
        KeyboardNavigation.SetTabNavigation(bar, KeyboardNavigationMode.Cycle);
        bar.MouseLeftButtonDown += (_, e) => e.Handled = true;   // clicks on the toolbar do not draw
        System.Windows.Automation.AutomationProperties.SetName(bar, UiText.Titles.Editor);
        return bar;
    }

    /// <summary>Every button of the toolbar (tests: all reachable by keyboard and named).</summary>
    internal IEnumerable<ButtonBase> ToolbarButtons => Descendants<ButtonBase>(toolbarRow);

    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject node) continue;
            if (node is T match) yield return match;
            foreach (var deeper in Descendants<T>(node)) yield return deeper;
        }
    }

    void AddColorButtons()
    {
        for (int i = 0; i < InkStyle.Colors.Length; i++)
        {
            var face = new Grid { Width = Metrics.ColorRingSize, Height = Metrics.ColorRingSize };
            var ring = new Shapes.Ellipse { Width = Metrics.ColorRingSize, Height = Metrics.ColorRingSize, StrokeThickness = Metrics.ColorRingThickness, Visibility = Visibility.Hidden };
            ring.SetResourceReference(Shapes.Shape.StrokeProperty, UiTokens.Keys.TextPrimary);
            face.Children.Add(ring);
            face.Children.Add(new Shapes.Ellipse { Width = Metrics.ColorDotSize, Height = Metrics.ColorDotSize, Fill = new SolidColorBrush(InkStyle.Colors[i]) });
            colorRings.Add(ring);
            int index = i;
            var button = new KitButton { Content = face, Width = Metrics.ColorButtonWidth, Height = ToolButton.Size + UiTokens.Depth.Control, Padding = new Thickness(0) };
            Mui.SetRole(button, MuiRole.Plain);
            Mui.SetRadius(button, UiTokens.Radius.Pill(Metrics.ColorButtonWidth));
            string tip = UiText.Editor.ColorTip(index + 1);
            System.Windows.Automation.AutomationProperties.SetName(button, tip);
            button.Click += delegate { Guarded("Choosing a colour", () => SetColor(index)); };
            HookTooltip(button, () => tip);
            toolbarRow.Children.Add(button);
        }
    }

    void AddWidthButtons()
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(UiTokens.Space.Md, 0, UiTokens.Space.Xs, 0), VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < Metrics.WidthDots.Length; i++)
        {
            var dot = new Shapes.Ellipse { Width = Metrics.WidthDots[i], Height = Metrics.WidthDots[i] };
            dot.SetResourceReference(Shapes.Shape.FillProperty, UiTokens.Keys.TextPrimary);
            int index = i;
            var button = new KitToggleButton { Content = dot, Width = Metrics.WidthButtonSize, Height = Metrics.WidthButtonSize + UiTokens.Depth.Control, Padding = new Thickness(0) };
            Mui.SetRole(button, MuiRole.Plain);
            Mui.SetRadius(button, Metrics.WidthButtonRadius);
            string tip = UiText.Editor.WidthNames[index] + UiText.Editor.WidthHint;
            System.Windows.Automation.AutomationProperties.SetName(button, UiText.Editor.WidthNames[index]);
            button.Click += delegate { Guarded("Choosing a width", () => SetWidth(index)); };
            HookTooltip(button, () => tip);
            widthButtons.Add(button);
            buttons.Children.Add(button);
        }
        toolbarRow.Children.Add(buttons);
    }

    StoneChrome BuildToolMenu()
    {
        var menu = new StoneChrome { Radius = UiTokens.Radius.Group, Depth = 0, Padding = new Thickness(UiTokens.Space.Sm), MinWidth = Metrics.MenuMinWidth, Effect = Elevation.Floating() };
        menu.SetResourceReference(StoneChrome.FillProperty, UiTokens.Keys.PopoverSurface);
        menu.SetResourceReference(StoneChrome.InkProperty, UiTokens.Keys.Ink);
        menu.Child = menuItems;
        menu.Cursor = Cursors.Arrow;
        menu.Visibility = Visibility.Hidden;
        KeyboardNavigation.SetDirectionalNavigation(menuItems, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetTabNavigation(menuItems, KeyboardNavigationMode.Cycle);
        menu.MouseLeftButtonDown += (_, e) => e.Handled = true;
        return menu;
    }

    // ---------- Toolbar ----------

    static TextBlock Glyph(string glyph, Brush? foreground)
    {
        var text = TextStyle.Icon(glyph, Metrics.IconSize);
        if (foreground != null) text.Foreground = foreground;
        return text;
    }

    void AddSeparator() => toolbarRow.Children.Add(new KitSeparator(Orientation.Vertical, Metrics.SeparatorHeight));

    KitButton ActionButton(string glyph, string label, Action click)
    {
        var button = new KitButton { Content = TextStyle.Icon(glyph, Metrics.IconSize), Width = ToolButton.Size, Height = ToolButton.Size + UiTokens.Depth.Control, Padding = new Thickness(0), Margin = new Thickness(1, 0, 1, 0) };
        Mui.SetRole(button, MuiRole.Plain);
        Mui.SetRadius(button, UiTokens.Radius.Button);
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += delegate { Guarded("Editor button " + label, click); };
        HookTooltip(button, () => label);
        return button;
    }

    /// <summary>The label under or above the toolbar while the pointer is over a button or it has the keyboard focus.</summary>
    void HookTooltip(ButtonBase button, Func<string> text)
    {
        button.MouseEnter += delegate { if (openMenuGroup == null) ShowTooltip(button, text()); };
        button.MouseLeave += delegate { tooltip.Visibility = Visibility.Hidden; };
        button.GotKeyboardFocus += delegate { if (openMenuGroup == null) ShowTooltip(button, text()); };
        button.LostKeyboardFocus += delegate { tooltip.Visibility = Visibility.Hidden; };
    }

    // Button for one tool or a group of related tools; a group shows the tool picked last.
    void AddToolGroup(params Tool[] tools)
    {
        var group = new ToolGroup(tools);
        UIElement content = group.IconHost;
        if (tools.Length > 1)
        {
            // small triangle bottom right: there is more
            var withMarker = new Grid { Width = Metrics.ButtonSize, Height = Metrics.ButtonSize };
            withMarker.Children.Add(group.IconHost);
            var triangle = new Shapes.Path
            {
                Data = Geometry.Parse("M 30,26 L 30,30 L 26,30 Z"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            triangle.SetResourceReference(Shapes.Shape.FillProperty, UiTokens.Keys.TextSecondary);
            withMarker.Children.Add(triangle);
            content = withMarker;
        }
        var button = new ToolButton(IconGlyphs.Pen, "") { Content = content, ToolTip = null, Margin = new Thickness(1, 0, 1, 0) };
        group.Button = button;
        button.Click += delegate
        {
            Guarded("Editor tool", () =>
            {
                if (Array.IndexOf(group.Tools, interaction.Tool) >= 0 && group.Tools.Length > 1)
                {
                    if (openMenuGroup == group) CloseMenu(); else OpenMenu(group);
                }
                else SetTool(group.Current);
                button.IsChecked = Array.IndexOf(group.Tools, interaction.Tool) >= 0;   // the toggle shows the tool, not the click
            });
        };
        button.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && group.Tools.Length > 1) { e.Handled = true; OpenMenu(group, focusFirst: true); }
        };
        HookTooltip(button, () => ToolLabel(group));
        ShowIcon(group);
        groups.Add(group);
        toolbarRow.Children.Add(button);
    }

    string ToolLabel(ToolGroup group)
    {
        string label = UiText.Editor.ToolNames[(int)group.Current] + "   " + ToolKeys[(int)group.Current];
        if (group.Tools.Length > 1 && Array.IndexOf(group.Tools, interaction.Tool) >= 0) label += UiText.Editor.MoreHint;
        return label;
    }

    static void ShowIcon(ToolGroup group)
    {
        group.IconHost.Children.Clear();
        group.IconHost.Children.Add(ToolIcon(group.Current, group.IconBrush));
        System.Windows.Automation.AutomationProperties.SetName(group.Button, UiText.Editor.ToolNames[(int)group.Current]);
    }

    // Menu of a tool group: icon, name and key, right next to the button; arrow keys move, Enter chooses, Esc closes.
    void OpenMenu(ToolGroup group, bool focusFirst = false)
    {
        tooltip.Visibility = Visibility.Hidden;
        openMenuGroup = group;
        menuItems.Children.Clear();
        foreach (Tool groupTool in group.Tools) menuItems.Children.Add(MenuEntry(groupTool));
        toolMenu.Visibility = Visibility.Visible;
        toolMenu.InvalidateMeasure();   // otherwise the height from the last time sticks
        toolMenu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Point buttonPosition = group.Button.TranslatePoint(new Point(0, 0), root);
        double toolbarTop = Canvas.GetTop(toolbar), menuHeight = toolMenu.DesiredSize.Height;
        Rect monitor = MonitorArea();
        Canvas.SetLeft(toolMenu, Math.Max(monitor.Left + 4, Math.Min(monitor.Right - 4 - toolMenu.DesiredSize.Width, buttonPosition.X - 6)));
        Canvas.SetTop(toolMenu, toolbarTop + toolbar.ActualHeight + 6 + menuHeight < monitor.Bottom - 4 ? toolbarTop + toolbar.ActualHeight + 6 : toolbarTop - 6 - menuHeight);
        if (focusFirst || InputModality.Keyboard && group.Button.IsKeyboardFocused)
        {
            toolMenu.UpdateLayout();
            (menuItems.Children.OfType<UIElement>().FirstOrDefault())?.Focus();
        }
    }

    KitButton MenuEntry(Tool entryTool)
    {
        bool active = entryTool == interaction.Tool;
        var icon = new Grid { Width = 26, HorizontalAlignment = HorizontalAlignment.Left };
        icon.Children.Add(ToolIcon(entryTool, new SolidColorBrush(ThemeColor(active ? UiTokens.Keys.AccentTint : UiTokens.Keys.TextPrimary))));
        var key = TextStyle.Text(ToolKeys[(int)entryTool].ToString(), TextRole.Description, UiTokens.Keys.TextSecondary);
        key.Margin = new Thickness(28, 0, 0, 0);
        key.VerticalAlignment = VerticalAlignment.Center;
        var name = TextStyle.Apply(new TextBlock { Text = UiText.Editor.ToolNames[(int)entryTool], Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, TextRole.RowTitle);
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(key, Dock.Right);
        row.Children.Add(icon);
        row.Children.Add(key);
        row.Children.Add(name);
        var entry = new KitButton { Content = row, Padding = new Thickness(8, 7, 12, 7), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Mui.SetRole(entry, MuiRole.Plain);
        Mui.SetDepth(entry, 0);
        Mui.SetRadius(entry, 8);
        if (active) Selector.SetIsSelected(entry, true);
        System.Windows.Automation.AutomationProperties.SetName(entry, UiText.Editor.ToolNames[(int)entryTool]);
        entry.Click += delegate { Guarded("Choosing a tool", () => SetTool(entryTool)); };
        return entry;
    }

    internal void CloseMenu()
    {
        var group = openMenuGroup;
        openMenuGroup = null;
        bool hadFocus = toolMenu.IsKeyboardFocusWithin;
        toolMenu.Visibility = Visibility.Hidden;
        if (hadFocus) group?.Button.Focus();   // the focus goes back to whatever opened the menu
    }

    /// <summary>Icons: font glyphs where a suitable one exists, otherwise drawn in the same stroke style.</summary>
    internal static UIElement ToolIcon(Tool icon, SolidColorBrush foreground)
    {
        switch (icon)
        {
            case Tool.Move: return Glyph(IconGlyphs.Move, foreground);
            case Tool.Arrow: return Glyph(IconGlyphs.ArrowUpRight, foreground);
            case Tool.Box: return Glyph(IconGlyphs.Rectangle, foreground);
            case Tool.Ellipse: return Glyph(IconGlyphs.Ellipse, foreground);
            case Tool.Pen: return Glyph(IconGlyphs.Pen, foreground);
            case Tool.Marker: return Glyph(IconGlyphs.Highlight, foreground);
            case Tool.Text: return Glyph(IconGlyphs.Text, foreground);
            case Tool.Number: return NumberIcon(foreground);
            case Tool.Pixel: return PixelateIcon(foreground);
        }
        var canvas = new Canvas { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (icon == Tool.Line)
            canvas.Children.Add(new Shapes.Path { Data = Geometry.Parse("M 2.5,13.5 L 13.5,2.5"), Stroke = foreground, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        else if (icon == Tool.Spot)
        {
            // dimmed area with a bright hole
            var hole = new EllipseGeometry(new Point(8, 8), 4.5, 4.5);
            canvas.Children.Add(new Shapes.Path { Data = Geometry.Combine(new RectangleGeometry(new Rect(1, 1, 14, 14), 2.5, 2.5), hole, GeometryCombineMode.Exclude, null), Fill = foreground, Opacity = 0.45 });
            canvas.Children.Add(new Shapes.Path { Data = new EllipseGeometry(new Point(8, 8), 4.5, 4.5), Stroke = foreground, StrokeThickness = 1.25 });
        }
        else if (icon == Tool.Label)
        {
            // filled plate with the A punched out
            var plate = new RectangleGeometry(new Rect(0, 1.5, 16, 13), 3, 3);
            canvas.Children.Add(new Shapes.Path { Data = Geometry.Combine(plate, Digits("A", 11, new Point(8, 8)), GeometryCombineMode.Exclude, null), Fill = foreground });
        }
        else if (icon == Tool.Blur)
        {
            // drop, as is common for blur tools
            canvas.Children.Add(new Shapes.Path { Data = Geometry.Parse("M 8,1.5 C 8,1.5 3,7.4 3,10.3 A 5,5 0 0 0 13,10.3 C 13,7.4 8,1.5 8,1.5 Z"), Stroke = foreground, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round });
            canvas.Children.Add(new Shapes.Path { Data = Geometry.Parse("M 5.6,10.6 A 2.6,2.6 0 0 0 7.6,12.7"), Stroke = foreground, StrokeThickness = 1.25, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Opacity = 0.6 });
        }
        return canvas;
    }

    // Circle and digit as geometry, the digit centered by its visible outline.
    static Grid NumberIcon(SolidColorBrush foreground)
    {
        var icon = new Grid { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.Children.Add(new Shapes.Ellipse { Stroke = foreground, StrokeThickness = 1.5 });
        icon.Children.Add(new Shapes.Path
        {
            Data = Digits("1", 11.5, new Point(9, 9)),
            Fill = foreground,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        });
        return icon;
    }

    // 3x3 squares on whole pixels: 4 wide, 1 gap.
    static Canvas PixelateIcon(SolidColorBrush foreground)
    {
        var icon = new Canvas { Width = 14, Height = 14, SnapsToDevicePixels = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 9; i++)
        {
            var square = new Shapes.Rectangle { Width = 4, Height = 4, Fill = foreground, Opacity = i % 2 == 0 ? 1 : 0.4 };
            Canvas.SetLeft(square, i % 3 * 5);
            Canvas.SetTop(square, i / 3 * 5);
            icon.Children.Add(square);
        }
        return icon;
    }

    Color ThemeColor(string key) => ((SolidColorBrush)Resources[key]).Color;

    internal void SetTool(Tool selected)
    {
        CommitText();
        CloseMenu();
        interaction.Tool = selected;
        foreach (var group in groups)
        {
            bool active = Array.IndexOf(group.Tools, selected) >= 0;
            if (active && group.Current != selected) { group.Current = selected; ShowIcon(group); }
            group.Button.IsChecked = active;
            group.IconBrush.Color = ThemeColor(active ? UiTokens.Keys.AccentTint : UiTokens.Keys.TextPrimary);
            if (group.Button.IsMouseOver) ShowTooltip(group.Button, ToolLabel(group));
        }
        if (selected != Tool.Move) document.Selected = null;
        UpdateNumberPreview();
    }

    internal void SetColor(int index)
    {
        style.SetColor(index);
        for (int i = 0; i < colorRings.Count; i++) colorRings[i].Visibility = i == index ? Visibility.Visible : Visibility.Hidden;
        if (textBox != null) StyleTextBox();
        UpdateNumberPreview();
    }

    internal void SetWidth(int index)
    {
        style.SetWidth(index);
        for (int i = 0; i < widthButtons.Count; i++) widthButtons[i].IsChecked = i == index;
        if (textBox != null) StyleTextBox();
        UpdateNumberPreview();
    }

    // One size step via the mouse wheel or +/-.
    void StepWidth(int direction)
    {
        if (!style.StepWidth(direction)) return;
        SetWidth(style.WidthIndex);
        ShowNotice(UiText.Editor.SizeNote(UiText.Editor.WidthNames[style.WidthIndex]));
    }

    void ShowTooltip(FrameworkElement target, string text)
    {
        tooltipText.Text = text;
        MeasureAfterTextChange(tooltip, tooltipText);
        Point position = target.TranslatePoint(new Point(0, 0), root);
        double toolbarTop = Canvas.GetTop(toolbar), toolbarHeight = toolbar.ActualHeight, tooltipHeight = tooltip.DesiredSize.Height;
        Rect monitor = MonitorArea();
        Canvas.SetLeft(tooltip, Math.Max(monitor.Left + 4, Math.Min(monitor.Right - 4 - tooltip.DesiredSize.Width, position.X + target.ActualWidth / 2 - tooltip.DesiredSize.Width / 2)));
        Canvas.SetTop(tooltip, toolbarTop + toolbarHeight + 8 + tooltipHeight < monitor.Bottom - 4 ? toolbarTop + toolbarHeight + 8 : toolbarTop - 8 - tooltipHeight);
        tooltip.Visibility = Visibility.Visible;
    }

    void ShowNotice(string text)
    {
        noticeText.Text = text;
        MeasureAfterTextChange(notice, noticeText);
        double toolbarTop = Canvas.GetTop(toolbar), noticeHeight = notice.DesiredSize.Height;
        Rect monitor = MonitorArea();
        Canvas.SetLeft(notice, Canvas.GetLeft(toolbar) + (toolbar.ActualWidth - notice.DesiredSize.Width) / 2);
        Canvas.SetTop(notice, toolbarTop - 12 - noticeHeight > monitor.Top + 4 ? toolbarTop - 12 - noticeHeight : toolbarTop + toolbar.ActualHeight + 12);
        notice.Visibility = Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetName(notice, text);
        noticeTimer.Stop();
        noticeTimer.Start();
    }

    // A new text only marks the text block for measuring: Measure on the chip would return its cached size
    // (the first time after Load the size without any text). So the whole chain is marked first.
    static void MeasureAfterTextChange(FrameworkElement chip, TextBlock text)
    {
        for (DependencyObject? element = text; element != null && element != chip; element = VisualTreeHelper.GetParent(element))
            (element as UIElement)?.InvalidateMeasure();
        chip.InvalidateMeasure();
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    }

    // ---------- For the pixel tests ----------

    internal Canvas Surface => root;
    internal FrameworkElement Notice => notice;
    internal void Notify(string text) => ShowNotice(text);
    internal FrameworkElement Toolbar => toolbar;
    internal FrameworkElement ToolMenu => toolMenu;
    internal void OpenGroupMenu(int groupIndex) => OpenMenu(groups[groupIndex]);
    internal void TypeText(string text) => textBox!.Text = text;
}
