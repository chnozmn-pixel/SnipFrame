using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SnipFrame;

public enum CaptureMode
{
    Region,
    Window
}

/// <summary>
/// Frozen-desktop overlay. One borderless window spans the whole virtual screen, so a selection
/// can start on one monitor and end on another, and the snapshot is painted by a child visual
/// (drawing on Window itself would sit behind the window chrome and look black).
/// </summary>
public sealed class CaptureOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int SwpNoActivate = 0x0010;
    private const int WsExToolWindow = 0x00000080;
    private const int WmDpiChanged = 0x02E0;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int VkEscape = 0x1B;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly CaptureMode _mode;
    private readonly Rect _desktop;
    private readonly BitmapSource _screenshot;
    private readonly OverlaySurface _surface = new();
    private readonly DispatcherTimer _placementTimer;

    private HookProc? _escapeHookProc;
    private IntPtr _escapeHook;

    private Point? _start;
    private Point? _current;
    private Rect? _hoverPhysical;
    private int _placementTicks;
    private bool _finished;

    /// <summary>Raised with the cropped bitmap, in image pixels.</summary>
    public event Action<BitmapSource>? Captured;

    /// <summary>Raised when the user pressed Esc or closed the overlay without selecting.</summary>
    public event Action? Cancelled;

    public CaptureOverlayWindow(CaptureMode mode, Rect desktop, BitmapSource screenshot, double dpiScale)
    {
        _mode = mode;
        _desktop = desktop;
        _screenshot = screenshot;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        Focusable = true;

        _surface.Mode = mode;
        _surface.Image = screenshot;
        Content = _surface;

        // Windows resizes a window when the monitor DPI changes and WPF applies that suggestion,
        // which can shrink the overlay back onto a single monitor. Re-assert the rectangle for a
        // moment after showing so the overlay always ends up covering the whole virtual screen.
        _placementTimer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _placementTimer.Tick += (_, _) =>
        {
            EnsurePlacement();
            if (--_placementTicks <= 0) _placementTimer.Stop();
        };

        // Initial DIP placement; the exact physical rectangle is applied once the handle exists.
        var scale = dpiScale <= 0 ? 1.0 : dpiScale;
        Left = desktop.X / scale;
        Top = desktop.Y / scale;
        Width = Math.Max(1, desktop.Width / scale);
        Height = Math.Max(1, desktop.Height / scale);
    }

    /// <summary>Image pixels per device-independent pixel of the overlay.</summary>
    private double PixelsPerDip => _screenshot.PixelWidth / Math.Max(1.0, _surface.ActualWidth);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExToolWindow));

        if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(WndProc);

        InstallEscapeHook();
        PlaceWindow();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        PlaceWindow();
        WatchPlacement();
        Activate();
        Focus();
        Keyboard.Focus(this);
        _surface.InvalidateVisual();
    }

    /// <summary>
    /// Esc has to cancel even when the overlay never got keyboard focus (the capture can be started
    /// from the tray or a global hotkey), so a low-level hook watches for it while the overlay is up.
    /// </summary>
    private void InstallEscapeHook()
    {
        if (_escapeHook != IntPtr.Zero) return;

        _escapeHookProc = OnEscapeHook;
        _escapeHook = SetWindowsHookEx(WhKeyboardLl, _escapeHookProc, GetModuleHandle(null), 0);
    }

    private void RemoveEscapeHook()
    {
        if (_escapeHook == IntPtr.Zero) return;

        UnhookWindowsHookEx(_escapeHook);
        _escapeHook = IntPtr.Zero;
        _escapeHookProc = null;
    }

    private IntPtr OnEscapeHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 &&
            (wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown) &&
            Marshal.ReadInt32(lParam) == VkEscape)
        {
            Dispatcher.BeginInvoke(new Action(() => Finish(null)));
            return new IntPtr(1);
        }

        return CallNextHookEx(_escapeHook, code, wParam, lParam);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmDpiChanged)
        {
            // Let WPF finish its own handling first, then put the coverage back.
            Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                PlaceWindow();
                WatchPlacement();
            }));
        }

        return IntPtr.Zero;
    }

    private void WatchPlacement()
    {
        _placementTicks = 40;
        _placementTimer.Start();
    }

    /// <summary>Restores the virtual-screen rectangle if something (WPF, DPI change) moved or resized us.</summary>
    private void EnsurePlacement()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (!GetWindowRect(handle, out var current)) return;

        var matches =
            Math.Abs(current.Left - _desktop.X) <= 1 &&
            Math.Abs(current.Top - _desktop.Y) <= 1 &&
            Math.Abs(current.Right - (_desktop.X + _desktop.Width)) <= 1 &&
            Math.Abs(current.Bottom - (_desktop.Y + _desktop.Height)) <= 1;

        if (!matches) PlaceWindow();
    }

    /// <summary>WPF applies its own placement while showing; re-assert the physical rectangle afterwards.</summary>
    private void PlaceWindow()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        SetWindowPos(handle, HwndTopmost,
            (int)Math.Round(_desktop.X), (int)Math.Round(_desktop.Y),
            (int)Math.Round(_desktop.Width), (int)Math.Round(_desktop.Height),
            SwpNoActivate);

        _surface.InvalidateVisual();
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

        _start = PointerDip();
        _current = _start;
        CaptureMouse();
        Focus();
        Keyboard.Focus(this);
        SyncSurface();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = PointerDip();

        if (_mode == CaptureMode.Window)
        {
            var hwnd = CaptureService.WindowAt(ToPhysical(position));
            if (CaptureService.TryGetWindowBounds(hwnd, out var bounds))
            {
                if (_hoverPhysical != bounds)
                {
                    _hoverPhysical = bounds;
                    SyncSurface();
                }
            }
            else if (_hoverPhysical is not null)
            {
                _hoverPhysical = null;
                SyncSurface();
            }

            return;
        }

        if (_start.HasValue)
        {
            _current = position;
            SyncSurface();
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
        SyncSurface();

        if (selection is not { Width: > 3, Height: > 3 })
        {
            return;
        }

        CommitRegion(selection.Value);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Finish(null);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        RemoveEscapeHook();
        if (!_finished) Cancelled?.Invoke();
    }

    private void SyncSurface()
    {
        _surface.Selection = SelectionDip();
        _surface.Hover = _hoverPhysical is { } physical ? ToDip(physical) : null;
        _surface.ShowBadge = _start.HasValue || _hoverPhysical is not null;
        _surface.InvalidateVisual();
    }

    // ---------- commit ----------

    private void CommitRegion(Rect dip)
    {
        var image = CaptureService.Crop(_screenshot, ToImageRect(dip));
        Finish(image);
    }

    private void CommitWindow()
    {
        if (_hoverPhysical is not { Width: > 1 } bounds)
        {
            Finish(null);
            return;
        }

        var image = CaptureService.Crop(_screenshot, ToImageRect(ToDip(bounds)));
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

    /// <summary>
    /// Surface DIPs per physical pixel. The frozen snapshot covers the whole window, so this ratio
    /// maps overlay space onto screen pixels no matter which monitor the window DPI came from.
    /// </summary>
    private double DipScale => _surface.ActualWidth <= 0
        ? 1.0
        : _surface.ActualWidth / Math.Max(1, _screenshot.PixelWidth);

    /// <summary>
    /// Pointer position in surface DIPs, read from physical screen coordinates. WPF's own position
    /// is expressed in the window DPI, which jumps when a drag crosses into a differently scaled
    /// monitor; physical coordinates keep the selection continuous across both screens.
    /// </summary>
    private Point PointerDip()
    {
        var physical = CaptureService.CursorPosition();
        var scale = DipScale;
        return new Point((physical.X - _desktop.X) * scale, (physical.Y - _desktop.Y) * scale);
    }

    /// <summary>Overlay DIPs → bitmap pixels (origin of the snapshot is the virtual-screen origin).</summary>
    private Int32Rect ToImageRect(Rect dip)
    {
        var scale = PixelsPerDip;
        return new Int32Rect(
            (int)Math.Round(dip.X * scale),
            (int)Math.Round(dip.Y * scale),
            Math.Max(1, (int)Math.Round(dip.Width * scale)),
            Math.Max(1, (int)Math.Round(dip.Height * scale)));
    }

    private Point ToPhysical(Point dip)
    {
        var scale = PixelsPerDip;
        return new Point(_desktop.X + dip.X * scale, _desktop.Y + dip.Y * scale);
    }

    private Rect ToDip(Rect physical)
    {
        var scale = PixelsPerDip;
        return new Rect(
            (physical.X - _desktop.X) / scale,
            (physical.Y - _desktop.Y) / scale,
            physical.Width / scale,
            physical.Height / scale);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    /// <summary>Paints the frozen desktop, the dim mask, the selection and the hints.</summary>
    private sealed class OverlaySurface : FrameworkElement
    {
        public BitmapSource? Image;
        public CaptureMode Mode;
        public Rect? Selection;
        public Rect? Hover;
        public bool ShowBadge;

        protected override void OnRender(DrawingContext dc)
        {
            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 0 || height <= 0) return;

            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));

            if (Image is not null)
            {
                RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
                dc.DrawImage(Image, new Rect(0, 0, width, height));
            }

            var dim = new SolidColorBrush(Color.FromArgb(122, 0, 0, 0));
            dim.Freeze();

            var active = Selection ?? Hover;
            if (active is { Width: > 2, Height: > 2 } rect)
            {
                var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
                mask.Children.Add(new RectangleGeometry(new Rect(0, 0, width, height)));
                mask.Children.Add(new RectangleGeometry(rect));
                dc.DrawGeometry(dim, null, mask);

                var pen = new Pen(new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)), 1);
                pen.Freeze();
                dc.DrawRectangle(null, pen, rect);

                if (Selection.HasValue)
                {
                    DrawHandles(dc, rect);
                    if (ShowBadge) DrawBadge(dc, rect);
                }
            }
            else
            {
                dc.DrawRectangle(dim, null, new Rect(0, 0, width, height));
            }

            DrawHint(dc, width);
        }

        private static void DrawHandles(DrawingContext dc, Rect rect)
        {
            var brush = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255));
            brush.Freeze();

            const double size = 6;
            foreach (var corner in new[]
                     {
                         rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight
                     })
            {
                dc.DrawRectangle(brush, null,
                    new Rect(corner.X - size / 2, corner.Y - size / 2, size, size));
            }
        }

        private void DrawBadge(DrawingContext dc, Rect rect)
        {
            var label = $"{Math.Round(rect.Width * ScaleX)} × {Math.Round(rect.Height * ScaleY)}";
            var text = Text(label, 12, FontWeights.Medium, Brushes.White);

            var top = rect.Y - text.Height - 16;
            if (top < 6) top = rect.Y + 8;

            var box = new Rect(rect.X, top, text.Width + 14, text.Height + 8);
            var background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 24));
            background.Freeze();

            dc.DrawRoundedRectangle(background, null, box, 5, 5);
            dc.DrawText(text, new Point(box.X + 7, box.Y + 4));
        }

        private void DrawHint(DrawingContext dc, double width)
        {
            var text = Mode == CaptureMode.Region
                ? L.T("capture.regionHint", "Drag to select an area  ·  Esc to cancel")
                : L.T("capture.windowHint", "Click a window to capture it  ·  Esc to cancel");

            var formatted = Text(text, 13, FontWeights.SemiBold, Brushes.White);
            const double padding = 14;

            var box = new Rect(
                (width - formatted.Width) / 2 - padding,
                24,
                formatted.Width + padding * 2,
                formatted.Height + padding * 0.85);

            var background = new SolidColorBrush(Color.FromArgb(225, 20, 20, 24));
            background.Freeze();

            dc.DrawRoundedRectangle(background, null, box, 8, 8);
            dc.DrawText(formatted, new Point(box.X + padding, box.Y + padding * 0.42));
        }

        private FormattedText Text(string value, double size, FontWeight weight, Brush brush) =>
            new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
                size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        private double ScaleX => Image is null ? 1 : Image.PixelWidth / Math.Max(1.0, ActualWidth);
        private double ScaleY => Image is null ? 1 : Image.PixelHeight / Math.Max(1.0, ActualHeight);
    }
}
