using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace SnipFrame;

/// <summary>Physical pixel bounds of a monitor plus its DPI scale (1.0 = 96 dpi).</summary>
public sealed record MonitorInfo(Rect Bounds, double Scale)
{
    public Point Origin => new(Bounds.X, Bounds.Y);
}

public static class CaptureService
{
    // ---------- monitors ----------

    public static MonitorInfo GetMonitorAtCursor()
    {
        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor);
        var scale = 1.0;
        var monitor = MonitorFromPoint(new POINT { X = cursor.X, Y = cursor.Y }, 2 /* MONITOR_DEFAULTTONEAREST */);
        if (GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dpiX, out _) == 0 && dpiX > 0)
        {
            scale = dpiX / 96.0;
        }

        var bounds = screen.Bounds;
        return new MonitorInfo(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), scale);
    }

    public static MonitorInfo GetPrimaryMonitor()
    {
        var screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        var bounds = screen.Bounds;
        var scale = 1.0;
        var monitor = MonitorFromPoint(new POINT { X = bounds.X + bounds.Width / 2, Y = bounds.Y + bounds.Height / 2 }, 2);
        if (GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0) scale = dpiX / 96.0;
        return new MonitorInfo(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), scale);
    }

    // ---------- capture ----------

    /// <summary>Grabs a physical pixel rectangle from the screen.</summary>
    public static BitmapSource? Capture(Rect physical)
    {
        var rect = new Rectangle(
            (int)Math.Round(physical.X),
            (int)Math.Round(physical.Y),
            Math.Max(1, (int)Math.Round(physical.Width)),
            Math.Max(1, (int)Math.Round(physical.Height)));

        try
        {
            using var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(rect.Width, rect.Height), CopyPixelOperation.SourceCopy);
            }

            var handle = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(handle);
            }
        }
        catch
        {
            return null;
        }
    }

    public static BitmapSource? Crop(BitmapSource source, Int32Rect region)
    {
        var x = Math.Clamp(region.X, 0, source.PixelWidth - 1);
        var y = Math.Clamp(region.Y, 0, source.PixelHeight - 1);
        var width = Math.Clamp(region.Width, 1, source.PixelWidth - x);
        var height = Math.Clamp(region.Height, 1, source.PixelHeight - y);
        try
        {
            var cropped = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            return null;
        }
    }

    // ---------- windows ----------

    /// <summary>The topmost visible window of another process under a physical screen point.</summary>
    public static IntPtr WindowAt(Point screenPoint)
    {
        var found = IntPtr.Zero;
        var ownPid = Environment.ProcessId;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            if (IsCloaked(hwnd)) return true;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == ownPid) return true;

            var style = GetWindowLongPtr(hwnd, -20 /* GWL_EXSTYLE */).ToInt64();
            if ((style & 0x00000080 /* WS_EX_TOOLWINDOW */) != 0) return true;

            if (!TryGetWindowBounds(hwnd, out var bounds)) return true;
            if (bounds.Width < 40 || bounds.Height < 40) return true;
            if (!bounds.Contains(screenPoint)) return true;

            found = hwnd;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    public static bool TryGetWindowBounds(IntPtr hwnd, out Rect bounds)
    {
        bounds = Rect.Empty;
        if (hwnd == IntPtr.Zero) return false;

        var frame = new RECT();
        if (DwmGetWindowAttribute(hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, ref frame, Marshal.SizeOf<RECT>()) != 0)
        {
            if (!GetWindowRect(hwnd, out frame)) return false;
        }

        bounds = new Rect(frame.Left, frame.Top, Math.Max(0, frame.Right - frame.Left), Math.Max(0, frame.Bottom - frame.Top));
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out var cloaked, sizeof(int)) != 0) return false;
        return cloaked != 0;
    }

    // ---------- interop ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, ref RECT value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
