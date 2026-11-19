using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SnipFrame;

/// <summary>
/// Vector replacements for the SF Symbols the macOS build uses. Every glyph is drawn in a
/// 20 × 20 box and stroked with the foreground of the surrounding control.
/// </summary>
public static class Icons
{
    public const string Region =
        "M3 7V4.8A1.8 1.8 0 0 1 4.8 3H7 M13 3h2.2A1.8 1.8 0 0 1 17 4.8V7 " +
        "M17 13v2.2a1.8 1.8 0 0 1-1.8 1.8H13 M7 17H4.8A1.8 1.8 0 0 1 3 15.2V13";

    public const string Window =
        "M5 4.5h10A1.5 1.5 0 0 1 16.5 6v8A1.5 1.5 0 0 1 15 15.5H5A1.5 1.5 0 0 1 3.5 14V6A1.5 1.5 0 0 1 5 4.5z " +
        "M3.5 8h13";

    public const string Display =
        "M3 4h14v9.5H3z M7.5 16.5h5 M10 13.5v3";

    public const string Pen =
        "M4 16l.7-3.3 8.7-8.7a1.7 1.7 0 0 1 2.4 2.4l-8.7 8.7L4 16z M11.8 6.2l2.1 2.1";

    public const string Highlighter =
        "M4.5 16.5h5.5 M8.7 14.7l-2.6-2.6 6.2-6.2 2.6 2.6-6.2 6.2z";

    public const string Arrow =
        "M5 15L14.8 5.2 M8.8 5h6.2v6.2";

    public const string Rectangle =
        "M4 5.5h12v9H4z";

    public const string Ellipse =
        "M4 10a6 4.6 0 1 0 12 0a6 4.6 0 1 0 -12 0z";

    public const string Mosaic =
        "M4 4h4.2v4.2H4z M11.8 4H16v4.2h-4.2z M4 11.8h4.2V16H4z M11.8 11.8H16V16h-4.2z";

    public const string Text =
        "M4.2 5h11.6 M10 5v10.5 M7 15.5h6";

    public const string Eraser =
        "M4 16.5h7 M8.6 15.2L5.4 12l6.2-6.2 3.2 3.2-5.2 5.2z M9.4 8.2l3.2 3.2";

    public const string Undo =
        "M7.6 5.4L4 9l3.6 3.6 M4 9h7.4a4.4 4.4 0 0 1 0 8.8H8.6";

    public const string Redo =
        "M12.4 5.4L16 9l-3.6 3.6 M16 9H8.6a4.4 4.4 0 0 0 0 8.8h2.8";

    public static string For(EditorTool tool) => tool switch
    {
        EditorTool.Pen => Pen,
        EditorTool.Highlighter => Highlighter,
        EditorTool.Arrow => Arrow,
        EditorTool.Rectangle => Rectangle,
        EditorTool.Ellipse => Ellipse,
        EditorTool.Mosaic => Mosaic,
        EditorTool.Text => Text,
        _ => Eraser
    };

    /// <summary>Builds a path whose stroke follows the closest control's foreground.</summary>
    public static Path Create(string data, double size = 17, double thickness = 1.5)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        path.SetBinding(Shape.StrokeProperty, new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1)
        });

        return path;
    }
}
