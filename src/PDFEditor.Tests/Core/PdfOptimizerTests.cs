using PDFEditor.Core.Services;
using Xunit;

namespace PDFEditor.Tests.Core;

public class PdfOptimizerTests
{
    private static byte[] GetMinimalValidPdf()
    {
        // Minimal valid one-page PDF (enough for iText7 to parse)
        const string minPdf =
            "%PDF-1.4\n" +
            "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
            "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
            "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>endobj\n" +
            "xref\n0 4\n0000000000 65535 f\r\n" +
            "0000000009 00000 n\r\n" +
            "0000000058 00000 n\r\n" +
            "0000000115 00000 n\r\n" +
            "trailer<</Size 4/Root 1 0 R>>\n" +
            "startxref\n190\n%%EOF\n";
        return System.Text.Encoding.ASCII.GetBytes(minPdf);
    }

    [Fact]
    public void Constructor_CreatesInstance()
    {
        var optimizer = new PdfOptimizer();
        Assert.NotNull(optimizer);
    }

    [Fact]
    public void Optimize_NullInputThrows()
    {
        var optimizer = new PdfOptimizer();
        Assert.Throws<ArgumentNullException>(() => optimizer.Optimize(null!, new PdfOptimizationOptions()));
    }

    [Fact]
    public void Optimize_EmptyInputThrows()
    {
        var optimizer = new PdfOptimizer();
        Assert.Throws<ArgumentException>(() => optimizer.Optimize(Array.Empty<byte>(), new PdfOptimizationOptions()));
    }

    [Fact]
    public void Optimize_NullOptions_UsesDefaults()
    {
        var optimizer = new PdfOptimizer();
        var pdf = GetMinimalValidPdf();
        // null options → uses PdfOptimizationOptions defaults, should not throw
        var result = optimizer.Optimize(pdf, null);
        Assert.NotNull(result);
    }

    [Fact]
    public void Optimize_ReturnsByteArray()
    {
        var optimizer = new PdfOptimizer();
        var pdf = GetMinimalValidPdf();
        var result = optimizer.Optimize(pdf, new PdfOptimizationOptions { OptimizeStreams = true, CompressImages = false });
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void Optimize_ResultStartsWithPdfHeader()
    {
        var optimizer = new PdfOptimizer();
        var pdf = GetMinimalValidPdf();
        var result = optimizer.Optimize(pdf, new PdfOptimizationOptions { OptimizeStreams = false, CompressImages = false });
        var header = System.Text.Encoding.ASCII.GetString(result, 0, Math.Min(5, result.Length));
        Assert.StartsWith("%PDF-", header);
    }

    [Fact]
    public void GetSizeStats_ReturnsMeaningfulStats()
    {
        var optimizer = new PdfOptimizer();
        var pdf = GetMinimalValidPdf();
        var stats = optimizer.GetSizeStats(pdf);
        Assert.True(stats.OriginalBytes > 0);
        Assert.True(stats.OptimizedBytes > 0);
        Assert.True(stats.PageCount >= 1);
    }

    [Fact]
    public void PdfOptimizationOptions_Defaults()
    {
        var opts = new PdfOptimizationOptions();
        Assert.True(opts.CompressImages);
        Assert.Equal(75, opts.ImageQuality);
        Assert.True(opts.OptimizeStreams);
        Assert.False(opts.RemoveMetadata);
        Assert.False(opts.Linearize);
    }

    [Fact]
    public void PdfSizeStats_SavingPercent_IsCorrect()
    {
        var stats = new PdfSizeStats { OriginalBytes = 1000, OptimizedBytes = 800, PageCount = 1 };
        Assert.Equal(20.0, stats.SavingPercent, 1);
    }

    [Fact]
    public void PdfSizeStats_NoSaving_IsZeroPercent()
    {
        var stats = new PdfSizeStats { OriginalBytes = 1000, OptimizedBytes = 1000, PageCount = 1 };
        Assert.Equal(0.0, stats.SavingPercent, 1);
    }

    [Fact]
    public void Optimize_RemoveMetadata_DoesNotThrow()
    {
        var optimizer = new PdfOptimizer();
        var pdf = GetMinimalValidPdf();
        var ex = Record.Exception(() =>
            optimizer.Optimize(pdf, new PdfOptimizationOptions { RemoveMetadata = true, CompressImages = false, OptimizeStreams = false }));
        Assert.Null(ex);
    }

    [Fact]
    public void Optimize_FullCompression_DoesNotThrow()
    {
        var optimizer = new PdfOptimizer();
        var pdf = GetMinimalValidPdf();
        var ex = Record.Exception(() =>
            optimizer.Optimize(pdf, new PdfOptimizationOptions { FullCompression = true, CompressImages = false }));
        Assert.Null(ex);
    }

    [Fact]
    public void Optimize_AllOptions_DoesNotThrow()
    {
        var optimizer = new PdfOptimizer();
        var pdf = GetMinimalValidPdf();
        var ex = Record.Exception(() =>
            optimizer.Optimize(pdf, new PdfOptimizationOptions
            {
                CompressImages  = true,
                ImageQuality    = 60,
                OptimizeStreams = true,
                RemoveMetadata  = true,
                FullCompression = true,
                Linearize       = false   // linearize needs valid cross-ref; skip in unit tests
            }));
        Assert.Null(ex);
    }
}
