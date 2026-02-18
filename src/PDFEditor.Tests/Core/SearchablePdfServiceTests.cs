using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

public class SearchablePdfServiceTests
{
    // Use a fresh OCR service for each test — tessdata may not be available
    // so we test only the non-OCR-dependent methods here.
    private readonly SearchablePdfService _service = new(new TesseractOcrService());

    // ── IsPageImageBased ──────────────────────────────────────────────

    [Fact]
    public void IsPageImageBased_TextPage_ReturnsFalse()
    {
        // Pages created by TestPdfGenerator have text content
        var pdf = TestPdfGenerator.CreatePdfWithContent("This page has real text.");
        Assert.False(_service.IsPageImageBased(pdf, 0));
    }

    [Fact]
    public void IsPageImageBased_InvalidIndex_ReturnsFalse()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        Assert.False(_service.IsPageImageBased(pdf, 99));
        Assert.False(_service.IsPageImageBased(pdf, -1));
    }

    [Fact]
    public void IsPageImageBased_MultiPage_ChecksCorrectPage()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("Page one text", "Page two text", "Page three text");
        // All pages have text, none should be image-based
        Assert.False(_service.IsPageImageBased(pdf, 0));
        Assert.False(_service.IsPageImageBased(pdf, 1));
        Assert.False(_service.IsPageImageBased(pdf, 2));
    }

    // ── CountImageBasedPages ──────────────────────────────────────────

    [Fact]
    public void CountImageBasedPages_AllTextPages_ZeroImageBased()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("Text 1", "Text 2", "Text 3");
        var (imageBased, total) = _service.CountImageBasedPages(pdf);
        Assert.Equal(3, total);
        Assert.Equal(0, imageBased);
    }

    [Fact]
    public void CountImageBasedPages_SinglePage_ReturnsCorrectTotal()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1, "Hello World");
        var (imageBased, total) = _service.CountImageBasedPages(pdf);
        Assert.Equal(1, total);
        Assert.Equal(0, imageBased);
    }

    [Fact]
    public void CountImageBasedPages_MultiplePages_ReturnsCorrectTotal()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(5);
        var (_, total) = _service.CountImageBasedPages(pdf);
        Assert.Equal(5, total);
    }

    [Fact]
    public void CountImageBasedPages_InvalidPdf_ReturnsZero()
    {
        var (imageBased, total) = _service.CountImageBasedPages(new byte[] { 0, 1, 2 });
        Assert.Equal(0, total);
        Assert.Equal(0, imageBased);
    }
}
