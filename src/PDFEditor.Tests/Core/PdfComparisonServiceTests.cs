using PDFEditor.Core.Abstractions;
using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

public class PdfComparisonServiceTests
{
    private readonly PdfComparisonService _service = new();

    // ── AreIdentical ──────────────────────────────────────────────────

    [Fact]
    public void AreIdentical_SameDocument_ReturnsTrue()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("Identical content on every page.");
        Assert.True(_service.AreIdentical(pdf, pdf));
    }

    [Fact]
    public void AreIdentical_DifferentContent_ReturnsFalse()
    {
        var left = TestPdfGenerator.CreatePdfWithContent("Left document content.");
        var right = TestPdfGenerator.CreatePdfWithContent("Right document content.");
        Assert.False(_service.AreIdentical(left, right));
    }

    [Fact]
    public void AreIdentical_DifferentPageCount_ReturnsFalse()
    {
        var left = TestPdfGenerator.CreateSimplePdf(1);
        var right = TestPdfGenerator.CreateSimplePdf(3);
        Assert.False(_service.AreIdentical(left, right));
    }

    // ── Compare ───────────────────────────────────────────────────────

    [Fact]
    public void Compare_IdenticalDocuments_NoDifferences()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("Hello World");
        var result = _service.Compare(pdf, pdf);
        Assert.True(result.AreIdentical);
        Assert.Equal(0, result.TotalDifferences);
    }

    [Fact]
    public void Compare_DifferentContent_FindsDifferences()
    {
        var left = TestPdfGenerator.CreatePdfWithContent("Line one.\nLine two.\nLine three.");
        var right = TestPdfGenerator.CreatePdfWithContent("Line one.\nLine MODIFIED.\nLine three.");
        var result = _service.Compare(left, right);
        Assert.False(result.AreIdentical);
        Assert.True(result.TotalDifferences > 0);
    }

    [Fact]
    public void Compare_MorePagesInRight_DetectsPageAdded()
    {
        var left = TestPdfGenerator.CreatePdfWithContent("Page 1 content.");
        var right = TestPdfGenerator.CreatePdfWithContent("Page 1 content.", "Extra page content.");
        var result = _service.Compare(left, right);
        Assert.False(result.AreIdentical);
        Assert.Contains(result.Differences, d => d.Type == DifferenceType.PageAdded);
    }

    [Fact]
    public void Compare_MorePagesInLeft_DetectsPageRemoved()
    {
        var left = TestPdfGenerator.CreatePdfWithContent("Page 1 content.", "Extra page content.");
        var right = TestPdfGenerator.CreatePdfWithContent("Page 1 content.");
        var result = _service.Compare(left, right);
        Assert.False(result.AreIdentical);
        Assert.Contains(result.Differences, d => d.Type == DifferenceType.PageRemoved);
    }

    [Fact]
    public void Compare_DifferentMetadata_DetectsChange()
    {
        var left = TestPdfGenerator.CreatePdfWithMetadata("Title A", "Author A", "Subject A");
        var right = TestPdfGenerator.CreatePdfWithMetadata("Title B", "Author B", "Subject B");
        var result = _service.Compare(left, right);
        Assert.Contains(result.Differences, d => d.Type == DifferenceType.MetadataChanged);
    }

    [Fact]
    public void Compare_SetsFileNames()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf();
        var result = _service.Compare(pdf, pdf, "left.pdf", "right.pdf");
        Assert.Equal("left.pdf", result.LeftFileName);
        Assert.Equal("right.pdf", result.RightFileName);
    }

    [Fact]
    public void Compare_CountsAreCategorised()
    {
        var left = TestPdfGenerator.CreatePdfWithContent("Same line.\nRemoved line.");
        var right = TestPdfGenerator.CreatePdfWithContent("Same line.\nAdded line.");
        var result = _service.Compare(left, right);
        // TotalDifferences should equal sum of sub-counts
        Assert.Equal(result.Differences.Count, result.TotalDifferences);
    }

    // ── GenerateReport ────────────────────────────────────────────────

    [Fact]
    public void GenerateReport_ProducesNonEmptyText()
    {
        var left = TestPdfGenerator.CreatePdfWithContent("Alpha");
        var right = TestPdfGenerator.CreatePdfWithContent("Beta");
        var result = _service.Compare(left, right, "a.pdf", "b.pdf");
        var report = _service.GenerateReport(result);
        Assert.False(string.IsNullOrWhiteSpace(report));
        Assert.Contains("a.pdf", report);
        Assert.Contains("b.pdf", report);
    }

    [Fact]
    public void GenerateReport_IdenticalDocuments_SaysIdentical()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("Same");
        var result = _service.Compare(pdf, pdf);
        var report = _service.GenerateReport(result);
        Assert.Contains("identical", report, StringComparison.OrdinalIgnoreCase);
    }

    // ── GenerateHtmlReport ────────────────────────────────────────────

    [Fact]
    public void GenerateHtmlReport_ContainsHtmlElements()
    {
        var left = TestPdfGenerator.CreatePdfWithContent("Foo");
        var right = TestPdfGenerator.CreatePdfWithContent("Bar");
        var result = _service.Compare(left, right);
        var html = _service.GenerateHtmlReport(result);
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("</html>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateHtmlReport_IncludesDifferences()
    {
        var left = TestPdfGenerator.CreatePdfWithContent("Hello");
        var right = TestPdfGenerator.CreatePdfWithContent("World");
        var result = _service.Compare(left, right);
        var html = _service.GenerateHtmlReport(result);
        // Should contain some diff markers
        Assert.True(html.Length > 200);
    }
}
