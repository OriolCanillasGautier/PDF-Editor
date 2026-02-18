using ImageMagick;
using NLog;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace PDFEditor.Core.Services;

/// <summary>
/// Extracts embedded images from PDF documents.
/// </summary>
public class ImageExtractionService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Metadata about an extracted image.</summary>
    public class ExtractedImageInfo
    {
        public int PageNumber { get; set; }
        public int ImageIndex { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string Format { get; set; } = "png";
        public int Width { get; set; }
        public int Height { get; set; }
        public long SizeBytes => Data.Length;
    }

    /// <summary>
    /// Extracts all images from a PDF document.
    /// </summary>
    public List<ExtractedImageInfo> ExtractAll(byte[] pdfBytes, int[]? pageIndices = null)
    {
        var results = new List<ExtractedImageInfo>();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdfDoc = new PdfDocument(reader);

        int total = pdfDoc.GetNumberOfPages();
        int[] pages = pageIndices ?? Enumerable.Range(0, total).ToArray();

        foreach (int pi in pages)
        {
            int pageNum = pi + 1;
            if (pageNum < 1 || pageNum > total) continue;

            var page = pdfDoc.GetPage(pageNum);
            var listener = new ImageListener();
            new PdfCanvasProcessor(listener).ProcessPageContent(page);

            for (int i = 0; i < listener.Images.Count; i++)
            {
                var img = listener.Images[i];
                try
                {
                    var (normalized, format) = NormalizeImage(img.Data);
                    if (normalized.Length == 0) continue;

                    int w = 0, h = 0;
                    try
                    {
                        using var mgk = new MagickImage(normalized);
                        w = (int)mgk.Width;
                        h = (int)mgk.Height;
                    }
                    catch { w = (int)Math.Abs(img.Width); h = (int)Math.Abs(img.Height); }

                    results.Add(new ExtractedImageInfo
                    {
                        PageNumber = pageNum,
                        ImageIndex = i,
                        Data = normalized,
                        Format = format,
                        Width = w,
                        Height = h
                    });
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to normalize image on page {Page}, index {Index}", pageNum, i);
                }
            }
        }

        Log.Info("Extracted {Count} images from PDF", results.Count);
        return results;
    }

    /// <summary>
    /// Saves extracted images to a folder.
    /// </summary>
    public async Task<int> ExtractToFolderAsync(
        byte[] pdfBytes, string outputFolder,
        int[]? pageIndices = null,
        string format = "png",
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputFolder);
        var images = await Task.Run(() => ExtractAll(pdfBytes, pageIndices), ct);
        int saved = 0;

        foreach (var img in images)
        {
            ct.ThrowIfCancellationRequested();
            string ext = format.ToLowerInvariant();
            string filename = $"page{img.PageNumber}_image{img.ImageIndex + 1}.{ext}";
            string path = Path.Combine(outputFolder, filename);

            try
            {
                if (img.Format.Equals(ext, StringComparison.OrdinalIgnoreCase))
                {
                    await File.WriteAllBytesAsync(path, img.Data, ct);
                }
                else
                {
                    using var mgk = new MagickImage(img.Data);
                    mgk.Format = ext switch
                    {
                        "jpg" or "jpeg" => MagickFormat.Jpeg,
                        "bmp" => MagickFormat.Bmp,
                        "tiff" or "tif" => MagickFormat.Tiff,
                        "webp" => MagickFormat.WebP,
                        _ => MagickFormat.Png
                    };
                    await mgk.WriteAsync(path, ct);
                }
                saved++;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to save image: {Path}", path);
            }
        }

        return saved;
    }

    /// <summary>Returns count of images in the PDF without extracting data.</summary>
    public int CountImages(byte[] pdfBytes)
    {
        int count = 0;
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdfDoc = new PdfDocument(reader);

        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
        {
            var listener = new ImageCountListener();
            new PdfCanvasProcessor(listener).ProcessPageContent(pdfDoc.GetPage(i));
            count += listener.Count;
        }
        return count;
    }

    private static (byte[] data, string format) NormalizeImage(byte[] raw)
    {
        if (raw == null || raw.Length < 4) return (Array.Empty<byte>(), "");

        // JPEG
        if (raw[0] == 0xFF && raw[1] == 0xD8 && raw[2] == 0xFF)
            return (raw, "jpg");

        // PNG
        if (raw.Length >= 8
            && raw[0] == 0x89 && raw[1] == 0x50 && raw[2] == 0x4E && raw[3] == 0x47)
            return (raw, "png");

        // Convert via Magick.NET
        try
        {
            using var mgk = new MagickImage(raw);
            using var ms = new MemoryStream();
            mgk.Format = MagickFormat.Png;
            mgk.Write(ms);
            return (ms.ToArray(), "png");
        }
        catch
        {
            return (Array.Empty<byte>(), "");
        }
    }

    private class RawImage
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public float Width { get; set; }
        public float Height { get; set; }
    }

    private class ImageListener : IEventListener
    {
        public List<RawImage> Images { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_IMAGE) return;
            try
            {
                var info = (ImageRenderInfo)data;
                var img = info.GetImage();
                if (img == null) return;
                var bytes = img.GetImageBytes(true);
                if (bytes == null || bytes.Length < 100) return;
                var m = info.GetImageCtm();
                Images.Add(new RawImage { Data = bytes, Width = m.Get(0), Height = m.Get(4) });
            }
            catch { }
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_IMAGE };
    }

    private class ImageCountListener : IEventListener
    {
        public int Count { get; private set; }

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type == EventType.RENDER_IMAGE) Count++;
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_IMAGE };
    }
}
