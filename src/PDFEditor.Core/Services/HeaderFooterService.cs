using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Adds, updates, or removes headers and footers from PDF documents.
/// Supports page numbers, dates, custom text, font/size/alignment, and
/// separate even/odd page configurations.
/// </summary>
public class HeaderFooterService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Horizontal alignment for header/footer text.
    /// </summary>
    public enum HFAlignment
    {
        Left,
        Center,
        Right
    }

    /// <summary>
    /// Defines the content and style for a header or footer element.
    /// </summary>
    public class HFElement
    {
        /// <summary>
        /// Text template. Use placeholders:
        /// {page} — current page number,
        /// {total} — total page count,
        /// {date} — current date (yyyy-MM-dd),
        /// {title} — document title.
        /// </summary>
        public string Template { get; set; } = string.Empty;

        /// <summary>
        /// Horizontal alignment of the text.
        /// </summary>
        public HFAlignment Alignment { get; set; } = HFAlignment.Center;

        /// <summary>
        /// Font size in points. Default 9pt.
        /// </summary>
        public float FontSize { get; set; } = 9f;

        /// <summary>
        /// Font name. Use "Helvetica", "Times-Roman", "Courier", etc.
        /// </summary>
        public string FontName { get; set; } = "Helvetica";

        /// <summary>
        /// Whether to use bold. Default false.
        /// </summary>
        public bool Bold { get; set; }

        /// <summary>
        /// Whether to use italic. Default false.
        /// </summary>
        public bool Italic { get; set; }

        /// <summary>
        /// Text color as hex string (e.g., "333333"). Default grey.
        /// </summary>
        public string ColorHex { get; set; } = "666666";
    }

    /// <summary>
    /// Configuration for adding headers and footers.
    /// </summary>
    public class HFOptions
    {
        /// <summary>
        /// Header element (null to skip header).
        /// </summary>
        public HFElement? Header { get; set; }

        /// <summary>
        /// Footer element (null to skip footer).
        /// </summary>
        public HFElement? Footer { get; set; }

        /// <summary>
        /// Optional separate header for even pages (for alternating alignment in duplex printing).
        /// If null, the main Header is used for all pages.
        /// </summary>
        public HFElement? HeaderEven { get; set; }

        /// <summary>
        /// Optional separate footer for even pages.
        /// If null, the main Footer is used for all pages.
        /// </summary>
        public HFElement? FooterEven { get; set; }

        /// <summary>
        /// Margin from page edge in points for the header. Default 36pt (~0.5in).
        /// </summary>
        public float HeaderMarginPt { get; set; } = 36f;

        /// <summary>
        /// Margin from page edge in points for the footer. Default 36pt (~0.5in).
        /// </summary>
        public float FooterMarginPt { get; set; } = 36f;

        /// <summary>
        /// Left/right margin inset in points. Default 54pt (~0.75in).
        /// </summary>
        public float SideMarginPt { get; set; } = 54f;

        /// <summary>
        /// Whether to draw a thin separator line between header/footer and content.
        /// </summary>
        public bool DrawSeparatorLine { get; set; }

        /// <summary>
        /// First page number (allows starting from a number other than 1).
        /// </summary>
        public int StartPageNumber { get; set; } = 1;

        /// <summary>
        /// Skip header/footer on the first page. Default false.
        /// </summary>
        public bool SkipFirstPage { get; set; }

        /// <summary>
        /// Specific page indices (0-based) to apply to. If null, applies to all pages.
        /// </summary>
        public int[]? PageIndices { get; set; }
    }

    /// <summary>
    /// Adds headers and/or footers to a PDF document.
    /// </summary>
    /// <param name="pdfBytes">Source PDF bytes</param>
    /// <param name="options">Header/footer configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Modified PDF bytes with headers/footers added</returns>
    public async Task<byte[]> AddHeaderFooterAsync(byte[] pdfBytes, HFOptions options,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => AddHeaderFooter(pdfBytes, options, cancellationToken), cancellationToken);
    }

    private byte[] AddHeaderFooter(byte[] pdfBytes, HFOptions options, CancellationToken ct)
    {
        if (options.Header == null && options.Footer == null)
        {
            Log.Warn("No header or footer specified — returning original PDF");
            return pdfBytes;
        }

        using var srcMs = new MemoryStream(pdfBytes, writable: false);
        using var outMs = new MemoryStream();

        using var reader = new PdfReader(srcMs);
        using var writer = new PdfWriter(outMs);
        using var doc = new PdfDocument(reader, writer);

        int totalPages = doc.GetNumberOfPages();
        string? docTitle = doc.GetDocumentInfo()?.GetTitle();

        var pageSet = options.PageIndices != null
            ? new HashSet<int>(options.PageIndices)
            : null;

        for (int i = 1; i <= totalPages; i++)
        {
            ct.ThrowIfCancellationRequested();

            int zeroIdx = i - 1;

            // Skip if not in page set
            if (pageSet != null && !pageSet.Contains(zeroIdx)) continue;

            // Skip first page if requested
            if (options.SkipFirstPage && i == 1) continue;

            int displayPageNum = options.StartPageNumber + zeroIdx;
            bool isEven = (i % 2 == 0);

            var page = doc.GetPage(i);
            var mediaBox = page.GetMediaBox();
            float pageWidth = mediaBox.GetWidth();
            float pageHeight = mediaBox.GetHeight();

            var canvas = new PdfCanvas(page);

            // Resolve text for placeholders
            string ResolveTemplate(string template) => template
                .Replace("{page}", displayPageNum.ToString())
                .Replace("{total}", totalPages.ToString())
                .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"))
                .Replace("{title}", docTitle ?? "");

            // Header
            var header = (isEven && options.HeaderEven != null) ? options.HeaderEven : options.Header;
            if (header != null && !string.IsNullOrWhiteSpace(header.Template))
            {
                string text = ResolveTemplate(header.Template);
                float y = pageHeight - options.HeaderMarginPt;
                DrawText(canvas, text, header, options.SideMarginPt, y, pageWidth, options);

                if (options.DrawSeparatorLine)
                {
                    float lineY = y - header.FontSize - 4f;
                    DrawLine(canvas, options.SideMarginPt, lineY,
                        pageWidth - options.SideMarginPt, lineY, header.ColorHex);
                }
            }

            // Footer
            var footer = (isEven && options.FooterEven != null) ? options.FooterEven : options.Footer;
            if (footer != null && !string.IsNullOrWhiteSpace(footer.Template))
            {
                string text = ResolveTemplate(footer.Template);
                float y = options.FooterMarginPt;
                DrawText(canvas, text, footer, options.SideMarginPt, y, pageWidth, options);

                if (options.DrawSeparatorLine)
                {
                    float lineY = y + footer.FontSize + 4f;
                    DrawLine(canvas, options.SideMarginPt, lineY,
                        pageWidth - options.SideMarginPt, lineY, footer.ColorHex);
                }
            }
        }

        doc.Close();
        Log.Info("Added headers/footers to {Pages} pages", totalPages);
        return outMs.ToArray();
    }

    /// <summary>
    /// Removes header and footer content from margins by overlaying white rectangles.
    /// This is an approximation — it covers the margin areas without modifying page content streams.
    /// </summary>
    /// <param name="pdfBytes">Source PDF bytes</param>
    /// <param name="marginPt">Margin size in points to clear (default 50pt)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Modified PDF bytes</returns>
    public async Task<byte[]> RemoveHeaderFooterAsync(byte[] pdfBytes, float marginPt = 50f,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            using var srcMs = new MemoryStream(pdfBytes, writable: false);
            using var outMs = new MemoryStream();
            using var reader = new PdfReader(srcMs);
            using var writer = new PdfWriter(outMs);
            using var doc = new PdfDocument(reader, writer);

            for (int i = 1; i <= doc.GetNumberOfPages(); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = doc.GetPage(i);
                var box = page.GetMediaBox();
                float w = box.GetWidth();
                float h = box.GetHeight();
                var canvas = new PdfCanvas(page);

                // White rectangle at top (header region)
                canvas.SaveState()
                    .SetFillColor(ColorConstants.WHITE)
                    .Rectangle(0, h - marginPt, w, marginPt)
                    .Fill()
                    .RestoreState();

                // White rectangle at bottom (footer region)
                canvas.SaveState()
                    .SetFillColor(ColorConstants.WHITE)
                    .Rectangle(0, 0, w, marginPt)
                    .Fill()
                    .RestoreState();
            }

            doc.Close();
            Log.Info("Removed headers/footers (cleared {Margin}pt margins)", marginPt);
            return outMs.ToArray();
        }, cancellationToken);
    }

    #region Drawing Helpers

    private static void DrawText(PdfCanvas canvas, string text, HFElement element,
        float sideMargin, float y, float pageWidth, HFOptions options)
    {
        var font = ResolveFont(element.FontName, element.Bold, element.Italic);
        var color = ParseColor(element.ColorHex);
        float textWidth = font.GetWidth(text, element.FontSize);

        float x = element.Alignment switch
        {
            HFAlignment.Left => sideMargin,
            HFAlignment.Right => pageWidth - sideMargin - textWidth,
            HFAlignment.Center => (pageWidth - textWidth) / 2f,
            _ => sideMargin
        };

        canvas.SaveState();
        canvas.BeginText()
            .SetFontAndSize(font, element.FontSize)
            .SetColor(color, true)
            .MoveText(x, y)
            .ShowText(text)
            .EndText();
        canvas.RestoreState();
    }

    private static void DrawLine(PdfCanvas canvas, float x1, float y1, float x2, float y2, string colorHex)
    {
        var color = ParseColor(colorHex);
        canvas.SaveState()
            .SetStrokeColor(color)
            .SetLineWidth(0.5f)
            .MoveTo(x1, y1)
            .LineTo(x2, y2)
            .Stroke()
            .RestoreState();
    }

    private static PdfFont ResolveFont(string fontName, bool bold, bool italic)
    {
        try
        {
            string baseName = fontName.ToLowerInvariant() switch
            {
                "times" or "times-roman" or "times new roman" =>
                    bold && italic ? StandardFonts.TIMES_BOLDITALIC :
                    bold ? StandardFonts.TIMES_BOLD :
                    italic ? StandardFonts.TIMES_ITALIC :
                    StandardFonts.TIMES_ROMAN,
                "courier" or "courier new" =>
                    bold && italic ? StandardFonts.COURIER_BOLDOBLIQUE :
                    bold ? StandardFonts.COURIER_BOLD :
                    italic ? StandardFonts.COURIER_OBLIQUE :
                    StandardFonts.COURIER,
                _ => // Helvetica (default)
                    bold && italic ? StandardFonts.HELVETICA_BOLDOBLIQUE :
                    bold ? StandardFonts.HELVETICA_BOLD :
                    italic ? StandardFonts.HELVETICA_OBLIQUE :
                    StandardFonts.HELVETICA
            };

            return PdfFontFactory.CreateFont(baseName);
        }
        catch
        {
            return PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        }
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            if (hex.StartsWith("#")) hex = hex[1..];
            if (hex.Length != 6) return new DeviceRgb(102, 102, 102);

            int r = Convert.ToInt32(hex[..2], 16);
            int g = Convert.ToInt32(hex[2..4], 16);
            int b = Convert.ToInt32(hex[4..6], 16);
            return new DeviceRgb(r, g, b);
        }
        catch
        {
            return new DeviceRgb(102, 102, 102); // fallback grey
        }
    }

    #endregion
}
