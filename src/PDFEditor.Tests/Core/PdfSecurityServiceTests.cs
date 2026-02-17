using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for PdfSecurityService (encryption, decryption, password handling)
/// </summary>
public class PdfSecurityServiceTests
{
    private readonly PdfSecurityService _securityService = new();

    [Fact]
    public void IsEncrypted_UnencryptedPdf_ReturnsFalse()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        Assert.False(_securityService.IsEncrypted(pdf));
    }

    [Fact]
    public void Encrypt_WithUserPassword_ProducesEncryptedPdf()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var encrypted = _securityService.Encrypt(pdf, "user123", "owner456");
        Assert.NotNull(encrypted);
        Assert.True(encrypted.Length > 0);
        Assert.True(_securityService.IsEncrypted(encrypted));
    }

    [Fact]
    public void Encrypt_WithoutUserPassword_ProducesEncryptedPdf()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var encrypted = _securityService.Encrypt(pdf, null, "owner456");
        Assert.NotNull(encrypted);
        Assert.True(encrypted.Length > 0);
    }

    [Fact]
    public void Decrypt_WithOwnerPassword_ReturnsDecryptedPdf()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var encrypted = _securityService.Encrypt(pdf, "user123", "owner456");
        var decrypted = _securityService.Decrypt(encrypted, "owner456");
        Assert.NotNull(decrypted);
        Assert.False(_securityService.IsEncrypted(decrypted));
    }

    [Fact]
    public void TryOpenWithPassword_OwnerPassword_ReturnsBytes()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var encrypted = _securityService.Encrypt(pdf, "pass123", "owner456");
        var result = _securityService.TryOpenWithPassword(encrypted, "owner456");
        Assert.NotNull(result);
        Assert.True(result!.Length > 0);
    }

    [Fact]
    public void TryOpenWithPassword_WrongPassword_ReturnsNull()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var encrypted = _securityService.Encrypt(pdf, "pass123", "owner456");
        var result = _securityService.TryOpenWithPassword(encrypted, "wrongpass");
        Assert.Null(result);
    }

    [Fact]
    public void OpenWithPassword_OwnerPassword_ReturnsDecrypted()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var encrypted = _securityService.Encrypt(pdf, "secret", "owner");
        var opened = _securityService.OpenWithPassword(encrypted, "owner");
        Assert.NotNull(opened);
        Assert.False(_securityService.IsEncrypted(opened));
    }

    [Fact]
    public void Encrypt_WithPermissions_ProducesValidPdf()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var encrypted = _securityService.Encrypt(pdf, "user", "owner",
            allowPrinting: false, allowCopying: false, allowEditing: false);
        Assert.NotNull(encrypted);
        Assert.True(encrypted.Length > 0);
    }
}
