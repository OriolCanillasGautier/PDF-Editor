using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Utils;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Arranges PDF pages into booklet format for double-sided printing.
/// Uses 2-up imposition: two logical pages per physical sheet, ordered so that
/// folding the printed stack produces a correctly-sequenced booklet.
/// </summary>
public class PdfBookletService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Page size for the output sheet (two logical pages side by side).
    /// </summary>
    public enum SheetSize
    {
        /// <summary>A4 (297 × 210 mm) — each half is A5</summary>
        A4Landscape,
        /// <summary>Letter (11 × 8.5 in) — each half is half-letter</summary>
        LetterLandscape,
        /// <summary>A3 (420 × 297 mm) — each half is A4</summary>
        A3Landscape,
        /// <summary>Tabloid (17 × 11 in) — each half is letter</summary>
        TabloidLandscape
    }

    /// <summary>
    /// Options for booklet generation.
    /// </summary>
    public class BookletOptions
    {
        /// <summary>
        /// Target sheet size for the booklet output.
        /// </summary>
        public SheetSize Sheet { get; set; } = SheetSize.A4Landscape;

        /// <summary>
        /// Whether to add blank pages at the end so total page count is a multiple of 4.
        /// Required for proper booklet folding. Defaults to true.
        /// </summary>
        public bool PadToMultipleOf4 { get; set; } = true;

        /// <summary>
        /// Binding margin in points added between the two pages on each sheet.
        /// Defaults to 18pt (~6mm).
        /// </summary>
        public float BindingMarginPt { get; set; } = 18f;
    }

    /// <summary>
    /// Creates a booklet PDF from the source document.
    /// Pages are imposed in saddle-stitch order: for a 4-page document the
    /// sheet order is (4,1) front and (2,3) back.
    /// </summary>
    /// <param name="pdfBytes">Source PDF bytes</param>
    /// <param name="options">Booklet configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Booklet PDF as bytes</returns>
    public async Task<byte[]> CreateBookletAsync(byte[] pdfBytes, BookletOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new BookletOptions();

        return await Task.Run(() => CreateBooklet(pdfBytes, options, cancellationToken), cancellationToken);
    }

    private byte[] CreateBooklet(byte[] pdfBytes, BookletOptions options, CancellationToken ct)
    {
        using var srcMs = new MemoryStream(pdfBytes, writable: false);
        using var reader = new PdfReader(srcMs);
        using var srcDoc = new PdfDocument(reader);

        int pageCount = srcDoc.GetNumberOfPages();
        Log.Info("Creating booklet from {PageCount} pages", pageCount);

        // Pad to multiple of 4
        int padded = pageCount;
        if (options.PadToMultipleOf4 && padded % 4 != 0)
            padded += 4 - (padded % 4);

        // Compute saddle-stitch page order
        // For a booklet with N pages (multiple of 4):
        //   Sheet 1 front: (N, 1)   Sheet 1 back: (2, N-1)
        //   Sheet 2 front: (N-2, 3) Sheet 2 back: (4, N-3)  etc.
        var sheets = new List<(int left, int right)>();
        for (int i = 0; i < padded; i += 4)
        {
            ct.ThrowIfCancellationRequested();
            sheets.Add((padded - i, i + 1));         // front of sheet (left=last, right=first)
            sheets.Add((i + 2, padded - i - 1));     // back of sheet  (left=next, right=prev)
        }

        // Get sheet dimensions
        var (sheetWidth, sheetHeight) = GetSheetDimensions(options.Sheet);
        float halfWidth = (sheetWidth - options.BindingMarginPt) / 2f;

        using var outMs = new MemoryStream();
        using var writer = new PdfWriter(outMs);
        using var outDoc = new PdfDocument(writer);

        foreach (var (left, right) in sheets)
        {
            ct.ThrowIfCancellationRequested();

            var sheetPage = outDoc.AddNewPage(new PageSize(sheetWidth, sheetHeight));
            var canvas = new PdfCanvas(sheetPage);

            // Left half
            if (left >= 1 && left <= pageCount)
            {
                PlacePage(canvas, srcDoc, left, 0, 0, halfWidth, sheetHeight);
            }

            // Right half
            if (right >= 1 && right <= pageCount)
            {
                float rightX = halfWidth + options.BindingMarginPt;
                PlacePage(canvas, srcDoc, right, rightX, 0, halfWidth, sheetHeight);
            }
        }

        outDoc.Close();
        Log.Info("Booklet created: {Sheets} sheets from {Pages} pages", sheets.Count, pageCount);
        return outMs.ToArray();
    }

    /// <summary>
    /// Places a source page scaled to fit within the given rectangle on the output canvas.
    /// </summary>
    private static void PlacePage(PdfCanvas canvas, PdfDocument srcDoc, int pageNum,
        float x, float y, float width, float height)
    {
        var srcPage = srcDoc.GetPage(pageNum);
        var srcBox = srcPage.GetMediaBox();

        float srcW = srcBox.GetWidth();
        float srcH = srcBox.GetHeight();

        // Compute scale to fit within the target rectangle
        float scaleX = width / srcW;
        float scaleY = height / srcH;
        float scale = Math.Min(scaleX, scaleY);

        // Center within the rectangle
        float scaledW = srcW * scale;
        float scaledH = srcH * scale;
        float offsetX = x + (width - scaledW) / 2f;
        float offsetY = y + (height - scaledH) / 2f;

        // Copy page as form XObject and place it
        var formXObj = srcPage.CopyAsFormXObject(canvas.GetDocument());

        canvas.SaveState();
        canvas.ConcatMatrix(scale, 0, 0, scale, offsetX, offsetY);
        canvas.AddXObjectAt(formXObj, 0, 0);
        canvas.RestoreState();
    }

    /// <summary>
    /// Returns (width, height) in PDF points for the given sheet size in landscape orientation.
    /// </summary>
    private static (float width, float height) GetSheetDimensions(SheetSize sheet)
    {
        return sheet switch
        {
            SheetSize.A4Landscape      => (PageSize.A4.Rotate().GetWidth(), PageSize.A4.Rotate().GetHeight()),
            SheetSize.LetterLandscape  => (PageSize.LETTER.Rotate().GetWidth(), PageSize.LETTER.Rotate().GetHeight()),
            SheetSize.A3Landscape      => (PageSize.A3.Rotate().GetWidth(), PageSize.A3.Rotate().GetHeight()),
            SheetSize.TabloidLandscape => (PageSize.TABLOID.Rotate().GetWidth(), PageSize.TABLOID.Rotate().GetHeight()),
            _ => (PageSize.A4.Rotate().GetWidth(), PageSize.A4.Rotate().GetHeight())
        };
    }

    /// <summary>
    /// Calculates the number of sheets and blank pages needed for the booklet.
    /// Useful for showing the user a preview of the booklet layout.
    /// </summary>
    /// <param name="pageCount">Number of pages in the source document</param>
    /// <returns>Tuple of (sheets, totalPages including padding, blankPages)</returns>
    public static (int sheets, int totalPages, int blankPages) CalculateBookletInfo(int pageCount)
    {
        int padded = pageCount;
        if (padded % 4 != 0)
            padded += 4 - (padded % 4);

        int blanks = padded - pageCount;
        int sheets = padded / 2; // each physical sheet holds 2 logical pages per side = 4 total

        return (sheets, padded, blanks);
    }
}
