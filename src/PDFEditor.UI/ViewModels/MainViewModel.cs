using System.Collections.ObjectModel;
using System.Reactive;
using NLog;
using PDFEditor.Core.Services;
using ReactiveUI;

namespace PDFEditor.UI.ViewModels;

/// <summary>
/// App-level ViewModel managing document tabs, theme, and global state.
/// Each open PDF document gets its own DocumentTabViewModel.
/// </summary>
public class MainViewModel : ReactiveObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly SessionService _sessionService = new();
    private SessionData _sessionData = new();

    private DocumentTabViewModel? _activeTab;
    private bool _isDarkTheme;
    private string _appStatus = "Ready";

    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = new();
    public ObservableCollection<string> RecentFiles { get; } = new();

    public DocumentTabViewModel? ActiveTab
    {
        get => _activeTab;
        set
        {
            this.RaiseAndSetIfChanged(ref _activeTab, value);
            this.RaisePropertyChanged(nameof(HasActiveTab));
        }
    }

    public bool HasActiveTab => _activeTab != null;

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
    }

    public string AppStatus
    {
        get => _appStatus;
        set => this.RaiseAndSetIfChanged(ref _appStatus, value);
    }

    // Commands
    public ReactiveCommand<Unit, Unit> NewTabCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }

    public MainViewModel()
    {
        NewTabCommand = ReactiveCommand.Create(CreateNewTab);
        CloseTabCommand = ReactiveCommand.Create(CloseActiveTab);
        ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);

        // Load previous session
        LoadSession();
    }

    /// <summary>
    /// Loads session data (recent files, theme preference).
    /// Does NOT auto-reopen files — that's handled by RestoreSession().
    /// </summary>
    private void LoadSession()
    {
        try
        {
            var session = _sessionService.LoadSession();
            if (session != null)
            {
                _sessionData = session;
                IsDarkTheme = session.IsDarkTheme;

                RecentFiles.Clear();
                foreach (var f in session.RecentFiles)
                    RecentFiles.Add(f);

                Log.Info("Session loaded: {RecentCount} recent files, dark={Dark}",
                    session.RecentFiles.Count, session.IsDarkTheme);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load session");
        }
    }

    /// <summary>
    /// Restores previously open files from the last session
    /// </summary>
    public void RestoreSession()
    {
        try
        {
            if (_sessionData.OpenFiles.Count == 0) return;

            Log.Info("Restoring {Count} files from session", _sessionData.OpenFiles.Count);
            foreach (var filePath in _sessionData.OpenFiles)
            {
                if (System.IO.File.Exists(filePath))
                {
                    try { OpenFile(filePath); }
                    catch (Exception ex)
                    {
                        Log.Warn(ex, "Failed to restore file: {Path}", filePath);
                    }
                }
            }

            // Restore active tab
            if (_sessionData.ActiveTabIndex >= 0 && _sessionData.ActiveTabIndex < Tabs.Count)
                ActiveTab = Tabs[_sessionData.ActiveTabIndex];
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Session restore failed");
        }
    }

    /// <summary>
    /// Saves the current session state
    /// </summary>
    public void SaveSession()
    {
        try
        {
            _sessionData.OpenFiles = Tabs
                .Where(t => t.FilePath != null)
                .Select(t => t.FilePath!)
                .ToList();
            _sessionData.ActiveTabIndex = _activeTab != null ? Tabs.IndexOf(_activeTab) : 0;
            _sessionData.IsDarkTheme = IsDarkTheme;

            _sessionService.SaveSession(_sessionData);
            Log.Info("Session saved: {FileCount} files", _sessionData.OpenFiles.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save session");
        }
    }

    /// <summary>
    /// Opens a PDF file in a new tab
    /// </summary>
    public DocumentTabViewModel OpenFile(string filePath)
    {
        Log.Info("Opening file in new tab: {FilePath}", filePath);
        var tab = new DocumentTabViewModel();
        tab.LoadPdf(filePath);
        Tabs.Add(tab);
        ActiveTab = tab;
        AppStatus = tab.StatusText;

        // Track in recent files
        _sessionService.AddRecentFile(_sessionData, filePath);
        RecentFiles.Clear();
        foreach (var f in _sessionData.RecentFiles) RecentFiles.Add(f);

        return tab;
    }

    /// <summary>
    /// Opens a file in the active tab (if empty) or creates a new tab
    /// </summary>
    public void OpenFileInActiveOrNewTab(string filePath)
    {
        if (_activeTab != null && !_activeTab.IsDocumentLoaded)
        {
            _activeTab.LoadPdf(filePath);
        }
        else
        {
            OpenFile(filePath);
        }
    }

    private void CreateNewTab()
    {
        var tab = new DocumentTabViewModel();
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    public void CloseActiveTab()
    {
        if (_activeTab == null) return;
        var idx = Tabs.IndexOf(_activeTab);
        Tabs.Remove(_activeTab);

        if (Tabs.Count > 0)
            ActiveTab = Tabs[Math.Min(idx, Tabs.Count - 1)];
        else
            ActiveTab = null;
    }

    public void CloseTab(DocumentTabViewModel tab)
    {
        var wasActive = tab == _activeTab;
        var idx = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (wasActive)
        {
            if (Tabs.Count > 0)
                ActiveTab = Tabs[Math.Min(idx, Tabs.Count - 1)];
            else
                ActiveTab = null;
        }
    }

    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
    }
}
