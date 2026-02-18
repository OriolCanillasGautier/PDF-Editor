using PDFEditor.Core.Services;
using Xunit;

namespace PDFEditor.Tests.Core;

public class AnnotationExportServiceTests
{
    private readonly AnnotationExportService _service = new();

    private static List<PdfAnnotation> CreateSampleAnnotations()
    {
        return new List<PdfAnnotation>
        {
            new()
            {
                Type = AnnotationType.Text,
                PageIndex = 0,
                Text = "Important note",
                X = 0.1, Y = 0.2, Width = 0.3, Height = 0.05,
                Color = "#FF0000",
                FontSize = 12f
            },
            new()
            {
                Type = AnnotationType.Highlight,
                PageIndex = 0,
                X = 0.1, Y = 0.4, Width = 0.5, Height = 0.02,
                FillColor = "#FFFF00",
                FillOpacity = 0.4f
            },
            new()
            {
                Type = AnnotationType.StickyNote,
                PageIndex = 1,
                X = 0.8, Y = 0.1, Width = 0.05, Height = 0.05,
                NoteContent = "Review this section",
                NoteColor = "#FFFACD"
            },
            new()
            {
                Type = AnnotationType.Rectangle,
                PageIndex = 2,
                X = 0.2, Y = 0.3, Width = 0.4, Height = 0.2,
                StrokeColor = "#0000FF",
                StrokeWidth = 2f
            },
            new()
            {
                Type = AnnotationType.Stamp,
                PageIndex = 2,
                StampPreset = StampType.Approved,
                StampText = "APPROVED",
                X = 0.3, Y = 0.5, Width = 0.2, Height = 0.06
            }
        };
    }

    // ── GenerateTextReport ────────────────────────────────────────────

    [Fact]
    public void GenerateTextReport_EmptyList_ReturnsReport()
    {
        var report = _service.GenerateTextReport(new List<PdfAnnotation>());
        Assert.False(string.IsNullOrWhiteSpace(report));
        Assert.Contains("0", report); // zero annotations
    }

    [Fact]
    public void GenerateTextReport_WithAnnotations_IncludesAll()
    {
        var annotations = CreateSampleAnnotations();
        var report = _service.GenerateTextReport(annotations, "test.pdf");
        Assert.Contains("test.pdf", report);
        Assert.Contains("5", report); // 5 annotations total
        Assert.Contains("Text", report);
        Assert.Contains("Highlight", report);
        Assert.Contains("StickyNote", report);
    }

    [Fact]
    public void GenerateTextReport_GroupsByPage()
    {
        var annotations = CreateSampleAnnotations();
        var report = _service.GenerateTextReport(annotations);
        // Should have page groupings: pages 0, 1, 2
        Assert.Contains("Page 1", report); // Page 0 displayed as 1
        Assert.Contains("Page 2", report);
        Assert.Contains("Page 3", report);
    }

    [Fact]
    public void GenerateTextReport_NoDocName_StillWorks()
    {
        var annotations = CreateSampleAnnotations();
        var report = _service.GenerateTextReport(annotations);
        Assert.False(string.IsNullOrWhiteSpace(report));
    }

    // ── GenerateHtmlReport ────────────────────────────────────────────

    [Fact]
    public void GenerateHtmlReport_EmptyList_ReturnsValidHtml()
    {
        var html = _service.GenerateHtmlReport(new List<PdfAnnotation>());
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("</html>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateHtmlReport_WithAnnotations_IncludesDetails()
    {
        var annotations = CreateSampleAnnotations();
        var html = _service.GenerateHtmlReport(annotations, "sample.pdf");
        Assert.Contains("sample.pdf", html);
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Text", html);
        Assert.Contains("Highlight", html);
    }

    [Fact]
    public void GenerateHtmlReport_HasStyleTag()
    {
        var annotations = CreateSampleAnnotations();
        var html = _service.GenerateHtmlReport(annotations);
        Assert.Contains("<style", html, StringComparison.OrdinalIgnoreCase);
    }

    // ── GenerateCsvReport ─────────────────────────────────────────────

    [Fact]
    public void GenerateCsvReport_EmptyList_ReturnsHeaderOnly()
    {
        var csv = _service.GenerateCsvReport(new List<PdfAnnotation>());
        Assert.False(string.IsNullOrWhiteSpace(csv));
        var lines = csv.Trim().Split('\n');
        Assert.Single(lines); // header row only
    }

    [Fact]
    public void GenerateCsvReport_WithAnnotations_HasCorrectRowCount()
    {
        var annotations = CreateSampleAnnotations();
        var csv = _service.GenerateCsvReport(annotations);
        var lines = csv.Trim().Split('\n');
        // 1 header + 5 data rows
        Assert.Equal(6, lines.Length);
    }

    [Fact]
    public void GenerateCsvReport_IncludesTypes()
    {
        var annotations = CreateSampleAnnotations();
        var csv = _service.GenerateCsvReport(annotations);
        Assert.Contains("Text", csv);
        Assert.Contains("Highlight", csv);
        Assert.Contains("StickyNote", csv);
        Assert.Contains("Rectangle", csv);
        Assert.Contains("Stamp", csv);
    }

    [Fact]
    public void GenerateCsvReport_HeaderHasExpectedColumns()
    {
        var csv = _service.GenerateCsvReport(new List<PdfAnnotation>());
        var header = csv.Trim().Split('\n')[0];
        Assert.Contains("Page", header);
        Assert.Contains("Type", header);
    }

    [Fact]
    public void GenerateCsvReport_EscapesCommasInText()
    {
        var annotations = new List<PdfAnnotation>
        {
            new()
            {
                Type = AnnotationType.Text,
                PageIndex = 0,
                Text = "Hello, World",
                X = 0.1, Y = 0.2, Width = 0.3, Height = 0.05
            }
        };
        var csv = _service.GenerateCsvReport(annotations);
        // Text containing comma should be quoted
        Assert.Contains("\"Hello, World\"", csv);
    }
}
