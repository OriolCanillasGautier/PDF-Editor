using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using IOPath = System.IO.Path;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NLog;
using PDFEditor.Core.Abstractions;
using PDFEditor.Core.Services;
using PDFEditor.UI.ViewModels;

namespace PDFEditor.UI;

public partial class MainWindow : Window
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private MainViewModel Vm => (MainViewModel)DataContext!;
    private DocumentTabViewModel? Tab => Vm?.ActiveTab;

    // Drag-drop reorder state
    private bool _isDragging;
    private Point _dragStartPoint;
    private int _dragSourceIndex = -1;
    private const double DragThreshold = 10;

    // Annotation drawing state
    private bool _isDrawingAnnotation;
    private Point _annotStartPoint;
    private PdfAnnotation? _currentAnnotation;
    private Rectangle? _previewRect;
    private Line? _previewLine;
    private Polyline? _previewPolyline;

    // Inline text editing state
    private TextBox? _inlineTextBox;
    private PdfAnnotation? _editingAnnotation;
    private bool _isEditingInline;

    // Annotation selection & drag state
    private PdfAnnotation? _selectedAnnotation;
    private bool _isDraggingAnnotation;
    private Point _annotDragStart;
    private double _annotOrigX, _annotOrigY;

    // Current formatting state (used by toolbar)
    private float _currentFontSize = 14f;
    private string _currentFontColor = "#000000";
    private bool _currentBold;
    private bool _currentItalic;

    // Continuous-scroll: active annotation canvas (set when user presses on a per-page canvas)
    private Canvas? _activeAnnotCanvas;
    // Guards to prevent thumbnail↔scroll feedback loops
    private bool _isProgrammaticScroll;
    private bool _suppressThumbnailNav;

    public MainWindow()
    {
        InitializeComponent();
        Log.Info("MainWindow initialized");

        // Enable file drop
        AddHandler(DragDrop.DropEvent, OnDragDropFile);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DragDrop.SetAllowDrop(this, true);

        // Session restore on load, session save on close
        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        try
        {
            // Apply saved theme
            if (Vm.IsDarkTheme && Application.Current != null)
                Application.Current.RequestedThemeVariant = ThemeVariant.Dark;

            // Restore previously open files
            Vm.RestoreSession();

            // Build the recent files menu
            RefreshRecentFilesMenu();

            Log.Info("Session restored on window open");
        }
        catch (Exception ex) { Log.Error(ex, "Session restore on open failed"); }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            Vm.SaveSession();
            Log.Info("Session saved on window close");
        }
        catch (Exception ex) { Log.Error(ex, "Session save on close failed"); }
    }

    private void RefreshRecentFilesMenu()
    {
        var menu = this.FindControl<MenuItem>("RecentFilesMenu");
        if (menu == null) return;

        menu.Items.Clear();

        if (Vm.RecentFiles.Count == 0)
        {
            var empty = new MenuItem { Header = "(no recent files)", IsEnabled = false };
            menu.Items.Add(empty);
        }
        else
        {
            foreach (var filePath in Vm.RecentFiles)
            {
                var item = new MenuItem { Header = IOPath.GetFileName(filePath), Tag = filePath };
                item.Click += OnRecentFileClick;
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var clearItem = new MenuItem { Header = "Clear Recent Files" };
            clearItem.Click += (_, _) =>
            {
                Vm.RecentFiles.Clear();
                RefreshRecentFilesMenu();
            };
            menu.Items.Add(clearItem);
        }
    }

    private void OnRecentFileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string filePath)
        {
            if (File.Exists(filePath))
            {
                Vm.OpenFile(filePath);
                RefreshRecentFilesMenu();
            }
            else
            {
                if (Tab != null) Tab.StatusText = $"File not found: {filePath}";
            }
        }
    }

    #region Keyboard Shortcuts (fixes Ctrl+Z not working via InputGesture binding)

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        var mod = e.KeyModifiers;
        var key = e.Key;

        // Ctrl+key shortcuts
        if (mod == KeyModifiers.Control)
        {
            switch (key)
            {
                case Key.Z:
                    if (Tab?.CanUndo == true && !Tab.IsBusy) ExecuteTabCommand(() => Tab.UndoCommand.Execute().Subscribe());
                    e.Handled = true;
                    return;
                case Key.Y:
                    if (Tab?.CanRedo == true && !Tab.IsBusy) ExecuteTabCommand(() => Tab.RedoCommand.Execute().Subscribe());
                    e.Handled = true;
                    return;
                case Key.O:
                    OnOpenClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.S:
                    OnSaveClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.P:
                    OnPrintClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.F:
                    OnSearchClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.G:
                    OnGoToPageClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.W:
                    OnCloseTabClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.A:
                    OnSelectAllClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.V:
                    OnClipboardPaste();
                    e.Handled = true;
                    return;
            }
        }
        else if (mod == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            if (key == Key.S)
            {
                OnSaveAsClick(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
        }
        else if (mod == KeyModifiers.None)
        {
            switch (key)
            {
                case Key.Home:
                    OnFirstPageClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.End:
                    OnLastPageClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.Left:
                case Key.PageUp:
                    OnPrevPageClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.Right:
                case Key.PageDown:
                    OnNextPageClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.Delete:
                    // Delete selected annotation first, if any
                    if (_selectedAnnotation != null && Tab?.IsAnnotationMode == true)
                    {
                        Tab.RemoveAnnotation(_selectedAnnotation.Id);
                        _selectedAnnotation = null;
                        RenderAnnotationsOnCanvas();
                        e.Handled = true;
                        return;
                    }
                    if (Tab is { IsDocumentLoaded: true, IsBusy: false, PageCount: > 1 })
                        ExecuteTabCommand(() => Tab.DeletePageCommand.Execute().Subscribe());
                    e.Handled = true;
                    return;
                case Key.Escape:
                    // Cancel inline text editing first
                    if (_isEditingInline)
                    {
                        _isEditingInline = false;
                        _editingAnnotation = null;
                        var annotCanvas = GetAnnotationElements().canvas;
                        if (annotCanvas != null && _inlineTextBox != null)
                        {
                            annotCanvas.Children.Remove(_inlineTextBox);
                            _inlineTextBox = null;
                        }
                        e.Handled = true;
                        return;
                    }
                    // Deselect annotation
                    if (_selectedAnnotation != null)
                    {
                        _selectedAnnotation = null;
                        RenderAnnotationsOnCanvas();
                        e.Handled = true;
                        return;
                    }
                    if (Tab?.IsSearchVisible == true) Tab.ToggleSearch();
                    if (Tab?.IsAnnotationMode == true) Tab.IsAnnotationMode = false;
                    e.Handled = true;
                    return;
            }
        }
    }

    private void ExecuteTabCommand(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Error(ex, "Error executing tab command"); }
    }

    #endregion

    #region File / Tab Operations

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open PDF",
                AllowMultiple = true,
                FileTypeFilter = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } }
            });

            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    Vm.OpenFile(path);
            }
            RefreshRecentFilesMenu();
        }
        catch (Exception ex) { Log.Error(ex, "Open failed"); }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null) return;
        if (!string.IsNullOrEmpty(Tab.FilePath))
        {
            Tab.SavePdf(Tab.FilePath);
        }
        else
        {
            await SaveAsAsync();
        }
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e) => await SaveAsAsync();

    private async System.Threading.Tasks.Task SaveAsAsync()
    {
        if (Tab == null) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save PDF As",
                DefaultExtension = "pdf",
                FileTypeChoices = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } },
                SuggestedFileName = IOPath.GetFileName(Tab.FilePath ?? "document.pdf")
            });
            if (file != null)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)) Tab.SavePdf(path);
            }
        }
        catch (Exception ex) { Log.Error(ex, "Save As failed"); }
    }

    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        Vm?.CloseActiveTab();
    }

    private void OnCloseSpecificTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DocumentTabViewModel tab)
            Vm?.CloseTab(tab);
    }

    private void OnCloseOtherTabsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is DocumentTabViewModel tab && Vm != null)
        {
            var others = Vm.Tabs.Where(t => t != tab).ToList();
            foreach (var t in others) Vm.CloseTab(t);
        }
    }

    private void OnCloseAllTabsClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var all = Vm.Tabs.ToList();
        foreach (var t in all) Vm.CloseTab(t);
    }

    private void OnNewTabClick(object? sender, RoutedEventArgs e) => Vm?.NewTabCommand.Execute().Subscribe();

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    #endregion

    #region Edit Operations

    private void OnUndoClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.CanUndo == true && !Tab.IsBusy)
            ExecuteTabCommand(() => Tab.UndoCommand.Execute().Subscribe());
    }

    private void OnRedoClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.CanRedo == true && !Tab.IsBusy)
            ExecuteTabCommand(() => Tab.RedoCommand.Execute().Subscribe());
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        var lb = this.FindControl<ListBox>("ThumbListBox");
        if (lb != null) lb.SelectAll();
    }

    private void OnContextDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (Tab is { IsDocumentLoaded: true, IsBusy: false, PageCount: > 1 })
            ExecuteTabCommand(() => Tab.DeletePageCommand.Execute().Subscribe());
    }

    private void OnContextRotateRightClick(object? sender, RoutedEventArgs e)
    {
        if (Tab is { IsDocumentLoaded: true, IsBusy: false })
            ExecuteTabCommand(() => Tab.RotateRightCommand.Execute().Subscribe());
    }

    private void OnContextRotateLeftClick(object? sender, RoutedEventArgs e)
    {
        if (Tab is { IsDocumentLoaded: true, IsBusy: false })
            ExecuteTabCommand(() => Tab.RotateLeftCommand.Execute().Subscribe());
    }

    private void OnContextMoveUpClick(object? sender, RoutedEventArgs e)
    {
        if (Tab is { IsDocumentLoaded: true, IsBusy: false })
            ExecuteTabCommand(() => Tab.MovePageUpCommand.Execute().Subscribe());
    }

    private void OnContextMoveDownClick(object? sender, RoutedEventArgs e)
    {
        if (Tab is { IsDocumentLoaded: true, IsBusy: false })
            ExecuteTabCommand(() => Tab.MovePageDownCommand.Execute().Subscribe());
    }

    #endregion

    #region Navigation

    /// <summary>Scrolls the continuous-scroll viewer to the given 0-based page index.</summary>
    private void ScrollToPage(int pageIndex)
    {
        if (Tab == null || pageIndex < 0 || pageIndex >= Tab.PageCount) return;
        var sv = this.FindControl<ScrollViewer>("PageScrollViewer");
        if (sv == null) return;
        _isProgrammaticScroll = true;
        try
        {
            sv.Offset = new Vector(sv.Offset.X, Tab.GetPageScrollOffset(pageIndex));
            Tab.SetCurrentPageSilent(pageIndex);
        }
        finally { _isProgrammaticScroll = false; }
        // Kick off render for pages now visible after the programmatic scroll
        Dispatcher.UIThread.Post(() =>
        {
            var sv2 = this.FindControl<ScrollViewer>("PageScrollViewer");
            if (sv2 != null) NotifyVisiblePagesFromScroller(sv2);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void OnFirstPageClick(object? sender, RoutedEventArgs e) => ScrollToPage(0);
    private void OnLastPageClick(object? sender, RoutedEventArgs e)  => ScrollToPage((Tab?.PageCount ?? 1) - 1);
    private void OnPrevPageClick(object? sender, RoutedEventArgs e)  => ScrollToPage((Tab?.CurrentPageIndex ?? 1) - 1);
    private void OnNextPageClick(object? sender, RoutedEventArgs e)  => ScrollToPage((Tab?.CurrentPageIndex ?? 0) + 1);

    #endregion

    #region Continuous Scroll

    /// <summary>
    /// Fired whenever the PageScrollViewer scrolls (user scroll or programmatic).
    /// Updates <see cref="DocumentTabViewModel.CurrentPageIndex"/> and triggers lazy renders.
    /// </summary>
    private void OnPageScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isProgrammaticScroll) return;
        if (Tab == null || sender is not ScrollViewer sv) return;

        // Determine the center-most visible page
        int center = GetCenterVisiblePage(sv);
        if (center >= 0 && center != Tab.CurrentPageIndex)
        {
            _suppressThumbnailNav = true;
            Tab.SetCurrentPageSilent(center);
            _suppressThumbnailNav = false;
        }

        NotifyVisiblePagesFromScroller(sv);
    }

    /// <summary>
    /// Returns the page index that is most centered (or first fully visible) in the scroll viewport.
    /// </summary>
    private int GetCenterVisiblePage(ScrollViewer sv)
    {
        if (Tab == null || Tab.PageViews.Count == 0) return -1;
        double top    = sv.Offset.Y;
        double center = top + sv.Viewport.Height / 2.0;
        double y = 12.0; // top margin (matches Margin="16,12")
        for (int i = 0; i < Tab.PageViews.Count; i++)
        {
            double h = Tab.PageViews[i].IsRendered ? Tab.PageViews[i].RenderedHeight : 1100.0;
            double bottom = y + h;
            if (center >= y && center <= bottom) return i;
            if (y > center) return Math.Max(0, i - 1);
            y = bottom + 12.0; // spacing
        }
        return Tab.PageViews.Count - 1;
    }

    /// <summary>
    /// Determines the set of currently visible pages and asks the ViewModel to render them.
    /// </summary>
    private void NotifyVisiblePagesFromScroller(ScrollViewer sv)
    {
        if (Tab == null || Tab.PageViews.Count == 0) return;
        double top    = sv.Offset.Y;
        double bottom = top + sv.Viewport.Height;
        var visible = new List<int>();
        double y = 12.0;
        for (int i = 0; i < Tab.PageViews.Count; i++)
        {
            double h       = Tab.PageViews[i].IsRendered ? Tab.PageViews[i].RenderedHeight : 1100.0;
            double itemBot = y + h;
            if (itemBot > top && y < bottom) visible.Add(i);
            if (y > bottom) break;
            y = itemBot + 12.0;
        }
        Tab.NotifyVisiblePageViews(visible);
    }

    #endregion

    #region Zoom

    private void OnContextZoomInClick(object? sender, RoutedEventArgs e) => Tab?.ZoomInCommand.Execute().Subscribe();
    private void OnContextZoomOutClick(object? sender, RoutedEventArgs e) => Tab?.ZoomOutCommand.Execute().Subscribe();
    private void OnContextZoomFitClick(object? sender, RoutedEventArgs e) => Tab?.ZoomFitCommand.Execute().Subscribe();

    private void OnZoomSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (Tab != null && Math.Abs(Tab.ZoomLevel - e.NewValue) > 0.001)
        {
            Tab.ZoomLevel = e.NewValue;
            // Re-render visible pages at new zoom
            var sv = this.FindControl<ScrollViewer>("PageScrollViewer");
            if (sv != null) Dispatcher.UIThread.Post(
                () => NotifyVisiblePagesFromScroller(sv),
                Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    #endregion

    #region Theme

    private void OnToggleThemeClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        Vm.ToggleThemeCommand.Execute().Subscribe();
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = Vm.IsDarkTheme
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
        }
    }

    #endregion

    #region Search

    private void OnSearchClick(object? sender, RoutedEventArgs e) => Tab?.ToggleSearch();

    private void OnDoSearchClick(object? sender, RoutedEventArgs e)
    {
        if (Tab is { IsDocumentLoaded: true })
            ExecuteTabCommand(() => Tab.SearchCommand.Execute().Subscribe());
    }

    private void OnNextSearchClick(object? sender, RoutedEventArgs e) =>
        Tab?.NextSearchResultCommand.Execute().Subscribe();

    private void OnPrevSearchClick(object? sender, RoutedEventArgs e) =>
        Tab?.PrevSearchResultCommand.Execute().Subscribe();

    private void OnCloseSearchClick(object? sender, RoutedEventArgs e) => Tab?.ToggleSearch();

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Tab is { IsDocumentLoaded: true })
        {
            ExecuteTabCommand(() => Tab.SearchCommand.Execute().Subscribe());
            e.Handled = true;
        }
    }

    private void OnSearchResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is SearchResultItem item)
            Tab?.GoToPage(item.PageNumber);
    }

    #endregion

    #region Go to Page

    private async void OnGoToPageClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var dialog = new Window
            {
                Title = "Go to Page",
                Width = 300, Height = 140,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var input = new TextBox { Watermark = $"Page (1-{Tab.PageCount})", Margin = new Thickness(10) };
            var btn = new Button { Content = "Go", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(10) };

            btn.Click += (_, _) =>
            {
                if (int.TryParse(input.Text, out int page)) Tab.GoToPage(page);
                dialog.Close();
            };
            input.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter)
                {
                    if (int.TryParse(input.Text, out int page)) Tab.GoToPage(page);
                    dialog.Close();
                }
            };

            dialog.Content = new StackPanel { Children = { input, btn } };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "GoToPage dialog error"); }
    }

    #endregion

    #region Merge / Split / Extract

    private async void OnMergeClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select PDF(s) to Merge",
                AllowMultiple = true,
                FileTypeFilter = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } }
            });
            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)) await Tab.MergeWithAsync(path);
            }
        }
        catch (Exception ex) { Log.Error(ex, "Merge failed"); }
    }

    private async void OnSplitClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Output Folder for Split Pages"
            });
            if (folder.Count == 0) return;
            var path = folder[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            var pages = Tab.SplitAll();
            if (pages == null) return;
            for (int i = 0; i < pages.Count; i++)
            {
                var fn = IOPath.Combine(path, $"{IOPath.GetFileNameWithoutExtension(Tab.FilePath ?? "doc")}_page{i + 1}.pdf");
                File.WriteAllBytes(fn, pages[i]);
            }
            Tab.StatusText = $"Split into {pages.Count} files in {path}";
        }
        catch (Exception ex) { Log.Error(ex, "Split failed"); }
    }

    private async void OnExtractPagesClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        await ShowExtractDialog();
    }

    private async void OnExtractSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        if (Tab.SelectedPageIndices.Count < 1) return;

        try
        {
            var result = Tab.ExtractSpecificPages(Tab.SelectedPageIndices.Select(i => i + 1).ToArray());
            if (result == null) return;

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Extracted Pages",
                DefaultExtension = "pdf",
                FileTypeChoices = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } },
                SuggestedFileName = "extracted_pages.pdf"
            });
            if (file != null)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    File.WriteAllBytes(path, result);
                    Tab.StatusText = $"Extracted {Tab.SelectedPageIndices.Count} page(s)";
                }
            }
        }
        catch (Exception ex) { Log.Error(ex, "Extract selected failed"); }
    }

    private async System.Threading.Tasks.Task ShowExtractDialog()
    {
        try
        {
            var dialog = new Window
            {
                Title = "Extract Pages",
                Width = 350, Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var startInput = new TextBox { Watermark = "Start page", Margin = new Thickness(10, 10, 10, 5) };
            var endInput = new TextBox { Watermark = "End page", Margin = new Thickness(10, 5, 10, 5) };
            var btn = new Button { Content = "Extract", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(10) };

            btn.Click += async (_, _) =>
            {
                if (int.TryParse(startInput.Text, out int s) && int.TryParse(endInput.Text, out int en))
                {
                    var result = Tab!.ExtractPageRange(s, en);
                    if (result != null)
                    {
                        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                        {
                            Title = "Save Extracted", DefaultExtension = "pdf",
                            FileTypeChoices = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } },
                            SuggestedFileName = $"pages_{s}-{en}.pdf"
                        });
                        if (file != null)
                        {
                            var p = file.TryGetLocalPath();
                            if (!string.IsNullOrEmpty(p))
                            {
                                File.WriteAllBytes(p, result);
                                Tab.StatusText = $"Extracted pages {s}-{en}";
                            }
                        }
                    }
                }
                dialog.Close();
            };

            dialog.Content = new StackPanel { Children = { startInput, endInput, btn } };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "Extract dialog error"); }
    }

    #endregion

    #region Export

    private async void OnExportImagesClick(object? sender, RoutedEventArgs e)
    {
        if (Tab is not { IsDocumentLoaded: true, PdfBytes: not null }) return;
        try
        {
            var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Output Folder for Images"
            });
            if (folder.Count == 0) return;
            var path = folder[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            for (int i = 0; i < Tab.PageCount; i++)
            {
                var img = Tab.ExportService.ExportPageToImage(Tab.PdfBytes, i, "png", 200);
                var fn = IOPath.Combine(path, $"page_{i + 1}.png");
                File.WriteAllBytes(fn, img);
            }
            Tab.StatusText = $"Exported {Tab.PageCount} images to {path}";
        }
        catch (Exception ex) { Log.Error(ex, "Export images failed"); }
    }

    private async void OnExportCurrentPageClick(object? sender, RoutedEventArgs e)
    {
        if (Tab is not { IsDocumentLoaded: true, PdfBytes: not null }) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Page as Image",
                DefaultExtension = "png",
                FileTypeChoices = new[] { new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } } },
                SuggestedFileName = $"page_{Tab.CurrentPageIndex + 1}.png"
            });
            if (file != null)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    var img = Tab.ExportService.ExportPageToImage(Tab.PdfBytes, Tab.CurrentPageIndex, "png", 200);
                    File.WriteAllBytes(path, img);
                    Tab.StatusText = $"Exported page {Tab.CurrentPageIndex + 1} as image";
                }
            }
        }
        catch (Exception ex) { Log.Error(ex, "Export current page failed"); }
    }

    private async void OnExportTextClick(object? sender, RoutedEventArgs e)
    {
        if (Tab is not { IsDocumentLoaded: true, PdfBytes: not null }) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Text",
                DefaultExtension = "txt",
                FileTypeChoices = new[] { new FilePickerFileType("Text File") { Patterns = new[] { "*.txt" } } },
                SuggestedFileName = "extracted_text.txt"
            });
            if (file != null)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    var searchSvc = new PdfSearchService();
                    var text = searchSvc.ExtractAllText(Tab.PdfBytes);
                    File.WriteAllText(path, text);
                    Tab.StatusText = "Text exported";
                }
            }
        }
        catch (Exception ex) { Log.Error(ex, "Export text failed"); }
    }

    private async void OnExportHtmlClick(object? sender, RoutedEventArgs e)
    {
        if (Tab is not { IsDocumentLoaded: true, PdfBytes: not null }) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export to HTML",
                DefaultExtension = "html",
                FileTypeChoices = new[] { new FilePickerFileType("HTML File") { Patterns = new[] { "*.html" } } },
                SuggestedFileName = "document.html"
            });
            if (file != null)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    var html = Tab.ExportService.ExportToHtml(Tab.PdfBytes);
                    File.WriteAllText(path, html);
                    Tab.StatusText = "Exported to HTML";
                }
            }
        }
        catch (Exception ex) { Log.Error(ex, "Export HTML failed"); }
    }

    private async void OnExportDocxClick(object? sender, RoutedEventArgs e)
    {
        if (Tab is not { IsDocumentLoaded: true, PdfBytes: not null }) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export to DOCX",
                DefaultExtension = "docx",
                FileTypeChoices = new[] { new FilePickerFileType("Word Document") { Patterns = new[] { "*.docx" } } },
                SuggestedFileName = IOPath.GetFileNameWithoutExtension(Tab.FilePath ?? "document") + ".docx"
            });
            if (file != null)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    Tab.StatusText = "Exporting to DOCX...";
                    var provider = new PDFEditor.Core.Services.Export.DocxExportProvider();
                    var options = new PDFEditor.Core.Abstractions.ExportOptions
                    {
                        BaseFileName = IOPath.GetFileNameWithoutExtension(Tab.FilePath ?? "document"),
                        OutputFormat = "DOCX"
                    };
                    var result = await provider.ExportAsync(Tab.PdfBytes, options);
                    if (result.Success)
                    {
                        File.WriteAllBytes(path, result.Data);
                        Tab.StatusText = $"Exported to DOCX: {IOPath.GetFileName(path)}";
                    }
                    else
                    {
                        Tab.StatusText = $"Export failed: {result.ErrorMessage}";
                    }
                }
            }
        }
        catch (Exception ex) { Log.Error(ex, "Export DOCX failed"); Tab.StatusText = $"Export error: {ex.Message}"; }
    }

    private async void OnExportDialogClick(object? sender, RoutedEventArgs e)
    {
        if (Tab is not { IsDocumentLoaded: true, PdfBytes: not null }) return;
        try
        {
            var registry = PDFEditor.Core.Services.Export.ExportProviderRegistry.CreateDefault();
            var dialog = new Window
            {
                Title = "Export Document",
                Width = 480, Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var formatCombo = new ComboBox
            {
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(10, 5)
            };
            foreach (var p in registry.Providers)
                formatCombo.Items.Add(new ComboBoxItem { Content = p.FormatName, Tag = p });

            var dpiInput = new TextBox { Text = "150", Watermark = "DPI (e.g. 150)", Margin = new Thickness(10, 5) };
            var qualityInput = new TextBox { Text = "90", Watermark = "JPEG quality (1-100)", Margin = new Thickness(10, 5) };
            var pageRangeInput = new TextBox { Watermark = $"Page range (e.g. 1-{Tab.PageCount}) or leave blank for all", Margin = new Thickness(10, 5) };

            // Image-quality settings panel — only visible for raster image export formats
            var imageDpiPanel = new StackPanel();
            imageDpiPanel.Children.Add(new TextBlock
            {
                Text = "Image Settings",
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(10, 10, 10, 0)
            });
            imageDpiPanel.Children.Add(dpiInput);
            imageDpiPanel.Children.Add(qualityInput);

            static bool FormatUsesImageQuality(PDFEditor.Core.Abstractions.IExportProvider p)
            {
                var imgExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp" };
                return p.SupportedExtensions.Any(e => imgExts.Contains(e));
            }

            // Set initial panel visibility
            var firstProvider = registry.Providers.FirstOrDefault();
            imageDpiPanel.IsVisible = firstProvider != null && FormatUsesImageQuality(firstProvider);

            formatCombo.SelectionChanged += (_, _) =>
            {
                if (formatCombo.SelectedItem is ComboBoxItem { Tag: PDFEditor.Core.Abstractions.IExportProvider sp })
                    imageDpiPanel.IsVisible = FormatUsesImageQuality(sp);
            };

            var statusLabel = new TextBlock { Text = "", FontSize = 11, Opacity = 0.6, Margin = new Thickness(10, 5), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, IsVisible = false, Margin = new Thickness(10, 5) };

            var exportBtn = new Button
            {
                Content = "Export",
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(24, 8),
                Margin = new Thickness(10)
            };

            exportBtn.Click += async (_, _) =>
            {
                var selectedItem = formatCombo.SelectedItem as ComboBoxItem;
                if (selectedItem?.Tag is not PDFEditor.Core.Abstractions.IExportProvider provider) return;

                int.TryParse(dpiInput.Text, out int dpi);
                int.TryParse(qualityInput.Text, out int quality);
                if (dpi <= 0) dpi = 150;
                if (quality <= 0 || quality > 100) quality = 90;

                int[]? pageIndices = null;
                if (!string.IsNullOrWhiteSpace(pageRangeInput.Text))
                    pageIndices = ParsePageRange(pageRangeInput.Text, Tab.PageCount);

                var options = new PDFEditor.Core.Abstractions.ExportOptions
                {
                    Dpi = dpi,
                    Quality = quality,
                    PageIndices = pageIndices,
                    OutputFormat = provider.SupportedExtensions[0].TrimStart('.').ToUpperInvariant(),
                    BaseFileName = IOPath.GetFileNameWithoutExtension(Tab.FilePath ?? "document")
                };

                exportBtn.IsEnabled = false;
                progressBar.IsVisible = true;
                statusLabel.Text = "Exporting...";

                try
                {
                    if (provider.SupportsPerPageExport && (pageIndices == null || pageIndices.Length > 1))
                    {
                        // Per-page export: pick folder
                        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                        {
                            Title = "Select Output Folder"
                        });
                        if (folder.Count == 0) { exportBtn.IsEnabled = true; progressBar.IsVisible = false; statusLabel.Text = ""; return; }
                        var dirPath = folder[0].TryGetLocalPath();
                        if (string.IsNullOrEmpty(dirPath)) { exportBtn.IsEnabled = true; progressBar.IsVisible = false; return; }

                        var progress = new Progress<PDFEditor.Core.Abstractions.ExportProgress>(p =>
                        {
                            progressBar.Value = p.ProgressPercent;
                            statusLabel.Text = p.Message;
                        });

                        var results = await provider.ExportPagesAsync(Tab.PdfBytes, options, progress);
                        int successCount = 0;
                        foreach (var r in results)
                        {
                            if (r.Success)
                            {
                                File.WriteAllBytes(IOPath.Combine(dirPath, r.FileName), r.Data);
                                successCount++;
                            }
                        }
                        statusLabel.Text = $"Exported {successCount} file(s) to {dirPath}";
                        Tab.StatusText = statusLabel.Text;
                    }
                    else
                    {
                        // Single-file export: pick file
                        var ext = provider.SupportedExtensions[0];
                        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                        {
                            Title = "Export",
                            DefaultExtension = ext.TrimStart('.'),
                            FileTypeChoices = new[] { new FilePickerFileType(provider.FormatName) { Patterns = new[] { $"*{ext}" } } },
                            SuggestedFileName = $"{options.BaseFileName}{ext}"
                        });
                        if (file != null)
                        {
                            var path = file.TryGetLocalPath();
                            if (!string.IsNullOrEmpty(path))
                            {
                                var result = await provider.ExportAsync(Tab.PdfBytes, options);
                                if (result.Success)
                                {
                                    File.WriteAllBytes(path, result.Data);
                                    statusLabel.Text = $"Exported: {IOPath.GetFileName(path)}";
                                    Tab.StatusText = statusLabel.Text;
                                }
                                else
                                {
                                    statusLabel.Text = $"Failed: {result.ErrorMessage}";
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Export dialog error");
                    statusLabel.Text = $"Error: {ex.Message}";
                }
                finally
                {
                    exportBtn.IsEnabled = true;
                    progressBar.IsVisible = false;
                }
            };

            dialog.Content = new StackPanel
            {
                Spacing = 2,
                Margin = new Thickness(10),
                Children =
                {
                    new TextBlock { Text = "Export Format", FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 10, 10, 0) },
                    formatCombo,
                    imageDpiPanel,
                    new TextBlock { Text = "Page Range", FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 10, 10, 0) },
                    pageRangeInput,
                    new Separator { Margin = new Thickness(10, 8) },
                    progressBar,
                    statusLabel,
                    exportBtn
                }
            };

            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "Export dialog failed"); }
    }

    private static int[]? ParsePageRange(string input, int maxPages)
    {
        var pages = new List<int>();
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                var range = part.Split('-');
                if (range.Length == 2 && int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end))
                {
                    start = Math.Max(1, start);
                    end = Math.Min(maxPages, end);
                    for (int i = start; i <= end; i++)
                        pages.Add(i - 1); // convert to 0-based
                }
            }
            else if (int.TryParse(part, out int page) && page >= 1 && page <= maxPages)
            {
                pages.Add(page - 1); // convert to 0-based
            }
        }
        return pages.Count > 0 ? pages.Distinct().OrderBy(p => p).ToArray() : null;
    }

    #endregion

    #region Extract Text View

    private void OnExtractTextViewClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        var text = Tab.ExtractCurrentPageText();
        if (string.IsNullOrWhiteSpace(text))
        {
            Tab.StatusText = "No text found on this page";
            return;
        }

        var win = new Window
        {
            Title = $"Text - Page {Tab.CurrentPageIndex + 1}",
            Width = 600, Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Thickness(8)
            }
        };
        win.Show(this);
    }

    #endregion

    #region Watermark / Page Numbers / Password

    private async void OnWatermarkClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var dialog = new Window
            {
                Title = "Add Watermark",
                Width = 350, Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var textInput = new TextBox { Watermark = "Watermark text", Margin = new Thickness(10) };
            var sizeInput = new TextBox { Watermark = "Font size (default 40)", Margin = new Thickness(10, 0, 10, 10) };
            var opacityInput = new TextBox { Watermark = "Opacity 0-1 (default 0.3)", Margin = new Thickness(10, 0, 10, 10) };
            var btn = new Button { Content = "Apply Watermark", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(10) };

            btn.Click += async (_, _) =>
            {
                float sz = 40f; float op = 0.3f;
                if (!string.IsNullOrEmpty(sizeInput.Text)) float.TryParse(sizeInput.Text, out sz);
                if (!string.IsNullOrEmpty(opacityInput.Text)) float.TryParse(opacityInput.Text, out op);
                await Tab.ApplyWatermarkAsync(textInput.Text ?? "WATERMARK", sz, op);
                dialog.Close();
            };

            dialog.Content = new StackPanel { Children = { textInput, sizeInput, opacityInput, btn } };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "Watermark dialog error"); }
    }

    private async void OnPageNumbersClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var dialog = new Window
            {
                Title = "Add Page Numbers",
                Width = 350, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var input = new TextBox
            {
                Text = "Page {0} of {1}",
                Watermark = "Format: Page {0} of {1}",
                Margin = new Thickness(10)
            };
            var btn = new Button { Content = "Apply", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(10) };

            btn.Click += async (_, _) =>
            {
                await Tab.ApplyPageNumbersAsync(input.Text ?? "Page {0} of {1}");
                dialog.Close();
            };

            dialog.Content = new StackPanel { Children = { input, btn } };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "Page numbers dialog error"); }
    }

    private async void OnPasswordProtectClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var dialog = new Window
            {
                Title = "Password Protect",
                Width = 380, Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var userPwInput = new TextBox { Watermark = "User password (for opening, optional)", Margin = new Thickness(10, 10, 10, 5) };
            var ownerPwInput = new TextBox { Watermark = "Owner password (required)", Margin = new Thickness(10, 5, 10, 5) };
            var printCb = new CheckBox { Content = "Allow Printing", IsChecked = true, Margin = new Thickness(10, 5) };
            var copyCb = new CheckBox { Content = "Allow Copying", IsChecked = true, Margin = new Thickness(10, 0) };
            var editCb = new CheckBox { Content = "Allow Editing", IsChecked = false, Margin = new Thickness(10, 0) };
            var btn = new Button { Content = "Encrypt", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(10) };

            btn.Click += async (_, _) =>
            {
                var ownerPw = ownerPwInput.Text;
                if (string.IsNullOrWhiteSpace(ownerPw))
                {
                    Tab.StatusText = "Owner password is required";
                    return;
                }
                await Tab.EncryptDocumentAsync(
                    string.IsNullOrWhiteSpace(userPwInput.Text) ? null : userPwInput.Text,
                    ownerPw,
                    printCb.IsChecked == true,
                    copyCb.IsChecked == true,
                    editCb.IsChecked == true);
                dialog.Close();
            };

            dialog.Content = new StackPanel { Children = { userPwInput, ownerPwInput, printCb, copyCb, editCb, btn } };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "Password protect dialog error"); }
    }

    #endregion

    #region Print

    private async void OnPrintClick(object? sender, RoutedEventArgs e)
    {
        if (Tab is not { IsDocumentLoaded: true, PdfBytes: not null }) return;
        try
        {
            // Save to temp file and open with system default PDF handler for printing
            var tmpFile = IOPath.Combine(IOPath.GetTempPath(), $"pdfeditor_print_{Guid.NewGuid():N}.pdf");
            File.WriteAllBytes(tmpFile, Tab.PdfBytes);
            Tab.StatusText = "Opening PDF for printing...";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = tmpFile,
                UseShellExecute = true,
                Verb = "print"
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex) { Log.Error(ex, "Print failed"); Tab.StatusText = $"Print error: {ex.Message}"; }
    }

    #endregion

    #region Thumbnail Selection & Drag-Drop Reorder

    private void OnThumbnailSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressThumbnailNav) return;
        if (Tab == null) return;
        var lb = sender as ListBox;
        if (lb == null) return;

        var indices = new List<int>();
        foreach (var item in lb.SelectedItems)
        {
            var idx = lb.ItemsSource?.Cast<object>().ToList().IndexOf(item) ?? -1;
            if (idx >= 0) indices.Add(idx);
        }
        Tab.UpdateSelectedPages(indices);

        // Scroll the continuous viewer to the primary selected page
        if (lb.SelectedIndex >= 0)
            ScrollToPage(lb.SelectedIndex);
    }

    private void ThumbListBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        var lb = sender as ListBox;
        if (lb == null) return;

        // Only start drag tracking on left button press
        var props = e.GetCurrentPoint(lb).Properties;
        if (!props.IsLeftButtonPressed) return;

        var point = e.GetPosition(lb);
        _dragStartPoint = point;
        _isDragging = false;
        _dragSourceIndex = -1;

        // Find which thumbnail item was clicked
        var hit = lb.InputHitTest(point) as Visual;
        if (hit != null)
        {
            var lbi = hit.FindAncestorOfType<ListBoxItem>();
            if (lbi != null && lbi.DataContext != null)
            {
                var container = lb.ItemsSource?.Cast<object>().ToList();
                if (container != null)
                {
                    var itemIdx = container.IndexOf(lbi.DataContext);
                    if (itemIdx >= 0) _dragSourceIndex = itemIdx;
                }
            }
        }
    }

    private void ThumbListBox_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSourceIndex < 0 || Tab == null || !Tab.IsDocumentLoaded) return;
        var lb = sender as ListBox;
        if (lb == null) return;

        // Only track if left button is still held
        var props = e.GetCurrentPoint(lb).Properties;
        if (!props.IsLeftButtonPressed)
        {
            _dragSourceIndex = -1;
            _isDragging = false;
            lb.Cursor = Cursor.Default;
            return;
        }

        var point = e.GetPosition(lb);
        var delta = point - _dragStartPoint;

        if (!_isDragging && (Math.Abs(delta.X) > DragThreshold || Math.Abs(delta.Y) > DragThreshold))
        {
            _isDragging = true;
        }

        if (_isDragging)
        {
            lb.Cursor = new Cursor(StandardCursorType.DragMove);
        }
    }

    private async void ThumbListBox_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var lb = sender as ListBox;
        if (lb != null) lb.Cursor = Cursor.Default;

        if (!_isDragging || _dragSourceIndex < 0 || Tab == null)
        {
            _dragSourceIndex = -1;
            _isDragging = false;
            return;
        }

        try
        {
            // Find target index by hit-testing
            var point = e.GetPosition(lb!);
            var hit = lb!.InputHitTest(point) as Visual;
            int targetIdx = -1;

            if (hit != null)
            {
                var lbi = hit.FindAncestorOfType<ListBoxItem>();
                if (lbi != null)
                {
                    var container = lb.ItemsSource?.Cast<object>().ToList();
                    if (container != null)
                    {
                        targetIdx = container.IndexOf(lbi.DataContext!);
                    }
                }
            }

            if (targetIdx >= 0 && targetIdx != _dragSourceIndex)
            {
                await Tab.MovePageToAsync(_dragSourceIndex, targetIdx);
            }
        }
        catch (Exception ex) { Log.Error(ex, "Drag-drop reorder failed"); }
        finally
        {
            _dragSourceIndex = -1;
            _isDragging = false;
        }
    }

    #endregion

    #region Drag-Drop Files

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
    }

    private void OnDragDropFile(object? sender, DragEventArgs e)
    {
        try
        {
            var files = e.Data.GetFiles()?.ToList();
            if (files == null) return;

            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    Vm?.OpenFile(path);
                }
            }
        }
        catch (Exception ex) { Log.Error(ex, "File drop failed"); }
    }

    #endregion

    #region Annotation Tools

    private void OnToggleAnnotationMode(object? sender, RoutedEventArgs e)
    {
        if (Tab == null) return;
        Tab.IsAnnotationMode = !Tab.IsAnnotationMode;
        Tab.StatusText = Tab.IsAnnotationMode ? "Annotation mode ON" : "Annotation mode OFF";

        // Update button appearance
        var btn = this.FindControl<Button>("BtnAnnotMode");
        if (btn != null)
        {
            btn.FontWeight = Tab.IsAnnotationMode ? FontWeight.Bold : FontWeight.Normal;
        }
    }

    private void OnAnnotToolText(object? sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationType.Text);
    private void OnAnnotToolHighlight(object? sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationType.Highlight);
    private void OnAnnotToolRectangle(object? sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationType.Rectangle);
    private void OnAnnotToolEllipse(object? sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationType.Ellipse);
    private void OnAnnotToolArrow(object? sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationType.Arrow);
    private void OnAnnotToolFreehand(object? sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationType.FreehandDraw);
    private void OnAnnotToolBlur(object? sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationType.Blur);
    private void OnAnnotToolRedact(object? sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationType.Redact);

    private async void OnAnnotToolImage(object? sender, RoutedEventArgs e)
    {
        if (Tab == null) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Image",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif" } }
                }
            });
            if (files.Count > 0)
            {
                var path = files[0].TryGetLocalPath();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var imgData = File.ReadAllBytes(path);
                    Tab.ActiveAnnotationTool = AnnotationType.Image;
                    Tab.IsAnnotationMode = true;

                    // Create image annotation at center of page
                    var ann = new PdfAnnotation
                    {
                        Type = AnnotationType.Image,
                        X = 0.25, Y = 0.25,
                        Width = 0.5, Height = 0.5,
                        ImageData = imgData
                    };
                    Tab.AddAnnotation(ann);
                    RenderAnnotationsOnCanvas();
                }
            }
        }
        catch (Exception ex) { Log.Error(ex, "Add image annotation failed"); }
    }

    private void SetAnnotationTool(AnnotationType tool)
    {
        if (Tab == null) return;
        Tab.ActiveAnnotationTool = tool;
        Tab.IsAnnotationMode = true;
        Tab.StatusText = $"Annotation tool: {tool}";
    }

    private async void OnBurnAnnotations(object? sender, RoutedEventArgs e)
    {
        if (Tab == null) return;
        try
        {
            await Tab.BurnAnnotationsAsync();
            ClearAnnotationCanvas();
        }
        catch (Exception ex) { Log.Error(ex, "Burn annotations failed"); }
    }

    #endregion

    #region Annotation Canvas Drawing

    /// <summary>
    /// Returns the active annotation canvas + its sibling Image in page-view DataTemplate items.
    /// Falls back to the legacy named controls when the named canvas still exists.
    /// </summary>
    private (Canvas? canvas, Image? pageImage) GetAnnotationElements()
    {
        var canvas = _activeAnnotCanvas;
        Image? img = null;
        if (canvas?.Parent is Panel panel)
            img = panel.Children.OfType<Image>().FirstOrDefault();
        // Fall back to named controls (no-op in the new AXAML which has no named canvas)
        canvas ??= this.FindControl<Canvas>("AnnotationCanvas");
        img    ??= this.FindControl<Image>("PageImage");
        return (canvas, img);
    }

    private void AnnotCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Tab == null || !Tab.IsAnnotationMode) return;

        // Set active canvas + current page index from DataTemplate Tag
        if (sender is Canvas sc)
        {
            _activeAnnotCanvas = sc;
            if (sc.Tag is int pi) Tab.SetCurrentPageSilent(pi);
        }

        var (canvas, pageImage) = GetAnnotationElements();
        if (canvas == null || pageImage == null) return;

        var point = e.GetPosition(canvas);
        var imgW = pageImage.Bounds.Width;
        var imgH = pageImage.Bounds.Height;
        if (imgW <= 0 || imgH <= 0) return;

        // If we're currently editing inline text, commit it first
        if (_isEditingInline)
        {
            CommitInlineTextBox(canvas, pageImage);
        }

        // Check if clicking on an existing annotation (for selection/dragging)
        var normPt = NormalizePoint(point, pageImage);
        var hitAnnotation = HitTestAnnotation(normPt.X, normPt.Y);

        if (hitAnnotation != null)
        {
            _selectedAnnotation = hitAnnotation;

            // Double-click on text annotation → inline edit
            if (e.ClickCount == 2 && hitAnnotation.Type == AnnotationType.Text)
            {
                var editPt = new Point(hitAnnotation.X * imgW, hitAnnotation.Y * imgH);
                PlaceInlineTextBox(editPt, canvas, pageImage, hitAnnotation);
                e.Handled = true;
                return;
            }

            // Single click → start drag to reposition
            _isDraggingAnnotation = true;
            _annotDragStart = point;
            _annotOrigX = hitAnnotation.X;
            _annotOrigY = hitAnnotation.Y;
            RenderAnnotationsOnCanvas(); // re-render with selection highlight
            e.Handled = true;
            return;
        }

        _selectedAnnotation = null;
        _annotStartPoint = point;
        _isDrawingAnnotation = true;

        var tool = Tab.ActiveAnnotationTool;

        if (tool == AnnotationType.Text)
        {
            // Place an inline editable text box directly on the canvas
            _isDrawingAnnotation = false;
            PlaceInlineTextBox(point, canvas, pageImage, null);
            return;
        }

        if (tool == AnnotationType.StickyNote)
        {
            _isDrawingAnnotation = false;
            ShowStickyNoteDialog(point, canvas, pageImage);
            return;
        }

        if (tool == AnnotationType.FreehandDraw)
        {
            _previewPolyline = new Polyline
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Points = new List<Point> { point }
            };
            canvas.Children.Add(_previewPolyline);
            _currentAnnotation = new PdfAnnotation { Type = AnnotationType.FreehandDraw };
            _currentAnnotation.Points.Clear();
            var nPt = NormalizePoint(point, pageImage);
            _currentAnnotation.Points.Add((nPt.X, nPt.Y));
            return;
        }

        if (tool == AnnotationType.Arrow)
        {
            _previewLine = new Line
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                StartPoint = point,
                EndPoint = point
            };
            canvas.Children.Add(_previewLine);
            return;
        }

        // Rectangle, Ellipse, Highlight, Blur, Redact => draw preview rect
        _previewRect = new Rectangle
        {
            StrokeThickness = 1,
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 2 }
        };

        switch (tool)
        {
            case AnnotationType.Highlight:
                _previewRect.Fill = new SolidColorBrush(Colors.Yellow, 0.3);
                _previewRect.Stroke = Brushes.Orange;
                break;
            case AnnotationType.Blur:
                _previewRect.Fill = new SolidColorBrush(Colors.Blue, 0.15);
                _previewRect.Stroke = Brushes.Blue;
                break;
            case AnnotationType.Redact:
                _previewRect.Fill = new SolidColorBrush(Colors.Black, 0.3);
                _previewRect.Stroke = Brushes.Black;
                break;
            default:
                _previewRect.Fill = null;
                _previewRect.Stroke = Brushes.Red;
                break;
        }

        Canvas.SetLeft(_previewRect, point.X);
        Canvas.SetTop(_previewRect, point.Y);
        _previewRect.Width = 0;
        _previewRect.Height = 0;
        canvas.Children.Add(_previewRect);
    }

    private void AnnotCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        var (canvas, pageImage) = GetAnnotationElements();
        if (canvas == null || pageImage == null || Tab == null) return;

        var point = e.GetPosition(canvas);

        // Handle annotation dragging (repositioning)
        if (_isDraggingAnnotation && _selectedAnnotation != null)
        {
            var imgW = pageImage.Bounds.Width;
            var imgH = pageImage.Bounds.Height;
            if (imgW <= 0 || imgH <= 0) return;

            var dx = (point.X - _annotDragStart.X) / imgW;
            var dy = (point.Y - _annotDragStart.Y) / imgH;

            _selectedAnnotation.X = Math.Clamp(_annotOrigX + dx, 0, Math.Max(0, 1 - _selectedAnnotation.Width));
            _selectedAnnotation.Y = Math.Clamp(_annotOrigY + dy, 0, Math.Max(0, 1 - _selectedAnnotation.Height));

            RenderAnnotationsOnCanvas();
            return;
        }

        if (!_isDrawingAnnotation) return;

        if (_previewPolyline != null)
        {
            var pts = (List<Point>)_previewPolyline.Points;
            pts.Add(point);
            _previewPolyline.Points = new List<Point>(pts); // Force re-render
            var nPt = NormalizePoint(point, pageImage);
            _currentAnnotation?.Points.Add((nPt.X, nPt.Y));
            return;
        }

        if (_previewLine != null)
        {
            _previewLine.EndPoint = point;
            return;
        }

        if (_previewRect != null)
        {
            var x = Math.Min(_annotStartPoint.X, point.X);
            var y = Math.Min(_annotStartPoint.Y, point.Y);
            var w = Math.Abs(point.X - _annotStartPoint.X);
            var h = Math.Abs(point.Y - _annotStartPoint.Y);
            Canvas.SetLeft(_previewRect, x);
            Canvas.SetTop(_previewRect, y);
            _previewRect.Width = w;
            _previewRect.Height = h;
        }
    }

    private void AnnotCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Handle end of annotation drag
        if (_isDraggingAnnotation)
        {
            _isDraggingAnnotation = false;
            RenderAnnotationsOnCanvas();
            return;
        }

        if (!_isDrawingAnnotation || Tab == null) return;
        _isDrawingAnnotation = false;

        var (canvas, pageImage) = GetAnnotationElements();
        if (canvas == null || pageImage == null) return;

        var point = e.GetPosition(canvas);
        var tool = Tab.ActiveAnnotationTool;

        if (_previewPolyline != null && _currentAnnotation != null)
        {
            _currentAnnotation.StrokeColor = "#FF0000";
            _currentAnnotation.StrokeWidth = 2;
            Tab.AddAnnotation(_currentAnnotation);
            _currentAnnotation = null;
            _previewPolyline = null;
            RenderAnnotationsOnCanvas();
            return;
        }

        if (_previewLine != null)
        {
            var start = NormalizePoint(_annotStartPoint, pageImage);
            var end = NormalizePoint(point, pageImage);

            var ann = new PdfAnnotation
            {
                Type = AnnotationType.Arrow,
                X = start.X, Y = start.Y,
                EndX = end.X, EndY = end.Y,
                StrokeColor = "#FF0000",
                StrokeWidth = 2
            };
            Tab.AddAnnotation(ann);
            _previewLine = null;
            RenderAnnotationsOnCanvas();
            return;
        }

        if (_previewRect != null)
        {
            var start = NormalizePoint(_annotStartPoint, pageImage);
            var end = NormalizePoint(point, pageImage);

            var x = Math.Min(start.X, end.X);
            var y = Math.Min(start.Y, end.Y);
            var w = Math.Abs(end.X - start.X);
            var h = Math.Abs(end.Y - start.Y);

            if (w > 0.01 && h > 0.01) // Minimum size
            {
                var ann = new PdfAnnotation
                {
                    Type = tool,
                    X = x, Y = y,
                    Width = w, Height = h
                };

                // Set defaults based on type
                switch (tool)
                {
                    case AnnotationType.Highlight:
                        ann.FillColor = "#FFFF00";
                        ann.FillOpacity = 0.35f;
                        break;
                    case AnnotationType.Rectangle:
                        ann.StrokeColor = "#FF0000";
                        ann.StrokeWidth = 2;
                        break;
                    case AnnotationType.Ellipse:
                        ann.StrokeColor = "#0000FF";
                        ann.StrokeWidth = 2;
                        break;
                    case AnnotationType.Blur:
                        ann.BlurRadius = 10;
                        break;
                    case AnnotationType.Redact:
                        ann.FillColor = "#000000";
                        ann.FillOpacity = 1f;
                        break;
                }

                Tab.AddAnnotation(ann);
            }

            _previewRect = null;
            RenderAnnotationsOnCanvas();
        }
    }

    private async void ShowTextAnnotationDialog(Point canvasPt, Canvas canvas, Image pageImage)
    {
        try
        {
            var dialog = new Window
            {
                Title = "Add Text Annotation",
                Width = 400, Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var textInput = new TextBox { Watermark = "Enter text...", AcceptsReturn = true, Margin = new Thickness(10), Height = 80 };
            var sizeInput = new TextBox { Text = "14", Watermark = "Font size", Margin = new Thickness(10, 0, 10, 5) };
            var colorInput = new TextBox { Text = "#000000", Watermark = "Color (#hex)", Margin = new Thickness(10, 0, 10, 5) };
            var boldCb = new CheckBox { Content = "Bold", Margin = new Thickness(10, 0) };
            var btn = new Button { Content = "Add Text", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(10) };

            btn.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(textInput.Text))
                {
                    float.TryParse(sizeInput.Text, out float fontSize);
                    if (fontSize < 1) fontSize = 14;

                    var nPt = NormalizePoint(canvasPt, pageImage);
                    var ann = new PdfAnnotation
                    {
                        Type = AnnotationType.Text,
                        X = nPt.X, Y = nPt.Y,
                        Width = 0.3, Height = 0.05,
                        Text = textInput.Text,
                        FontSize = fontSize,
                        Color = colorInput.Text ?? "#000000",
                        IsBold = boldCb.IsChecked == true
                    };
                    Tab?.AddAnnotation(ann);
                    RenderAnnotationsOnCanvas();
                }
                dialog.Close();
            };

            dialog.Content = new StackPanel { Children = { textInput, sizeInput, colorInput, boldCb, btn } };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "Text annotation dialog error"); }
    }

    private async void ShowStickyNoteDialog(Point canvasPt, Canvas canvas, Image pageImage)
    {
        try
        {
            var dialog = new Window
            {
                Title = "Add Sticky Note",
                Width = 400, Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var noteInput = new TextBox { Watermark = "Note content...", AcceptsReturn = true, Margin = new Thickness(10), Height = 80 };
            var colorInput = new TextBox { Text = "#FFFACD", Watermark = "Note color (#hex)", Margin = new Thickness(10, 0, 10, 5) };
            var btn = new Button { Content = "Add Note", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(10) };

            btn.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(noteInput.Text))
                {
                    var nPt = NormalizePoint(canvasPt, pageImage);
                    var ann = new PdfAnnotation
                    {
                        Type = AnnotationType.StickyNote,
                        X = nPt.X, Y = nPt.Y,
                        Width = 0.03, Height = 0.03,
                        NoteContent = noteInput.Text,
                        NoteColor = colorInput.Text ?? "#FFFACD"
                    };
                    Tab?.AddAnnotation(ann);
                    RenderAnnotationsOnCanvas();
                }
                dialog.Close();
            };

            dialog.Content = new StackPanel { Children = { noteInput, colorInput, btn } };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "Sticky note dialog error"); }
    }

    #endregion

    #region Inline Text Editing & Annotation Selection

    private PdfAnnotation? HitTestAnnotation(double normX, double normY)
    {
        if (Tab == null) return null;
        // Iterate in reverse so topmost (last added) annotations are hit first
        foreach (var ann in Tab.CurrentPageAnnotations.AsEnumerable().Reverse())
        {
            double ax = ann.X, ay = ann.Y;
            double aw = ann.Width > 0 ? ann.Width : 0.15;
            double ah = ann.Height > 0 ? ann.Height : 0.04;

            // For text annotations, use a generous hit area
            if (ann.Type == AnnotationType.Text)
            {
                aw = Math.Max(aw, 0.15);
                ah = Math.Max(ah, 0.04);
            }

            if (normX >= ax && normX <= ax + aw && normY >= ay && normY <= ay + ah)
                return ann;
        }
        return null;
    }

    private void PlaceInlineTextBox(Point canvasPt, Canvas canvas, Image pageImage, PdfAnnotation? existingAnnotation)
    {
        try
        {
            // If already editing, commit the current one first
            if (_isEditingInline)
                CommitInlineTextBox(canvas, pageImage);

            _editingAnnotation = existingAnnotation;
            _isEditingInline = true;

            // Determine initial values from existing annotation or toolbar state
            var text = existingAnnotation?.Text ?? "";
            var fontSize = existingAnnotation?.FontSize > 0 ? existingAnnotation.FontSize : _currentFontSize;
            var isBold = existingAnnotation?.IsBold ?? _currentBold;
            var isItalic = existingAnnotation?.IsItalic ?? _currentItalic;
            var color = existingAnnotation?.Color ?? _currentFontColor;

            _inlineTextBox = new TextBox
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = isBold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = isItalic ? FontStyle.Italic : FontStyle.Normal,
                Foreground = TryParseBrush(color),
                Background = new SolidColorBrush(Colors.White, 0.85),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                BorderThickness = new Thickness(2),
                MinWidth = 100,
                MinHeight = 28,
                MaxWidth = pageImage.Bounds.Width * 0.6,
                Padding = new Thickness(4, 2),
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Watermark = "Type text here..."
            };

            Canvas.SetLeft(_inlineTextBox, canvasPt.X);
            Canvas.SetTop(_inlineTextBox, canvasPt.Y);
            canvas.Children.Add(_inlineTextBox);

            // Focus textbox after a short delay to ensure layout
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _inlineTextBox?.Focus();
                if (!string.IsNullOrEmpty(_inlineTextBox?.Text))
                    _inlineTextBox.SelectAll();
            }, Avalonia.Threading.DispatcherPriority.Background);

            // Handle Enter (without Shift) to commit, Escape to cancel
            _inlineTextBox.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    _isEditingInline = false;
                    _editingAnnotation = null;
                    if (_inlineTextBox != null)
                    {
                        canvas.Children.Remove(_inlineTextBox);
                        _inlineTextBox = null;
                    }
                    args.Handled = true;
                }
                else if (args.Key == Key.Enter && !args.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    CommitInlineTextBox(canvas, pageImage);
                    args.Handled = true;
                }
            };

            // Also commit when the TextBox loses focus
            _inlineTextBox.LostFocus += (s, args) =>
            {
                if (_isEditingInline)
                    CommitInlineTextBox(canvas, pageImage);
            };
        }
        catch (Exception ex) { Log.Error(ex, "PlaceInlineTextBox error"); }
    }

    private void CommitInlineTextBox(Canvas canvas, Image pageImage)
    {
        try
        {
            if (_inlineTextBox == null || !_isEditingInline) return;

            var text = _inlineTextBox.Text?.Trim();
            _isEditingInline = false;

            if (!string.IsNullOrEmpty(text))
            {
                var left = Canvas.GetLeft(_inlineTextBox);
                var top = Canvas.GetTop(_inlineTextBox);
                var nPt = NormalizePoint(new Point(left, top), pageImage);

                // Estimate normalized width/height from TextBox actual size
                var imgW = pageImage.Bounds.Width;
                var imgH = pageImage.Bounds.Height;
                var normW = imgW > 0 ? Math.Max(_inlineTextBox.Bounds.Width / imgW, 0.05) : 0.2;
                var normH = imgH > 0 ? Math.Max(_inlineTextBox.Bounds.Height / imgH, 0.02) : 0.04;

                if (_editingAnnotation != null)
                {
                    // Update existing annotation
                    _editingAnnotation.Text = text;
                    _editingAnnotation.X = nPt.X;
                    _editingAnnotation.Y = nPt.Y;
                    _editingAnnotation.Width = normW;
                    _editingAnnotation.Height = normH;
                    _editingAnnotation.FontSize = (float)_inlineTextBox.FontSize;
                    _editingAnnotation.IsBold = _inlineTextBox.FontWeight == FontWeight.Bold;
                    _editingAnnotation.IsItalic = _inlineTextBox.FontStyle == FontStyle.Italic;
                    // Try to extract color from foreground
                    if (_inlineTextBox.Foreground is SolidColorBrush scb)
                        _editingAnnotation.Color = $"#{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}";
                }
                else
                {
                    // Create new annotation
                    var ann = new PdfAnnotation
                    {
                        Type = AnnotationType.Text,
                        X = nPt.X,
                        Y = nPt.Y,
                        Width = normW,
                        Height = normH,
                        Text = text,
                        FontSize = (float)_inlineTextBox.FontSize,
                        Color = _currentFontColor,
                        IsBold = _currentBold,
                        IsItalic = _currentItalic
                    };
                    Tab?.AddAnnotation(ann);
                }
            }

            // Remove the inline TextBox from canvas
            canvas.Children.Remove(_inlineTextBox);
            _inlineTextBox = null;
            _editingAnnotation = null;

            RenderAnnotationsOnCanvas();
        }
        catch (Exception ex) { Log.Error(ex, "CommitInlineTextBox error"); }
    }

    private void OnFormatFontSizeChanged(object? sender, RoutedEventArgs e)
    {
        var tbFontSize = this.FindControl<TextBox>("TbFontSize");
        if (tbFontSize != null && float.TryParse(tbFontSize.Text, out float size) && size > 0 && size <= 200)
        {
            _currentFontSize = size;
            if (_isEditingInline && _inlineTextBox != null)
                _inlineTextBox.FontSize = size;
            if (_selectedAnnotation != null && _selectedAnnotation.Type == AnnotationType.Text)
            {
                _selectedAnnotation.FontSize = size;
                RenderAnnotationsOnCanvas();
            }
        }
    }

    private void OnFormatBoldToggle(object? sender, RoutedEventArgs e)
    {
        _currentBold = !_currentBold;
        var btnBold = this.FindControl<Button>("BtnBold");
        if (btnBold != null)
            btnBold.FontWeight = _currentBold ? FontWeight.ExtraBold : FontWeight.Bold;

        if (_isEditingInline && _inlineTextBox != null)
            _inlineTextBox.FontWeight = _currentBold ? FontWeight.Bold : FontWeight.Normal;
        if (_selectedAnnotation != null && _selectedAnnotation.Type == AnnotationType.Text)
        {
            _selectedAnnotation.IsBold = _currentBold;
            RenderAnnotationsOnCanvas();
        }
    }

    private void OnFormatItalicToggle(object? sender, RoutedEventArgs e)
    {
        _currentItalic = !_currentItalic;
        var btnItalic = this.FindControl<Button>("BtnItalic");
        if (btnItalic != null)
            btnItalic.FontStyle = _currentItalic ? FontStyle.Italic : FontStyle.Normal;

        if (_isEditingInline && _inlineTextBox != null)
            _inlineTextBox.FontStyle = _currentItalic ? FontStyle.Italic : FontStyle.Normal;
        if (_selectedAnnotation != null && _selectedAnnotation.Type == AnnotationType.Text)
        {
            _selectedAnnotation.IsItalic = _currentItalic;
            RenderAnnotationsOnCanvas();
        }
    }

    private void OnFormatColorChanged(object? sender, RoutedEventArgs e)
    {
        var tbColor = this.FindControl<TextBox>("TbFontColor");
        if (tbColor != null && !string.IsNullOrWhiteSpace(tbColor.Text))
        {
            var hex = tbColor.Text.Trim();
            if (!hex.StartsWith("#")) hex = "#" + hex;
            _currentFontColor = hex;

            try
            {
                var brush = TryParseBrush(hex);
                if (_isEditingInline && _inlineTextBox != null)
                    _inlineTextBox.Foreground = brush;
                if (_selectedAnnotation != null && _selectedAnnotation.Type == AnnotationType.Text)
                {
                    _selectedAnnotation.Color = hex;
                    RenderAnnotationsOnCanvas();
                }
            }
            catch { /* ignore invalid color */ }
        }
    }

    #endregion

    #region Canvas Rendering & Helpers

    private Point NormalizePoint(Point canvasPoint, Image pageImage)
    {
        var imgW = pageImage.Bounds.Width;
        var imgH = pageImage.Bounds.Height;
        if (imgW <= 0 || imgH <= 0) return new Point(0, 0);
        return new Point(
            Math.Clamp(canvasPoint.X / imgW, 0, 1),
            Math.Clamp(canvasPoint.Y / imgH, 0, 1)
        );
    }

    private void RenderAnnotationsOnCanvas()
    {
        var (canvas, pageImage) = GetAnnotationElements();
        if (canvas == null || pageImage == null || Tab == null) return;

        canvas.Children.Clear();

        var imgW = pageImage.Bounds.Width;
        var imgH = pageImage.Bounds.Height;
        if (imgW <= 0 || imgH <= 0) return;

        foreach (var ann in Tab.CurrentPageAnnotations)
        {
            switch (ann.Type)
            {
                case AnnotationType.Text:
                    var tb = new TextBlock
                    {
                        Text = ann.Text ?? "",
                        FontSize = ann.FontSize > 0 ? ann.FontSize : 14,
                        FontWeight = ann.IsBold ? FontWeight.Bold : FontWeight.Normal,
                        FontStyle = ann.IsItalic ? FontStyle.Italic : FontStyle.Normal,
                        Foreground = TryParseBrush(ann.Color)
                    };
                    Canvas.SetLeft(tb, ann.X * imgW);
                    Canvas.SetTop(tb, ann.Y * imgH);
                    canvas.Children.Add(tb);
                    break;

                case AnnotationType.Highlight:
                    var hlRect = new Rectangle
                    {
                        Width = ann.Width * imgW,
                        Height = ann.Height * imgH,
                        Fill = new SolidColorBrush(ParseColor(ann.FillColor), ann.FillOpacity)
                    };
                    Canvas.SetLeft(hlRect, ann.X * imgW);
                    Canvas.SetTop(hlRect, ann.Y * imgH);
                    canvas.Children.Add(hlRect);
                    break;

                case AnnotationType.Rectangle:
                    var rect = new Rectangle
                    {
                        Width = ann.Width * imgW,
                        Height = ann.Height * imgH,
                        Stroke = TryParseBrush(ann.StrokeColor),
                        StrokeThickness = ann.StrokeWidth
                    };
                    Canvas.SetLeft(rect, ann.X * imgW);
                    Canvas.SetTop(rect, ann.Y * imgH);
                    canvas.Children.Add(rect);
                    break;

                case AnnotationType.Ellipse:
                    var ell = new Ellipse
                    {
                        Width = ann.Width * imgW,
                        Height = ann.Height * imgH,
                        Stroke = TryParseBrush(ann.StrokeColor),
                        StrokeThickness = ann.StrokeWidth
                    };
                    Canvas.SetLeft(ell, ann.X * imgW);
                    Canvas.SetTop(ell, ann.Y * imgH);
                    canvas.Children.Add(ell);
                    break;

                case AnnotationType.Arrow:
                    var arrowLine = new Line
                    {
                        StartPoint = new Point(ann.X * imgW, ann.Y * imgH),
                        EndPoint = new Point(ann.EndX * imgW, ann.EndY * imgH),
                        Stroke = TryParseBrush(ann.StrokeColor),
                        StrokeThickness = ann.StrokeWidth
                    };
                    canvas.Children.Add(arrowLine);
                    break;

                case AnnotationType.FreehandDraw:
                    if (ann.Points.Count > 1)
                    {
                        var poly = new Polyline
                        {
                            Stroke = TryParseBrush(ann.StrokeColor),
                            StrokeThickness = ann.StrokeWidth,
                            Points = ann.Points.Select(p => new Point(p.x * imgW, p.y * imgH)).ToList()
                        };
                        canvas.Children.Add(poly);
                    }
                    break;

                case AnnotationType.Blur:
                case AnnotationType.Redact:
                    var coverRect = new Rectangle
                    {
                        Width = ann.Width * imgW,
                        Height = ann.Height * imgH,
                        Fill = ann.Type == AnnotationType.Blur
                            ? new SolidColorBrush(Colors.LightBlue, 0.4)
                            : Brushes.Black,
                        Stroke = Brushes.Gray,
                        StrokeThickness = 1,
                        StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 2 }
                    };
                    Canvas.SetLeft(coverRect, ann.X * imgW);
                    Canvas.SetTop(coverRect, ann.Y * imgH);
                    canvas.Children.Add(coverRect);

                    // Label
                    var label = new TextBlock
                    {
                        Text = ann.Type == AnnotationType.Blur ? "BLUR" : "REDACT",
                        FontSize = 10,
                        Foreground = ann.Type == AnnotationType.Blur ? Brushes.DarkBlue : Brushes.White,
                        Opacity = 0.7
                    };
                    Canvas.SetLeft(label, ann.X * imgW + 3);
                    Canvas.SetTop(label, ann.Y * imgH + 2);
                    canvas.Children.Add(label);
                    break;

                case AnnotationType.Image:
                    var imgRect = new Rectangle
                    {
                        Width = ann.Width * imgW,
                        Height = ann.Height * imgH,
                        Stroke = Brushes.Green,
                        StrokeThickness = 1,
                        Fill = new SolidColorBrush(Colors.Green, 0.1)
                    };
                    Canvas.SetLeft(imgRect, ann.X * imgW);
                    Canvas.SetTop(imgRect, ann.Y * imgH);
                    canvas.Children.Add(imgRect);

                    var imgLabel = new TextBlock
                    {
                        Text = "IMAGE",
                        FontSize = 10,
                        Foreground = Brushes.DarkGreen,
                        Opacity = 0.7
                    };
                    Canvas.SetLeft(imgLabel, ann.X * imgW + 3);
                    Canvas.SetTop(imgLabel, ann.Y * imgH + 2);
                    canvas.Children.Add(imgLabel);
                    break;

                case AnnotationType.Stamp:
                    var stampBorder = new Border
                    {
                        Width = ann.Width * imgW,
                        Height = ann.Height * imgH,
                        BorderBrush = Brushes.Red,
                        BorderThickness = new Thickness(3),
                        Background = new SolidColorBrush(Colors.White, 0.7)
                    };
                    var stampText = new TextBlock
                    {
                        Text = ann.StampText ?? "STAMP",
                        FontSize = ann.FontSize > 0 ? ann.FontSize : 20,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.Red,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center
                    };
                    stampBorder.Child = stampText;
                    Canvas.SetLeft(stampBorder, ann.X * imgW);
                    Canvas.SetTop(stampBorder, ann.Y * imgH);
                    canvas.Children.Add(stampBorder);
                    break;

                case AnnotationType.StickyNote:
                    var noteBg = new Rectangle
                    {
                        Width = 24, Height = 24,
                        Fill = new SolidColorBrush(ParseColor(ann.NoteColor)),
                        Stroke = Brushes.Orange,
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(noteBg, ann.X * imgW);
                    Canvas.SetTop(noteBg, ann.Y * imgH);
                    canvas.Children.Add(noteBg);

                    var noteIcon = new TextBlock
                    {
                        Text = "N",
                        FontSize = 14,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.DarkOrange
                    };
                    Canvas.SetLeft(noteIcon, ann.X * imgW + 6);
                    Canvas.SetTop(noteIcon, ann.Y * imgH + 2);
                    canvas.Children.Add(noteIcon);
                    break;

                case AnnotationType.Underline:
                    var ulLine = new Line
                    {
                        StartPoint = new Point(ann.X * imgW, (ann.Y + ann.Height) * imgH),
                        EndPoint = new Point((ann.X + ann.Width) * imgW, (ann.Y + ann.Height) * imgH),
                        Stroke = TryParseBrush(ann.StrokeColor ?? "#00AA00"),
                        StrokeThickness = ann.StrokeWidth > 0 ? ann.StrokeWidth : 2
                    };
                    canvas.Children.Add(ulLine);
                    break;

                case AnnotationType.Strikethrough:
                    var stLine = new Line
                    {
                        StartPoint = new Point(ann.X * imgW, (ann.Y + ann.Height / 2) * imgH),
                        EndPoint = new Point((ann.X + ann.Width) * imgW, (ann.Y + ann.Height / 2) * imgH),
                        Stroke = TryParseBrush(ann.StrokeColor ?? "#FF0000"),
                        StrokeThickness = ann.StrokeWidth > 0 ? ann.StrokeWidth : 2
                    };
                    canvas.Children.Add(stLine);
                    break;
            }

            // Draw selection highlight around selected annotation
            if (ann == _selectedAnnotation)
            {
                double sx = ann.X * imgW;
                double sy = ann.Y * imgH;
                double sw = (ann.Width > 0 ? ann.Width : 0.15) * imgW;
                double sh = (ann.Height > 0 ? ann.Height : 0.04) * imgH;

                var selRect = new Rectangle
                {
                    Width = sw + 8,
                    Height = sh + 8,
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                    StrokeThickness = 2,
                    StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 },
                    Fill = null
                };
                Canvas.SetLeft(selRect, sx - 4);
                Canvas.SetTop(selRect, sy - 4);
                canvas.Children.Add(selRect);
            }
        }
    }

    private void ClearAnnotationCanvas()
    {
        GetAnnotationElements().canvas?.Children.Clear();
    }

    private static IBrush TryParseBrush(string hex)
    {
        try
        {
            var c = ParseColor(hex);
            return new SolidColorBrush(c);
        }
        catch { return Brushes.Black; }
    }

    private static Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            var r = Convert.ToByte(hex.Substring(0, 2), 16);
            var g = Convert.ToByte(hex.Substring(2, 2), 16);
            var b = Convert.ToByte(hex.Substring(4, 2), 16);
            return Color.FromRgb(r, g, b);
        }
        return Colors.Black;
    }

    #endregion

    #region Crop Page

    private async void OnCropPageClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var dialog = new Window
            {
                Title = "Crop Page",
                Width = 380, Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var infoLabel = new TextBlock
            {
                Text = "Enter crop margins as percentage (0-100).\nE.g., 10 = remove 10% from that side.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Thickness(10, 10, 10, 5),
                FontSize = 12,
                Opacity = 0.7
            };
            var leftInput = new TextBox { Watermark = "Left % (0-100)", Text = "0", Margin = new Thickness(10, 5) };
            var topInput = new TextBox { Watermark = "Top % (0-100)", Text = "0", Margin = new Thickness(10, 5) };
            var rightInput = new TextBox { Watermark = "Right % (0-100)", Text = "0", Margin = new Thickness(10, 5) };
            var bottomInput = new TextBox { Watermark = "Bottom % (0-100)", Text = "0", Margin = new Thickness(10, 5) };
            var btn = new Button
            {
                Content = "Crop",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(10)
            };

            btn.Click += async (_, _) =>
            {
                double.TryParse(leftInput.Text, out double left);
                double.TryParse(topInput.Text, out double top);
                double.TryParse(rightInput.Text, out double right);
                double.TryParse(bottomInput.Text, out double bottom);

                // Convert percentages to 0-1 range
                left = Math.Clamp(left / 100.0, 0, 0.49);
                top = Math.Clamp(top / 100.0, 0, 0.49);
                right = Math.Clamp(right / 100.0, 0, 0.49);
                bottom = Math.Clamp(bottom / 100.0, 0, 0.49);

                await Tab.CropPageAsync(left, top, right, bottom);
                dialog.Close();
            };

            dialog.Content = new StackPanel
            {
                Children = { infoLabel, leftInput, topInput, rightInput, bottomInput, btn }
            };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "Crop dialog error"); }
    }

    #endregion

    #region Batch Processing

    private async void OnBatchProcessingClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Window
            {
                Title = "Batch Processing",
                Width = 500, Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var opLabel = new TextBlock { Text = "Operation:", FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 10, 10, 3) };
            var opCombo = new ComboBox
            {
                Margin = new Thickness(10, 0, 10, 5),
                ItemsSource = new[] { "Rotate All Pages", "Add Watermark", "Add Page Numbers", "Encrypt", "Export Images", "Merge All", "Split All" },
                SelectedIndex = 0
            };
            var paramLabel = new TextBlock { Text = "Parameters:", FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 5, 10, 3) };
            var paramInput = new TextBox
            {
                Watermark = "Rotation: 90/180/270 | Watermark: text | Password: pwd",
                Margin = new Thickness(10, 0, 10, 5)
            };
            var filesLabel = new TextBlock { Text = "Selected Files:", FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 5, 10, 3) };
            var filesList = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                Height = 100,
                Margin = new Thickness(10, 0, 10, 5),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
            var addFilesBtn = new Button { Content = "Add PDF Files...", Margin = new Thickness(10, 0, 10, 5) };
            var outputLabel = new TextBlock { Text = "Output Folder:", FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 5, 10, 3) };
            var outputPath = new TextBox { IsReadOnly = true, Margin = new Thickness(10, 0, 10, 5) };
            var selectOutputBtn = new Button { Content = "Select Output Folder...", Margin = new Thickness(10, 0, 10, 5) };
            var runBtn = new Button
            {
                Content = "Run Batch",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(10),
                FontWeight = FontWeight.SemiBold
            };
            var statusLabel = new TextBlock { Text = "", Margin = new Thickness(10, 0), FontSize = 11, Opacity = 0.7 };

            var inputFiles = new List<string>();

            addFilesBtn.Click += async (_, _) =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select PDF Files for Batch Processing",
                    AllowMultiple = true,
                    FileTypeFilter = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } }
                });
                foreach (var f in files)
                {
                    var p = f.TryGetLocalPath();
                    if (!string.IsNullOrEmpty(p) && !inputFiles.Contains(p))
                        inputFiles.Add(p);
                }
                filesList.Text = string.Join("\n", inputFiles.Select(IOPath.GetFileName));
            };

            selectOutputBtn.Click += async (_, _) =>
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Output Folder"
                });
                if (folders.Count > 0)
                    outputPath.Text = folders[0].TryGetLocalPath() ?? "";
            };

            runBtn.Click += async (_, _) =>
            {
                if (inputFiles.Count == 0) { statusLabel.Text = "No files selected"; return; }
                if (string.IsNullOrEmpty(outputPath.Text)) { statusLabel.Text = "No output folder selected"; return; }

                var opTypeMap = new[] { "rotate", "watermark", "pagenumbers", "encrypt", "export_images", "merge", "split" };
                var opIdx = opCombo.SelectedIndex;
                if (opIdx < 0) opIdx = 0;

                var config = new BatchOperationConfig
                {
                    OperationType = opTypeMap[opIdx],
                    InputFiles = inputFiles,
                    OutputFolder = outputPath.Text,
                    WatermarkText = paramInput.Text ?? "",
                    RotationDegrees = int.TryParse(paramInput.Text, out int rot) ? rot : 90,
                    OwnerPassword = paramInput.Text ?? ""
                };

                statusLabel.Text = "Processing...";
                runBtn.IsEnabled = false;

                try
                {
                    var batchSvc = new PdfBatchService();
                    var results = await Task.Run(() =>
                        batchSvc.ProcessBatch(config, (i, total, name) =>
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                statusLabel.Text = $"Processing {i + 1}/{total}: {name}");
                        }));

                    var successes = results.Count(r => r.Success);
                    var failures = results.Count(r => !r.Success);
                    statusLabel.Text = $"Done: {successes} succeeded, {failures} failed";
                }
                catch (Exception ex)
                {
                    statusLabel.Text = $"Error: {ex.Message}";
                    Log.Error(ex, "Batch processing error");
                }
                finally { runBtn.IsEnabled = true; }
            };

            dialog.Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Children = { opLabel, opCombo, paramLabel, paramInput, filesLabel, filesList, addFilesBtn, outputLabel, outputPath, selectOutputBtn, runBtn, statusLabel }
                }
            };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "Batch processing dialog error"); }
    }

    #endregion

    #region OCR

    private async void OnOcrCurrentPageClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var ocrService = new PDFEditor.Core.Services.TesseractOcrService();
            if (!ocrService.IsAvailable)
            {
                var errorDialog = new Window
                {
                    Title = "OCR Unavailable",
                    Width = 450, Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false
                };
                errorDialog.Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Tesseract OCR is not available.", FontWeight = FontWeight.Bold, FontSize = 16 },
                        new TextBlock { Text = "Please install Tesseract OCR and download language data files (.traineddata) to one of these locations:", TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = $"• {System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata")}\n• Set TESSDATA_PREFIX environment variable\n• C:\\Program Files\\Tesseract-OCR\\tessdata", FontSize = 12, TextWrapping = TextWrapping.Wrap }
                    }
                };
                await errorDialog.ShowDialog(this);
                return;
            }

            // Language selection
            var languages = ocrService.GetSupportedLanguages();
            var langDialog = new Window
            {
                Title = "OCR - Current Page",
                Width = 420, Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var langCombo = new ComboBox { ItemsSource = languages, SelectedIndex = languages.IndexOf("eng") >= 0 ? languages.IndexOf("eng") : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var dpiCombo = new ComboBox { ItemsSource = new[] { "150", "200", "300", "400" }, SelectedIndex = 2, HorizontalAlignment = HorizontalAlignment.Stretch };
            var statusLabel = new TextBlock { Text = $"Page {Tab.CurrentPageIndex + 1} will be processed.", Margin = new Thickness(0, 5, 0, 0) };
            var runBtn = new Button { Content = "Run OCR", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            var resultText = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 0, IsVisible = false };

            runBtn.Click += async (_, _) =>
            {
                var lang = langCombo.SelectedItem?.ToString() ?? "eng";
                var dpi = int.TryParse(dpiCombo.SelectedItem?.ToString(), out int d) ? d : 300;
                statusLabel.Text = "Running OCR... please wait.";
                runBtn.IsEnabled = false;

                try
                {
                    var text = await ocrService.OcrPdfPage(Tab.PdfBytes!, Tab.CurrentPageIndex, lang, dpi);
                    resultText.Text = text;
                    resultText.Height = 150;
                    resultText.IsVisible = true;
                    langDialog.Height = 500;
                    statusLabel.Text = $"OCR complete. {text.Length} characters recognized.";
                }
                catch (Exception ex)
                {
                    statusLabel.Text = $"OCR failed: {ex.Message}";
                    Log.Error(ex, "OCR current page failed");
                }
                finally { runBtn.IsEnabled = true; }
            };

            langDialog.Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Language:", FontWeight = FontWeight.SemiBold },
                        langCombo,
                        new TextBlock { Text = "DPI (higher = better quality, slower):", FontWeight = FontWeight.SemiBold },
                        dpiCombo,
                        runBtn,
                        statusLabel,
                        resultText
                    }
                }
            };
            await langDialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "OCR current page dialog error"); }
    }

    private async void OnOcrAllPagesClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var ocrService = new PDFEditor.Core.Services.TesseractOcrService();
            if (!ocrService.IsAvailable)
            {
                var errorDialog = new Window
                {
                    Title = "OCR Unavailable",
                    Width = 450, Height = 180,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false
                };
                errorDialog.Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Tesseract OCR is not available.", FontWeight = FontWeight.Bold },
                        new TextBlock { Text = "Install Tesseract OCR and ensure tessdata files are accessible.", TextWrapping = TextWrapping.Wrap }
                    }
                };
                await errorDialog.ShowDialog(this);
                return;
            }

            var languages = ocrService.GetSupportedLanguages();
            var dialog = new Window
            {
                Title = "OCR - All Pages",
                Width = 500, Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = true
            };

            var langCombo = new ComboBox { ItemsSource = languages, SelectedIndex = languages.IndexOf("eng") >= 0 ? languages.IndexOf("eng") : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var dpiCombo = new ComboBox { ItemsSource = new[] { "150", "200", "300" }, SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 20, IsVisible = false };
            var statusLabel = new TextBlock { Text = $"Will OCR all {Tab.PageCount} pages.", Margin = new Thickness(0, 5, 0, 0) };
            var runBtn = new Button { Content = "Run OCR on All Pages", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            var resultText = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 0, IsVisible = false };

            runBtn.Click += async (_, _) =>
            {
                var lang = langCombo.SelectedItem?.ToString() ?? "eng";
                var dpi = int.TryParse(dpiCombo.SelectedItem?.ToString(), out int d) ? d : 200;
                statusLabel.Text = "Running OCR... this may take a while.";
                runBtn.IsEnabled = false;
                progressBar.IsVisible = true;

                try
                {
                    var progress = new Progress<(int current, int total)>(p =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            progressBar.Value = (double)p.current / p.total * 100;
                            statusLabel.Text = $"Processing page {p.current} of {p.total}...";
                        });
                    });

                    var text = await ocrService.OcrEntirePdf(Tab.PdfBytes!, lang, dpi, progress);
                    resultText.Text = text;
                    resultText.Height = 200;
                    resultText.IsVisible = true;
                    dialog.Height = 650;
                    statusLabel.Text = $"OCR complete. {text.Length} characters recognized across {Tab.PageCount} pages.";
                    progressBar.Value = 100;
                }
                catch (Exception ex)
                {
                    statusLabel.Text = $"OCR failed: {ex.Message}";
                    Log.Error(ex, "OCR all pages failed");
                }
                finally { runBtn.IsEnabled = true; }
            };

            dialog.Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Language:", FontWeight = FontWeight.SemiBold },
                        langCombo,
                        new TextBlock { Text = "DPI:", FontWeight = FontWeight.SemiBold },
                        dpiCombo,
                        runBtn,
                        progressBar,
                        statusLabel,
                        resultText
                    }
                }
            };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "OCR all pages dialog error"); }
    }

    #endregion

    #region Searchable PDF

    private async void OnMakeSearchablePdfClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded || Tab.PdfBytes == null) return;
        try
        {
            var ocrService = Tab.SearchablePdfService;

            // Language selector
            var langCombo = new ComboBox { Width = 200 };
            var tesseract = new PDFEditor.Core.Services.TesseractOcrService();
            var languages = tesseract.GetSupportedLanguages();
            if (languages.Count == 0) languages.Add("eng");
            foreach (var lang in languages) langCombo.Items.Add(lang);
            langCombo.SelectedIndex = languages.IndexOf("eng") >= 0 ? languages.IndexOf("eng") : 0;

            // DPI selector
            var dpiCombo = new ComboBox { Width = 200 };
            foreach (var d in new[] { 150, 200, 300, 400 }) dpiCombo.Items.Add(d);
            dpiCombo.SelectedIndex = 2; // 300

            // Check image-based pages
            var (imageBased, total) = ocrService.CountImageBasedPages(Tab.PdfBytes);
            var infoText = new TextBlock
            {
                Text = $"This document has {total} page(s), {imageBased} appear to be image-based (no text layer).",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Thickness(0, 5)
            };

            var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, IsVisible = false, Height = 20 };
            var statusLabel = new TextBlock { Text = "Ready", Foreground = Avalonia.Media.Brushes.Gray };

            var runBtn = new Button { Content = "Make Searchable", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };

            var dialog = new Window
            {
                Title = "Make Searchable PDF (OCR)",
                Width = 450, Height = 350,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            runBtn.Click += async (_, _) =>
            {
                var lang = langCombo.SelectedItem?.ToString() ?? "eng";
                var dpi = (int)(dpiCombo.SelectedItem ?? 300);

                statusLabel.Text = "Making PDF searchable... this may take a while.";
                runBtn.IsEnabled = false;
                progressBar.IsVisible = true;

                try
                {
                    var progress = new Progress<(int current, int total)>(p =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            progressBar.Value = (double)p.current / p.total * 100;
                            statusLabel.Text = $"Processing page {p.current} of {p.total}...";
                        });
                    });

                    var resultBytes = await ocrService.MakeSearchableAsync(Tab.PdfBytes, lang, dpi, progress);

                    // Ask user where to save
                    var sp = this.StorageProvider;
                    var file = await sp.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                    {
                        Title = "Save Searchable PDF",
                        DefaultExtension = "pdf",
                        FileTypeChoices = new[]
                        {
                            new Avalonia.Platform.Storage.FilePickerFileType("PDF Files") { Patterns = new[] { "*.pdf" } }
                        },
                        SuggestedFileName = IOPath.GetFileNameWithoutExtension(Tab.FilePath ?? "document") + "_searchable.pdf"
                    });

                    if (file != null)
                    {
                        var path = file.Path.LocalPath;
                        await File.WriteAllBytesAsync(path, resultBytes);
                        statusLabel.Text = $"Searchable PDF saved to: {IOPath.GetFileName(path)}";
                        progressBar.Value = 100;
                    }
                    else
                    {
                        statusLabel.Text = "Save cancelled.";
                    }
                }
                catch (Exception ex)
                {
                    statusLabel.Text = $"Failed: {ex.Message}";
                    Log.Error(ex, "Make searchable PDF failed");
                }
                finally { runBtn.IsEnabled = true; }
            };

            dialog.Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Make Searchable PDF", FontWeight = FontWeight.Bold, FontSize = 16 },
                        infoText,
                        new TextBlock { Text = "Language:", FontWeight = FontWeight.SemiBold },
                        langCombo,
                        new TextBlock { Text = "DPI:", FontWeight = FontWeight.SemiBold },
                        dpiCombo,
                        runBtn,
                        progressBar,
                        statusLabel
                    }
                }
            };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "Make searchable PDF dialog error"); }
    }

    #endregion

    #region Undo History

    private void OnUndoHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var undoList = Tab.GetUndoHistory();
            var redoList = Tab.GetRedoHistory();

            var dialog = new Window
            {
                Title = "Undo/Redo History",
                Width = 400, Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = true
            };

            var undoLabel = new TextBlock { Text = $"Undo Stack ({undoList.Count}):", FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 10, 10, 3) };
            var undoListBox = new ListBox
            {
                ItemsSource = undoList,
                Height = 150,
                Margin = new Thickness(10, 0, 10, 5)
            };
            var undoToBtn = new Button
            {
                Content = "Undo to Selected",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 5),
                IsEnabled = undoList.Count > 0
            };

            var redoLabel = new TextBlock { Text = $"Redo Stack ({redoList.Count}):", FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 10, 10, 3) };
            var redoListBox = new ListBox
            {
                ItemsSource = redoList,
                Height = 150,
                Margin = new Thickness(10, 0, 10, 5)
            };

            undoToBtn.Click += async (_, _) =>
            {
                var selectedIdx = undoListBox.SelectedIndex;
                if (selectedIdx < 0) return;

                int steps = selectedIdx + 1; // Undo from top of stack to selected
                await Tab.UndoToAsync(steps);
                dialog.Close();
            };

            dialog.Content = new StackPanel
            {
                Children = { undoLabel, undoListBox, undoToBtn, redoLabel, redoListBox }
            };
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Undo history dialog error"); }
    }

    #endregion

    #region Stamp & StickyNote & Underline & Strikethrough Annotations

    private void OnAnnotToolUnderline(object? sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationType.Underline);
    private void OnAnnotToolStrikethrough(object? sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationType.Strikethrough);

    private void OnAnnotToolStickyNote(object? sender, RoutedEventArgs e)
    {
        if (Tab == null) return;
        Tab.ActiveAnnotationTool = AnnotationType.StickyNote;
        Tab.IsAnnotationMode = true;
        Tab.StatusText = "Annotation tool: Sticky Note – click on page to place";
    }

    private void OnStampApproved(object? sender, RoutedEventArgs e) => PlaceStamp(StampType.Approved, "APPROVED");
    private void OnStampRejected(object? sender, RoutedEventArgs e) => PlaceStamp(StampType.Rejected, "REJECTED");
    private void OnStampConfidential(object? sender, RoutedEventArgs e) => PlaceStamp(StampType.Confidential, "CONFIDENTIAL");
    private void OnStampDraft(object? sender, RoutedEventArgs e) => PlaceStamp(StampType.Draft, "DRAFT");
    private void OnStampFinal(object? sender, RoutedEventArgs e) => PlaceStamp(StampType.Final, "FINAL");
    private void OnStampForReview(object? sender, RoutedEventArgs e) => PlaceStamp(StampType.ForReview, "FOR REVIEW");

    private async void OnStampCustom(object? sender, RoutedEventArgs e)
    {
        if (Tab == null) return;
        try
        {
            var dialog = new Window
            {
                Title = "Custom Stamp",
                Width = 320, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var textInput = new TextBox { Watermark = "Stamp text...", Margin = new Thickness(10) };
            var btn = new Button { Content = "Place Stamp", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(10) };

            btn.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(textInput.Text))
                    PlaceStamp(StampType.Custom, textInput.Text);
                dialog.Close();
            };

            dialog.Content = new StackPanel { Children = { textInput, btn } };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { Log.Error(ex, "Custom stamp dialog error"); }
    }

    private void PlaceStamp(StampType stampType, string text)
    {
        if (Tab == null) return;

        var ann = new PdfAnnotation
        {
            Type = AnnotationType.Stamp,
            X = 0.3, Y = 0.4,
            Width = 0.4, Height = 0.08,
            StampPreset = stampType,
            StampText = text,
            Color = "#FF0000",
            FontSize = 24,
            IsBold = true
        };
        Tab.IsAnnotationMode = true;
        Tab.AddAnnotation(ann);
        RenderAnnotationsOnCanvas();
        Tab.StatusText = $"Stamp placed: {text}";
    }

    #endregion

    #region Clipboard Paste

    private async void OnClipboardPaste()
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            // Try to get files from clipboard
            var dataObj = await clipboard.GetDataAsync("Files");

            // Try to get text (file path)
            var text = await clipboard.GetTextAsync();
            if (!string.IsNullOrEmpty(text) && File.Exists(text))
            {
                var ext = IOPath.GetExtension(text).ToLowerInvariant();
                if (ext == ".pdf")
                {
                    await Tab.MergeWithAsync(text);
                    Tab.StatusText = "Pasted PDF from clipboard";
                    return;
                }
                if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")
                {
                    var imgBytes = File.ReadAllBytes(text);
                    await Tab.InsertImageAsPageAsync(imgBytes);
                    Tab.StatusText = "Pasted image as new page";
                    return;
                }
            }

            Tab.StatusText = "No pasteable content in clipboard";
        }
        catch (Exception ex) { Log.Error(ex, "Clipboard paste error"); }
    }

    #endregion

    #region Help / About

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var version = PDFEditor.Core.AppConfig.ApplicationVersion;

        var githubLink = new Button
        {
            Content = "GitHub Repository",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Padding = new Thickness(12, 6),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        githubLink.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/OriolCanillasGautier/PDF-Editor",
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { Log.Warn(ex, "Failed to open GitHub link"); }
        };

        var licenseLink = new Button
        {
            Content = "View License (AGPL v3)",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Padding = new Thickness(12, 6),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        licenseLink.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/OriolCanillasGautier/PDF-Editor/blob/main/LICENSE",
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { Log.Warn(ex, "Failed to open license link"); }
        };

        var w = new Window
        {
            Title = "About PDF Editor",
            Width = 450, Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Spacing = 8,
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = "PDF Editor", FontSize = 28, FontWeight = FontWeight.Bold, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    new TextBlock { Text = $"Version {version}", FontSize = 14, Opacity = 0.7, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    new TextBlock { Text = "Cross-platform PDF Editor", FontSize = 13, Opacity = 0.6, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) },
                    new Separator { Margin = new Thickness(0, 8) },
                    new TextBlock { Text = "Built with:", FontSize = 12, FontWeight = FontWeight.SemiBold, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    new TextBlock { Text = "Avalonia UI 11 • iText7 • Docnet (PDFium) • Magick.NET • ReactiveUI", FontSize = 11, Opacity = 0.5, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                    new Separator { Margin = new Thickness(0, 8) },
                    githubLink,
                    licenseLink,
                    new Separator { Margin = new Thickness(0, 4) },
                    new TextBlock { Text = "© 2026 Oriol Canillas. Licensed under AGPL v3.", FontSize = 10, Opacity = 0.4, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                }
            }
        };
        w.Show(this);
    }

    private void OnShortcutsClick(object? sender, RoutedEventArgs e)
    {
        var shortcuts = @"Keyboard Shortcuts:

Ctrl+O     Open file
Ctrl+S     Save
Ctrl+Shift+S  Save As
Ctrl+W     Close tab
Ctrl+Z     Undo
Ctrl+Y     Redo
Ctrl+V     Paste (image as page / PDF merge)
Ctrl+F     Search
Ctrl+G     Go to page
Ctrl+A     Select all pages
Ctrl+P     Print
Home       First page
End        Last page
Left/PgUp  Previous page
Right/PgDn Next page
Delete     Delete selected page(s)
Escape     Close search / exit annotation mode

Drag-Drop: Drag thumbnails to reorder pages
Annotations: Enable annotation mode, select a tool, draw on page
";
        var w = new Window
        {
            Title = "Keyboard Shortcuts",
            Width = 450, Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox
            {
                Text = shortcuts,
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                FontSize = 12,
                Margin = new Thickness(10),
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }
        };
        w.Show(this);
    }

    #endregion

    #region Form Fields

    private void OnDetectFormFieldsClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;
        try
        {
            var hasFields = Tab.FormService.HasFormFields(Tab.PdfBytes);
            if (!hasFields)
            {
                ShowInfoDialog("Form Fields", "This PDF does not contain any interactive form fields.");
                return;
            }
            var fields = Tab.FormService.GetFormFields(Tab.PdfBytes);
            var msg = $"Found {fields.Count} form field(s):\n\n";
            foreach (var f in fields)
            {
                msg += $"  [{f.FieldType}] {f.Name}";
                if (!string.IsNullOrEmpty(f.Value)) msg += $" = \"{f.Value}\"";
                msg += $" (Page {f.PageIndex + 1})\n";
            }
            ShowInfoDialog("Form Fields", msg);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error detecting form fields");
            ShowInfoDialog("Error", $"Failed to detect form fields: {ex.Message}");
        }
    }

    private async void OnFillFormClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;

        var fields = Tab.FormService.GetFormFields(Tab.PdfBytes);
        if (fields.Count == 0)
        {
            ShowInfoDialog("Fill Form", "No form fields found in this document.");
            return;
        }

        // Build a dialog with text inputs for each editable field
        var dialog = new Window
        {
            Title = "Fill Form Fields",
            Width = 500,
            MinHeight = 300,
            MaxHeight = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = BuildFillFormPanel(fields)
        };
        await dialog.ShowDialog(this);
    }

    private StackPanel BuildFillFormPanel(List<PDFEditor.Core.Abstractions.FormFieldInfo> fields)
    {
        var panel = new StackPanel { Margin = new Thickness(15), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Fill Form Fields", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) });

        var fieldInputs = new Dictionary<string, Control>();
        var scroll = new ScrollViewer { MaxHeight = 450 };
        var inner = new StackPanel { Spacing = 6 };

        foreach (var f in fields)
        {
            if (f.IsReadOnly) continue;

            inner.Children.Add(new TextBlock
            {
                Text = $"{f.Name} ({f.FieldType})",
                FontSize = 11,
                Opacity = 0.7,
                Margin = new Thickness(0, 4, 0, 0)
            });

            if (f.FieldType == PDFEditor.Core.Abstractions.FormFieldType.Checkbox)
            {
                var cb = new CheckBox { IsChecked = f.IsChecked, Content = f.Name };
                fieldInputs[f.Name] = cb;
                inner.Children.Add(cb);
            }
            else if (f.FieldType == PDFEditor.Core.Abstractions.FormFieldType.Dropdown && f.Options.Count > 0)
            {
                var combo = new ComboBox { ItemsSource = f.Options, SelectedItem = f.Value };
                if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;
                fieldInputs[f.Name] = combo;
                inner.Children.Add(combo);
            }
            else
            {
                var tb = new TextBox { Text = f.Value, Padding = new Thickness(4, 2) };
                fieldInputs[f.Name] = tb;
                inner.Children.Add(tb);
            }
        }

        scroll.Content = inner;
        panel.Children.Add(scroll);

        var btnRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var applyBtn = new Button { Content = "Apply", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        applyBtn.Click += (s, e) =>
        {
            var values = new Dictionary<string, string>();
            foreach (var kvp in fieldInputs)
            {
                if (kvp.Value is TextBox tb) values[kvp.Key] = tb.Text ?? "";
                else if (kvp.Value is CheckBox cb) values[kvp.Key] = (cb.IsChecked == true) ? "true" : "false";
                else if (kvp.Value is ComboBox combo) values[kvp.Key] = combo.SelectedItem?.ToString() ?? "";
            }
            try
            {
                var newBytes = Tab!.FormService.FillForm(Tab.PdfBytes!, values);
                Tab.UpdatePdfBytes(newBytes, "Fill Form");
                ShowInfoDialog("Fill Form", $"Filled {values.Count} field(s) successfully.");
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error", $"Failed to fill form: {ex.Message}");
            }
            ((Window)((Control)s!).Parent!.Parent!).Close();
        };
        btnRow.Children.Add(applyBtn);
        panel.Children.Add(btnRow);
        return panel;
    }

    private void OnFlattenFormClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;
        try
        {
            var newBytes = Tab.FormService.FlattenForm(Tab.PdfBytes);
            Tab.UpdatePdfBytes(newBytes, "Flatten Form");
            ShowInfoDialog("Flatten Form", "Form fields have been flattened into static content.");
        }
        catch (Exception ex)
        {
            ShowInfoDialog("Error", $"Failed to flatten form: {ex.Message}");
        }
    }

    private async void OnExportFormDataClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;
        var result = Tab.FormService.ExportFormData(Tab.PdfBytes);
        if (!result.Success || result.FieldValues.Count == 0)
        {
            ShowInfoDialog("Export Form Data", result.ErrorMessage ?? "No form data to export.");
            return;
        }

        var sp = this.StorageProvider;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Form Data",
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
            SuggestedFileName = IOPath.GetFileNameWithoutExtension(Tab.FilePath ?? "form") + "_data.json"
        });
        if (file == null) return;

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(result.FieldValues, Newtonsoft.Json.Formatting.Indented);
        await File.WriteAllTextAsync(file.Path.LocalPath, json);
        ShowInfoDialog("Export Form Data", $"Exported {result.FieldValues.Count} field(s) to JSON.");
    }

    private async void OnImportFormDataClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;

        var sp = this.StorageProvider;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Form Data (JSON)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });
        if (files == null || files.Count == 0) return;

        try
        {
            var json = await File.ReadAllTextAsync(files[0].Path.LocalPath);
            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            if (data == null || data.Count == 0)
            {
                ShowInfoDialog("Import Form Data", "No valid field data found in the JSON file.");
                return;
            }
            var newBytes = Tab.FormService.ImportFormData(Tab.PdfBytes, data);
            Tab.UpdatePdfBytes(newBytes, "Import Form Data");
            ShowInfoDialog("Import Form Data", $"Imported {data.Count} field value(s).");
        }
        catch (Exception ex)
        {
            ShowInfoDialog("Error", $"Failed to import form data: {ex.Message}");
        }
    }

    private void OnAddTextFieldClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;
        try
        {
            var fieldName = $"TextField_{DateTime.Now:HHmmss}";
            var newBytes = Tab.FormService.AddTextField(Tab.PdfBytes, Tab.CurrentPageIndex, fieldName, 50, 700, 200, 20, "");
            Tab.UpdatePdfBytes(newBytes, "Add Text Field");
            ShowInfoDialog("Add Field", $"Added text field \"{fieldName}\" at page {Tab.CurrentPageIndex + 1}.");
        }
        catch (Exception ex)
        {
            ShowInfoDialog("Error", $"Failed to add text field: {ex.Message}");
        }
    }

    private void OnAddCheckboxFieldClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;
        try
        {
            var fieldName = $"Checkbox_{DateTime.Now:HHmmss}";
            var newBytes = Tab.FormService.AddCheckboxField(Tab.PdfBytes, Tab.CurrentPageIndex, fieldName, 50, 700, 15, 15);
            Tab.UpdatePdfBytes(newBytes, "Add Checkbox");
            ShowInfoDialog("Add Field", $"Added checkbox \"{fieldName}\" at page {Tab.CurrentPageIndex + 1}.");
        }
        catch (Exception ex)
        {
            ShowInfoDialog("Error", $"Failed to add checkbox: {ex.Message}");
        }
    }

    private void OnAddDropdownFieldClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;
        try
        {
            var fieldName = $"Dropdown_{DateTime.Now:HHmmss}";
            var options = new[] { "Option 1", "Option 2", "Option 3" };
            var newBytes = Tab.FormService.AddDropdownField(Tab.PdfBytes, Tab.CurrentPageIndex, fieldName, 50, 700, 150, 20, options);
            Tab.UpdatePdfBytes(newBytes, "Add Dropdown");
            ShowInfoDialog("Add Field", $"Added dropdown \"{fieldName}\" at page {Tab.CurrentPageIndex + 1}.");
        }
        catch (Exception ex)
        {
            ShowInfoDialog("Error", $"Failed to add dropdown: {ex.Message}");
        }
    }

    private void OnAddRadioButtonFieldClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;

        var dialog = new Window
        {
            Title = "Add Radio Button Group",
            Width = 400, Height = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new StackPanel { Margin = new Thickness(15), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Add Radio Button Group", FontSize = 16, FontWeight = FontWeight.SemiBold });

        panel.Children.Add(new TextBlock { Text = "Group Name:", FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
        var nameBox = new TextBox { Text = $"RadioGroup_{DateTime.Now:HHmmss}", Padding = new Thickness(4, 2) };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock { Text = "Options (one per line):", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        var optionsBox = new TextBox
        {
            AcceptsReturn = true, Height = 100,
            Text = "Option 1\nOption 2\nOption 3",
            Padding = new Thickness(4, 2)
        };
        panel.Children.Add(optionsBox);

        var addBtn = new Button { Content = "Add", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        addBtn.Click += (s, ev) =>
        {
            try
            {
                var groupName = nameBox.Text?.Trim() ?? "RadioGroup";
                var options = (optionsBox.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(o => o.Trim()).Where(o => o.Length > 0).ToArray();
                if (options.Length < 2)
                {
                    ShowInfoDialog("Error", "Please enter at least 2 options.");
                    return;
                }
                var newBytes = Tab.FormService.AddRadioButtonField(Tab.PdfBytes, Tab.CurrentPageIndex, groupName, 50, 700, 15, 15, options);
                Tab.UpdatePdfBytes(newBytes, "Add Radio Button Group");
                dialog.Close();
                ShowInfoDialog("Add Field", $"Added radio button group \"{groupName}\" with {options.Length} options on page {Tab.CurrentPageIndex + 1}.");
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error", $"Failed to add radio button group: {ex.Message}");
            }
        };
        panel.Children.Add(addBtn);

        dialog.Content = panel;
        dialog.Show(this);
    }

    private void OnAddSignatureFieldClick2(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;
        try
        {
            var fieldName = $"Signature_{DateTime.Now:HHmmss}";
            var newBytes = Tab.FormService.AddSignatureField(Tab.PdfBytes, Tab.CurrentPageIndex, fieldName, 50, 50, 200, 80);
            Tab.UpdatePdfBytes(newBytes, "Add Signature Field (Form)");
            ShowInfoDialog("Add Field", $"Added signature form field \"{fieldName}\" on page {Tab.CurrentPageIndex + 1}.");
        }
        catch (Exception ex)
        {
            ShowInfoDialog("Error", $"Failed to add signature field: {ex.Message}");
        }
    }

    private void OnEditFieldPropertiesClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;

        var fields = Tab.FormService.GetFormFields(Tab.PdfBytes);
        if (fields.Count == 0)
        {
            ShowInfoDialog("Field Properties", "No form fields found in this document.");
            return;
        }

        var dialog = new Window
        {
            Title = "Edit Form Field Properties",
            Width = 500, Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var mainPanel = new StackPanel { Margin = new Thickness(15), Spacing = 8 };
        mainPanel.Children.Add(new TextBlock { Text = "Edit Form Field Properties", FontSize = 16, FontWeight = FontWeight.SemiBold });
        mainPanel.Children.Add(new TextBlock { Text = $"Found {fields.Count} form field(s). Select a field to edit:", FontSize = 12, Opacity = 0.7 });

        var fieldCombo = new ComboBox { MinWidth = 300 };
        foreach (var f in fields)
            fieldCombo.Items.Add($"{f.Name} ({f.FieldType})");
        if (fields.Count > 0) fieldCombo.SelectedIndex = 0;
        mainPanel.Children.Add(fieldCombo);

        var propsPanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0) };

        var readOnlyCb = new CheckBox { Content = "Read Only" };
        var requiredCb = new CheckBox { Content = "Required" };
        propsPanel.Children.Add(readOnlyCb);
        propsPanel.Children.Add(requiredCb);

        propsPanel.Children.Add(new TextBlock { Text = "Default Value:", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        var defaultValueBox = new TextBox { Padding = new Thickness(4, 2) };
        propsPanel.Children.Add(defaultValueBox);

        mainPanel.Children.Add(propsPanel);

        // Update props when field selection changes
        void UpdatePropsDisplay()
        {
            if (fieldCombo.SelectedIndex >= 0 && fieldCombo.SelectedIndex < fields.Count)
            {
                var f = fields[fieldCombo.SelectedIndex];
                readOnlyCb.IsChecked = f.IsReadOnly;
                requiredCb.IsChecked = f.IsRequired;
                defaultValueBox.Text = f.DefaultValue;
            }
        }
        UpdatePropsDisplay();
        fieldCombo.SelectionChanged += (s, ev) => UpdatePropsDisplay();

        var btnRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var applyBtn = new Button { Content = "Apply", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        applyBtn.Click += (s, ev) =>
        {
            if (fieldCombo.SelectedIndex < 0 || fieldCombo.SelectedIndex >= fields.Count) return;
            try
            {
                var f = fields[fieldCombo.SelectedIndex];
                var newBytes = Tab.FormService.SetFieldProperties(
                    Tab.PdfBytes, f.Name,
                    readOnlyCb.IsChecked,
                    requiredCb.IsChecked,
                    defaultValueBox.Text);
                Tab.UpdatePdfBytes(newBytes, $"Edit Field Properties: {f.Name}");
                dialog.Close();
                ShowInfoDialog("Field Properties", $"Updated properties for field \"{f.Name}\".");
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error", $"Failed to update field: {ex.Message}");
            }
        };
        btnRow.Children.Add(applyBtn);

        var cancelBtn = new Button { Content = "Cancel", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancelBtn.Click += (s, ev) => dialog.Close();
        btnRow.Children.Add(cancelBtn);

        mainPanel.Children.Add(btnRow);
        dialog.Content = mainPanel;
        dialog.Show(this);
    }

    #endregion

    #region Digital Signatures

    private async void OnSignDocumentClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;

        // Pick certificate file
        var sp = this.StorageProvider;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Certificate File (PFX/P12)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Certificate") { Patterns = new[] { "*.pfx", "*.p12" } } }
        });
        if (files == null || files.Count == 0) return;

        var certPath = files[0].Path.LocalPath;

        // Show signing options dialog
        var dialog = new Window
        {
            Title = "Sign Document",
            Width = 420,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new StackPanel { Margin = new Thickness(15), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Sign PDF Document", FontSize = 16, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"Certificate: {IOPath.GetFileName(certPath)}", FontSize = 11, Opacity = 0.6 });

        panel.Children.Add(new TextBlock { Text = "Certificate Password:", FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
        var pwdBox = new TextBox { PasswordChar = '*', Padding = new Thickness(4, 2) };
        panel.Children.Add(pwdBox);

        panel.Children.Add(new TextBlock { Text = "Reason:", FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
        var reasonBox = new TextBox { Padding = new Thickness(4, 2), Text = "Document approval" };
        panel.Children.Add(reasonBox);

        panel.Children.Add(new TextBlock { Text = "Location:", FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
        var locationBox = new TextBox { Padding = new Thickness(4, 2) };
        panel.Children.Add(locationBox);

        var visibleCb = new CheckBox { Content = "Visible signature", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(visibleCb);

        var signBtn = new Button { Content = "Sign", MinWidth = 100, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        signBtn.Click += (s2, e2) =>
        {
            try
            {
                var options = new PDFEditor.Core.Abstractions.SigningOptions
                {
                    CertificatePath = certPath,
                    CertificatePassword = pwdBox.Text ?? "",
                    Reason = reasonBox.Text ?? "",
                    Location = locationBox.Text ?? "",
                    PageIndex = Tab.CurrentPageIndex,
                    IsVisible = visibleCb.IsChecked == true,
                    X = 50, Y = 50, Width = 200, Height = 80
                };
                var signed = Tab.SignatureService2.SignDocument(Tab.PdfBytes!, options);
                Tab.UpdatePdfBytes(signed, "Sign Document");
                dialog.Close();
                ShowInfoDialog("Sign Document", "Document signed successfully.");
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error", $"Signing failed: {ex.Message}");
            }
        };
        panel.Children.Add(signBtn);

        dialog.Content = panel;
        await dialog.ShowDialog(this);
    }

    private void OnVerifySignaturesClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;
        try
        {
            var sigs = Tab.SignatureService2.VerifySignatures(Tab.PdfBytes);
            if (sigs.Count == 0)
            {
                ShowInfoDialog("Verify Signatures", "No digital signatures found in this document.");
                return;
            }
            var msg = $"Found {sigs.Count} signature(s):\n\n";
            foreach (var s in sigs)
            {
                msg += $"  Field: {s.FieldName}\n";
                msg += $"  Signer: {s.SignerName}\n";
                msg += $"  Date: {s.SignDate?.ToString("g") ?? "Unknown"}\n";
                msg += $"  Valid: {(s.IsValid ? "YES" : "NO")} - {s.ValidationMessage}\n";
                msg += $"  Covers whole doc: {s.CoversWholeDocument}\n\n";
            }
            ShowInfoDialog("Verify Signatures", msg);
        }
        catch (Exception ex)
        {
            ShowInfoDialog("Error", $"Verification failed: {ex.Message}");
        }
    }

    private void OnListSignaturesClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;
        var sigs = Tab.SignatureService2.GetSignatures(Tab.PdfBytes);
        if (sigs.Count == 0)
        {
            ShowInfoDialog("Signatures", "No signatures found.");
            return;
        }
        var msg = $"{sigs.Count} signature(s):\n\n";
        foreach (var s in sigs)
        {
            msg += $"  {s.FieldName}: {s.SignerName}";
            if (!string.IsNullOrEmpty(s.Reason)) msg += $" ({s.Reason})";
            if (s.SignDate != null) msg += $" on {s.SignDate:g}";
            msg += "\n";
        }
        ShowInfoDialog("Signatures", msg);
    }

    private void OnAddSignatureFieldClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;
        try
        {
            var fieldName = $"Signature_{DateTime.Now:HHmmss}";
            var newBytes = Tab.SignatureService2.AddSignatureField(Tab.PdfBytes, Tab.CurrentPageIndex, fieldName, 50, 50, 200, 80);
            Tab.UpdatePdfBytes(newBytes, "Add Signature Field");
            ShowInfoDialog("Add Signature Field", $"Added signature field \"{fieldName}\" on page {Tab.CurrentPageIndex + 1}.");
        }
        catch (Exception ex)
        {
            ShowInfoDialog("Error", $"Failed to add signature field: {ex.Message}");
        }
    }

    private async void OnCertificateManagerClick(object? sender, RoutedEventArgs e)
    {
        var certService = new PDFEditor.Core.Services.CertificateManagerService();

        var dialog = new Window
        {
            Title = "Certificate Manager",
            Width = 680,
            Height = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var mainStack = new StackPanel { Margin = new Thickness(14), Spacing = 8 };
        mainStack.Children.Add(new TextBlock
        {
            Text = "Certificate Manager",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold
        });
        mainStack.Children.Add(new TextBlock
        {
            Text = "Certificates available for digital signing:",
            FontSize = 11,
            Opacity = 0.7
        });

        // Certificate list
        var certList = new ListBox
        {
            Height = 180,
            Margin = new Thickness(0, 4, 0, 0),
            SelectionMode = SelectionMode.Single
        };

        var detailBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            Height = 160,
            FontFamily = new Avalonia.Media.FontFamily("Cascadia Code,Consolas,Courier New,monospace"),
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            Text = "Select a certificate to see details."
        };

        // Load store certificates
        var storeCerts = certService.ListStoreCertificates();
        var allCerts = new List<PDFEditor.Core.Services.CertificateInfo>(storeCerts);

        void RefreshList()
        {
            certList.Items.Clear();
            foreach (var c in allCerts)
            {
                var statusMark = c.IsValid ? "✓" : (c.IsExpired ? "✗" : "!");
                certList.Items.Add($"{statusMark} {c.DisplayName}  —  {c.Source}  (exp. {c.NotAfter:yyyy-MM-dd})");
            }
        }

        certList.SelectionChanged += (_, _) =>
        {
            var idx = certList.SelectedIndex;
            if (idx >= 0 && idx < allCerts.Count)
                detailBox.Text = allCerts[idx].GetSummary();
        };

        RefreshList();

        // Buttons row
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        var inspectBtn = new Button { Content = "Inspect PFX File...", Padding = new Thickness(10, 4) };
        inspectBtn.Click += async (s2, e2) =>
        {
            var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Certificate (PFX/P12)",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Certificate") { Patterns = new[] { "*.pfx", "*.p12" } } }
            });
            if (files == null || files.Count == 0) return;

            var certPath = files[0].Path.LocalPath;

            // Ask for password
            var pwdDialog = new Window { Title = "Certificate Password", Width = 320, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var pwdPanel = new StackPanel { Margin = new Thickness(12), Spacing = 8 };
            pwdPanel.Children.Add(new TextBlock { Text = $"Password for: {IOPath.GetFileName(certPath)}", FontSize = 11 });
            var pwdBox = new TextBox { PasswordChar = '*', Padding = new Thickness(4, 2) };
            pwdPanel.Children.Add(pwdBox);
            var okBtn = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(10, 4) };
            string? pwd = null;
            okBtn.Click += (_, _) => { pwd = pwdBox.Text ?? ""; pwdDialog.Close(); };
            pwdPanel.Children.Add(okBtn);
            pwdDialog.Content = pwdPanel;
            await pwdDialog.ShowDialog(dialog);
            if (pwd == null) return;

            var (ok, info, err) = certService.TryInspectCertificateFile(certPath, pwd);
            if (!ok || info == null)
            {
                ShowInfoDialog("Certificate Error", $"Could not read certificate:\n{err}");
                return;
            }

            // Add to list if not already present
            if (!allCerts.Any(c => c.Thumbprint == info.Thumbprint))
            {
                allCerts.Add(info);
                RefreshList();
            }

            // Select and show details
            var newIdx = allCerts.FindIndex(c => c.Thumbprint == info.Thumbprint);
            if (newIdx >= 0) certList.SelectedIndex = newIdx;
        };
        btnPanel.Children.Add(inspectBtn);

        var validateBtn = new Button { Content = "Validate Chain", Padding = new Thickness(10, 4) };
        validateBtn.Click += (_,_) =>
        {
            var idx = certList.SelectedIndex;
            if (idx < 0 || idx >= allCerts.Count) { ShowInfoDialog("Validate", "Select a certificate first."); return; }
            var cert = allCerts[idx];
            if (cert.Source != "File" || string.IsNullOrEmpty(cert.FilePath)) { ShowInfoDialog("Validate", "Chain validation requires a PFX file. Select a file-based certificate."); return; }

            var (valid, errors) = certService.ValidateCertificateChain(cert.FilePath, "");
            var msg = valid ? "Chain builds successfully.\n" : "Chain has issues:\n";
            if (errors.Length > 0) msg += string.Join("\n", errors.Select(e => $"  • {e}"));
            ShowInfoDialog("Certificate Chain", msg);
        };
        btnPanel.Children.Add(validateBtn);

        var reportBtn = new Button { Content = "Copy Report", Padding = new Thickness(10, 4) };
        reportBtn.Click += async (_,_) =>
        {
            var report = certService.GenerateCertificateReport(allCerts);
            if (TopLevel.GetTopLevel(this)?.Clipboard != null)
                await TopLevel.GetTopLevel(this)!.Clipboard!.SetTextAsync(report);
            ShowInfoDialog("Report", "Certificate report copied to clipboard.");
        };
        btnPanel.Children.Add(reportBtn);

        var refreshBtn = new Button { Content = "↺ Refresh Store", Padding = new Thickness(10, 4) };
        refreshBtn.Click += (_,_) =>
        {
            allCerts.Clear();
            allCerts.AddRange(certService.ListStoreCertificates());
            RefreshList();
            detailBox.Text = $"{allCerts.Count} certificate(s) loaded from Windows store.";
        };
        btnPanel.Children.Add(refreshBtn);

        mainStack.Children.Add(certList);
        mainStack.Children.Add(detailBox);
        mainStack.Children.Add(btnPanel);

        var closeBtn = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(12, 4),
            Margin = new Thickness(0, 6, 0, 0)
        };
        closeBtn.Click += (_,_) => dialog.Close();
        mainStack.Children.Add(closeBtn);

        dialog.Content = new ScrollViewer { Content = mainStack };
        await dialog.ShowDialog(this);
    }

    private void OnAnnotationListClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null) return;

        var annotations = Tab.Annotations;
        if (annotations.Count == 0)
        {
            ShowInfoDialog("Annotations", "No annotations in this document.");
            return;
        }

        var dialog = new Window
        {
            Title = "Annotation List",
            Width = 550,
            Height = 450,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new DockPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock
        {
            Text = $"{annotations.Count} Annotation(s)",
            FontSize = 15, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
            [DockPanel.DockProperty] = Dock.Top
        });

        var list = new ListBox { Margin = new Thickness(0, 4, 0, 4) };
        foreach (var ann in annotations)
        {
            var label = $"[{ann.Type}] Page {ann.PageIndex + 1}";
            if (!string.IsNullOrEmpty(ann.Text)) label += $": \"{ann.Text}\"";
            if (ann.Type == AnnotationType.Stamp) label += $" ({ann.StampPreset})";
            list.Items.Add(new ListBoxItem { Content = label, Tag = ann });
        }
        panel.Children.Add(list);

        var btnRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0),
            [DockPanel.DockProperty] = Dock.Bottom
        };

        var goToBtn = new Button { Content = "Go To Page", MinWidth = 90 };
        goToBtn.Click += (s2, e2) =>
        {
            if (list.SelectedItem is ListBoxItem item && item.Tag is PdfAnnotation ann)
            {
                Tab.CurrentPageIndex = ann.PageIndex;
                dialog.Close();
            }
        };
        btnRow.Children.Add(goToBtn);

        var deleteBtn = new Button { Content = "Delete", MinWidth = 70, Foreground = Avalonia.Media.Brushes.OrangeRed };
        deleteBtn.Click += (s2, e2) =>
        {
            if (list.SelectedItem is ListBoxItem item && item.Tag is PdfAnnotation ann)
            {
                Tab.Annotations.Remove(ann);
                list.Items.Remove(item);
            }
        };
        btnRow.Children.Add(deleteBtn);

        panel.Children.Add(btnRow);
        dialog.Content = panel;
        dialog.Show(this);
    }

    private void ShowInfoDialog(string title, string message)
    {
        var w = new Window
        {
            Title = title,
            Width = 450,
            MinHeight = 150,
            MaxHeight = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Thickness(15),
                    FontSize = 13
                }
            }
        };
        w.Show(this);
    }

    #endregion

    #region Redaction

    private void OnRedactTextClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;

        var dialog = new Window
        {
            Title = "Redact Text",
            Width = 420, Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new StackPanel { Margin = new Thickness(15), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Redact Text", FontSize = 16, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Enter text to permanently redact from the document:", FontSize = 12, Opacity = 0.7 });

        var textBox = new TextBox { Watermark = "Text to redact...", Padding = new Thickness(6, 4) };
        panel.Children.Add(textBox);

        var caseCb = new CheckBox { Content = "Case sensitive", IsChecked = false };
        panel.Children.Add(caseCb);

        var btnRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var applyBtn = new Button { Content = "Redact", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        applyBtn.Click += (s, ev) =>
        {
            var text = textBox.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                var targets = Tab.RedactionService.FindRedactionTargets(Tab.PdfBytes, text, caseCb.IsChecked == true);
                if (targets.Count == 0)
                {
                    ShowInfoDialog("Redaction", $"No occurrences of \"{text}\" found in the document.");
                    return;
                }

                var newBytes = Tab.RedactionService.RedactText(Tab.PdfBytes, text, caseCb.IsChecked == true);
                Tab.UpdatePdfBytes(newBytes, $"Redact text: \"{text}\"");
                dialog.Close();
                ShowInfoDialog("Redaction", $"Redacted {targets.Count} occurrence(s) of \"{text}\".");
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error", $"Redaction failed: {ex.Message}");
            }
        };
        btnRow.Children.Add(applyBtn);

        var cancelBtn = new Button { Content = "Cancel", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancelBtn.Click += (s, ev) => dialog.Close();
        btnRow.Children.Add(cancelBtn);

        panel.Children.Add(btnRow);
        dialog.Content = panel;
        dialog.Show(this);
    }

    private void OnRedactPageClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;
        try
        {
            var newBytes = Tab.RedactionService.RedactPages(Tab.PdfBytes, new[] { Tab.CurrentPageIndex });
            Tab.UpdatePdfBytes(newBytes, $"Redact page {Tab.CurrentPageIndex + 1}");
            ShowInfoDialog("Redaction", $"Page {Tab.CurrentPageIndex + 1} content has been permanently redacted.");
        }
        catch (Exception ex)
        {
            ShowInfoDialog("Error", $"Page redaction failed: {ex.Message}");
        }
    }

    private void OnFindRedactionTargetsClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;

        var dialog = new Window
        {
            Title = "Find Redaction Targets",
            Width = 450, Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new StackPanel { Margin = new Thickness(15), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Preview text to redact (without applying):", FontSize = 12 });

        var textBox = new TextBox { Watermark = "Text to search...", Padding = new Thickness(6, 4) };
        panel.Children.Add(textBox);

        var caseCb = new CheckBox { Content = "Case sensitive", IsChecked = false };
        panel.Children.Add(caseCb);

        var findBtn = new Button { Content = "Find", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        findBtn.Click += (s, ev) =>
        {
            var text = textBox.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                var targets = Tab.RedactionService.FindRedactionTargets(Tab.PdfBytes, text, caseCb.IsChecked == true);
                if (targets.Count == 0)
                {
                    ShowInfoDialog("Redaction Preview", $"No occurrences of \"{text}\" found.");
                }
                else
                {
                    var byPage = targets.GroupBy(t => t.PageIndex + 1);
                    var msg = $"Found {targets.Count} occurrence(s) of \"{text}\":\n\n";
                    foreach (var g in byPage.OrderBy(g => g.Key))
                        msg += $"  Page {g.Key}: {g.Count()} occurrence(s)\n";
                    msg += "\nUse 'Redact Text...' to permanently remove these.";
                    ShowInfoDialog("Redaction Preview", msg);
                }
                dialog.Close();
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error", $"Search failed: {ex.Message}");
            }
        };
        panel.Children.Add(findBtn);

        dialog.Content = panel;
        dialog.Show(this);
    }

    #endregion

    #region Document Comparison

    private async void OnCompareDocumentsClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null)
        {
            ShowInfoDialog("Compare", "Open a document first to use as the left/baseline document.");
            return;
        }

        var dlg = new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select document to compare with...",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("PDF Files") { Patterns = new[] { "*.pdf" } }
            }
        };

        var files = await StorageProvider.OpenFilePickerAsync(dlg);
        if (files == null || files.Count == 0) return;

        try
        {
            var rightPath = files[0].Path.LocalPath;
            var rightBytes = File.ReadAllBytes(rightPath);
            var leftName = IOPath.GetFileName(Tab.FilePath ?? "Current Document");
            var rightName = IOPath.GetFileName(rightPath);

            var result = Tab.ComparisonService.Compare(Tab.PdfBytes, rightBytes, leftName, rightName);

            // Show result in a dialog with option to save report
            var reportDialog = new Window
            {
                Title = $"Comparison: {leftName} vs {rightName}",
                Width = 700, Height = 550,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var mainPanel = new DockPanel { Margin = new Thickness(10) };

            // Summary header
            var summaryText = result.AreIdentical
                ? "Documents are text-identical."
                : $"{result.TotalDifferences} difference(s): {result.AddedCount} added, {result.RemovedCount} removed, {result.ModifiedCount} modified";
            var summaryBlock = new TextBlock
            {
                Text = summaryText,
                FontSize = 14, FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                [DockPanel.DockProperty] = Dock.Top
            };
            mainPanel.Children.Add(summaryBlock);

            // Report content
            var reportText = Tab.ComparisonService.GenerateReport(result);
            var textViewer = new TextBox
            {
                Text = reportText,
                IsReadOnly = true,
                AcceptsReturn = true,
                FontFamily = new Avalonia.Media.FontFamily("Consolas, Courier New, monospace"),
                FontSize = 11,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
            var scrollViewer = new ScrollViewer { Content = textViewer };

            // Buttons
            var btnRow = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 8, 0, 0),
                [DockPanel.DockProperty] = Dock.Bottom
            };

            var saveTextBtn = new Button { Content = "Save Text Report...", MinWidth = 120 };
            saveTextBtn.Click += async (s2, e2) =>
            {
                var saveDlg = new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Save Comparison Report",
                    DefaultExtension = "txt",
                    SuggestedFileName = $"comparison_{leftName}_vs_{rightName}.txt",
                    FileTypeChoices = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                    }
                };
                var saveFile = await StorageProvider.SaveFilePickerAsync(saveDlg);
                if (saveFile != null)
                {
                    await File.WriteAllTextAsync(saveFile.Path.LocalPath, reportText);
                    ShowInfoDialog("Saved", $"Report saved to {saveFile.Path.LocalPath}");
                }
            };
            btnRow.Children.Add(saveTextBtn);

            var saveHtmlBtn = new Button { Content = "Save HTML Report...", MinWidth = 120 };
            saveHtmlBtn.Click += async (s2, e2) =>
            {
                var htmlReport = Tab.ComparisonService.GenerateHtmlReport(result);
                var saveDlg = new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Save HTML Comparison Report",
                    DefaultExtension = "html",
                    SuggestedFileName = $"comparison_{leftName}_vs_{rightName}.html",
                    FileTypeChoices = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("HTML Files") { Patterns = new[] { "*.html" } }
                    }
                };
                var saveFile = await StorageProvider.SaveFilePickerAsync(saveDlg);
                if (saveFile != null)
                {
                    await File.WriteAllTextAsync(saveFile.Path.LocalPath, htmlReport);
                    ShowInfoDialog("Saved", $"HTML report saved to {saveFile.Path.LocalPath}");
                }
            };
            btnRow.Children.Add(saveHtmlBtn);

            mainPanel.Children.Add(btnRow);
            mainPanel.Children.Add(scrollViewer);

            reportDialog.Content = mainPanel;
            reportDialog.Show(this);
        }
        catch (Exception ex)
        {
            ShowInfoDialog("Error", $"Comparison failed: {ex.Message}");
        }
    }

    #endregion

    #region Security / Permissions

    private void OnPermissionManagerClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;

        var dialog = new Window
        {
            Title = "Encrypt & Set Permissions",
            Width = 440, Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new StackPanel { Margin = new Thickness(15), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Encrypt & Set Permissions", FontSize = 16, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Set passwords, encryption level, and document permissions.", FontSize = 12, Opacity = 0.7 });

        panel.Children.Add(new TextBlock { Text = "Encryption Level:", FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        var encLevelCombo = new ComboBox { MinWidth = 250 };
        encLevelCombo.Items.Add("256-bit AES (recommended)");
        encLevelCombo.Items.Add("128-bit AES");
        encLevelCombo.SelectedIndex = 0;
        panel.Children.Add(encLevelCombo);

        panel.Children.Add(new TextBlock { Text = "User Password (to open document):", FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
        var userPwBox = new TextBox { Watermark = "(optional - leave empty for no open password)", PasswordChar = '*', Padding = new Thickness(6, 4) };
        panel.Children.Add(userPwBox);

        panel.Children.Add(new TextBlock { Text = "Owner Password (to change permissions):", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        var ownerPwBox = new TextBox { Watermark = "(required)", PasswordChar = '*', Padding = new Thickness(6, 4) };
        panel.Children.Add(ownerPwBox);

        panel.Children.Add(new TextBlock { Text = "Permissions:", FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });

        var printCb = new CheckBox { Content = "Allow Printing", IsChecked = true };
        var copyCb = new CheckBox { Content = "Allow Copy / Text Extraction", IsChecked = true };
        var editCb = new CheckBox { Content = "Allow Content Editing", IsChecked = false };
        panel.Children.Add(printCb);
        panel.Children.Add(copyCb);
        panel.Children.Add(editCb);

        var btnRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var applyBtn = new Button { Content = "Apply", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        applyBtn.Click += (s, ev) =>
        {
            var ownerPw = ownerPwBox.Text?.Trim();
            if (string.IsNullOrEmpty(ownerPw))
            {
                ShowInfoDialog("Error", "Owner password is required.");
                return;
            }

            try
            {
                var userPw = string.IsNullOrEmpty(userPwBox.Text?.Trim()) ? null : userPwBox.Text!.Trim();
                var encLevel = encLevelCombo.SelectedIndex == 1
                    ? PDFEditor.Core.Services.PdfEncryptionLevel.Aes128
                    : PDFEditor.Core.Services.PdfEncryptionLevel.Aes256;
                var newBytes = Tab.SecurityService.Encrypt(
                    Tab.PdfBytes,
                    userPw,
                    ownerPw,
                    printCb.IsChecked == true,
                    copyCb.IsChecked == true,
                    editCb.IsChecked == true,
                    encLevel);
                Tab.UpdatePdfBytes(newBytes, "Encrypt / Set Permissions");
                dialog.Close();

                var levelName = encLevel == PDFEditor.Core.Services.PdfEncryptionLevel.Aes128 ? "128-bit AES" : "256-bit AES";
                var perms = new List<string>();
                if (printCb.IsChecked == true) perms.Add("Print");
                if (copyCb.IsChecked == true) perms.Add("Copy");
                if (editCb.IsChecked == true) perms.Add("Edit");
                ShowInfoDialog("Encryption", $"Document encrypted with {levelName}.\nUser password: {(userPw != null ? "Set" : "None")}\nPermissions: {string.Join(", ", perms)}");
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error", $"Encryption failed: {ex.Message}");
            }
        };
        btnRow.Children.Add(applyBtn);

        var cancelBtn = new Button { Content = "Cancel", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancelBtn.Click += (s, ev) => dialog.Close();
        btnRow.Children.Add(cancelBtn);

        panel.Children.Add(btnRow);
        dialog.Content = panel;
        dialog.Show(this);
    }

    private void OnDecryptDocumentClick(object? sender, RoutedEventArgs e)
    {
        if (Tab?.PdfBytes == null) return;

        if (!Tab.SecurityService.IsEncrypted(Tab.PdfBytes))
        {
            ShowInfoDialog("Decrypt", "This document is not encrypted.");
            return;
        }

        var dialog = new Window
        {
            Title = "Decrypt Document",
            Width = 400, Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new StackPanel { Margin = new Thickness(15), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Enter password to decrypt:", FontSize = 13 });

        var pwBox = new TextBox { PasswordChar = '*', Padding = new Thickness(6, 4), Watermark = "Password..." };
        panel.Children.Add(pwBox);

        var btnRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var decryptBtn = new Button { Content = "Decrypt", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        decryptBtn.Click += (s, ev) =>
        {
            var pw = pwBox.Text?.Trim();
            if (string.IsNullOrEmpty(pw)) return;

            try
            {
                var decrypted = Tab.SecurityService.Decrypt(Tab.PdfBytes, pw);
                Tab.UpdatePdfBytes(decrypted, "Decrypt Document");
                dialog.Close();
                ShowInfoDialog("Decrypt", "Document decrypted successfully.");
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error", $"Decryption failed: {ex.Message}");
            }
        };
        btnRow.Children.Add(decryptBtn);

        var cancelBtn = new Button { Content = "Cancel", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancelBtn.Click += (s, ev) => dialog.Close();
        btnRow.Children.Add(cancelBtn);

        panel.Children.Add(btnRow);
        dialog.Content = panel;
        dialog.Show(this);
    }

    #endregion

    #region Annotation Export

    private async void OnExportAnnotationReportClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null) return;

        var annotations = Tab.Annotations;
        if (annotations.Count == 0)
        {
            ShowInfoDialog("Export Report", "No annotations to export.");
            return;
        }

        var saveDlg = new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export Annotation Report",
            DefaultExtension = "html",
            SuggestedFileName = $"annotations_{IOPath.GetFileNameWithoutExtension(Tab.FilePath ?? "document")}.html",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("HTML Report") { Patterns = new[] { "*.html" } },
                new Avalonia.Platform.Storage.FilePickerFileType("Text Report") { Patterns = new[] { "*.txt" } },
                new Avalonia.Platform.Storage.FilePickerFileType("CSV Export") { Patterns = new[] { "*.csv" } }
            }
        };

        var file = await StorageProvider.SaveFilePickerAsync(saveDlg);
        if (file == null) return;

        try
        {
            var filePath = file.Path.LocalPath;
            var ext = IOPath.GetExtension(filePath).ToLowerInvariant();
            var docName = IOPath.GetFileName(Tab.FilePath ?? "document.pdf");
            string content;

            if (ext is ".csv")
                content = Tab.AnnotationExportService.GenerateCsvReport(annotations);
            else if (ext is ".txt")
                content = Tab.AnnotationExportService.GenerateTextReport(annotations, docName);
            else
                content = Tab.AnnotationExportService.GenerateHtmlReport(annotations, docName);

            await File.WriteAllTextAsync(filePath, content);
            ShowInfoDialog("Export Report", $"Annotation report ({annotations.Count} annotations) saved to:\n{filePath}");
        }
        catch (Exception ex)
        {
            ShowInfoDialog("Error", $"Failed to export report: {ex.Message}");
        }
    }

    private async void OnExportXfdfClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null) return;

        var annotations = Tab.Annotations;
        if (annotations.Count == 0)
        {
            ShowInfoDialog("Export XFDF", "No annotations to export.");
            return;
        }

        var saveDlg = new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export Annotations to XFDF",
            DefaultExtension = "xfdf",
            SuggestedFileName = IOPath.GetFileNameWithoutExtension(Tab.FilePath ?? "document") + ".xfdf",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("XFDF Files") { Patterns = new[] { "*.xfdf" } }
            }
        };

        var file = await StorageProvider.SaveFilePickerAsync(saveDlg);
        if (file == null) return;

        try
        {
            await Tab.XfdfAnnotationService.ExportToFileAsync(
                annotations.ToList(), file.Path.LocalPath, Tab.FilePath);
            ShowInfoDialog("Export XFDF", $"Exported {annotations.Count} annotation(s) to XFDF.");
        }
        catch (Exception ex)
        {
            ShowInfoDialog("Error", $"XFDF export failed: {ex.Message}");
        }
    }

    private async void OnImportXfdfClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Annotations from XFDF",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("XFDF Files") { Patterns = new[] { "*.xfdf" } } }
        });
        if (files == null || files.Count == 0) return;

        try
        {
            var imported = await Tab.XfdfAnnotationService.ImportFromFileAsync(files[0].Path.LocalPath);
            if (imported.Count == 0)
            {
                ShowInfoDialog("Import XFDF", "No annotations found in the XFDF file.");
                return;
            }

            foreach (var ann in imported)
                Tab.Annotations.Add(ann);

            ShowInfoDialog("Import XFDF", $"Imported {imported.Count} annotation(s) from XFDF.");
        }
        catch (Exception ex)
        {
            ShowInfoDialog("Error", $"XFDF import failed: {ex.Message}");
        }
    }

    private void OnAnnotationPropertiesClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null) return;

        var annotations = Tab.Annotations;
        if (annotations.Count == 0)
        {
            ShowInfoDialog("Annotation Properties", "No annotations in this document.");
            return;
        }

        var dialog = new Window
        {
            Title = "Annotation Properties",
            Width = 500, Height = 550,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var mainPanel = new StackPanel { Margin = new Thickness(15), Spacing = 8 };
        mainPanel.Children.Add(new TextBlock { Text = "Annotation Properties", FontSize = 16, FontWeight = FontWeight.SemiBold });
        mainPanel.Children.Add(new TextBlock { Text = "Select an annotation to view/edit its properties:", FontSize = 12, Opacity = 0.7 });

        var annoCombo = new ComboBox { MinWidth = 350 };
        for (int i = 0; i < annotations.Count; i++)
        {
            var a = annotations[i];
            annoCombo.Items.Add($"[{i + 1}] {a.Type} (Page {a.PageIndex + 1})");
        }
        if (annotations.Count > 0) annoCombo.SelectedIndex = 0;
        mainPanel.Children.Add(annoCombo);

        var propsPanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0) };

        var colorLabel = new TextBlock { Text = "Color:", FontSize = 12 };
        var colorBox = new TextBox { Padding = new Thickness(4, 2), Watermark = "#000000" };
        propsPanel.Children.Add(colorLabel);
        propsPanel.Children.Add(colorBox);

        var fillColorLabel = new TextBlock { Text = "Fill Color:", FontSize = 12 };
        var fillColorBox = new TextBox { Padding = new Thickness(4, 2), Watermark = "#FFFF00" };
        propsPanel.Children.Add(fillColorLabel);
        propsPanel.Children.Add(fillColorBox);

        var opacityLabel = new TextBlock { Text = "Opacity (0.0 - 1.0):", FontSize = 12 };
        var opacityBox = new TextBox { Padding = new Thickness(4, 2), Watermark = "0.30" };
        propsPanel.Children.Add(opacityLabel);
        propsPanel.Children.Add(opacityBox);

        var fontSizeLabel = new TextBlock { Text = "Font Size:", FontSize = 12 };
        var fontSizeBox = new TextBox { Padding = new Thickness(4, 2), Watermark = "14" };
        propsPanel.Children.Add(fontSizeLabel);
        propsPanel.Children.Add(fontSizeBox);

        var strokeLabel = new TextBlock { Text = "Stroke Width:", FontSize = 12 };
        var strokeBox = new TextBox { Padding = new Thickness(4, 2), Watermark = "1.0" };
        propsPanel.Children.Add(strokeLabel);
        propsPanel.Children.Add(strokeBox);

        var textLabel = new TextBlock { Text = "Text / Content:", FontSize = 12 };
        var textBox = new TextBox { Padding = new Thickness(4, 2), AcceptsReturn = true, Height = 60 };
        propsPanel.Children.Add(textLabel);
        propsPanel.Children.Add(textBox);

        mainPanel.Children.Add(propsPanel);

        void UpdatePropsDisplay()
        {
            if (annoCombo.SelectedIndex >= 0 && annoCombo.SelectedIndex < annotations.Count)
            {
                var a = annotations[annoCombo.SelectedIndex];
                colorBox.Text = a.Color;
                fillColorBox.Text = a.FillColor;
                opacityBox.Text = a.FillOpacity.ToString("F2");
                fontSizeBox.Text = a.FontSize.ToString("F1");
                strokeBox.Text = a.StrokeWidth.ToString("F1");
                textBox.Text = a.Text ?? a.NoteContent ?? a.StampText ?? "";
            }
        }
        UpdatePropsDisplay();
        annoCombo.SelectionChanged += (s, ev) => UpdatePropsDisplay();

        var btnRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var applyBtn = new Button { Content = "Apply", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        applyBtn.Click += (s, ev) =>
        {
            if (annoCombo.SelectedIndex < 0 || annoCombo.SelectedIndex >= annotations.Count) return;
            var a = annotations[annoCombo.SelectedIndex];
            if (!string.IsNullOrWhiteSpace(colorBox.Text)) a.Color = colorBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(fillColorBox.Text)) a.FillColor = fillColorBox.Text.Trim();
            if (float.TryParse(opacityBox.Text, out float op)) a.FillOpacity = Math.Clamp(op, 0f, 1f);
            if (float.TryParse(fontSizeBox.Text, out float fs)) a.FontSize = Math.Max(1f, fs);
            if (float.TryParse(strokeBox.Text, out float sw)) a.StrokeWidth = Math.Max(0.1f, sw);

            var txt = textBox.Text;
            if (a.Type == AnnotationType.StickyNote) a.NoteContent = txt;
            else if (a.Type == AnnotationType.Stamp) a.StampText = txt;
            else a.Text = txt;

            dialog.Close();
            ShowInfoDialog("Annotation Properties", "Annotation properties updated.");
        };
        btnRow.Children.Add(applyBtn);

        var deleteBtn = new Button { Content = "Delete", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        deleteBtn.Click += (s, ev) =>
        {
            if (annoCombo.SelectedIndex >= 0 && annoCombo.SelectedIndex < annotations.Count)
            {
                annotations.RemoveAt(annoCombo.SelectedIndex);
                dialog.Close();
                ShowInfoDialog("Annotation Properties", "Annotation deleted.");
            }
        };
        btnRow.Children.Add(deleteBtn);

        var cancelBtn = new Button { Content = "Cancel", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancelBtn.Click += (s, ev) => dialog.Close();
        btnRow.Children.Add(cancelBtn);

        mainPanel.Children.Add(btnRow);
        dialog.Content = mainPanel;
        dialog.Show(this);
    }

    private void OnCustomStampClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null) return;

        var dialog = new Window
        {
            Title = "Create Custom Stamp",
            Width = 420, Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new StackPanel { Margin = new Thickness(15), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Create Custom Stamp", FontSize = 16, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Design a custom stamp annotation for the current page.", FontSize = 12, Opacity = 0.7 });

        panel.Children.Add(new TextBlock { Text = "Stamp Text:", FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
        var stampTextBox = new TextBox { Text = "CUSTOM", Padding = new Thickness(4, 2) };
        panel.Children.Add(stampTextBox);

        panel.Children.Add(new TextBlock { Text = "Color (hex):", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        var colorBox = new TextBox { Text = "#FF0000", Padding = new Thickness(4, 2) };
        panel.Children.Add(colorBox);

        panel.Children.Add(new TextBlock { Text = "Font Size:", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        var fontSizeBox = new TextBox { Text = "24", Padding = new Thickness(4, 2) };
        panel.Children.Add(fontSizeBox);

        panel.Children.Add(new TextBlock { Text = "Rotation (degrees):", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        var rotationBox = new TextBox { Text = "-30", Padding = new Thickness(4, 2) };
        panel.Children.Add(rotationBox);

        panel.Children.Add(new TextBlock { Text = "Opacity (0.0 - 1.0):", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        var opacityBox = new TextBox { Text = "0.5", Padding = new Thickness(4, 2) };
        panel.Children.Add(opacityBox);

        var addBtn = new Button { Content = "Add Stamp", MinWidth = 100, HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        addBtn.Click += (s, ev) =>
        {
            var text = stampTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                ShowInfoDialog("Error", "Stamp text is required.");
                return;
            }

            float fontSize = 24f;
            float.TryParse(fontSizeBox.Text, out fontSize);
            double rotation = -30;
            double.TryParse(rotationBox.Text, out rotation);
            float opacity = 0.5f;
            float.TryParse(opacityBox.Text, out opacity);

            var stamp = new PDFEditor.Core.Services.PdfAnnotation
            {
                Type = PDFEditor.Core.Services.AnnotationType.Stamp,
                PageIndex = Tab.CurrentPageIndex,
                X = 0.2,
                Y = 0.3,
                Width = 0.6,
                Height = 0.15,
                StampPreset = PDFEditor.Core.Services.StampType.Custom,
                StampText = text,
                Color = colorBox.Text?.Trim() ?? "#FF0000",
                FontSize = Math.Max(1f, fontSize),
                Rotation = rotation,
                FillOpacity = Math.Clamp(opacity, 0f, 1f)
            };

            Tab.Annotations.Add(stamp);
            dialog.Close();
            ShowInfoDialog("Custom Stamp", $"Added custom stamp \"{text}\" on page {Tab.CurrentPageIndex + 1}.");
        };
        panel.Children.Add(addBtn);

        dialog.Content = panel;
        dialog.Show(this);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  Phase 6+ Service UI Handlers
    // ═══════════════════════════════════════════════════════════════

    #region Document Processing

    private async void OnAutoCropClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            Tab.StatusText = "Auto-cropping pages...";
            var svc = new AutoCropService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var result = await Task.Run(() => svc.AutoCrop(pdfBytes, 10f));
            var path = Tab.FilePath!;
            File.WriteAllBytes(path, result);
            Tab.LoadPdf(Tab.FilePath!);
            Tab.StatusText = "Auto-crop complete.";
            ShowInfoDialog("Auto-Crop", "Pages auto-cropped successfully.");
        }
        catch (Exception ex) { Log.Error(ex, "Auto-crop failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnDeskewClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            Tab.StatusText = "Analyzing and deskewing pages...";
            var svc = new DeskewService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var analyses = await Task.Run(() => svc.AnalyzeSkew(pdfBytes));
            var skewed = analyses.Where(a => a.NeedsDeskew).ToList();
            if (skewed.Count == 0)
            {
                ShowInfoDialog("Deskew", "No skewed pages detected.");
                Tab.StatusText = "Ready";
                return;
            }
            var result = await Task.Run(() => svc.DeskewAll(pdfBytes));
            File.WriteAllBytes(Tab.FilePath!, result);
            Tab.LoadPdf(Tab.FilePath!);
            Tab.StatusText = $"Deskewed {skewed.Count} page(s).";
            ShowInfoDialog("Deskew", $"Corrected skew on {skewed.Count} page(s).");
        }
        catch (Exception ex) { Log.Error(ex, "Deskew failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnBackgroundRemovalClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            Tab.StatusText = "Removing backgrounds...";
            var svc = new BackgroundRemovalService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var result = await svc.RemoveBackgroundsAsync(pdfBytes);
            File.WriteAllBytes(Tab.FilePath!, result);
            Tab.LoadPdf(Tab.FilePath!);
            Tab.StatusText = "Background removal complete.";
            ShowInfoDialog("Background Removal", "Background removal completed successfully.");
        }
        catch (Exception ex) { Log.Error(ex, "Background removal failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnHeaderFooterClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var dialog = new Window { Title = "Header / Footer", Width = 500, Height = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };

            panel.Children.Add(new TextBlock { Text = "Header/Footer Settings", FontSize = 16, FontWeight = FontWeight.Bold });

            var headerLabel = new TextBlock { Text = "Header template (use {page}, {pages}, {date}):" };
            var headerBox = new TextBox { Text = "Page {page} of {pages}", Padding = new Thickness(6, 4) };
            var footerLabel = new TextBlock { Text = "Footer template:", Margin = new Thickness(0, 8, 0, 0) };
            var footerBox = new TextBox { Text = "", Padding = new Thickness(6, 4) };

            var alignLabel = new TextBlock { Text = "Alignment:", Margin = new Thickness(0, 8, 0, 0) };
            var alignCombo = new ComboBox { Items = { "Center", "Left", "Right" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };

            var fontSizeLabel = new TextBlock { Text = "Font size:", Margin = new Thickness(0, 8, 0, 0) };
            var fontSizeBox = new TextBox { Text = "9", Padding = new Thickness(6, 4) };

            var separatorCheck = new CheckBox { Content = "Draw separator line", IsChecked = true };
            var skipFirstCheck = new CheckBox { Content = "Skip first page" };

            panel.Children.Add(headerLabel);
            panel.Children.Add(headerBox);
            panel.Children.Add(footerLabel);
            panel.Children.Add(footerBox);
            panel.Children.Add(alignLabel);
            panel.Children.Add(alignCombo);
            panel.Children.Add(fontSizeLabel);
            panel.Children.Add(fontSizeBox);
            panel.Children.Add(separatorCheck);
            panel.Children.Add(skipFirstCheck);

            var applyBtn = new Button { Content = "Apply", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Padding = new Thickness(20, 8) };
            applyBtn.Click += async (s, args) =>
            {
                try
                {
                    var svc = new HeaderFooterService();
                    var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
                    float.TryParse(fontSizeBox.Text, out float fs);
                    if (fs < 1) fs = 9;

                    var alignStr = (alignCombo.SelectedItem?.ToString() ?? "Center");
                    var align = alignStr == "Left" ? HeaderFooterService.HFAlignment.Left
                              : alignStr == "Right" ? HeaderFooterService.HFAlignment.Right
                              : HeaderFooterService.HFAlignment.Center;

                    var options = new HeaderFooterService.HFOptions
                    {
                        DrawSeparatorLine = separatorCheck.IsChecked == true,
                        SkipFirstPage = skipFirstCheck.IsChecked == true
                    };
                    if (!string.IsNullOrWhiteSpace(headerBox.Text))
                        options.Header = new HeaderFooterService.HFElement { Template = headerBox.Text, Alignment = align, FontSize = fs };
                    if (!string.IsNullOrWhiteSpace(footerBox.Text))
                        options.Footer = new HeaderFooterService.HFElement { Template = footerBox.Text, Alignment = align, FontSize = fs };

                    var result = await svc.AddHeaderFooterAsync(pdfBytes, options);
                    File.WriteAllBytes(Tab.FilePath!, result);
                    Tab.LoadPdf(Tab.FilePath!);
                    dialog.Close();
                    ShowInfoDialog("Header/Footer", "Header/Footer added successfully.");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            panel.Children.Add(applyBtn);

            dialog.Content = new ScrollViewer { Content = panel };
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Header/Footer failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnTableOfContentsClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            Tab.StatusText = "Detecting headings...";
            var svc = new TableOfContentsService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var headings = await Task.Run(() => svc.DetectHeadings(pdfBytes));
            if (headings.Count == 0)
            {
                ShowInfoDialog("Table of Contents", "No headings detected in the document.");
                Tab.StatusText = "Ready";
                return;
            }
            var tocText = svc.GenerateTocText(headings);

            var dialog = new Window { Title = "Table of Contents", Width = 500, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = $"Detected {headings.Count} heading(s):", FontWeight = FontWeight.Bold });
            panel.Children.Add(new TextBox { Text = tocText, IsReadOnly = true, AcceptsReturn = true, Height = 250, FontFamily = new FontFamily("Consolas") });

            var addBtn = new Button { Content = "Add Bookmarks to PDF", Padding = new Thickness(16, 8), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            addBtn.Click += async (s, args) =>
            {
                try
                {
                    var result = await Task.Run(() => svc.AddOutlines(pdfBytes, headings));
                    File.WriteAllBytes(Tab.FilePath!, result);
                    Tab.LoadPdf(Tab.FilePath!);
                    dialog.Close();
                    ShowInfoDialog("Table of Contents", $"Added {headings.Count} bookmark(s) to the PDF.");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            panel.Children.Add(addBtn);

            dialog.Content = panel;
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "TOC failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnBookletClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            Tab.StatusText = "Creating booklet...";
            var svc = new PdfBookletService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var result = await svc.CreateBookletAsync(pdfBytes);

            var saveDialog = new SaveFileDialog { Title = "Save Booklet PDF", DefaultExtension = "pdf" };
            saveDialog.Filters?.Add(new FileDialogFilter { Name = "PDF Files", Extensions = { "pdf" } });
            var savePath = await saveDialog.ShowAsync(this);
            if (!string.IsNullOrEmpty(savePath))
            {
                File.WriteAllBytes(savePath, result);
                ShowInfoDialog("Booklet", $"Booklet saved to: {savePath}");
            }
            Tab.StatusText = "Ready";
        }
        catch (Exception ex) { Log.Error(ex, "Booklet failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnPrintToPdfClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var dialog = new Window { Title = "Print to PDF", Width = 400, Height = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Page Size:", FontWeight = FontWeight.Bold });
            var sizeCombo = new ComboBox { Items = { "A4", "A3", "Letter", "Legal" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            var fitCheck = new CheckBox { Content = "Fit to page", IsChecked = true };
            var marginLabel = new TextBlock { Text = "Margin (pt):" };
            var marginBox = new TextBox { Text = "36", Padding = new Thickness(6, 4) };
            var linearizeCheck = new CheckBox { Content = "Linearize for web" };

            panel.Children.Add(sizeCombo);
            panel.Children.Add(fitCheck);
            panel.Children.Add(marginLabel);
            panel.Children.Add(marginBox);
            panel.Children.Add(linearizeCheck);

            var printBtn = new Button { Content = "Print to PDF", Padding = new Thickness(16, 8), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            printBtn.Click += async (s, args) =>
            {
                try
                {
                    var svc = new PrintToPdfService();
                    var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
                    float.TryParse(marginBox.Text, out float margin);

                    var sizeStr = sizeCombo.SelectedItem?.ToString() ?? "A4";
                    var targetSize = sizeStr switch
                    {
                        "A3" => PrintToPdfService.A3,
                        "Letter" => PrintToPdfService.Letter,
                        "Legal" => PrintToPdfService.Legal,
                        _ => PrintToPdfService.A4
                    };

                    var options = new PrintOptions
                    {
                        TargetPageSize = targetSize,
                        FitToPage = fitCheck.IsChecked == true,
                        MarginPt = margin > 0 ? margin : 36,
                        Linearize = linearizeCheck.IsChecked == true
                    };

                    var result = await svc.PrintAsync(pdfBytes, options);
                    if (result.Success)
                    {
                        var saveDialog = new SaveFileDialog { Title = "Save Printed PDF", DefaultExtension = "pdf" };
                        saveDialog.Filters?.Add(new FileDialogFilter { Name = "PDF Files", Extensions = { "pdf" } });
                        var savePath = await saveDialog.ShowAsync(this);
                        if (!string.IsNullOrEmpty(savePath))
                        {
                            File.WriteAllBytes(savePath, result.Data!);
                            dialog.Close();
                            ShowInfoDialog("Print to PDF", $"Saved to: {savePath}");
                        }
                    }
                    else ShowInfoDialog("Error", result.ErrorMessage ?? "Print failed.");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            panel.Children.Add(printBtn);
            dialog.Content = panel;
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Print to PDF failed"); ShowInfoDialog("Error", ex.Message); }
    }

    #endregion

    #region Text & Content

    private async void OnFindReplaceClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var dialog = new Window { Title = "Find & Replace", Width = 450, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Find & Replace in PDF", FontSize = 16, FontWeight = FontWeight.Bold });

            var findLabel = new TextBlock { Text = "Find:" };
            var findBox = new TextBox { Padding = new Thickness(6, 4) };
            var replaceLabel = new TextBlock { Text = "Replace with:" };
            var replaceBox = new TextBox { Padding = new Thickness(6, 4) };
            var caseCheck = new CheckBox { Content = "Case sensitive" };

            panel.Children.Add(findLabel);
            panel.Children.Add(findBox);
            panel.Children.Add(replaceLabel);
            panel.Children.Add(replaceBox);
            panel.Children.Add(caseCheck);

            var replaceBtn = new Button { Content = "Replace All", Padding = new Thickness(16, 8), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            replaceBtn.Click += async (s, args) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(findBox.Text)) { ShowInfoDialog("Error", "Search text required."); return; }
                    var svc = new PdfTextEditService();
                    var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
                    var result = await Task.Run(() => svc.FindAndReplace(pdfBytes, findBox.Text, replaceBox.Text ?? "", caseCheck.IsChecked == true));
                    File.WriteAllBytes(Tab.FilePath!, result);
                    Tab.LoadPdf(Tab.FilePath!);
                    dialog.Close();
                    ShowInfoDialog("Find & Replace", $"Replaced all occurrences of \"{findBox.Text}\" with \"{replaceBox.Text}\".");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            panel.Children.Add(replaceBtn);
            dialog.Content = panel;
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Find & Replace failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private void OnFontAnalysisClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new FontReplacementService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var report = svc.GenerateFontReport(pdfBytes);

            var dialog = new Window { Title = "Font Analysis", Width = 600, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dialog.Content = new ScrollViewer
            {
                Content = new TextBox { Text = report, IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(12) }
            };
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Font analysis failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnFontReplacementClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new FontReplacementService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var fonts = svc.AnalyzeFonts(pdfBytes);

            var dialog = new Window { Title = "Font Replacement", Width = 500, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Replace Font", FontSize = 16, FontWeight = FontWeight.Bold });

            var sourceLabel = new TextBlock { Text = "Source font:" };
            var sourceCombo = new ComboBox { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            foreach (var f in fonts) sourceCombo.Items.Add(f.FontName);
            if (sourceCombo.Items.Count > 0) sourceCombo.SelectedIndex = 0;

            var targetLabel = new TextBlock { Text = "Target font:" };
            var targetCombo = new ComboBox { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            foreach (var std in FontReplacementService.StandardFonts.Keys) targetCombo.Items.Add(std);
            if (targetCombo.Items.Count > 0) targetCombo.SelectedIndex = 0;

            panel.Children.Add(sourceLabel);
            panel.Children.Add(sourceCombo);
            panel.Children.Add(targetLabel);
            panel.Children.Add(targetCombo);

            var replBtn = new Button { Content = "Replace", Padding = new Thickness(16, 8), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            replBtn.Click += async (s, args) =>
            {
                try
                {
                    var options = new FontReplacementOptions
                    {
                        SourceFontName = sourceCombo.SelectedItem?.ToString() ?? "",
                        TargetFontName = targetCombo.SelectedItem?.ToString() ?? "Helvetica"
                    };
                    var result = await Task.Run(() => svc.ReplaceFont(pdfBytes, options));
                    File.WriteAllBytes(Tab.FilePath!, result);
                    Tab.LoadPdf(Tab.FilePath!);
                    dialog.Close();
                    ShowInfoDialog("Font Replacement", "Font replaced successfully.");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            panel.Children.Add(replBtn);
            dialog.Content = panel;
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Font replacement failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnTableEditorClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            Tab.StatusText = "Detecting tables...";
            var svc = new TableEditorService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var tables = await Task.Run(() => svc.DetectAllTables(pdfBytes));

            if (tables.Count == 0)
            {
                ShowInfoDialog("Table Editor", "No tables detected in the document.");
                Tab.StatusText = "Ready";
                return;
            }

            var report = new System.Text.StringBuilder();
            foreach (var table in tables)
            {
                report.AppendLine($"Page {table.PageIndex + 1}: Table {table.TableIndex + 1} ({table.RowCount} rows × {table.ColumnCount} cols)");
                report.AppendLine(svc.ExtractTableAsCsv(table));
                report.AppendLine();
            }

            var dialog = new Window { Title = $"Table Editor - {tables.Count} table(s) found", Width = 700, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new DockPanel { Margin = new Thickness(12) };

            var exportBtn = new Button { Content = "Export as CSV", Padding = new Thickness(12, 6), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(exportBtn, Dock.Top);
            exportBtn.Click += async (s, args) =>
            {
                var saveDialog = new SaveFileDialog { Title = "Save CSV", DefaultExtension = "csv" };
                saveDialog.Filters?.Add(new FileDialogFilter { Name = "CSV Files", Extensions = { "csv" } });
                var savePath = await saveDialog.ShowAsync(this);
                if (!string.IsNullOrEmpty(savePath))
                    File.WriteAllText(savePath, report.ToString());
            };
            panel.Children.Add(exportBtn);
            panel.Children.Add(new TextBox { Text = report.ToString(), IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), FontSize = 11 });

            dialog.Content = panel;
            dialog.Show(this);
            Tab.StatusText = $"Found {tables.Count} table(s).";
        }
        catch (Exception ex) { Log.Error(ex, "Table editor failed"); ShowInfoDialog("Error", ex.Message); }
    }

    #endregion

    #region Image Tools

    private async void OnExtractImagesClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var folderDialog = new OpenFolderDialog { Title = "Select output folder for images" };
            var folder = await folderDialog.ShowAsync(this);
            if (string.IsNullOrEmpty(folder)) return;

            Tab.StatusText = "Extracting images...";
            var svc = new ImageExtractionService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var count = await svc.ExtractToFolderAsync(pdfBytes, folder);
            Tab.StatusText = $"Extracted {count} image(s).";
            ShowInfoDialog("Image Extraction", $"Extracted {count} image(s) to:\n{folder}");
        }
        catch (Exception ex) { Log.Error(ex, "Image extraction failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnCompressImagesClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            Tab.StatusText = "Compressing images...";
            var svc = new ImageCompressService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var result = await svc.CompressAsync(pdfBytes);

            File.WriteAllBytes(Tab.FilePath!, result.OutputPdf);
            Tab.LoadPdf(Tab.FilePath!);

            var ratio = result.CompressionRatio.ToString("P1");
            Tab.StatusText = $"Compressed {result.ImagesProcessed} image(s), {ratio} reduction.";
            ShowInfoDialog("Image Compression",
                $"Original: {result.OriginalSize:N0} bytes\nCompressed: {result.CompressedSize:N0} bytes\nRatio: {ratio}\nImages processed: {result.ImagesProcessed}");
        }
        catch (Exception ex) { Log.Error(ex, "Image compression failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnReplaceImageClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new ImageReplaceService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var images = svc.ListImages(pdfBytes);

            if (images.Count == 0)
            {
                ShowInfoDialog("Replace Image", "No images found in the document.");
                return;
            }

            // Pick replacement image file
            var openDialog = new OpenFileDialog { Title = "Select replacement image", AllowMultiple = false };
            openDialog.Filters?.Add(new FileDialogFilter { Name = "Images", Extensions = { "png", "jpg", "jpeg", "bmp" } });
            var files = await openDialog.ShowAsync(this);
            if (files == null || files.Length == 0) return;

            var newImageBytes = File.ReadAllBytes(files[0]);
            var result = svc.ReplaceAllImages(pdfBytes, newImageBytes);
            File.WriteAllBytes(Tab.FilePath!, result);
            Tab.LoadPdf(Tab.FilePath!);
            ShowInfoDialog("Replace Image", $"Replaced {images.Count} image(s) in the document.");
        }
        catch (Exception ex) { Log.Error(ex, "Image replace failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnInsertBarcodeClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var dialog = new Window { Title = "Insert Barcode", Width = 450, Height = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Insert Barcode", FontSize = 16, FontWeight = FontWeight.Bold });

            var dataLabel = new TextBlock { Text = "Data / URL:" };
            var dataBox = new TextBox { Text = "https://example.com", Padding = new Thickness(6, 4) };
            var typeLabel = new TextBlock { Text = "Type:" };
            var typeCombo = new ComboBox { Items = { "QR", "Code128", "Code39", "EAN13", "DataMatrix", "PDF417" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            var sizeLabel = new TextBlock { Text = "Size (px):" };
            var sizeBox = new TextBox { Text = "300", Padding = new Thickness(6, 4) };
            var pageLabel = new TextBlock { Text = $"Page (1-{Tab.PageCount}):" };
            var pageBox = new TextBox { Text = (Tab.CurrentPageIndex + 1).ToString(), Padding = new Thickness(6, 4) };

            panel.Children.Add(dataLabel);
            panel.Children.Add(dataBox);
            panel.Children.Add(typeLabel);
            panel.Children.Add(typeCombo);
            panel.Children.Add(sizeLabel);
            panel.Children.Add(sizeBox);
            panel.Children.Add(pageLabel);
            panel.Children.Add(pageBox);

            var insertBtn = new Button { Content = "Insert", Padding = new Thickness(16, 8), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            insertBtn.Click += async (s, args) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dataBox.Text)) { ShowInfoDialog("Error", "Data required."); return; }

                    var svc = new BarcodeService();
                    int.TryParse(sizeBox.Text, out int size);
                    if (size < 50) size = 300;
                    int.TryParse(pageBox.Text, out int page);
                    page = Math.Clamp(page, 1, Tab.PageCount) - 1;

                    var typeStr = typeCombo.SelectedItem?.ToString() ?? "QR";
                    var bType = Enum.TryParse<BarcodeService.BarcodeType>(typeStr, true, out var bt) ? bt : BarcodeService.BarcodeType.QRCode;

                    var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
                    var result = await Task.Run(() => svc.GenerateAndEmbed(pdfBytes, dataBox.Text, bType, page, size));
                    File.WriteAllBytes(Tab.FilePath!, result);
                    Tab.LoadPdf(Tab.FilePath!);
                    dialog.Close();
                    ShowInfoDialog("Barcode", $"Barcode inserted on page {page + 1}.");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            panel.Children.Add(insertBtn);
            dialog.Content = panel;
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Insert barcode failed"); ShowInfoDialog("Error", ex.Message); }
    }

    #endregion

    #region Security Extended

    private async void OnMetadataScrubberClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new MetadataScrubberService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var summary = await svc.InspectAsync(pdfBytes);

            if (!summary.HasAnyMetadata)
            {
                ShowInfoDialog("Metadata Scrubber", "No metadata found in the document.");
                return;
            }

            var info = $"Title: {summary.Title ?? "(none)"}\nAuthor: {summary.Author ?? "(none)"}\nSubject: {summary.Subject ?? "(none)"}\nCreator: {summary.Creator ?? "(none)"}\nProducer: {summary.Producer ?? "(none)"}\nKeywords: {summary.Keywords ?? "(none)"}\nHas XMP: {summary.HasXmp}\nCustom keys: {summary.CustomKeys?.Count ?? 0}";

            var dialog = new Window { Title = "Metadata Scrubber", Width = 500, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Current Metadata", FontSize = 16, FontWeight = FontWeight.Bold });
            panel.Children.Add(new TextBox { Text = info, IsReadOnly = true, AcceptsReturn = true, Height = 200 });

            var preserveTitleCheck = new CheckBox { Content = "Preserve title" };
            panel.Children.Add(preserveTitleCheck);

            var scrubBtn = new Button { Content = "Scrub All Metadata", Padding = new Thickness(16, 8), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Foreground = Brushes.OrangeRed };
            scrubBtn.Click += async (s, args) =>
            {
                try
                {
                    var result = await svc.ScrubAsync(pdfBytes, preserveTitleCheck.IsChecked == true);
                    File.WriteAllBytes(Tab.FilePath!, result);
                    Tab.LoadPdf(Tab.FilePath!);
                    dialog.Close();
                    ShowInfoDialog("Metadata Scrubber", "All metadata has been removed.");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            panel.Children.Add(scrubBtn);
            dialog.Content = panel;
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Metadata scrubber failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private void OnSanitizeDocumentClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new DocumentSanitizerService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var report = svc.Inspect(pdfBytes);

            if (report.TotalItemsRemoved == 0 && !report.HadOpenAction)
            {
                ShowInfoDialog("Sanitize Document", "Document is clean — no threats found.");
                return;
            }

            var reportText = $"JavaScript actions: {report.JavaScriptActionsRemoved}\nEmbedded files: {report.EmbeddedFilesRemoved}\nExternal links: {report.ExternalLinksRemoved}\nForm actions: {report.FormActionsRemoved}\nMultiMedia: {report.MultiMediaRemoved}\nOpen actions: {(report.HadOpenAction ? "Yes" : "No")}\nMetadata fields: {report.MetadataFieldsCleaned}";

            var dialog = new Window { Title = "Sanitize Document", Width = 500, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Threat Analysis", FontSize = 16, FontWeight = FontWeight.Bold });
            panel.Children.Add(new TextBox { Text = reportText, IsReadOnly = true, AcceptsReturn = true, Height = 180 });

            var sanitizeBtn = new Button { Content = "Sanitize Now", Padding = new Thickness(16, 8), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Foreground = Brushes.OrangeRed };
            sanitizeBtn.Click += (s, args) =>
            {
                try
                {
                    var (sanitized, _) = svc.Sanitize(pdfBytes);
                    File.WriteAllBytes(Tab.FilePath!, sanitized);
                    Tab.LoadPdf(Tab.FilePath!);
                    dialog.Close();
                    ShowInfoDialog("Sanitize", "Document sanitized successfully.");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            panel.Children.Add(sanitizeBtn);
            dialog.Content = panel;
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Sanitize failed"); ShowInfoDialog("Error", ex.Message); }
    }

    #endregion

    #region Accessibility

    private void OnAccessibilityCheckClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new AccessibilityCheckerService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var report = svc.CheckAccessibility(pdfBytes, IOPath.GetFileName(Tab.FilePath!));
            var reportText = svc.GenerateReportText(report);

            var dialog = new Window { Title = $"Accessibility Report — Score: {report.ComplianceScore}%", Width = 700, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dialog.Content = new ScrollViewer
            {
                Content = new TextBox { Text = reportText, IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(12) }
            };
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Accessibility check failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnAutoTagClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            Tab.StatusText = "Auto-tagging PDF...";
            var svc = new AutoTagService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);

            if (svc.IsTagged(pdfBytes))
            {
                ShowInfoDialog("Auto-Tag", "Document is already tagged.");
                Tab.StatusText = "Ready";
                return;
            }

            var result = await Task.Run(() => svc.AutoTag(pdfBytes));
            File.WriteAllBytes(Tab.FilePath!, result.TaggedPdf);
            Tab.LoadPdf(Tab.FilePath!);
            Tab.StatusText = "Auto-tagging complete.";
            ShowInfoDialog("Auto-Tag", $"Tagged {result.TotalElementsTagged} elements:\n• Paragraphs: {result.ParagraphsTagged}\n• Headings: {result.HeadingsTagged}\n• Images: {result.ImagesTagged}\n• Tables: {result.TablesTagged}\n• Lists: {result.ListsTagged}");
        }
        catch (Exception ex) { Log.Error(ex, "Auto-tag failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private void OnAltTextEditorClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new AltTextEditorService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var images = svc.GetImageAltTexts(pdfBytes);

            if (images.Count == 0)
            {
                ShowInfoDialog("Alt Text Editor", "No images found in the document.");
                return;
            }

            var missing = svc.CountMissingAltTexts(pdfBytes);
            var report = svc.GenerateAltTextReport(pdfBytes);

            var dialog = new Window { Title = $"Alt Text Editor — {missing} missing", Width = 600, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dialog.Content = new ScrollViewer
            {
                Content = new TextBox { Text = report, IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(12) }
            };
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Alt text editor failed"); ShowInfoDialog("Error", ex.Message); }
    }

    #endregion

    #region Archiving

    private async void OnConvertPdfAClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            Tab.StatusText = "Converting to PDF/A-2B...";
            var svc = new PdfArchiverService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var result = await svc.ConvertToPdfA2BAsync(pdfBytes);

            if (result.Success)
            {
                var saveDialog = new SaveFileDialog { Title = "Save PDF/A", DefaultExtension = "pdf" };
                saveDialog.Filters?.Add(new FileDialogFilter { Name = "PDF Files", Extensions = { "pdf" } });
                var savePath = await saveDialog.ShowAsync(this);
                if (!string.IsNullOrEmpty(savePath))
                {
                    File.WriteAllBytes(savePath, result.Data!);
                    ShowInfoDialog("PDF/A", $"PDF/A-2B saved to: {savePath}");
                }
            }
            else ShowInfoDialog("Error", result.ErrorMessage ?? "Conversion failed.");
            Tab.StatusText = "Ready";
        }
        catch (Exception ex) { Log.Error(ex, "PDF/A conversion failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnInspectPdfAClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new PdfArchiverService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var info = await svc.InspectConformanceAsync(pdfBytes);

            var text = $"Has PDF/A XMP claim: {info.HasXmpPdfAClaim}\nPDF/A Part: {info.PdfAPart}\nConformance Level: {info.PdfAConformanceLevel}\nConformance: {info.ConformanceLabel}\nPages: {info.PageCount}\nPDF Version: {info.PdfVersion}";
            ShowInfoDialog("PDF/A Conformance", text);
        }
        catch (Exception ex) { Log.Error(ex, "PDF/A inspection failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnConvertPdfXClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            Tab.StatusText = "Converting to PDF/X-4...";
            var svc = new PdfXService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var result = await Task.Run(() => svc.ConvertToPdfX(pdfBytes));

            var saveDialog = new SaveFileDialog { Title = "Save PDF/X", DefaultExtension = "pdf" };
            saveDialog.Filters?.Add(new FileDialogFilter { Name = "PDF Files", Extensions = { "pdf" } });
            var savePath = await saveDialog.ShowAsync(this);
            if (!string.IsNullOrEmpty(savePath))
            {
                File.WriteAllBytes(savePath, result);
                ShowInfoDialog("PDF/X", $"PDF/X-4 saved to: {savePath}");
            }
            Tab.StatusText = "Ready";
        }
        catch (Exception ex) { Log.Error(ex, "PDF/X conversion failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private void OnInspectPdfXClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new PdfXService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var report = svc.GenerateReport(pdfBytes);
            ShowInfoDialog("PDF/X Inspection", report);
        }
        catch (Exception ex) { Log.Error(ex, "PDF/X inspection failed"); ShowInfoDialog("Error", ex.Message); }
    }

    #endregion

    #region Form Advanced

    private void OnCalculationFieldsClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new CalculationFieldService();
            var dialog = new Window { Title = "Calculation Fields", Width = 550, Height = 450, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Calculation Rules", FontSize = 16, FontWeight = FontWeight.Bold });
            panel.Children.Add(new TextBlock { Text = $"Current rules: {svc.Rules.Count}", Opacity = 0.6 });

            var targetLabel = new TextBlock { Text = "Target field name:" };
            var targetBox = new TextBox { Padding = new Thickness(6, 4) };
            var typeLabel = new TextBlock { Text = "Calculation type:" };
            var typeCombo = new ComboBox { Items = { "Sum", "Average", "Min", "Max", "Count", "Product" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            var sourceLabel = new TextBlock { Text = "Source fields (comma-separated):" };
            var sourceBox = new TextBox { Padding = new Thickness(6, 4) };

            panel.Children.Add(targetLabel);
            panel.Children.Add(targetBox);
            panel.Children.Add(typeLabel);
            panel.Children.Add(typeCombo);
            panel.Children.Add(sourceLabel);
            panel.Children.Add(sourceBox);

            var addBtn = new Button { Content = "Add Rule & Apply", Padding = new Thickness(16, 8), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            addBtn.Click += (s, args) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(targetBox.Text) || string.IsNullOrWhiteSpace(sourceBox.Text))
                    { ShowInfoDialog("Error", "Target and source fields required."); return; }

                    var typeStr = typeCombo.SelectedItem?.ToString() ?? "Sum";
                    var calcType = Enum.TryParse<CalculationType>(typeStr, out var ct) ? ct : CalculationType.Sum;
                    var sources = sourceBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

                    svc.AddRule(new CalculationRule { TargetField = targetBox.Text.Trim(), Type = calcType, SourceFields = sources });
                    var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
                    var result = svc.EvaluateAndApply(pdfBytes);
                    File.WriteAllBytes(Tab.FilePath!, result);
                    Tab.LoadPdf(Tab.FilePath!);
                    dialog.Close();
                    ShowInfoDialog("Calculation Fields", "Rule added and applied.");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            panel.Children.Add(addBtn);
            dialog.Content = new ScrollViewer { Content = panel };
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Calculation fields failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private void OnConditionalLogicClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new ConditionalLogicService();
            var dialog = new Window { Title = "Conditional Logic", Width = 550, Height = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Conditional Logic Rules", FontSize = 16, FontWeight = FontWeight.Bold });
            panel.Children.Add(new TextBlock { Text = $"Current rules: {svc.Rules.Count}", Opacity = 0.6 });

            var targetLabel = new TextBlock { Text = "Target field:" };
            var targetBox = new TextBox { Padding = new Thickness(6, 4) };
            var actionLabel = new TextBlock { Text = "Action:" };
            var actionCombo = new ComboBox { Items = { "Show", "Hide", "Enable", "Disable", "SetValue", "SetRequired", "SetReadOnly" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            var condFieldLabel = new TextBlock { Text = "Condition: When field..." };
            var condFieldBox = new TextBox { Padding = new Thickness(6, 4) };
            var compLabel = new TextBlock { Text = "...is:" };
            var compCombo = new ComboBox { Items = { "Equals", "NotEquals", "Contains", "IsEmpty", "IsNotEmpty" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            var valueLabel = new TextBlock { Text = "...value:" };
            var valueBox = new TextBox { Padding = new Thickness(6, 4) };

            panel.Children.Add(targetLabel); panel.Children.Add(targetBox);
            panel.Children.Add(actionLabel); panel.Children.Add(actionCombo);
            panel.Children.Add(condFieldLabel); panel.Children.Add(condFieldBox);
            panel.Children.Add(compLabel); panel.Children.Add(compCombo);
            panel.Children.Add(valueLabel); panel.Children.Add(valueBox);

            var addBtn = new Button { Content = "Add Rule & Apply", Padding = new Thickness(16, 8), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            addBtn.Click += (s, args) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(targetBox.Text) || string.IsNullOrWhiteSpace(condFieldBox.Text))
                    { ShowInfoDialog("Error", "Target and condition field required."); return; }

                    var action = Enum.TryParse<ConditionalAction>(actionCombo.SelectedItem?.ToString(), out var a) ? a : ConditionalAction.Show;
                    var comp = Enum.TryParse<ComparisonOperator>(compCombo.SelectedItem?.ToString(), out var c) ? c : ComparisonOperator.Equals;

                    svc.AddRule(new ConditionalRule
                    {
                        TargetField = targetBox.Text.Trim(),
                        Action = action,
                        Conditions = new List<Condition>
                        {
                            new() { FieldName = condFieldBox.Text.Trim(), Comparison = comp, Value = valueBox.Text?.Trim() ?? "" }
                        }
                    });

                    var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
                    var result = svc.EvaluateAndApply(pdfBytes);
                    File.WriteAllBytes(Tab.FilePath!, result);
                    Tab.LoadPdf(Tab.FilePath!);
                    dialog.Close();
                    ShowInfoDialog("Conditional Logic", "Rule added and applied.");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            panel.Children.Add(addBtn);
            dialog.Content = new ScrollViewer { Content = panel };
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Conditional logic failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private void OnFormValidationClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new FormValidationService();
            var formSvc = new PdfFormService();
            var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
            var fields = formSvc.GetFormFields(pdfBytes);

            if (fields.Count == 0)
            {
                ShowInfoDialog("Form Validation", "No form fields found in the document.");
                return;
            }

            // Auto-generate basic rules
            svc.AutoGenerateRules(fields);
            var formData = formSvc.ExportFormData(pdfBytes);
            var validationResult = svc.Validate(formData.FieldValues);

            var report = new System.Text.StringBuilder();
            report.AppendLine($"Form Fields: {fields.Count}");
            report.AppendLine($"Validation Rules: {svc.Rules.Count}");
            report.AppendLine();

            if (validationResult.IsValid)
                report.AppendLine("All fields pass validation.");
            else
            {
                report.AppendLine($"{validationResult.Errors.Count} validation error(s):");
                foreach (var err in validationResult.Errors)
                    report.AppendLine($"  • {err.FieldName}: {err.ErrorMessage}");
            }

            ShowInfoDialog("Form Validation", report.ToString());
        }
        catch (Exception ex) { Log.Error(ex, "Form validation failed"); ShowInfoDialog("Error", ex.Message); }
    }

    #endregion

    #region Electronic Signature

    private async void OnElectronicSignatureClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var dialog = new Window { Title = "Electronic Signature", Width = 500, Height = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Electronic Signature", FontSize = 16, FontWeight = FontWeight.Bold });

            var nameLabel = new TextBlock { Text = "Signer name:" };
            var nameBox = new TextBox { Padding = new Thickness(6, 4) };
            var reasonLabel = new TextBlock { Text = "Reason:" };
            var reasonBox = new TextBox { Text = "Approved", Padding = new Thickness(6, 4) };
            var locationLabel = new TextBlock { Text = "Location:" };
            var locationBox = new TextBox { Padding = new Thickness(6, 4) };
            var pageLabel = new TextBlock { Text = $"Page (1-{Tab.PageCount}):" };
            var pageBox = new TextBox { Text = (Tab.CurrentPageIndex + 1).ToString(), Padding = new Thickness(6, 4) };

            var typeLabel = new TextBlock { Text = "Signature type:" };
            var typeCombo = new ComboBox { Items = { "Typed (Cursive Font)", "Upload Image" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };

            panel.Children.Add(nameLabel); panel.Children.Add(nameBox);
            panel.Children.Add(reasonLabel); panel.Children.Add(reasonBox);
            panel.Children.Add(locationLabel); panel.Children.Add(locationBox);
            panel.Children.Add(pageLabel); panel.Children.Add(pageBox);
            panel.Children.Add(typeLabel); panel.Children.Add(typeCombo);

            var signBtn = new Button { Content = "Sign Document", Padding = new Thickness(16, 8), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            signBtn.Click += async (s, args) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(nameBox.Text)) { ShowInfoDialog("Error", "Signer name required."); return; }

                    var svc = new ElectronicSignatureService();
                    byte[] sigImage;

                    if (typeCombo.SelectedIndex == 1)
                    {
                        // Upload image
                        var openDialog = new OpenFileDialog { Title = "Select signature image", AllowMultiple = false };
                        openDialog.Filters?.Add(new FileDialogFilter { Name = "Images", Extensions = { "png", "jpg", "jpeg" } });
                        var files = await openDialog.ShowAsync(this);
                        if (files == null || files.Length == 0) return;
                        sigImage = File.ReadAllBytes(files[0]);
                    }
                    else
                    {
                        sigImage = svc.CreateTypedSignature(nameBox.Text);
                    }

                    int.TryParse(pageBox.Text, out int page);
                    page = Math.Clamp(page, 1, Tab.PageCount) - 1;

                    var sig = new ElectronicSignatureService.ElectronicSignature
                    {
                        SignerName = nameBox.Text.Trim(),
                        Reason = reasonBox.Text?.Trim() ?? "",
                        Location = locationBox.Text?.Trim() ?? "",
                        SignatureImage = sigImage,
                        SignedDate = DateTime.Now
                    };

                    var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
                    var result = await Task.Run(() => svc.AddSignature(pdfBytes, sig, page));
                    File.WriteAllBytes(Tab.FilePath!, result);
                    Tab.LoadPdf(Tab.FilePath!);
                    dialog.Close();
                    ShowInfoDialog("Electronic Signature", $"Signature by \"{nameBox.Text}\" added to page {page + 1}.");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            panel.Children.Add(signBtn);
            dialog.Content = new ScrollViewer { Content = panel };
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Electronic signature failed"); ShowInfoDialog("Error", ex.Message); }
    }

    #endregion

    #region Productivity

    private void OnQuickActionsClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new QuickActionsService();
            var templates = svc.GetBuiltInTemplates();

            var dialog = new Window { Title = "Quick Actions", Width = 600, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Quick Actions", FontSize = 16, FontWeight = FontWeight.Bold });
            panel.Children.Add(new TextBlock { Text = $"Built-in templates: {templates.Count} | Custom actions: {svc.Actions.Count}", Opacity = 0.6 });

            var listBox = new ListBox { Height = 300 };
            foreach (var t in templates)
                listBox.Items.Add($"{t.Name} — {t.Description}");
            foreach (var a in svc.Actions)
                listBox.Items.Add($"[Custom] {a.Name} — {a.Description}");
            if (listBox.Items.Count > 0) listBox.SelectedIndex = 0;

            panel.Children.Add(listBox);

            var runBtn = new Button { Content = "Run Selected Action", Padding = new Thickness(16, 8), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            runBtn.Click += (s, args) =>
            {
                try
                {
                    var idx = listBox.SelectedIndex;
                    if (idx < 0) return;

                    string actionId;
                    if (idx < templates.Count) actionId = templates[idx].Id;
                    else actionId = svc.Actions[idx - templates.Count].Id;

                    var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
                    var result = svc.Execute(pdfBytes, actionId);
                    File.WriteAllBytes(Tab.FilePath!, result);
                    Tab.LoadPdf(Tab.FilePath!);
                    dialog.Close();
                    ShowInfoDialog("Quick Actions", "Action executed successfully.");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            panel.Children.Add(runBtn);
            dialog.Content = panel;
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Quick actions failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private void OnTemplatesClick(object? sender, RoutedEventArgs e)
    {
        if (Tab == null || !Tab.IsDocumentLoaded) return;
        try
        {
            var svc = new TemplateService();
            var dialog = new Window { Title = "Templates", Width = 550, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Document Templates", FontSize = 16, FontWeight = FontWeight.Bold });
            panel.Children.Add(new TextBlock { Text = $"Templates: {svc.Templates.Count} | Categories: {svc.GetCategories().Count}", Opacity = 0.6 });

            var nameLabel = new TextBlock { Text = "Template name:", Margin = new Thickness(0, 8, 0, 0) };
            var nameBox = new TextBox { Padding = new Thickness(6, 4) };
            var descLabel = new TextBlock { Text = "Description:" };
            var descBox = new TextBox { Padding = new Thickness(6, 4) };
            var catLabel = new TextBlock { Text = "Category:" };
            var catBox = new TextBox { Text = "General", Padding = new Thickness(6, 4) };

            panel.Children.Add(nameLabel); panel.Children.Add(nameBox);
            panel.Children.Add(descLabel); panel.Children.Add(descBox);
            panel.Children.Add(catLabel); panel.Children.Add(catBox);

            var saveBtn = new Button { Content = "Save as Template", Padding = new Thickness(16, 8), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            saveBtn.Click += (s, args) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(nameBox.Text)) { ShowInfoDialog("Error", "Name required."); return; }
                    var pdfBytes = File.ReadAllBytes(Tab.FilePath!);
                    svc.SaveAsTemplate(pdfBytes, nameBox.Text.Trim(), descBox.Text?.Trim() ?? "", catBox.Text?.Trim() ?? "General");
                    dialog.Close();
                    ShowInfoDialog("Templates", $"Template \"{nameBox.Text}\" saved.");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            panel.Children.Add(saveBtn);
            dialog.Content = panel;
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Templates failed"); ShowInfoDialog("Error", ex.Message); }
    }

    private void OnWatchFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var svc = new WatchFolderService();
            var dialog = new Window { Title = "Watch Folder", Width = 550, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Watch Folder", FontSize = 16, FontWeight = FontWeight.Bold });
            panel.Children.Add(new TextBlock { Text = svc.IsRunning ? "Status: RUNNING" : "Status: Stopped", Foreground = svc.IsRunning ? Brushes.Green : Brushes.Gray });

            var watchLabel = new TextBlock { Text = "Watch folder path:" };
            var watchBox = new TextBox { Padding = new Thickness(6, 4) };
            var outputLabel = new TextBlock { Text = "Output folder path:" };
            var outputBox = new TextBox { Padding = new Thickness(6, 4) };
            var actionLabel = new TextBlock { Text = "Action:" };
            var actionCombo = new ComboBox { Items = { "Copy", "Compress", "OCR", "Watermark", "ConvertToPdfA", "RemoveMetadata" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };

            panel.Children.Add(watchLabel); panel.Children.Add(watchBox);
            panel.Children.Add(outputLabel); panel.Children.Add(outputBox);
            panel.Children.Add(actionLabel); panel.Children.Add(actionCombo);

            var startBtn = new Button { Content = "Start Watching", Padding = new Thickness(16, 8), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            var stopBtn = new Button { Content = "Stop", Padding = new Thickness(16, 8), Margin = new Thickness(8, 12, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };

            startBtn.Click += (s, args) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(watchBox.Text) || string.IsNullOrWhiteSpace(outputBox.Text))
                    { ShowInfoDialog("Error", "Watch and output paths required."); return; }

                    var action = Enum.TryParse<WatchFolderAction>(actionCombo.SelectedItem?.ToString(), out var a) ? a : WatchFolderAction.Copy;
                    svc.Start(new WatchFolderConfig
                    {
                        WatchPath = watchBox.Text.Trim(),
                        OutputPath = outputBox.Text.Trim(),
                        Action = action
                    });
                    ShowInfoDialog("Watch Folder", "Watching started. New PDF files will be processed automatically.");
                }
                catch (Exception ex) { ShowInfoDialog("Error", ex.Message); }
            };
            stopBtn.Click += (s, args) =>
            {
                svc.Stop();
                ShowInfoDialog("Watch Folder", "Watching stopped.");
            };

            var btnPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            btnPanel.Children.Add(startBtn);
            btnPanel.Children.Add(stopBtn);
            panel.Children.Add(btnPanel);

            dialog.Content = panel;
            dialog.Show(this);
        }
        catch (Exception ex) { Log.Error(ex, "Watch folder failed"); ShowInfoDialog("Error", ex.Message); }
    }

    #endregion
}