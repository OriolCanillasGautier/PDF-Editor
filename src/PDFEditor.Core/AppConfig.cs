namespace PDFEditor.Core;

/// <summary>
/// Application configuration and constants
/// </summary>
public static class AppConfig
{
    public const string ApplicationVersion = "1.0.0-alpha";
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
