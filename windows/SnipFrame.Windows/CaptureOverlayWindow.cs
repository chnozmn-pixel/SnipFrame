using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnipFrame;

public enum CaptureMode
{
    Region,
    Window
}

/// <summary>Frozen-screen overlay that lets the user pick a region or a window.</summary>
public sealed class CaptureOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly CaptureMode _mode;
    private readonly MonitorInfo _monitor;
    private readonly BitmapSource _screenshot;

    private Point? _start;
    private Point? _current;
    private Rect? _hoverBounds;
    private bool _finished;

    /// <summary>Raised with the cropped bitmap, in image pixels.</summary>
    public event Action<BitmapSource>? Captured;

    /// <summary>Raised when the user pressed Esc or closed the overlay without selecting.</summary>
    public event Action? Cancelled;

    public CaptureOverlayWindow(CaptureMode mode, MonitorInfo monitor)
    {
        _mode = mode;
        _monitor = monitor;
        _screenshot = CaptureService.Capture(monitor.Bounds) ?? throw new InvalidOperationException("capture failed");

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        UseLayoutRounding = true;
        Left = 0;
        Top = 0;
        Width = 1;
        Height = 1;
    }

    private double DipPerPixel => _screenshot.PixelWidth > 0 ? ActualWidth / _screenshot.PixelWidth : 1;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;

        // Per-monitor DPI aware: position and size the window in physical pixels.
        SetWindowPos(handle, HwndTopmost,
            (int)_monitor.Bounds.X, (int)_monitor.Bounds.Y,
            (int)_monitor.Bounds.Width, (int)_monitor.Bounds.Height,
            SwpShowWindow);

        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | 0x00000080 /* WS_EX_TOOLWINDOW */));
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Focus();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        dc.DrawImage(_screenshot, new Rect(0, 0, width, height));

        var dim = new SolidColorBrush(Color.FromArgb(122, 0, 0, 0));
        dim.Freeze();

        var selection = SelectionDip();
        if (selection is { Width: > 1, Height: > 1 })
        {
            var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
            mask.Children.Add(new RectangleGeometry(new Rect(0, 0, width, height)));
            mask.Children.Add(new RectangleGeometry(selection.Value));
            dc.DrawGeometry(dim, null, mask);

            var border = new Pen(new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), 1);
            border.Freeze();
            dc.DrawRectangle(null, border, selection.Value);
            DrawSizeBadge(dc, selection.Value);
        }
        else if (_mode == CaptureMode.Window && _hoverBounds is { Width: > 1 } hover)
        {
            var dip = PhysicalToDip(hover);
            var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
            mask.Children.Add(new RectangleGeometry(new Rect(0, 0, width, height)));
            mask.Children.Add(new RectangleGeometry(dip));
            dc.DrawGeometry(dim, null, mask);

            var pen = new Pen(new SolidColorBrush(Color.FromArgb(255, 120, 190, 255)), 2);
            pen.Freeze();
            dc.DrawRectangle(null, pen, dip);
            DrawSizeBadge(dc, dip);
        }
        else
        {
            dc.DrawRectangle(dim, null, new Rect(0, 0, width, height));
        }

        DrawHint(dc, width);
    }

    private void DrawHint(DrawingContext dc, double width)
    {
        var text = _mode == CaptureMode.Region
            ? L.T("capture.regionHint", "Drag to select an area  ·  Esc to cancel")
            : L.T("capture.windowHint", "Click a window to capture it  ·  Esc to cancel");

        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var padding = 14.0;
        var box = new Rect(
            (width - formatted.Width) / 2 - padding,
            24,
            formatted.Width + padding * 2,
            formatted.Height + padding * 0.9);

        var background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 24));
        background.Freeze();
        dc.DrawRoundedRectangle(background, null, box, 8, 8);
        dc.DrawText(formatted, new Point(box.X + padding, box.Y + padding * 0.45));
    }

    private void DrawSizeBadge(DrawingContext dc, Rect dip)
    {
        if (!_start.HasValue) return;
        var physical = DipToPhysical(dip);
        var label = $"{Math.Round(physical.Width)} × {Math.Round(physical.Height)}";

        var formatted = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var box = new Rect(dip.X, Math.Max(0, dip.Y - formatted.Height - 12), formatted.Width + 14, formatted.Height + 8);
        var background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 24));
        background.Freeze();
        dc.DrawRoundedRectangle(background, null, box, 5, 5);
        dc.DrawText(formatted, new Point(box.X + 7, box.Y + 3));
    }

    // ---------- input ----------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_mode == CaptureMode.Window)
        {
            CommitWindow();
            return;
        }

        _start = e.GetPosition(this);
        _current = _start;
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);

        if (_mode == CaptureMode.Window)
        {
            var hwnd = CaptureService.WindowAt(DipToPhysical(position));
            if (CaptureService.TryGetWindowBounds(hwnd, out var bounds))
            {
                _hoverBounds = bounds;
                InvalidateVisual();
            }

            return;
        }

        if (_start.HasValue)
        {
            _current = position;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_mode != CaptureMode.Region || !_start.HasValue) return;

        ReleaseMouseCapture();
        var selection = SelectionDip();
        _start = null;
        _current = null;

        if (selection is not { Width: > 3, Height: > 3 })
        {
            InvalidateVisual();
            return;
        }

        CommitRegion(selection.Value);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Finish(null);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_finished) Cancelled?.Invoke();
    }

    private void CommitRegion(Rect dip)
    {
        var physical = DipToPhysical(dip);
        var local = new Int32Rect(
            (int)Math.Round(physical.X - _monitor.Bounds.X),
            (int)Math.Round(physical.Y - _monitor.Bounds.Y),
            (int)Math.Round(physical.Width),
            (int)Math.Round(physical.Height));

        var image = CaptureService.Crop(_screenshot, local);
        Finish(image);
    }

    private void CommitWindow()
    {
        if (_hoverBounds is not { Width: > 1 } bounds)
        {
            Finish(null);
            return;
        }

        var local = new Int32Rect(
            (int)Math.Round(bounds.X - _monitor.Bounds.X),
            (int)Math.Round(bounds.Y - _monitor.Bounds.Y),
            (int)Math.Round(bounds.Width),
            (int)Math.Round(bounds.Height));

        var image = CaptureService.Crop(_screenshot, local);
        Finish(image);
    }

    private void Finish(BitmapSource? image)
    {
        if (_finished) return;
        _finished = true;

        if (image is not null) Captured?.Invoke(image);
        else Cancelled?.Invoke();

        Close();
    }

    // ---------- geometry ----------

    private Rect? SelectionDip()
    {
        if (_start is not { } start || _current is not { } current) return null;
        return AnnotationRenderer.RectFrom(start, current);
    }

    private Point DipToPhysical(Point dip)
    {
        var scale = DipPerPixel;
        return new Point(_monitor.Bounds.X + dip.X / scale, _monitor.Bounds.Y + dip.Y / scale);
    }

    private Rect DipToPhysical(Rect dip)
    {
        var topLeft = DipToPhysical(dip.TopLeft);
        var bottomRight = DipToPhysical(dip.BottomRight);
        return new Rect(topLeft, bottomRight);
    }

    private Rect PhysicalToDip(Rect physical)
    {
        var scale = DipPerPixel;
        return new Rect(
            (physical.X - _monitor.Bounds.X) * scale,
            (physical.Y - _monitor.Bounds.Y) * scale,
            physical.Width * scale,
            physical.Height * scale);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
