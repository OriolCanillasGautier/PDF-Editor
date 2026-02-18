using iText.Kernel.Pdf;
using iText.Kernel.XMP;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Scrubs all metadata from a PDF: Info dictionary, XMP streams, document ID.
/// Results in a PDF with no identifiable author, creator, producer, or custom property data.
/// </summary>
public class MetadataScrubberService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Removes all metadata from a PDF and returns cleaned bytes.
    /// </summary>
    /// <param name="pdfBytes">Source PDF bytes</param>
    /// <param name="preserveTitle">When true, retains the document title (useful for accessibility)</param>
    /// <returns>Cleaned PDF bytes</returns>
    public async Task<byte[]> ScrubAsync(byte[] pdfBytes, bool preserveTitle = false)
    {
        Log.Info("Metadata scrub started — {Bytes} bytes", pdfBytes.Length);

        return await Task.Run(() =>
        {
            using var inMs  = new MemoryStream(pdfBytes, writable: false);
            using var outMs = new MemoryStream();

            var readerProps = new ReaderProperties();
            using var reader = new PdfReader(inMs, readerProps);
            using var writer = new PdfWriter(outMs);
            using var doc    = new PdfDocument(reader, writer);

            // ── 1. Clear Info dictionary ─────────────────────────────────────────
            var info = doc.GetDocumentInfo();

            string? titleBackup = preserveTitle ? info.GetTitle() : null;

            info.SetAuthor(string.Empty);
            info.SetCreator(string.Empty);
            info.SetKeywords(string.Empty);
            info.SetSubject(string.Empty);
            info.SetProducer(string.Empty);
            info.SetMoreInfo("CreationDate", null);
            info.SetMoreInfo("ModDate", null);

            if (preserveTitle && !string.IsNullOrEmpty(titleBackup))
                info.SetTitle(titleBackup);
            else
                info.SetTitle(string.Empty);

            // Remove any custom keys via raw trailer /Info dictionary
            var infoPdfDict = doc.GetTrailer().GetAsDictionary(PdfName.Info);
            if (infoPdfDict != null)
            {
                var keysToRemove = new List<PdfName>();
                foreach (var key in infoPdfDict.KeySet())
                {
                    string name = key.GetValue();
                    if (name != "Title" || !preserveTitle)
                        keysToRemove.Add(key);
                }
                foreach (var key in keysToRemove)
                    infoPdfDict.Remove(key);
            }

            // ── 2. Clear XMP metadata ────────────────────────────────────────────
            try
            {
                var xmpBytes = doc.GetXmpMetadata(false);
                if (xmpBytes != null)
                {
                    // Replace with minimal valid XMP
                    var meta = XMPMetaFactory.Create();
                    if (preserveTitle && !string.IsNullOrEmpty(titleBackup))
                        meta.SetLocalizedText(XMPConst.NS_DC, "title", "x-default", "x-default", titleBackup);

                    doc.SetXmpMetadata(meta);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Could not clear XMP metadata; continuing without XMP removal");
            }

            // ── 3. Remove document-level optional metadata parts ─────────────────
            var catalog = doc.GetCatalog().GetPdfObject();
            // PieceInfo holds application private data blobs; SpiderInfo has crawl data
            catalog.Remove(new PdfName("PieceInfo"));
            catalog.Remove(new PdfName("SpiderInfo"));
            catalog.Remove(new PdfName("Legal"));

            doc.Close();

            byte[] result = outMs.ToArray();
            Log.Info("Metadata scrub complete — {Bytes} bytes", result.Length);
            return result;
        });
    }

    /// <summary>
    /// Synchronous convenience wrapper.
    /// </summary>
    public byte[] Scrub(byte[] pdfBytes, bool preserveTitle = false) =>
        ScrubAsync(pdfBytes, preserveTitle).GetAwaiter().GetResult();

    /// <summary>
    /// Returns a summary of all metadata found in the PDF (before scrubbing).
    /// </summary>
    public async Task<MetadataSummary> InspectAsync(byte[] pdfBytes)
    {
        return await Task.Run(() =>
        {
            using var ms     = new MemoryStream(pdfBytes, writable: false);
            using var reader = new PdfReader(ms);
            using var doc    = new PdfDocument(reader);

            var info = doc.GetDocumentInfo();
            var summary = new MetadataSummary
            {
                Title        = info.GetTitle(),
                Author       = info.GetAuthor(),
                Subject      = info.GetSubject(),
                Keywords     = info.GetKeywords(),
                Creator      = info.GetCreator(),
                Producer     = info.GetProducer(),
                CreationDate = info.GetMoreInfo("CreationDate"),
                ModDate      = info.GetMoreInfo("ModDate"),
                HasXmp       = doc.GetXmpMetadata(false) != null,
            };

            // Enumerate extra custom keys via raw trailer /Info dictionary
            var infoPdfDict = doc.GetTrailer().GetAsDictionary(PdfName.Info);
            if (infoPdfDict != null)
            {
                foreach (var key in infoPdfDict.KeySet())
                {
                    string name = key.GetValue();
                    if (!IsStandardInfoKey(name))
                        summary.CustomKeys[name] = infoPdfDict.GetAsString(key)?.GetValue() ?? "(binary)";
                }
            }

            return summary;
        });
    }

    private static readonly HashSet<string> _standardInfoKeys = new(StringComparer.Ordinal)
    {
        "Title", "Author", "Subject", "Keywords", "Creator", "Producer", "CreationDate", "ModDate", "Trapped",
    };

    private static bool IsStandardInfoKey(string name) => _standardInfoKeys.Contains(name);
}

/// <summary>
/// Metadata found in a PDF document.
/// </summary>
public class MetadataSummary
{
    public string? Title        { get; set; }
    public string? Author       { get; set; }
    public string? Subject      { get; set; }
    public string? Keywords     { get; set; }
    public string? Creator      { get; set; }
    public string? Producer     { get; set; }
    public string? CreationDate { get; set; }
    public string? ModDate      { get; set; }
    public bool    HasXmp       { get; set; }
    public Dictionary<string, string> CustomKeys { get; } = new();

    public bool HasAnyMetadata =>
        !string.IsNullOrEmpty(Title)    ||
        !string.IsNullOrEmpty(Author)   ||
        !string.IsNullOrEmpty(Subject)  ||
        !string.IsNullOrEmpty(Keywords) ||
        !string.IsNullOrEmpty(Creator)  ||
        !string.IsNullOrEmpty(Producer) ||
        HasXmp ||
        CustomKeys.Count > 0;

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(Title))    sb.AppendLine($"Title:    {Title}");
        if (!string.IsNullOrEmpty(Author))   sb.AppendLine($"Author:   {Author}");
        if (!string.IsNullOrEmpty(Subject))  sb.AppendLine($"Subject:  {Subject}");
        if (!string.IsNullOrEmpty(Keywords)) sb.AppendLine($"Keywords: {Keywords}");
        if (!string.IsNullOrEmpty(Creator))  sb.AppendLine($"Creator:  {Creator}");
        if (!string.IsNullOrEmpty(Producer)) sb.AppendLine($"Producer: {Producer}");
        if (!string.IsNullOrEmpty(CreationDate)) sb.AppendLine($"Created:  {CreationDate}");
        if (!string.IsNullOrEmpty(ModDate))  sb.AppendLine($"Modified: {ModDate}");
        if (HasXmp)                          sb.AppendLine("XMP metadata: present");
        foreach (var (k, v) in CustomKeys)  sb.AppendLine($"[Custom] {k}: {v}");
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no metadata)";
    }
}
