using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for VisualDiffService (pixel-level PDF comparison)
/// </summary>
public class VisualDiffServiceTests
{
    private readonly VisualDiffService _service = new();

    [Fact]
    public async Task CompareVisually_IdenticalDocs_NoVisualDifferences()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(1, "Identical content");
        var result = await _service.CompareVisuallyAsync(
            pdfBytes, pdfBytes, "DocA", "DocA",
            new VisualDiffService.VisualDiffOptions { Dpi = 72 });

        Assert.NotNull(result);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(0, result.PagesWithDifferences);
        Assert.Equal("DocA", result.LeftFileName);
    }

    [Fact]
    public async Task CompareVisually_DifferentDocs_DetectsDifferences()
    {
        var leftPdf = TestPdfGenerator.CreatePdfWithContent("Hello World");
        var rightPdf = TestPdfGenerator.CreatePdfWithContent("Goodbye World");
        var result = await _service.CompareVisuallyAsync(
            leftPdf, rightPdf, "Left", "Right",
            new VisualDiffService.VisualDiffOptions { Dpi = 72, IncludeTextComparison = true });

        Assert.NotNull(result);
        Assert.Equal(1, result.TotalPages);
        // Text is different so there should be at least some pixel differences
        Assert.True(result.Pages[0].DiffImage != null);
        Assert.NotNull(result.TextComparison);
    }

    [Fact]
    public async Task CompareVisually_DifferentPageCounts_HandlesCorrectly()
    {
        var leftPdf = TestPdfGenerator.CreateSimplePdf(2);
        var rightPdf = TestPdfGenerator.CreateSimplePdf(3);
        var result = await _service.CompareVisuallyAsync(
            leftPdf, rightPdf, "Left", "Right",
            new VisualDiffService.VisualDiffOptions { Dpi = 72 });

        Assert.Equal(3, result.TotalPages); // max of both
        Assert.Equal(100.0, result.Pages[2].DifferencePercent); // page only in right
        Assert.Contains("Added", result.Pages[2].Status);
    }

    [Fact]
    public async Task CompareVisually_RendersImages()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var result = await _service.CompareVisuallyAsync(
            pdf, pdf, "A", "B",
            new VisualDiffService.VisualDiffOptions { Dpi = 72 });

        var page = result.Pages[0];
        Assert.NotNull(page.LeftImage);
        Assert.NotNull(page.RightImage);
        Assert.True(page.LeftImage!.Length > 100);
    }

    [Fact]
    public async Task CompareVisually_WithCancellation_ThrowsOperationCanceled()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(5);
        var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel immediately

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.CompareVisuallyAsync(pdf, pdf, cancellationToken: cts.Token));
    }

    [Fact]
    public void GenerateSideBySideImage_CreatesCompositeImage()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var renderService = new PdfRenderService();
        var (pixels, w, h) = renderService.RenderPage(pdf, 0, 200, 200);

        using var img = new ImageMagick.MagickImage();
        img.ReadPixels(pixels, new ImageMagick.PixelReadSettings(
            (uint)w, (uint)h, ImageMagick.StorageType.Char, ImageMagick.PixelMapping.BGRA));
        img.Format = ImageMagick.MagickFormat.Png;
        using var ms = new MemoryStream();
        img.Write(ms);
        var pngBytes = ms.ToArray();

        var sideBySide = _service.GenerateSideBySideImage(pngBytes, pngBytes, null);
        Assert.NotNull(sideBySide);
        Assert.True(sideBySide.Length > 100);
    }

    [Fact]
    public async Task GenerateVisualDiffHtmlReport_ProducesHtml()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var result = await _service.CompareVisuallyAsync(
            pdf, pdf, "A", "B",
            new VisualDiffService.VisualDiffOptions { Dpi = 72 });

        var html = _service.GenerateVisualDiffHtmlReport(result);
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("Visual Comparison Report", html);
        Assert.Contains("data:image/png;base64", html);
    }

    [Fact]
    public void MergeChanges_CopiesPagesFromRight()
    {
        var leftPdf = TestPdfGenerator.CreatePdfWithContent("Page 1 Left", "Page 2 Left");
        var rightPdf = TestPdfGenerator.CreatePdfWithContent("Page 1 Right", "Page 2 Right");

        var merged = _service.MergeChanges(leftPdf, rightPdf, new[] { 0 }); // merge page 1 from right
        Assert.NotNull(merged);
        Assert.True(merged.Length > 100);

        var pdfOps = new PdfOperations();
        Assert.Equal(2, pdfOps.GetPageCount(merged)); // still 2 pages
    }
}
