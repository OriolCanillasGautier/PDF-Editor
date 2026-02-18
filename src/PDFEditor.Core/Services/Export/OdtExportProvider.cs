using NLog;
using PDFEditor.Core.Abstractions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.IO.Compression;
using System.Text;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF content to OpenDocument Text (.odt) format.
/// Builds the ODT ZIP package with hand-crafted XML, analogous to our DocxExportProvider approach.
/// </summary>
public class OdtExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const string NsOffice   = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string NsStyle    = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private const string NsText     = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string NsFo       = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private const string NsManifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    public string FormatName => "OpenDocument Text (ODT)";
    public string[] SupportedExtensions => new[] { ".odt" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    public async Task<ExportResult> ExportAsync(
        byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var odt = await Task.Run(() => GenerateOdt(pdfBytes, options, cancellationToken), cancellationToken);
            return ExportResult.Ok(odt, $"{options.BaseFileName}.odt",
                "application/vnd.oasis.opendocument.text");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ODT export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(
        byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("ODT export produces a single document.");

    private byte[] GenerateOdt(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        var bodyContent = ExtractContent(pdfBytes, options, ct);

        string contentXml = BuildContentXml(bodyContent);
        string stylesXml  = BuildStylesXml();
        string metaXml    = BuildMetaXml(options.BaseFileName ?? "Document");
        string manifestXml = BuildManifestXml();
        string mimetype   = "application/vnd.oasis.opendocument.text";

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // mimetype must be first entry, uncompressed, no extra field
            var mimeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var s = mimeEntry.Open())
            {
                var b = Encoding.ASCII.GetBytes(mimetype);
                s.Write(b, 0, b.Length);
            }

            AddEntry(zip, "META-INF/manifest.xml", manifestXml);
            AddEntry(zip, "content.xml", contentXml);
            AddEntry(zip, "styles.xml", stylesXml);
            AddEntry(zip, "meta.xml", metaXml);
        }

        return ms.ToArray();
    }

    private static void AddEntry(ZipArchive zip, string path, string xml)
    {
        var e = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var s = e.Open();
        var b = Encoding.UTF8.GetBytes(xml);
        s.Write(b, 0, b.Length);
    }

    private static string Xe(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    private StringBuilder ExtractContent(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        var body = new StringBuilder(16 * 1024);

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);

        int total = pdfDoc.GetNumberOfPages();
        int[] pages = options.PageIndices ?? Enumerable.Range(0, total).ToArray();

        for (int pi = 0; pi < pages.Length; pi++)
        {
            ct.ThrowIfCancellationRequested();
            int pageNum = pages[pi] + 1;
            if (pageNum < 1 || pageNum > total) continue;

            var page = pdfDoc.GetPage(pageNum);
            var text = PdfTextExtractor.GetTextFromPage(page, new SimpleTextExtractionStrategy());

            if (string.IsNullOrWhiteSpace(text))
            {
                body.Append($"<text:p text:style-name=\"Standard\">[No extractable content on page {pageNum}]</text:p>");
                continue;
            }

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line))
                    body.Append("<text:p text:style-name=\"Standard\"/>");
                else
                    body.Append($"<text:p text:style-name=\"Standard\">{Xe(SanitizeXml(line))}</text:p>");
            }

            // Page break between pages
            if (pi < pages.Length - 1)
                body.Append("<text:p text:style-name=\"PageBreak\"/>");
        }

        return body;
    }

    private static string SanitizeXml(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c == '\t' || c == '\n' || c == '\r'
                || (c >= '\x20' && c <= '\uD7FF')
                || (c >= '\uE000' && c <= '\uFFFD'))
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static string BuildContentXml(StringBuilder body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        $"<office:document-content " +
        $"xmlns:office=\"{NsOffice}\" " +
        $"xmlns:style=\"{NsStyle}\" " +
        $"xmlns:text=\"{NsText}\" " +
        $"xmlns:fo=\"{NsFo}\" " +
        "office:version=\"1.2\">" +
        "<office:automatic-styles>" +
        "<style:style style:name=\"Standard\" style:family=\"paragraph\">" +
        "<style:paragraph-properties fo:margin-top=\"0.1in\" fo:margin-bottom=\"0.08in\"/>" +
        "<style:text-properties fo:font-size=\"11pt\" style:font-name=\"Liberation Sans\"/>" +
        "</style:style>" +
        "<style:style style:name=\"PageBreak\" style:family=\"paragraph\">" +
        "<style:paragraph-properties fo:break-before=\"page\"/>" +
        "</style:style>" +
        "</office:automatic-styles>" +
        "<office:body><office:text>" +
        body.ToString() +
        "</office:text></office:body>" +
        "</office:document-content>";

    private static string BuildStylesXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        $"<office:document-styles xmlns:office=\"{NsOffice}\" " +
        $"xmlns:style=\"{NsStyle}\" " +
        $"xmlns:fo=\"{NsFo}\" " +
        "office:version=\"1.2\">" +
        "<office:styles>" +
        "<style:default-style style:family=\"paragraph\">" +
        "<style:paragraph-properties fo:line-height=\"115%\"/>" +
        "<style:text-properties fo:font-size=\"11pt\" style:font-name=\"Liberation Sans\" " +
        "fo:language=\"en\" fo:country=\"US\"/>" +
        "</style:default-style>" +
        "</office:styles>" +
        "</office:document-styles>";

    private static string BuildMetaXml(string title) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        $"<office:document-meta xmlns:office=\"{NsOffice}\" " +
        "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
        "xmlns:meta=\"urn:oasis:names:tc:opendocument:xmlns:meta:1.0\" " +
        "office:version=\"1.2\">" +
        "<office:meta>" +
        $"<dc:title>{Xe(title)}</dc:title>" +
        "<meta:generator>PDFEditor</meta:generator>" +
        $"<dc:date>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss}</dc:date>" +
        "</office:meta>" +
        "</office:document-meta>";

    private static string BuildManifestXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        $"<manifest:manifest xmlns:manifest=\"{NsManifest}\" manifest:version=\"1.2\">" +
        "<manifest:file-entry manifest:full-path=\"/\" manifest:version=\"1.2\" " +
            "manifest:media-type=\"application/vnd.oasis.opendocument.text\"/>" +
        "<manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/>" +
        "<manifest:file-entry manifest:full-path=\"styles.xml\" manifest:media-type=\"text/xml\"/>" +
        "<manifest:file-entry manifest:full-path=\"meta.xml\" manifest:media-type=\"text/xml\"/>" +
        "</manifest:manifest>";
}
