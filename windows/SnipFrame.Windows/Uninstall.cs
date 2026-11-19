using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SnipFrame;

/// <summary>Removes shortcuts, the Add/Remove Programs entry and the install folder itself.</summary>
internal static class Uninstall
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SnipFrame";
    private const string AppPathKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\SnipFrame.exe";

    public static void Run()
    {
        var executable = Environment.ProcessPath ?? string.Empty;
        var folder = Path.GetDirectoryName(executable) ?? string.Empty;

        TryDelete(StartMenuShortcut());
        TryDelete(DesktopShortcut());
        TryDeleteKey(UninstallKey);
        TryDeleteKey(AppPathKey);

        if (folder.Length == 0) return;

        ScheduleFolderRemoval(folder);
    }

    private static string StartMenuShortcut() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", "SnipFrame.lnk");

    private static string DesktopShortcut() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SnipFrame.lnk");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A locked shortcut must not stop the uninstall.
        }
    }

    private static void TryDeleteKey(string path)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
        catch
        {
            // Best effort: the app still gets removed even if the registry is locked down.
        }
    }

    /// <summary>A running executable cannot delete its own folder, so a hidden cmd finishes the job.</summary>
    private static void ScheduleFolderRemoval(string folder)
    {
        try
        {
            var script = Path.Combine(Path.GetTempPath(), $"snipframe-uninstall-{Guid.NewGuid():N}.cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                $"rd /s /q \"{folder}\"\r\n" +
                "del /f /q \"%~f0\"\r\n");

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch
        {
            // Nothing else to try; the shortcuts and registry entry are already gone.
        }
    }
}
