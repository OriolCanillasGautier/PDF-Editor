namespace PDFEditor.Core.Abstractions;

/// <summary>
/// Information about a digital signature found in a PDF.
/// </summary>
public class PdfSignatureInfo
{
    public string FieldName { get; set; } = string.Empty;
    public string SignerName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime? SignDate { get; set; }
    public bool CoversWholeDocument { get; set; }
    public bool IsValid { get; set; }
    public string? ValidationMessage { get; set; }
    public int PageIndex { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}

/// <summary>
/// Options for signing a PDF document.
/// </summary>
public class SigningOptions
{
    /// <summary>
    /// Path to the PFX/P12 certificate file.
    /// </summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>
    /// Password for the certificate file.
    /// </summary>
    public string CertificatePassword { get; set; } = string.Empty;

    /// <summary>
    /// Reason for signing.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Location of signing.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Contact information of the signer.
    /// </summary>
    public string ContactInfo { get; set; } = string.Empty;

    /// <summary>
    /// Name of the signature field (auto-generated if empty).
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Page to place the visible signature on (0-based). -1 for invisible signature.
    /// </summary>
    public int PageIndex { get; set; } = 0;

    /// <summary>
    /// Position and size of the visible signature area.
    /// </summary>
    public float X { get; set; } = 50;
    public float Y { get; set; } = 50;
    public float Width { get; set; } = 200;
    public float Height { get; set; } = 80;

    /// <summary>
    /// Whether to make the signature visible on the page.
    /// </summary>
    public bool IsVisible { get; set; } = true;
}

/// <summary>
/// Service interface for PDF digital signature operations.
/// </summary>
public interface ISignatureService
{
    /// <summary>
    /// Lists all digital signatures in the PDF.
    /// </summary>
    List<PdfSignatureInfo> GetSignatures(byte[] pdfBytes);

    /// <summary>
    /// Signs the PDF with the specified certificate and returns the signed PDF bytes.
    /// </summary>
    byte[] SignDocument(byte[] pdfBytes, SigningOptions options);

    /// <summary>
    /// Verifies all signatures in the PDF and returns updated info with validation status.
    /// </summary>
    List<PdfSignatureInfo> VerifySignatures(byte[] pdfBytes);

    /// <summary>
    /// Checks if the document has been modified since the last signature.
    /// </summary>
    bool IsDocumentModifiedAfterSigning(byte[] pdfBytes);

    /// <summary>
    /// Adds an empty (unsigned) signature field to the PDF for later signing.
    /// </summary>
    byte[] AddSignatureField(byte[] pdfBytes, int pageIndex, string fieldName,
        float x, float y, float width, float height);

    /// <summary>
    /// Lists available certificate files (PFX/P12) from a directory.
    /// </summary>
    List<string> ListCertificates(string directoryPath);
}
