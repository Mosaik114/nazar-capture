using System.Windows.Controls;
using NazarCapture.Engine.Tests.Pixel;

namespace NazarCapture.Engine.Tests.NazarUi;

/// <summary>The painter of layer 2: squircle outline as in Hams, the stone's layout, and its looks per state.</summary>
public sealed class StoneChromeTests
{
    /// <summary>
    /// Every seventh point of Hams' <c>_squircle_points(width, height, radius)</c> (hams/ui_kit/surfaces.py, branch
    /// redesign), printed with Python and rounded to 3 decimals.
    /// </summary>
    public static TheoryData<int, int, int, double[]> HamsPoints => new()
    {
        { 120, 36, 10, new[] { 109.0, 0.0, 115.909, 0.551, 118.082, 2.306, 118.956, 6.131, 118.822, 30.359, 117.613, 33.337, 114.94, 34.721, 7.215, 34.989, 2.679, 34.278, 0.722, 32.321, 0.011, 27.785, 0.279, 4.06, 1.663, 1.387, 4.641, 0.178 } },
        { 40, 40, 14, new[] { 25.0, 0.0, 34.673, 0.771, 37.715, 3.228, 38.938, 8.583, 38.75, 32.502, 37.058, 36.672, 33.315, 38.609, 10.101, 38.984, 3.75, 37.989, 1.011, 35.25, 0.016, 28.899, 0.391, 5.685, 2.328, 1.942, 6.498, 0.25 } },
        { 300, 200, 14, new[] { 285.0, 0.0, 294.673, 0.771, 297.715, 3.228, 298.938, 8.583, 298.75, 192.502, 297.058, 196.672, 293.315, 198.609, 10.101, 198.984, 3.75, 197.989, 1.011, 195.25, 0.016, 188.899, 0.391, 5.685, 2.328, 1.942, 6.498, 0.25 } },
        { 22, 22, 6, new[] { 15.0, 0.0, 19.146, 0.33, 20.449, 1.384, 20.973, 3.678, 20.893, 18.215, 20.168, 20.002, 18.564, 20.832, 4.329, 20.993, 1.607, 20.567, 0.433, 19.393, 0.007, 16.671, 0.168, 2.436, 0.998, 0.832, 2.785, 0.107 } },
    };

    [Theory]
    [MemberData(nameof(HamsPoints))]
    public void SquirclePointsMatchHamsWithinHalfAPixel(int width, int height, int radius, double[] expected)
    {
        var points = Squircle.Points(width - 1, height - 1, radius);
        Assert.Equal(96, points.Length);
        for (int i = 0; i * 7 < points.Length; i++)
        {
            var p = points[i * 7];
            Assert.InRange(Math.Abs(p.X - expected[2 * i]), 0, 0.5);
            Assert.InRange(Math.Abs(p.Y - expected[2 * i + 1]), 0, 0.5);
        }
    }

    [Fact]
    public void RadiusIsClampedToHalfTheShorterSide()
    {
        Assert.Equal(5, Squircle.ClampRadius(40, 10, 14));
        Assert.Equal(0, Squircle.ClampRadius(40, 10, -3));
        Assert.Equal(4, Squircle.Points(39, 9, 0).Length);   // no radius: a plain rectangle
    }

    [Fact]
    public void TheSideAddsDepthBelowTheFaceAndPressingMovesTheChildDown() => WpfThread.Run(() =>
    {
        var child = new Border { Width = 80, Height = 30 };
        var stone = new StoneChrome { Side = Brushes.Gray, Fill = Brushes.White, Depth = 2, Padding = new Thickness(4, 1, 4, 1), Child = child };
        stone.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(new Size(88, 34), stone.DesiredSize);
        stone.Arrange(new Rect(stone.DesiredSize));
        Assert.Equal(1, child.TranslatePoint(new Point(), stone).Y);
        stone.IsPressed = true;
        stone.UpdateLayout();
        Assert.Equal(3, child.TranslatePoint(new Point(), stone).Y);

        stone.Side = null;   // no side colour: no side, no extra height
        stone.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(new Size(88, 32), stone.DesiredSize);
    });

    [Fact]
    public void PlateAndStoneUseTheirTokens() => WpfThread.Run(() =>
    {
        var plate = StoneChrome.Plate();
        Assert.Equal(UiTokens.Depth.Group, plate.Depth);
        Assert.Equal(UiTokens.Radius.Group, plate.Radius);
        var host = new Border { Child = plate };
        MuiTheme.Apply(host.Resources, true);
        Assert.Same(MuiTheme.Light[UiTokens.Keys.GroupSurface], plate.Fill);
        Assert.Same(MuiTheme.Light[UiTokens.Keys.GroupSide], plate.Side);
        MuiTheme.Apply(host.Resources, false);
        Assert.Same(MuiTheme.Dark[UiTokens.Keys.GroupSurface], plate.Fill);
        Assert.Equal(UiTokens.Depth.Control, StoneChrome.Stone().Depth);
        Assert.NotNull(StoneChrome.FloatingPlate().Effect);
    });

    /// <summary>Pressed: the side is gone, the face sits lower. Focus: a stronger edge in the focus colour.</summary>
    [Fact]
    public void PressedAndFocusChangeThePaintedPixels() => WpfThread.Run(() =>
    {
        Color Pixel(StoneChrome chrome, int x, int y)
        {
            chrome.Measure(new Size(60, 30));
            chrome.Arrange(new Rect(0, 0, 60, 30));
            var bitmap = new RenderTargetBitmap(60, 30, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(chrome);
            var data = new byte[4];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), data, 4, 0);
            return Color.FromArgb(data[3], data[2], data[1], data[0]);
        }
        StoneChrome Make() => new() { Width = 60, Height = 30, Fill = Brushes.White, Side = Brushes.Black, Ink = Brushes.Red, FocusInk = Brushes.Blue, Depth = 3, Radius = 6 };

        var normal = Make();
        Assert.Equal(Colors.Black, Pixel(normal, 30, 28));      // the side below the face
        Assert.Equal(Colors.White, Pixel(normal, 30, 1));       // the face starts at the top
        var pressed = Make();
        pressed.IsPressed = true;
        Assert.Equal(Colors.White, Pixel(pressed, 30, 26));     // sunk: face where the side was
        Assert.Equal(0, Pixel(pressed, 30, 1).A);               // and nothing above it
        var focused = Make();
        focused.ShowFocus = true;
        var edge = Pixel(focused, 30, 0);
        Assert.True(edge.B > 150 && edge.R < 100, "focus edge is blue: " + edge);
    });

    [Theory]
    [MemberData(nameof(PixelMatrixTests.Variants), MemberType = typeof(PixelMatrixTests))]
    [Trait("Category", "Pixel")]
    public void SurfaceSheet(bool light, double scale) => WpfThread.Run(() =>
    {
        PixelComparison.RequireFonts();
        var check = new PixelComparison();
        var sheet = KitSamples.Surfaces();
        MuiTheme.Apply(sheet.Resources, light);
        check.Check("kit_surfaces." + (light ? "light" : "dark") + "." + ((int)Math.Round(scale * 100)).ToString(CultureInfo.InvariantCulture),
            PixelMatrixTests.Draw(sheet, scale));
        check.AssertAllMatch();
    });
}
