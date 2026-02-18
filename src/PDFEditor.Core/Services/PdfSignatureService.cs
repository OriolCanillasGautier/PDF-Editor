using iText.Kernel.Pdf;
using iText.Signatures;
using iText.Kernel.Geom;
using iText.Forms;
using iText.Forms.Fields;
using Org.BouncyCastle.Pkcs;
using NLog;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Core.Services;

/// <summary>
/// Digital signature operations using iText7 7.2.x.
/// Supports signing, verifying, listing signatures, and certificate management.
/// </summary>
public class PdfSignatureService : ISignatureService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <inheritdoc/>
    public List<PdfSignatureInfo> GetSignatures(byte[] pdfBytes)
    {
        var result = new List<PdfSignatureInfo>();
        try
        {
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var doc = new PdfDocument(reader);

            var signUtil = new SignatureUtil(doc);
            var sigNames = signUtil.GetSignatureNames();

            foreach (string name in sigNames)
            {
                var sig = signUtil.GetSignature(name);
                var info = new PdfSignatureInfo
                {
                    FieldName = name,
                    CoversWholeDocument = signUtil.SignatureCoversWholeDocument(name)
                };

                if (sig != null)
                {
                    info.Reason = sig.GetReason()?.ToString() ?? string.Empty;
                    info.Location = sig.GetLocation()?.ToString() ?? string.Empty;
                    info.SignerName = sig.GetName()?.ToString() ?? string.Empty;

                    var dateStr = sig.GetDate()?.ToString();
                    if (!string.IsNullOrEmpty(dateStr))
                    {
                        // iText PdfDate format: D:YYYYMMDDHHmmSSOHH'mm'
                        try
                        {
                            info.SignDate = iText.Kernel.Pdf.PdfDate.Decode(dateStr);
                        }
                        catch
                        {
                            // Best effort parse
                        }
                    }
                }

                // Get signature field position
                var form = PdfAcroForm.GetAcroForm(doc, false);
                if (form != null)
                {
                    var fields = form.GetFormFields();
                    if (fields.TryGetValue(name, out var field))
                    {
                        var widgets = field.GetWidgets();
                        if (widgets?.Count > 0)
                        {
                            var rect = widgets[0].GetRectangle();
                            if (rect != null)
                            {
                                info.X = rect.GetAsNumber(0)?.FloatValue() ?? 0;
                                info.Y = rect.GetAsNumber(1)?.FloatValue() ?? 0;
                                float x2 = rect.GetAsNumber(2)?.FloatValue() ?? 0;
                                float y2 = rect.GetAsNumber(3)?.FloatValue() ?? 0;
                                info.Width = x2 - info.X;
                                info.Height = y2 - info.Y;
                            }
                            var page = widgets[0].GetPage();
                            if (page != null)
                                info.PageIndex = doc.GetPageNumber(page) - 1;
                        }
                    }
                }

                result.Add(info);
            }

            Log.Info("Found {Count} signatures in PDF", result.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error reading signatures from PDF");
        }
        return result;
    }

    /// <inheritdoc/>
    public byte[] SignDocument(byte[] pdfBytes, SigningOptions options)
    {
        try
        {
            // Load the PKCS12 certificate
            using var certStream = new FileStream(options.CertificatePath, FileMode.Open, FileAccess.Read);
            var pkcs12Store = new Pkcs12StoreBuilder().Build();
            pkcs12Store.Load(certStream, options.CertificatePassword.ToCharArray());

            // Find the first private key entry
            string? alias = null;
            foreach (string a in pkcs12Store.Aliases)
            {
                if (pkcs12Store.IsKeyEntry(a))
                {
                    alias = a;
                    break;
                }
            }

            if (alias == null)
                throw new InvalidOperationException("No private key found in the certificate file.");

            var privateKey = pkcs12Store.GetKey(alias).Key;
            var chain = pkcs12Store.GetCertificateChain(alias)
                .Select(c => c.Certificate)
                .ToArray();

            // Generate field name if not provided
            var fieldName = string.IsNullOrWhiteSpace(options.FieldName)
                ? $"Sig_{DateTime.Now:yyyyMMddHHmmss}"
                : options.FieldName;

            var outputMs = new MemoryStream();
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            var signer = new PdfSigner(reader, outputMs, new StampingProperties().UseAppendMode());

            // Set signature appearance
            var appearance = signer.GetSignatureAppearance();
            appearance.SetReason(options.Reason);
            appearance.SetLocation(options.Location);
            appearance.SetContact(options.ContactInfo);

            if (options.IsVisible && options.PageIndex >= 0)
            {
                appearance.SetPageNumber(options.PageIndex + 1); // 1-based
                appearance.SetPageRect(new Rectangle(options.X, options.Y, options.Width, options.Height));
            }

            signer.SetFieldName(fieldName);

            // Sign with the private key (SHA-256)
            var externalSignature = new PrivateKeySignature(privateKey, DigestAlgorithms.SHA256);
            signer.SignDetached(externalSignature, chain, null, null, null, 0, PdfSigner.CryptoStandard.CADES);

            Log.Info("Document signed successfully with field '{FieldName}' by {Signer}",
                fieldName, chain.FirstOrDefault()?.SubjectDN?.ToString() ?? "Unknown");

            return outputMs.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error signing PDF document");
            throw;
        }
    }

    /// <inheritdoc/>
    public List<PdfSignatureInfo> VerifySignatures(byte[] pdfBytes)
    {
        var signatures = GetSignatures(pdfBytes);
        try
        {
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var doc = new PdfDocument(reader);
            var signUtil = new SignatureUtil(doc);

            foreach (var sig in signatures)
            {
                try
                {
                    var pkcs7 = signUtil.ReadSignatureData(sig.FieldName);
                    if (pkcs7 != null)
                    {
                        sig.IsValid = pkcs7.VerifySignatureIntegrityAndAuthenticity();
                        sig.ValidationMessage = sig.IsValid
                            ? "Signature is valid"
                            : "Signature integrity check failed";
                    }
                    else
                    {
                        sig.IsValid = false;
                        sig.ValidationMessage = "Could not read signature data";
                    }
                }
                catch (Exception ex)
                {
                    sig.IsValid = false;
                    sig.ValidationMessage = $"Verification error: {ex.Message}";
                    Log.Warn(ex, "Error verifying signature '{FieldName}'", sig.FieldName);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error verifying signatures");
        }
        return signatures;
    }

    /// <inheritdoc/>
    public bool IsDocumentModifiedAfterSigning(byte[] pdfBytes)
    {
        try
        {
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var doc = new PdfDocument(reader);
            var signUtil = new SignatureUtil(doc);
            var sigNames = signUtil.GetSignatureNames();

            if (sigNames.Count == 0) return false;

            // The last signature should cover the whole document
            var lastSig = sigNames[sigNames.Count - 1];
            return !signUtil.SignatureCoversWholeDocument(lastSig);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking document modification status");
            return false;
        }
    }

    /// <inheritdoc/>
    public byte[] AddSignatureField(byte[] pdfBytes, int pageIndex, string fieldName,
        float x, float y, float width, float height)
    {
        try
        {
            var outputMs = new MemoryStream();
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var writer = new PdfWriter(outputMs);
            using var doc = new PdfDocument(reader, writer);

            var form = PdfAcroForm.GetAcroForm(doc, true);
            var page = doc.GetPage(pageIndex + 1);
            var rect = new Rectangle(x, y, width, height);

            var sigField = PdfSignatureFormField.CreateSignature(doc, rect);
            sigField.SetFieldName(fieldName);
            form.AddField(sigField, page);

            doc.Close();
            Log.Info("Added signature field '{FieldName}' on page {Page}", fieldName, pageIndex + 1);
            return outputMs.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding signature field");
            throw;
        }
    }

    /// <inheritdoc/>
    public List<string> ListCertificates(string directoryPath)
    {
        var certificates = new List<string>();
        try
        {
            if (Directory.Exists(directoryPath))
            {
                certificates.AddRange(Directory.GetFiles(directoryPath, "*.pfx"));
                certificates.AddRange(Directory.GetFiles(directoryPath, "*.p12"));
                Log.Info("Found {Count} certificate files in {Directory}", certificates.Count, directoryPath);
            }
            else
            {
                Log.Warn("Certificate directory not found: {Directory}", directoryPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error listing certificates from {Directory}", directoryPath);
        }
        return certificates;
    }
}
