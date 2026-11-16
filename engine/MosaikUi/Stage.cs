using System.Windows.Shell;

namespace MosaikCapture.Engine.MosaikUi;

/// <summary>
/// The painted head of a settings window, as Hams' Stage (window_stage.py): the app mark, the title, a quiet painted
/// banner in the free stretch between title and cross (it stands against the cross) and the round close stone. The
/// cross stands with the middle of its face on the middle of the stage: a stone is taller than its face by its side.
/// The banner's motif is the app's own (<see cref="BannerMotif"/>); everything else is the family's.
/// </summary>
internal sealed class Stage : Grid
{
    /// <summary>Height of the stage (Hams' Stage.HEIGHT).</summary>
    public const double StageHeight = 76;
    /// <summary>Free space between the title and the beginning of the banner.</summary>
    public const double TitleGap = UiTokens.Space.Xl;

    readonly TextBlock title;

    public Stage(string title, Action close, string closeTooltip, BannerMotif motif)
    {
        Height = StageHeight;
        Background = Brushes.Transparent;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var mark = new AppMark(AppMark.StageSize) { Margin = new Thickness(UiTokens.Space.Window, 0, UiTokens.Space.Md, 0) };
        Children.Add(mark);
        this.title = TextStyle.Text(title, TextRole.WindowTitle);
        this.title.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(this.title, 1);
        Children.Add(this.title);
        Banner = new StageBanner(motif) { Margin = new Thickness(TitleGap, 0, UiTokens.Space.Lg, 0) };
        SetColumn(Banner, 2);
        Children.Add(Banner);
        CloseButton = IconButton.Stone(LineIcon.Shape.Close, closeTooltip, close, UiTokens.Keys.Error);
        CloseButton.VerticalAlignment = VerticalAlignment.Center;
        CloseButton.Margin = new Thickness(0, UiTokens.Depth.Control, UiTokens.Space.Window, 0);   // the face on the middle line
        WindowChrome.SetIsHitTestVisibleInChrome(CloseButton, true);
        SetColumn(CloseButton, 3);
        Children.Add(CloseButton);
    }

    public string Title
    {
        get => title.Text;
        set => title.Text = value;
    }

    public StageBanner Banner { get; }

    public IconButton CloseButton { get; }
}

/// <summary>
/// The banner of a <see cref="Stage"/>: a motif in ink and watercolour (Tusche und Aquarell), kept calm for a work
/// tool. It covers only the free stretch (capped, and in steps, so a resize repaints only when a step is crossed) and
/// fades into the paper at both ends: long towards the title, short at the cross. Painted in the accent of the app.
/// </summary>
internal sealed class StageBanner : FrameworkElement
{
    /// <summary>Shortest stretch worth a banner; below it the stage stays plain paper.</summary>
    public const double MinLine = 64;
    /// <summary>Longest stretch: a wide window keeps one short motif near the cross.</summary>
    public const double MaxLine = 300;
    /// <summary>The banner's width comes in steps of this much.</summary>
    public const double Step = 16;

    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(nameof(Tint), typeof(Brush), typeof(StageBanner),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty WashProperty = DependencyProperty.Register(nameof(Wash), typeof(Brush), typeof(StageBanner),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    readonly BannerMotif motif;

    public StageBanner(BannerMotif motif)
    {
        this.motif = motif;
        IsHitTestVisible = false;   // the stage underneath moves the window
        SetResourceReference(TintProperty, UiTokens.Keys.AccentTint);
        SetResourceReference(WashProperty, UiTokens.Keys.AccentSurface);
    }

    public Brush? Tint { get => (Brush?)GetValue(TintProperty); set => SetValue(TintProperty, value); }

    public Brush? Wash { get => (Brush?)GetValue(WashProperty); set => SetValue(WashProperty, value); }

    /// <summary>How wide the picture is for a free stretch: capped and in steps, 0 when too short (Hams' track_width).</summary>
    public static double TrackWidth(double stretch)
    {
        stretch = Math.Min(Math.Floor(stretch), MaxLine);
        if (stretch < MinLine) return 0;
        return stretch - stretch % Step;
    }

    /// <summary>The width painted now.</summary>
    public double Painted => TrackWidth(ActualWidth);

    protected override Size MeasureOverride(Size available) => new(0, 0);

    protected override void OnRender(DrawingContext dc)
    {
        double width = Painted, height = ActualHeight;
        if (width <= 0 || height <= 0 || Tint is not SolidColorBrush tint || Wash is not SolidColorBrush wash) return;
        dc.PushTransform(new TranslateTransform(ActualWidth - width, 0));
        motif.Paint(dc, width, height, tint.Color, wash.Color);
        dc.Pop();
    }
}

/// <summary>What an app paints on its stage banner; the only part of the stage that is not the family's.</summary>
internal abstract class BannerMotif
{
    /// <summary>Paints the motif into a stretch of <paramref name="width"/> x <paramref name="height"/>.</summary>
    public abstract void Paint(DrawingContext dc, double width, double height, Color tint, Color wash);

    /// <summary>1 inside, 0 at both ends: a long soft start (towards the title), a short end at the cross.</summary>
    public static double Fade(double u)
    {
        double left = Clamp(u / 0.18), right = Clamp((1 - u) / 0.06);
        return left * left * (3 - 2 * left) * (right * right * (3 - 2 * right));
    }

    /// <summary>The fade as an opacity mask over [0, width].</summary>
    protected static Brush FadeMask(double width)
    {
        var stops = new GradientStopCollection();
        for (int i = 0; i <= 32; i++) stops.Add(new GradientStop(Color.FromScRgb((float)Fade(i / 32.0), 0, 0, 0), i / 32.0));
        var brush = new LinearGradientBrush(stops, new Point(0, 0), new Point(width, 0)) { MappingMode = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    protected static double Clamp(double value) => Math.Max(0, Math.Min(1, value));

    protected static Pen InkPen(Color tint, double opacity, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Clamp(opacity)), tint.R, tint.G, tint.B)), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }
}

/// <summary>
/// Nazar's banner: a viewfinder in ink and watercolour. A fine horizon line with a scale of small ticks (denser and
/// taller near the frame, like the swings of Hams' voice track near a loud word), four selection corners framing the
/// point of focus, a lens ring around it, and a soft cobalt wash pooling inside the frame and running thin along the
/// line. The logo's concept ("Fokus": lens ring, square corner, point) in the head of the window.
/// </summary>
internal sealed class ViewfinderMotif : BannerMotif
{
    /// <summary>Opacity of the ink at its strongest (Hams' INK_LINE).</summary>
    public const double InkLine = 0.62;
    /// <summary>Strength of the wash inside the frame (Hams' WASH). Small on purpose.</summary>
    public const double WashStrength = 0.20;
    /// <summary>Where the frame stands, as a share of the stretch.</summary>
    public const double Centre = 0.64;
    /// <summary>Distance between two ticks of the scale (one swing of Hams' track is 3.4).</summary>
    public const double TickStep = 5;
    /// <summary>Pixels per logical pixel of the wash picture; it is soft, so it scales without showing it.</summary>
    const int WashScale = 2;

    [ThreadStatic] static Dictionary<(int, int, Color, Color), BitmapSource>? washes;

    /// <summary>0..1, how close <paramref name="u"/> lies to the frame (the "loudness" of the motif).</summary>
    public static double Near(double u) => Math.Exp(-Math.Pow((u - Centre) / 0.17, 2));

    /// <summary>Half height and half width of the frame for a stretch.</summary>
    public static (double HalfHeight, double HalfWidth) Frame(double width, double height)
    {
        double half = Math.Min(0.29 * height, 0.16 * width);
        return (half, 1.34 * half);
    }

    public override void Paint(DrawingContext dc, double width, double height, Color tint, Color wash)
    {
        double mid = Math.Round(height / 2) + 0.5, cx = Math.Round(Centre * width) + 0.5;
        var (fh, fw) = Frame(width, height);
        double ring = 0.42 * fh;

        dc.DrawImage(Wash((int)width, (int)height, tint, wash), new Rect(0, 0, width, height));

        dc.PushOpacityMask(FadeMask(width));
        // the horizon, broken by the lens ring
        var line = InkPen(tint, 0.40 * InkLine, 1);
        dc.DrawLine(line, new Point(0, mid), new Point(cx - ring - 3, mid));
        dc.DrawLine(line, new Point(cx + ring + 3, mid), new Point(width, mid));
        // the scale: ticks outside the frame, taller and stronger towards it, every fourth a little longer
        int index = 0;
        for (double x = TickStep / 2; x < width; x += TickStep, index++)
        {
            if (Math.Abs(x - cx) < fw + 4) continue;
            double near = Near(x / width);
            double half = (index % 4 == 0 ? 2.6 : 1.4) + 0.20 * height * Math.Pow(near, 1.6);
            double px = Math.Round(x) + 0.5;
            dc.DrawLine(InkPen(tint, InkLine * (0.30 + 0.62 * near), 1), new Point(px, mid - half), new Point(px, mid + half));
        }
        // the four selection corners
        var corner = InkPen(tint, InkLine * 1.15, 1.4);
        double arm = Math.Max(5, 0.44 * fh);
        foreach (int sx in new[] { -1, 1 })
            foreach (int sy in new[] { -1, 1 })
            {
                var tip = new Point(cx + sx * fw, mid + sy * fh);
                var figure = new StreamGeometry();
                using (var g = figure.Open())
                {
                    g.BeginFigure(new Point(tip.X - sx * arm, tip.Y), false, false);
                    g.LineTo(tip, true, true);
                    g.LineTo(new Point(tip.X, tip.Y - sy * arm), true, true);
                }
                figure.Freeze();
                dc.DrawGeometry(null, corner, figure);
            }
        // the lens ring and the point of focus
        dc.DrawEllipse(null, InkPen(tint, InkLine * 0.85, 1.1), new Point(cx, mid), ring, ring);
        var point = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * InkLine * 1.2), tint.R, tint.G, tint.B));
        point.Freeze();
        dc.DrawEllipse(point, null, new Point(cx, mid), 1.6, 1.6);
        dc.Pop();
    }

    /// <summary>The watercolour as a soft picture (cached per size and colours).</summary>
    static BitmapSource Wash(int width, int height, Color tint, Color surface)
    {
        washes ??= new Dictionary<(int, int, Color, Color), BitmapSource>();
        if (washes.TryGetValue((width, height, tint, surface), out var cached)) return cached;
        if (washes.Count > 32) washes.Clear();
        var bitmap = RenderWash(width, height, tint, surface);
        washes[(width, height, tint, surface)] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// The wash (Hams' _wash, the shape the viewfinder's): a band around the line that swells inside the frame, softened,
    /// with paper fibres in it, plus a faint glow that reaches further, like paint that ran into wet paper. Deterministic.
    /// </summary>
    internal static BitmapSource RenderWash(int width, int height, Color tint, Color surface)
    {
        int w = Math.Max(1, width * WashScale), h = Math.Max(1, height * WashScale);
        var body = new double[w * h];
        var glow = new double[w * h];
        var (fh, fw) = Frame(width, height);
        double cx = Centre * width, mid = height / 2.0;
        for (int y = 0; y < h; y++)
        {
            double dy = Math.Abs((y + 0.5) / WashScale - mid);
            for (int x = 0; x < w; x++)
            {
                double lx = (x + 0.5) / WashScale, u = lx / width, near = Near(u);
                double loud = 0.10 + 0.90 * near;
                double reach = 0.62 * loud * (height / 2.0);
                // inside the frame the wash fills a soft rectangle rather than a lens
                double inFrame = Math.Abs(lx - cx) < fw * 0.96 ? 1 : 0;
                double extent = Math.Max(reach, inFrame * fh * 0.72);
                body[y * w + x] = dy <= extent ? 1 : 0;
                double spread = Math.Max(0.15 + 0.85 * loud, 1e-3) * (height / 2.0);
                double g = 1 - Math.Pow(dy / spread, 2);
                glow[y * w + x] = g > 0 ? g : 0;
            }
        }
        Blur(body, w, h, (int)Math.Round(1.6 * WashScale));
        Blur(glow, w, h, (int)Math.Round(height * 0.06 * WashScale));
        var fibres = Fibres(w, h);
        double r = tint.R * 0.6 + surface.R * 0.4, gr = tint.G * 0.6 + surface.G * 0.4, b = tint.B * 0.6 + surface.B * 0.4;
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                double fade = Fade((x + 0.5) / w);
                double alpha = Clamp((body[i] * (0.75 + 0.5 * fibres[i]) * WashStrength + glow[i] * 0.07) * fade);
                pixels[i * 4] = (byte)Math.Round(b * alpha);
                pixels[i * 4 + 1] = (byte)Math.Round(gr * alpha);
                pixels[i * 4 + 2] = (byte)Math.Round(r * alpha);
                pixels[i * 4 + 3] = (byte)Math.Round(255 * alpha);
            }
        var bitmap = BitmapSource.Create(w, h, 96 * WashScale, 96 * WashScale, PixelFormats.Pbgra32, null, pixels, w * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Paper fibres: a coarse random grid (fixed seed), smoothly interpolated, 0..1.</summary>
    static double[] Fibres(int w, int h)
    {
        int rows = Math.Max(2, h / (6 * WashScale)), cols = Math.Max(2, w / (5 * WashScale));   // Hams' cell size in logical pixels
        var random = new Random(9);
        var cells = new double[rows * cols];
        for (int i = 0; i < cells.Length; i++) cells[i] = random.NextDouble();
        var result = new double[w * h];
        for (int y = 0; y < h; y++)
        {
            double fy = (double)y / Math.Max(1, h - 1) * (rows - 1);
            int y0 = (int)fy, y1 = Math.Min(rows - 1, y0 + 1);
            double ty = Smooth(fy - y0);
            for (int x = 0; x < w; x++)
            {
                double fx = (double)x / Math.Max(1, w - 1) * (cols - 1);
                int x0 = (int)fx, x1 = Math.Min(cols - 1, x0 + 1);
                double tx = Smooth(fx - x0);
                double top = cells[y0 * cols + x0] * (1 - tx) + cells[y0 * cols + x1] * tx;
                double bottom = cells[y1 * cols + x0] * (1 - tx) + cells[y1 * cols + x1] * tx;
                result[y * w + x] = top * (1 - ty) + bottom * ty;
            }
        }
        return result;
    }

    static double Smooth(double t) => t * t * (3 - 2 * t);

    /// <summary>Three box blurs: close to a Gaussian of the given radius.</summary>
    static void Blur(double[] data, int w, int h, int radius)
    {
        if (radius < 1) return;
        var buffer = new double[data.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxPass(data, buffer, w, h, radius, horizontal: true);
            BoxPass(buffer, data, w, h, radius, horizontal: false);
        }
    }

    static void BoxPass(double[] source, double[] target, int w, int h, int radius, bool horizontal)
    {
        int lines = horizontal ? h : w, length = horizontal ? w : h;
        for (int line = 0; line < lines; line++)
        {
            double sum = 0;
            int count = 0;
            int At(int k) => horizontal ? line * w + k : k * w + line;
            for (int k = 0; k <= Math.Min(radius, length - 1); k++) { sum += source[At(k)]; count++; }
            for (int k = 0; k < length; k++)
            {
                target[At(k)] = sum / count;
                int add = k + radius + 1, remove = k - radius;
                if (add < length) { sum += source[At(add)]; count++; }
                if (remove >= 0) { sum -= source[At(remove)]; count--; }
            }
        }
    }
}
