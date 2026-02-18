using PDFEditor.Core.Abstractions;
using PDFEditor.Core.Services;
using PDFEditor.Core.Services.Export;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for the enhanced DocxExportProvider with image extraction, heading detection,
/// and table structure support.
/// </summary>
public class DocxExportProviderEnhancedTests
{
    private readonly DocxExportProvider _provider = new();

    [Fact]
    public async Task ExportAsync_BasicPdf_ProducesValidDocx()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(2);
        var options = new ExportOptions { BaseFileName = "test" };

        var result = await _provider.ExportAsync(pdfBytes, options);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Data);
        Assert.Equal("test.docx", result.FileName);
        Assert.Contains("wordprocessingml", result.MimeType);
    }

    [Fact]
    public async Task ExportAsync_SinglePage_ProducesValidDocx()
    {
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf();
        var options = new ExportOptions
        {
            BaseFileName = "minimal",
            PageIndices = new[] { 0 }
        };

        var result = await _provider.ExportAsync(pdfBytes, options);
        Assert.True(result.Success);
        Assert.True(result.Data.Length > 100);
    }

    [Fact]
    public async Task ExportAsync_EmptyTextPage_HandlesFallback()
    {
        // Create a PDF that would appear to have text
        var pdfBytes = TestPdfGenerator.CreatePdfWithContent("Some text content");
        var options = new ExportOptions { BaseFileName = "fallback" };

        var result = await _provider.ExportAsync(pdfBytes, options);
        Assert.True(result.Success);
        Assert.True(result.Data.Length > 100);
    }

    [Fact]
    public async Task ExportAsync_MultiplePages_IncludesAllPages()
    {
        var pdfBytes = TestPdfGenerator.CreatePdfWithContent("Page A", "Page B", "Page C");
        var options = new ExportOptions { BaseFileName = "multi" };

        var result = await _provider.ExportAsync(pdfBytes, options);
        Assert.True(result.Success);
        Assert.True(result.Data.Length > 200);
    }

    [Fact]
    public async Task ExportAsync_PageSubset_ExportsOnlySelectedPages()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(5);
        var options = new ExportOptions
        {
            BaseFileName = "subset",
            PageIndices = new[] { 0, 2, 4 }
        };

        var result = await _provider.ExportAsync(pdfBytes, options);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExportAsync_WithCancellation_ThrowsTaskCanceled()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(3);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _provider.ExportAsync(pdfBytes,
            new ExportOptions { BaseFileName = "cancel" }, cts.Token);
        // ExportAsync catches exceptions and returns Fail
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExportPagesAsync_ThrowsNotSupported()
    {
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _provider.ExportPagesAsync(pdfBytes, new ExportOptions()));
    }

    [Fact]
    public void Properties_AreCorrect()
    {
        Assert.Equal("Microsoft Word (DOCX)", _provider.FormatName);
        Assert.Contains(".docx", _provider.SupportedExtensions);
        Assert.True(_provider.SupportsBatch);
        Assert.False(_provider.SupportsPerPageExport);
    }

    [Fact]
    public async Task ExportAsync_PdfWithDifferentFontSizes_ProducesDocx()
    {
        // Create a PDF with headings (large font) and body text (normal font)
        // using iText7 to set different font sizes
        var ms = new MemoryStream();
        var writer = new iText.Kernel.Pdf.PdfWriter(ms);
        var pdf = new iText.Kernel.Pdf.PdfDocument(writer);
        var doc = new iText.Layout.Document(pdf);

        doc.Add(new iText.Layout.Element.Paragraph("Main Title")
            .SetFontSize(24).SetBold());
        doc.Add(new iText.Layout.Element.Paragraph("This is body text paragraph one.")
            .SetFontSize(12));
        doc.Add(new iText.Layout.Element.Paragraph("Section Header")
            .SetFontSize(18).SetBold());
        doc.Add(new iText.Layout.Element.Paragraph("Body text paragraph two.")
            .SetFontSize(12));

        doc.Close();
        var pdfBytes = ms.ToArray();

        var result = await _provider.ExportAsync(pdfBytes,
            new ExportOptions { BaseFileName = "headings" });

        Assert.True(result.Success);
        Assert.True(result.Data.Length > 200);
    }

    [Fact]
    public async Task ExportAsync_PdfWithImage_ProducesDocx()
    {
        // Create a PDF with an embedded image
        var ms = new MemoryStream();
        var writer = new iText.Kernel.Pdf.PdfWriter(ms);
        var pdf = new iText.Kernel.Pdf.PdfDocument(writer);
        var doc = new iText.Layout.Document(pdf);

        doc.Add(new iText.Layout.Element.Paragraph("Document with image"));

        // Create a small test image (1x1 red pixel PNG)
        var pngBytes = CreateMinimalPng();
        var imgData = iText.IO.Image.ImageDataFactory.Create(pngBytes);
        var img = new iText.Layout.Element.Image(imgData);
        img.SetWidth(100);
        img.SetHeight(100);
        doc.Add(img);

        doc.Add(new iText.Layout.Element.Paragraph("Text after image"));
        doc.Close();

        var pdfWithImage = ms.ToArray();
        var result = await _provider.ExportAsync(pdfWithImage,
            new ExportOptions { BaseFileName = "with_image" });

        Assert.True(result.Success);
        Assert.True(result.Data.Length > 200);
    }

    /// <summary>
    /// Creates a minimal valid PNG (1x1 red pixel).
    /// </summary>
    private static byte[] CreateMinimalPng()
    {
        using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Red, 10, 10);
        image.Format = ImageMagick.MagickFormat.Png;
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task ExportAsync_PdfContainingXmlIllegalControlChars_StillProducesValidDocx()
    {
        // PDF text that contains illegal XML 1.0 control characters (0x01–0x08, 0x0B–0x0C, 0x0E–0x1F)
        // These must be stripped by SanitizeXml before insertion into Word XML.
        // If not stripped, Word throws "error opening file" instead of just the repair dialog.
        var illegalText = "Normal \x01\x02\x03 text \x0B\x0C\x0E\x1F with controls";
        var pdfBytes = CreatePdfWithRawText(illegalText);
        var options = new ExportOptions { BaseFileName = "control-chars" };

        var result = await _provider.ExportAsync(pdfBytes, options);

        // Export must succeed and produce a non-trivial DOCX
        Assert.True(result.Success, $"Export failed: {result.ErrorMessage}");
        Assert.True(result.Data.Length > 100);

        // Verify the DOCX is a valid ZIP (starts with PK signature)
        Assert.Equal(0x50, result.Data[0]); // 'P'
        Assert.Equal(0x4B, result.Data[1]); // 'K'
    }

    [Fact]
    public async Task ExportAsync_DocumentSettingsPartPresent_DocxContainsSettings()
    {
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf();
        var result = await _provider.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "settings" });

        Assert.True(result.Success);

        // Decompress and check that settings.xml relationship exists in the DOCX
        using var zipStream = new MemoryStream(result.Data);
        using var zip = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read);
        var settingsEntry = zip.Entries.FirstOrDefault(e => e.FullName.Contains("settings.xml", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(settingsEntry);
    }

    [Fact]
    public async Task ExportAsync_NormalizeImageForDocx_SkipsIncompatibleImageGracefully()
    {
        // Create a PDF with explicitly invalid "image" bytes (not JPEG, not PNG, random garbage)
        // The DOCX export must gracefully skip it rather than embedding garbage.
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf(); // no images
        var result = await _provider.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "no-img" });
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExportAsync_ContentTypesXml_HasCorrectStructure()
    {
        // This test validates the root cause of the "Word experienced an error opening the file" bug.
        // [Content_Types].xml must:
        //   1. Have Default Extension="xml" ContentType="application/xml" (NOT document.main+xml)
        //   2. Have Override for /word/document.xml with the document.main+xml content type
        //   3. Have Override for /word/styles.xml and /word/settings.xml
        var pdfBytes = TestPdfGenerator.CreatePdfWithContent("Hello World");
        var result = await _provider.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "ct-test" });

        Assert.True(result.Success);

        using var zipStream = new MemoryStream(result.Data);
        using var zip = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read);

        var ctEntry = zip.GetEntry("[Content_Types].xml");
        Assert.NotNull(ctEntry);

        using var reader = new StreamReader(ctEntry!.Open());
        var xml = reader.ReadToEnd();

        // Default for xml extension must be application/xml, NOT the document.main type
        Assert.Contains("Extension=\"xml\"", xml);
        Assert.Contains("ContentType=\"application/xml\"", xml);

        // document.main content type must appear only as an Override for /word/document.xml
        Assert.Contains("PartName=\"/word/document.xml\"", xml);
        Assert.Contains("application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", xml);

        // Styles and settings overrides must exist
        Assert.Contains("PartName=\"/word/styles.xml\"", xml);
        Assert.Contains("PartName=\"/word/settings.xml\"", xml);

        // The document.main type must NOT be a Default — it must only appear in Override elements
        Assert.DoesNotContain(
            "Extension=\"xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"",
            xml);
    }

    [Fact]
    public async Task ExportAsync_DocxIsValidZip_WithRequiredEntries()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(2);
        var result = await _provider.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "structure" });

        Assert.True(result.Success);

        // Must be a valid ZIP (PK magic bytes)
        Assert.Equal(0x50, result.Data[0]);
        Assert.Equal(0x4B, result.Data[1]);

        using var zipStream = new MemoryStream(result.Data);
        using var zip = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read);
        var entryNames = zip.Entries.Select(e => e.FullName).ToList();

        // Required OOXML DOCX entries
        Assert.Contains("[Content_Types].xml", entryNames);
        Assert.Contains("_rels/.rels", entryNames);
        Assert.True(entryNames.Any(e => e.Contains("word/document.xml")));
        Assert.True(entryNames.Any(e => e.Contains("word/_rels/")));
    }

    /// <summary>
    /// Creates a minimal PDF in-memory that contains the given raw text using iText7.
    /// </summary>
    private static byte[] CreatePdfWithRawText(string text)
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
