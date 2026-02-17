using PDFEditor.Core.Abstractions;
using PDFEditor.Core.Services.Export;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for ExportProviderRegistry and individual export providers
/// </summary>
public class ExportProviderRegistryTests
{
    [Fact]
    public void CreateDefault_RegistersAllProviders()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        Assert.True(registry.Providers.Count >= 4);
    }

    [Fact]
    public void GetProviderByName_Image_ReturnsProvider()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        var provider = registry.GetProviderByName("Image (PNG, JPEG, TIFF, BMP, WebP)");
        Assert.NotNull(provider);
        Assert.Equal("Image (PNG, JPEG, TIFF, BMP, WebP)", provider!.FormatName);
    }

    [Fact]
    public void GetProviderByName_Text_ReturnsProvider()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        var provider = registry.GetProviderByName("Plain Text (TXT)");
        Assert.NotNull(provider);
    }

    [Fact]
    public void GetProviderByName_Html_ReturnsProvider()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        var provider = registry.GetProviderByName("HTML Document");
        Assert.NotNull(provider);
    }

    [Fact]
    public void GetProviderByName_Docx_ReturnsProvider()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        var provider = registry.GetProviderByName("Microsoft Word (DOCX)");
        Assert.NotNull(provider);
    }

    [Fact]
    public void GetProvidersByExtension_PNG_ReturnsImageProvider()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        var providers = registry.GetProvidersByExtension(".png").ToList();
        Assert.NotEmpty(providers);
        Assert.Equal("Image (PNG, JPEG, TIFF, BMP, WebP)", providers[0].FormatName);
    }

    [Fact]
    public void GetProvidersByExtension_TXT_ReturnsTextProvider()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        var providers = registry.GetProvidersByExtension(".txt").ToList();
        Assert.NotEmpty(providers);
    }

    [Fact]
    public void GetProvidersByExtension_HTML_ReturnsHtmlProvider()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        var providers = registry.GetProvidersByExtension(".html").ToList();
        Assert.NotEmpty(providers);
    }

    [Fact]
    public void GetProvidersByExtension_DOCX_ReturnsDocxProvider()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        var providers = registry.GetProvidersByExtension(".docx").ToList();
        Assert.NotEmpty(providers);
    }

    [Fact]
    public void GetProviderByName_NonExistent_ReturnsNull()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        var provider = registry.GetProviderByName("SVG");
        Assert.Null(provider);
    }

    [Fact]
    public void GetProvidersByExtension_NonExistent_ReturnsEmpty()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        var providers = registry.GetProvidersByExtension(".xyz").ToList();
        Assert.Empty(providers);
    }

    [Fact]
    public void Register_CustomProvider_AddedSuccessfully()
    {
        var registry = ExportProviderRegistry.CreateDefault();
        int initialCount = registry.Providers.Count;
        registry.Register(new DummyExportProvider());
        Assert.Equal(initialCount + 1, registry.Providers.Count);
    }

    // ------ Export Provider Integration Tests ------

    [Fact]
    public async Task ImageExportProvider_ExportAsync_ReturnsData()
    {
        var provider = new ImageExportProvider();
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var options = new ExportOptions { Dpi = 72, OutputFormat = "PNG", BaseFileName = "test" };
        var result = await provider.ExportAsync(pdf, options);
        Assert.True(result.Success);
        Assert.True(result.Data.Length > 0);
    }

    [Fact]
    public async Task ImageExportProvider_ExportPagesAsync_ReturnsPerPage()
    {
        var provider = new ImageExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(2);
        var options = new ExportOptions { Dpi = 72, OutputFormat = "PNG", BaseFileName = "test" };
        var results = await provider.ExportPagesAsync(pdf, options);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task TextExportProvider_ExportAsync_ReturnsText()
    {
        var provider = new TextExportProvider();
        var pdf = TestPdfGenerator.CreatePdfWithContent("Hello text export test");
        var options = new ExportOptions { BaseFileName = "test" };
        var result = await provider.ExportAsync(pdf, options);
        Assert.True(result.Success);
        var text = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("Hello text export test", text);
    }

    [Fact]
    public async Task HtmlExportProvider_ExportAsync_ReturnsHtml()
    {
        var provider = new HtmlExportProvider();
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var options = new ExportOptions { Dpi = 72, BaseFileName = "test" };
        var result = await provider.ExportAsync(pdf, options);
        Assert.True(result.Success);
        var html = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("<!DOCTYPE html>", html);
    }

    [Fact]
    public async Task DocxExportProvider_ExportAsync_ReturnsData()
    {
        var provider = new DocxExportProvider();
        var pdf = TestPdfGenerator.CreatePdfWithContent("DOCX export test content");
        var options = new ExportOptions { BaseFileName = "test" };
        var result = await provider.ExportAsync(pdf, options);
        Assert.True(result.Success);
        Assert.True(result.Data.Length > 0);
        Assert.Equal("test.docx", result.FileName);
    }

    /// <summary>
    /// Dummy provider for testing registration
    /// </summary>
    private class DummyExportProvider : IExportProvider
    {
        public string FormatName => "Dummy";
        public string[] SupportedExtensions => new[] { ".dummy" };
        public bool SupportsBatch => false;
        public bool SupportsPerPageExport => false;

        public Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ExportResult.Ok(Array.Empty<byte>(), "test.dummy", "application/x-dummy"));

        public Task<List<ExportResult>> ExportPagesAsync(byte[] pdfBytes, ExportOptions options,
            IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ExportResult>());
    }
}
