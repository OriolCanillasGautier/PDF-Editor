using Docnet.Core;
using Docnet.Core.Models;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Renders PDF pages to images using Docnet.Core (PDFium engine).
/// All Docnet calls are serialised through a static lock to prevent
/// the native PDFium library from being accessed concurrently (which
/// can cause AccessViolationException crashes).
/// </summary>
public class PdfRenderService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly object PdfiumLock = new();

    /// <summary>
    /// Renders a specific page of a PDF to BGRA pixel data
    /// </summary>
    /// <param name="pdfBytes">PDF file bytes</param>
    /// <param name="pageIndex">0-based page index</param>
    /// <param name="maxWidth">Maximum render width in pixels</param>
    /// <param name="maxHeight">Maximum render height in pixels</param>
    /// <returns>Tuple of (BGRA pixel data, actual width, actual height)</returns>
    public (byte[] pixels, int width, int height) RenderPage(
        byte[] pdfBytes, int pageIndex, int maxWidth = 1200, int maxHeight = 1600)
    {
        lock (PdfiumLock)
        {
            using var docReader = DocLib.Instance.GetDocReader(
                pdfBytes, new PageDimensions(maxWidth, maxHeight));
            using var pageReader = docReader.GetPageReader(pageIndex);

            var rawBytes = pageReader.GetImage();
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();

            // Blend transparent pixels with white background
            for (int i = 0; i < rawBytes.Length; i += 4)
            {
                byte a = rawBytes[i + 3];
                if (a < 255)
                {
                    float alpha = a / 255f;
                    float invAlpha = 1f - alpha;
                    rawBytes[i]     = (byte)(rawBytes[i]     * alpha + 255 * invAlpha); // B
                    rawBytes[i + 1] = (byte)(rawBytes[i + 1] * alpha + 255 * invAlpha); // G
                    rawBytes[i + 2] = (byte)(rawBytes[i + 2] * alpha + 255 * invAlpha); // R
                    rawBytes[i + 3] = 255;                                               // A
                }
            }

            return (rawBytes, width, height);
        }
    }

    /// <summary>
    /// Gets the number of pages in a PDF
    /// </summary>
    public int GetPageCount(byte[] pdfBytes)
    {
        lock (PdfiumLock)
        {
            using var docReader = DocLib.Instance.GetDocReader(
                pdfBytes, new PageDimensions(10, 10));
            return docReader.GetPageCount();
        }
    }

    /// <summary>
    /// Renders a small thumbnail preview of a page
    /// </summary>
    public (byte[] pixels, int width, int height) RenderThumbnail(
        byte[] pdfBytes, int pageIndex, int maxWidth = 150, int maxHeight = 200)
    {
        return RenderPage(pdfBytes, pageIndex, maxWidth, maxHeight);
    }
}
