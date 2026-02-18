using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Information about an image's alt text
/// </summary>
public class ImageAltTextInfo
{
    public int PageIndex { get; set; }
    public int ImageIndex { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string? CurrentAltText { get; set; }
    public bool HasAltText => !string.IsNullOrWhiteSpace(CurrentAltText);
}

/// <summary>
/// Service for managing alt text (alternative text descriptions) for images in PDF documents.
/// Alt text is critical for screen reader accessibility (WCAG 2.1 / PDF/UA compliance).
/// </summary>
public class AltTextEditorService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Lists all images and their current alt text status
    /// </summary>
    public List<ImageAltTextInfo> GetImageAltTexts(byte[] pdfBytes)
    {
        Log.Info("Scanning PDF for image alt texts");
        var results = new List<ImageAltTextInfo>();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var doc = new PdfDocument(reader);

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);
            var resources = page.GetResources();
            var xObjects = resources.GetResource(PdfName.XObject);

            if (xObjects == null) continue;

            int idx = 0;
            foreach (var name in xObjects.KeySet())
            {
                var stream = xObjects.GetAsStream(name);
                if (stream == null) continue;
                if (!PdfName.Image.Equals(stream.GetAsName(PdfName.Subtype)))
                    continue;

                int w = stream.GetAsNumber(PdfName.Width)?.IntValue() ?? 0;
                int h = stream.GetAsNumber(PdfName.Height)?.IntValue() ?? 0;

                // Check for alt text in structure tree
                string? altText = null;
                if (doc.IsTagged())
                {
                    altText = FindAltTextInStructTree(doc, i, name.GetValue());
                }

                results.Add(new ImageAltTextInfo
                {
                    PageIndex = i - 1,
                    ImageIndex = idx++,
                    ResourceName = name.GetValue(),
                    Width = w,
                    Height = h,
                    CurrentAltText = altText
                });
            }
        }

        int withAlt = results.Count(r => r.HasAltText);
        Log.Info("Found {Total} images, {WithAlt} with alt text, {Without} without",
            results.Count, withAlt, results.Count - withAlt);
        return results;
    }

    /// <summary>
    /// Sets alt text for an image. Creates structure tags if document is not tagged.
    /// </summary>
    public byte[] SetAltText(byte[] pdfBytes, int pageIndex, string resourceName, string altText)
    {
        Log.Info("Setting alt text for image '{Name}' on page {Page}: \"{Alt}\"",
            resourceName, pageIndex + 1, altText);

        var outMs = new MemoryStream();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var writer = new PdfWriter(outMs);
        using var doc = new PdfDocument(reader, writer);

        if (!doc.IsTagged())
            doc.SetTagged();

        int pageNum = pageIndex + 1;
        if (pageNum < 1 || pageNum > doc.GetNumberOfPages())
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        // Find or create structure element for image
        var structTreeRoot = doc.GetStructTreeRoot();
        bool found = false;

        if (structTreeRoot != null)
        {
            found = SetAltTextInStructTree(structTreeRoot, pageNum, resourceName, altText);
        }

        if (!found)
        {
            // Create a Figure element with alt text
            // Add it via the tag structure context
            var tagPointer = doc.GetTagStructureContext().GetAutoTaggingPointer();
            tagPointer.SetPageForTagging(doc.GetPage(pageNum));

            // Add figure element
            tagPointer.AddTag(iText.Kernel.Pdf.Tagging.StandardRoles.FIGURE);
            tagPointer.GetProperties().SetAlternateDescription(altText);
            tagPointer.MoveToParent();

            Log.Debug("Created new Figure tag with alt text");
        }

        doc.Close();
        return outMs.ToArray();
    }

    /// <summary>
    /// Sets alt text for multiple images at once
    /// </summary>
    public byte[] SetBulkAltTexts(byte[] pdfBytes, Dictionary<(int pageIndex, string resourceName), string> altTexts)
    {
        Log.Info("Setting alt text for {Count} images", altTexts.Count);
        byte[] result = pdfBytes;

        foreach (var kvp in altTexts)
        {
            try
            {
                result = SetAltText(result, kvp.Key.pageIndex, kvp.Key.resourceName, kvp.Value);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to set alt text for image '{Name}' on page {Page}",
                    kvp.Key.resourceName, kvp.Key.pageIndex + 1);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets count of images missing alt text
    /// </summary>
    public int CountMissingAltTexts(byte[] pdfBytes)
    {
        var images = GetImageAltTexts(pdfBytes);
        return images.Count(i => !i.HasAltText);
    }

    /// <summary>
    /// Generates a report of alt text coverage
    /// </summary>
    public string GenerateAltTextReport(byte[] pdfBytes)
    {
        var images = GetImageAltTexts(pdfBytes);
        if (!images.Any())
            return "No images found in document.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Alt Text Coverage Report");
        sb.AppendLine("=======================");
        sb.AppendLine($"Total images: {images.Count}");
        sb.AppendLine($"With alt text: {images.Count(i => i.HasAltText)}");
        sb.AppendLine($"Missing alt text: {images.Count(i => !i.HasAltText)}");
        sb.AppendLine();

        foreach (var group in images.GroupBy(i => i.PageIndex))
        {
            sb.AppendLine($"Page {group.Key + 1}:");
            foreach (var img in group)
            {
                string status = img.HasAltText ? $"✓ \"{img.CurrentAltText}\"" : "✗ MISSING";
                sb.AppendLine($"  [{img.ResourceName}] {img.Width}×{img.Height} — {status}");
            }
        }

        return sb.ToString();
    }

    private string? FindAltTextInStructTree(PdfDocument doc, int pageNum, string resourceName)
    {
        try
        {
            var root = doc.GetStructTreeRoot();
            if (root == null) return null;

            return SearchStructElement(root, resourceName);
        }
        catch
        {
            return null;
        }
    }

    private string? SearchStructElement(iText.Kernel.Pdf.Tagging.IStructureNode node, string resourceName)
    {
        if (node is iText.Kernel.Pdf.Tagging.PdfStructElem elem)
        {
            var role = elem.GetRole()?.GetValue();
            if (role == "Figure" || role == "Image")
            {
                var alt = elem.GetPdfObject().GetAsString(new PdfName("Alt"));
                if (alt != null)
                    return alt.GetValue();
            }

            var kids = elem.GetKids();
            if (kids != null)
            {
                foreach (var kid in kids)
                {
                    var result = SearchStructElement(kid, resourceName);
                    if (result != null) return result;
                }
            }
        }
        return null;
    }

    private bool SetAltTextInStructTree(iText.Kernel.Pdf.Tagging.PdfStructTreeRoot root, int pageNum, string resourceName, string altText)
    {
        return SetAltInNode(root, resourceName, altText);
    }

    private bool SetAltInNode(iText.Kernel.Pdf.Tagging.IStructureNode node, string resourceName, string altText)
    {
        if (node is iText.Kernel.Pdf.Tagging.PdfStructElem elem)
        {
            var role = elem.GetRole()?.GetValue();
            if (role == "Figure" || role == "Image")
            {
                elem.GetPdfObject().Put(new PdfName("Alt"), new PdfString(altText));
                return true;
            }

            var kids = elem.GetKids();
            if (kids != null)
            {
                foreach (var kid in kids)
                {
                    if (SetAltInNode(kid, resourceName, altText))
                        return true;
                }
            }
        }
        return false;
    }
}
