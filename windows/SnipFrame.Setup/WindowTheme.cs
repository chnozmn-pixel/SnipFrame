using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SnipFrame.Setup;

internal static class WindowTheme
{
    private const int ImmersiveDarkMode = 20;
    private const int ImmersiveDarkModeLegacy = 19;

    public static void Apply(Window window)
    {
        // The handle only exists after Show, so always wait for SourceInitialized.
        window.SourceInitialized += (_, _) => UseDarkTitleBar(window);
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) UseDarkTitleBar(window);
    }

    private static void UseDarkTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var enabled = 1;
        if (DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, ImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
