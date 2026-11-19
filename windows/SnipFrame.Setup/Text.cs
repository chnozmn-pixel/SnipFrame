using System.Globalization;

namespace SnipFrame.Setup;

/// <summary>Installer strings: English, or Turkish when Windows runs in Turkish.</summary>
internal static class Text
{
    private static bool Turkish =>
        CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase);

    public static string T(string english, string turkish) => Turkish ? turkish : english;
}
