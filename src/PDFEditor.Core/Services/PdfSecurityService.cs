using iText.Kernel.Pdf;

namespace PDFEditor.Core.Services;

/// <summary>
/// Supported encryption levels for PDF documents.
/// </summary>
public enum PdfEncryptionLevel
{
    /// <summary>128-bit AES encryption (PDF 1.6+)</summary>
    Aes128,
    /// <summary>256-bit AES encryption (PDF 2.0, most secure)</summary>
    Aes256
}

/// <summary>
/// Password protection, encryption, and permission management for PDFs
/// </summary>
public class PdfSecurityService
{
    /// <summary>
    /// Encrypts a PDF with user and/or owner passwords using the specified encryption level.
    /// </summary>
    /// <param name="pdfBytes">Input PDF bytes</param>
    /// <param name="userPassword">Password to open the document (null = no open password)</param>
    /// <param name="ownerPassword">Password to change permissions (recommended)</param>
    /// <param name="allowPrinting">Allow printing</param>
    /// <param name="allowCopying">Allow text/image copying</param>
    /// <param name="allowEditing">Allow content editing</param>
    /// <param name="encryptionLevel">Encryption strength (128-bit or 256-bit AES)</param>
    public byte[] Encrypt(byte[] pdfBytes, string? userPassword, string ownerPassword,
        bool allowPrinting = true, bool allowCopying = true, bool allowEditing = false,
        PdfEncryptionLevel encryptionLevel = PdfEncryptionLevel.Aes256)
    {
        int permissions = 0;
        if (allowPrinting) permissions |= EncryptionConstants.ALLOW_PRINTING;
        if (allowCopying) permissions |= EncryptionConstants.ALLOW_COPY;
        if (allowEditing) permissions |= EncryptionConstants.ALLOW_MODIFY_CONTENTS;

        int encryptionConstant = encryptionLevel switch
        {
            PdfEncryptionLevel.Aes128 => EncryptionConstants.ENCRYPTION_AES_128,
            PdfEncryptionLevel.Aes256 => EncryptionConstants.ENCRYPTION_AES_256,
            _ => EncryptionConstants.ENCRYPTION_AES_256
        };

        var outputMs = new MemoryStream();
        var readerProps = new ReaderProperties();

        using (var reader = new PdfReader(new MemoryStream(pdfBytes), readerProps))
        {
            var writerProps = new WriterProperties()
                .SetStandardEncryption(
                    userPassword != null ? System.Text.Encoding.UTF8.GetBytes(userPassword) : null,
                    System.Text.Encoding.UTF8.GetBytes(ownerPassword),
                    permissions,
                    encryptionConstant);

            using var writer = new PdfWriter(outputMs, writerProps);
            var srcDoc = new PdfDocument(reader);
            var destDoc = new PdfDocument(writer);
            srcDoc.CopyPagesTo(1, srcDoc.GetNumberOfPages(), destDoc);
            srcDoc.Close();
            destDoc.Close();
        }
        return outputMs.ToArray();
    }

    /// <summary>
    /// Removes encryption/password from a PDF (requires correct password)
    /// </summary>
    public byte[] Decrypt(byte[] pdfBytes, string password)
    {
        var outputMs = new MemoryStream();
        var readerProps = new ReaderProperties()
            .SetPassword(System.Text.Encoding.UTF8.GetBytes(password));

        using (var reader = new PdfReader(new MemoryStream(pdfBytes), readerProps))
        {
            var srcDoc = new PdfDocument(reader);
            var writer = new PdfWriter(outputMs);
            var destDoc = new PdfDocument(writer);
            srcDoc.CopyPagesTo(1, srcDoc.GetNumberOfPages(), destDoc);
            srcDoc.Close();
            destDoc.Close();
        }
        return outputMs.ToArray();
    }

    /// <summary>
    /// Opens a password-protected PDF and returns its bytes in decrypted form
    /// </summary>
    public byte[] OpenWithPassword(byte[] encryptedPdfBytes, string password)
    {
        return Decrypt(encryptedPdfBytes, password);
    }

    /// <summary>
    /// Checks if a PDF is encrypted
    /// </summary>
    public bool IsEncrypted(byte[] pdfBytes)
    {
        try
        {
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            var doc = new PdfDocument(reader);
            doc.Close();
            return false;
        }
        catch (iText.Kernel.Exceptions.BadPasswordException)
        {
            return true;
        }
    }

    /// <summary>
    /// Tries to open with password, returns null if password is wrong
    /// </summary>
    public byte[]? TryOpenWithPassword(byte[] pdfBytes, string password)
    {
        try
        {
            return OpenWithPassword(pdfBytes, password);
        }
        catch
        {
            return null;
        }
    }
}
