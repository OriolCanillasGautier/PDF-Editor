using NLog;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF pages to HTML with embedded base64 images for visual fidelity
/// </summary>
public class HtmlExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly ImageExportProvider _imageProvider = new();

    public string FormatName => "HTML Document";
    public string[] SupportedExtensions => new[] { ".html", ".htm" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    public async Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var html = await Task.Run(() =>
                GenerateHtml(pdfBytes, options, cancellationToken), cancellationToken);

            var data = System.Text.Encoding.UTF8.GetBytes(html);
            return ExportResult.Ok(data, $"{options.BaseFileName}.html", "text/html");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HTML export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // HTML export is a single document, not per-page
        throw new NotSupportedException("HTML export does not support per-page export. Use ExportAsync instead.");
    }

    private string GenerateHtml(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        var pdfOps = new PdfOperations();
        int pageCount = pdfOps.GetPageCount(pdfBytes);
        var pageIndices = options.PageIndices ?? Enumerable.Range(0, pageCount).ToArray();
        var title = options.BaseFileName;
        int dpi = options.Dpi > 0 ? options.Dpi : 150;

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
        sb.AppendLine($"<p class=\"info\">{pageIndices.Length} page{(pageIndices.Length != 1 ? "s" : "")}</p>");

        var imgOptions = new ExportOptions
        {
            Dpi = dpi,
            Quality = options.Quality,
            OutputFormat = "PNG"
        };

        foreach (var pageIdx in pageIndices)
        {
            ct.ThrowIfCancellationRequested();

            var renderService = new PdfRenderService();
            int scaledWidth = (int)(8.5 * dpi);
            int scaledHeight = (int)(11.0 * dpi);
            var (pixels, width, height) = renderService.RenderPage(pdfBytes, pageIdx, scaledWidth, scaledHeight);

            using var image = new ImageMagick.MagickImage();
            var settings = new ImageMagick.PixelReadSettings((uint)width, (uint)height,
                ImageMagick.StorageType.Char, ImageMagick.PixelMapping.BGRA);
            image.ReadPixels(pixels, settings);
            image.Format = ImageMagick.MagickFormat.Png;

            var ms = new MemoryStream();
            image.Write(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());

            sb.AppendLine("<div class=\"page\">");
            sb.AppendLine($"  <div class=\"page-label\">Page {pageIdx + 1} of {pageCount}</div>");
            sb.AppendLine($"  <img src=\"data:image/png;base64,{base64}\" alt=\"Page {pageIdx + 1}\" />");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}
