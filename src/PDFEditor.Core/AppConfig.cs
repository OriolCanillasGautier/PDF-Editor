namespace PDFEditor.Core;

/// <summary>
/// Application configuration and constants
/// </summary>
public static class AppConfig
{
    /// <summary>
    /// Application version — keep in sync with Directory.Build.props &lt;Version&gt; tag
    /// </summary>
    public const string ApplicationVersion = "0.0.2";
    public const string ApplicationName = "PDF Editor";
    public const int DefaultDpi = 300;
    public const string SupportedImageFormats = "PNG,JPEG,TIFF,BMP";
    public const string SupportedPdfFormats = "PDF,PDF/A-1b,PDF/A-2b,PDF/A-3b";

    public static class Paths
    {
        public static string? SettingsDirectory 
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, ApplicationName);
            }
        }
    }
}
