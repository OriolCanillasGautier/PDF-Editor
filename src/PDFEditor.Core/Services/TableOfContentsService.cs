using NLog;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Action;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Navigation;

namespace PDFEditor.Core.Services;

/// <summary>
/// Generates a table of contents (bookmarks/outlines) for a PDF based on font-size heading detection.
/// </summary>
public class TableOfContentsService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Represents a detected heading in the PDF.</summary>
    public class DetectedHeading
    {
        public int PageNumber { get; set; }
        public int Level { get; set; }
        public string Text { get; set; } = "";
        public float FontSize { get; set; }
        public float Y { get; set; }
    }

    /// <summary>
    /// Detects headings in the PDF based on font-size ratios.
    /// </summary>
    public List<DetectedHeading> DetectHeadings(byte[] pdfBytes, int[]? pageIndices = null)
    {
        var allChunks = new List<(int pageNum, float fontSize, bool isBold, string text, float y)>();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdfDoc = new PdfDocument(reader);

        int total = pdfDoc.GetNumberOfPages();
        int[] pages = pageIndices ?? Enumerable.Range(0, total).ToArray();

        foreach (int pi in pages)
        {
            int pageNum = pi + 1;
            if (pageNum < 1 || pageNum > total) continue;

            var page = pdfDoc.GetPage(pageNum);
            var listener = new HeadingListener();
            new PdfCanvasProcessor(listener).ProcessPageContent(page);

            // Group into lines
            var sorted = listener.Chunks.OrderByDescending(c => c.y).ThenBy(c => c.x).ToList();
            float lastY = float.MaxValue;
            var lineBuf = new List<(float fontSize, bool isBold, string text)>();

            foreach (var chunk in sorted)
            {
                if (Math.Abs(chunk.y - lastY) > 2.0f && lineBuf.Count > 0)
                {
                    var lineText = string.Join("", lineBuf.Select(c => c.text)).Trim();
                    if (!string.IsNullOrWhiteSpace(lineText))
                    {
                        var dom = lineBuf.GroupBy(c => Math.Round(c.fontSize, 1))
                                         .OrderByDescending(g => g.Sum(c => c.text.Length)).First();
                        allChunks.Add((pageNum, (float)dom.Key,
                            lineBuf.Count(c => c.isBold) > lineBuf.Count / 2, lineText, lastY));
                    }
                    lineBuf.Clear();
                }
                lastY = chunk.y;
                lineBuf.Add((chunk.fontSize, chunk.isBold, chunk.text));
            }
            if (lineBuf.Count > 0)
            {
                var lineText = string.Join("", lineBuf.Select(c => c.text)).Trim();
                if (!string.IsNullOrWhiteSpace(lineText))
                {
                    var dom = lineBuf.GroupBy(c => Math.Round(c.fontSize, 1))
                                     .OrderByDescending(g => g.Sum(c => c.text.Length)).First();
                    allChunks.Add((pageNum, (float)dom.Key,
                        lineBuf.Count(c => c.isBold) > lineBuf.Count / 2, lineText, lastY));
                }
            }
        }

        // Determine body size (median)
        var sizes = allChunks.Select(c => c.fontSize).OrderBy(s => s).ToList();
        float bodySize = sizes.Count > 0 ? sizes[sizes.Count / 2] : 12f;

        // Filter headings by size ratio
        var headings = new List<DetectedHeading>();
        foreach (var chunk in allChunks)
        {
            float ratio = chunk.fontSize / bodySize;
            int level = 0;
            if (ratio >= 2.0f) level = 1;
            else if (ratio >= 1.5f) level = 2;
            else if (ratio >= 1.2f) level = 3;
            else if (ratio > 1.0f && chunk.isBold) level = 4;

            if (level > 0 && chunk.text.Length <= 200)
            {
                headings.Add(new DetectedHeading
                {
                    PageNumber = chunk.pageNum,
                    Level = level,
                    Text = chunk.text,
                    FontSize = chunk.fontSize,
                    Y = chunk.y
                });
            }
        }

        Log.Info("Detected {Count} headings across {Pages} pages", headings.Count, pages.Length);
        return headings;
    }

    /// <summary>
    /// Adds PDF outline/bookmarks based on detected headings.
    /// </summary>
    public byte[] AddOutlines(byte[] pdfBytes, List<DetectedHeading>? headings = null)
    {
        headings ??= DetectHeadings(pdfBytes);
        if (headings.Count == 0) return pdfBytes;

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var outMs  = new MemoryStream();
        using var writer = new PdfWriter(outMs);
        using var pdfDoc = new PdfDocument(reader, writer);

        var outlines = pdfDoc.GetOutlines(true);
        // Clear existing outlines by iterating children
        var existing = outlines.GetAllChildren();
        if (existing != null)
        {
            // Remove all children from the outline tree
            foreach (var child in existing.ToList())
            {
                if (child is PdfOutline outline)
                {
                    try { outline.RemoveOutline(); }
                    catch { /* Ignore removal errors */ }
                }
            }
        }

        // Build hierarchy
        var stack = new Stack<(PdfOutline outline, int level)>();
        stack.Push((outlines, 0));

        foreach (var h in headings)
        {
            int pageNum = Math.Min(h.PageNumber, pdfDoc.GetNumberOfPages());
            var page = pdfDoc.GetPage(pageNum);
            float y = page.GetMediaBox().GetHeight(); // top of page as default

            while (stack.Count > 1 && stack.Peek().level >= h.Level)
                stack.Pop();

            var parent = stack.Peek().outline;
            var dest = PdfExplicitDestination.CreateXYZ(page, 0, y, 0);
            var child = parent.AddOutline(h.Text);
            child.AddDestination(dest);
            stack.Push((child, h.Level));
        }

        pdfDoc.Close();
        Log.Info("Added {Count} outline entries", headings.Count);
        return outMs.ToArray();
    }

    /// <summary>
    /// Generates a text table of contents.
    /// </summary>
    public string GenerateTocText(List<DetectedHeading> headings)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("TABLE OF CONTENTS");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();

        foreach (var h in headings)
        {
            string indent = new(' ', (h.Level - 1) * 4);
            sb.AppendLine($"{indent}{h.Text} .... {h.PageNumber}");
        }

        return sb.ToString();
    }

    private class HeadingListener : IEventListener
    {
        public List<(float x, float y, float fontSize, bool isBold, string text)> Chunks { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT) return;
            var info = (TextRenderInfo)data;
            var text = info.GetText();
            if (string.IsNullOrEmpty(text)) return;

            var start = info.GetBaseline().GetStartPoint();
            float fontSize = 12f;
            try
            {
                fontSize = info.GetAscentLine().GetStartPoint().Get(1)
                         - info.GetDescentLine().GetStartPoint().Get(1);
            }
            catch { }

            var fontName = info.GetFont()?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? "";
            bool bold = fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase);

            Chunks.Add((start.Get(0), start.Get(1), fontSize, bold, text));
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_TEXT };
    }
}
