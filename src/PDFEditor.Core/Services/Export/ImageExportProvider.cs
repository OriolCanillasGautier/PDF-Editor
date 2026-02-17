using ImageMagick;
using NLog;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF pages to image formats: PNG, JPEG, TIFF, BMP, WebP
/// </summary>
public class ImageExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly PdfRenderService _renderService = new();

    public string FormatName => "Image (PNG, JPEG, TIFF, BMP, WebP)";
    public string[] SupportedExtensions => new[] { ".png", ".jpg", ".jpeg", ".tiff", ".tif", ".bmp", ".webp" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => true;

    public async Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Single-document export: default to first page or all pages combined
            var pageIndex = options.PageIndices?.FirstOrDefault() ?? 0;
            var imageBytes = await Task.Run(() =>
                RenderPageToImage(pdfBytes, pageIndex, options), cancellationToken);

            var ext = GetExtension(options.OutputFormat);
            return ExportResult.Ok(
                imageBytes,
                $"{options.BaseFileName}_page{pageIndex + 1}{ext}",
                GetMimeType(options.OutputFormat),
                pageIndex + 1);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Image export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public async Task<List<ExportResult>> ExportPagesAsync(byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ExportResult>();
        var pageCount = _renderService.GetPageCount(pdfBytes);
        var pageIndices = options.PageIndices ?? Enumerable.Range(0, pageCount).ToArray();
        var ext = GetExtension(options.OutputFormat);

        for (int i = 0; i < pageIndices.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageIdx = pageIndices[i];

            progress?.Report(new ExportProgress
            {
                CurrentPage = i + 1,
                TotalPages = pageIndices.Length,
                Message = $"Exporting page {pageIdx + 1}..."
            });

            try
            {
                var imageBytes = await Task.Run(() =>
                    RenderPageToImage(pdfBytes, pageIdx, options), cancellationToken);

                results.Add(ExportResult.Ok(
                    imageBytes,
                    $"{options.BaseFileName}_page{pageIdx + 1}{ext}",
                    GetMimeType(options.OutputFormat),
                    pageIdx + 1));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to export page {Page}", pageIdx + 1);
                results.Add(ExportResult.Fail($"Page {pageIdx + 1}: {ex.Message}"));
            }
        }

        return results;
    }

    private byte[] RenderPageToImage(byte[] pdfBytes, int pageIndex, ExportOptions options)
    {
        int dpi = options.Dpi > 0 ? options.Dpi : 150;
        int scaledWidth = (int)(8.5 * dpi);
        int scaledHeight = (int)(11.0 * dpi);
        var (pixels, width, height) = _renderService.RenderPage(pdfBytes, pageIndex, scaledWidth, scaledHeight);

        using var image = new MagickImage();
        var settings = new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.BGRA);
        image.ReadPixels(pixels, settings);

        var format = options.OutputFormat.ToUpperInvariant();
        image.Format = format switch
        {
            "JPEG" or "JPG" => MagickFormat.Jpeg,
            "TIFF" or "TIF" => MagickFormat.Tiff,
            "BMP" => MagickFormat.Bmp,
            "WEBP" => MagickFormat.WebP,
            _ => MagickFormat.Png
        };

        if (image.Format == MagickFormat.Jpeg)
            image.Quality = (uint)(options.Quality > 0 ? options.Quality : 90);

        var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    private static string GetExtension(string format) => format.ToUpperInvariant() switch
    {
        "JPEG" or "JPG" => ".jpg",
        "TIFF" or "TIF" => ".tiff",
        "BMP" => ".bmp",
        "WEBP" => ".webp",
        _ => ".png"
    };

    private static string GetMimeType(string format) => format.ToUpperInvariant() switch
    {
        "JPEG" or "JPG" => "image/jpeg",
        "TIFF" or "TIF" => "image/tiff",
        "BMP" => "image/bmp",
        "WEBP" => "image/webp",
        _ => "image/png"
    };
}
