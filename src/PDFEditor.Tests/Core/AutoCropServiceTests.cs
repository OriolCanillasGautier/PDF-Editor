using Xunit;
using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for AutoCropService — margin analysis and automatic cropping.
/// </summary>
public class AutoCropServiceTests
{
    private readonly AutoCropService _service = new();

    [Fact]
    public void AnalyzeMargins_SimplePdf_ReturnsPageMargins()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);

        var margins = _service.AnalyzeMargins(pdf);

        Assert.NotNull(margins);
        Assert.NotEmpty(margins);
        Assert.Equal(2, margins.Count);
    }

    [Fact]
    public void AnalyzeMargins_SinglePage_ReturnsOneEntry()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var margins = _service.AnalyzeMargins(pdf);

        Assert.Single(margins);
    }

    [Fact]
    public void AutoCrop_SimplePdf_ReturnsValidPdf()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var result = _service.AutoCrop(pdf);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void AutoCrop_WithPadding_ReturnsValidPdf()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);

        var result = _service.AutoCrop(pdf, padding: 20f);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void UniformCrop_SimplePdf_ReturnsValidPdf()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);

        var result = _service.UniformCrop(pdf);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void AutoCrop_NullBytes_ThrowsException()
    {
        Assert.ThrowsAny<Exception>(() => _service.AutoCrop(null!));
    }

    [Fact]
    public void AnalyzeMargins_MarginsHaveReasonableValues()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var margins = _service.AnalyzeMargins(pdf);

        foreach (var m in margins)
        {
            Assert.True(m.MarginLeft >= 0, "Left margin should be >= 0");
            Assert.True(m.MarginTop >= 0, "Top margin should be >= 0");
            Assert.True(m.MarginRight >= 0, "Right margin should be >= 0");
            Assert.True(m.MarginBottom >= 0, "Bottom margin should be >= 0");
        }
    }
}
