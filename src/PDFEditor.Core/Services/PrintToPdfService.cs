using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Produces print-ready PDF output from existing PDF documents.
/// Features:
///  - Page normalization (resize pages to target paper size with fit-to-page scaling)
///  - Linearization (fast-web-view / print-spooler optimization)
///  - Margin injection
///  - Page subset selection
/// </summary>
public class PrintToPdfService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // Standard paper sizes in points (1 pt = 1/72 inch)
    public static readonly PageSize A4  = PageSize.A4;
    public static readonly PageSize A3  = PageSize.A3;
    public static readonly PageSize Letter = PageSize.LETTER;
    public static readonly PageSize Legal  = PageSize.LEGAL;

    /// <summary>
    /// Produces a print-ready PDF, optionally normalizing page sizes and linearizing.
    /// </summary>
    public async Task<PrintResult> PrintAsync(byte[] pdfBytes, PrintOptions options)
    {
        Log.Info("Print-to-PDF started — {Bytes} bytes, target={Target}", pdfBytes.Length, options.TargetPageSize?.ToString() ?? "original");

        return await Task.Run(() =>
        {
            try
            {
                using var inMs = new MemoryStream(pdfBytes, writable: false);
                using var outMs = new MemoryStream();

                var writerProps = new WriterProperties();
                if (options.Linearize)
                    writerProps.SetFullCompressionMode(true);

                using var reader = new PdfReader(inMs);
                using var writer = new PdfWriter(outMs, writerProps);
                using var srcDoc = new PdfDocument(reader);
                using var dstDoc = new PdfDocument(writer);

                int total = srcDoc.GetNumberOfPages();
                int[]? indices = options.PageIndices;
                if (indices == null || indices.Length == 0)
                    indices = Enumerable.Range(1, total).ToArray(); // 1-based for iText copying

                foreach (int pageNum in indices)
                {
                    if (pageNum < 1 || pageNum > total) continue;

                    var srcPage   = srcDoc.GetPage(pageNum);
                    var mediaBox  = srcPage.GetMediaBox();
                    var srcWidth  = mediaBox.GetWidth();
                    var srcHeight = mediaBox.GetHeight();

                    PageSize targetSize = options.TargetPageSize ?? new PageSize(mediaBox);

                    if (options.FitToPage && options.TargetPageSize != null)
                    {
                        // Render source page into target page with fit-to-page scaling
                        RenderPageWithScale(srcDoc, srcPage, dstDoc, targetSize,
                            srcWidth, srcHeight, options.MarginPt);
                    }
                    else
                    {
                        // Copy page verbatim (may change page size if different)
                        srcDoc.CopyPagesTo(pageNum, pageNum, dstDoc);

                        if (options.TargetPageSize != null)
                        {
                            var dstPage = dstDoc.GetPage(dstDoc.GetNumberOfPages());
                            dstPage.SetMediaBox(new Rectangle(
                                targetSize.GetX(), targetSize.GetY(),
                                targetSize.GetWidth(), targetSize.GetHeight()));
                        }
                    }
                }

                dstDoc.Close();

                byte[] result = outMs.ToArray();
                Log.Info("Print-to-PDF complete — {Pages} pages, {Bytes} bytes", indices.Length, result.Length);
                return PrintResult.Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Print-to-PDF failed");
                return PrintResult.Fail($"Print-to-PDF failed: {ex.Message}");
            }
        });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static void RenderPageWithScale(
        PdfDocument srcDoc, PdfPage srcPage,
        PdfDocument dstDoc, PageSize targetSize,
        float srcW, float srcH, float marginPt)
    {
        float availW = targetSize.GetWidth()  - 2 * marginPt;
        float availH = targetSize.GetHeight() - 2 * marginPt;

        float scaleX = availW / srcW;
        float scaleY = availH / srcH;
        float scale  = Math.Min(scaleX, scaleY);

        float scaledW = srcW * scale;
        float scaledH = srcH * scale;

        // Center on target page
        float offsetX = (targetSize.GetWidth()  - scaledW) / 2f;
        float offsetY = (targetSize.GetHeight() - scaledH) / 2f;

        int srcPageNum = srcDoc.GetPageNumber(srcPage);

        var dstPage = dstDoc.AddNewPage(targetSize);
        var canvas  = new PdfCanvas(dstPage);

        // Use XObject form of source page
        var xObj = srcPage.CopyAsFormXObject(dstDoc);
        canvas.SaveState()
              .ConcatMatrix(scale, 0, 0, scale, offsetX, offsetY)
              .AddXObjectAt(xObj, 0, 0)
              .RestoreState();
        canvas.Release();
    }
}

/// <summary>Options for the print-to-PDF operation.</summary>
public class PrintOptions
{
    /// <summary>Target paper size. Null = keep original page sizes.</summary>
    public PageSize? TargetPageSize { get; set; }

    /// <summary>When true and TargetPageSize is set, scales page content to fit the target.</summary>
    public bool FitToPage { get; set; } = true;

    /// <summary>Margin in points around the page (when FitToPage is true). Default: 36 pt = 0.5 inch.</summary>
    public float MarginPt { get; set; } = 36f;

    /// <summary>Page numbers to include (1-based). Null = all pages.</summary>
    public int[]? PageIndices { get; set; }

    /// <summary>Enable full compression / linearization for faster print spooling.</summary>
    public bool Linearize { get; set; } = false;
}

/// <summary>Result of a print-to-PDF operation.</summary>
public class PrintResult
{
    public bool    Success      { get; private set; }
    public byte[]? Data         { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static PrintResult Ok(byte[] data)    => new() { Success = true,  Data = data };
    public static PrintResult Fail(string error) => new() { Success = false, ErrorMessage = error };
}
