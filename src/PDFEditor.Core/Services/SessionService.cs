using Newtonsoft.Json;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Stores information about the user's session for restore on next startup
/// </summary>
public class SessionData
{
    public List<string> OpenFiles { get; set; } = new();
    public int ActiveTabIndex { get; set; }
    public bool IsDarkTheme { get; set; }
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>
    /// Maximum number of recent files to track
    /// </summary>
    [JsonIgnore]
    public const int MaxRecentFiles = 20;
}

/// <summary>
/// Persists and restores user session state (open files, theme, recent files)
/// </summary>
public class SessionService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly string _sessionFilePath;

    public SessionService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PDFEditor");
        Directory.CreateDirectory(appDataDir);
        _sessionFilePath = Path.Combine(appDataDir, "session.json");
    }

    /// <summary>
    /// Saves the current session state
    /// </summary>
    public void SaveSession(SessionData session)
    {
        try
        {
            var json = JsonConvert.SerializeObject(session, Formatting.Indented);
            File.WriteAllText(_sessionFilePath, json);
            Log.Debug("Session saved: {FileCount} open files, {RecentCount} recent",
                session.OpenFiles.Count, session.RecentFiles.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save session");
        }
    }

    /// <summary>
    /// Loads the last saved session state
    /// </summary>
    public SessionData? LoadSession()
    {
        try
        {
            if (!File.Exists(_sessionFilePath)) return null;

            var json = File.ReadAllText(_sessionFilePath);
            var session = JsonConvert.DeserializeObject<SessionData>(json);
            Log.Debug("Session loaded: {FileCount} open files", session?.OpenFiles.Count ?? 0);
            return session;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load session");
            return null;
        }
    }

    /// <summary>
    /// Adds a file to the recent files list
    /// </summary>
    public void AddRecentFile(SessionData session, string filePath)
    {
        session.RecentFiles.Remove(filePath);
        session.RecentFiles.Insert(0, filePath);
        if (session.RecentFiles.Count > SessionData.MaxRecentFiles)
            session.RecentFiles.RemoveRange(SessionData.MaxRecentFiles,
                session.RecentFiles.Count - SessionData.MaxRecentFiles);
    }
}
