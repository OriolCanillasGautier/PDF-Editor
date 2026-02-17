using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for PdfWatermarkService (text watermarks, headers, footers, page numbers)
/// </summary>
public class PdfWatermarkServiceTests
{
    private readonly PdfWatermarkService _watermarkService = new();
    private readonly PdfOperations _pdfOps = new();

    [Fact]
    public void AddTextWatermark_AppliesWatermark_ReturnsModifiedPdf()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var result = _watermarkService.AddTextWatermark(pdf, "CONFIDENTIAL");
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        Assert.Equal(1, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void AddTextWatermark_CustomParameters_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);
        var result = _watermarkService.AddTextWatermark(pdf, "DRAFT",
            fontSize: 80f, opacity: 0.5f, rotation: 30f);
        Assert.NotNull(result);
        Assert.Equal(2, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void AddTextWatermarkToPages_SpecificPages_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var result = _watermarkService.AddTextWatermarkToPages(pdf, "TEST", new[] { 1, 3 });
        Assert.NotNull(result);
        Assert.Equal(3, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void AddHeaderFooter_WithHeader_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);
        var result = _watermarkService.AddHeaderFooter(pdf, "Document Header", null);
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void AddHeaderFooter_WithFooter_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);
        var result = _watermarkService.AddHeaderFooter(pdf, null, "Page {page} of {pages}");
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void AddHeaderFooter_BothHeaderAndFooter_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);
        var result = _watermarkService.AddHeaderFooter(pdf, "Title", "Page {page}");
        Assert.NotNull(result);
        Assert.Equal(2, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void AddPageNumbers_DefaultFormat_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var result = _watermarkService.AddPageNumbers(pdf);
        Assert.NotNull(result);
        Assert.Equal(3, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void AddPageNumbers_CustomFormat_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);
        var result = _watermarkService.AddPageNumbers(pdf, "- {page} -", atBottom: false, fontSize: 12f);
        Assert.NotNull(result);
        Assert.Equal(2, _pdfOps.GetPageCount(result));
    }
}
