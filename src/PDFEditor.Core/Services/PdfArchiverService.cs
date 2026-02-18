using iText.Kernel.Pdf;
using iText.Kernel.XMP;
using iText.Pdfa;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Converts PDF documents to PDF/A-2B (ISO 19005-2) archival format using iText7.
/// PDF/A requires embedded fonts, a color output intent (sRGB ICC profile),
/// and conformance-level XMP metadata.
/// </summary>
public class PdfArchiverService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // Candidate sRGB ICC profile paths (Windows, macOS, Linux)
    private static readonly string[] IccProfileCandidates =
    {
        @"C:\Windows\System32\spool\drivers\color\sRGB Color Space Profile.icm",
        "/usr/share/color/icc/colord/sRGB.icc",
        "/usr/share/color/icc/sRGB.icc",
        "/Library/ColorSync/Profiles/sRGB Profile.icc",
    };

    /// <summary>
    /// Converts a PDF to PDF/A-2B compliant format.
    /// </summary>
    /// <param name="pdfBytes">Source PDF bytes</param>
    /// <param name="progressCallback">Optional progress reporter</param>
    /// <returns>PDF/A-2B compliant PDF bytes, or original with watermark failure metadata if conversion fails</returns>
    public async Task<PdfArchiverResult> ConvertToPdfA2BAsync(
        byte[] pdfBytes,
        IProgress<int>? progressCallback = null)
    {
        Log.Info("PDF/A-2B conversion started — {Bytes} bytes", pdfBytes.Length);

        return await Task.Run(() =>
        {
            string? iccPath = FindIccProfile();
            if (iccPath == null)
            {
                const string msg =
                    "No sRGB ICC profile found on this system. " +
                    "PDF/A conversion requires an sRGB ICC profile to embed as the output intent. " +
                    "On Windows, install a color profile or place sRGB.icc in the application directory.";
                Log.Warn(msg);
                return PdfArchiverResult.Fail(msg);
            }

            Log.Info("Using ICC profile: {Path}", iccPath);

            try
            {
                progressCallback?.Report(10);

                using var iccStream = File.OpenRead(iccPath);
                var outputIntent = new PdfOutputIntent(
                    "Custom",
                    string.Empty,
                    "http://www.color.org",
                    "sRGB IEC61966-2.1",
                    iccStream);

                progressCallback?.Report(20);

                using var inMs  = new MemoryStream(pdfBytes, writable: false);
                using var outMs = new MemoryStream();

                int pageCount;

                // Use explicit using blocks to guarantee pdfADoc is flushed BEFORE outMs.ToArray()
                using (var reader = new PdfReader(inMs))
                using (var srcDoc = new PdfDocument(reader))
                {
                    pageCount = srcDoc.GetNumberOfPages();

                    // PDF/A-2B: ISO 19005-2 Level B (basic conformance)
                    using (var writer = new PdfWriter(outMs,
                               new WriterProperties().SetPdfVersion(PdfVersion.PDF_1_7)))
                    using (var pdfADoc = new PdfADocument(writer, PdfAConformanceLevel.PDF_A_2B, outputIntent))
                    {
                        progressCallback?.Report(40);

                        // Copy all pages from source into the PDF/A document
                        srcDoc.CopyPagesTo(1, pageCount, pdfADoc);

                        // Set conformance XMP metadata
                        var xmpMeta = XMPMetaFactory.Create();
                        xmpMeta.SetProperty(XMPConst.NS_PDFA_ID, "conformance", "B");
                        xmpMeta.SetProperty(XMPConst.NS_PDFA_ID, "part", "2");
                        pdfADoc.SetXmpMetadata(xmpMeta);

                        progressCallback?.Report(80);
                    } // ← pdfADoc.Close() & writer.Close() here — data flushed to outMs
                }

                progressCallback?.Report(100);
                byte[] result = outMs.ToArray(); // ← now safely readable
                Log.Info("PDF/A-2B conversion complete — {Pages} pages, {Bytes} bytes", pageCount, result.Length);
                return PdfArchiverResult.Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PDF/A-2B conversion failed");
                return PdfArchiverResult.Fail($"PDF/A conversion failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Validates whether a PDF claims to be PDF/A compliant (checks XMP metadata).
    /// Does NOT perform full conformance checking — use a dedicated veraPDF tool for that.
    /// </summary>
    public async Task<PdfAValidationInfo> InspectConformanceAsync(byte[] pdfBytes)
    {
        return await Task.Run(() =>
        {
            using var ms     = new MemoryStream(pdfBytes, writable: false);
            using var reader = new PdfReader(ms);
            using var doc    = new PdfDocument(reader);

            var info = new PdfAValidationInfo { PageCount = doc.GetNumberOfPages() };

            try
            {
                var xmpBytes = doc.GetXmpMetadata(false);
                if (xmpBytes != null)
                {
                    var xmp = XMPMetaFactory.ParseFromBuffer(xmpBytes);
                    info.PdfAConformanceLevel = xmp.GetPropertyString(XMPConst.NS_PDFA_ID, "conformance");
                    info.PdfAPart            = xmp.GetPropertyString(XMPConst.NS_PDFA_ID, "part");
                    info.HasXmpPdfAClaim     = !string.IsNullOrEmpty(info.PdfAPart);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not parse XMP from PDF for conformance check");
            }

            info.PdfVersion = doc.GetPdfVersion()?.ToString() ?? "unknown";

            return info;
        });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static string? FindIccProfile()
    {
        // Check application directory first (user can drop sRGB.icc there)
        string? appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        if (appDir != null)
        {
            foreach (string name in new[] { "sRGB.icc", "sRGB Color Space Profile.icm", "sRGB2014.icc" })
            {
                string local = Path.Combine(appDir, name);
                if (File.Exists(local)) return local;
            }
        }

        foreach (string path in IccProfileCandidates)
            if (File.Exists(path)) return path;

        return null;
    }
}

/// <summary>Result of a PDF/A conversion operation.</summary>
public class PdfArchiverResult
{
    public bool    Success      { get; private set; }
    public byte[]? Data         { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static PdfArchiverResult Ok(byte[] data)   => new() { Success = true,  Data = data };
    public static PdfArchiverResult Fail(string error) => new() { Success = false, ErrorMessage = error };
}

/// <summary>PDF/A conformance metadata extracted from a document.</summary>
public class PdfAValidationInfo
{
    public bool   HasXmpPdfAClaim     { get; set; }
    public string? PdfAPart           { get; set; }
    public string? PdfAConformanceLevel { get; set; }
    public int    PageCount           { get; set; }
    public string PdfVersion          { get; set; } = string.Empty;

    public string ConformanceLabel =>
        HasXmpPdfAClaim
            ? $"PDF/A-{PdfAPart}{PdfAConformanceLevel}"
            : "Not PDF/A (no XMP claim)";
}
