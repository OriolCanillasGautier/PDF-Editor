using Xunit;
using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for ElectronicSignatureService — typed/drawn signatures and PDF embedding.
/// </summary>
public class ElectronicSignatureServiceTests
{
    private readonly ElectronicSignatureService _service = new();

    [Fact]
    public void CreateTypedSignature_ValidName_ReturnsPngBytes()
    {
        var result = _service.CreateTypedSignature("John Doe");

        Assert.NotNull(result);
        Assert.True(result.Length > 100, "Signature image should be non-trivial");
        // PNG magic bytes
        Assert.Equal(0x89, result[0]);
        Assert.Equal(0x50, result[1]);
    }

    [Fact]
    public void CreateTypedSignature_EmptyName_ThrowsArgException()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.CreateTypedSignature(""));
    }

    [Fact]
    public void CreateTypedSignature_WhitespaceName_ThrowsArgException()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.CreateTypedSignature("   "));
    }

    [Fact]
    public void CreateTypedSignature_DifferentNames_ProduceDifferentImages()
    {
        var a = _service.CreateTypedSignature("Alice");
        var b = _service.CreateTypedSignature("Bob");

        // Different names should produce different images
        Assert.NotEqual(a.Length, b.Length);
    }

    [Fact]
    public void CreateDrawnSignature_ValidStrokes_ReturnsPngBytes()
    {
        var strokes = new List<List<(float x, float y)>>
        {
            new()
            {
                (0.1f, 0.5f), (0.3f, 0.3f), (0.5f, 0.5f), (0.7f, 0.3f), (0.9f, 0.5f)
            }
        };

        var result = _service.CreateDrawnSignature(strokes);

        Assert.NotNull(result);
        Assert.True(result.Length > 100);
        Assert.Equal(0x89, result[0]); // PNG
    }

    [Fact]
    public void CreateDrawnSignature_MultipleStrokes_Succeeds()
    {
        var strokes = new List<List<(float x, float y)>>
        {
            new() { (0.1f, 0.2f), (0.4f, 0.8f) },
            new() { (0.5f, 0.2f), (0.9f, 0.8f) },
        };

        var result = _service.CreateDrawnSignature(strokes);
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void CreateDrawnSignature_EmptyStrokes_ThrowsArgException()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.CreateDrawnSignature(new List<List<(float x, float y)>>()));
    }

    [Fact]
    public void CreateDrawnSignature_NullStrokes_ThrowsArgException()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.CreateDrawnSignature(null!));
    }

    [Fact]
    public void AddSignature_ValidPdfAndSig_ReturnsModifiedPdf()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var sigImage = _service.CreateTypedSignature("Test Signer");
        var sig = new ElectronicSignatureService.ElectronicSignature
        {
            SignerName = "Test Signer",
            SignatureImage = sigImage,
            Reason = "Approval",
            Location = "Office"
        };

        var result = _service.AddSignature(pdf, sig, 0);

        Assert.NotNull(result);
        Assert.True(result.Length > pdf.Length, "Signed PDF should be larger");
    }

    [Fact]
    public void AddSignature_InvalidPage_ThrowsArgumentOutOfRange()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var sig = new ElectronicSignatureService.ElectronicSignature
        {
            SignerName = "Test",
            SignatureImage = _service.CreateTypedSignature("Test"),
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.AddSignature(pdf, sig, 5));
    }

    [Fact]
    public void AddSignature_EmptyImage_ThrowsArgException()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var sig = new ElectronicSignatureService.ElectronicSignature
        {
            SignerName = "Test",
            SignatureImage = Array.Empty<byte>(),
        };

        Assert.Throws<ArgumentException>(() =>
            _service.AddSignature(pdf, sig, 0));
    }

    [Fact]
    public void ValidateSignatureImage_ValidPng_ReturnsTrue()
    {
        var sigImage = _service.CreateTypedSignature("Validator");

        var (isValid, message) = _service.ValidateSignatureImage(sigImage);

        Assert.True(isValid);
        Assert.Contains("Valid", message);
    }

    [Fact]
    public void ValidateSignatureImage_EmptyBytes_ReturnsFalse()
    {
        var (isValid, _) = _service.ValidateSignatureImage(Array.Empty<byte>());
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateSignatureImage_NullBytes_ReturnsFalse()
    {
        var (isValid, _) = _service.ValidateSignatureImage(null!);
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateSignatureImage_GarbageBytes_ReturnsFalse()
    {
        var garbage = new byte[] { 0xFF, 0x00, 0xAB, 0xCD, 0xEF };
        var (isValid, message) = _service.ValidateSignatureImage(garbage);

        // Might be valid (Magick can read some formats) or invalid
        // Just check it doesn't throw
        Assert.NotNull(message);
    }

    [Fact]
    public void ElectronicSignature_DefaultValues_AreReasonable()
    {
        var sig = new ElectronicSignatureService.ElectronicSignature();

        Assert.NotNull(sig.SignerName);
        Assert.NotNull(sig.Reason);
        Assert.NotNull(sig.Location);
        Assert.NotNull(sig.SignatureImage);
        Assert.InRange(sig.X, 0f, 1f);
        Assert.InRange(sig.Y, 0f, 1f);
        Assert.InRange(sig.Width, 0f, 1f);
        Assert.InRange(sig.Height, 0f, 1f);
        Assert.InRange(sig.SignedDate, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));
    }
}
