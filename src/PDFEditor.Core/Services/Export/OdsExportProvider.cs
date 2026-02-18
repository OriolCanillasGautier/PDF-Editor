using NLog;
using PDFEditor.Core.Abstractions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.IO.Compression;
using System.Text;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF table data to OpenDocument Spreadsheet (.ods) format.
/// Detects table structures via column alignment heuristics and exports to ODS ZIP package.
/// </summary>
public class OdsExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string FormatName => "OpenDocument Spreadsheet (ODS)";
    public string[] SupportedExtensions => new[] { ".ods" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    public async Task<ExportResult> ExportAsync(
        byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ods = await Task.Run(() => GenerateOds(pdfBytes, options, cancellationToken), cancellationToken);
            return ExportResult.Ok(ods, $"{options.BaseFileName}.ods",
                "application/vnd.oasis.opendocument.spreadsheet");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error(ex, "ODS export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(
        byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Per-page export not supported for ODS");
    }

    private byte[] GenerateOds(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        // Extract text per page, detect tables
        var pageData = ExtractTableData(pdfBytes, options, ct);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            // mimetype (must be first, uncompressed)
            AddEntry(zip, "mimetype", "application/vnd.oasis.opendocument.spreadsheet",
                CompressionLevel.NoCompression);

            // META-INF/manifest.xml
            AddEntry(zip, "META-INF/manifest.xml", BuildManifest());

            // content.xml (spreadsheet data)
            AddEntry(zip, "content.xml", BuildContent(pageData));

            // styles.xml
            AddEntry(zip, "styles.xml", BuildStyles());

            // meta.xml
            AddEntry(zip, "meta.xml", BuildMeta());
        }

        Log.Info("ODS export complete: {SheetCount} sheets", pageData.Count);
        return ms.ToArray();
    }

    private List<SheetData> ExtractTableData(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        var sheets = new List<SheetData>();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var doc = new PdfDocument(reader);

        var pageIndices = options.PageIndices?.Length > 0
            ? options.PageIndices
            : Enumerable.Range(0, doc.GetNumberOfPages()).ToArray();

        foreach (int pageIdx in pageIndices)
        {
            ct.ThrowIfCancellationRequested();
            int iTextPage = pageIdx + 1;
            if (iTextPage > doc.GetNumberOfPages()) continue;

            var page = doc.GetPage(iTextPage);
            var strategy = new SimpleTextExtractionStrategy();
            var text = PdfTextExtractor.GetTextFromPage(page, strategy);

            if (string.IsNullOrWhiteSpace(text)) continue;

            var rows = ParseTabularData(text);
            sheets.Add(new SheetData
            {
                Name = $"Page {iTextPage}",
                Rows = rows
            });
        }

        // If no data found, add empty sheet
        if (sheets.Count == 0)
            sheets.Add(new SheetData { Name = "Sheet1", Rows = new List<string[]>() });

        return sheets;
    }

    /// <summary>
    /// Parses text into rows/columns using column alignment heuristics.
    /// Lines split by 2+ spaces are treated as table columns.
    /// </summary>
    private List<string[]> ParseTabularData(string text)
    {
        var rows = new List<string[]>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Split by 2+ spaces (common PDF table column separator)
            var parts = System.Text.RegularExpressions.Regex.Split(trimmed, @"\s{2,}")
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToArray();

            if (parts.Length > 0)
                rows.Add(parts);
        }

        return rows;
    }

    private string BuildManifest()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.2\">");
        sb.AppendLine("  <manifest:file-entry manifest:media-type=\"application/vnd.oasis.opendocument.spreadsheet\" manifest:full-path=\"/\"/>");
        sb.AppendLine("  <manifest:file-entry manifest:media-type=\"text/xml\" manifest:full-path=\"content.xml\"/>");
        sb.AppendLine("  <manifest:file-entry manifest:media-type=\"text/xml\" manifest:full-path=\"styles.xml\"/>");
        sb.AppendLine("  <manifest:file-entry manifest:media-type=\"text/xml\" manifest:full-path=\"meta.xml\"/>");
        sb.AppendLine("</manifest:manifest>");
        return sb.ToString();
    }

    private string BuildContent(List<SheetData> sheets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<office:document-content");
        sb.AppendLine("  xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"");
        sb.AppendLine("  xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\"");
        sb.AppendLine("  xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\"");
        sb.AppendLine("  xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\"");
        sb.AppendLine("  xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\"");
        sb.AppendLine("  office:version=\"1.2\">");

        sb.AppendLine("  <office:automatic-styles>");
        sb.AppendLine("    <style:style style:name=\"co1\" style:family=\"table-column\">");
        sb.AppendLine("      <style:table-column-properties fo:break-before=\"auto\" style:column-width=\"3cm\"/>");
        sb.AppendLine("    </style:style>");
        sb.AppendLine("    <style:style style:name=\"ro1\" style:family=\"table-row\">");
        sb.AppendLine("      <style:table-row-properties style:row-height=\"0.5cm\" fo:break-before=\"auto\"/>");
        sb.AppendLine("    </style:style>");
        sb.AppendLine("    <style:style style:name=\"ta1\" style:family=\"table\" style:master-page-name=\"Default\">");
        sb.AppendLine("      <style:table-properties table:display=\"true\" style:writing-mode=\"lr-tb\"/>");
        sb.AppendLine("    </style:style>");
        sb.AppendLine("  </office:automatic-styles>");

        sb.AppendLine("  <office:body>");
        sb.AppendLine("    <office:spreadsheet>");

        foreach (var sheet in sheets)
        {
            int maxCols = sheet.Rows.Count > 0 ? sheet.Rows.Max(r => r.Length) : 1;
            string safeName = EscapeXml(sheet.Name);

            sb.AppendLine($"    <table:table table:name=\"{safeName}\" table:style-name=\"ta1\">");
            sb.AppendLine($"      <table:table-column table:style-name=\"co1\" table:number-columns-repeated=\"{maxCols}\"/>");

            foreach (var row in sheet.Rows)
            {
                sb.AppendLine("      <table:table-row table:style-name=\"ro1\">");
                for (int c = 0; c < maxCols; c++)
                {
                    string cellValue = c < row.Length ? row[c] : "";
                    string escapedValue = EscapeXml(cellValue);

                    // Try to detect numeric values
                    if (double.TryParse(cellValue, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double numVal))
                    {
                        sb.AppendLine($"        <table:table-cell office:value-type=\"float\" office:value=\"{numVal}\">");
                        sb.AppendLine($"          <text:p>{escapedValue}</text:p>");
                        sb.AppendLine("        </table:table-cell>");
                    }
                    else
                    {
                        sb.AppendLine($"        <table:table-cell office:value-type=\"string\">");
                        sb.AppendLine($"          <text:p>{escapedValue}</text:p>");
                        sb.AppendLine("        </table:table-cell>");
                    }
                }
                sb.AppendLine("      </table:table-row>");
            }

            sb.AppendLine("    </table:table>");
        }

        sb.AppendLine("    </office:spreadsheet>");
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
        sb.AppendLine("  xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\"");
        sb.AppendLine("  office:version=\"1.2\">");
        sb.AppendLine("  <office:styles>");
        sb.AppendLine("    <style:default-style style:family=\"table-cell\">");
        sb.AppendLine("      <style:text-properties fo:font-family=\"Arial\" fo:font-size=\"10pt\"/>");
        sb.AppendLine("    </style:default-style>");
        sb.AppendLine("  </office:styles>");
        sb.AppendLine("  <office:automatic-styles>");
        sb.AppendLine("    <style:page-layout style:name=\"pm1\">");
        sb.AppendLine("      <style:page-layout-properties fo:margin-top=\"1.27cm\" fo:margin-bottom=\"1.27cm\" fo:margin-left=\"1.27cm\" fo:margin-right=\"1.27cm\" fo:page-width=\"21.001cm\" fo:page-height=\"29.7cm\" style:print-orientation=\"portrait\"/>");
        sb.AppendLine("    </style:page-layout>");
        sb.AppendLine("  </office:automatic-styles>");
        sb.AppendLine("  <office:master-styles>");
        sb.AppendLine("    <style:master-page style:name=\"Default\" style:page-layout-name=\"pm1\"/>");
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

    private static string EscapeXml(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static void AddEntry(ZipArchive zip, string path, string content,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(path, level);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private class SheetData
    {
        public string Name { get; set; } = "";
        public List<string[]> Rows { get; set; } = new();
    }
}
