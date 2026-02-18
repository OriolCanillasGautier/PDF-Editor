using PDFEditor.Core.Abstractions;
using PDFEditor.Core.Services.Export;
using PDFEditor.Tests.Helpers;
using System.Text;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for MarkdownExportProvider and CsvExportProvider.
/// </summary>
public class NewExportProviderTests
{
    // ─── Markdown Tests ───────────────────────────────────────────────────────────

    private readonly MarkdownExportProvider _md = new();
    private readonly CsvExportProvider _csv = new();

    [Fact]
    public void MarkdownProvider_FormatName_IsCorrect()
    {
        Assert.Equal("Markdown", _md.FormatName);
        Assert.Contains(".md", _md.SupportedExtensions);
        Assert.True(_md.SupportsBatch);
        Assert.False(_md.SupportsPerPageExport);
    }

    [Fact]
    public async Task MarkdownExport_BasicPdf_ProducesMarkdown()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(2);
        var result = await _md.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "test" });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotEmpty(result.Data);
        Assert.Equal("test.md", result.FileName);
        Assert.Contains("markdown", result.MimeType, StringComparison.OrdinalIgnoreCase);

        var text = Encoding.UTF8.GetString(result.Data);
        Assert.Contains("test", text); // Title included
    }

    [Fact]
    public async Task MarkdownExport_MinimalPdf_ProducesNonEmptyOutput()
    {
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf();
        var result = await _md.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "min" });

        Assert.True(result.Success);
        Assert.True(result.Data.Length > 0);
    }

    [Fact]
    public async Task MarkdownExport_MultiPagePdf_ContainsPageSeparators()
    {
        var pdfBytes = TestPdfGenerator.CreatePdfWithContent("First page content", "Second page content");
        var result = await _md.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "pages" });

        Assert.True(result.Success);
        var text = Encoding.UTF8.GetString(result.Data);
        // Multi-page PDF should have page headers
        Assert.Contains("Page", text);
    }

    [Fact]
    public async Task MarkdownExport_WithHeadings_ContainsMarkdownHeaders()
    {
        // PDF with large-font title text → should become Markdown heading
        using var ms = new MemoryStream();
        var writer = new iText.Kernel.Pdf.PdfWriter(ms);
        var pdf = new iText.Kernel.Pdf.PdfDocument(writer);
        var doc = new iText.Layout.Document(pdf);
        doc.Add(new iText.Layout.Element.Paragraph("Big Title").SetFontSize(24).SetBold());
        doc.Add(new iText.Layout.Element.Paragraph("Body text here.").SetFontSize(12));
        doc.Close();

        var result = await _md.ExportAsync(ms.ToArray(), new ExportOptions { BaseFileName = "headings" });

        Assert.True(result.Success);
        var text = Encoding.UTF8.GetString(result.Data);
        // Should have at least one # heading
        Assert.Contains("#", text);
    }

    [Fact]
    public async Task MarkdownExport_CancellationToken_ReturnsFail()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(5);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _md.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "cancel" }, cts.Token);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task MarkdownExport_ExportPagesAsync_ThrowsNotSupported()
    {
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _md.ExportPagesAsync(pdfBytes, new ExportOptions()));
    }

    [Fact]
    public async Task MarkdownExport_OutputIsUtf8_AndStartsWithTitle()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(1);
        var result = await _md.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "mytitle" });

        Assert.True(result.Success);
        var text = Encoding.UTF8.GetString(result.Data);
        Assert.StartsWith("# mytitle", text.TrimStart());
    }

    [Fact]
    public async Task MarkdownExport_ControlCharsInText_StillSucceeds()
    {
        var illegalText = "Text with \x01\x02\x0B\x1F control chars";
        var pdfBytes = CreatePdfWithText(illegalText);
        var result = await _md.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "ctrl" });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.Data.Length > 0);
    }

    // ─── CSV Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void CsvProvider_FormatName_IsCorrect()
    {
        Assert.Contains("CSV", _csv.FormatName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".csv", _csv.SupportedExtensions);
        Assert.True(_csv.SupportsBatch);
        Assert.False(_csv.SupportsPerPageExport);
    }

    [Fact]
    public async Task CsvExport_BasicPdf_ProducesCsv()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(2);
        var result = await _csv.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "data" });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotEmpty(result.Data);
        Assert.Equal("data.csv", result.FileName);
        Assert.Contains("csv", result.MimeType, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CsvExport_MinimalPdf_ProducesNonEmptyOutput()
    {
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf();
        var result = await _csv.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "min" });

        Assert.True(result.Success);
        Assert.True(result.Data.Length > 0);
    }

    [Fact]
    public async Task CsvExport_MultiPagePdf_ContainsPageComments()
    {
        var pdfBytes = TestPdfGenerator.CreatePdfWithContent("Page one", "Page two", "Page three");
        var result = await _csv.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "pages" });

        Assert.True(result.Success);
        var text = Encoding.UTF8.GetString(result.Data);
        Assert.Contains("Page", text);
    }

    [Fact]
    public async Task CsvExport_SpecialCharsInText_ProperlyQuoted()
    {
        var textWithComma = "Value, with comma";
        var pdfBytes = CreatePdfWithText(textWithComma);
        var result = await _csv.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "quoted" });

        Assert.True(result.Success);
        var text = Encoding.UTF8.GetString(result.Data);
        // Value containing comma should be quoted in CSV
        Assert.Contains("\"", text);
    }

    [Fact]
    public async Task CsvExport_CancellationToken_ReturnsFail()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(5);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _csv.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "cancel" }, cts.Token);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task CsvExport_ExportPagesAsync_ThrowsNotSupported()
    {
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _csv.ExportPagesAsync(pdfBytes, new ExportOptions()));
    }

    [Fact]
    public async Task CsvExport_OutputIsUtf8()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(1);
        var result = await _csv.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "utf8" });

        Assert.True(result.Success);
        // Should be parseable as UTF-8 without BOM issues
        var text = Encoding.UTF8.GetString(result.Data);
        Assert.NotEmpty(text);
    }

    // ─── Registry Tests ───────────────────────────────────────────────────────────

    [Fact]
    public void ExportRegistry_CreateDefault_Contains8Providers()
    {
        // Registry now has 15 built-in providers: Image, Text, HTML, DOCX, XLSX, RTF, Markdown, CSV, JSON, PPTX, EPUB, LaTeX, ODT, ODP, ODS
        var registry = ExportProviderRegistry.CreateDefault();
        Assert.Equal(15, registry.Providers.Count);
    }

    [Fact]
    public void ExportRegistry_CreateDefault_ContainsJsonProvider()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        Assert.NotNull(registry.GetProvidersByExtension(".json").FirstOrDefault());
    }

    [Fact]
    public void ExportRegistry_CreateDefault_ContainsPptxProvider()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        Assert.NotNull(registry.GetProvidersByExtension(".pptx").FirstOrDefault());
    }

    [Fact]
    public void ExportRegistry_CreateDefault_ContainsMarkdownProvider()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        Assert.NotNull(registry.GetProviderByName("Markdown"));
        Assert.NotEmpty(registry.GetProvidersByExtension(".md"));
    }

    [Fact]
    public void ExportRegistry_CreateDefault_ContainsCsvProvider()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        Assert.NotNull(registry.GetProvidersByExtension(".csv").FirstOrDefault());
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static byte[] CreatePdfWithText(string text)
    {
        using var ms = new MemoryStream();
        var writer = new iText.Kernel.Pdf.PdfWriter(ms);
        var pdf = new iText.Kernel.Pdf.PdfDocument(writer);
        var doc = new iText.Layout.Document(pdf);
        doc.Add(new iText.Layout.Element.Paragraph(text).SetFontSize(12));
        doc.Close();
        return ms.ToArray();
    }
}
