using System.Windows.Documents;

namespace NazarCapture.Engine.NazarUi;

/// <summary>The text roles of the kit (Hams' _FONT_ROLES in ui_kit/scale.py, points converted to WPF pixels).</summary>
internal enum TextRole
{
    /// <summary>Window title: rounded, bold, 16 pt.</summary>
    WindowTitle,
    /// <summary>Section heading above a setting group: rounded, bold, 13 pt.</summary>
    Section,
    /// <summary>Title of a setting row or menu item: semibold, 12 pt.</summary>
    RowTitle,
    /// <summary>Help text under a title: 11 pt.</summary>
    Description,
    /// <summary>Button labels: rounded, bold, 12 pt.</summary>
    Button,
    /// <summary>Key caps and small chips: 11 pt (Hams' "shortcut").</summary>
    Shortcut,
    /// <summary>Card titles: rounded, bold, 12.5 pt (between button and section; the cards are small floating panels).</summary>
    CardTitle,
    /// <summary>Small labels under icons and on images: 9 pt.</summary>
    Caption,
}

/// <summary>Font family, size and weight of every <see cref="TextRole"/>, applied as resource references.</summary>
internal static class TextStyle
{
    /// <summary>Points to WPF pixels (1/96 inch).</summary>
    public static double Px(double points) => Math.Round(points * 96 / 72, 2);

    public static double Size(TextRole role) => role switch
    {
        TextRole.WindowTitle => Px(16),
        TextRole.Section => Px(13),
        TextRole.RowTitle => Px(12),
        TextRole.Description => Px(11),
        TextRole.Button => Px(12),
        TextRole.Shortcut => Px(11),
        TextRole.CardTitle => Px(12.5),
        _ => Px(9),
    };

    public static FontWeight Weight(TextRole role) => role switch
    {
        TextRole.WindowTitle or TextRole.Section or TextRole.Button or TextRole.CardTitle => FontWeights.Bold,
        TextRole.RowTitle => FontWeights.SemiBold,
        _ => FontWeights.Normal,
    };

    public static bool Rounded(TextRole role) => role is TextRole.WindowTitle or TextRole.Section or TextRole.Button or TextRole.CardTitle;

    /// <summary>Gives <paramref name="element"/> (TextBlock, Control or any element via TextElement) the role's font.</summary>
    public static T Apply<T>(T element, TextRole role) where T : FrameworkElement
    {
        element.SetResourceReference(TextElement.FontFamilyProperty, Rounded(role) ? MuiTheme.RoundedFontKey : MuiTheme.BodyFontKey);
        element.SetValue(TextElement.FontSizeProperty, Size(role));
        element.SetValue(TextElement.FontWeightProperty, Weight(role));
        return element;
    }

    /// <summary>A text in a role and a colour token.</summary>
    public static TextBlock Text(string text, TextRole role, string colorKey = UiTokens.Keys.TextPrimary)
    {
        var block = Apply(new TextBlock { Text = text }, role);
        block.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
        return block;
    }

    /// <summary>An icon glyph (Segoe Fluent Icons) at the given size; the colour comes from the surrounding text.</summary>
    public static TextBlock Icon(string glyph, double size = 16) => new()
    {
        Text = glyph,
        FontFamily = Palette.IconFont,
        FontSize = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
    };
}
