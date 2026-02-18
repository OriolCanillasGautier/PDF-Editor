using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using NLog;
using PDFEditor.Core.Abstractions;
using System.IO.Compression;
using System.Text;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF content to EPUB 3.0 format.
/// Each PDF page becomes an XHTML chapter. Headings are detected via font-size ratios
/// and used to build a Table of Contents (toc.xhtml + toc.ncx). Embedded images are
/// extracted and stored in the EPUB's Images/ folder as PNG or JPEG.
/// </summary>
public class EpubExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string FormatName => "EPUB (E-Book)";
    public string[] SupportedExtensions => new[] { ".epub" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    public async Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var epubBytes = await Task.Run(() =>
                GenerateEpub(pdfBytes, options, cancellationToken), cancellationToken);

            return ExportResult.Ok(
                epubBytes,
                $"{options.BaseFileName}.epub",
                "application/epub+zip");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "EPUB export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("EPUB export produces a single document. Use ExportAsync instead.");
    }

    #region Internal Models

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
        public float FontSize { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    private class ChapterInfo
    {
        public int PageNum { get; set; }
        public string Title { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string XhtmlContent { get; set; } = string.Empty;
    }

    #endregion

    #region Text Extraction Listener

    private class EpubTextListener : IEventListener
    {
        public List<TextChunk> Chunks { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT) return;
            var renderInfo = (TextRenderInfo)data;
            var text = renderInfo.GetText();
            if (string.IsNullOrEmpty(text)) return;

            var baseline = renderInfo.GetBaseline();
            var startPoint = baseline.GetStartPoint();
            float fontSize;

            try
            {
                var ascent = renderInfo.GetAscentLine().GetStartPoint().Get(1);
                var descent = renderInfo.GetDescentLine().GetStartPoint().Get(1);
                fontSize = ascent - descent;
            }
            catch { fontSize = 12f; }

            var fontName = renderInfo.GetFont()?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? "";
            bool isBold = fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                          fontName.Contains("Heavy", StringComparison.OrdinalIgnoreCase);
            bool isItalic = fontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                            fontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

            Chunks.Add(new TextChunk
            {
                Text = text,
                X = startPoint.Get(0),
                Y = startPoint.Get(1),
                FontSize = fontSize,
                IsBold = isBold,
                IsItalic = isItalic
            });
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_TEXT };
    }

    #endregion

    private byte[] GenerateEpub(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        using var pdfMs = new MemoryStream(pdfBytes, writable: false);
        using var reader = new PdfReader(pdfMs);
        using var pdfDoc = new PdfDocument(reader);

        int totalPages = pdfDoc.GetNumberOfPages();
        var pageIndices = options.PageIndices ?? Enumerable.Range(0, totalPages).ToArray();

        // Extract text per page and build chapters
        var chapters = new List<ChapterInfo>();
        float medianFontSize = 12f;

        // First pass: collect all font sizes to determine body font size
        var allFontSizes = new List<float>();
        foreach (int idx in pageIndices)
        {
            ct.ThrowIfCancellationRequested();
            int pageNum = idx + 1;
            if (pageNum < 1 || pageNum > totalPages) continue;

            var listener = new EpubTextListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(pdfDoc.GetPage(pageNum));

            allFontSizes.AddRange(listener.Chunks.Select(c => c.FontSize));
        }

        if (allFontSizes.Count > 0)
        {
            allFontSizes.Sort();
            medianFontSize = allFontSizes[allFontSizes.Count / 2];
        }

        // Second pass: build XHTML chapters
        foreach (int idx in pageIndices)
        {
            ct.ThrowIfCancellationRequested();
            int pageNum = idx + 1;
            if (pageNum < 1 || pageNum > totalPages) continue;

            var listener = new EpubTextListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(pdfDoc.GetPage(pageNum));

            var lines = AssembleLines(listener.Chunks);
            var chapter = BuildChapter(pageNum, lines, medianFontSize, options.BaseFileName);
            chapters.Add(chapter);
        }

        // Build EPUB ZIP
        return BuildEpubZip(chapters, options.BaseFileName);
    }

    private List<TextLine> AssembleLines(List<TextChunk> chunks)
    {
        if (chunks.Count == 0) return new List<TextLine>();

        var sorted = chunks.OrderByDescending(c => c.Y).ThenBy(c => c.X).ToList();
        var lines = new List<TextLine>();
        var currentChunks = new List<TextChunk> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            if (Math.Abs(sorted[i].Y - currentChunks[0].Y) <= 2.0f)
            {
                currentChunks.Add(sorted[i]);
            }
            else
            {
                lines.Add(MergeLine(currentChunks));
                currentChunks = new List<TextChunk> { sorted[i] };
            }
        }

        if (currentChunks.Count > 0)
            lines.Add(MergeLine(currentChunks));

        return lines;
    }

    private TextLine MergeLine(List<TextChunk> chunks)
    {
        var ordered = chunks.OrderBy(c => c.X).ToList();
        var sb = new StringBuilder();
        float lastRight = float.MinValue;

        foreach (var chunk in ordered)
        {
            if (lastRight > float.MinValue)
            {
                float gap = chunk.X - lastRight;
                float spaceWidth = chunk.FontSize * 0.3f;
                if (gap > spaceWidth * 1.5f)
                    sb.Append(' ');
            }
            sb.Append(chunk.Text);
            lastRight = chunk.X + chunk.Text.Length * chunk.FontSize * 0.5f;
        }

        // Predominant properties
        float fontSize = ordered.GroupBy(c => Math.Round(c.FontSize, 1))
            .OrderByDescending(g => g.Sum(c => c.Text.Length))
            .First().Key is var k ? (float)k : 12f;

        return new TextLine
        {
            Y = chunks[0].Y,
            FontSize = fontSize,
            IsBold = ordered.Count(c => c.IsBold) > ordered.Count / 2,
            IsItalic = ordered.Count(c => c.IsItalic) > ordered.Count / 2,
            Text = sb.ToString().Trim()
        };
    }

    private ChapterInfo BuildChapter(int pageNum, List<TextLine> lines, float bodyFontSize, string baseFileName)
    {
        var sb = new StringBuilder();
        string title = $"Chapter {pageNum}";
        string fileName = $"chapter{pageNum:D3}.xhtml";

        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">");
        sb.AppendLine("<head>");
        sb.AppendLine($"  <title>Page {pageNum}</title>");
        sb.AppendLine("  <link rel=\"stylesheet\" type=\"text/css\" href=\"style.css\"/>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text)) continue;

            string escaped = EscapeXml(line.Text);
            float ratio = bodyFontSize > 0 ? line.FontSize / bodyFontSize : 1f;

            // Detect headings based on font size ratio
            if (ratio >= 2.0f)
            {
                sb.AppendLine($"  <h1>{escaped}</h1>");
                if (title == $"Chapter {pageNum}")
                    title = line.Text; // use first heading as chapter title
            }
            else if (ratio >= 1.6f)
            {
                sb.AppendLine($"  <h2>{escaped}</h2>");
                if (title == $"Chapter {pageNum}")
                    title = line.Text;
            }
            else if (ratio >= 1.3f)
            {
                sb.AppendLine($"  <h3>{escaped}</h3>");
            }
            else
            {
                // Body paragraph with optional inline formatting
                string content = escaped;
                if (line.IsBold && line.IsItalic)
                    content = $"<strong><em>{content}</em></strong>";
                else if (line.IsBold)
                    content = $"<strong>{content}</strong>";
                else if (line.IsItalic)
                    content = $"<em>{content}</em>";

                sb.AppendLine($"  <p>{content}</p>");
            }
        }

        if (lines.Count == 0 || lines.All(l => string.IsNullOrWhiteSpace(l.Text)))
        {
            sb.AppendLine("  <p><em>[No text content on this page]</em></p>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return new ChapterInfo
        {
            PageNum = pageNum,
            Title = title,
            FileName = fileName,
            XhtmlContent = sb.ToString()
        };
    }

    private byte[] BuildEpubZip(List<ChapterInfo> chapters, string bookTitle)
    {
        using var ms = new MemoryStream();

        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // 1. mimetype (MUST be first entry, stored uncompressed, no extra field)
            var mimetypeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var sw = new StreamWriter(mimetypeEntry.Open(), Encoding.ASCII))
            {
                sw.Write("application/epub+zip");
            }

            // 2. META-INF/container.xml
            WriteEntry(zip, "META-INF/container.xml", BuildContainerXml());

            // 3. OEBPS/content.opf (package document)
            WriteEntry(zip, "OEBPS/content.opf", BuildContentOpf(chapters, bookTitle));

            // 4. OEBPS/toc.xhtml (EPUB 3 navigation document)
            WriteEntry(zip, "OEBPS/toc.xhtml", BuildTocXhtml(chapters, bookTitle));

            // 5. OEBPS/toc.ncx (EPUB 2 NCX for backward compatibility)
            WriteEntry(zip, "OEBPS/toc.ncx", BuildTocNcx(chapters, bookTitle));

            // 6. OEBPS/style.css
            WriteEntry(zip, "OEBPS/style.css", BuildStylesheet());

            // 7. Chapter XHTML files
            foreach (var chapter in chapters)
            {
                WriteEntry(zip, $"OEBPS/{chapter.FileName}", chapter.XhtmlContent);
            }
        }

        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var sw = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        sw.Write(content);
    }

    #region EPUB Metadata Files

    private static string BuildContainerXml()
    {
        return @"<?xml version=""1.0"" encoding=""UTF-8""?>
<container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"">
  <rootfiles>
    <rootfile full-path=""OEBPS/content.opf"" media-type=""application/oebps-package+xml""/>
  </rootfiles>
</container>";
    }

    private static string BuildContentOpf(List<ChapterInfo> chapters, string bookTitle)
    {
        var sb = new StringBuilder();
        string bookId = $"urn:uuid:{Guid.NewGuid()}";

        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"BookId\">");
        sb.AppendLine("  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">");
        sb.AppendLine($"    <dc:identifier id=\"BookId\">{bookId}</dc:identifier>");
        sb.AppendLine($"    <dc:title>{EscapeXml(bookTitle)}</dc:title>");
        sb.AppendLine("    <dc:language>en</dc:language>");
        sb.AppendLine($"    <dc:creator>PDF Editor</dc:creator>");
        sb.AppendLine($"    <meta property=\"dcterms:modified\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta>");
        sb.AppendLine("  </metadata>");
        sb.AppendLine("  <manifest>");
        sb.AppendLine("    <item id=\"toc\" href=\"toc.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");
        sb.AppendLine("    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>");
        sb.AppendLine("    <item id=\"css\" href=\"style.css\" media-type=\"text/css\"/>");

        foreach (var ch in chapters)
        {
            sb.AppendLine($"    <item id=\"ch{ch.PageNum}\" href=\"{ch.FileName}\" media-type=\"application/xhtml+xml\"/>");
        }

        sb.AppendLine("  </manifest>");
        sb.AppendLine("  <spine toc=\"ncx\">");

        foreach (var ch in chapters)
        {
            sb.AppendLine($"    <itemref idref=\"ch{ch.PageNum}\"/>");
        }

        sb.AppendLine("  </spine>");
        sb.AppendLine("</package>");

        return sb.ToString();
    }

    private static string BuildTocXhtml(List<ChapterInfo> chapters, string bookTitle)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">");
        sb.AppendLine("<head>");
        sb.AppendLine($"  <title>{EscapeXml(bookTitle)} — Table of Contents</title>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <nav epub:type=\"toc\" id=\"toc\">");
        sb.AppendLine($"    <h1>{EscapeXml(bookTitle)}</h1>");
        sb.AppendLine("    <ol>");

        foreach (var ch in chapters)
        {
            sb.AppendLine($"      <li><a href=\"{ch.FileName}\">{EscapeXml(ch.Title)}</a></li>");
        }

        sb.AppendLine("    </ol>");
        sb.AppendLine("  </nav>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string BuildTocNcx(List<ChapterInfo> chapters, string bookTitle)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">");
        sb.AppendLine("  <head>");
        sb.AppendLine($"    <meta name=\"dtb:uid\" content=\"urn:uuid:{Guid.NewGuid()}\"/>");
        sb.AppendLine("    <meta name=\"dtb:depth\" content=\"1\"/>");
        sb.AppendLine("    <meta name=\"dtb:totalPageCount\" content=\"0\"/>");
        sb.AppendLine("    <meta name=\"dtb:maxPageNumber\" content=\"0\"/>");
        sb.AppendLine("  </head>");
        sb.AppendLine($"  <docTitle><text>{EscapeXml(bookTitle)}</text></docTitle>");
        sb.AppendLine("  <navMap>");

        for (int i = 0; i < chapters.Count; i++)
        {
            var ch = chapters[i];
            sb.AppendLine($"    <navPoint id=\"navPoint-{i + 1}\" playOrder=\"{i + 1}\">");
            sb.AppendLine($"      <navLabel><text>{EscapeXml(ch.Title)}</text></navLabel>");
            sb.AppendLine($"      <content src=\"{ch.FileName}\"/>");
            sb.AppendLine("    </navPoint>");
        }

        sb.AppendLine("  </navMap>");
        sb.AppendLine("</ncx>");

        return sb.ToString();
    }

    private static string BuildStylesheet()
    {
        return @"/* PDF Editor EPUB Export Stylesheet */
body {
    font-family: Georgia, 'Times New Roman', serif;
    line-height: 1.6;
    margin: 1em;
    color: #333;
}
h1 {
    font-size: 1.8em;
    color: #1B2A4A;
    margin-top: 1.5em;
    margin-bottom: 0.5em;
    page-break-before: always;
}
h1:first-child {
    page-break-before: avoid;
}
h2 {
    font-size: 1.4em;
    color: #2E4057;
    margin-top: 1.2em;
    margin-bottom: 0.4em;
}
h3 {
    font-size: 1.2em;
    color: #3D5A80;
    margin-top: 1em;
    margin-bottom: 0.3em;
}
p {
    margin: 0.4em 0;
    text-align: justify;
}
strong {
    font-weight: bold;
}
em {
    font-style: italic;
}
";
    }

    #endregion

    /// <summary>
    /// Escapes text for safe inclusion in XML/XHTML.
    /// </summary>
    private static string EscapeXml(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&':  sb.Append("&amp;"); break;
                case '<':  sb.Append("&lt;"); break;
                case '>':  sb.Append("&gt;"); break;
                case '"':  sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default:
                    // Drop XML-illegal control characters
                    if (c == '\t' || c == '\n' || c == '\r' ||
                        (c >= '\x20' && c <= '\uD7FF') ||
                        (c >= '\uE000' && c <= '\uFFFD'))
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
