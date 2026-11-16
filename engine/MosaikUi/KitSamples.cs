namespace MosaikCapture.Engine.MosaikUi;

/// <summary>
/// The sample sheets of the kit: every surface in every state, built from the kit alone. The pixel tests render
/// them in both themes and at 100 and 150 %; <c>--kit-demo</c> shows them in a window.
/// </summary>
internal static partial class KitSamples
{
    /// <summary>One state of a surface: name, fill and side tokens, pressed, focused, text token.</summary>
    internal readonly record struct SurfaceState(string Name, string Fill, string Side, bool Pressed, bool Focus, string Text);

    /// <summary>The five states of a secondary stone, as Hams' ControlStyle resolves them.</summary>
    internal static readonly SurfaceState[] SecondaryStates =
    {
        new("Normal", UiTokens.Keys.ControlSurface, UiTokens.Keys.ControlSide, false, false, UiTokens.Keys.TextPrimary),
        new("Hover", UiTokens.Keys.ControlHover, UiTokens.Keys.ControlSide, false, false, UiTokens.Keys.TextPrimary),
        new("Gedrückt", UiTokens.Keys.ControlPressed, UiTokens.Keys.ControlSide, true, false, UiTokens.Keys.TextPrimary),
        new("Fokus", UiTokens.Keys.ControlSurface, UiTokens.Keys.ControlSide, false, true, UiTokens.Keys.TextPrimary),
        new("Aus", UiTokens.Keys.ControlSurface, UiTokens.Keys.ControlSide, false, false, UiTokens.Keys.TextDisabled),
    };

    /// <summary>The same five states of the primary (accent) stone.</summary>
    internal static readonly SurfaceState[] PrimaryStates =
    {
        new("Normal", UiTokens.Keys.AccentSurface, UiTokens.Keys.AccentSide, false, false, UiTokens.Keys.OnAccent),
        new("Hover", UiTokens.Keys.AccentHover, UiTokens.Keys.AccentSide, false, false, UiTokens.Keys.OnAccent),
        new("Gedrückt", UiTokens.Keys.AccentPressed, UiTokens.Keys.AccentSide, true, false, UiTokens.Keys.OnAccent),
        new("Fokus", UiTokens.Keys.AccentSurface, UiTokens.Keys.AccentSide, false, true, UiTokens.Keys.OnAccent),
        new("Aus", UiTokens.Keys.ControlSurface, UiTokens.Keys.ControlSide, false, false, UiTokens.Keys.TextDisabled),
    };

    /// <summary>The sheet of phase 2: stones in all states (secondary, primary), a key cap, a plate and a floating plate.</summary>
    public static FrameworkElement Surfaces()
    {
        var sheet = Sheet();
        var column = new StackPanel { Margin = new Thickness(UiTokens.Space.Window) };
        sheet.Child = column;
        column.Children.Add(Row(SecondaryStates, primaryFocus: false));
        column.Children.Add(Row(PrimaryStates, primaryFocus: true));

        var plates = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, UiTokens.Space.SectionGap, 0, 0) };
        var cap = KeyCap("Umschalt");
        cap.VerticalAlignment = VerticalAlignment.Top;
        plates.Children.Add(cap);
        var plate = StoneChrome.Plate();
        plate.Margin = new Thickness(UiTokens.Space.Xl, 0, 0, 0);
        plate.Child = Label("Platte", UiTokens.Keys.TextPrimary, 200, 64);
        plates.Children.Add(plate);
        var floating = StoneChrome.FloatingPlate();
        floating.Margin = new Thickness(UiTokens.Space.SectionGap, 0, UiTokens.Space.Xl, UiTokens.Space.Xl);
        floating.Child = Label("Schwebend", UiTokens.Keys.TextPrimary, 200, 64);
        plates.Children.Add(floating);
        column.Children.Add(plates);
        return sheet;
    }

    /// <summary>A paper sheet (window background) carrying a sample page.</summary>
    internal static Border Sheet()
    {
        var sheet = new Border();
        sheet.SetResourceReference(Border.BackgroundProperty, UiTokens.Keys.WindowBg);
        return sheet;
    }

    static StackPanel Row(SurfaceState[] states, bool primaryFocus)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, UiTokens.Space.Lg) };
        foreach (var state in states)
        {
            var stone = new StoneChrome { Margin = new Thickness(0, 0, UiTokens.Space.ControlGap, 0) };
            stone.SetResourceReference(StoneChrome.FillProperty, state.Fill);
            stone.SetResourceReference(StoneChrome.SideProperty, state.Side);
            stone.SetResourceReference(StoneChrome.InkProperty, UiTokens.Keys.Ink);
            // On a filled role the focus colour would not show; the edge takes the text colour (Hams' _ON_FILLS).
            stone.SetResourceReference(StoneChrome.FocusInkProperty, primaryFocus ? UiTokens.Keys.OnAccent : UiTokens.Keys.Focus);
            stone.IsPressed = state.Pressed;
            stone.ShowFocus = state.Focus;
            stone.Child = Label(state.Name, state.Text, 104, UiTokens.Height.Standard);
            row.Children.Add(stone);
        }
        return row;
    }

    static StoneChrome KeyCap(string text)
    {
        var cap = new StoneChrome { Depth = UiTokens.Depth.Keycap, Radius = UiTokens.Radius.Keycap, Padding = new Thickness(UiTokens.Space.Md, 0, UiTokens.Space.Md, 0) };
        cap.SetResourceReference(StoneChrome.FillProperty, UiTokens.Keys.ControlSurface);
        cap.SetResourceReference(StoneChrome.SideProperty, UiTokens.Keys.ControlSide);
        cap.SetResourceReference(StoneChrome.InkProperty, UiTokens.Keys.Ink);
        var text_ = Label(text, UiTokens.Keys.TextPrimary, double.NaN, UiTokens.Height.Keycap);
        text_.FontSize = 12;
        cap.Child = text_;
        return cap;
    }

    static TextBlock Label(string text, string foreground, double width, double height)
    {
        var label = new TextBlock
        {
            Text = text,
            Width = width,
            Height = height,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Padding = new Thickness(0, Math.Max(0, (height - 19) / 2), 0, 0),
        };
        label.SetResourceReference(TextBlock.FontFamilyProperty, MuiTheme.RoundedFontKey);
        label.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        return label;
    }
}
