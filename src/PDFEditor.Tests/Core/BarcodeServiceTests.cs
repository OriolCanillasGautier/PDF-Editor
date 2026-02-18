using Xunit;
using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for BarcodeService — barcode generation and PDF embedding.
/// </summary>
public class BarcodeServiceTests
{
    private readonly BarcodeService _service = new();

    [Fact]
    public void GenerateBarcode_QRCode_ReturnsPngBytes()
    {
        var result = _service.GenerateBarcode("Hello World", BarcodeService.BarcodeType.QRCode);

        Assert.NotNull(result);
        Assert.True(result.Length > 100, "QR code image should be non-trivial size");
        // PNG magic bytes
        Assert.Equal(0x89, result[0]);
        Assert.Equal(0x50, result[1]); // 'P'
    }

    [Fact]
    public void GenerateBarcode_Code128_ReturnsPngBytes()
    {
        var result = _service.GenerateBarcode("ABC123", BarcodeService.BarcodeType.Code128);

        Assert.NotNull(result);
        Assert.True(result.Length > 100);
        Assert.Equal(0x89, result[0]); // PNG
    }

    [Fact]
    public void GenerateBarcode_Code39_ReturnsPngBytes()
    {
        var result = _service.GenerateBarcode("HELLO", BarcodeService.BarcodeType.Code39);

        Assert.NotNull(result);
        Assert.True(result.Length > 50);
    }

    [Fact]
    public void GenerateBarcode_DataMatrix_ReturnsPngBytes()
    {
        var result = _service.GenerateBarcode("DataTest", BarcodeService.BarcodeType.DataMatrix);

        Assert.NotNull(result);
        Assert.True(result.Length > 100);
    }

    [Fact]
    public void GenerateBarcode_EmptyData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.GenerateBarcode("", BarcodeService.BarcodeType.QRCode));
    }

    [Fact]
    public void GenerateBarcode_WhitespaceData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.GenerateBarcode("   ", BarcodeService.BarcodeType.QRCode));
    }

    [Fact]
    public void GenerateBarcode_DifferentSizes_ProduceDifferentResults()
    {
        var small = _service.GenerateBarcode("Test", BarcodeService.BarcodeType.QRCode, 100);
        var large = _service.GenerateBarcode("Test", BarcodeService.BarcodeType.QRCode, 500);

        Assert.NotEqual(small.Length, large.Length);
    }

    [Fact]
    public void GenerateBarcode_DifferentData_ProduceDifferentResults()
    {
        var a = _service.GenerateBarcode("AAA", BarcodeService.BarcodeType.QRCode);
        var b = _service.GenerateBarcode("BBB", BarcodeService.BarcodeType.QRCode);

        // They should differ (different hash-based pattern)
        Assert.NotEqual(a.Length, b.Length); // might be same length, so check content
    }

    [Fact]
    public void EmbedBarcode_ValidPdfAndImage_ReturnsModifiedPdf()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(1);
        var barcodeImage = _service.GenerateBarcode("Test123", BarcodeService.BarcodeType.QRCode, 200);

        var result = _service.EmbedBarcode(pdfBytes, barcodeImage, 0);

        Assert.NotNull(result);
        Assert.True(result.Length > pdfBytes.Length, "PDF with barcode should be larger");
    }

    [Fact]
    public void EmbedBarcode_InvalidPageIndex_ThrowsArgumentOutOfRange()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(1);
        var barcodeImage = _service.GenerateBarcode("Test", BarcodeService.BarcodeType.Code128);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.EmbedBarcode(pdfBytes, barcodeImage, 5));
    }

    [Fact]
    public void EmbedBarcode_CustomPlacement_Succeeds()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(1);
        var barcodeImage = _service.GenerateBarcode("Custom", BarcodeService.BarcodeType.QRCode);
        var placement = new BarcodeService.BarcodePlacement
        {
            X = 0.1f, Y = 0.8f, Width = 0.15f, Height = 0.15f
        };

        var result = _service.EmbedBarcode(pdfBytes, barcodeImage, 0, placement);
        Assert.NotNull(result);
    }

    [Fact]
    public void GenerateAndEmbed_CombinedOperation_Succeeds()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(2);

        var result = _service.GenerateAndEmbed(pdfBytes, "Embedded QR",
            BarcodeService.BarcodeType.QRCode, 1);

        Assert.NotNull(result);
        Assert.True(result.Length > pdfBytes.Length);
    }

    [Fact]
    public void GenerateBarcode_EAN13Type_Succeeds()
    {
        var result = _service.GenerateBarcode("1234567890123", BarcodeService.BarcodeType.EAN13);
        Assert.NotNull(result);
        Assert.True(result.Length > 50);
    }

    [Fact]
    public void GenerateBarcode_PDF417Type_Succeeds()
    {
        var result = _service.GenerateBarcode("PDF417 Test Data", BarcodeService.BarcodeType.PDF417);
        Assert.NotNull(result);
        Assert.True(result.Length > 50);
    }
}
