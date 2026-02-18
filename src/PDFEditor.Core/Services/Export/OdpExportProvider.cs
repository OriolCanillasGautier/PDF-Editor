using NLog;
using PDFEditor.Core.Abstractions;
using iText.Kernel.Pdf;
using Docnet.Core;
using Docnet.Core.Models;
using ImageMagick;
using System.IO.Compression;
using System.Text;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF pages to OpenDocument Presentation (.odp) format.
/// Each PDF page is rendered as an image and placed on a slide.
/// Builds the ODP ZIP package with hand-crafted ODF XML.
/// </summary>
public class OdpExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly object PdfiumLock = new();

    public string FormatName => "OpenDocument Presentation (ODP)";
    public string[] SupportedExtensions => new[] { ".odp" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    public async Task<ExportResult> ExportAsync(
        byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var odp = await Task.Run(() => GenerateOdp(pdfBytes, options, cancellationToken), cancellationToken);
            return ExportResult.Ok(odp, $"{options.BaseFileName}.odp",
                "application/vnd.oasis.opendocument.presentation");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error(ex, "ODP export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(
        byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Per-page export not supported for ODP");
    }

    private byte[] GenerateOdp(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        int dpi = options.Dpi > 0 ? options.Dpi : 150;
        var pageIndices = GetPageIndices(pdfBytes, options);
        var slideImages = new List<(int pageIndex, byte[] png, int widthPx, int heightPx)>();

        // Render pages to PNG images
        lock (PdfiumLock)
        {
            using var docnet = DocLib.Instance;
            using var docReader = docnet.GetDocReader(pdfBytes, new PageDimensions(dpi, dpi));
            foreach (int pageIdx in pageIndices)
            {
                ct.ThrowIfCancellationRequested();
                using var pageReader = docReader.GetPageReader(pageIdx);
                int w = pageReader.GetPageWidth();
                int h = pageReader.GetPageHeight();
                var rawBytes = pageReader.GetImage();

                using var image = new MagickImage();
                var readSettings = new MagickReadSettings
                {
                    Width = (uint)w,
                    Height = (uint)h,
                    Format = MagickFormat.Bgra,
                    Depth = 8
                };
                image.Read(rawBytes, readSettings);
                image.Format = MagickFormat.Png;

                slideImages.Add((pageIdx, image.ToByteArray(), w, h));
            }
        }

        // Build ODP ZIP
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            // mimetype (must be first, uncompressed)
            AddEntry(zip, "mimetype", "application/vnd.oasis.opendocument.presentation",
                CompressionLevel.NoCompression);

            // META-INF/manifest.xml
            AddEntry(zip, "META-INF/manifest.xml", BuildManifest(slideImages.Count));

            // Add slide images
            for (int i = 0; i < slideImages.Count; i++)
            {
                AddBinaryEntry(zip, $"Pictures/slide{i + 1}.png", slideImages[i].png);
            }

            // content.xml (slides)
            AddEntry(zip, "content.xml", BuildContent(slideImages));

            // styles.xml
            AddEntry(zip, "styles.xml", BuildStyles());

            // meta.xml
            AddEntry(zip, "meta.xml", BuildMeta());
        }

        Log.Info("ODP export complete: {SlideCount} slides", slideImages.Count);
        return ms.ToArray();
    }

    private string BuildManifest(int slideCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.2\">");
        sb.AppendLine("  <manifest:file-entry manifest:media-type=\"application/vnd.oasis.opendocument.presentation\" manifest:full-path=\"/\"/>");
        sb.AppendLine("  <manifest:file-entry manifest:media-type=\"text/xml\" manifest:full-path=\"content.xml\"/>");
        sb.AppendLine("  <manifest:file-entry manifest:media-type=\"text/xml\" manifest:full-path=\"styles.xml\"/>");
        sb.AppendLine("  <manifest:file-entry manifest:media-type=\"text/xml\" manifest:full-path=\"meta.xml\"/>");

        for (int i = 1; i <= slideCount; i++)
        {
            sb.AppendLine($"  <manifest:file-entry manifest:media-type=\"image/png\" manifest:full-path=\"Pictures/slide{i}.png\"/>");
        }

        sb.AppendLine("</manifest:manifest>");
        return sb.ToString();
    }

    private string BuildContent(List<(int pageIndex, byte[] png, int widthPx, int heightPx)> slides)
    {
        // Standard slide dimensions (25.4cm x 19.05cm = 10" x 7.5")
        const string slideW = "25.4cm";
        const string slideH = "19.05cm";

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<office:document-content");
        sb.AppendLine("  xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"");
        sb.AppendLine("  xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\"");
        sb.AppendLine("  xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\"");
        sb.AppendLine("  xmlns:presentation=\"urn:oasis:names:tc:opendocument:xmlns:presentation:1.0\"");
        sb.AppendLine("  xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\"");
        sb.AppendLine("  xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\"");
        sb.AppendLine("  xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\"");
        sb.AppendLine("  xmlns:xlink=\"http://www.w3.org/1999/xlink\"");
        sb.AppendLine("  office:version=\"1.2\">");

        // Automatic styles for image frames
        sb.AppendLine("  <office:automatic-styles>");
        sb.AppendLine("    <style:style style:name=\"dp1\" style:family=\"drawing-page\"/>");
        for (int i = 0; i < slides.Count; i++)
        {
            sb.AppendLine($"    <style:style style:name=\"gr{i + 1}\" style:family=\"graphic\">");
            sb.AppendLine("      <style:graphic-properties style:protect=\"size\"/>");
            sb.AppendLine("    </style:style>");
        }
        sb.AppendLine("  </office:automatic-styles>");

        sb.AppendLine("  <office:body>");
        sb.AppendLine("    <office:presentation>");

        for (int i = 0; i < slides.Count; i++)
        {
            sb.AppendLine($"    <draw:page draw:name=\"Slide{i + 1}\" draw:style-name=\"dp1\" draw:master-page-name=\"Default\" presentation:presentation-page-layout-name=\"AL0T0\">");
            sb.AppendLine($"      <draw:frame draw:style-name=\"gr{i + 1}\" draw:layer=\"layout\" svg:x=\"0cm\" svg:y=\"0cm\" svg:width=\"{slideW}\" svg:height=\"{slideH}\">");
            sb.AppendLine($"        <draw:image xlink:href=\"Pictures/slide{i + 1}.png\" xlink:type=\"simple\" xlink:show=\"embed\" xlink:actuate=\"onLoad\"/>");
            sb.AppendLine("      </draw:frame>");
            sb.AppendLine("    </draw:page>");
        }

        sb.AppendLine("    </office:presentation>");
        sb.AppendLine("  </office:body>");
        sb.AppendLine("</office:document-content>");
        return sb.ToString();
    }

    private string BuildStyles()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<office:document-styles");
        sb.AppendLine("  xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"");
        sb.AppendLine("  xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\"");
        sb.AppendLine("  xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\"");
        sb.AppendLine("  xmlns:presentation=\"urn:oasis:names:tc:opendocument:xmlns:presentation:1.0\"");
        sb.AppendLine("  xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\"");
        sb.AppendLine("  office:version=\"1.2\">");
        sb.AppendLine("  <office:styles>");
        sb.AppendLine("    <draw:gradient draw:name=\"Default\" draw:style=\"linear\" draw:start-color=\"#ffffff\" draw:end-color=\"#ffffff\" draw:angle=\"0\"/>");
        sb.AppendLine("  </office:styles>");
        sb.AppendLine("  <office:automatic-styles>");
        sb.AppendLine("    <style:page-layout style:name=\"PM0\">");
        sb.AppendLine("      <style:page-layout-properties fo:margin-top=\"0cm\" fo:margin-bottom=\"0cm\" fo:margin-left=\"0cm\" fo:margin-right=\"0cm\" fo:page-width=\"25.4cm\" fo:page-height=\"19.05cm\" style:print-orientation=\"landscape\"/>");
        sb.AppendLine("    </style:page-layout>");
        sb.AppendLine("  </office:automatic-styles>");
        sb.AppendLine("  <office:master-styles>");
        sb.AppendLine("    <style:master-page style:name=\"Default\" style:page-layout-name=\"PM0\"/>");
        sb.AppendLine("  </office:master-styles>");
        sb.AppendLine("</office:document-styles>");
        return sb.ToString();
    }

    private string BuildMeta()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<office:document-meta");
        sb.AppendLine("  xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"");
        sb.AppendLine("  xmlns:meta=\"urn:oasis:names:tc:opendocument:xmlns:meta:1.0\"");
        sb.AppendLine("  xmlns:dc=\"http://purl.org/dc/elements/1.1/\"");
        sb.AppendLine("  office:version=\"1.2\">");
        sb.AppendLine("  <office:meta>");
        sb.AppendLine($"    <meta:generator>PDFEditor</meta:generator>");
        sb.AppendLine($"    <meta:creation-date>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta:creation-date>");
        sb.AppendLine("  </office:meta>");
        sb.AppendLine("</office:document-meta>");
        return sb.ToString();
    }

    private int[] GetPageIndices(byte[] pdfBytes, ExportOptions options)
    {
        if (options.PageIndices != null && options.PageIndices.Length > 0)
            return options.PageIndices;

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var doc = new PdfDocument(reader);
        return Enumerable.Range(0, doc.GetNumberOfPages()).ToArray();
    }

    private static void AddEntry(ZipArchive zip, string path, string content,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(path, level);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void AddBinaryEntry(ZipArchive zip, string path, byte[] data)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(data, 0, data.Length);
    }
}
