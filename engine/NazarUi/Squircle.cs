namespace NazarCapture.Engine.NazarUi;

/// <summary>
/// The outline of every painted surface: a rectangle whose corners follow a superellipse, as Hams'
/// <c>_squircle_points</c> (ui_kit/surfaces.py). Only the corners are curved, the edges between them stay
/// straight; a superellipse over the whole surface would turn a wide button into an ellipse.
/// </summary>
internal static class Squircle
{
    /// <summary>Exponent of the superellipse (Hams' SQUIRCLE_EXPONENT): visibly softer than a circular arc.</summary>
    public const double Exponent = 4.2;

    /// <summary>Points per corner, as in Hams.</summary>
    public const int Steps = 24;

    /// <summary>The radius actually used: at most half the shorter side, never negative.</summary>
    public static double ClampRadius(double width, double height, double radius) =>
        Math.Max(0, Math.Min(radius, Math.Min(Math.Floor(width / 2), Math.Floor(height / 2))));

    /// <summary>
    /// The outline clockwise from the top edge, corners top right, bottom right, bottom left, top left. The
    /// shape spans 0..<paramref name="right"/> and 0..<paramref name="bottom"/>; Hams passes width - 1 and
    /// height - 1 (pixel centres), a WPF geometry the full width and height.
    /// </summary>
    public static Point[] Points(double right, double bottom, double radius, int steps = Steps)
    {
        radius = ClampRadius(right + 1, bottom + 1, radius);
        if (radius <= 0) return new[] { new Point(0, 0), new Point(right, 0), new Point(right, bottom), new Point(0, bottom) };

        double exponent = 2.0 / Exponent;
        var quarter = new (double X, double Y)[steps];
        for (int i = 0; i < steps; i++)
        {
            double t = i * (Math.PI / 2) / (steps - 1);
            quarter[i] = (radius * Math.Pow(Math.Max(0, Math.Cos(t)), exponent), radius * Math.Pow(Math.Max(0, Math.Sin(t)), exponent));
        }
        var points = new Point[4 * steps];
        int n = 0;
        foreach (var (x, y) in quarter) points[n++] = new Point(right - radius + y, radius - x);           // top right
        foreach (var (x, y) in quarter) points[n++] = new Point(right - radius + x, bottom - radius + y);  // bottom right
        foreach (var (x, y) in quarter) points[n++] = new Point(radius - y, bottom - radius + x);          // bottom left
        foreach (var (x, y) in quarter) points[n++] = new Point(radius - x, radius - y);                   // top left
        return points;
    }

    /// <summary>The outline as a frozen geometry covering 0..width, 0..height, moved down by <paramref name="top"/>.</summary>
    public static StreamGeometry Geometry(double width, double height, double radius, double top = 0)
    {
        var geometry = new StreamGeometry();
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        using (var context = geometry.Open())
        {
            var points = Points(width, height, Math.Min(radius, Math.Min(width, height) / 2));
            context.BeginFigure(Offset(points[0], top), true, true);
            var rest = new List<Point>(points.Length - 1);
            for (int i = 1; i < points.Length; i++) rest.Add(Offset(points[i], top));
            context.PolyLineTo(rest, true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>A plain rounded rectangle (circular corners) for the rare surface that is not a squircle.</summary>
    public static Geometry Rounded(double width, double height, double radius, double top = 0)
    {
        radius = Math.Min(radius, Math.Min(width, height) / 2);
        var geometry = new RectangleGeometry(new Rect(0, top, Math.Max(0, width), Math.Max(0, height)), radius, radius);
        geometry.Freeze();
        return geometry;
    }

    static Point Offset(Point p, double top) => new(p.X, p.Y + top);
}
