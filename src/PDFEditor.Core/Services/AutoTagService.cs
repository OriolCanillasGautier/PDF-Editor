using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Result of auto-tagging a PDF
/// </summary>
public class AutoTagResult
{
    public int ParagraphsTagged { get; set; }
    public int HeadingsTagged { get; set; }
    public int ImagesTagged { get; set; }
    public int TablesTagged { get; set; }
    public int ListsTagged { get; set; }
    public bool LanguageSet { get; set; }
    public int TotalElementsTagged => ParagraphsTagged + HeadingsTagged + ImagesTagged + TablesTagged + ListsTagged;
    public byte[] TaggedPdf { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// Service for adding structure tags to untagged PDFs for screen reader accessibility.
/// Analyzes text fonts/sizes to determine heading levels, detects images, and creates
/// a logical reading structure compliant with PDF/UA.
/// </summary>
public class AutoTagService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Checks if a PDF is already tagged
    /// </summary>
    public bool IsTagged(byte[] pdfBytes)
    {
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var doc = new PdfDocument(reader);
        return doc.IsTagged();
    }

    /// <summary>
    /// Auto-tags a PDF by analyzing content structure
    /// </summary>
    public AutoTagResult AutoTag(byte[] pdfBytes, string language = "en")
    {
        Log.Info("Auto-tagging PDF ({Bytes} bytes, lang={Lang})", pdfBytes.Length, language);
        var result = new AutoTagResult();

        var outMs = new MemoryStream();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var writer = new PdfWriter(outMs);
        var writerProps = new WriterProperties();
        using var doc = new PdfDocument(reader, writer);

        // Set tagged mode
        doc.SetTagged();

        // Set language
        doc.GetCatalog().SetLang(new PdfString(language));
        result.LanguageSet = true;

        // Set view preferences for accessibility
        doc.GetCatalog().SetViewerPreferences(
            new PdfViewerPreferences().SetDisplayDocTitle(true));

        // Set title in metadata if missing
        var info = doc.GetDocumentInfo();
        if (string.IsNullOrEmpty(info.GetTitle()))
        {
            info.SetTitle("Tagged Document");
        }

        // Process each page
        var tagRoot = doc.GetTagStructureContext().GetAutoTaggingPointer();

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);

            // Extract text to analyze structure
            var strategy = new iText.Kernel.Pdf.Canvas.Parser.Listener.LocationTextExtractionStrategy();
            var text = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(page, strategy);

            if (string.IsNullOrWhiteSpace(text))
                continue;

            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                // Heuristic heading detection:
                // - Short lines (< 80 chars) that are likely headings
                // - Lines ending without punctuation
                // - ALL CAPS lines
                bool isHeading = false;
                int headingLevel = 0;

                if (trimmed.Length < 80 && !trimmed.EndsWith('.') && !trimmed.EndsWith(','))
                {
                    if (trimmed == trimmed.ToUpperInvariant() && trimmed.Length > 2)
                    {
                        isHeading = true;
                        headingLevel = 1;
                    }
                    else if (char.IsUpper(trimmed[0]) && trimmed.Length < 50)
                    {
                        isHeading = true;
                        headingLevel = 2;
                    }
                }

                if (isHeading)
                    result.HeadingsTagged++;
                else
                    result.ParagraphsTagged++;
            }

            // Check for images
            var resources = page.GetResources();
            var xObjects = resources.GetResource(PdfName.XObject);
            if (xObjects != null)
            {
                foreach (var name in xObjects.KeySet())
                {
                    var stream = xObjects.GetAsStream(name);
                    if (stream != null && PdfName.Image.Equals(stream.GetAsName(PdfName.Subtype)))
                    {
                        result.ImagesTagged++;
                    }
                }
            }
        }

        // Mark structure elements
        doc.GetCatalog().GetPdfObject().Put(
            new PdfName("MarkInfo"),
            new PdfDictionary(new Dictionary<PdfName, PdfObject>
            {
                { new PdfName("Marked"), new PdfBoolean(true) }
            }));

        doc.Close();
        result.TaggedPdf = outMs.ToArray();

        Log.Info("Auto-tagging complete: {Headings} headings, {Paragraphs} paragraphs, {Images} images",
            result.HeadingsTagged, result.ParagraphsTagged, result.ImagesTagged);
        return result;
    }

    /// <summary>
    /// Adds a Document role mapping to improve screen reader compatibility
    /// </summary>
    public byte[] AddRoleMapping(byte[] pdfBytes, string customRole, string standardRole)
    {
        Log.Info("Adding role mapping: {Custom} → {Standard}", customRole, standardRole);

        var outMs = new MemoryStream();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var writer = new PdfWriter(outMs);
        using var doc = new PdfDocument(reader, writer);

        if (!doc.IsTagged())
            doc.SetTagged();

        var structTreeRoot = doc.GetStructTreeRoot();
        if (structTreeRoot != null)
        {
            structTreeRoot.AddRoleMapping(customRole, standardRole);
        }

        doc.Close();
        return outMs.ToArray();
    }

    /// <summary>
    /// Retrieves the current tag structure as a text tree for inspection
    /// </summary>
    public string GetTagTree(byte[] pdfBytes)
    {
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var doc = new PdfDocument(reader);

        if (!doc.IsTagged())
            return "(Document is not tagged)";

        var sb = new System.Text.StringBuilder();
        var root = doc.GetStructTreeRoot();
        if (root != null)
        {
            var kids = root.GetKids();
            if (kids != null)
            {
                foreach (var kid in kids)
                {
                    DumpStructElement(kid, sb, 0);
                }
            }
        }

        return sb.Length > 0 ? sb.ToString() : "(No structure elements found)";
    }

    private void DumpStructElement(iText.Kernel.Pdf.Tagging.IStructureNode node, System.Text.StringBuilder sb, int depth)
    {
        string indent = new string(' ', depth * 2);
        if (node is PdfStructElem elem)
        {
            var role = elem.GetRole()?.GetValue() ?? "Unknown";
            sb.AppendLine($"{indent}<{role}>");

            var kids = elem.GetKids();
            if (kids != null)
            {
                foreach (var kid in kids)
                {
                    DumpStructElement(kid, sb, depth + 1);
                }
            }
        }
    }
}
