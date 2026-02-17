using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Avalonia.Media.Imaging;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using NLog;
using PDFEditor.Core.Services;
using ReactiveUI;

namespace PDFEditor.UI.ViewModels;

/// <summary>
/// Represents a single page thumbnail in the sidebar
/// </summary>
public class ThumbnailItem : ReactiveObject
{
    private Bitmap? _image;
    public int PageNumber { get; set; }

    public Bitmap? Image
    {
        get => _image;
        set => this.RaiseAndSetIfChanged(ref _image, value);
    }
}

/// <summary>
/// Represents a single search result in the search panel
/// </summary>
public class SearchResultItem
{
    public int PageNumber { get; set; }
    public string DisplayText { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel for a single open PDF document tab.
/// Thread-safe: all PDF mutations go through a SemaphoreSlim lock.
/// All reactive canExecute observables are marshaled to the UI thread.
/// </summary>
public class DocumentTabViewModel : ReactiveObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // Services
    private readonly PdfRenderService _renderService = new();
    private readonly PdfOperations _pdfOps = new();
    private readonly PdfSearchService _searchService = new();
    private readonly PdfSplitService _splitService = new();
    private readonly PdfWatermarkService _watermarkService = new();
    private readonly PdfSecurityService _securityService = new();
    private readonly PdfExportService _exportService = new();
    private readonly PdfAnnotationService _annotationService = new();
    private readonly PdfCropService _cropService = new();
    private readonly UndoRedoManager _undoRedo = new();

    // Thread safety: only one PDF mutation at a time
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private bool _isBusy;

    // PDF state
    private byte[]? _pdfBytes;
    private string? _filePath;
    private bool _isModified;

    // View state
    private int _currentPageIndex;
    private int _pageCount;
    private Bitmap? _currentPageImage;
    private string _pageInfoText = "No document";
    private string? _title;
    private string? _author;
    private string? _subject;
    private string _statusText = "Ready";
    private bool _isDocumentLoaded;
    private int _selectedThumbnailIndex = -1;
    private double _zoomLevel = 1.0;
    private string _searchQuery = string.Empty;
    private bool _isSearchVisible;
    private int _currentSearchResultIndex = -1;

    // Multi-selection
    private List<int> _selectedPageIndices = new();

    // Annotations
    private AnnotationType _activeAnnotationTool = AnnotationType.Text;
    private bool _isAnnotationMode;

    // Collections
    public ObservableCollection<ThumbnailItem> Thumbnails { get; } = new();
    public ObservableCollection<SearchResultItem> SearchResults { get; } = new();
    public ObservableCollection<PdfAnnotation> Annotations { get; } = new();

    // Public getters for services / state
    public byte[]? PdfBytes => _pdfBytes;
    public UndoRedoManager UndoRedo => _undoRedo;
    public PdfExportService ExportService => _exportService;
    public PdfSecurityService SecurityService => _securityService;
    public PdfWatermarkService WatermarkService => _watermarkService;
    public PdfSplitService SplitService => _splitService;
    public PdfAnnotationService AnnotationService => _annotationService;
    public PdfCropService CropService => _cropService;

    #region Properties

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public string TabTitle => _isModified
        ? $"* {Path.GetFileName(_filePath ?? "Untitled")}"
        : Path.GetFileName(_filePath ?? "Untitled");

    public string? FilePath
    {
        get => _filePath;
        set
        {
            this.RaiseAndSetIfChanged(ref _filePath, value);
            this.RaisePropertyChanged(nameof(TabTitle));
        }
    }

    public bool IsModified
    {
        get => _isModified;
        set
        {
            this.RaiseAndSetIfChanged(ref _isModified, value);
            this.RaisePropertyChanged(nameof(TabTitle));
        }
    }

    public int CurrentPageIndex
    {
        get => _currentPageIndex;
        set
        {
            if (value >= 0 && value < _pageCount && value != _currentPageIndex)
            {
                this.RaiseAndSetIfChanged(ref _currentPageIndex, value);
                _selectedThumbnailIndex = value;
                this.RaisePropertyChanged(nameof(SelectedThumbnailIndex));
                RenderCurrentPage();
                UpdatePageInfo();
            }
        }
    }

    public int PageCount
    {
        get => _pageCount;
        private set => this.RaiseAndSetIfChanged(ref _pageCount, value);
    }

    public Bitmap? CurrentPageImage
    {
        get => _currentPageImage;
        set => this.RaiseAndSetIfChanged(ref _currentPageImage, value);
    }

    public string PageInfoText
    {
        get => _pageInfoText;
        set => this.RaiseAndSetIfChanged(ref _pageInfoText, value);
    }

    public string? Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string? Author
    {
        get => _author;
        set => this.RaiseAndSetIfChanged(ref _author, value);
    }

    public string? Subject
    {
        get => _subject;
        set => this.RaiseAndSetIfChanged(ref _subject, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public bool IsDocumentLoaded
    {
        get => _isDocumentLoaded;
        private set => this.RaiseAndSetIfChanged(ref _isDocumentLoaded, value);
    }

    public int SelectedThumbnailIndex
    {
        get => _selectedThumbnailIndex;
        set
        {
            if (value != _selectedThumbnailIndex)
            {
                this.RaiseAndSetIfChanged(ref _selectedThumbnailIndex, value);
                if (value >= 0 && value < _pageCount && value != _currentPageIndex)
                {
                    _currentPageIndex = value;
                    this.RaisePropertyChanged(nameof(CurrentPageIndex));
                    RenderCurrentPage();
                    UpdatePageInfo();
                }
            }
        }
    }

    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            value = Math.Clamp(value, 0.25, 4.0);
            if (Math.Abs(value - _zoomLevel) > 0.001)
            {
                this.RaiseAndSetIfChanged(ref _zoomLevel, value);
                RenderCurrentPage();
                this.RaisePropertyChanged(nameof(ZoomPercent));
            }
        }
    }

    public string ZoomPercent => $"{(int)(_zoomLevel * 100)}%";

    public string SearchQuery
    {
        get => _searchQuery;
        set => this.RaiseAndSetIfChanged(ref _searchQuery, value);
    }

    public bool IsSearchVisible
    {
        get => _isSearchVisible;
        set => this.RaiseAndSetIfChanged(ref _isSearchVisible, value);
    }

    public bool CanUndo => _undoRedo.CanUndo;
    public bool CanRedo => _undoRedo.CanRedo;

    // Multi-selection
    public List<int> SelectedPageIndices => _selectedPageIndices;
    public bool HasMultipleSelection => _selectedPageIndices.Count > 1;
    public string SelectionInfoText => _selectedPageIndices.Count > 1
        ? $"{_selectedPageIndices.Count} pages selected"
        : string.Empty;

    /// <summary>
    /// Called from code-behind when the thumbnail ListBox selection changes
    /// </summary>
    public void UpdateSelectedPages(IList<int> indices)
    {
        _selectedPageIndices = indices.OrderBy(i => i).ToList();
        this.RaisePropertyChanged(nameof(SelectedPageIndices));
        this.RaisePropertyChanged(nameof(HasMultipleSelection));
        this.RaisePropertyChanged(nameof(SelectionInfoText));
    }

    // Annotation properties
    public AnnotationType ActiveAnnotationTool
    {
        get => _activeAnnotationTool;
        set => this.RaiseAndSetIfChanged(ref _activeAnnotationTool, value);
    }

    public bool IsAnnotationMode
    {
        get => _isAnnotationMode;
        set => this.RaiseAndSetIfChanged(ref _isAnnotationMode, value);
    }

    /// <summary>
    /// Gets annotations for the current page
    /// </summary>
    public IEnumerable<PdfAnnotation> CurrentPageAnnotations =>
        Annotations.Where(a => a.PageIndex == _currentPageIndex);

    public void AddAnnotation(PdfAnnotation annotation)
    {
        annotation.PageIndex = _currentPageIndex;
        Annotations.Add(annotation);
        this.RaisePropertyChanged(nameof(CurrentPageAnnotations));
        IsModified = true;
        StatusText = $"Added {annotation.Type} annotation";
        Log.Debug("Added annotation: {Type} on page {Page}", annotation.Type, _currentPageIndex + 1);
    }

    public void RemoveAnnotation(string annotationId)
    {
        var ann = Annotations.FirstOrDefault(a => a.Id == annotationId);
        if (ann != null)
        {
            Annotations.Remove(ann);
            this.RaisePropertyChanged(nameof(CurrentPageAnnotations));
            IsModified = true;
        }
    }

    /// <summary>
    /// Burns all annotations into the PDF permanently
    /// </summary>
    public async Task BurnAnnotationsAsync()
    {
        await RunLockedAsync("BurnAnnotations", async () =>
        {
            if (_pdfBytes == null || Annotations.Count == 0) return;

            var localBytes = _pdfBytes;
            var annList = Annotations.ToList();
            var newBytes = await Task.Run(() => _annotationService.BurnAnnotations(localBytes, annList));

            await OnUIThread(() =>
            {
                RecordAndApply($"Burn {annList.Count} annotation(s)", newBytes);
                Annotations.Clear();
                this.RaisePropertyChanged(nameof(CurrentPageAnnotations));
                RenderCurrentPage();
                LoadThumbnails();
                StatusText = $"Burned {annList.Count} annotation(s) into PDF";
            });
        });
    }

    /// <summary>
    /// Moves a page from one index to another (for drag-drop reorder)
    /// </summary>
    public async Task MovePageToAsync(int fromIndex, int toIndex)
    {
        await RunLockedAsync("MovePageTo", async () =>
        {
            if (_pdfBytes == null) return;
            if (fromIndex < 0 || fromIndex >= _pageCount) return;
            if (toIndex < 0 || toIndex >= _pageCount) return;
            if (fromIndex == toIndex) return;

            var localBytes = _pdfBytes;
            var newBytes = await Task.Run(() => _splitService.MovePage(localBytes, fromIndex, toIndex));

            await OnUIThread(() =>
            {
                RecordAndApply($"Move page {fromIndex + 1} to position {toIndex + 1}", newBytes);
                _currentPageIndex = toIndex;
                this.RaisePropertyChanged(nameof(CurrentPageIndex));
                _selectedThumbnailIndex = toIndex;
                this.RaisePropertyChanged(nameof(SelectedThumbnailIndex));
                RenderCurrentPage();
                LoadThumbnails();
                UpdatePageInfo();
                StatusText = $"Moved page to position {toIndex + 1}";
            });
        });
    }

    /// <summary>
    /// Crops the current page (or selected pages) to a normalized region
    /// </summary>
    public async Task CropPageAsync(double left, double top, double right, double bottom)
    {
        await RunLockedAsync("CropPage", async () =>
        {
            if (_pdfBytes == null) return;

            var pagesToCrop = _selectedPageIndices.Count > 1
                ? _selectedPageIndices.Select(i => i + 1).ToArray()
                : new[] { _currentPageIndex + 1 };

            var localBytes = _pdfBytes;
            var l = left; var t = top; var r = right; var b = bottom;
            var newBytes = await Task.Run(() =>
                _cropService.CropPages(localBytes, pagesToCrop, l, t, r, b));

            await OnUIThread(() =>
            {
                RecordAndApply($"Crop {pagesToCrop.Length} page(s)", newBytes);
                RenderCurrentPage();
                LoadThumbnails();
                StatusText = $"Cropped {pagesToCrop.Length} page(s)";
            });
        });
    }

    /// <summary>
    /// Returns the undo history descriptions for the history panel
    /// </summary>
    public List<string> GetUndoHistory() => _undoRedo.UndoHistoryDescriptions;
    public List<string> GetRedoHistory() => _undoRedo.RedoHistoryDescriptions;

    /// <summary>
    /// Undo multiple steps at once (for history panel click)
    /// </summary>
    public async Task UndoToAsync(int steps)
    {
        await RunLockedAsync("UndoTo", async () =>
        {
            await Task.CompletedTask;
            await OnUIThread(() =>
            {
                var state = _undoRedo.UndoTo(steps);
                if (state != null)
                {
                    _pdfBytes = state;
                    RefreshAfterUndoRedo();
                    StatusText = $"Undone {steps} step(s)";
                }
            });
        });
    }

    /// <summary>
    /// Inserts a page from an image file (clipboard or file)
    /// </summary>
    public async Task InsertImageAsPageAsync(byte[] imageBytes)
    {
        await RunLockedAsync("InsertImageAsPage", async () =>
        {
            if (_pdfBytes == null) return;

            // Convert image to a single-page PDF, then insert
            var localBytes = _pdfBytes;
            var insertAt = _currentPageIndex;
            var newBytes = await Task.Run(() =>
            {
                // Create a minimal single-page PDF with the image
                using var imgPdfMs = new MemoryStream();
                using var writer = new iText.Kernel.Pdf.PdfWriter(imgPdfMs);
                using var imgDoc = new iText.Kernel.Pdf.PdfDocument(writer);
                var page = imgDoc.AddNewPage();

                var imgData = iText.IO.Image.ImageDataFactory.Create(imageBytes);
                var pdfImg = new iText.Layout.Element.Image(imgData);

                // Size page to image (in points)
                float imgW = pdfImg.GetImageWidth();
                float imgH = pdfImg.GetImageHeight();
                page.SetMediaBox(new iText.Kernel.Geom.Rectangle(0, 0, imgW, imgH));

                var canvas = new iText.Layout.Canvas(new iText.Kernel.Pdf.Canvas.PdfCanvas(page),
                    new iText.Kernel.Geom.Rectangle(0, 0, imgW, imgH));
                canvas.Add(pdfImg.SetFixedPosition(0, 0));
                canvas.Close();
                imgDoc.Close();

                var imgPdfBytes = imgPdfMs.ToArray();
                return _splitService.InsertPages(localBytes, imgPdfBytes, insertAt);
            });

            await OnUIThread(() =>
            {
                RecordAndApply("Insert image as page", newBytes);
                PageCount = _renderService.GetPageCount(_pdfBytes!);
                RenderCurrentPage();
                LoadThumbnails();
                UpdatePageInfo();
                StatusText = "Inserted image as page";
            });
        });
    }

    #endregion

    #region Commands

    public ReactiveCommand<Unit, Unit> NextPageCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousPageCommand { get; }
    public ReactiveCommand<Unit, Unit> FirstPageCommand { get; }
    public ReactiveCommand<Unit, Unit> LastPageCommand { get; }
    public ReactiveCommand<Unit, Unit> DeletePageCommand { get; }
    public ReactiveCommand<Unit, Unit> RotateRightCommand { get; }
    public ReactiveCommand<Unit, Unit> RotateLeftCommand { get; }
    public ReactiveCommand<Unit, Unit> MovePageUpCommand { get; }
    public ReactiveCommand<Unit, Unit> MovePageDownCommand { get; }
    public ReactiveCommand<Unit, Unit> ZoomInCommand { get; }
    public ReactiveCommand<Unit, Unit> ZoomOutCommand { get; }
    public ReactiveCommand<Unit, Unit> ZoomFitCommand { get; }
    public ReactiveCommand<Unit, Unit> UndoCommand { get; }
    public ReactiveCommand<Unit, Unit> RedoCommand { get; }
    public ReactiveCommand<Unit, Unit> SearchCommand { get; }
    public ReactiveCommand<Unit, Unit> NextSearchResultCommand { get; }
    public ReactiveCommand<Unit, Unit> PrevSearchResultCommand { get; }

    #endregion

    public DocumentTabViewModel()
    {
        // Use AvaloniaScheduler.Instance directly — guarantees the Avalonia UI dispatcher
        // regardless of whether RxApp.MainThreadScheduler was correctly initialized.
        IScheduler uiScheduler = AvaloniaScheduler.Instance;

        var canGoNext = this.WhenAnyValue(x => x.CurrentPageIndex, x => x.PageCount, (i, c) => c > 0 && i < c - 1)
            .ObserveOn(uiScheduler);
        var canGoPrev = this.WhenAnyValue(x => x.CurrentPageIndex, x => x.PageCount, (i, c) => c > 0 && i > 0)
            .ObserveOn(uiScheduler);
        var hasDoc = this.WhenAnyValue(x => x.IsDocumentLoaded)
            .ObserveOn(uiScheduler);

        // Combine "has document" with "not busy" for destructive operations — all on UI thread
        var canOperate = this.WhenAnyValue(x => x.IsDocumentLoaded, x => x.IsBusy, (d, b) => d && !b)
            .ObserveOn(uiScheduler);
        var canOperateMulti = this.WhenAnyValue(x => x.IsDocumentLoaded, x => x.PageCount, x => x.IsBusy,
            (d, c, b) => d && c > 1 && !b)
            .ObserveOn(uiScheduler);
        var canUndoObs = this.WhenAnyValue(x => x.CanUndo, x => x.IsBusy, (u, b) => u && !b)
            .ObserveOn(uiScheduler);
        var canRedoObs = this.WhenAnyValue(x => x.CanRedo, x => x.IsBusy, (r, b) => r && !b)
            .ObserveOn(uiScheduler);

        NextPageCommand = ReactiveCommand.Create(GoNextPage, canGoNext);
        PreviousPageCommand = ReactiveCommand.Create(GoPreviousPage, canGoPrev);
        FirstPageCommand = ReactiveCommand.Create(() => { if (_pageCount > 0) CurrentPageIndex = 0; }, hasDoc);
        LastPageCommand = ReactiveCommand.Create(() => { if (_pageCount > 0) CurrentPageIndex = _pageCount - 1; }, hasDoc);
        DeletePageCommand = ReactiveCommand.CreateFromTask(ExecuteDeletePageAsync, canOperateMulti);
        RotateRightCommand = ReactiveCommand.CreateFromTask(() => ExecuteRotatePageAsync(90), canOperate);
        RotateLeftCommand = ReactiveCommand.CreateFromTask(() => ExecuteRotatePageAsync(270), canOperate);
        MovePageUpCommand = ReactiveCommand.CreateFromTask(ExecuteMovePageUpAsync,
            this.WhenAnyValue(x => x.CurrentPageIndex, x => x.PageCount, x => x.IsBusy,
                (i, c, b) => c > 0 && i > 0 && !b).ObserveOn(uiScheduler));
        MovePageDownCommand = ReactiveCommand.CreateFromTask(ExecuteMovePageDownAsync,
            this.WhenAnyValue(x => x.CurrentPageIndex, x => x.PageCount, x => x.IsBusy,
                (i, c, b) => c > 0 && i < c - 1 && !b).ObserveOn(uiScheduler));
        ZoomInCommand = ReactiveCommand.Create(() => { ZoomLevel += 0.25; });
        ZoomOutCommand = ReactiveCommand.Create(() => { ZoomLevel -= 0.25; });
        ZoomFitCommand = ReactiveCommand.Create(() => { ZoomLevel = 1.0; });
        UndoCommand = ReactiveCommand.CreateFromTask(ExecuteUndoAsync, canUndoObs);
        RedoCommand = ReactiveCommand.CreateFromTask(ExecuteRedoAsync, canRedoObs);
        SearchCommand = ReactiveCommand.CreateFromTask(ExecuteSearchAsync, hasDoc);
        NextSearchResultCommand = ReactiveCommand.Create(GoNextSearchResult);
        PrevSearchResultCommand = ReactiveCommand.Create(GoPrevSearchResult);

        // Subscribe to command exceptions so they don't crash the app
        SubscribeCommandErrors(NextPageCommand, PreviousPageCommand, FirstPageCommand, LastPageCommand,
            DeletePageCommand, RotateRightCommand, RotateLeftCommand, MovePageUpCommand, MovePageDownCommand,
            ZoomInCommand, ZoomOutCommand, ZoomFitCommand, UndoCommand, RedoCommand,
            SearchCommand, NextSearchResultCommand, PrevSearchResultCommand);

        Log.Info("DocumentTabViewModel created");
    }

    private void SubscribeCommandErrors(params ReactiveCommand<Unit, Unit>[] commands)
    {
        foreach (var cmd in commands)
        {
            cmd.ThrownExceptions.Subscribe(ex =>
            {
                Log.Error(ex, "Command error");
                Dispatcher.UIThread.Post(() => StatusText = $"Error: {ex.Message}");
            });
        }
    }

    #region Thread-Safe Operation Wrapper

    /// <summary>
    /// Runs an operation under the lock so only one mutation at a time can happen.
    /// IsBusy changes are always dispatched to the UI thread.
    /// </summary>
    private async Task RunLockedAsync(string operationName, Func<Task> operation)
    {
        if (!await _operationLock.WaitAsync(0))
        {
            Log.Warn("Operation '{Operation}' skipped — another operation is in progress", operationName);
            await Dispatcher.UIThread.InvokeAsync(() =>
                StatusText = "Please wait — another operation is in progress");
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsBusy = true);
            Log.Debug("Starting operation: {Operation}", operationName);
            await operation();
            Log.Debug("Completed operation: {Operation}", operationName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Operation '{Operation}' failed", operationName);
            await Dispatcher.UIThread.InvokeAsync(() =>
                StatusText = $"Error: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Helper to dispatch all UI property updates after a Task.Run() completes on a background thread.
    /// </summary>
    private async Task OnUIThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            await Dispatcher.UIThread.InvokeAsync(action);
    }

    #endregion

    #region Navigation

    private void GoNextPage() { if (_currentPageIndex < _pageCount - 1) CurrentPageIndex++; }
    private void GoPreviousPage() { if (_currentPageIndex > 0) CurrentPageIndex--; }

    public void GoToPage(int pageNumber)
    {
        int idx = pageNumber - 1;
        if (idx >= 0 && idx < _pageCount)
            CurrentPageIndex = idx;
    }

    #endregion

    #region Document Loading & Saving

    public void LoadPdf(string filePath)
    {
        try
        {
            Log.Info("Loading PDF: {FilePath}", filePath);
            StatusText = "Loading...";
            var rawBytes = File.ReadAllBytes(filePath);

            if (_securityService.IsEncrypted(rawBytes))
            {
                Log.Warn("Document is password-protected: {FilePath}", filePath);
                StatusText = "Document is password-protected";
                return;
            }

            LoadPdfFromBytes(rawBytes, filePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load PDF: {FilePath}", filePath);
            StatusText = $"Error: {ex.Message}";
        }
    }

    public void LoadPdfFromBytes(byte[] pdfBytes, string? filePath = null)
    {
        _pdfBytes = pdfBytes;
        FilePath = filePath;
        _undoRedo.Clear();

        PageCount = _renderService.GetPageCount(_pdfBytes);
        IsDocumentLoaded = true;
        IsModified = false;

        _currentPageIndex = 0;
        this.RaisePropertyChanged(nameof(CurrentPageIndex));
        _selectedThumbnailIndex = 0;
        this.RaisePropertyChanged(nameof(SelectedThumbnailIndex));

        LoadMetadata();
        RenderCurrentPage();
        LoadThumbnails();
        UpdatePageInfo();
        RaiseUndoRedoChanged();
        StatusText = $"Loaded: {Path.GetFileName(filePath ?? "document")} ({PageCount} pages)";
        Log.Info("PDF loaded: {FilePath}, {PageCount} pages", filePath, PageCount);
    }

    public void SavePdf(string filePath)
    {
        if (_pdfBytes == null) return;
        try
        {
            Log.Info("Saving PDF: {FilePath}", filePath);
            StatusText = "Saving...";
            var bytes = _pdfOps.SetMetadata(_pdfBytes, Title, Author, Subject);
            File.WriteAllBytes(filePath, bytes);
            _pdfBytes = bytes;
            FilePath = filePath;
            IsModified = false;
            StatusText = $"Saved: {Path.GetFileName(filePath)}";
            Log.Info("PDF saved: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save PDF: {FilePath}", filePath);
            StatusText = $"Error saving: {ex.Message}";
        }
    }

    #endregion

    #region Page Operations

    private void RecordAndApply(string description, byte[] newBytes)
    {
        if (_pdfBytes == null) return;
        _undoRedo.RecordAction(description, _pdfBytes, newBytes);
        _pdfBytes = newBytes;
        IsModified = true;
        RaiseUndoRedoChanged();
        Log.Debug("Recorded action: {Description} (undo stack: {UndoCount})", description, _undoRedo.UndoCount);
    }

    private void RaiseUndoRedoChanged()
    {
        this.RaisePropertyChanged(nameof(CanUndo));
        this.RaisePropertyChanged(nameof(CanRedo));
    }

    private async Task ExecuteDeletePageAsync()
    {
        await RunLockedAsync("DeletePage", async () =>
        {
            if (_pdfBytes == null || _pageCount <= 1) return;

            // Multi-select aware: delete all selected pages, or just the current one
            var pagesToDelete = _selectedPageIndices.Count > 1
                ? _selectedPageIndices.Select(i => i + 1).ToArray()
                : new[] { _currentPageIndex + 1 };

            // Don't delete all pages
            if (pagesToDelete.Length >= _pageCount)
            {
                await OnUIThread(() => StatusText = "Cannot delete all pages");
                return;
            }

            var localBytes = _pdfBytes;
            var newBytes = await Task.Run(() => _pdfOps.DeletePages(localBytes, pagesToDelete));

            await OnUIThread(() =>
            {
                RecordAndApply($"Delete {pagesToDelete.Length} page(s)", newBytes);
                PageCount = _renderService.GetPageCount(_pdfBytes);
                if (_currentPageIndex >= PageCount)
                {
                    _currentPageIndex = PageCount - 1;
                    this.RaisePropertyChanged(nameof(CurrentPageIndex));
                }
                _selectedThumbnailIndex = _currentPageIndex;
                this.RaisePropertyChanged(nameof(SelectedThumbnailIndex));
                RenderCurrentPage();
                LoadThumbnails();
                UpdatePageInfo();
                StatusText = $"Deleted {pagesToDelete.Length} page(s)";
            });
        });
    }

    private async Task ExecuteRotatePageAsync(int degrees)
    {
        await RunLockedAsync($"RotatePage({degrees})", async () =>
        {
            if (_pdfBytes == null) return;

            // Multi-select aware: rotate all selected pages
            var pagesToRotate = _selectedPageIndices.Count > 1
                ? _selectedPageIndices.Select(i => i + 1).ToArray()
                : new[] { _currentPageIndex + 1 };

            var localBytes = _pdfBytes;
            var newBytes = await Task.Run(() => _pdfOps.RotatePages(localBytes, pagesToRotate, degrees));

            await OnUIThread(() =>
            {
                RecordAndApply($"Rotate {pagesToRotate.Length} page(s) by {degrees}°", newBytes);
                RenderCurrentPage();
                LoadThumbnails();
                StatusText = $"Rotated {pagesToRotate.Length} page(s) by {degrees}°";
            });
        });
    }

    private async Task ExecuteMovePageUpAsync()
    {
        await RunLockedAsync("MovePageUp", async () =>
        {
            if (_pdfBytes == null || _currentPageIndex <= 0) return;

            var localBytes = _pdfBytes;
            int fromIdx = _currentPageIndex;
            var newBytes = await Task.Run(() => _splitService.MovePage(localBytes, fromIdx, fromIdx - 1));

            await OnUIThread(() =>
            {
                int oldPage = _currentPageIndex + 1;
                RecordAndApply($"Move page {oldPage} up", newBytes);
                _currentPageIndex--;
                this.RaisePropertyChanged(nameof(CurrentPageIndex));
                _selectedThumbnailIndex = _currentPageIndex;
                this.RaisePropertyChanged(nameof(SelectedThumbnailIndex));
                RenderCurrentPage();
                LoadThumbnails();
                UpdatePageInfo();
                StatusText = $"Moved page {oldPage} up";
            });
        });
    }

    private async Task ExecuteMovePageDownAsync()
    {
        await RunLockedAsync("MovePageDown", async () =>
        {
            if (_pdfBytes == null || _currentPageIndex >= _pageCount - 1) return;

            var localBytes = _pdfBytes;
            int fromIdx = _currentPageIndex;
            var newBytes = await Task.Run(() => _splitService.MovePage(localBytes, fromIdx, fromIdx + 1));

            await OnUIThread(() =>
            {
                int oldPage = _currentPageIndex + 1;
                RecordAndApply($"Move page {oldPage} down", newBytes);
                _currentPageIndex++;
                this.RaisePropertyChanged(nameof(CurrentPageIndex));
                _selectedThumbnailIndex = _currentPageIndex;
                this.RaisePropertyChanged(nameof(SelectedThumbnailIndex));
                RenderCurrentPage();
                LoadThumbnails();
                UpdatePageInfo();
                StatusText = $"Moved page {oldPage} down";
            });
        });
    }

    public async Task MergeWithAsync(string otherFilePath)
    {
        await RunLockedAsync("Merge", async () =>
        {
            if (_pdfBytes == null) return;

            Log.Info("Merging with: {FilePath}", otherFilePath);
            var otherBytes = await Task.Run(() => File.ReadAllBytes(otherFilePath));
            var localBytes = _pdfBytes;
            var newBytes = await Task.Run(() => _pdfOps.MergeDocuments(localBytes, otherBytes));

            await OnUIThread(() =>
            {
                RecordAndApply($"Merge with {Path.GetFileName(otherFilePath)}", newBytes);
                PageCount = _renderService.GetPageCount(_pdfBytes);
                RenderCurrentPage();
                LoadThumbnails();
                UpdatePageInfo();
                StatusText = $"Merged — now {PageCount} pages";
            });
        });
    }

    public void MergeWith(string otherFilePath) => _ = MergeWithAsync(otherFilePath);

    public byte[]? ExtractPageRange(int startPage, int endPage)
    {
        if (_pdfBytes == null) return null;
        try
        {
            Log.Info("Extracting pages {Start}-{End}", startPage, endPage);
            return _splitService.ExtractPages(_pdfBytes, startPage, endPage);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to extract pages {Start}-{End}", startPage, endPage);
            StatusText = $"Error: {ex.Message}";
            return null;
        }
    }

    public byte[]? ExtractSpecificPages(int[] pageNumbers)
    {
        if (_pdfBytes == null) return null;
        try
        {
            Log.Info("Extracting {Count} specific pages", pageNumbers.Length);
            return _splitService.ExtractSpecificPages(_pdfBytes, pageNumbers);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to extract specific pages");
            StatusText = $"Error: {ex.Message}";
            return null;
        }
    }

    public List<byte[]>? SplitAll()
    {
        if (_pdfBytes == null) return null;
        try
        {
            Log.Info("Splitting into individual pages");
            return _splitService.SplitAll(_pdfBytes);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to split document");
            StatusText = $"Error: {ex.Message}";
            return null;
        }
    }

    public async Task ApplyWatermarkAsync(string text, float fontSize, float opacity)
    {
        await RunLockedAsync("Watermark", async () =>
        {
            if (_pdfBytes == null) return;

            Log.Info("Applying watermark: {Text}", text);
            var localBytes = _pdfBytes;
            var newBytes = await Task.Run(() => _watermarkService.AddTextWatermark(localBytes, text, fontSize, opacity));

            await OnUIThread(() =>
            {
                RecordAndApply($"Watermark: {text}", newBytes);
                RenderCurrentPage();
                LoadThumbnails();
                StatusText = $"Watermark applied: \"{text}\"";
            });
        });
    }

    public void ApplyWatermark(string text, float fontSize, float opacity) => _ = ApplyWatermarkAsync(text, fontSize, opacity);

    public async Task ApplyPageNumbersAsync(string format)
    {
        await RunLockedAsync("PageNumbers", async () =>
        {
            if (_pdfBytes == null) return;

            Log.Info("Adding page numbers with format: {Format}", format);
            var localBytes = _pdfBytes;
            var newBytes = await Task.Run(() => _watermarkService.AddPageNumbers(localBytes, format));

            await OnUIThread(() =>
            {
                RecordAndApply("Add page numbers", newBytes);
                RenderCurrentPage();
                LoadThumbnails();
                StatusText = "Page numbers added";
            });
        });
    }

    public void ApplyPageNumbers(string format) => _ = ApplyPageNumbersAsync(format);

    public async Task EncryptDocumentAsync(string? userPassword, string ownerPassword,
        bool allowPrint, bool allowCopy, bool allowEdit)
    {
        await RunLockedAsync("Encrypt", async () =>
        {
            if (_pdfBytes == null) return;

            Log.Info("Encrypting document");
            var localBytes = _pdfBytes;
            var newBytes = await Task.Run(() => _securityService.Encrypt(
                localBytes, userPassword, ownerPassword, allowPrint, allowCopy, allowEdit));

            await OnUIThread(() =>
            {
                RecordAndApply("Encrypt document", newBytes);
                StatusText = "Document encrypted";
            });
        });
    }

    public void EncryptDocument(string? userPassword, string ownerPassword,
        bool allowPrint, bool allowCopy, bool allowEdit) =>
        _ = EncryptDocumentAsync(userPassword, ownerPassword, allowPrint, allowCopy, allowEdit);

    public string ExtractCurrentPageText()
    {
        if (_pdfBytes == null) return string.Empty;
        try { return _pdfOps.ExtractText(_pdfBytes, _currentPageIndex + 1); }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to extract text from page {Page}", _currentPageIndex + 1);
            return string.Empty;
        }
    }

    #endregion

    #region Undo/Redo

    private async Task ExecuteUndoAsync()
    {
        await RunLockedAsync("Undo", async () =>
        {
            await Task.CompletedTask;
            await OnUIThread(() =>
            {
                var state = _undoRedo.Undo();
                if (state != null)
                {
                    Log.Info("Undo: {Description}", _undoRedo.RedoDescription);
                    _pdfBytes = state;
                    RefreshAfterUndoRedo();
                    StatusText = $"Undo: {_undoRedo.RedoDescription}";
                }
                else
                {
                    Log.Debug("Undo attempted but nothing to undo");
                }
            });
        });
    }

    private async Task ExecuteRedoAsync()
    {
        await RunLockedAsync("Redo", async () =>
        {
            await Task.CompletedTask;
            await OnUIThread(() =>
            {
                var state = _undoRedo.Redo();
                if (state != null)
                {
                    Log.Info("Redo: {Description}", _undoRedo.UndoDescription);
                    _pdfBytes = state;
                    RefreshAfterUndoRedo();
                    StatusText = $"Redo: {_undoRedo.UndoDescription}";
                }
                else
                {
                    Log.Debug("Redo attempted but nothing to redo");
                }
            });
        });
    }

    private void RefreshAfterUndoRedo()
    {
        if (_pdfBytes == null) return;
        PageCount = _renderService.GetPageCount(_pdfBytes);
        if (_currentPageIndex >= PageCount)
        {
            _currentPageIndex = PageCount - 1;
            this.RaisePropertyChanged(nameof(CurrentPageIndex));
        }
        _selectedThumbnailIndex = _currentPageIndex;
        this.RaisePropertyChanged(nameof(SelectedThumbnailIndex));
        LoadMetadata();
        RenderCurrentPage();
        LoadThumbnails();
        UpdatePageInfo();
        IsModified = true;
        RaiseUndoRedoChanged();
    }

    #endregion

    #region Search

    public void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
        if (!IsSearchVisible)
        {
            SearchResults.Clear();
            SearchQuery = string.Empty;
        }
    }

    private async Task ExecuteSearchAsync()
    {
        if (_pdfBytes == null || string.IsNullOrWhiteSpace(_searchQuery)) return;
        try
        {
            Log.Info("Searching for: {Query}", _searchQuery);
            var localBytes = _pdfBytes;
            var query = _searchQuery;

            var results = await Task.Run(() => _searchService.Search(localBytes, query));

            await OnUIThread(() =>
            {
                SearchResults.Clear();
                foreach (var r in results)
                {
                    SearchResults.Add(new SearchResultItem
                    {
                        PageNumber = r.PageNumber,
                        DisplayText = r.DisplayText
                    });
                }
                _currentSearchResultIndex = results.Count > 0 ? 0 : -1;
                if (results.Count > 0) GoToPage(results[0].PageNumber);
                StatusText = $"Found {results.Count} result(s) for \"{_searchQuery}\"";
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Search failed for query: {Query}", _searchQuery);
            await OnUIThread(() => StatusText = $"Search error: {ex.Message}");
        }
    }

    private void GoNextSearchResult()
    {
        if (SearchResults.Count == 0) return;
        _currentSearchResultIndex = (_currentSearchResultIndex + 1) % SearchResults.Count;
        GoToPage(SearchResults[_currentSearchResultIndex].PageNumber);
    }

    private void GoPrevSearchResult()
    {
        if (SearchResults.Count == 0) return;
        _currentSearchResultIndex = (_currentSearchResultIndex - 1 + SearchResults.Count) % SearchResults.Count;
        GoToPage(SearchResults[_currentSearchResultIndex].PageNumber);
    }

    #endregion

    #region Rendering

    private void RenderCurrentPage()
    {
        if (_pdfBytes == null || _pageCount == 0) return;
        try
        {
            int maxW = (int)(900 * _zoomLevel);
            int maxH = (int)(1200 * _zoomLevel);
            var (pixels, width, height) = _renderService.RenderPage(_pdfBytes, _currentPageIndex, maxW, maxH);
            CurrentPageImage = BitmapHelper.CreateBitmapFromBgra(pixels, width, height);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Render failed for page {Page}", _currentPageIndex + 1);
            StatusText = $"Render error: {ex.Message}";
        }
    }

    private void LoadThumbnails()
    {
        if (_pdfBytes == null) return;
        Thumbnails.Clear();
        for (int i = 0; i < _pageCount; i++)
        {
            try
            {
                var (pixels, width, height) = _renderService.RenderThumbnail(_pdfBytes, i);
                Thumbnails.Add(new ThumbnailItem
                {
                    PageNumber = i + 1,
                    Image = BitmapHelper.CreateBitmapFromBgra(pixels, width, height)
                });
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to render thumbnail for page {Page}", i + 1);
                Thumbnails.Add(new ThumbnailItem { PageNumber = i + 1 });
            }
        }
    }

    private void UpdateThumbnail(int pageIndex)
    {
        if (_pdfBytes == null || pageIndex < 0 || pageIndex >= Thumbnails.Count) return;
        try
        {
            var (pixels, width, height) = _renderService.RenderThumbnail(_pdfBytes, pageIndex);
            Thumbnails[pageIndex].Image = BitmapHelper.CreateBitmapFromBgra(pixels, width, height);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to update thumbnail for page {Page}", pageIndex + 1);
        }
    }

    private void LoadMetadata()
    {
        if (_pdfBytes == null) return;
        try
        {
            var (title, author, subject) = _pdfOps.GetMetadata(_pdfBytes);
            _title = title ?? string.Empty;
            _author = author ?? string.Empty;
            _subject = subject ?? string.Empty;
            this.RaisePropertyChanged(nameof(Title));
            this.RaisePropertyChanged(nameof(Author));
            this.RaisePropertyChanged(nameof(Subject));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to load metadata");
        }
    }

    private void UpdatePageInfo()
    {
        PageInfoText = IsDocumentLoaded ? $"Page {_currentPageIndex + 1} of {_pageCount}" : "No document";
    }

    #endregion
}

/// <summary>
/// Creates Avalonia Bitmaps from raw BGRA pixel data
/// </summary>
internal static class BitmapHelper
{
    public static Bitmap CreateBitmapFromBgra(byte[] bgraPixels, int width, int height)
    {
        int stride = ((width * 3 + 3) / 4) * 4;
        int imageSize = stride * height;
        byte[] bgrPixels = new byte[imageSize];

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int srcIdx = (y * width + x) * 4;
                int dstIdx = y * stride + x * 3;
                bgrPixels[dstIdx] = bgraPixels[srcIdx];
                bgrPixels[dstIdx + 1] = bgraPixels[srcIdx + 1];
                bgrPixels[dstIdx + 2] = bgraPixels[srcIdx + 2];
            }

        const int headerSize = 54;
        int fileSize = headerSize + imageSize;
        var ms = new MemoryStream(fileSize);
        var bw = new BinaryWriter(ms);

        bw.Write((byte)0x42); bw.Write((byte)0x4D);
        bw.Write(fileSize); bw.Write(0); bw.Write(headerSize);
        bw.Write(40); bw.Write(width); bw.Write(-height);
        bw.Write((short)1); bw.Write((short)24);
        bw.Write(0); bw.Write(imageSize);
        bw.Write(2835); bw.Write(2835);
        bw.Write(0); bw.Write(0);
        bw.Write(bgrPixels, 0, imageSize);
        bw.Flush();

        ms.Position = 0;
        return new Bitmap(ms);
    }
}
