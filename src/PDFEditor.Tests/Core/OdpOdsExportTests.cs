using PDFEditor.Core.Services.Export;
using PDFEditor.Core.Abstractions;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for OdpExportProvider, OdsExportProvider, ExportProviderRegistry (15 providers)
/// </summary>
public class OdpOdsExportTests
{
    // ===== ODP Export Tests =====

    [Fact]
    public async Task Odp_ExportAsync_ReturnsValidResult()
    {
        var provider = new OdpExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(2);
        var options = new ExportOptions { Dpi = 72, BaseFileName = "test" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.Data.Length > 0);
        Assert.EndsWith(".odp", result.FileName);
    }

    [Fact]
    public void Odp_FormatName_IsCorrect()
    {
        var provider = new OdpExportProvider();

        Assert.Equal("OpenDocument Presentation (ODP)", provider.FormatName);
        Assert.Contains(".odp", provider.SupportedExtensions);
    }

    [Fact]
    public async Task Odp_ExportAsync_SinglePage()
    {
        var provider = new OdpExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var options = new ExportOptions { Dpi = 72, BaseFileName = "single" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Odp_ExportAsync_SpecificPages()
    {
        var provider = new OdpExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(5);
        var options = new ExportOptions
        {
            Dpi = 72,
            BaseFileName = "specific",
            PageIndices = new[] { 0, 2, 4 }
        };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Odp_ExportAsync_Cancellation()
    {
        var provider = new OdpExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var options = new ExportOptions { Dpi = 72, BaseFileName = "cancel" };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ExportAsync(pdf, options, cts.Token));
    }

    // ===== ODS Export Tests =====

    [Fact]
    public async Task Ods_ExportAsync_ReturnsValidResult()
    {
        var provider = new OdsExportProvider();
        var pdf = TestPdfGenerator.CreatePdfWithContent(
            "Name    Age    City\nAlice   30     NYC\nBob     25     LA");
        var options = new ExportOptions { BaseFileName = "test" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.Data.Length > 0);
        Assert.EndsWith(".ods", result.FileName);
    }

    [Fact]
    public void Ods_FormatName_IsCorrect()
    {
        var provider = new OdsExportProvider();

        Assert.Equal("OpenDocument Spreadsheet (ODS)", provider.FormatName);
        Assert.Contains(".ods", provider.SupportedExtensions);
    }

    [Fact]
    public async Task Ods_ExportAsync_MultiPage()
    {
        var provider = new OdsExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var options = new ExportOptions { BaseFileName = "multi" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Ods_ExportAsync_EmptyPage()
    {
        var provider = new OdsExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(1, " ");
        var options = new ExportOptions { BaseFileName = "empty" };

        var result = await provider.ExportAsync(pdf, options);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Ods_ExportAsync_Cancellation()
    {
        var provider = new OdsExportProvider();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var options = new ExportOptions { BaseFileName = "cancel" };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ExportAsync(pdf, options, cts.Token));
    }

    // ===== Registry Tests =====

    [Fact]
    public void Registry_CreateDefault_Has15Providers()
    {
        var registry = ExportProviderRegistry.CreateDefault();

        Assert.Equal(15, registry.Providers.Count);
    }

    [Fact]
    public void Registry_FindsOdp()
    {
        var registry = ExportProviderRegistry.CreateDefault();

        var providers = registry.GetProvidersByExtension(".odp");

        Assert.Single(providers);
    }

    [Fact]
    public void Registry_FindsOds()
    {
        var registry = ExportProviderRegistry.CreateDefault();

        var providers = registry.GetProvidersByExtension(".ods");

        Assert.Single(providers);
    }
}
