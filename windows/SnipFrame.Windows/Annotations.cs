using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnipFrame;

public enum EditorTool
{
    Pen,
    Highlighter,
    Arrow,
    Rectangle,
    Ellipse,
    Mosaic,
    Text,
    Eraser
}

/// <summary>One drawing on top of the captured image. Coordinates are image pixels.</summary>
public sealed class Annotation
{
    public EditorTool Tool { get; init; }
    public List<Point> Points { get; init; } = new();
    public Color Color { get; init; } = Colors.Black;
    public double Width { get; init; } = 3;
    public string? Text { get; init; }

    public Rect Bounds
    {
        get
        {
            if (Points.Count == 0) return Rect.Empty;
            var rect = new Rect(Points[0], new Size(0, 0));
            foreach (var point in Points) rect.Union(new Rect(point, new Size(0, 0)));
            return rect;
        }
    }

    public Annotation Clone() => new()
    {
        Tool = Tool,
        Points = new List<Point>(Points),
        Color = Color,
        Width = Width,
        Text = Text
    };

    public static List<Annotation> CloneAll(IEnumerable<Annotation> items) => items.Select(item => item.Clone()).ToList();
}

public static class AnnotationRenderer
{
    public static void Draw(DrawingContext dc, IReadOnlyList<Annotation> items, BitmapSource baseImage, double pixelsPerDip = 1.0)
    {
        foreach (var item in items)
        {
            switch (item.Tool)
            {
                case EditorTool.Pen:
                    DrawStroke(dc, item, item.Color, item.Width, PenLineCap.Round);
                    break;

                case EditorTool.Highlighter:
                    var highlight = Color.FromArgb(90, item.Color.R, item.Color.G, item.Color.B);
                    DrawStroke(dc, item, highlight, item.Width * 5, PenLineCap.Flat);
                    break;

                case EditorTool.Arrow:
                    DrawArrow(dc, item);
                    break;

                case EditorTool.Rectangle:
                case EditorTool.Ellipse:
                {
                    if (item.Points.Count < 2) break;
                    var rect = RectFrom(item.Points[0], item.Points[1]);
                    if (rect.Width < 1 || rect.Height < 1) break;
                    var pen = new Pen(new SolidColorBrush(item.Color), item.Width);
                    pen.Freeze();
                    if (item.Tool == EditorTool.Rectangle)
                        dc.DrawRectangle(null, pen, rect);
                    else
                        dc.DrawEllipse(null, pen, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2);
                    break;
                }

                case EditorTool.Mosaic:
                {
                    if (item.Points.Count < 2) break;
                    var rect = RectFrom(item.Points[0], item.Points[1]);
                    if (rect.Width < 2 || rect.Height < 2) break;
                    var mosaic = MosaicBitmap.Create(baseImage, rect, 12);
                    if (mosaic is not null) dc.DrawImage(mosaic, rect);
                    break;
                }

                case EditorTool.Text:
                {
                    if (string.IsNullOrEmpty(item.Text) || item.Points.Count == 0) break;
                    var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                    var formatted = new FormattedText(
                        item.Text,
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        Math.Max(10, item.Width),
                        new SolidColorBrush(item.Color),
                        pixelsPerDip);
                    dc.DrawText(formatted, item.Points[0]);
                    break;
                }
            }
        }
    }

    public static Rect RectFrom(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static void DrawStroke(DrawingContext dc, Annotation item, Color color, double width, PenLineCap cap)
    {
        if (item.Points.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(item.Points[0], false, false);
            context.PolyLineTo(item.Points.Skip(1).ToList(), true, false);
        }

        geometry.Freeze();
        var pen = new Pen(new SolidColorBrush(color), width)
        {
            StartLineCap = cap,
            EndLineCap = cap,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawArrow(DrawingContext dc, Annotation item)
    {
        if (item.Points.Count < 2) return;
        var start = item.Points[0];
        var end = item.Points[^1];
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 4) return;

        var brush = new SolidColorBrush(item.Color);
        var pen = new Pen(brush, item.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(pen, start, end);

        var angle = Math.Atan2(dy, dx);
        var headLength = Math.Max(10, item.Width * 4);
        var spread = Math.PI / 7;

        var left = new Point(
            end.X - headLength * Math.Cos(angle - spread),
            end.Y - headLength * Math.Sin(angle - spread));
        var right = new Point(
            end.X - headLength * Math.Cos(angle + spread),
            end.Y - headLength * Math.Sin(angle + spread));

        dc.DrawLine(pen, end, left);
        dc.DrawLine(pen, end, right);
    }

    /// <summary>Splits a stroke around the eraser point; shapes and text are removed entirely.</summary>
    public static List<Annotation>? Erase(IReadOnlyList<Annotation> items, Point center, double radius, out bool changed)
    {
        changed = false;
        var result = new List<Annotation>(items.Count);

        foreach (var item in items)
        {
            var isStroke = item.Tool is EditorTool.Pen or EditorTool.Highlighter;
            if (!isStroke)
            {
                var hit = item.Points.Count > 0 && Rect.Inflate(item.Bounds, radius, radius).Contains(center);
                if (hit)
                {
                    changed = true;
                    continue;
                }

                result.Add(item);
                continue;
            }

            if (!TouchesStroke(item, center, radius))
            {
                result.Add(item);
                continue;
            }

            changed = true;
            foreach (var run in RemainingRuns(item.Points, center, radius))
            {
                if (run.Count < 2) continue;
                result.Add(new Annotation
                {
                    Tool = item.Tool,
                    Points = run,
                    Color = item.Color,
                    Width = item.Width
                });
            }
        }

        return result;
    }

    private static bool TouchesStroke(Annotation item, Point center, double radius)
    {
        for (var i = 0; i < item.Points.Count - 1; i++)
        {
            if (DistanceToSegment(center, item.Points[i], item.Points[i + 1]) <= radius + item.Width / 2) return true;
        }

        return item.Points.Count == 1 && (item.Points[0] - center).Length <= radius;
    }

    private static List<List<Point>> RemainingRuns(IReadOnlyList<Point> points, Point center, double radius)
    {
        var runs = new List<List<Point>>();
        var current = new List<Point>();

        for (var i = 0; i < points.Count; i++)
        {
            var inside = (points[i] - center).Length <= radius;
            if (inside)
            {
                if (current.Count > 1) runs.Add(current);
                current = new List<Point>();
                continue;
            }

            current.Add(points[i]);
        }

        if (current.Count > 1) runs.Add(current);
        return runs;
    }

    private static double DistanceToSegment(Point point, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 0.0001) return (point - a).Length;

        var t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1);
        var projection = new Point(a.X + t * dx, a.Y + t * dy);
        return (point - projection).Length;
    }
}

/// <summary>Builds a blocky copy of an image region for redaction.</summary>
public static class MosaicBitmap
{
    public static BitmapSource? Create(BitmapSource source, Rect rect, int blockSize)
    {
        var x = (int)Math.Floor(rect.X);
        var y = (int)Math.Floor(rect.Y);
        var width = (int)Math.Ceiling(rect.Width);
        var height = (int)Math.Ceiling(rect.Height);

        x = Math.Clamp(x, 0, Math.Max(0, source.PixelWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, source.PixelHeight - 1));
        width = Math.Min(width, source.PixelWidth - x);
        height = Math.Min(height, source.PixelHeight - y);
        if (width < 2 || height < 2) return null;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(new Int32Rect(x, y, width, height), pixels, stride, 0);

        blockSize = Math.Max(2, blockSize);
        for (var by = 0; by < height; by += blockSize)
        {
            for (var bx = 0; bx < width; bx += blockSize)
            {
                var blockWidth = Math.Min(blockSize, width - bx);
                var blockHeight = Math.Min(blockSize, height - by);

                long b = 0, g = 0, r = 0, a = 0;
                var count = 0;
                for (var py = 0; py < blockHeight; py++)
                {
                    for (var px = 0; px < blockWidth; px++)
                    {
                        var index = (by + py) * stride + (bx + px) * 4;
                        b += pixels[index];
                        g += pixels[index + 1];
                        r += pixels[index + 2];
                        a += pixels[index + 3];
                        count++;
                    }
                }

                if (count == 0) continue;
                for (var py = 0; py < blockHeight; py++)
                {
                    for (var px = 0; px < blockWidth; px++)
                    {
                        var index = (by + py) * stride + (bx + px) * 4;
                        pixels[index] = (byte)(b / count);
                        pixels[index + 1] = (byte)(g / count);
                        pixels[index + 2] = (byte)(r / count);
                        pixels[index + 3] = (byte)(a / count);
                    }
                }
            }
        }

        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
