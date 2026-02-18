using Avalonia.Threading;
using PDFEditor.Core.Services;
using PDFEditor.UI.ViewModels;
using PDFEditor.Tests.Infrastructure;
using System.Reactive;
using Xunit;

namespace PDFEditor.Tests.ViewModels;

/// <summary>
/// Unit tests for DocumentTabViewModel — per-document state and operations.
/// Tests are limited to property logic and synchronous methods that do not
/// require a real PDF or a running Avalonia message loop.
/// </summary>
[Collection("AvaloniaTests")]
public class DocumentTabViewModelTests
{
    private static DocumentTabViewModel CreateVm()
    {
        var vm = new DocumentTabViewModel();
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    // ----------------------------------------------------------
    // Initial State
    // ----------------------------------------------------------

    [Fact]
    public void Constructor_IsDocumentLoaded_IsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.IsDocumentLoaded);
    }

    [Fact]
    public void Constructor_PageCount_IsZero()
    {
        var vm = CreateVm();
        Assert.Equal(0, vm.PageCount);
    }

    [Fact]
    public void Constructor_IsBusy_IsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void Constructor_FilePath_IsNull()
    {
        var vm = CreateVm();
        Assert.Null(vm.FilePath);
    }

    [Fact]
    public void Constructor_TabTitle_ShowsUntitled()
    {
        var vm = CreateVm();
        Assert.Contains("Untitled", vm.TabTitle);
    }

    [Fact]
    public void Constructor_CanUndo_IsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void Constructor_CanRedo_IsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void Constructor_ZoomLevel_IsOne()
    {
        var vm = CreateVm();
        Assert.Equal(1.0, vm.ZoomLevel, precision: 3);
    }

    [Fact]
    public void Constructor_IsAnnotationMode_IsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.IsAnnotationMode);
    }

    [Fact]
    public void Constructor_StatusText_IsReady()
    {
        var vm = CreateVm();
        Assert.Equal("Ready", vm.StatusText);
    }

    [Fact]
    public void Constructor_Annotations_IsEmpty()
    {
        var vm = CreateVm();
        Assert.Empty(vm.Annotations);
    }

    [Fact]
    public void Constructor_Thumbnails_IsEmpty()
    {
        var vm = CreateVm();
        Assert.Empty(vm.Thumbnails);
    }

    // ----------------------------------------------------------
    // ZoomLevel Clamping
    // ----------------------------------------------------------

    [Fact]
    public void ZoomLevel_SetAboveMax_ClampsToFour()
    {
        var vm = CreateVm();
        vm.ZoomLevel = 10.0;
        Assert.Equal(4.0, vm.ZoomLevel, precision: 3);
    }

    [Fact]
    public void ZoomLevel_SetBelowMin_ClampsToQuarter()
    {
        var vm = CreateVm();
        vm.ZoomLevel = 0.0;
        Assert.Equal(0.25, vm.ZoomLevel, precision: 3);
    }

    [Fact]
    public void ZoomLevel_SetNegative_ClampsToMinimum()
    {
        var vm = CreateVm();
        vm.ZoomLevel = -1.0;
        Assert.Equal(0.25, vm.ZoomLevel, precision: 3);
    }

    [Fact]
    public void ZoomLevel_SetValid_IsAccepted()
    {
        var vm = CreateVm();
        vm.ZoomLevel = 2.0;
        Assert.Equal(2.0, vm.ZoomLevel, precision: 3);
    }

    [Fact]
    public void ZoomPercent_ReflectsZoomLevel()
    {
        var vm = CreateVm();
        vm.ZoomLevel = 1.5;
        Assert.Equal("150%", vm.ZoomPercent);
    }

    // ----------------------------------------------------------
    // ZoomIn / ZoomOut / ZoomFit Commands
    // ----------------------------------------------------------

    [Fact]
    public void ZoomInCommand_IncreasesZoomBy25Percent()
    {
        var vm = CreateVm();
        double initial = vm.ZoomLevel;

        vm.ZoomInCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(initial + 0.25, vm.ZoomLevel, precision: 3);
    }

    [Fact]
    public void ZoomOutCommand_DecreasesZoomBy25Percent()
    {
        var vm = CreateVm();
        vm.ZoomLevel = 2.0;

        vm.ZoomOutCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1.75, vm.ZoomLevel, precision: 3);
    }

    [Fact]
    public void ZoomFitCommand_ResetsZoomToOne()
    {
        var vm = CreateVm();
        vm.ZoomLevel = 3.0;

        vm.ZoomFitCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1.0, vm.ZoomLevel, precision: 3);
    }

    // ----------------------------------------------------------
    // IsModified & TabTitle
    // ----------------------------------------------------------

    [Fact]
    public void IsModified_Default_IsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.IsModified);
    }

    [Fact]
    public void TabTitle_WhenModified_HasAsterisk()
    {
        var vm = CreateVm();
        vm.IsModified = true;
        Assert.StartsWith("*", vm.TabTitle);
    }

    [Fact]
    public void TabTitle_WhenNotModified_NoAsterisk()
    {
        var vm = CreateVm();
        vm.IsModified = false;
        Assert.DoesNotMatch(@"^\*", vm.TabTitle);
    }

    [Fact]
    public void FilePath_Set_UpdatesTabTitle()
    {
        var vm = CreateVm();
        vm.FilePath = @"C:\docs\report.pdf";
        Assert.Contains("report.pdf", vm.TabTitle);
    }

    // ----------------------------------------------------------
    // Annotation Mode
    // ----------------------------------------------------------

    [Fact]
    public void IsAnnotationMode_SetTrue_IsReflected()
    {
        var vm = CreateVm();
        vm.IsAnnotationMode = true;
        Assert.True(vm.IsAnnotationMode);
    }

    [Fact]
    public void ActiveAnnotationTool_ChangesToHighlight()
    {
        var vm = CreateVm();
        vm.ActiveAnnotationTool = AnnotationType.Highlight;
        Assert.Equal(AnnotationType.Highlight, vm.ActiveAnnotationTool);
    }

    [Fact]
    public void ActiveAnnotationTool_ChangesToFreehandDraw()
    {
        var vm = CreateVm();
        vm.ActiveAnnotationTool = AnnotationType.FreehandDraw;
        Assert.Equal(AnnotationType.FreehandDraw, vm.ActiveAnnotationTool);
    }

    // ----------------------------------------------------------
    // Annotations
    // ----------------------------------------------------------

    [Fact]
    public void AddAnnotation_IncreasesAnnotationCount()
    {
        var vm = CreateVm();
        var ann = new PdfAnnotation { Type = AnnotationType.Text, Text = "Note" };

        vm.AddAnnotation(ann);

        Assert.Single(vm.Annotations);
    }

    [Fact]
    public void AddAnnotation_SetsCurrentPageIndex()
    {
        var vm = CreateVm();
        var ann = new PdfAnnotation { Type = AnnotationType.Highlight };

        vm.AddAnnotation(ann);

        Assert.Equal(0, ann.PageIndex); // current page is 0 for new VM
    }

    [Fact]
    public void AddAnnotation_SetsIsModified()
    {
        var vm = CreateVm();
        vm.AddAnnotation(new PdfAnnotation { Type = AnnotationType.Text });

        Assert.True(vm.IsModified);
    }

    [Fact]
    public void RemoveAnnotation_ByKnownId_RemovesIt()
    {
        var vm = CreateVm();
        var ann = new PdfAnnotation { Type = AnnotationType.Text, Id = "test-id" };
        vm.AddAnnotation(ann);

        vm.RemoveAnnotation("test-id");

        Assert.Empty(vm.Annotations);
    }

    [Fact]
    public void RemoveAnnotation_UnknownId_DoesNotThrow()
    {
        var vm = CreateVm();
        var ex = Record.Exception(() => vm.RemoveAnnotation("does-not-exist"));
        Assert.Null(ex);
    }

    [Fact]
    public void AddMultipleAnnotations_AllAppear()
    {
        var vm = CreateVm();
        vm.AddAnnotation(new PdfAnnotation { Type = AnnotationType.Text });
        vm.AddAnnotation(new PdfAnnotation { Type = AnnotationType.Highlight });
        vm.AddAnnotation(new PdfAnnotation { Type = AnnotationType.Rectangle });

        Assert.Equal(3, vm.Annotations.Count);
    }

    // ----------------------------------------------------------
    // Multi-page selection helper
    // ----------------------------------------------------------

    [Fact]
    public void UpdateSelectedPages_SinglePage_NotMultiSelection()
    {
        var vm = CreateVm();
        vm.UpdateSelectedPages(new[] { 0 });

        Assert.False(vm.HasMultipleSelection);
        Assert.Empty(vm.SelectionInfoText);
    }

    [Fact]
    public void UpdateSelectedPages_MultiplePages_IsMultiSelection()
    {
        var vm = CreateVm();
        vm.UpdateSelectedPages(new[] { 0, 2, 4 });

        Assert.True(vm.HasMultipleSelection);
        Assert.Contains("3", vm.SelectionInfoText);
    }

    [Fact]
    public void SelectedPageIndices_AreOrderedAscending()
    {
        var vm = CreateVm();
        vm.UpdateSelectedPages(new[] { 4, 0, 2 });

        Assert.Equal(new[] { 0, 2, 4 }, vm.SelectedPageIndices);
    }

    // ----------------------------------------------------------
    // Undo/Redo history
    // ----------------------------------------------------------

    [Fact]
    public void GetUndoHistory_Initially_IsEmpty()
    {
        var vm = CreateVm();
        Assert.Empty(vm.GetUndoHistory());
    }

    [Fact]
    public void GetRedoHistory_Initially_IsEmpty()
    {
        var vm = CreateVm();
        Assert.Empty(vm.GetRedoHistory());
    }

    // ----------------------------------------------------------
    // SearchQuery
    // ----------------------------------------------------------

    [Fact]
    public void SearchQuery_Default_IsEmpty()
    {
        var vm = CreateVm();
        Assert.Equal(string.Empty, vm.SearchQuery);
    }

    [Fact]
    public void SearchQuery_Set_UpdatesValue()
    {
        var vm = CreateVm();
        vm.SearchQuery = "invoice";
        Assert.Equal("invoice", vm.SearchQuery);
    }

    [Fact]
    public void IsSearchVisible_Default_IsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.IsSearchVisible);
    }

    [Fact]
    public void IsSearchVisible_SetTrue_IsReflected()
    {
        var vm = CreateVm();
        vm.IsSearchVisible = true;
        Assert.True(vm.IsSearchVisible);
    }
}
