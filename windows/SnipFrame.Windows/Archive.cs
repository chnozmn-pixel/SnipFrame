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
    public static string Folder
    {
        get
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures))
            {
                pictures = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipFrame");
            }

            return Path.Combine(pictures, "SnipFrame");
        }
    }

    /// <summary>Writes the image and returns the new path, or null when it could not be saved.</summary>
    public static string? Save(BitmapSource? image)
    {
        if (image is null) return null;

        try
        {
            Directory.CreateDirectory(Folder);

            var path = UniquePath();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }
        catch
        {
            // Archiving is a convenience: a failure must never interrupt the capture.
            return null;
        }
    }

    private static string UniquePath()
    {
        var name = $"SnipFrame {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
        var path = Path.Combine(Folder, name + ".png");

        for (var index = 2; File.Exists(path); index++)
        {
            path = Path.Combine(Folder, $"{name} ({index}).png");
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
