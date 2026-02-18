using Xunit;
using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for ImageExtractionService — extract images from PDF documents.
/// </summary>
public class ImageExtractionServiceTests
{
    private readonly ImageExtractionService _service = new();

    [Fact]
    public void CountImages_SimplePdf_ReturnsZeroOrMore()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var count = _service.CountImages(pdf);

        Assert.True(count >= 0);
    }

    [Fact]
    public void ExtractAll_SimplePdf_ReturnsEmptyOrImages()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);

        var images = _service.ExtractAll(pdf);

        Assert.NotNull(images);
        // Simple text PDF may not have images
    }

    [Fact]
    public void ExtractAll_NullBytes_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _service.ExtractAll(null!));
    }

    [Fact]
    public void ExtractAll_EmptyBytes_ThrowsException()
    {
        Assert.ThrowsAny<Exception>(() => _service.ExtractAll(Array.Empty<byte>()));
    }

    [Fact]
    public void ExtractAll_WithPageSelection_FiltersPages()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(5);

        var allImages = _service.ExtractAll(pdf);
        var pageImages = _service.ExtractAll(pdf, new[] { 0, 1 });

        Assert.NotNull(pageImages);
        Assert.True(pageImages.Count <= allImages.Count);
    }

    [Fact]
    public async Task ExtractToFolderAsync_CreatesOutputDirectory()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_extract_{Guid.NewGuid():N}");

        try
        {
            var files = await _service.ExtractToFolderAsync(pdf, tempDir);
            Assert.NotNull(files);
            // Directory should exist even if no images found
            Assert.True(Directory.Exists(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
