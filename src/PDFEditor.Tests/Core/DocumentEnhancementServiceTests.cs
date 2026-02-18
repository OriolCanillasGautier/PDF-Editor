using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for DocumentSanitizerService, AutoTagService, AltTextEditorService, FontReplacementService
/// </summary>
public class DocumentEnhancementServiceTests
{
    // ===== DocumentSanitizerService Tests =====

    [Fact]
    public void Sanitizer_Inspect_ReturnsReport()
    {
        var service = new DocumentSanitizerService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var report = service.Inspect(pdf);

        Assert.NotNull(report);
        Assert.Equal(0, report.JavaScriptActionsRemoved);
        Assert.Equal(0, report.EmbeddedFilesRemoved);
    }

    [Fact]
    public void Sanitizer_Sanitize_ReturnsByteArray()
    {
        var service = new DocumentSanitizerService();
        var pdf = TestPdfGenerator.CreateSimplePdf(2);
        var options = new SanitizationOptions
        {
            RemoveJavaScript = true,
            RemoveEmbeddedFiles = true,
            ScrubMetadata = true
        };

        var (result, report) = service.Sanitize(pdf, options);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        Assert.NotNull(report);
    }

    [Fact]
    public void Sanitizer_Sanitize_DefaultOptions()
    {
        var service = new DocumentSanitizerService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var (result, _) = service.Sanitize(pdf);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    // ===== AutoTagService Tests =====

    [Fact]
    public void AutoTag_IsTagged_ReturnsBool()
    {
        var service = new AutoTagService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var result = service.IsTagged(pdf);

        // Simple test PDF is not tagged
        Assert.False(result);
    }

    [Fact]
    public void AutoTag_AutoTag_ReturnsResult()
    {
        var service = new AutoTagService();
        var pdf = TestPdfGenerator.CreateSimplePdf(2);

        var result = service.AutoTag(pdf);

        Assert.NotNull(result);
        Assert.True(result.TaggedPdf.Length > 0);
    }

    [Fact]
    public void AutoTag_GetTagTree_ReturnsString()
    {
        var service = new AutoTagService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var tree = service.GetTagTree(pdf);

        Assert.NotNull(tree);
    }

    // ===== AltTextEditorService Tests =====

    [Fact]
    public void AltText_GetImageAltTexts_ReturnsList()
    {
        var service = new AltTextEditorService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var result = service.GetImageAltTexts(pdf);

        Assert.NotNull(result);
    }

    [Fact]
    public void AltText_CountMissingAltTexts_ReturnsCount()
    {
        var service = new AltTextEditorService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var count = service.CountMissingAltTexts(pdf);

        Assert.True(count >= 0);
    }

    [Fact]
    public void AltText_GenerateReport_ReturnsString()
    {
        var service = new AltTextEditorService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var report = service.GenerateAltTextReport(pdf);

        Assert.NotNull(report);
        Assert.True(report.Contains("Alt Text Coverage Report") || report.Contains("No images found"));
    }

    // ===== FontReplacementService Tests =====

    [Fact]
    public void FontReplacement_AnalyzeFonts_ReturnsList()
    {
        var service = new FontReplacementService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var fonts = service.AnalyzeFonts(pdf);

        Assert.NotNull(fonts);
    }

    [Fact]
    public void FontReplacement_GenerateReport_ReturnsString()
    {
        var service = new FontReplacementService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var report = service.GenerateFontReport(pdf);

        Assert.NotNull(report);
        Assert.Contains("Font Analysis Report", report);
    }

    [Fact]
    public void FontReplacement_EmbedAllFonts_ReturnsBytes()
    {
        var service = new FontReplacementService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var result = service.EmbedAllFonts(pdf);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }
}
