using NLog;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PDFEditor.Core.Services;

/// <summary>
/// Summary information about a certificate available for PDF signing.
/// </summary>
public class CertificateInfo
{
    /// <summary>Subject distinguished name (e.g. "CN=John Doe, O=Acme Corp").</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Issuer distinguished name.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Certificate serial number (hex string).</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>SHA-1 thumbprint (hex string, all uppercase, no separators).</summary>
    public string Thumbprint { get; set; } = string.Empty;

    /// <summary>Friendly name set on the certificate (may be empty).</summary>
    public string FriendlyName { get; set; } = string.Empty;

    /// <summary>Certificate validity start date/time.</summary>
    public DateTime NotBefore { get; set; }

    /// <summary>Certificate validity end date/time.</summary>
    public DateTime NotAfter { get; set; }

    /// <summary>Whether the certificate has expired.</summary>
    public bool IsExpired => DateTime.UtcNow > NotAfter;

    /// <summary>Whether the certificate is not yet valid.</summary>
    public bool IsNotYetValid => DateTime.UtcNow < NotBefore;

    /// <summary>True when the certificate is currently within its validity window.</summary>
    public bool IsValid => !IsExpired && !IsNotYetValid;

    /// <summary>
    /// Whether the certificate has a private key available (required for signing).
    /// </summary>
    public bool HasPrivateKey { get; set; }

    /// <summary>
    /// Source: "Store" for Windows certificate store, "File" for a PFX/P12 file.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// File path when Source == "File"; empty for store certificates.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Key usages (e.g. "Digital Signature, Non-Repudiation").
    /// </summary>
    public string KeyUsage { get; set; } = string.Empty;

    /// <summary>
    /// Short display name for the UI (extracted from Subject CN, or Thumbprint if absent).
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(FriendlyName))
                return FriendlyName;

            // Extract CN from subject
            var parts = Subject.Split(',');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    return trimmed[3..].Trim();
            }

            return Thumbprint.Length > 16 ? Thumbprint[..16] + "…" : Thumbprint;
        }
    }

    /// <summary>Multi-line human-readable summary of the certificate.</summary>
    public string GetSummary()
    {
        var lines = new List<string>
        {
            $"Subject:      {Subject}",
            $"Issuer:       {Issuer}",
            $"Serial:       {SerialNumber}",
            $"Thumbprint:   {Thumbprint}",
            $"Valid From:   {NotBefore:yyyy-MM-dd HH:mm}",
            $"Valid Until:  {NotAfter:yyyy-MM-dd HH:mm}",
            $"Status:       {(IsValid ? "✓ Valid" : IsExpired ? "✗ Expired" : "✗ Not yet valid")}",
            $"Private Key:  {(HasPrivateKey ? "Available" : "Not available")}",
            $"Source:       {Source}",
        };

        if (!string.IsNullOrEmpty(KeyUsage))
            lines.Add($"Key Usage:    {KeyUsage}");

        if (!string.IsNullOrEmpty(FilePath))
            lines.Add($"File:         {FilePath}");

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Manages certificates available for PDF digital signing.
/// Supports reading from the Windows Certificate Store and from PFX/P12 files.
/// </summary>
public class CertificateManagerService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // ------------------------------------------------------------------
    // Certificate Store access (Windows)
    // ------------------------------------------------------------------

    /// <summary>
    /// Lists certificates from the user's Personal (MY) Windows certificate store
    /// that have private keys and can be used for signing.
    /// Returns an empty list on non-Windows platforms or if the store cannot be read.
    /// </summary>
    public List<CertificateInfo> ListStoreCertificates(bool signingCapableOnly = true)
    {
        var results = new List<CertificateInfo>();

        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

            foreach (var cert in store.Certificates)
            {
                try
                {
                    if (signingCapableOnly && !cert.HasPrivateKey)
                        continue;

                    results.Add(MapCertificate(cert, "Windows Store", string.Empty));
                }
                catch (CryptographicException ex)
                {
                    Log.Debug(ex, "Skipping unreadable certificate in store");
                }
            }

            Log.Info("Listed {Count} certificates from Windows Certificate Store", results.Count);
        }
        catch (Exception ex)
        {
            // X509Store may not be available on non-Windows or in restricted environments
            Log.Warn(ex, "Could not read Windows Certificate Store");
        }

        return results;
    }

    // ------------------------------------------------------------------
    // PFX / P12 file inspection
    // ------------------------------------------------------------------

    /// <summary>
    /// Loads certificate information from a PFX/P12 file without importing it.
    /// The password is required to unlock the private key info.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the file cannot be parsed or password is wrong.
    /// </exception>
    public CertificateInfo InspectCertificateFile(string filePath, string password)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Certificate file not found: {filePath}");

        try
        {
            var cert = new X509Certificate2(
                filePath,
                password,
                X509KeyStorageFlags.EphemeralKeySet);   // don't import into store

            var info = MapCertificate(cert, "File", filePath);
            cert.Dispose();
            return info;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Could not open certificate. Verify the file is a valid PFX/P12 and the password is correct.",
                ex);
        }
    }

    /// <summary>
    /// Validates that a certificate file can be opened with the given password.
    /// Returns the certificate info on success or throws on failure.
    /// </summary>
    public (bool Success, CertificateInfo? Info, string Error) TryInspectCertificateFile(
        string filePath, string password)
    {
        try
        {
            var info = InspectCertificateFile(filePath, password);
            return (true, info, string.Empty);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Certificate inspection failed for: {Path}", filePath);
            return (false, null, ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Chain validation
    // ------------------------------------------------------------------

    /// <summary>
    /// Performs a basic chain-building validation on a PFX certificate.
    /// Returns true if the chain can be built (may not indicate trusted CA in all environments).
    /// </summary>
    public (bool ChainValid, string[] ChainErrors) ValidateCertificateChain(
        string filePath, string password)
    {
        try
        {
            using var cert = new X509Certificate2(
                filePath, password, X509KeyStorageFlags.EphemeralKeySet);

            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

            bool valid = chain.Build(cert);
            var errors = chain.ChainStatus
                .Select(s => s.StatusInformation.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            return (valid, errors);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Chain validation failed");
            return (false, new[] { ex.Message });
        }
    }

    // ------------------------------------------------------------------
    // Export / Reporting
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns a formatted text report listing all provided certificates.
    /// Useful for logging or displaying in a "Certificate Manager" dialog.
    /// </summary>
    public string GenerateCertificateReport(IEnumerable<CertificateInfo> certs)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Certificate Manager Report ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        int idx = 1;
        foreach (var cert in certs)
        {
            sb.AppendLine($"--- Certificate {idx++} ---");
            sb.AppendLine(cert.GetSummary());
            sb.AppendLine();
        }

        if (idx == 1)
            sb.AppendLine("No certificates found.");

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static CertificateInfo MapCertificate(X509Certificate2 cert, string source, string filePath)
    {
        // Extract key usage extensions
        string keyUsage = string.Empty;
        var ku = cert.Extensions
            .OfType<X509KeyUsageExtension>()
            .FirstOrDefault();
        if (ku != null)
            keyUsage = ku.KeyUsages.ToString();

        return new CertificateInfo
        {
            Subject      = cert.Subject,
            Issuer       = cert.Issuer,
            SerialNumber = cert.SerialNumber ?? string.Empty,
            Thumbprint   = cert.Thumbprint ?? string.Empty,
            FriendlyName = cert.FriendlyName ?? string.Empty,
            NotBefore    = cert.NotBefore.ToUniversalTime(),
            NotAfter     = cert.NotAfter.ToUniversalTime(),
            HasPrivateKey = cert.HasPrivateKey,
            Source       = source,
            FilePath     = filePath,
            KeyUsage     = keyUsage
        };
    }
}
