using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnipFrame;

/// <summary>Draws the captured image plus annotations and turns mouse input into annotations.</summary>
public sealed class AnnotationCanvas : FrameworkElement
{
    private const int HistoryLimit = 100;

    private readonly List<Annotation> _annotations = new();
    private readonly List<List<Annotation>> _undo = new();
    private readonly List<List<Annotation>> _redo = new();

    private Annotation? _current;
    private Point _dragStart;
    private bool _erasing;
    private bool _erased;
    private List<Annotation>? _eraseSnapshot;

    public AnnotationCanvas()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public BitmapSource? BaseImage { get; set; }
    public EditorTool Tool { get; set; } = EditorTool.Arrow;
    public Color StrokeColor { get; set; } = Color.FromRgb(0x1C, 0x1C, 0x21);
    public double LineWidth { get; set; } = 3;

    public IReadOnlyList<Annotation> Annotations => _annotations;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public event Action? Changed;
    public event Action<Point>? TextRequested;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (BaseImage is null) return new Size(0, 0);
        return new Size(BaseImage.PixelWidth, BaseImage.PixelHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (BaseImage is null) return finalSize;
        return new Size(BaseImage.PixelWidth, BaseImage.PixelHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (BaseImage is null) return;

        var target = new Rect(0, 0, BaseImage.PixelWidth, BaseImage.PixelHeight);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        dc.DrawImage(BaseImage, target);

        AnnotationRenderer.Draw(dc, _annotations, BaseImage);

        if (_current is not null)
        {
            AnnotationRenderer.Draw(dc, new[] { _current }, BaseImage);
        }
    }

    // ---------- mouse ----------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        var point = e.GetPosition(this);
        if (BaseImage is null) return;

        if (Tool == EditorTool.Text)
        {
            TextRequested?.Invoke(point);
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.Eraser)
        {
            _erasing = true;
            _erased = false;
            _eraseSnapshot = Annotation.CloneAll(_annotations);
            CaptureMouse();
            EraseAt(point);
            return;
        }

        _dragStart = point;
        _current = new Annotation
        {
            Tool = Tool,
            Points = { point, point },
            Color = StrokeColor,
            Width = LineWidth
        };

        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (BaseImage is null) return;

        var point = e.GetPosition(this);

        if (_erasing)
        {
            EraseAt(point);
            return;
        }

        if (_current is null || e.LeftButton != MouseButtonState.Pressed) return;

        if (_current.Tool is EditorTool.Pen or EditorTool.Highlighter)
        {
            _current.Points.Add(point);
        }
        else
        {
            _current.Points[1] = point;
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();

        if (_erasing)
        {
            _erasing = false;
            if (_erased && _eraseSnapshot is not null)
            {
                PushSnapshot(_eraseSnapshot);
                Changed?.Invoke();
            }

            _eraseSnapshot = null;
            _erased = false;
            InvalidateVisual();
            return;
        }

        var annotation = _current;
        _current = null;
        if (annotation is null) return;

        var isStroke = annotation.Tool is EditorTool.Pen or EditorTool.Highlighter;
        var first = annotation.Points[0];
        var last = annotation.Points[^1];
        var farEnough = Math.Abs(first.X - last.X) > 2 || Math.Abs(first.Y - last.Y) > 2;

        if (isStroke ? annotation.Points.Count > 1 : farEnough)
        {
            PushHistory();
            _annotations.Add(annotation);
            Changed?.Invoke();
        }

        InvalidateVisual();
    }

    private void EraseAt(Point point)
    {
        var updated = AnnotationRenderer.Erase(_annotations, point, Math.Max(8, LineWidth * 2.5), out var changed);
        if (!changed || updated is null) return;

        _erased = true;
        _annotations.Clear();
        _annotations.AddRange(updated);
        InvalidateVisual();
    }

    // ---------- text ----------

    public void CommitText(string text, Point origin, Color color, double fontSize)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return;

        PushHistory();
        _annotations.Add(new Annotation
        {
            Tool = EditorTool.Text,
            Points = { origin },
            Color = color,
            Width = fontSize,
            Text = trimmed
        });

        Changed?.Invoke();
        InvalidateVisual();
    }

    // ---------- history ----------

    private void PushHistory() => PushSnapshot(_annotations);

    private void PushSnapshot(IReadOnlyList<Annotation> snapshot)
    {
        _undo.Add(Annotation.CloneAll(snapshot));
        if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
        _redo.Clear();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add(Annotation.CloneAll(_annotations));
        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);

        _annotations.Clear();
        _annotations.AddRange(previous);
        Changed?.Invoke();
        InvalidateVisual();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(Annotation.CloneAll(_annotations));
        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);

        _annotations.Clear();
        _annotations.AddRange(next);
        Changed?.Invoke();
        InvalidateVisual();
    }

    // ---------- export ----------

    /// <summary>Flattens the capture and the annotations into a bitmap at native pixel size.</summary>
    public BitmapSource? RenderBitmap()
    {
        if (BaseImage is null) return null;

        var width = BaseImage.PixelWidth;
        var height = BaseImage.PixelHeight;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(BaseImage, new Rect(0, 0, width, height));
            AnnotationRenderer.Draw(dc, _annotations, BaseImage);
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
