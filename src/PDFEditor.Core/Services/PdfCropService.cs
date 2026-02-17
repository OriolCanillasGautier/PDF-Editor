using iText.Kernel.Geom;
using iText.Kernel.Pdf;

namespace PDFEditor.Core.Services;

/// <summary>
/// Service for cropping and resizing PDF pages
/// </summary>
public class PdfCropService
{
    /// <summary>
    /// Crops a page to a normalized region (0-1 coordinates relative to page dimensions).
    /// </summary>
    /// <param name="pdfBytes">Source PDF bytes</param>
    /// <param name="pageNumber">1-based page number</param>
    /// <param name="left">Left edge 0-1</param>
    /// <param name="top">Top edge 0-1</param>
    /// <param name="right">Right edge 0-1</param>
    /// <param name="bottom">Bottom edge 0-1</param>
    /// <returns>New PDF bytes with cropped page</returns>
    public byte[] CropPage(byte[] pdfBytes, int pageNumber, double left, double top, double right, double bottom)
    {
        using var ms = new MemoryStream();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var writer = new PdfWriter(ms);
        using var doc = new PdfDocument(reader, writer);

        var page = doc.GetPage(pageNumber);
        var mediaBox = page.GetMediaBox();

        float pageW = mediaBox.GetWidth();
        float pageH = mediaBox.GetHeight();

        // Convert normalized coordinates to PDF points (origin = bottom-left)
        float newLeft = mediaBox.GetLeft() + (float)(left * pageW);
        float newBottom = mediaBox.GetBottom() + (float)((1 - bottom) * pageH);
        float newRight = mediaBox.GetLeft() + (float)(right * pageW);
        float newTop = mediaBox.GetBottom() + (float)((1 - top) * pageH);

        page.SetCropBox(new Rectangle(newLeft, newBottom, newRight - newLeft, newTop - newBottom));
        page.SetMediaBox(new Rectangle(newLeft, newBottom, newRight - newLeft, newTop - newBottom));

        doc.Close();
        return ms.ToArray();
    }

    /// <summary>
    /// Crops multiple pages with the same region
    /// </summary>
    public byte[] CropPages(byte[] pdfBytes, int[] pageNumbers, double left, double top, double right, double bottom)
    {
        var result = pdfBytes;
        // Apply to all specified pages in one document
        using var ms = new MemoryStream();
        using var reader = new PdfReader(new MemoryStream(result));
        using var writer = new PdfWriter(ms);
        using var doc = new PdfDocument(reader, writer);

        foreach (var pageNum in pageNumbers)
        {
            if (pageNum < 1 || pageNum > doc.GetNumberOfPages()) continue;

            var page = doc.GetPage(pageNum);
            var mediaBox = page.GetMediaBox();
            float pageW = mediaBox.GetWidth();
            float pageH = mediaBox.GetHeight();

            float newLeft = mediaBox.GetLeft() + (float)(left * pageW);
            float newBottom = mediaBox.GetBottom() + (float)((1 - bottom) * pageH);
            float newRight = mediaBox.GetLeft() + (float)(right * pageW);
            float newTop = mediaBox.GetBottom() + (float)((1 - top) * pageH);

            page.SetCropBox(new Rectangle(newLeft, newBottom, newRight - newLeft, newTop - newBottom));
            page.SetMediaBox(new Rectangle(newLeft, newBottom, newRight - newLeft, newTop - newBottom));
        }

        doc.Close();
        return ms.ToArray();
    }

    /// <summary>
    /// Adds margins/borders to all pages (in PDF points, 1 point = 1/72 inch)
    /// </summary>
    public byte[] AddMargins(byte[] pdfBytes, float marginLeft, float marginTop, float marginRight, float marginBottom)
    {
        using var ms = new MemoryStream();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var writer = new PdfWriter(ms);
        using var doc = new PdfDocument(reader, writer);

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);
            var mediaBox = page.GetMediaBox();

            var newBox = new Rectangle(
                mediaBox.GetLeft() - marginLeft,
                mediaBox.GetBottom() - marginBottom,
                mediaBox.GetWidth() + marginLeft + marginRight,
                mediaBox.GetHeight() + marginTop + marginBottom
            );
            page.SetMediaBox(newBox);
        }

        doc.Close();
        return ms.ToArray();
    }

    /// <summary>
    /// Resizes all pages to a standard size (e.g., A4, Letter)
    /// </summary>
    public byte[] ResizePages(byte[] pdfBytes, float targetWidth, float targetHeight)
    {
        using var ms = new MemoryStream();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var writer = new PdfWriter(ms);
        using var doc = new PdfDocument(reader, writer);

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);
            page.SetMediaBox(new Rectangle(0, 0, targetWidth, targetHeight));
        }

        doc.Close();
        return ms.ToArray();
    }

    // Standard page sizes in points (1 inch = 72 points)
    public static readonly (float w, float h) A4 = (595.28f, 841.89f);
    public static readonly (float w, float h) Letter = (612f, 792f);
    public static readonly (float w, float h) Legal = (612f, 1008f);
    public static readonly (float w, float h) A3 = (841.89f, 1190.55f);
}
