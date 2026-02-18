using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using NLog;
using PDFEditor.Core.Abstractions;
using System.Text;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF content to Markdown (.md) format.
/// Detects headings (via font-size ratios), bold/italic runs, tables aligned by X position,
/// and bullet/numbered list items. Output is clean UTF-8 Markdown.
/// </summary>
public class MarkdownExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string FormatName => "Markdown";
    public string[] SupportedExtensions => new[] { ".md" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    // ─── IExportProvider ────────────────────────────────────────────────────────

    public async Task<ExportResult> ExportAsync(
        byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var md = await Task.Run(
                () => GenerateMarkdown(pdfBytes, options, cancellationToken),
                cancellationToken);

            var bytes = Encoding.UTF8.GetBytes(md);
            return ExportResult.Ok(bytes, $"{options.BaseFileName}.md", "text/markdown");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Markdown export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(
        byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Markdown export produces a single file. Use ExportAsync instead.");

    // ─── Internal Models ─────────────────────────────────────────────────────────

    private class TextChunk
    {
        public string Text { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float FontSize { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
    }

    private class TextLine
    {
        public float Y { get; set; }
        public List<TextChunk> Chunks { get; set; } = new();
        public string Text { get; set; } = string.Empty;
        public float PredominantFontSize { get; set; }
        public bool PredominantBold { get; set; }
        public bool PredominantItalic { get; set; }
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
                var asc = ri.GetAscentLine().GetStartPoint().Get(1);
                var desc = ri.GetDescentLine().GetStartPoint().Get(1);
                fontSize = asc - desc;
            }
            catch { }

            var fontName = ri.GetFont()?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? "";
            Chunks.Add(new TextChunk
            {
                Text = text,
                X = start.Get(0),
                Y = start.Get(1),
                FontSize = fontSize,
                IsBold = fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                         fontName.Contains("Heavy", StringComparison.OrdinalIgnoreCase),
                IsItalic = fontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                           fontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase)
            });
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_TEXT };
    }

    // ─── Core Processing ─────────────────────────────────────────────────────────

    private string GenerateMarkdown(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        var sb = new StringBuilder();

        // Document title
        sb.AppendLine($"# {EscapeMarkdown(options.BaseFileName)}");
        sb.AppendLine();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdfDoc = new PdfDocument(reader);

        int totalPages = pdfDoc.GetNumberOfPages();
        var pageIndices = options.PageIndices ?? Enumerable.Range(0, totalPages).ToArray();

        for (int idx = 0; idx < pageIndices.Length; idx++)
        {
            ct.ThrowIfCancellationRequested();
            int pageNum = pageIndices[idx] + 1;
            if (pageNum < 1 || pageNum > totalPages) continue;

            var page = pdfDoc.GetPage(pageNum);

            // Try structural extraction first
            var listener = new ChunkListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(page);

            if (listener.Chunks.Count > 0)
            {
                var lines = AssembleLines(listener.Chunks);
                if (pageIndices.Length > 1)
                {
                    sb.AppendLine($"---");
                    sb.AppendLine();
                    sb.AppendLine($"## Page {pageNum}");
                    sb.AppendLine();
                }
                RenderLines(lines, sb);
            }
            else
            {
                // Fallback: simple extraction
                var text = PdfTextExtractor.GetTextFromPage(page, new SimpleTextExtractionStrategy());
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (pageIndices.Length > 1)
                    {
                        sb.AppendLine($"---");
                        sb.AppendLine();
                        sb.AppendLine($"## Page {pageNum}");
                        sb.AppendLine();
                    }
                    foreach (var line in text.Split('\n'))
                        sb.AppendLine(CleanText(line));
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

            line.Text = sb.ToString().Trim();

            var byLen = ordered.GroupBy(c => Math.Round(c.FontSize, 1))
                               .OrderByDescending(g => g.Sum(c => c.Text.Length))
                               .First();
            line.PredominantFontSize = (float)byLen.Key;
            line.PredominantBold = ordered.Count(c => c.IsBold) > ordered.Count / 2;
            line.PredominantItalic = ordered.Count(c => c.IsItalic) > ordered.Count / 2;
        }

        return lines;
    }

    private void RenderLines(List<TextLine> lines, StringBuilder sb)
    {
        if (lines.Count == 0) return;

        // Compute body font size (median) for heading detection
        var fontSizes = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text))
                             .Select(l => l.PredominantFontSize)
                             .OrderBy(s => s).ToList();
        float bodySize = fontSizes.Count > 0 ? fontSizes[fontSizes.Count / 2] : 12f;

        // Detect table rows (2+ lines with multi-column alignment)
        var tableGroups = DetectTableRanges(lines);
        var tableLineIndices = new HashSet<int>(tableGroups.SelectMany(g => g));

        int i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line.Text)) { i++; continue; }

            // Table block?
            var tableGroup = tableGroups.FirstOrDefault(g => g.Contains(i));
            if (tableGroup != null)
            {
                RenderTable(lines, tableGroup, sb);
                i = tableGroup.Max() + 1;
                sb.AppendLine();
                continue;
            }

            float ratio = bodySize > 0 ? line.PredominantFontSize / bodySize : 1f;
            bool isHeading = ratio >= 1.2f || (ratio > 1.0f && line.PredominantBold);

            string text = CleanText(line.Text);

            if (isHeading)
            {
                string prefix = ratio >= 2.0f ? "## " :
                                ratio >= 1.5f ? "### " : "#### ";
                // Bold headings get bold MD too for lower levels
                sb.AppendLine($"{prefix}{(line.PredominantBold && !prefix.StartsWith("##") ? $"**{text}**" : text)}");
                sb.AppendLine();
            }
            else
            {
                // Detect list items
                string? listMarker = DetectListMarker(text, out string listContent);
                if (listMarker != null)
                {
                    sb.AppendLine($"{listMarker} {InlineFormat(listContent, line)}");
                }
                else
                {
                    sb.AppendLine(InlineFormat(text, line));
                }
            }

            i++;
        }

        sb.AppendLine();
    }

    // Detect consecutive lines that look like table rows (≥2 "columns" each)
    private List<List<int>> DetectTableRanges(List<TextLine> lines)
    {
        var groups = new List<List<int>>();
        int i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Chunks.Count >= 2 &&
                lines[i].Chunks.Select(c => c.X).Distinct().Count() >= 2)
            {
                var group = new List<int> { i };
                int j = i + 1;
                while (j < lines.Count &&
                       lines[j].Chunks.Count >= 2 &&
                       !string.IsNullOrWhiteSpace(lines[j].Text))
                {
                    group.Add(j);
                    j++;
                }
                if (group.Count >= 2)
                {
                    groups.Add(group);
                    i = j;
                    continue;
                }
            }
            i++;
        }
        return groups;
    }

    private void RenderTable(List<TextLine> lines, List<int> rowIndices, StringBuilder sb)
    {
        // Get column count and collect rows as string arrays
        var rows = rowIndices.Select(idx =>
        {
            // Split chunks into columns by X position clusters
            var chunks = lines[idx].Chunks.OrderBy(c => c.X).ToList();
            return CollapseIntoColumns(chunks);
        }).ToList();

        if (rows.Count == 0) return;

        int colCount = rows.Max(r => r.Count);
        // Pad all rows to same width
        var padded = rows.Select(r =>
        {
            while (r.Count < colCount) r.Add("");
            return r;
        }).ToList();

        // Header row
        sb.AppendLine("| " + string.Join(" | ", padded[0].Select(c => EscapeMarkdown(CleanText(c)))) + " |");
        // Separator
        sb.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", colCount)) + " |");
        // Data rows
        for (int r = 1; r < padded.Count; r++)
            sb.AppendLine("| " + string.Join(" | ", padded[r].Select(c => EscapeMarkdown(CleanText(c)))) + " |");
    }

    private List<string> CollapseIntoColumns(List<TextChunk> chunks)
    {
        if (chunks.Count == 0) return new List<string>();
        if (chunks.Count == 1) return new List<string> { chunks[0].Text };

        // Simple gap-based column splitting: large gap → new column
        var cols = new List<string>();
        var cur = new StringBuilder(chunks[0].Text);
        float prev = chunks[0].X + chunks[0].Text.Length * chunks[0].FontSize * 0.5f;
        float avgFontSize = chunks.Average(c => c.FontSize);

        for (int i = 1; i < chunks.Count; i++)
        {
            float gap = chunks[i].X - prev;
            if (gap > avgFontSize * 2.0f)
            {
                cols.Add(cur.ToString().Trim());
                cur = new StringBuilder(chunks[i].Text);
            }
            else
            {
                if (gap > 0) cur.Append(' ');
                cur.Append(chunks[i].Text);
            }
            prev = chunks[i].X + chunks[i].Text.Length * chunks[i].FontSize * 0.5f;
        }
        cols.Add(cur.ToString().Trim());
        return cols;
    }

    private string? DetectListMarker(string text, out string content)
    {
        if (text.Length > 2)
        {
            // Unordered: •, -, *, ◦, ▪, ▸
            if (text[0] is '•' or '◦' or '▪' or '▸' or '›')
            {
                content = text[1..].TrimStart();
                return "-";
            }
            if (text.Length > 3 && text[0] == '-' && text[1] == ' ')
            {
                content = text[2..];
                return "-";
            }
            if (text.Length > 3 && text[0] == '*' && text[1] == ' ')
            {
                content = text[2..];
                return "-";
            }
            // Ordered: 1. 1) 1:
            if (char.IsDigit(text[0]))
            {
                int k = 0;
                while (k < text.Length && char.IsDigit(text[k])) k++;
                if (k < text.Length && text[k] is '.' or ')' or ':' && k + 1 < text.Length && text[k + 1] == ' ')
                {
                    content = text[(k + 2)..];
                    return $"{text[..k]}.";
                }
            }
        }
        content = text;
        return null;
    }

    private string InlineFormat(string text, TextLine line)
    {
        if (line.PredominantBold && line.PredominantItalic) return $"***{text}***";
        if (line.PredominantBold) return $"**{text}**";
        if (line.PredominantItalic) return $"*{text}*";
        return text;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Strips XML-illegal chars and trims.</summary>
    private static string CleanText(string? input)
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

    /// <summary>Escapes Markdown special characters.</summary>
    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        // Escape pipe, backslash, backtick in table/inline contexts
        return text
            .Replace("\\", "\\\\")
            .Replace("|", "\\|")
            .Replace("`", "\\`");
    }
}
