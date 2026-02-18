using Xunit;
using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for TableOfContentsService — heading detection and bookmark generation.
/// </summary>
public class TableOfContentsServiceTests
{
    private readonly TableOfContentsService _service = new();

    [Fact]
    public void DetectHeadings_SimplePdf_ReturnsListOfHeadings()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);

        var headings = _service.DetectHeadings(pdf);

        Assert.NotNull(headings);
        // May or may not find headings depending on font size uniformity
    }

    [Fact]
    public void DetectHeadings_NullBytes_ThrowsException()
    {
        Assert.ThrowsAny<Exception>(() => _service.DetectHeadings(null!));
    }

    [Fact]
    public void AddOutlines_SimplePdf_ReturnsValidPdf()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);

        var result = _service.AddOutlines(pdf);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void AddOutlines_WithCustomHeadings_AddsBookmarks()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var headings = new List<TableOfContentsService.DetectedHeading>
        {
            new() { Text = "Chapter 1", Level = 1, PageNumber = 1 },
            new() { Text = "Section 1.1", Level = 2, PageNumber = 1 },
            new() { Text = "Chapter 2", Level = 1, PageNumber = 2 },
            new() { Text = "Chapter 3", Level = 1, PageNumber = 3 },
        };

        var result = _service.AddOutlines(pdf, headings);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        // Verify outlines exist by reopening
        using var reader = new iText.Kernel.Pdf.PdfReader(new MemoryStream(result));
        using var doc = new iText.Kernel.Pdf.PdfDocument(reader);
        var outlines = doc.GetOutlines(false);
        Assert.NotNull(outlines);
        var children = outlines.GetAllChildren();
        Assert.NotNull(children);
        Assert.True(children.Count > 0, "Should have at least 1 top-level outline");
    }

    [Fact]
    public void GenerateTocText_WithHeadings_ReturnsFormattedText()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var headings = new List<TableOfContentsService.DetectedHeading>
        {
            new() { Text = "Introduction", Level = 1, PageNumber = 1 },
            new() { Text = "Background", Level = 2, PageNumber = 1 },
            new() { Text = "Methods", Level = 1, PageNumber = 2 },
        };

        var toc = _service.GenerateTocText(headings);

        Assert.NotEmpty(toc);
        Assert.Contains("Introduction", toc);
        Assert.Contains("Methods", toc);
        Assert.Contains("TABLE OF CONTENTS", toc);
    }

    [Fact]
    public void GenerateTocText_EmptyHeadings_ReturnsHeader()
    {
        var toc = _service.GenerateTocText(new List<TableOfContentsService.DetectedHeading>());

        Assert.NotEmpty(toc);
        Assert.Contains("TABLE OF CONTENTS", toc);
    }

    [Fact]
    public void DetectedHeading_PageNumber_IsPositive()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);

        var headings = _service.DetectHeadings(pdf);

        foreach (var h in headings)
        {
            Assert.True(h.PageNumber >= 1, "Page number should be >= 1");
            Assert.True(h.Level >= 1, "Heading level should be >= 1");
            Assert.False(string.IsNullOrEmpty(h.Text), "Heading text should not be empty");
        }
    }
}
