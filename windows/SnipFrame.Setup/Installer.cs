using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SnipFrame.Setup;

internal sealed record InstallProgress(string Text, double Fraction);

internal static class Installer
{
    private const string PayloadResource = "SnipFrame.Setup.Payload.SnipFrame.zip";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SnipFrame";
    private const string AppPathKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\SnipFrame.exe";

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SnipFrame");

    /// <summary>Per-user install: no elevation, shortcuts in the current profile, uninstall in Settings.</summary>
    public static void Install(string directory, bool desktopShortcut, string version, IProgress<InstallProgress> progress)
    {
        progress.Report(new InstallProgress(Text.T("Closing a running SnipFrame…", "Açık SnipFrame kapatılıyor…"), 0.02));
        CloseRunningApp();

        Directory.CreateDirectory(directory);

        progress.Report(new InstallProgress(Text.T("Copying files…", "Dosyalar kopyalanıyor…"), 0.05));
        ExtractPayload(directory, progress);

        var executable = Path.Combine(directory, "SnipFrame.exe");
        if (!File.Exists(executable))
        {
            throw new InvalidOperationException(Text.T(
                "The application files are missing from the setup package.",
                "Kurulum paketinde uygulama dosyaları eksik."));
        }

        progress.Report(new InstallProgress(Text.T("Registering the app…", "Uygulama kaydediliyor…"), 0.9));
        RegisterUninstall(directory, executable, version);
        RegisterAppPath(executable);

        progress.Report(new InstallProgress(Text.T("Creating shortcuts…", "Kısayollar oluşturuluyor…"), 0.96));
        CreateShortcut(StartMenuShortcut(), executable, directory);
        if (desktopShortcut) CreateShortcut(DesktopShortcut(), executable, directory);
        else TryDelete(DesktopShortcut());

        progress.Report(new InstallProgress(Text.T("Finished", "Tamamlandı"), 1));
    }

    private static void ExtractPayload(string directory, IProgress<InstallProgress> progress)
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource)
                             ?? throw new InvalidOperationException(Text.T(
                                 "The setup package does not contain the application payload.",
                                 "Kurulum paketinde uygulama içeriği bulunamadı."));

        // ZipArchive needs a seekable stream; bundled resources can be forward-only.
        using var buffer = resource.CanSeek ? null : new MemoryStream();
        if (buffer is not null)
        {
            resource.CopyTo(buffer);
            buffer.Position = 0;
        }

        using var archive = new ZipArchive(buffer ?? resource, ZipArchiveMode.Read);

        var total = Math.Max(1, archive.Entries.Count);
        for (var index = 0; index < archive.Entries.Count; index++)
        {
            var entry = archive.Entries[index];
            var target = Path.GetFullPath(Path.Combine(directory, entry.FullName));

            if (!target.StartsWith(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
            {
                continue; // never write outside the install folder
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            // Existing single-file builds can be locked by a leftover process; skip what cannot be replaced.
            try
            {
                entry.ExtractToFile(target, overwrite: true);
            }
            catch (IOException)
            {
                TryDelete(target);
                entry.ExtractToFile(target, overwrite: true);
            }

            if (index % 4 == 0)
            {
                var fraction = 0.05 + 0.82 * (index + 1) / total;
                progress.Report(new InstallProgress(
                    Text.T($"Copying {entry.Name}…", $"{entry.Name} kopyalanıyor…"), fraction));
            }
        }
    }

    private static void CloseRunningApp()
    {
        foreach (var process in Process.GetProcessesByName("SnipFrame"))
        {
            try
            {
                process.Kill();
                process.WaitForExit(3000);
            }
            catch
            {
                // A process that refuses to exit is reported by the file copy step.
            }
        }
    }

    private static void RegisterUninstall(string directory, string executable, string version)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        if (key is null) return;

        key.SetValue("DisplayName", "SnipFrame");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "SnipFrame");
        key.SetValue("DisplayIcon", executable);
        key.SetValue("InstallLocation", directory);
        key.SetValue("UninstallString", $"\"{executable}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", EstimateSizeKb(directory), RegistryValueKind.DWord);
    }

    private static void RegisterAppPath(string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AppPathKey);
        key?.SetValue(string.Empty, executable);
    }

    private static int EstimateSizeKb(string directory)
    {
        try
        {
            var bytes = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
            return (int)Math.Min(int.MaxValue, bytes / 1024);
        }
        catch
        {
            return 0;
        }
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
            // Best effort.
        }
    }

    /// <summary>
    /// Creates a .lnk through the Windows Script Host COM object. Must run on an STA thread:
    /// the shell's shortcut object is apartment-bound and fails from thread-pool threads.
    /// Shortcuts are best effort; the app is already registered by the time they are created.
    /// </summary>
    private static void CreateShortcut(string linkPath, string targetPath, string workingDirectory)
    {
        try
        {
            WriteShortcut(linkPath, targetPath, workingDirectory);
        }
        catch
        {
            // A missing shortcut must never fail the installation.
        }
    }

    private static void WriteShortcut(string linkPath, string targetPath, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;

        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

            var shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                null, shell, new object[] { linkPath });
            if (shortcut is null) return;

            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { $"{targetPath},0" });
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "SnipFrame" });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);

            Marshal.ReleaseComObject(shortcut);
        }
        finally
        {
            if (shell is not null) Marshal.ReleaseComObject(shell);
        }
    }
}
