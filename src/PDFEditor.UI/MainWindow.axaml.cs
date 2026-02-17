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
using Avalonia.VisualTree;
using NLog;
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
                        var annotCanvas = this.FindControl<Canvas>("AnnotationCanvas");
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

    private void OnFirstPageClick(object? sender, RoutedEventArgs e) =>
        ExecuteTabCommand(() => Tab?.FirstPageCommand.Execute().Subscribe());

    private void OnLastPageClick(object? sender, RoutedEventArgs e) =>
        ExecuteTabCommand(() => Tab?.LastPageCommand.Execute().Subscribe());

    private void OnPrevPageClick(object? sender, RoutedEventArgs e) =>
        ExecuteTabCommand(() => Tab?.PreviousPageCommand.Execute().Subscribe());

    private void OnNextPageClick(object? sender, RoutedEventArgs e) =>
        ExecuteTabCommand(() => Tab?.NextPageCommand.Execute().Subscribe());

    #endregion

    #region Zoom

    private void OnContextZoomInClick(object? sender, RoutedEventArgs e) => Tab?.ZoomInCommand.Execute().Subscribe();
    private void OnContextZoomOutClick(object? sender, RoutedEventArgs e) => Tab?.ZoomOutCommand.Execute().Subscribe();
    private void OnContextZoomFitClick(object? sender, RoutedEventArgs e) => Tab?.ZoomFitCommand.Execute().Subscribe();

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

    private void AnnotCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Tab == null || !Tab.IsAnnotationMode) return;

        var canvas = this.FindControl<Canvas>("AnnotationCanvas");
        var pageImage = this.FindControl<Image>("PageImage");
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
        var canvas = this.FindControl<Canvas>("AnnotationCanvas");
        var pageImage = this.FindControl<Image>("PageImage");
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

        var canvas = this.FindControl<Canvas>("AnnotationCanvas");
        var pageImage = this.FindControl<Image>("PageImage");
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
        var canvas = this.FindControl<Canvas>("AnnotationCanvas");
        var pageImage = this.FindControl<Image>("PageImage");
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
        var canvas = this.FindControl<Canvas>("AnnotationCanvas");
        canvas?.Children.Clear();
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
        var w = new Window
        {
            Title = "About PDF Editor",
            Width = 400, Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "PDF Editor", FontSize = 24, FontWeight = FontWeight.Bold, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    new TextBlock { Text = "Cross-platform PDF Editor", FontSize = 13, Opacity = 0.6, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    new TextBlock { Text = "Built with Avalonia UI + iText7 + Docnet", FontSize = 12, Opacity = 0.5, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    new TextBlock { Text = "AGPL v3 License", FontSize = 11, Opacity = 0.4, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
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
}
