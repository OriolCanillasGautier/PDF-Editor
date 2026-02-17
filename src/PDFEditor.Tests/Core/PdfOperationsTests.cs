using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for PdfOperations service (iText7-based PDF manipulation)
/// </summary>
public class PdfOperationsTests
{
    private readonly PdfOperations _pdfOps = new();

    [Fact]
    public void GetPageCount_ValidPdf_ReturnsCorrectCount()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(5);
        var count = _pdfOps.GetPageCount(pdf);
        Assert.Equal(5, count);
    }

    [Fact]
    public void GetPageCount_SinglePagePdf_ReturnsOne()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var count = _pdfOps.GetPageCount(pdf);
        Assert.Equal(1, count);
    }

    [Fact]
    public void DeletePages_RemovesSinglePage_DecreasesCount()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var result = _pdfOps.DeletePages(pdf, new[] { 2 });
        var newCount = _pdfOps.GetPageCount(result);
        Assert.Equal(2, newCount);
    }

    [Fact]
    public void DeletePages_RemovesMultiplePages_DecreasesCount()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(5);
        var result = _pdfOps.DeletePages(pdf, new[] { 1, 3, 5 });
        var newCount = _pdfOps.GetPageCount(result);
        Assert.Equal(2, newCount);
    }

    [Fact]
    public void RotatePages_RotatesSinglePage_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var result = _pdfOps.RotatePages(pdf, new[] { 1 }, 90);
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        Assert.Equal(3, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void MergeDocuments_TwoDocuments_CombinesPages()
    {
        var pdf1 = TestPdfGenerator.CreateSimplePdf(2);
        var pdf2 = TestPdfGenerator.CreateSimplePdf(3);
        var result = _pdfOps.MergeDocuments(pdf1, pdf2);
        Assert.Equal(5, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void ExtractText_ValidPage_ReturnsText()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("Hello World Test Text");
        var text = _pdfOps.ExtractText(pdf, 1);
        Assert.Contains("Hello World", text);
    }

    [Fact]
    public void GetMetadata_WithMetadata_ReturnsValues()
    {
        var pdf = TestPdfGenerator.CreatePdfWithMetadata("Test Title", "Test Author", "Test Subject");
        var (title, author, subject) = _pdfOps.GetMetadata(pdf);
        Assert.Equal("Test Title", title);
        Assert.Equal("Test Author", author);
        Assert.Equal("Test Subject", subject);
    }

    [Fact]
    public void SetMetadata_UpdatesValues()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var result = _pdfOps.SetMetadata(pdf, "New Title", "New Author", "New Subject");
        var (title, author, subject) = _pdfOps.GetMetadata(result);
        Assert.Equal("New Title", title);
        Assert.Equal("New Author", author);
        Assert.Equal("New Subject", subject);
    }
}
