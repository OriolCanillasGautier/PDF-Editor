using Xunit;
using PDFEditor.Core.Services.Export;
using PDFEditor.Core.Abstractions;
using PDFEditor.Tests.Helpers;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for LatexExportProvider and OdtExportProvider.
/// </summary>
public class LatexOdtExportTests
{
    // ── LaTeX Export ──

    [Fact]
    public void LatexProvider_FormatName_IsLatex()
    {
        var provider = new LatexExportProvider();
        Assert.Contains("LaTeX", provider.FormatName);
    }

    [Fact]
    public void LatexProvider_SupportedExtensions_ContainsTex()
    {
        var provider = new LatexExportProvider();
        Assert.Contains(".tex", provider.SupportedExtensions);
    }

    [Fact]
    public async Task LatexProvider_Export_ProducesTexContent()
    {
        var provider = new LatexExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(2, "Hello LaTeX World");
        var options = new ExportOptions { BaseFileName = "test" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 0);

        var tex = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("\\documentclass", tex);
        Assert.Contains("\\begin{document}", tex);
        Assert.Contains("\\end{document}", tex);
    }

    [Fact]
    public async Task LatexProvider_Export_ContainsPageContent()
    {
        var provider = new LatexExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(1, "Unique test content xyz");
        var options = new ExportOptions { BaseFileName = "test" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success);
        var tex = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("Unique test content xyz", tex);
    }

    [Fact]
    public async Task LatexProvider_Export_EscapesSpecialChars()
    {
        var provider = new LatexExportProvider();
        // The PDF will contain these chars in text; the LaTeX should escape them
        var pdf = TestPdfGenerator.CreateSimplePdf(1, "Price is $100 & tax 10%");
        var options = new ExportOptions { BaseFileName = "test" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success);
        var tex = System.Text.Encoding.UTF8.GetString(result.Data);
        // $ and & and % should be escaped
        Assert.Contains("\\$", tex);
        Assert.Contains("\\&", tex);
        Assert.Contains("\\%", tex);
    }

    [Fact]
    public async Task LatexProvider_Export_WithPageSelection()
    {
        var provider = new LatexExportProvider();
        var pdf = TestPdfGenerator.CreatePdfWithContent("Page ONE", "Page TWO", "Page THREE");
        var options = new ExportOptions
        {
            BaseFileName = "test",
            PageIndices = new[] { 0 }
        };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success);
        var tex = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("Page ONE", tex);
        Assert.DoesNotContain("Page TWO", tex);
    }

    [Fact]
    public async Task LatexProvider_Export_FileName_HasTexExtension()
    {
        var provider = new LatexExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var options = new ExportOptions { BaseFileName = "myfile" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success);
        Assert.Equal("myfile.tex", result.FileName);
    }

    // ── ODT Export ──

    [Fact]
    public void OdtProvider_FormatName_IsODT()
    {
        var provider = new OdtExportProvider();
        Assert.Contains("OpenDocument", provider.FormatName);
    }

    [Fact]
    public void OdtProvider_SupportedExtensions_ContainsOdt()
    {
        var provider = new OdtExportProvider();
        Assert.Contains(".odt", provider.SupportedExtensions);
    }

    [Fact]
    public async Task OdtProvider_Export_ProducesZipBytes()
    {
        var provider = new OdtExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(2, "ODT export test");
        var options = new ExportOptions { BaseFileName = "test" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 100);

        // ZIP/ODF magic bytes: PK (0x50 0x4B)
        Assert.Equal(0x50, result.Data[0]);
        Assert.Equal(0x4B, result.Data[1]);
    }

    [Fact]
    public async Task OdtProvider_Export_ContainsRequiredOdfFiles()
    {
        var provider = new OdtExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var options = new ExportOptions { BaseFileName = "test" };

        var result = await provider.ExportAsync(pdf, options);
        Assert.True(result.Success);

        // Open as ZIP and check for required entries
        using var ms = new MemoryStream(result.Data);
        using var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        var entryNames = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("mimetype", entryNames);
        Assert.Contains("content.xml", entryNames);
        Assert.Contains("META-INF/manifest.xml", entryNames);
    }

    [Fact]
    public async Task OdtProvider_Export_MimetypeIsCorrect()
    {
        var provider = new OdtExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var options = new ExportOptions { BaseFileName = "test" };

        var result = await provider.ExportAsync(pdf, options);
        Assert.True(result.Success);

        using var ms = new MemoryStream(result.Data);
        using var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        var mimetypeEntry = archive.GetEntry("mimetype");
        Assert.NotNull(mimetypeEntry);

        using var reader = new StreamReader(mimetypeEntry!.Open());
        var mimetype = reader.ReadToEnd();
        Assert.Equal("application/vnd.oasis.opendocument.text", mimetype);
    }

    [Fact]
    public async Task OdtProvider_Export_WithPageSelection()
    {
        var provider = new OdtExportProvider();
        var pdf = TestPdfGenerator.CreatePdfWithContent("Alpha content", "Beta content", "Gamma content");
        var options = new ExportOptions
        {
            BaseFileName = "filtered",
            PageIndices = new[] { 1 }
        };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task OdtProvider_Export_FileName_HasOdtExtension()
    {
        var provider = new OdtExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var options = new ExportOptions { BaseFileName = "document" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success);
        Assert.Equal("document.odt", result.FileName);
    }

    // ── Registry ──

    [Fact]
    public void ExportRegistry_ContainsLatexAndOdt()
    {
        var registry = ExportProviderRegistry.CreateDefault();

        // Check providers by extension (more reliable than format name)
        Assert.NotEmpty(registry.GetProvidersByExtension(".tex"));
        Assert.NotEmpty(registry.GetProvidersByExtension(".odt"));
    }
}
