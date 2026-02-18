using PDFEditor.Core.Services;
using PDFEditor.Core.Abstractions;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for PdfSignatureService: listing, adding signature fields, verification.
/// Note: Actual signing tests require a certificate file and are integration tests.
/// </summary>
public class PdfSignatureServiceTests
{
    private readonly PdfSignatureService _sut = new();

    #region GetSignatures

    [Fact]
    public void GetSignatures_UnsignedDocument_ReturnsEmpty()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var sigs = _sut.GetSignatures(pdf);
        Assert.Empty(sigs);
    }

    [Fact]
    public void GetSignatures_InvalidBytes_ReturnsEmpty()
    {
        var sigs = _sut.GetSignatures(new byte[] { 0, 1, 2 });
        Assert.Empty(sigs);
    }

    #endregion

    #region VerifySignatures

    [Fact]
    public void VerifySignatures_NoSignatures_ReturnsEmpty()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var result = _sut.VerifySignatures(pdf);
        Assert.Empty(result);
    }

    #endregion

    #region IsDocumentModifiedAfterSigning

    [Fact]
    public void IsDocumentModifiedAfterSigning_UnsignedDoc_ReturnsFalse()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        Assert.False(_sut.IsDocumentModifiedAfterSigning(pdf));
    }

    #endregion

    #region AddSignatureField

    [Fact]
    public void AddSignatureField_CreatesField()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var result = _sut.AddSignatureField(pdf, 0, "SigField1", 50, 50, 200, 80);

        Assert.NotNull(result);
        Assert.True(result.Length > pdf.Length);

        // The signature field should be detectable via form service
        var formService = new PdfFormService();
        Assert.True(formService.HasFormFields(result));
        var fields = formService.GetFormFields(result);
        Assert.Single(fields);
        Assert.Equal("SigField1", fields[0].Name);
        Assert.Equal(FormFieldType.Signature, fields[0].FieldType);
    }

    [Fact]
    public void AddSignatureField_MultipleFields_AllExist()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);
        pdf = _sut.AddSignatureField(pdf, 0, "Sig1", 50, 50, 200, 80);
        pdf = _sut.AddSignatureField(pdf, 1, "Sig2", 50, 50, 200, 80);

        var formService = new PdfFormService();
        var fields = formService.GetFormFields(pdf);
        Assert.Equal(2, fields.Count);
    }

    [Fact]
    public void AddSignatureField_InvalidPage_Throws()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        Assert.ThrowsAny<Exception>(() =>
            _sut.AddSignatureField(pdf, 99, "BadSig", 50, 50, 200, 80));
    }

    #endregion

    #region ListCertificates

    [Fact]
    public void ListCertificates_NonExistentDir_ReturnsEmpty()
    {
        var result = _sut.ListCertificates(@"C:\NonExistentDirectory\Certs");
        Assert.Empty(result);
    }

    [Fact]
    public void ListCertificates_TempDir_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = _sut.ListCertificates(tempDir);
            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion
}
