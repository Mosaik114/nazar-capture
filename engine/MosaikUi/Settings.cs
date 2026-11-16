using System.Windows.Controls.Primitives;

namespace MosaikCapture.Engine.MosaikUi;

/// <summary>
/// One setting row (Hams' SettingRow), four forms: title and control; title, description and control; title with a
/// status line (dot and text, an error in the error colour) and control; title and a value. <c>stacked</c> puts the
/// control below the text in full width (long values). At least 68 high with its padding; a description wraps instead of being cut.
/// A row with a <see cref="ToggleSwitch"/> is a trigger as a whole: a click anywhere on it flips the switch.
/// </summary>
internal sealed class SettingRow : Grid
{
    readonly TextBlock title;
    readonly TextBlock description;
    readonly StatusDot dot = new(Status.None) { Margin = new Thickness(0, 5, UiTokens.Space.IconGap, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top };
    string baseDescription;
    UIElement? control;

    public SettingRow(string title, string? description = null, UIElement? control = null, bool stacked = false)
    {
        baseDescription = description ?? "";
        Margin = new Thickness(UiTokens.Space.RowX, UiTokens.Space.RowY, UiTokens.Space.RowX, UiTokens.Space.RowY);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Background = Brushes.Transparent;   // the whole row is hit-testable (switch rows)

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        this.title = TextStyle.Text(title, TextRole.RowTitle);
        this.title.TextWrapping = TextWrapping.Wrap;
        texts.Children.Add(this.title);
        this.description = TextStyle.Text(baseDescription, TextRole.Description, UiTokens.Keys.TextSecondary);
        this.description.TextWrapping = TextWrapping.Wrap;
        // a wrapping text needs a width: dot and text share a dock panel rather than a horizontal stack
        var lineGrid = new DockPanel { Margin = new Thickness(0, UiTokens.Space.TextGap, 0, 0), LastChildFill = true };
        DockPanel.SetDock(dot, Dock.Left);
        lineGrid.Children.Add(dot);
        lineGrid.Children.Add(this.description);
        lineGrid.Visibility = baseDescription.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        texts.Children.Add(lineGrid);
        Lines = lineGrid;
        Children.Add(texts);

        Stacked = stacked;
        // the row is at least RowMin high with its padding (Hams: minsize ROW_MIN - 2 * ROW_Y); a stacked row grows with its content
        MinHeight = stacked ? 0 : UiTokens.Height.RowMin - 2 * UiTokens.Space.RowY;
        if (stacked) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (stacked) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SetControl(control);
        AutomationName(title);
    }

    DockPanel Lines { get; }

    public bool Stacked { get; }

    public string Title => title.Text;

    public string Description => description.Text;

    public Status Status => dot.Status;

    public UIElement? Control => control;

    /// <summary>A row that only shows a value (Hams' "display" form): the value right, in the title role.</summary>
    public static SettingRow Display(string title, string value, string? description = null) =>
        new(title, description, TextStyle.Text(value, TextRole.RowTitle, UiTokens.Keys.TextSecondary));

    public void SetControl(UIElement? value)
    {
        if (control != null) Children.Remove(control);
        control = value;
        if (control == null) return;
        if (control is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.Margin = Stacked ? new Thickness(0, UiTokens.Space.Md, 0, 0) : new Thickness(UiTokens.Space.ControlGap, 0, 0, 0);
        }
        if (Stacked) { SetRow(control, 1); SetColumnSpan(control, 2); }
        else SetColumn(control, 1);
        Children.Add(control);
        if (control is ToggleSwitch toggle)
            MouseLeftButtonUp += (_, e) =>
            {
                if (toggle.IsEnabled && !toggle.IsMouseOver && !e.Handled) { toggle.IsChecked = toggle.IsChecked != true; e.Handled = true; }
            };
    }

    /// <summary>The help text (also the text a status falls back to).</summary>
    public void SetDescription(string text)
    {
        baseDescription = text ?? "";
        ShowStatus("", Status.None);
    }

    /// <summary>
    /// Shows a result in the row itself (Hams' status form): a dot and the text; an error in the error colour. An empty
    /// text returns to the description.
    /// </summary>
    public void ShowStatus(string text, Status status)
    {
        bool own = !string.IsNullOrEmpty(text);
        description.Text = own ? text : baseDescription;
        var shown = own ? status : Status.None;
        dot.Show(shown);
        dot.Visibility = shown == Status.None ? Visibility.Collapsed : Visibility.Visible;
        description.SetResourceReference(TextBlock.ForegroundProperty, shown == Status.Error ? UiTokens.Keys.Error : UiTokens.Keys.TextSecondary);
        Lines.Visibility = description.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (control is FrameworkElement fe)
            System.Windows.Automation.AutomationProperties.SetHelpText(fe, description.Text);
    }

    void AutomationName(string name)
    {
        System.Windows.Automation.AutomationProperties.SetName(this, name);
        if (control is FrameworkElement fe && string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(fe)))
            System.Windows.Automation.AutomationProperties.SetName(fe, name);
        if (control is FrameworkElement withHelp) System.Windows.Automation.AutomationProperties.SetHelpText(withHelp, baseDescription);
    }
}

/// <summary>
/// A group of setting rows on a paper plate (radius 14, depth 3), with a section heading above it; the rows are
/// separated by hairlines (Hams' settings sections).
/// </summary>
internal sealed class SettingGroup : StackPanel
{
    readonly StackPanel rows = new() { Margin = new Thickness(UiTokens.Space.GroupInset, 0, UiTokens.Space.GroupInset, 0) };

    public SettingGroup(string? heading = null)
    {
        if (heading != null)
        {
            var text = TextStyle.Text(heading, TextRole.Section);
            // flush with the row text below it (group inset plus row padding), as Hams' group()
            text.Margin = new Thickness(UiTokens.Space.GroupInset + UiTokens.Space.RowX, 0, 0, UiTokens.Space.GroupGap);
            Children.Add(text);
            System.Windows.Automation.AutomationProperties.SetName(this, heading);
        }
        var plate = StoneChrome.Plate();
        plate.Child = rows;
        Children.Add(plate);
    }

    public IEnumerable<SettingRow> Rows => rows.Children.OfType<SettingRow>();

    public SettingRow Add(SettingRow row)
    {
        if (rows.Children.Count > 0) rows.Children.Add(new KitSeparator { Margin = new Thickness(UiTokens.Space.RowX, 0, UiTokens.Space.RowX, 0) });
        rows.Children.Add(row);
        return row;
    }

    /// <summary>Shows or hides a row together with its hairline (a notice that comes and goes, as Hams' update row).</summary>
    public void SetRowVisible(SettingRow row, bool visible)
    {
        int index = rows.Children.IndexOf(row);
        if (index < 0) return;
        row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        int line = index > 0 ? index - 1 : index + 1;
        if (line < rows.Children.Count && rows.Children[line] is KitSeparator separator) separator.Visibility = row.Visibility;
    }
}

/// <summary>The round mark left of a card's title: a check in the accent, a warning on the error fill.</summary>
internal sealed class StatusMark : StoneChrome
{
    readonly TextBlock glyph = TextStyle.Icon(IconGlyphs.Accept, 11);

    public StatusMark()
    {
        Width = Height = 22;
        IsSquircle = false;
        Radius = 11;
        Depth = 0;
        Child = glyph;
        VerticalAlignment = VerticalAlignment.Top;
        Show(IconGlyphs.Accept, false);
    }

    public string Glyph => glyph.Text;

    public bool Warning { get; private set; }

    public void Show(string symbol, bool warning)
    {
        glyph.Text = symbol;
        Warning = warning;
        SetResourceReference(FillProperty, warning ? UiTokens.Keys.ErrorSurface : UiTokens.Keys.AccentSurface);
        glyph.SetResourceReference(TextBlock.ForegroundProperty, warning ? UiTokens.Keys.OnFill : UiTokens.Keys.OnAccent);
    }
}

/// <summary>
/// A floating card (screenshot and recording result): a plate with the desktop shadow instead of a soft box, header
/// with mark, title and subtitle. An error card (<see cref="ShowError"/>) shows its state in the mark and the title and
/// stays until it is closed.
/// </summary>
internal sealed class Card : StoneChrome
{
    public const double ContentWidth = 314;

    public Card()
    {
        Depth = UiTokens.Depth.Group;
        Radius = UiTokens.Radius.Group;
        SetResourceReference(FillProperty, UiTokens.Keys.GroupSurface);
        SetResourceReference(SideProperty, UiTokens.Keys.GroupSide);
        SetResourceReference(InkProperty, UiTokens.Keys.Ink);
        SetResourceReference(FocusInkProperty, UiTokens.Keys.Focus);
        Effect = Elevation.Floating();
        Padding = new Thickness(UiTokens.Space.Lg);
        Margin = new Thickness(Elevation.Margin);
        Width = ContentWidth + 2 * UiTokens.Space.Lg;
        Mark = new StatusMark { Margin = new Thickness(0, 1, UiTokens.Space.Md + UiTokens.Space.Xs, 0) };
        Title = TextStyle.Text("", TextRole.CardTitle);
        Title.TextTrimming = TextTrimming.CharacterEllipsis;
        Subtitle = TextStyle.Text("", TextRole.Description, UiTokens.Keys.TextSecondary);
        Subtitle.Margin = new Thickness(0, UiTokens.Space.TextGap, 0, 0);
        Subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        var texts = new StackPanel();
        texts.Children.Add(Title);
        texts.Children.Add(Subtitle);
        Header = new DockPanel { Margin = new Thickness(UiTokens.Space.Xs, UiTokens.Space.Lg, UiTokens.Space.Xs, 0) };
        DockPanel.SetDock(Mark, Dock.Left);
        Header.Children.Add(Mark);
        Header.Children.Add(texts);
        Body = new StackPanel();
        Child = Body;
    }

    public StackPanel Body { get; }

    public DockPanel Header { get; }

    public StatusMark Mark { get; }

    public TextBlock Title { get; }

    public TextBlock Subtitle { get; }

    public bool IsError { get; private set; }

    /// <summary>The row of equally wide <see cref="ActionButton"/>s.</summary>
    public static UniformGrid ActionRow() => new() { Rows = 1, Margin = new Thickness(0, UiTokens.Space.Lg, 0, 0) };

    /// <summary>Normal state: the check (or another symbol) in the accent, the title in ink.</summary>
    public void ShowNormal(string symbol, string title, string subtitle)
    {
        IsError = false;
        Mark.Show(symbol, false);
        Title.Text = title;
        Subtitle.Text = subtitle;
        Title.SetResourceReference(TextBlock.ForegroundProperty, UiTokens.Keys.TextPrimary);
        System.Windows.Automation.AutomationProperties.SetName(this, title);
    }

    /// <summary>Error state: warning mark, title in the error colour; the card stays until closed.</summary>
    public void ShowError(string title, string subtitle)
    {
        IsError = true;
        Mark.Show(IconGlyphs.Warning, true);
        Title.Text = title;
        Subtitle.Text = subtitle;
        Title.SetResourceReference(TextBlock.ForegroundProperty, UiTokens.Keys.Error);
        System.Windows.Automation.AutomationProperties.SetName(this, title);
    }
}
