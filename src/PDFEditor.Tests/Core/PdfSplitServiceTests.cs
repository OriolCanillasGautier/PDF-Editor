using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for PdfSplitService (page extraction, reordering, splitting)
/// </summary>
public class PdfSplitServiceTests
{
    private readonly PdfSplitService _splitService = new();
    private readonly PdfOperations _pdfOps = new();

    [Fact]
    public void ExtractPages_ValidRange_ReturnsSubset()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(5);
        var result = _splitService.ExtractPages(pdf, 2, 4);
        Assert.NotNull(result);
        Assert.Equal(3, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void ExtractSpecificPages_SelectedPages_ReturnsCorrectCount()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(5);
        var result = _splitService.ExtractSpecificPages(pdf, new[] { 1, 3, 5 });
        Assert.NotNull(result);
        Assert.Equal(3, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void SplitAll_ReturnsOneFilePerPage()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(4);
        var results = _splitService.SplitAll(pdf);
        Assert.NotNull(results);
        Assert.Equal(4, results.Count);
        foreach (var pagePdf in results)
        {
            Assert.Equal(1, _pdfOps.GetPageCount(pagePdf));
        }
    }

    [Fact]
    public void MovePage_MovesForward_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var result = _splitService.MovePage(pdf, 0, 2);
        Assert.NotNull(result);
        Assert.Equal(3, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void MovePage_MovesBackward_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var result = _splitService.MovePage(pdf, 2, 0);
        Assert.NotNull(result);
        Assert.Equal(3, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void InsertPages_InsertsAtPosition_IncreasesCount()
    {
        var original = TestPdfGenerator.CreateSimplePdf(2);
        var toInsert = TestPdfGenerator.CreateSimplePdf(1);
        var result = _splitService.InsertPages(original, toInsert, 1);
        Assert.NotNull(result);
        Assert.Equal(3, _pdfOps.GetPageCount(result));
    }
}
