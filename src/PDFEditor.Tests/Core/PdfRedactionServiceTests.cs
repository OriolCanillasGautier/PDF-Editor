using PDFEditor.Core.Abstractions;
using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

public class PdfRedactionServiceTests
{
    private readonly PdfRedactionService _service = new();

    // ── FindRedactionTargets ──────────────────────────────────────────

    [Fact]
    public void FindRedactionTargets_EmptyText_ReturnsEmpty()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf();
        var result = _service.FindRedactionTargets(pdf, "");
        Assert.Empty(result);
    }

    [Fact]
    public void FindRedactionTargets_TextNotFound_ReturnsEmpty()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1, "Hello World");
        var result = _service.FindRedactionTargets(pdf, "ZZZZZ_NONEXISTENT");
        Assert.Empty(result);
    }

    [Fact]
    public void FindRedactionTargets_TextFound_ReturnsMatches()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("Page one has confidential data.", "Page two is clean.");
        var result = _service.FindRedactionTargets(pdf, "confidential");
        Assert.Single(result);
        Assert.Equal(0, result[0].PageIndex);
        Assert.Contains("confidential", result[0].MatchedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindRedactionTargets_CaseInsensitive_FindsAll()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("SECRET secret Secret");
        var result = _service.FindRedactionTargets(pdf, "secret", caseSensitive: false);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void FindRedactionTargets_CaseSensitive_FindsExact()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("SECRET secret Secret");
        var result = _service.FindRedactionTargets(pdf, "SECRET", caseSensitive: true);
        Assert.Single(result);
    }

    [Fact]
    public void FindRedactionTargets_MultiplePages_FindsAll()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent(
            "Confidential information here.",
            "No secrets on this page.",
            "More Confidential data here.");
        var result = _service.FindRedactionTargets(pdf, "Confidential");
        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.PageIndex == 0);
        Assert.Contains(result, r => r.PageIndex == 2);
    }

    // ── RedactText ────────────────────────────────────────────────────

    [Fact]
    public void RedactText_EmptyText_ReturnsSameBytes()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf();
        var result = _service.RedactText(pdf, "");
        Assert.Equal(pdf, result);
    }

    [Fact]
    public void RedactText_TextNotFound_ReturnsSameBytes()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1, "Hello World");
        var result = _service.RedactText(pdf, "ZZZZZ_NONEXISTENT");
        Assert.Equal(pdf, result);
    }

    [Fact]
    public void RedactText_TextFound_ReturnsModifiedPdf()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("This document has sensitive data that should be redacted.");
        var result = _service.RedactText(pdf, "sensitive");
        Assert.NotEmpty(result);
        Assert.NotEqual(pdf, result);
        // Result should be valid PDF
        Assert.True(result.Length > 100);
    }

    [Fact]
    public void RedactText_PreservesPageCount()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("Page 1 sensitive", "Page 2 sensitive", "Page 3");
        var result = _service.RedactText(pdf, "sensitive");
        
        using var reader = new iText.Kernel.Pdf.PdfReader(new MemoryStream(result));
        var doc = new iText.Kernel.Pdf.PdfDocument(reader);
        Assert.Equal(3, doc.GetNumberOfPages());
        doc.Close();
    }

    // ── RedactAreas ───────────────────────────────────────────────────

    [Fact]
    public void RedactAreas_EmptyList_ReturnsSameBytes()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf();
        var result = _service.RedactAreas(pdf, new List<RedactionArea>());
        Assert.Equal(pdf, result);
    }

    [Fact]
    public void RedactAreas_ValidArea_ReturnsModifiedPdf()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var areas = new List<RedactionArea>
        {
            new() { PageIndex = 0, X = 50, Y = 700, Width = 200, Height = 20 }
        };
        var result = _service.RedactAreas(pdf, areas);
        Assert.NotEmpty(result);
        Assert.NotEqual(pdf, result);
    }

    [Fact]
    public void RedactAreas_WithReplacementText_ReturnsModifiedPdf()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var areas = new List<RedactionArea>
        {
            new() { PageIndex = 0, X = 50, Y = 700, Width = 200, Height = 20, ReplacementText = "[REDACTED]" }
        };
        var result = _service.RedactAreas(pdf, areas);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void RedactAreas_InvalidPageIndex_IgnoresGracefully()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var areas = new List<RedactionArea>
        {
            new() { PageIndex = 99, X = 50, Y = 700, Width = 200, Height = 20 }
        };
        // Should not throw, just skip invalid page
        var result = _service.RedactAreas(pdf, areas);
        Assert.NotEmpty(result);
    }

    // ── RedactPages ───────────────────────────────────────────────────

    [Fact]
    public void RedactPages_EmptyArray_ReturnsSameBytes()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf();
        var result = _service.RedactPages(pdf, Array.Empty<int>());
        Assert.Equal(pdf, result);
    }

    [Fact]
    public void RedactPages_ValidPage_ReturnsModifiedPdf()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var result = _service.RedactPages(pdf, new[] { 1 }); // Redact page 2 (0-based)
        Assert.NotEmpty(result);
        Assert.NotEqual(pdf, result);
    }

    [Fact]
    public void RedactPages_PreservesPageCount()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var result = _service.RedactPages(pdf, new[] { 0, 2 });
        
        using var reader = new iText.Kernel.Pdf.PdfReader(new MemoryStream(result));
        var doc = new iText.Kernel.Pdf.PdfDocument(reader);
        Assert.Equal(3, doc.GetNumberOfPages());
        doc.Close();
    }

    [Fact]
    public void RedactPages_InvalidIndex_IgnoresGracefully()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var result = _service.RedactPages(pdf, new[] { -1, 100 });
        Assert.NotEmpty(result);
    }
}
