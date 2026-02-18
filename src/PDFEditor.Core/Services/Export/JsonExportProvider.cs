using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using NLog;
using PDFEditor.Core.Abstractions;
using System.Text;
using System.Text.Json;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF content to structured JSON format.
/// Each page is represented as an object with page number, dimensions, and extracted text blocks.
/// Tables are approximated by grouping chunks sharing similar Y-positions.
/// </summary>
public class JsonExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string FormatName => "JSON";
    public string[] SupportedExtensions => new[] { ".json" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    // ─── IExportProvider ────────────────────────────────────────────────────────

    public async Task<ExportResult> ExportAsync(
        byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        Log.Info("JSON export started — {Bytes} bytes", pdfBytes.Length);

        return await Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(pdfBytes, writable: false);
                using var reader = new PdfReader(ms);
                using var doc = new PdfDocument(reader);

                int pageCount = doc.GetNumberOfPages();
                int[]? indices = options.PageIndices;
                if (indices == null || indices.Length == 0)
                    indices = Enumerable.Range(0, pageCount).ToArray();

                var pages = new List<JsonPage>();

                foreach (int idx in indices)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int pageNum = idx + 1;
                    if (pageNum < 1 || pageNum > pageCount) continue;

                    var pdfPage = doc.GetPage(pageNum);
                    var mediaBox = pdfPage.GetMediaBox();

                    var strategy = new JsonChunkStrategy();
                    var processor = new PdfCanvasProcessor(strategy);
                    processor.ProcessPageContent(pdfPage);

                    var chunks = strategy.Chunks;

                    // Group chunks into rows by Y-proximity (±4pt)
                    var rows = GroupIntoRows(chunks, tolerance: 4f);

                    pages.Add(new JsonPage
                    {
                        PageNumber = pageNum,
                        WidthPt = (float)mediaBox.GetWidth(),
                        HeightPt = (float)mediaBox.GetHeight(),
                        TextBlocks = rows,
                    });
                }

                var root = new JsonRoot
                {
                    TotalPages = pageCount,
                    ExportedPages = indices.Length,
                    Pages = pages,
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                };
                string json = JsonSerializer.Serialize(root, jsonOptions);
                byte[] data = Encoding.UTF8.GetBytes(json);

                string fileName = $"{options.BaseFileName}.json";
                Log.Info("JSON export complete — {Pages} pages, {Bytes} bytes", pages.Count, data.Length);
                return ExportResult.Ok(data, fileName, "application/json");
            }
            catch (OperationCanceledException)
            {
                return ExportResult.Fail("Export cancelled.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "JSON export failed");
                return ExportResult.Fail($"JSON export failed: {ex.Message}");
            }
        }, cancellationToken);
    }

    public Task<List<ExportResult>> ExportPagesAsync(
        byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // JSON batch = single file for all pages
        return ExportAsync(pdfBytes, options, cancellationToken)
               .ContinueWith(t => new List<ExportResult> { t.Result }, cancellationToken);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Groups sorted text chunks into horizontal rows based on Y-coordinate proximity.
    /// </summary>
    private static List<TextBlock> GroupIntoRows(List<TextChunk> chunks, float tolerance)
    {
        if (chunks.Count == 0) return new();

        // Sort top-to-bottom (Y decreasing in PDF coords), then left-to-right
        chunks.Sort((a, b) =>
        {
            float dy = b.Y - a.Y;
            if (Math.Abs(dy) > tolerance) return Math.Sign(dy);
            return a.X.CompareTo(b.X);
        });

        var rows = new List<TextBlock>();
        float currentY = chunks[0].Y;
        var rowText = new StringBuilder();
        var rowCells = new List<string>();
        float firstX = chunks[0].X;
        float lastX = chunks[0].X;
        float prevX = chunks[0].X;
        float prevWidth = 0;

        void FlushRow()
        {
            string text = rowText.ToString().Trim();
            if (text.Length == 0) return;
            rows.Add(new TextBlock
            {
                Text = text,
                X = firstX,
                Y = currentY,
                Cells = rowCells.Count > 1 ? new List<string>(rowCells) : null,
            });
        }

        foreach (var chunk in chunks)
        {
            float dy = Math.Abs(chunk.Y - currentY);
            if (dy > tolerance)
            {
                // New row
                FlushRow();
                rowText.Clear();
                rowCells.Clear();
                currentY = chunk.Y;
                firstX = chunk.X;
                prevX = chunk.X;
                prevWidth = 0;
            }

            // Detect column gap (cells in a table row)
            float gap = chunk.X - (prevX + prevWidth);
            if (rowCells.Count == 0)
                rowCells.Add(string.Empty);
            if (gap > 20f)
                rowCells.Add(string.Empty);

            rowCells[^1] += chunk.Text;
            rowText.Append(chunk.Text);
            lastX = chunk.X + chunk.Width;
            prevX = chunk.X;
            prevWidth = chunk.Width;
        }
        FlushRow();

        return rows;
    }

    // ─── Inner types ────────────────────────────────────────────────────────────

    private class JsonChunkStrategy : IEventListener
    {
        public List<TextChunk> Chunks { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT) return;
            var info = (TextRenderInfo)data;

            string text = info.GetText();
            if (string.IsNullOrEmpty(text)) return;

            var baseline = info.GetBaseline();
            var start = baseline.GetStartPoint();
            var end = baseline.GetEndPoint();

            Chunks.Add(new TextChunk
            {
                Text = text,
                X = start.Get(0),
                Y = start.Get(1),
                Width = end.Get(0) - start.Get(0),
            });
        }

        public ICollection<EventType> GetSupportedEvents() =>
            new[] { EventType.RENDER_TEXT };
    }

    private class TextChunk
    {
        public string Text { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
    }

    private class JsonRoot
    {
        public int TotalPages { get; set; }
        public int ExportedPages { get; set; }
        public List<JsonPage> Pages { get; set; } = new();
    }

    private class JsonPage
    {
        public int PageNumber { get; set; }
        public float WidthPt { get; set; }
        public float HeightPt { get; set; }
        public List<TextBlock> TextBlocks { get; set; } = new();
    }

    private class TextBlock
    {
        public string Text { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        /// <summary>Non-null when multiple column cells are detected in this row.</summary>
        public List<string>? Cells { get; set; }
    }
}
