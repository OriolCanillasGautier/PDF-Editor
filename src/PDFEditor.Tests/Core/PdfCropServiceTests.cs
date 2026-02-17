using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for PdfCropService (crop, margins, resize)
/// </summary>
public class PdfCropServiceTests
{
    private readonly PdfCropService _cropService = new();
    private readonly PdfOperations _pdfOps = new();

    [Fact]
    public void CropPage_ValidCrop_ReturnsModifiedPdf()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var result = _cropService.CropPage(pdf, 1, 0.1, 0.1, 0.9, 0.9);
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        Assert.Equal(1, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void CropPage_FullPage_PreservesContent()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var result = _cropService.CropPage(pdf, 1, 0.0, 0.0, 1.0, 1.0);
        Assert.NotNull(result);
        Assert.Equal(1, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void CropPages_MultiplePagesAtOnce_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);
        var result = _cropService.CropPages(pdf, new[] { 1, 2, 3 }, 0.1, 0.1, 0.9, 0.9);
        Assert.NotNull(result);
        Assert.Equal(3, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void AddMargins_PositiveMargins_ReturnsModifiedPdf()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var result = _cropService.AddMargins(pdf, 36f, 36f, 36f, 36f);
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void ResizePages_ToA4_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var result = _cropService.ResizePages(pdf, PdfCropService.A4.w, PdfCropService.A4.h);
        Assert.NotNull(result);
        Assert.Equal(1, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void ResizePages_ToLetter_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var result = _cropService.ResizePages(pdf, PdfCropService.Letter.w, PdfCropService.Letter.h);
        Assert.NotNull(result);
        Assert.Equal(1, _pdfOps.GetPageCount(result));
    }

    [Fact]
    public void CropPages_SkipsOutOfRangePages_DoesNotThrow()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);
        // Page 99 doesn't exist, should be skipped
        var result = _cropService.CropPages(pdf, new[] { 1, 99 }, 0.1, 0.1, 0.9, 0.9);
        Assert.NotNull(result);
        Assert.Equal(2, _pdfOps.GetPageCount(result));
    }
}
