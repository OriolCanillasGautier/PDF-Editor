using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for PdfExportService (image, text, HTML export)
/// </summary>
public class PdfExportServiceTests
{
    private readonly PdfExportService _exportService = new();

    [Fact]
    public void ExportPageToImage_PNG_ReturnsImageData()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var result = _exportService.ExportPageToImage(pdf, 0, "PNG", 72);
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void ExportPageToImage_JPEG_ReturnsImageData()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var result = _exportService.ExportPageToImage(pdf, 0, "JPEG", 72);
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void ExportPageToImage_BMP_ReturnsImageData()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var result = _exportService.ExportPageToImage(pdf, 0, "BMP", 72);
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void ExportAllPagesToImages_MultiplePages_ReturnsAll()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var results = _exportService.ExportAllPagesToImages(pdf, "PNG", 72);
        Assert.Equal(3, results.Count);
        foreach (var (pageNum, imageData) in results)
        {
            Assert.True(pageNum >= 1);
            Assert.True(imageData.Length > 0);
        }
    }

    [Fact]
    public void ExportAllPagesToImages_ProgressCallback_IsCalled()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);
        var progressCalls = new List<(int current, int total)>();
        _exportService.ExportAllPagesToImages(pdf, "PNG", 72,
            (current, total) => progressCalls.Add((current, total)));
        Assert.True(progressCalls.Count >= 2);
    }

    [Fact]
    public void ExportAllText_ReturnsPageText()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("Export text test content");
        var text = _exportService.ExportAllText(pdf);
        Assert.Contains("Export text test content", text);
    }

    [Fact]
    public void ExportToHtml_ReturnsValidHtml()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var html = _exportService.ExportToHtml(pdf, "Test Document");
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("Test Document", html);
        Assert.Contains("data:image/png;base64,", html);
        Assert.Contains("Page 1 of 1", html);
    }

    [Fact]
    public void ExportToHtml_MultiplePages_ContainsAllPageLabels()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var html = _exportService.ExportToHtml(pdf, "Multi Page");
        Assert.Contains("Page 1 of 3", html);
        Assert.Contains("Page 2 of 3", html);
        Assert.Contains("Page 3 of 3", html);
    }

    [Fact]
    public void CreatePdfFromImages_ProducesValidPdf()
    {
        // First generate an image from a PDF page, then create a PDF from it
        var sourcePdf = TestPdfGenerator.CreateMinimalPdf();
        var imgBytes = _exportService.ExportPageToImage(sourcePdf, 0, "PNG", 72);

        var result = _exportService.CreatePdfFromImages(new List<byte[]> { imgBytes });
        Assert.NotNull(result);
        Assert.True(result.Length > 0);

        var pdfOps = new PdfOperations();
        Assert.True(pdfOps.GetPageCount(result) >= 1);
    }
}
