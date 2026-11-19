using System.Globalization;
using System.Resources;

namespace SnipFrame;

/// <summary>Looks up UI strings in the satellite assemblies built from Strings*.resx.</summary>
public static class L
{
    private static readonly ResourceManager Manager = new("SnipFrame.Strings", typeof(L).Assembly);

    public static string T(string key, string fallback)
    {
        try
        {
            return Manager.GetString(key, CultureInfo.CurrentUICulture) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static string T(string key, string fallback, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, T(key, fallback), args);
    }

    /// <summary>"system", "en" or "tr"; "system" follows the Windows display language.</summary>
    public static void Apply(string language)
    {
        var culture = language switch
        {
            "en" => new CultureInfo("en"),
            "tr" => new CultureInfo("tr"),
            _ => CultureInfo.InstalledUICulture
        };

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}

/// <summary>Tool titles for the picker and the editor toolbar.</summary>
public static class ToolNames
{
    public static string Of(EditorTool tool) => L.T($"tool.{tool.ToString().ToLowerInvariant()}", tool.ToString());
}
