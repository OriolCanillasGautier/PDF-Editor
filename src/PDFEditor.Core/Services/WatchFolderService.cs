using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Configuration for the watch folder service
/// </summary>
public class WatchFolderConfig
{
    public string WatchPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string FileFilter { get; set; } = "*.pdf";
    public bool IncludeSubDirectories { get; set; } = false;
    public string? QuickActionId { get; set; }
    public bool DeleteOriginal { get; set; } = false;
    public bool Enabled { get; set; } = true;
    public WatchFolderAction Action { get; set; } = WatchFolderAction.Copy;
    public Dictionary<string, string> ActionParameters { get; set; } = new();
}

/// <summary>
/// Actions that can be performed on watched files
/// </summary>
public enum WatchFolderAction
{
    Copy,
    Watermark,
    Compress,
    Encrypt,
    ScrubMetadata,
    RunQuickAction,
    Export,
    Merge
}

/// <summary>
/// Event args for watch folder file processing events
/// </summary>
public class WatchFolderFileEventArgs : EventArgs
{
    public string FilePath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Service that monitors a folder for new PDF files and automatically processes them.
/// Uses FileSystemWatcher to detect file creation events, then applies configured
/// operations (watermark, compress, encrypt, etc.) and saves results to output folder.
/// </summary>
public class WatchFolderService : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private FileSystemWatcher? _watcher;
    private WatchFolderConfig? _config;
    private bool _isRunning;
    private readonly List<WatchFolderFileEventArgs> _processedFiles = new();

    /// <summary>
    /// Fired when a file is processed
    /// </summary>
    public event EventHandler<WatchFolderFileEventArgs>? FileProcessed;

    /// <summary>
    /// Whether the watch service is currently active
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Current configuration
    /// </summary>
    public WatchFolderConfig? CurrentConfig => _config;

    /// <summary>
    /// History of processed files
    /// </summary>
    public IReadOnlyList<WatchFolderFileEventArgs> ProcessHistory => _processedFiles.AsReadOnly();

    /// <summary>
    /// Starts watching a folder with the given configuration
    /// </summary>
    public void Start(WatchFolderConfig config)
    {
        if (_isRunning)
            Stop();

        _config = config ?? throw new ArgumentNullException(nameof(config));

        if (!Directory.Exists(config.WatchPath))
            throw new DirectoryNotFoundException($"Watch path does not exist: {config.WatchPath}");

        if (!Directory.Exists(config.OutputPath))
            Directory.CreateDirectory(config.OutputPath);

        Log.Info("Starting watch folder: {Watch} → {Output} (action: {Action})",
            config.WatchPath, config.OutputPath, config.Action);

        _watcher = new FileSystemWatcher(config.WatchPath, config.FileFilter)
        {
            IncludeSubdirectories = config.IncludeSubDirectories,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
        };

        _watcher.Created += OnFileCreated;
        _watcher.Renamed += OnFileRenamed;
        _isRunning = true;

        Log.Info("Watch folder started");
    }

    /// <summary>
    /// Stops watching
    /// </summary>
    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileCreated;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Dispose();
            _watcher = null;
        }

        _isRunning = false;
        Log.Info("Watch folder stopped");
    }

    /// <summary>
    /// Manually processes all existing files in the watch folder
    /// </summary>
    public async Task<int> ProcessExistingFilesAsync(CancellationToken ct = default)
    {
        if (_config == null) throw new InvalidOperationException("Watch folder not configured");

        var files = Directory.GetFiles(_config.WatchPath, _config.FileFilter,
            _config.IncludeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

        Log.Info("Processing {Count} existing files in watch folder", files.Length);
        int processed = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (await ProcessFileAsync(file))
                processed++;
        }

        return processed;
    }

    /// <summary>
    /// Process a single file according to the current config
    /// </summary>
    public async Task<bool> ProcessFileAsync(string filePath)
    {
        if (_config == null) throw new InvalidOperationException("Watch folder not configured");

        var eventArgs = new WatchFolderFileEventArgs { FilePath = filePath };

        try
        {
            Log.Info("Processing file: {Path}", filePath);

            // Wait for file to be ready (in case it's still being written)
            await WaitForFileReady(filePath);

            byte[] pdfBytes = await File.ReadAllBytesAsync(filePath);
            byte[] result = pdfBytes;

            // Apply configured action
            result = _config.Action switch
            {
                WatchFolderAction.Watermark => ApplyWatermark(result),
                WatchFolderAction.Compress => await ApplyCompress(result),
                WatchFolderAction.ScrubMetadata => await ApplyScrubMetadata(result),
                WatchFolderAction.Copy => result,
                _ => result
            };

            // Save to output
            string outputFileName = Path.GetFileName(filePath);
            string outputPath = Path.Combine(_config.OutputPath, outputFileName);

            // Avoid overwriting
            int counter = 1;
            while (File.Exists(outputPath))
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                string ext = Path.GetExtension(filePath);
                outputPath = Path.Combine(_config.OutputPath, $"{nameWithoutExt}_{counter++}{ext}");
            }

            await File.WriteAllBytesAsync(outputPath, result);
            eventArgs.OutputPath = outputPath;
            eventArgs.Success = true;

            if (_config.DeleteOriginal)
                File.Delete(filePath);

            Log.Info("File processed: {Input} → {Output}", filePath, outputPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process file: {Path}", filePath);
            eventArgs.Success = false;
            eventArgs.ErrorMessage = ex.Message;
        }

        _processedFiles.Add(eventArgs);
        FileProcessed?.Invoke(this, eventArgs);
        return eventArgs.Success;
    }

    /// <summary>
    /// Clears process history
    /// </summary>
    public void ClearHistory() => _processedFiles.Clear();

    public void Dispose()
    {
        Stop();
    }

    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!_config?.Enabled ?? true) return;
        await ProcessFileAsync(e.FullPath);
    }

    private async void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (!_config?.Enabled ?? true) return;
        if (Path.GetExtension(e.FullPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            await ProcessFileAsync(e.FullPath);
    }

    private async Task WaitForFileReady(string filePath, int maxRetries = 10)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return; // File is ready
            }
            catch (IOException)
            {
                await Task.Delay(500);
            }
        }
    }

    private byte[] ApplyWatermark(byte[] pdfBytes)
    {
        string text = _config?.ActionParameters.GetValueOrDefault("text", "PROCESSED") ?? "PROCESSED";
        var service = new PdfWatermarkService();
        return service.AddTextWatermark(pdfBytes, text);
    }

    private async Task<byte[]> ApplyCompress(byte[] pdfBytes)
    {
        var service = new ImageCompressService();
        return await service.QuickCompressAsync(pdfBytes);
    }

    private async Task<byte[]> ApplyScrubMetadata(byte[] pdfBytes)
    {
        var service = new MetadataScrubberService();
        return await service.ScrubAsync(pdfBytes);
    }
}
