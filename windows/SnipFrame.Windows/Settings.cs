using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnipFrame;

public sealed class AppSettings
{
    public const string DefaultHotkey = "Ctrl+Shift+X";

    public string Hotkey { get; set; } = DefaultHotkey;
    public EditorTool Tool { get; set; } = EditorTool.Arrow;
    public int DelaySeconds { get; set; }
    public string Language { get; set; } = "system";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnipFrame");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (loaded is not null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.Hotkey)) loaded.Hotkey = DefaultHotkey;
                    return loaded;
                }
            }
        }
        catch
        {
            // A corrupt settings file must never block startup.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // Saving settings is best-effort.
        }
    }
}
