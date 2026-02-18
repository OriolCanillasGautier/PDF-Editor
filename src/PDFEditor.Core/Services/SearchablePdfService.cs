using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Font;
using iText.IO.Font.Constants;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Service for creating searchable PDFs by adding an invisible text layer
/// on top of scanned/image-based PDF pages using OCR results.
/// </summary>
public class SearchablePdfService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly TesseractOcrService _ocrService;

    public SearchablePdfService(TesseractOcrService ocrService)
    {
        _ocrService = ocrService;
    }

    /// <summary>
    /// Creates a searchable PDF by overlaying invisible OCR text on each page.
    /// The visual appearance of the PDF remains unchanged, but text becomes selectable
    /// and searchable.
    /// </summary>
    /// <param name="pdfBytes">Source PDF bytes (typically scanned/image-based)</param>
    /// <param name="language">Tesseract language code (e.g., "eng", "spa")</param>
    /// <param name="dpi">DPI for rendering pages before OCR (higher = more accurate, slower)</param>
    /// <param name="progress">Optional progress reporter (current page, total pages)</param>
    /// <returns>New PDF bytes with invisible text layer</returns>
    public async Task<byte[]> MakeSearchableAsync(
        byte[] pdfBytes,
        string language = "eng",
        int dpi = 300,
        IProgress<(int current, int total)>? progress = null)
    {
        Log.Info("Creating searchable PDF. Language: {Lang}, DPI: {Dpi}", language, dpi);

        using var inputStream = new MemoryStream(pdfBytes);
        using var outputStream = new MemoryStream();
        using var reader = new PdfReader(inputStream);
        using var writer = new PdfWriter(outputStream);
        using var pdfDoc = new PdfDocument(reader, writer);

        var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        int pageCount = pdfDoc.GetNumberOfPages();
        int pagesProcessed = 0;

        for (int i = 1; i <= pageCount; i++)
        {
            progress?.Report((i, pageCount));
            var page = pdfDoc.GetPage(i);
            var pageSize = page.GetPageSize();

            try
            {
                // Render page to image for OCR
                var renderService = new PdfRenderService();
                int scaledWidth = (int)(pageSize.GetWidth() / 72.0 * dpi);
                int scaledHeight = (int)(pageSize.GetHeight() / 72.0 * dpi);

                var (pixels, width, height) = renderService.RenderPage(pdfBytes, i - 1, scaledWidth, scaledHeight);

                // Convert BGRA pixels to PNG for Tesseract
                using var image = new ImageMagick.MagickImage();
                var settings = new ImageMagick.PixelReadSettings(
                    (uint)width, (uint)height,
                    ImageMagick.StorageType.Char,
                    ImageMagick.PixelMapping.BGRA);
                image.ReadPixels(pixels, settings);
                image.Format = ImageMagick.MagickFormat.Png;

                using var pngStream = new MemoryStream();
                image.Write(pngStream);
                var pngBytes = pngStream.ToArray();

                // Get word-level OCR results with bounding boxes
                var ocrResults = await _ocrService.RecognizeTextRegions(pngBytes, language);

                if (ocrResults.Count == 0)
                {
                    Log.Debug("Page {Page}: No OCR text found", i);
                    continue;
                }

                // Add invisible text layer
                var canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdfDoc);

                // Scale factors: convert from image pixel coordinates to PDF points
                float scaleX = pageSize.GetWidth() / width;
                float scaleY = pageSize.GetHeight() / height;

                canvas.SaveState();
                // Set text rendering mode to invisible (mode 3)
                canvas.SetTextRenderingMode(PdfCanvasConstants.TextRenderingMode.INVISIBLE);

                foreach (var result in ocrResults)
                {
                    if (string.IsNullOrWhiteSpace(result.Text) || result.Confidence < 0.3f)
                        continue;

                    // Convert image coordinates to PDF coordinates
                    // PDF origin is bottom-left; image origin is top-left
                    float pdfX = result.BoundingBox.x * scaleX;
                    float pdfY = pageSize.GetHeight() - ((result.BoundingBox.y + result.BoundingBox.height) * scaleY);
                    float wordWidth = result.BoundingBox.width * scaleX;
                    float wordHeight = result.BoundingBox.height * scaleY;

                    // Calculate font size to roughly match the word bounding box height
                    float fontSize = Math.Max(1f, wordHeight * 0.85f);

                    try
                    {
                        canvas.BeginText()
                            .SetFontAndSize(font, fontSize)
                            .MoveText(pdfX, pdfY)
                            .ShowText(result.Text)
                            .EndText();
                    }
                    catch (Exception ex)
                    {
                        // Some characters may not be encodable in Helvetica — skip those
                        Log.Trace("Could not encode word '{Word}' on page {Page}: {Error}",
                            result.Text, i, ex.Message);
                    }
                }

                canvas.RestoreState();
                pagesProcessed++;
                Log.Debug("Page {Page}: Added {Count} invisible text fragments", i, ocrResults.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to OCR page {Page}", i);
                // Continue to next page — don't fail the entire document
            }
        }

        pdfDoc.Close();
        Log.Info("Searchable PDF created. {Processed}/{Total} pages processed", pagesProcessed, pageCount);
        return outputStream.ToArray();
    }

    /// <summary>
    /// Checks whether a page appears to be scanned/image-based (no selectable text).
    /// </summary>
    public bool IsPageImageBased(byte[] pdfBytes, int pageIndex)
    {
        try
        {
            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var doc = new PdfDocument(reader);

            if (pageIndex < 0 || pageIndex >= doc.GetNumberOfPages())
                return false;

            var page = doc.GetPage(pageIndex + 1);
            var strategy = new iText.Kernel.Pdf.Canvas.Parser.Listener.SimpleTextExtractionStrategy();
            var text = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(page, strategy);

            return string.IsNullOrWhiteSpace(text);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check page {Page} text content", pageIndex);
            return false;
        }
    }

    /// <summary>
    /// Counts how many pages in a document are image-based (no text layer).
    /// </summary>
    public (int imageBased, int total) CountImageBasedPages(byte[] pdfBytes)
    {
        try
        {
            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var doc = new PdfDocument(reader);

            int total = doc.GetNumberOfPages();
            int imageBased = 0;

            for (int i = 1; i <= total; i++)
            {
                var page = doc.GetPage(i);
                var strategy = new iText.Kernel.Pdf.Canvas.Parser.Listener.SimpleTextExtractionStrategy();
                var text = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(page, strategy);

                if (string.IsNullOrWhiteSpace(text))
                    imageBased++;
            }

            return (imageBased, total);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to count image-based pages");
            return (0, 0);
        }
    }
}
