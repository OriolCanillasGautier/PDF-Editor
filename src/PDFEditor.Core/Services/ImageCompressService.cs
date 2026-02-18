using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Xobject;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Statistics about image compression results
/// </summary>
public class ImageCompressionResult
{
    public long OriginalSize { get; set; }
    public long CompressedSize { get; set; }
    public int ImagesProcessed { get; set; }
    public int ImagesSkipped { get; set; }
    public double CompressionRatio => OriginalSize > 0 ? (1.0 - (double)CompressedSize / OriginalSize) * 100 : 0;
    public byte[] OutputPdf { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// Options for image compression within PDFs
/// </summary>
public class ImageCompressionOptions
{
    /// <summary>Target JPEG quality (1-100, lower = smaller)</summary>
    public int JpegQuality { get; set; } = 75;

    /// <summary>Maximum image dimension (width or height) — images larger will be downscaled</summary>
    public int MaxDimension { get; set; } = 2048;

    /// <summary>Minimum image width to process (skip tiny images like icons)</summary>
    public int MinWidth { get; set; } = 50;

    /// <summary>Minimum image height to process</summary>
    public int MinHeight { get; set; } = 50;

    /// <summary>If true, convert all images to JPEG (even PNGs)</summary>
    public bool ConvertAllToJpeg { get; set; } = false;

    /// <summary>DPI to downsample images to (0 = no downsampling)</summary>
    public int TargetDpi { get; set; } = 150;

    /// <summary>Apply grayscale conversion to reduce color data</summary>
    public bool ConvertToGrayscale { get; set; } = false;
}

/// <summary>
/// Service for compressing images within PDF documents to reduce file size.
/// Uses Magick.NET for image resampling and quality reduction.
/// </summary>
public class ImageCompressService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Analyzes images in a PDF and returns stats
    /// </summary>
    public List<(int PageIndex, int ImageCount, long EstimatedBytes)> AnalyzeImages(byte[] pdfBytes)
    {
        Log.Info("Analyzing images in PDF ({Bytes} bytes)", pdfBytes.Length);
        var results = new List<(int, int, long)>();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var doc = new PdfDocument(reader);

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);
            var resources = page.GetResources();
            var xObjects = resources.GetResource(PdfName.XObject);
            int imageCount = 0;
            long estimatedBytes = 0;

            if (xObjects != null)
            {
                foreach (var name in xObjects.KeySet())
                {
                    var obj = xObjects.GetAsStream(name);
                    if (obj != null)
                    {
                        var subtype = obj.GetAsName(PdfName.Subtype);
                        if (PdfName.Image.Equals(subtype))
                        {
                            imageCount++;
                            estimatedBytes += obj.GetBytes(false)?.Length ?? 0;
                        }
                    }
                }
            }

            results.Add((i - 1, imageCount, estimatedBytes));
        }

        int totalImages = results.Sum(r => r.Item2);
        Log.Info("Found {Images} images across {Pages} pages", totalImages, results.Count);
        return results;
    }

    /// <summary>
    /// Compresses all images in a PDF
    /// </summary>
    public async Task<ImageCompressionResult> CompressAsync(byte[] pdfBytes,
        ImageCompressionOptions? options = null,
        IProgress<(int current, int total)>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new ImageCompressionOptions();
        Log.Info("Compressing PDF images (quality={Quality}, maxDim={Max})",
            options.JpegQuality, options.MaxDimension);

        return await Task.Run(() =>
        {
            var result = new ImageCompressionResult { OriginalSize = pdfBytes.Length };

            var outMs = new MemoryStream();
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var writer = new PdfWriter(outMs);
            using var doc = new PdfDocument(reader, writer);

            int processed = 0;
            int skipped = 0;
            int total = 0;

            // Count total images first
            for (int i = 1; i <= doc.GetNumberOfPages(); i++)
            {
                var xObjects = doc.GetPage(i).GetResources().GetResource(PdfName.XObject);
                if (xObjects == null) continue;
                foreach (var name in xObjects.KeySet())
                {
                    var obj = xObjects.GetAsStream(name);
                    if (obj != null && PdfName.Image.Equals(obj.GetAsName(PdfName.Subtype)))
                        total++;
                }
            }

            int current = 0;
            for (int i = 1; i <= doc.GetNumberOfPages(); i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = doc.GetPage(i);
                var resources = page.GetResources();
                var xObjects = resources.GetResource(PdfName.XObject);
                if (xObjects == null) continue;

                foreach (var name in xObjects.KeySet())
                {
                    var stream = xObjects.GetAsStream(name);
                    if (stream == null) continue;
                    if (!PdfName.Image.Equals(stream.GetAsName(PdfName.Subtype)))
                        continue;

                    current++;
                    progress?.Report((current, total));

                    try
                    {
                        var imageBytes = stream.GetBytes(false);
                        if (imageBytes == null || imageBytes.Length == 0)
                        {
                            skipped++;
                            continue;
                        }

                        var widthObj = stream.GetAsNumber(PdfName.Width);
                        var heightObj = stream.GetAsNumber(PdfName.Height);
                        int w = widthObj?.IntValue() ?? 0;
                        int h = heightObj?.IntValue() ?? 0;

                        if (w < options.MinWidth || h < options.MinHeight)
                        {
                            skipped++;
                            continue;
                        }

                        // Compress using Magick.NET
                        byte[] compressed = CompressImageBytes(imageBytes, w, h, options);
                        if (compressed.Length < imageBytes.Length)
                        {
                            // Replace image data in the stream
                            var imgData = iText.IO.Image.ImageDataFactory.Create(compressed);
                            var newImage = new PdfImageXObject(imgData);
                            // Copy the compressed image object reference
                            xObjects.Put(name, newImage.GetPdfObject());
                            processed++;
                        }
                        else
                        {
                            skipped++; // Compression didn't help
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(ex, "Failed to compress image on page {Page}", i);
                        skipped++;
                    }
                }
            }

            doc.Close();
            var output = outMs.ToArray();

            result.CompressedSize = output.Length;
            result.ImagesProcessed = processed;
            result.ImagesSkipped = skipped;
            result.OutputPdf = output;

            Log.Info("Compression complete: {Processed} processed, {Skipped} skipped, {Ratio:F1}% reduction",
                processed, skipped, result.CompressionRatio);
            return result;
        }, ct);
    }

    private byte[] CompressImageBytes(byte[] rawBytes, int width, int height, ImageCompressionOptions options)
    {
        try
        {
            using var image = new ImageMagick.MagickImage(rawBytes);

            // Downscale if needed
            if (options.MaxDimension > 0 &&
                (image.Width > options.MaxDimension || image.Height > options.MaxDimension))
            {
                image.Resize((uint)options.MaxDimension, (uint)options.MaxDimension);
            }

            // Convert to grayscale if requested
            if (options.ConvertToGrayscale)
            {
                image.Grayscale();
            }

            // Strip metadata
            image.Strip();

            // Set quality and encode as JPEG
            image.Quality = (uint)options.JpegQuality;
            return image.ToByteArray(ImageMagick.MagickFormat.Jpeg);
        }
        catch
        {
            return rawBytes;
        }
    }

    /// <summary>
    /// Quick compress with default options
    /// </summary>
    public async Task<byte[]> QuickCompressAsync(byte[] pdfBytes, int quality = 75)
    {
        var result = await CompressAsync(pdfBytes, new ImageCompressionOptions { JpegQuality = quality });
        return result.OutputPdf;
    }
}
