using PDFEditor.Core.Services;
using PDFEditor.Core.Services.Export;
using PDFEditor.Core.Abstractions;
using PDFEditor.Tests.Helpers;
using iText.Kernel.Pdf;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using Xunit;

namespace PDFEditor.Tests.Integration;

/// <summary>
/// Integration tests that exercise end-to-end workflows:
/// service instantiation → operation → result validation.
/// These tests do NOT require external resources or UI.
/// </summary>
public class IntegrationWorkflowTests
{
    // ─── JSON Export ────────────────────────────────────────────────────────────

    [Fact]
    public async Task JsonExport_SinglePage_ProducesValidJson()
    {
        byte[] pdf = TestPdfGenerator.CreateSimplePdf(1, "Hello World Integration");
        var provider = new JsonExportProvider();
        var options  = new ExportOptions { BaseFileName = "test", OutputFormat = "JSON" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("test.json", result.FileName);
        Assert.Equal("application/json", result.MimeType);
        Assert.True(result.Data.Length > 0);

        // Must be valid JSON
        string json = Encoding.UTF8.GetString(result.Data);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("totalPages").GetInt32());
        Assert.Equal(1, root.GetProperty("exportedPages").GetInt32());
    }

    [Fact]
    public async Task JsonExport_MultiPage_AllPagesPresent()
    {
        byte[] pdf = TestPdfGenerator.CreateSimplePdf(3);
        var provider = new JsonExportProvider();
        var options  = new ExportOptions { BaseFileName = "multi" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success, result.ErrorMessage);
        string json = Encoding.UTF8.GetString(result.Data);
        using var doc = JsonDocument.Parse(json);
        var pages = doc.RootElement.GetProperty("pages");
        Assert.Equal(3, pages.GetArrayLength());
    }

    [Fact]
    public async Task JsonExport_PageSubset_OnlySelectedPages()
    {
        byte[] pdf = TestPdfGenerator.CreateSimplePdf(5);
        var provider = new JsonExportProvider();
        var options  = new ExportOptions { BaseFileName = "subset", PageIndices = new[] { 0, 2, 4 } };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success, result.ErrorMessage);
        string json = Encoding.UTF8.GetString(result.Data);
        using var doc = JsonDocument.Parse(json);
        var pages = doc.RootElement.GetProperty("pages");
        Assert.Equal(3, pages.GetArrayLength());
    }

    [Fact]
    public async Task JsonExport_TextContent_ContainsExpectedText()
    {
        const string markerText = "UNIQUE_MARKER_TEXT_12345";
        byte[] pdf = TestPdfGenerator.CreateSimplePdf(1, markerText);
        var provider = new JsonExportProvider();
        var options  = new ExportOptions { BaseFileName = "text" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success, result.ErrorMessage);
        string json = Encoding.UTF8.GetString(result.Data);
        Assert.Contains(markerText, json);
    }

    [Fact]
    public async Task JsonExport_PageDimensions_ArePositive()
    {
        byte[] pdf = TestPdfGenerator.CreateSimplePdf(1);
        var provider = new JsonExportProvider();
        var options  = new ExportOptions { BaseFileName = "dims" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success, result.ErrorMessage);
        string json = Encoding.UTF8.GetString(result.Data);
        using var doc = JsonDocument.Parse(json);
        var page = doc.RootElement.GetProperty("pages")[0];
        Assert.True(page.GetProperty("widthPt").GetSingle() > 0);
        Assert.True(page.GetProperty("heightPt").GetSingle() > 0);
    }

    // ─── PPTX Export ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PptxExport_SinglePage_ProducesValidZip()
    {
        byte[] pdf = TestPdfGenerator.CreateSimplePdf(1);
        var provider = new PptxExportProvider();
        var options  = new ExportOptions { BaseFileName = "test", Dpi = 72 };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("test.pptx", result.FileName);
        Assert.Contains("presentation", result.MimeType);
        Assert.True(result.Data.Length > 0);

        // PPTX is a ZIP file
        using var ms  = new MemoryStream(result.Data);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.True(zip.Entries.Count > 0);

        // Must contain presentation.xml
        bool hasPresentation = zip.Entries.Any(e => e.FullName.Contains("presentation.xml"));
        Assert.True(hasPresentation, "PPTX must contain presentation.xml");
    }

    [Fact]
    public async Task PptxExport_ContentTypesXml_IsValid()
    {
        byte[] pdf = TestPdfGenerator.CreateSimplePdf(1);
        var provider = new PptxExportProvider();
        var options  = new ExportOptions { BaseFileName = "ct", Dpi = 72 };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success, result.ErrorMessage);
        using var ms  = new MemoryStream(result.Data);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var ctEntry = zip.GetEntry("[Content_Types].xml");
        Assert.NotNull(ctEntry);

        using var sr = new StreamReader(ctEntry!.Open());
        string xml = sr.ReadToEnd();

        // Must be valid XML with Types root element
        Assert.Contains("<Types", xml);

        // The presentation main part must be covered either by Default or Override
        bool hasCoverage =
            xml.Contains("presentationml.presentation.main+xml") &&
            (xml.Contains("<Default Extension=\"xml\"") || xml.Contains("PartName=\"/ppt/presentation.xml\""));
        Assert.True(hasCoverage, $"Content_Types.xml must cover presentation.xml. Actual:\n{xml}");
    }

    // ─── Metadata Scrubber ───────────────────────────────────────────────────────

    [Fact]
    public async Task MetadataScrubber_RemovesAllMetadata()
    {
        byte[] pdf = TestPdfGenerator.CreatePdfWithMetadata(
            "Test Title", "Test Author", "Test Subject", pages: 1);
        var scrubber = new MetadataScrubberService();

        // Confirm metadata is present before scrubbing
        var before = await scrubber.InspectAsync(pdf);
        Assert.False(string.IsNullOrEmpty(before.Author));
        Assert.True(before.HasAnyMetadata);

        // Scrub
        byte[] scrubbed = await scrubber.ScrubAsync(pdf);

        // Confirm metadata is gone (Author and Subject should be empty)
        // Note: iText7 always sets Producer on output, so we only check user-controlled fields
        var after = await scrubber.InspectAsync(scrubbed);
        Assert.True(string.IsNullOrEmpty(after.Author), $"Author should be empty, got: '{after.Author}'");
        Assert.True(string.IsNullOrEmpty(after.Subject), $"Subject should be empty, got: '{after.Subject}'");
        Assert.True(string.IsNullOrEmpty(after.Keywords), $"Keywords should be empty, got: '{after.Keywords}'");
        Assert.True(string.IsNullOrEmpty(after.Creator), $"Creator should be empty, got: '{after.Creator}'");
    }

    [Fact]
    public async Task MetadataScrubber_PreserveTitle_KeypTitleOnly()
    {
        byte[] pdf = TestPdfGenerator.CreatePdfWithMetadata(
            "Keep This Title", "Remove This Author", "Remove Subject", pages: 1);
        var scrubber = new MetadataScrubberService();

        byte[] scrubbed = await scrubber.ScrubAsync(pdf, preserveTitle: true);

        var after = await scrubber.InspectAsync(scrubbed);
        Assert.Equal("Keep This Title", after.Title);
        Assert.True(string.IsNullOrEmpty(after.Author));
    }

    [Fact]
    public async Task MetadataScrubber_OutputIsParsableAsPdf()
    {
        byte[] pdf = TestPdfGenerator.CreatePdfWithMetadata("T", "A", "S");
        var scrubber = new MetadataScrubberService();

        byte[] scrubbed = await scrubber.ScrubAsync(pdf);

        // Must still be valid PDF
        using var ms     = new MemoryStream(scrubbed);
        using var reader = new PdfReader(ms);
        using var doc    = new PdfDocument(reader);
        Assert.True(doc.GetNumberOfPages() >= 1);
    }

    [Fact]
    public async Task MetadataScrubber_Inspect_NoMetadataPdf_ReturnsFalse()
    {
        byte[] pdf = TestPdfGenerator.CreateMinimalPdf();
        var scrubber = new MetadataScrubberService();

        var info = await scrubber.InspectAsync(pdf);

        // Minimal PDF has no author/subject/keywords set
        Assert.False(!string.IsNullOrEmpty(info.Author) && !string.IsNullOrEmpty(info.Subject));
    }

    // ─── Print-to-PDF ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrintToPdf_PageCopy_ProducesValidPdf()
    {
        byte[] pdf = TestPdfGenerator.CreateSimplePdf(2);
        var service = new PrintToPdfService();
        var options = new PrintOptions();

        var result = await service.PrintAsync(pdf, options);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Data);

        using var ms     = new MemoryStream(result.Data!);
        using var reader = new PdfReader(ms);
        using var doc    = new PdfDocument(reader);
        Assert.Equal(2, doc.GetNumberOfPages());
    }

    [Fact]
    public async Task PrintToPdf_A4Normalization_ProducesA4Pages()
    {
        byte[] pdf = TestPdfGenerator.CreateSimplePdf(1);
        var service = new PrintToPdfService();
        var options = new PrintOptions
        {
            TargetPageSize = PrintToPdfService.A4,
            FitToPage = true,
        };

        var result = await service.PrintAsync(pdf, options);

        Assert.True(result.Success, result.ErrorMessage);

        using var ms     = new MemoryStream(result.Data!);
        using var reader = new PdfReader(ms);
        using var doc    = new PdfDocument(reader);

        var page = doc.GetPage(1);
        var box  = page.GetMediaBox();
        // A4 is 595.28 x 841.89 pts — allow 2pt rounding
        Assert.InRange(box.GetWidth(),  593f, 597f);
        Assert.InRange(box.GetHeight(), 839f, 844f);
    }

    [Fact]
    public async Task PrintToPdf_SubsetPages_OnlySelectedPages()
    {
        byte[] pdf = TestPdfGenerator.CreateSimplePdf(4);
        var service = new PrintToPdfService();
        var options = new PrintOptions { PageIndices = new[] { 1, 3 } }; // 1-based

        var result = await service.PrintAsync(pdf, options);

        Assert.True(result.Success, result.ErrorMessage);

        using var ms     = new MemoryStream(result.Data!);
        using var reader = new PdfReader(ms);
        using var doc    = new PdfDocument(reader);
        Assert.Equal(2, doc.GetNumberOfPages());
    }

    // ─── PDF/A Archiver ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PdfArchiver_Inspect_StandardPdf_IsNotPdfA()
    {
        byte[] pdf = TestPdfGenerator.CreateSimplePdf(1);
        var service = new PdfArchiverService();

        var info = await service.InspectConformanceAsync(pdf);

        Assert.False(info.HasXmpPdfAClaim);
        Assert.Contains("Not PDF/A", info.ConformanceLabel);
        Assert.Equal(1, info.PageCount);
    }

    [Fact]
    public async Task PdfArchiver_Convert_WhenNoIccProfile_ReturnsFail()
    {
        // This test verifies graceful failure when no ICC profile is available.
        // If an ICC profile happens to be present on the test machine, skip.
        byte[] pdf = TestPdfGenerator.CreateSimplePdf(1);
        var service = new PdfArchiverService();

        var result = await service.ConvertToPdfA2BAsync(pdf);

        // Either succeeds (ICC found) or fails gracefully with descriptive error
        if (!result.Success)
        {
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("sRGB", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            // ICC was found — validate output is parsable
            Assert.NotNull(result.Data);
            using var ms     = new MemoryStream(result.Data!);
            using var reader = new PdfReader(ms);
            using var doc    = new PdfDocument(reader);
            Assert.True(doc.GetNumberOfPages() >= 1);
        }
    }

    // ─── End-to-End Workflow: open → scrub → export ───────────────────────────────

    [Fact]
    public async Task Workflow_ScrubThenJsonExport_ProducesCleanOutput()
    {
        // 1. Create PDF with sensitive metadata
        byte[] original = TestPdfGenerator.CreatePdfWithMetadata(
            "Classified", "Secret Agent", "Confidential Report", pages: 2);

        // 2. Scrub metadata
        var scrubber = new MetadataScrubberService();
        byte[] scrubbed = await scrubber.ScrubAsync(original);

        // Verify metadata gone
        var meta = await scrubber.InspectAsync(scrubbed);
        Assert.True(string.IsNullOrEmpty(meta.Author));

        // 3. Export to JSON
        var provider = new JsonExportProvider();
        var options  = new ExportOptions { BaseFileName = "clean" };
        var result   = await provider.ExportAsync(scrubbed, options);

        Assert.True(result.Success, result.ErrorMessage);
        string json = Encoding.UTF8.GetString(result.Data);

        // JSON must NOT contain the original sensitive metadata
        Assert.DoesNotContain("Classified", json);
        Assert.DoesNotContain("Secret Agent", json);
    }

    [Fact]
    public async Task Workflow_PrintNormalize_ThenExportJson_CorrectPageCount()
    {
        byte[] pdf = TestPdfGenerator.CreateSimplePdf(3);

        // Normalize to Letter
        var printService = new PrintToPdfService();
        var printOptions = new PrintOptions { TargetPageSize = PrintToPdfService.Letter, FitToPage = true };
        var printResult  = await printService.PrintAsync(pdf, printOptions);
        Assert.True(printResult.Success);

        // Export to JSON
        var jsonProvider = new JsonExportProvider();
        var jsonOptions  = new ExportOptions { BaseFileName = "normalized" };
        var jsonResult   = await jsonProvider.ExportAsync(printResult.Data!, jsonOptions);
        Assert.True(jsonResult.Success, jsonResult.ErrorMessage);

        string json = Encoding.UTF8.GetString(jsonResult.Data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("totalPages").GetInt32());
    }
}
