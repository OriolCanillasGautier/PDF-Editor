using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Font;
using iText.Kernel.Colors;
using iText.IO.Font.Constants;
using iText.Kernel.Pdf.Extgstate;

namespace PDFEditor.Core.Services;

/// <summary>
/// Adds text or diagonal watermarks to PDF pages
/// </summary>
public class PdfWatermarkService
{
    /// <summary>
    /// Adds a diagonal text watermark to all pages
    /// </summary>
    public byte[] AddTextWatermark(byte[] pdfBytes, string text,
        float fontSize = 60f, float opacity = 0.3f, float rotation = 45f)
    {
        return AddTextWatermarkToPages(pdfBytes, text, null, fontSize, opacity, rotation);
    }

    /// <summary>
    /// Adds a text watermark to specific pages (null = all pages)
    /// </summary>
    public byte[] AddTextWatermarkToPages(byte[] pdfBytes, string text,
        int[]? pageNumbers = null, float fontSize = 60f, float opacity = 0.3f,
        float rotation = 45f)
    {
        var outputMs = new MemoryStream();
        using (var reader = new PdfReader(new MemoryStream(pdfBytes)))
        using (var writer = new PdfWriter(outputMs))
        {
            var doc = new PdfDocument(reader, writer);
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var grayColor = new DeviceRgb(128, 128, 128);

            var gs = new PdfExtGState().SetFillOpacity(opacity);

            for (int i = 1; i <= doc.GetNumberOfPages(); i++)
            {
                if (pageNumbers != null && !pageNumbers.Contains(i))
                    continue;

                var page = doc.GetPage(i);
                var mediaBox = page.GetMediaBox();
                float pageWidth = mediaBox.GetWidth();
                float pageHeight = mediaBox.GetHeight();

                var canvas = new PdfCanvas(page);
                canvas.SaveState();
                canvas.SetExtGState(gs);

                // Position at center, rotated
                float centerX = pageWidth / 2f;
                float centerY = pageHeight / 2f;
                float radians = rotation * (float)Math.PI / 180f;

                canvas.BeginText();
                canvas.SetFontAndSize(font, fontSize);
                canvas.SetColor(grayColor, true);
                canvas.SetTextMatrix(
                    (float)Math.Cos(radians), (float)Math.Sin(radians),
                    -(float)Math.Sin(radians), (float)Math.Cos(radians),
                    centerX - fontSize * text.Length * 0.15f * (float)Math.Cos(radians),
                    centerY - fontSize * text.Length * 0.15f * (float)Math.Sin(radians));
                canvas.ShowText(text);
                canvas.EndText();

                canvas.RestoreState();
            }

            doc.Close();
        }
        return outputMs.ToArray();
    }

    /// <summary>
    /// Adds a header/footer text to all pages
    /// </summary>
    public byte[] AddHeaderFooter(byte[] pdfBytes, string? headerText, string? footerText,
        float fontSize = 10f)
    {
        var outputMs = new MemoryStream();
        using (var reader = new PdfReader(new MemoryStream(pdfBytes)))
        using (var writer = new PdfWriter(outputMs))
        {
            var doc = new PdfDocument(reader, writer);
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var color = new DeviceRgb(100, 100, 100);

            for (int i = 1; i <= doc.GetNumberOfPages(); i++)
            {
                var page = doc.GetPage(i);
                var mediaBox = page.GetMediaBox();
                var canvas = new PdfCanvas(page);

                canvas.BeginText();
                canvas.SetFontAndSize(font, fontSize);
                canvas.SetColor(color, true);

                if (!string.IsNullOrEmpty(headerText))
                {
                    string header = headerText.Replace("{page}", i.ToString())
                        .Replace("{pages}", doc.GetNumberOfPages().ToString());
                    canvas.SetTextMatrix(36, mediaBox.GetHeight() - 20);
                    canvas.ShowText(header);
                }

                if (!string.IsNullOrEmpty(footerText))
                {
                    string footer = footerText.Replace("{page}", i.ToString())
                        .Replace("{pages}", doc.GetNumberOfPages().ToString());
                    canvas.SetTextMatrix(36, 15);
                    canvas.ShowText(footer);
                }

                canvas.EndText();
            }

            doc.Close();
        }
        return outputMs.ToArray();
    }

    /// <summary>
    /// Adds page numbers to all pages
    /// </summary>
    public byte[] AddPageNumbers(byte[] pdfBytes, string format = "Page {page} of {pages}",
        bool atBottom = true, float fontSize = 9f)
    {
        return AddHeaderFooter(pdfBytes,
            atBottom ? null : format,
            atBottom ? format : null,
            fontSize);
    }
}
