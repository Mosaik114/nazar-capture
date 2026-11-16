namespace NazarCapture.Engine.Core;

/// <summary>
/// Sizes of the editor and overlay parts (in WPF units); shadows come from the kit (Elevation) (in WPF units), so toolbars, menus, chips and
/// cards look alike everywhere and the numbers have names.
/// </summary>
internal static class Metrics
{
    // ---------- Toolbars and buttons ----------

    /// <summary>Square toolbar button (editor, pinned image).</summary>
    public const double ButtonSize = 36;
    public const double ButtonRadius = 8;
    public const double IconSize = 16;
    public const double ToolbarRadius = 12;
    public const double ToolbarPadding = 6;
    public const double SeparatorHeight = 22;
    public const double SeparatorGap = 6;

    /// <summary>Width-choice buttons and their dots (even sizes sit exactly centered).</summary>
    public const double WidthButtonSize = 24, WidthButtonRadius = 6;
    public static readonly double[] WidthDots = { 4, 6, 8, 10, 12 };

    /// <summary>Color choice: hit area, selection ring and color dot.</summary>
    public const double ColorButtonWidth = 26, ColorRingSize = 24, ColorDotSize = 16, ColorRingThickness = 1.5;

    // ---------- Menus and chips ----------

    public const double MenuRadius = 10;
    public const double MenuPadding = 4;
    public const double MenuMinWidth = 196;
    public const double NoticeRadius = 10;
    public static readonly Thickness NoticePadding = new(14, 8, 14, 8);

    // ---------- Crop handles and markup ----------

    /// <summary>Visible handle dot and its larger hit area.</summary>
    public const double HandleSize = 11, HandleHitSize = 19, HandleStroke = 2;
    /// <summary>Corner radius of the spotlight hole and the invisible edge that moves it.</summary>
    public const double SpotlightRadius = 8, SpotlightGrabWidth = 12;
}
