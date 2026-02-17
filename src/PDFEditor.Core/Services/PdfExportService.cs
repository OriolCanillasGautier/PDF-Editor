using Docnet.Core;
using Docnet.Core.Models;
using ImageMagick;

namespace PDFEditor.Core.Services;

/// <summary>
/// Export PDF pages to various image formats and text
/// </summary>
public class PdfExportService
{
    private readonly PdfRenderService _renderService = new();

    /// <summary>
    /// Exports a single page to an image file
    /// </summary>
    public byte[] ExportPageToImage(byte[] pdfBytes, int pageIndex,
        string format = "PNG", int dpi = 150)
    {
        int scaledWidth = (int)(8.5 * dpi);   // approximate letter width
        int scaledHeight = (int)(11.0 * dpi);  // approximate letter height
        var (pixels, width, height) = _renderService.RenderPage(pdfBytes, pageIndex, scaledWidth, scaledHeight);

        // Use Magick.NET to convert BGRA pixels to the requested format
        using var image = new MagickImage();
        var settings = new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.BGRA);
        image.ReadPixels(pixels, settings);

        image.Format = format.ToUpperInvariant() switch
        {
            "JPEG" or "JPG" => MagickFormat.Jpeg,
            "TIFF" or "TIF" => MagickFormat.Tiff,
            "BMP" => MagickFormat.Bmp,
            "WEBP" => MagickFormat.WebP,
            _ => MagickFormat.Png
        };

        if (image.Format == MagickFormat.Jpeg)
        {
            image.Quality = 90;
        }

        var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Exports all pages to image files, returns list of (pageNum, imageBytes)
    /// </summary>
    public List<(int pageNumber, byte[] imageData)> ExportAllPagesToImages(
        byte[] pdfBytes, string format = "PNG", int dpi = 150,
        Action<int, int>? progressCallback = null)
    {
        int pageCount = _renderService.GetPageCount(pdfBytes);
        var results = new List<(int, byte[])>();

        for (int i = 0; i < pageCount; i++)
        {
            progressCallback?.Invoke(i + 1, pageCount);
            var imageBytes = ExportPageToImage(pdfBytes, i, format, dpi);
            results.Add((i + 1, imageBytes));
        }

        return results;
    }

    /// <summary>
    /// Exports all text from the PDF
    /// </summary>
    public string ExportAllText(byte[] pdfBytes)
    {
        var searchService = new PdfSearchService();
        return searchService.ExtractAllText(pdfBytes);
    }

    /// <summary>
    /// Generates an HTML document with each PDF page rendered as a base64 PNG image.
    /// This produces a visually accurate representation (not just text).
    /// </summary>
    public string ExportToHtml(byte[] pdfBytes, string title = "Exported PDF")
    {
        var pdfOps = new PdfOperations();
        int pageCount = pdfOps.GetPageCount(pdfBytes);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head>");
        sb.AppendLine($"<title>{System.Net.WebUtility.HtmlEncode(title)}</title>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<style>");
        sb.AppendLine("  body { font-family: 'Segoe UI', Arial, sans-serif; max-width: 900px; margin: 0 auto; padding: 20px; background: #f0f0f0; }");
        sb.AppendLine("  h1 { text-align: center; color: #333; margin-bottom: 4px; }");
        sb.AppendLine("  .info { text-align: center; color: #666; margin-bottom: 20px; }");
        sb.AppendLine("  .page { background: white; margin: 20px auto; box-shadow: 0 2px 8px rgba(0,0,0,0.15); overflow: hidden; }");
        sb.AppendLine("  .page img { width: 100%; height: auto; display: block; }");
        sb.AppendLine("  .page-label { background: #333; color: white; padding: 6px 12px; font-size: 13px; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine($"<h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>");
        sb.AppendLine($"<p class=\"info\">{pageCount} page{(pageCount != 1 ? "s" : "")}</p>");

        for (int i = 0; i < pageCount; i++)
        {
            var imageBytes = ExportPageToImage(pdfBytes, i, "PNG", 150);
            var base64 = Convert.ToBase64String(imageBytes);

            sb.AppendLine("<div class=\"page\">");
            sb.AppendLine($"  <div class=\"page-label\">Page {i + 1} of {pageCount}</div>");
            sb.AppendLine($"  <img src=\"data:image/png;base64,{base64}\" alt=\"Page {i + 1}\" />");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Creates a PDF from a list of image files
    /// </summary>
    public byte[] CreatePdfFromImages(List<byte[]> imageDataList)
    {
        var outputMs = new MemoryStream();
        var writer = new iText.Kernel.Pdf.PdfWriter(outputMs);
        var doc = new iText.Kernel.Pdf.PdfDocument(writer);
        var document = new iText.Layout.Document(doc);

        foreach (var imageData in imageDataList)
        {
            var imageObj = iText.IO.Image.ImageDataFactory.Create(imageData);
            var pdfImage = new iText.Layout.Element.Image(imageObj);

            // Scale to fit page
            var pageSize = iText.Kernel.Geom.PageSize.A4;
            pdfImage.ScaleToFit(pageSize.GetWidth() - 72, pageSize.GetHeight() - 72);
            pdfImage.SetFixedPosition(
                (pageSize.GetWidth() - pdfImage.GetImageScaledWidth()) / 2,
                (pageSize.GetHeight() - pdfImage.GetImageScaledHeight()) / 2);

            doc.AddNewPage(pageSize);
            document.Add(pdfImage);
        }

        document.Close();
        return outputMs.ToArray();
    }
}
