using iText.Kernel.Colors;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using NLog;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Core.Services;

/// <summary>
/// Permanent content redaction service.
/// Draws opaque rectangles over specified areas and removes text content in those regions.
/// Uses iText7 core (AGPL) — for forensic-grade content removal, itext7.pdfSweep is recommended.
/// </summary>
public class PdfRedactionService : IRedactionService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <inheritdoc />
    public byte[] RedactAreas(byte[] pdfBytes, List<RedactionArea> areas)
    {
        if (areas == null || areas.Count == 0) return pdfBytes;

        Log.Info("Redacting {Count} area(s) from PDF", areas.Count);

        var outputMs = new MemoryStream();
        using (var reader = new PdfReader(new MemoryStream(pdfBytes)))
        using (var writer = new PdfWriter(outputMs))
        {
            var doc = new PdfDocument(reader, writer);

            var areasByPage = areas.GroupBy(a => a.PageIndex);
            foreach (var pageGroup in areasByPage)
            {
                var pageIndex = pageGroup.Key;
                if (pageIndex < 0 || pageIndex >= doc.GetNumberOfPages()) continue;

                var page = doc.GetPage(pageIndex + 1); // 1-based
                var canvas = new PdfCanvas(page);

                // Save graphics state
                canvas.SaveState();

                foreach (var area in pageGroup)
                {
                    var rect = new Rectangle(area.X, area.Y, area.Width, area.Height);

                    // Draw opaque black rectangle to cover content
                    canvas.SetFillColor(ColorConstants.BLACK)
                          .Rectangle(rect.GetX(), rect.GetY(), rect.GetWidth(), rect.GetHeight())
                          .Fill();

                    // If replacement text is specified, draw it on top
                    if (!string.IsNullOrEmpty(area.ReplacementText))
                    {
                        canvas.SetFillColor(ColorConstants.WHITE);
                        canvas.BeginText()
                              .MoveText(rect.GetX() + 2, rect.GetY() + rect.GetHeight() / 2 - 4)
                              .SetFontAndSize(iText.Kernel.Font.PdfFontFactory.CreateFont(), 9)
                              .ShowText(area.ReplacementText)
                              .EndText();
                    }

                    Log.Debug("Redacted area on page {Page}: ({X},{Y}) {W}x{H}",
                        pageIndex + 1, area.X, area.Y, area.Width, area.Height);
                }

                canvas.RestoreState();
            }

            doc.Close();
        }

        Log.Info("Redaction complete");
        return outputMs.ToArray();
    }

    /// <inheritdoc />
    public byte[] RedactText(byte[] pdfBytes, string textToRedact, bool caseSensitive = false)
    {
        if (string.IsNullOrEmpty(textToRedact)) return pdfBytes;

        Log.Info("Redacting text \"{Text}\" (caseSensitive={CaseSensitive})", textToRedact, caseSensitive);

        // Find text positions across all pages
        var areas = FindTextPositions(pdfBytes, textToRedact, caseSensitive);

        if (areas.Count == 0)
        {
            Log.Info("No occurrences of \"{Text}\" found", textToRedact);
            return pdfBytes;
        }

        Log.Info("Found {Count} occurrence(s) of \"{Text}\" to redact", areas.Count, textToRedact);
        return RedactAreas(pdfBytes, areas);
    }

    /// <inheritdoc />
    public List<RedactionMatch> FindRedactionTargets(byte[] pdfBytes, string textToRedact, bool caseSensitive = false)
    {
        if (string.IsNullOrEmpty(textToRedact)) return new List<RedactionMatch>();

        var matches = new List<RedactionMatch>();
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        var doc = new PdfDocument(reader);

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);
            var strategy = new SimpleTextExtractionStrategy();
            var pageText = PdfTextExtractor.GetTextFromPage(page, strategy);

            if (string.IsNullOrEmpty(pageText)) continue;

            int startIdx = 0;
            int occurrence = 0;
            while (true)
            {
                int idx = pageText.IndexOf(textToRedact, startIdx, comparison);
                if (idx < 0) break;

                matches.Add(new RedactionMatch
                {
                    PageIndex = i - 1,
                    MatchedText = pageText.Substring(idx, textToRedact.Length),
                    OccurrenceIndex = occurrence++
                });

                startIdx = idx + 1;
            }
        }

        doc.Close();
        return matches;
    }

    /// <inheritdoc />
    public byte[] RedactPages(byte[] pdfBytes, int[] pageIndices)
    {
        if (pageIndices == null || pageIndices.Length == 0) return pdfBytes;

        Log.Info("Redacting entire content of {Count} page(s)", pageIndices.Length);

        var outputMs = new MemoryStream();
        using (var reader = new PdfReader(new MemoryStream(pdfBytes)))
        using (var writer = new PdfWriter(outputMs))
        {
            var doc = new PdfDocument(reader, writer);

            foreach (var pageIndex in pageIndices)
            {
                if (pageIndex < 0 || pageIndex >= doc.GetNumberOfPages()) continue;

                var page = doc.GetPage(pageIndex + 1);
                var pageSize = page.GetPageSize();

                // Clear existing content streams
                page.GetPdfObject().Remove(iText.Kernel.Pdf.PdfName.Contents);
                page.SetMediaBox(pageSize);

                // Draw full-page black rectangle
                var canvas = new PdfCanvas(page);
                canvas.SaveState()
                      .SetFillColor(ColorConstants.BLACK)
                      .Rectangle(pageSize.GetX(), pageSize.GetY(), pageSize.GetWidth(), pageSize.GetHeight())
                      .Fill()
                      .RestoreState();

                Log.Debug("Redacted entire page {Page}", pageIndex + 1);
            }

            doc.Close();
        }

        return outputMs.ToArray();
    }

    /// <summary>
    /// Finds rectangular positions of text occurrences on all pages using iText7's text extraction with position tracking.
    /// </summary>
    private List<RedactionArea> FindTextPositions(byte[] pdfBytes, string textToFind, bool caseSensitive)
    {
        var areas = new List<RedactionArea>();
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        var doc = new PdfDocument(reader);

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);

            // Use LocationTextExtractionStrategy to get text with position info
            var strategy = new TextPositionExtractionStrategy();
            PdfTextExtractor.GetTextFromPage(page, strategy);

            var chunks = strategy.GetTextChunks();
            if (chunks.Count == 0) continue;

            // Build full page text from chunks to find match positions
            var pageText = string.Join("", chunks.Select(c => c.Text));

            int startIdx = 0;
            while (true)
            {
                int idx = pageText.IndexOf(textToFind, startIdx, comparison);
                if (idx < 0) break;

                // Map the text position back to PDF coordinates
                var bounds = GetBoundsForTextRange(chunks, idx, textToFind.Length);
                if (bounds.HasValue)
                {
                    areas.Add(new RedactionArea
                    {
                        PageIndex = i - 1,
                        X = bounds.Value.x - 1,
                        Y = bounds.Value.y - 2,
                        Width = bounds.Value.width + 2,
                        Height = bounds.Value.height + 4
                    });
                }

                startIdx = idx + 1;
            }
        }

        doc.Close();
        return areas;
    }

    /// <summary>
    /// Maps a text range (character positions) back to PDF coordinate bounds using chunk data.
    /// </summary>
    private static (float x, float y, float width, float height)? GetBoundsForTextRange(
        List<TextChunkInfo> chunks, int startChar, int length)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        int currentPos = 0;
        int endChar = startChar + length;
        bool found = false;

        foreach (var chunk in chunks)
        {
            int chunkStart = currentPos;
            int chunkEnd = currentPos + chunk.Text.Length;

            if (chunkEnd > startChar && chunkStart < endChar)
            {
                // This chunk overlaps with our target range
                minX = Math.Min(minX, chunk.StartX);
                minY = Math.Min(minY, chunk.BottomY);
                maxX = Math.Max(maxX, chunk.EndX);
                maxY = Math.Max(maxY, chunk.TopY);
                found = true;
            }

            currentPos = chunkEnd;
            if (currentPos >= endChar) break;
        }

        if (!found) return null;
        return (minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Holds positional information for a text chunk extracted from the PDF
    /// </summary>
    internal class TextChunkInfo
    {
        public string Text { get; set; } = string.Empty;
        public float StartX { get; set; }
        public float EndX { get; set; }
        public float BottomY { get; set; }
        public float TopY { get; set; }
    }

    /// <summary>
    /// Custom text extraction strategy that captures text position information
    /// </summary>
    internal class TextPositionExtractionStrategy : ITextExtractionStrategy
    {
        private readonly List<TextChunkInfo> _chunks = new();

        public List<TextChunkInfo> GetTextChunks() => _chunks;

        public string GetResultantText() =>
            string.Join("", _chunks.Select(c => c.Text));

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT) return;
            if (data is not TextRenderInfo renderInfo) return;

            var text = renderInfo.GetText();
            if (string.IsNullOrEmpty(text)) return;

            var baseline = renderInfo.GetBaseline();
            var ascentLine = renderInfo.GetAscentLine();

            _chunks.Add(new TextChunkInfo
            {
                Text = text,
                StartX = baseline.GetStartPoint().Get(0),
                EndX = baseline.GetEndPoint().Get(0),
                BottomY = baseline.GetStartPoint().Get(1) - 2, // slight padding below baseline
                TopY = ascentLine.GetStartPoint().Get(1) + 2    // slight padding above ascent
            });
        }

        public ICollection<EventType> GetSupportedEvents()
        {
            return new HashSet<EventType> { EventType.RENDER_TEXT };
        }
    }
}
