using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace SnipFrame;

/// <summary>
/// Every capture is also written to a dated PNG in this folder, so a snip can be found again
/// long after the editor is closed.
/// </summary>
public static class Archive
{
    private static string? _activeFolder;
    private static bool _defaultBlocked;

    /// <summary>The folder the captures actually land in; the default one unless it is blocked.</summary>
    public static string Folder => _activeFolder ?? DefaultFolder;

    /// <summary>The preferred archive folder: Pictures\SnipFrame.</summary>
    public static string DefaultFolder
    {
        get
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures)) return FallbackFolder;

            return Path.Combine(pictures, "SnipFrame");
        }
    }

    /// <summary>
    /// Windows' Controlled Folder Access (ransomware protection) guards Pictures, so writing
    /// there can be denied. The archive then lives in the app's own local folder instead of
    /// failing, and neither an elevated run nor a Defender exclusion is required.
    /// </summary>
    public static string FallbackFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipFrame", "Archive");

    /// <summary>Writes the image and returns the new path, or null when it could not be saved.</summary>
    public static string? Save(BitmapSource? image)
    {
        if (image is null) return null;

        // A blocked default folder is only retried once per run: every attempt makes Windows
        // raise another "blocked by Controlled folder access" notification.
        if (!_defaultBlocked)
        {
            var primary = Write(image, DefaultFolder);
            if (primary is not null) return primary;

            _defaultBlocked = true;
        }

        return Write(image, FallbackFolder);
    }

    private static string? Write(BitmapSource image, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);

            var path = UniquePath(folder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            _activeFolder = folder;
            return path;
        }
        catch
        {
            // Denied by Windows' folder protection or by permissions: try the next candidate.
            return null;
        }
    }

    private static string UniquePath(string folder)
    {
        var name = $"SnipFrame {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
        var path = Path.Combine(folder, name + ".png");

        for (var index = 2; File.Exists(path); index++)
        {
            path = Path.Combine(folder, $"{name} ({index}).png");
        }

        return path;
    }

    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Process.Start(new ProcessStartInfo(Folder) { UseShellExecute = true });
        }
        catch
        {
            // Nothing useful to report if Explorer refuses to open.
        }
    }
}
