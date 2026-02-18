using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Xobject;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Service for replacing images within PDF documents.
/// Finds image XObjects and swaps their data with new images.
/// </summary>
public class ImageReplaceService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Lists all images in a PDF with their page, index, dimensions, and name
    /// </summary>
    public List<PdfImageInfo> ListImages(byte[] pdfBytes)
    {
        Log.Info("Listing images in PDF");
        var images = new List<PdfImageInfo>();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var doc = new PdfDocument(reader);

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var resources = doc.GetPage(i).GetResources();
            var xObjects = resources.GetResource(PdfName.XObject);
            if (xObjects == null) continue;

            int idx = 0;
            foreach (var name in xObjects.KeySet())
            {
                var stream = xObjects.GetAsStream(name);
                if (stream == null) continue;
                if (!PdfName.Image.Equals(stream.GetAsName(PdfName.Subtype)))
                    continue;

                var w = stream.GetAsNumber(PdfName.Width)?.IntValue() ?? 0;
                var h = stream.GetAsNumber(PdfName.Height)?.IntValue() ?? 0;

                images.Add(new PdfImageInfo
                {
                    PageIndex = i - 1,
                    ImageIndex = idx++,
                    ResourceName = name.GetValue(),
                    Width = w,
                    Height = h,
                    ByteCount = stream.GetBytes(false)?.Length ?? 0
                });
            }
        }

        Log.Info("Found {Count} images", images.Count);
        return images;
    }

    /// <summary>
    /// Replaces an image on a specific page by resource name
    /// </summary>
    public byte[] ReplaceImage(byte[] pdfBytes, int pageIndex, string resourceName, byte[] newImageBytes)
    {
        Log.Info("Replacing image '{Name}' on page {Page}", resourceName, pageIndex + 1);

        var outMs = new MemoryStream();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var writer = new PdfWriter(outMs);
        using var doc = new PdfDocument(reader, writer);

        int pageNum = pageIndex + 1;
        if (pageNum < 1 || pageNum > doc.GetNumberOfPages())
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        var page = doc.GetPage(pageNum);
        var resources = page.GetResources();
        var xObjects = resources.GetResource(PdfName.XObject);

        if (xObjects == null)
            throw new InvalidOperationException("Page has no XObject resources");

        var pdfName = new PdfName(resourceName);
        var existing = xObjects.GetAsStream(pdfName);
        if (existing == null)
            throw new InvalidOperationException($"Image resource '{resourceName}' not found on page {pageNum}");

        // Create new image XObject
        var imageData = iText.IO.Image.ImageDataFactory.Create(newImageBytes);
        var newImage = new PdfImageXObject(imageData);

        xObjects.Put(pdfName, newImage.GetPdfObject());

        Log.Info("Image replaced successfully");
        doc.Close();
        return outMs.ToArray();
    }

    /// <summary>
    /// Replaces an image by page index and image index
    /// </summary>
    public byte[] ReplaceImageByIndex(byte[] pdfBytes, int pageIndex, int imageIndex, byte[] newImageBytes)
    {
        var images = ListImages(pdfBytes);
        var target = images.FirstOrDefault(img => img.PageIndex == pageIndex && img.ImageIndex == imageIndex);
        if (target == null)
            throw new InvalidOperationException($"Image at page {pageIndex}, index {imageIndex} not found");

        return ReplaceImage(pdfBytes, pageIndex, target.ResourceName, newImageBytes);
    }

    /// <summary>
    /// Replaces all images in the document with a placeholder
    /// </summary>
    public byte[] ReplaceAllImages(byte[] pdfBytes, byte[] replacementImageBytes)
    {
        Log.Info("Replacing all images in PDF");
        var images = ListImages(pdfBytes);
        byte[] result = pdfBytes;

        // Group by page and process
        foreach (var group in images.GroupBy(i => i.PageIndex).OrderBy(g => g.Key))
        {
            foreach (var img in group)
            {
                try
                {
                    result = ReplaceImage(result, img.PageIndex, img.ResourceName, replacementImageBytes);
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Failed to replace image '{Name}' on page {Page}", img.ResourceName, img.PageIndex + 1);
                }
            }
        }

        return result;
    }
}

/// <summary>
/// Information about an image resource in a PDF
/// </summary>
public class PdfImageInfo
{
    public int PageIndex { get; set; }
    public int ImageIndex { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public long ByteCount { get; set; }
}
