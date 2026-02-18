using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for DeskewService, BackgroundRemovalService, ImageCompressService, ImageReplaceService
/// </summary>
public class ImageProcessingServiceTests
{
    // ===== DeskewService Tests =====

    [Fact]
    public void DeskewService_AnalyzeSkew_ReturnsResultForAllPages()
    {
        var service = new DeskewService();
        var pdf = TestPdfGenerator.CreateSimplePdf(3);

        var results = service.AnalyzeSkew(pdf);

        Assert.Equal(3, results.Count);
        foreach (var r in results)
        {
            Assert.True(r.PageIndex >= 0);
        }
    }

    [Fact]
    public void DeskewService_AnalyzeSkew_SinglePage()
    {
        var service = new DeskewService();
        var pdf = TestPdfGenerator.CreateSimplePdf(2);

        var results = service.AnalyzeSkew(pdf);

        Assert.Equal(2, results.Count);
        Assert.Equal(0, results[0].PageIndex);
        Assert.Equal(1, results[1].PageIndex);
    }

    [Fact]
    public void DeskewService_DeskewAll_ReturnsPdfBytes()
    {
        var service = new DeskewService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var result = service.DeskewAll(pdf, overrideAngle: 5.0);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void DeskewService_DeskewPages_SpecificPages()
    {
        var service = new DeskewService();
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var analyses = service.AnalyzeSkew(pdf);

        var result = service.DeskewPages(pdf, analyses);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    // ===== BackgroundRemovalService Tests =====

    [Fact]
    public void BackgroundRemoval_AnalyzeBackgrounds_ReturnsResults()
    {
        var service = new BackgroundRemovalService();
        var pdf = TestPdfGenerator.CreateSimplePdf(2);

        var results = service.AnalyzeBackgrounds(pdf);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void BackgroundRemoval_RemoveBackgroundFromImage_ReturnsBytes()
    {
        var service = new BackgroundRemovalService();
        // Create a simple PNG via Magick.NET
        using var img = new ImageMagick.MagickImage(ImageMagick.MagickColors.LightGray, 100, 100);
        img.Format = ImageMagick.MagickFormat.Png;
        var imageBytes = img.ToByteArray();

        var options = new BackgroundRemovalOptions();
        var result = service.RemoveBackgroundFromImage(imageBytes, options);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    // ===== ImageCompressService Tests =====

    [Fact]
    public void ImageCompress_AnalyzeImages_ReturnsList()
    {
        var service = new ImageCompressService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var results = service.AnalyzeImages(pdf);

        Assert.NotNull(results);
        // Simple text PDF may have no images
    }

    [Fact]
    public async Task ImageCompress_CompressAsync_ReturnsBytes()
    {
        var service = new ImageCompressService();
        var pdf = TestPdfGenerator.CreateSimplePdf(2);

        var options = new ImageCompressionOptions { JpegQuality = 75 };
        var result = await service.CompressAsync(pdf, options);

        Assert.NotNull(result);
        Assert.True(result.OutputPdf.Length > 0);
    }

    [Fact]
    public async Task ImageCompress_QuickCompress_ReturnsBytes()
    {
        var service = new ImageCompressService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var result = await service.QuickCompressAsync(pdf);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    // ===== ImageReplaceService Tests =====

    [Fact]
    public void ImageReplace_ListImages_ReturnsList()
    {
        var service = new ImageReplaceService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var images = service.ListImages(pdf);

        Assert.NotNull(images);
        // Text-only PDF won't have images
    }
}
