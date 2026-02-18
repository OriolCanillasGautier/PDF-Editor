using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using NLog;
using PDFEditor.Core.Abstractions;
using System.Text;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF structured data (tables detected by column alignment) as CSV.
/// Each PDF page is separated by a comment row. Tables are exported as proper CSV rows.
/// Unrecognised text lines are written as single-column rows, making every page parseable.
/// </summary>
public class CsvExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string FormatName => "CSV (Comma Separated Values)";
    public string[] SupportedExtensions => new[] { ".csv" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    // ─── IExportProvider ────────────────────────────────────────────────────────

    public async Task<ExportResult> ExportAsync(
        byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var csv = await Task.Run(
                () => GenerateCsv(pdfBytes, options, cancellationToken),
                cancellationToken);

            var bytes = Encoding.UTF8.GetBytes(csv);
            return ExportResult.Ok(bytes, $"{options.BaseFileName}.csv", "text/csv");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CSV export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(
        byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("CSV export produces a single file. Use ExportAsync instead.");

    // ─── Internal Models ─────────────────────────────────────────────────────────

    private class TextChunk
    {
        public string Text { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float FontSize { get; set; }
    }

    private class TextLine
    {
        public float Y { get; set; }
        public List<TextChunk> Chunks { get; set; } = new();
        public string FullText { get; set; } = string.Empty;
    }

    // ─── iText7 Listener ─────────────────────────────────────────────────────────

    private class ChunkListener : IEventListener
    {
        public List<TextChunk> Chunks { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT) return;
            var ri = (TextRenderInfo)data;
            var text = ri.GetText();
            if (string.IsNullOrEmpty(text)) return;

            var start = ri.GetBaseline().GetStartPoint();
            float fontSize = 12f;
            try
            {
                fontSize = ri.GetAscentLine().GetStartPoint().Get(1)
                         - ri.GetDescentLine().GetStartPoint().Get(1);
            }
            catch { }

            Chunks.Add(new TextChunk
            {
                Text = text,
                X = start.Get(0),
                Y = start.Get(1),
                FontSize = Math.Max(1f, fontSize)
            });
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_TEXT };
    }

    // ─── Core Processing ─────────────────────────────────────────────────────────

    private string GenerateCsv(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        var sb = new StringBuilder();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdfDoc = new PdfDocument(reader);

        int totalPages = pdfDoc.GetNumberOfPages();
        var pageIndices = options.PageIndices ?? Enumerable.Range(0, totalPages).ToArray();

        for (int idx = 0; idx < pageIndices.Length; idx++)
        {
            ct.ThrowIfCancellationRequested();
            int pageNum = pageIndices[idx] + 1;
            if (pageNum < 1 || pageNum > totalPages) continue;

            // Page separator comment
            if (idx > 0) sb.AppendLine();
            sb.AppendLine(CsvQuote($"# Page {pageNum}"));

            var page = pdfDoc.GetPage(pageNum);
            var listener = new ChunkListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(page);

            if (listener.Chunks.Count > 0)
            {
                var lines = AssembleLines(listener.Chunks);
                ExportPageAsCsv(lines, sb);
            }
            else
            {
                // Fallback: simple extraction
                var text = PdfTextExtractor.GetTextFromPage(page, new SimpleTextExtractionStrategy());
                foreach (var line in text.Split('\n'))
                {
                    var clean = SanitizeText(line.TrimEnd('\r'));
                    if (!string.IsNullOrWhiteSpace(clean))
                        sb.AppendLine(CsvQuote(clean));
                }
            }
        }

        return sb.ToString();
    }

    private List<TextLine> AssembleLines(List<TextChunk> chunks)
    {
        var sorted = chunks.OrderByDescending(c => c.Y).ThenBy(c => c.X).ToList();
        var lines = new List<TextLine>();
        TextLine? cur = null;

        foreach (var chunk in sorted)
        {
            if (cur == null || Math.Abs(chunk.Y - cur.Y) > 2.0f)
            {
                cur = new TextLine { Y = chunk.Y, Chunks = new List<TextChunk> { chunk } };
                lines.Add(cur);
            }
            else
            {
                cur.Chunks.Add(chunk);
            }
        }

        foreach (var line in lines)
        {
            var ordered = line.Chunks.OrderBy(c => c.X).ToList();
            var sb = new StringBuilder();
            float lastRight = float.MinValue;
            foreach (var chunk in ordered)
            {
                if (lastRight > float.MinValue)
                {
                    float gap = chunk.X - lastRight;
                    if (gap > chunk.FontSize * 0.4f) sb.Append(' ');
                }
                sb.Append(chunk.Text);
                lastRight = chunk.X + chunk.Text.Length * chunk.FontSize * 0.5f;
            }
            line.FullText = sb.ToString().Trim();
        }

        return lines;
    }

    private void ExportPageAsCsv(List<TextLine> lines, StringBuilder sb)
    {
        if (lines.Count == 0) return;

        // Identify x-positions of natural column boundaries using clustering
        // If a line has 2+ distinct chunk X-starts separated by a large gap → table row
        // Otherwise → single-column text row

        // Compute global column boundaries from lines with multiple chunks
        var columnLines = lines.Where(l => l.Chunks.Count >= 2).ToList();
        var columnBoundaries = DetermineColumnBoundaries(columnLines);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.FullText)) continue;

            if (columnBoundaries.Count >= 2 && line.Chunks.Count >= 2)
            {
                // Assign chunks to columns
                var cells = AssignToColumns(line.Chunks, columnBoundaries);
                sb.AppendLine(string.Join(",", cells.Select(c => CsvQuote(SanitizeText(c)))));
            }
            else
            {
                // Single-column row
                sb.AppendLine(CsvQuote(SanitizeText(line.FullText)));
            }
        }
    }

    /// <summary>
    /// Clusters X positions from multi-column lines to determine shared column starts.
    /// </summary>
    private List<float> DetermineColumnBoundaries(List<TextLine> multiColLines)
    {
        if (multiColLines.Count == 0) return new List<float>();

        // Collect all chunk X starts
        var xPositions = multiColLines
            .SelectMany(l => l.Chunks.Select(c => c.X))
            .OrderBy(x => x)
            .ToList();

        if (xPositions.Count == 0) return new List<float>();

        // Cluster nearby X positions (within 20pt = roughly 0.28 inch)
        var clusters = new List<List<float>>();
        foreach (var x in xPositions)
        {
            var cluster = clusters.FirstOrDefault(cl => Math.Abs(cl.Average() - x) < 20f);
            if (cluster != null) cluster.Add(x);
            else clusters.Add(new List<float> { x });
        }

        // Return one boundary per cluster (median), only if the cluster appears in ≥30% of lines
        int minLines = Math.Max(1, (int)(multiColLines.Count * 0.3));
        return clusters
            .Where(cl => cl.Count >= minLines)
            .Select(cl => { cl.Sort(); return cl[cl.Count / 2]; })
            .OrderBy(x => x)
            .ToList();
    }

    private List<string> AssignToColumns(List<TextChunk> chunks, List<float> columns)
    {
        var cells = new string[columns.Count];
        Array.Fill(cells, "");

        foreach (var chunk in chunks)
        {
            // Find nearest column boundary
            int best = 0;
            float bestDist = float.MaxValue;
            for (int c = 0; c < columns.Count; c++)
            {
                float d = Math.Abs(chunk.X - columns[c]);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            cells[best] += (cells[best].Length > 0 ? " " : "") + chunk.Text;
        }

        return cells.ToList();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Strips XML-illegal control chars from text.</summary>
    private static string SanitizeText(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c == '\t' || c == '\n' || c == '\r' ||
                (c >= '\x20' && c <= '\uD7FF') ||
                (c >= '\uE000' && c <= '\uFFFD'))
                sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Quotes a CSV field per RFC 4180.
    /// Fields containing commas, double-quotes, or newlines are quoted.
    /// Internal double-quotes are doubled.
    /// </summary>
    private static string CsvQuote(string field)
    {
        if (string.IsNullOrEmpty(field)) return "\"\"";
        if (field.Contains(',') || field.Contains('"') ||
            field.Contains('\n') || field.Contains('\r'))
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
        return field;
    }
}
