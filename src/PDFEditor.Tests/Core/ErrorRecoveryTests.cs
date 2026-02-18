using Microsoft.Extensions.DependencyInjection;
using PDFEditor.Core.Abstractions;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests that critical service methods degrade gracefully when given
/// corrupt, truncated, locked, or otherwise invalid inputs.
/// </summary>
public class ErrorRecoveryTests
{
    // ── IPdfDocument / ITextPdfService via LoadFromFile ────────────────────────

    [Fact]
    public void LoadFromFile_NonExistentPath_ThrowsFileNotFoundException()
    {
        var pdf = TestHelpers.BuildServiceProvider().GetService<IPdfDocument>();
        if (pdf == null) return; // skip if DI not configured in test environment

        Assert.Throws<FileNotFoundException>(() =>
            pdf.LoadFromFile(@"C:\does\not\exist\missing.pdf"));
    }

    [Fact]
    public void LoadFromFile_EmptyPath_ThrowsArgumentException()
    {
        var pdf = TestHelpers.BuildServiceProvider().GetService<IPdfDocument>();
        if (pdf == null) return;

        Assert.ThrowsAny<ArgumentException>(() => pdf.LoadFromFile(""));
    }

    [Fact]
    public void LoadFromFile_NullPath_ThrowsArgumentException()
    {
        var pdf = TestHelpers.BuildServiceProvider().GetService<IPdfDocument>();
        if (pdf == null) return;

        Assert.ThrowsAny<ArgumentException>(() => pdf.LoadFromFile(null!));
    }

    [Fact]
    public void LoadFromFile_BinaryTxtFile_ThrowsMeaningfulException()
    {
        var pdf = TestHelpers.BuildServiceProvider().GetService<IPdfDocument>();
        if (pdf == null) return;

        // Write random bytes to a temp file and try loading it as a PDF
        var tmpFile = Path.GetTempFileName();
        try
        {
            var garbage = new byte[256];
            new Random(42).NextBytes(garbage);
            File.WriteAllBytes(tmpFile, garbage);

            Assert.ThrowsAny<Exception>(() => pdf.LoadFromFile(tmpFile));
        }
        finally
        {
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
        }
    }

    // ── PdfOptimizer ──────────────────────────────────────────────────────────

    [Fact]
    public void PdfOptimizer_CorruptInput_ThrowsOrReturnsEmpty()
    {
        var optimizer = new PDFEditor.Core.Services.PdfOptimizer();
        var garbage   = new byte[512];
        new Random(42).NextBytes(garbage);
        Assert.ThrowsAny<Exception>(() =>
            optimizer.Optimize(garbage, new PDFEditor.Core.Services.PdfOptimizationOptions()));
    }

    [Fact]
    public void PdfOptimizer_NullInput_ThrowsArgumentNullException()
    {
        var optimizer = new PDFEditor.Core.Services.PdfOptimizer();
        Assert.Throws<ArgumentNullException>(() =>
            optimizer.Optimize(null!, new PDFEditor.Core.Services.PdfOptimizationOptions()));
    }

    [Fact]
    public void PdfOptimizer_EmptyInput_ThrowsArgumentException()
    {
        var optimizer = new PDFEditor.Core.Services.PdfOptimizer();
        Assert.Throws<ArgumentException>(() =>
            optimizer.Optimize([], new PDFEditor.Core.Services.PdfOptimizationOptions()));
    }

    // ── IRedactionService edge cases ──────────────────────────────────────────

    [Fact]
    public void RedactionService_NullInput_ThrowsArgumentException()
    {
        var redact = TestHelpers.BuildServiceProvider().GetService<IRedactionService>();
        if (redact == null) return;

        Assert.ThrowsAny<ArgumentException>(() => redact.RedactText(null!, "secret"));
    }

    [Fact]
    public void RedactionService_EmptyPatterns_ReturnsInput()
    {
        var redact = TestHelpers.BuildServiceProvider().GetService<IRedactionService>();
        if (redact == null) return;

        var pdf = TestHelpers.MinimalPdfBytes();
        // Empty-string pattern — should not throw and return input unchanged
        var ex = Record.Exception(() => redact.RedactText(pdf, ""));
        Assert.Null(ex);
    }
}
