namespace NazarCapture.Engine.NazarUi;

/// <summary>
/// A row of tabs like Hams' TabBar (ui_kit/tabs.py): words on the paper, the chosen one on a sheet of its own with its
/// word in the accent colour (roles <see cref="MuiRole.Tab"/> and <see cref="MuiRole.TabSelected"/>). Each tab keeps its
/// natural width (word plus padding) and the row wraps when the window is narrow; nothing is stretched.
/// Keyboard like a tab row in Windows: the row is one stop in the Tab order (the chosen tab), the arrow keys move to
/// the neighbour and show it at once, Home and End jump to the ends.
/// </summary>
internal sealed class TabBar : WrapPanel
{
    /// <summary>Padding left and right of a tab's word; tabs are slimmer than buttons.</summary>
    public const double TabPadding = UiTokens.Space.Lg;
    /// <summary>Gap between two tabs (and two rows).</summary>
    public const double Gap = UiTokens.Space.Xs;

    readonly List<KitButton> buttons = new();

    public TabBar(IEnumerable<string> titles)
    {
        Background = Brushes.Transparent;
        WindowDrag.SetIsSurface(this, true);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Once);
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.None);
        System.Windows.Automation.AutomationProperties.SetName(this, UiText.Settings.Tabs);
        foreach (string title in titles)
        {
            int index = buttons.Count;
            var button = new KitButton
            {
                Height = UiTokens.Height.Standard + UiTokens.Depth.Control,
                Padding = new Thickness(TabPadding, 0, TabPadding, 0),
                Margin = new Thickness(0, 0, Gap, Gap),
                Content = ButtonContent.Label(title, null),
            };
            ButtonContent.Name(button, title);
            Mui.SetRole(button, MuiRole.Tab);
            button.Click += (_, _) => Choose(index);
            buttons.Add(button);
            Children.Add(button);
        }
        PreviewKeyDown += OnKeyDown;
        Show(0);
    }

    /// <summary>A tab was chosen by click or keys.</summary>
    public event Action<int>? Chosen;

    public int Selected { get; private set; } = -1;

    public IReadOnlyList<KitButton> Buttons => buttons;

    /// <summary>Marks <paramref name="index"/> as the chosen tab (look and Tab stop), without raising <see cref="Chosen"/>.</summary>
    public void Show(int index)
    {
        if (index < 0 || index >= buttons.Count) return;
        Selected = index;
        for (int i = 0; i < buttons.Count; i++)
        {
            bool chosen = i == index;
            Mui.SetRole(buttons[i], chosen ? MuiRole.TabSelected : MuiRole.Tab);
            buttons[i].IsTabStop = chosen;   // one stop in the Tab order: the arrows reach the others
            System.Windows.Automation.AutomationProperties.SetItemStatus(buttons[i], chosen ? UiText.Settings.TabChosen : "");
        }
    }

    void Choose(int index)
    {
        if (index < 0 || index >= buttons.Count) return;
        Show(index);
        Chosen?.Invoke(index);
    }

    /// <summary>Moves to a tab by key and keeps the focus on it (the arrows belong to the tabs here).</summary>
    internal void Jump(int index)
    {
        index = Math.Max(0, Math.Min(buttons.Count - 1, index));
        if (index != Selected) Choose(index);
        buttons[index].Focus();
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        int focused = buttons.FindIndex(b => b.IsKeyboardFocused);
        if (focused < 0) return;
        switch (e.Key)
        {
            case Key.Left: Jump(focused - 1); break;
            case Key.Right: Jump(focused + 1); break;
            case Key.Home: Jump(0); break;
            case Key.End: Jump(buttons.Count - 1); break;
            default: return;
        }
        e.Handled = true;
    }
}
