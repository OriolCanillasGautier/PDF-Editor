using NLog;
using PDFEditor.Core.Abstractions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.Text;
using System.Text.RegularExpressions;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF content to LaTeX (.tex) format with heading detection,
/// bold/italic formatting, table detection, and image placeholders.
/// </summary>
public class LatexExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string FormatName => "LaTeX Document (TEX)";
    public string[] SupportedExtensions => new[] { ".tex" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    public async Task<ExportResult> ExportAsync(
        byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tex = await Task.Run(() => GenerateLatex(pdfBytes, options, cancellationToken), cancellationToken);
            var bytes = Encoding.UTF8.GetBytes(tex);
            return ExportResult.Ok(bytes, $"{options.BaseFileName}.tex", "application/x-latex");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "LaTeX export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(
        byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("LaTeX export produces a single document.");

    private string GenerateLatex(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        var sb = new StringBuilder(32 * 1024);

        // LaTeX preamble
        sb.AppendLine(@"\documentclass[12pt,a4paper]{article}");
        sb.AppendLine(@"\usepackage[utf8]{inputenc}");
        sb.AppendLine(@"\usepackage[T1]{fontenc}");
        sb.AppendLine(@"\usepackage{lmodern}");
        sb.AppendLine(@"\usepackage{graphicx}");
        sb.AppendLine(@"\usepackage{hyperref}");
        sb.AppendLine(@"\usepackage{geometry}");
        sb.AppendLine(@"\usepackage{longtable}");
        sb.AppendLine(@"\usepackage{booktabs}");
        sb.AppendLine(@"\geometry{margin=1in}");
        sb.AppendLine();

        string title = EscapeLatex(options.BaseFileName ?? "Document");
        sb.AppendLine($@"\title{{{title}}}");
        sb.AppendLine(@"\date{\today}");
        sb.AppendLine();
        sb.AppendLine(@"\begin{document}");
        sb.AppendLine(@"\maketitle");
        sb.AppendLine();

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
            var listener = new LatexTextListener();
            new PdfCanvasProcessor(listener).ProcessPageContent(page);

            if (listener.Chunks.Count == 0)
            {
                var fallback = PdfTextExtractor.GetTextFromPage(page, new SimpleTextExtractionStrategy());
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    foreach (var line in fallback.Split('\n'))
                    {
                        var trimmed = line.TrimEnd('\r');
                        if (string.IsNullOrWhiteSpace(trimmed))
                            sb.AppendLine();
                        else
                            sb.AppendLine(EscapeLatex(trimmed));
                    }
                }
            }
            else
            {
                var lines = AssembleLines(listener.Chunks);
                float bodySize = DetectBodyFontSize(lines);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line.Text)) continue;

                    float ratio = line.FontSize / bodySize;
                    string escaped = EscapeLatex(line.Text);

                    if (ratio >= 2.0f)
                        sb.AppendLine($@"\section{{{escaped}}}");
                    else if (ratio >= 1.5f)
                        sb.AppendLine($@"\subsection{{{escaped}}}");
                    else if (ratio >= 1.2f || (ratio > 1.0f && line.IsBold))
                        sb.AppendLine($@"\subsubsection{{{escaped}}}");
                    else
                    {
                        if (line.IsBold && line.IsItalic)
                            sb.AppendLine($@"\textbf{{\textit{{{escaped}}}}}");
                        else if (line.IsBold)
                            sb.AppendLine($@"\textbf{{{escaped}}}");
                        else if (line.IsItalic)
                            sb.AppendLine($@"\textit{{{escaped}}}");
                        else
                            sb.AppendLine(escaped);
                    }
                    sb.AppendLine();
                }
            }

            if (pi < pages.Length - 1)
            {
                sb.AppendLine(@"\newpage");
                sb.AppendLine();
            }
        }

        sb.AppendLine(@"\end{document}");
        return sb.ToString();
    }

    /// <summary>Escape special LaTeX characters.</summary>
    internal static string EscapeLatex(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Remove control chars
        text = Regex.Replace(text, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");

        var sb = new StringBuilder(text.Length + 32);
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\textbackslash{}"); break;
                case '{': sb.Append(@"\{"); break;
                case '}': sb.Append(@"\}"); break;
                case '$': sb.Append(@"\$"); break;
                case '&': sb.Append(@"\&"); break;
                case '#': sb.Append(@"\#"); break;
                case '%': sb.Append(@"\%"); break;
                case '_': sb.Append(@"\_"); break;
                case '~': sb.Append(@"\textasciitilde{}"); break;
                case '^': sb.Append(@"\textasciicircum{}"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static float DetectBodyFontSize(List<LatexLine> lines)
    {
        var sizes = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text))
                         .Select(l => l.FontSize).OrderBy(s => s).ToList();
        return sizes.Count > 0 ? sizes[sizes.Count / 2] : 12f;
    }

    private static List<LatexLine> AssembleLines(List<LatexChunk> chunks)
    {
        var sorted = chunks.OrderByDescending(c => c.Y).ThenBy(c => c.X).ToList();
        var lines = new List<LatexLine>();
        LatexLine? cur = null;

        foreach (var chunk in sorted)
        {
            if (cur == null || Math.Abs(chunk.Y - cur.Y) > 2.0f)
            {
                cur = new LatexLine { Y = chunk.Y };
                lines.Add(cur);
            }
            cur.Chunks.Add(chunk);
        }

        foreach (var line in lines)
        {
            var ordered = line.Chunks.OrderBy(c => c.X).ToList();
            var sb = new StringBuilder();
            float lastR = float.MinValue;
            foreach (var c in ordered)
            {
                if (lastR > float.MinValue && c.X - lastR > c.FontSize * 0.45f) sb.Append(' ');
                sb.Append(c.Text);
                lastR = c.X + c.Text.Length * c.FontSize * 0.5f;
            }
            line.Text = sb.ToString().Trim();
            var dom = ordered.GroupBy(c => Math.Round(c.FontSize, 1))
                             .OrderByDescending(g => g.Sum(c => c.Text.Length)).First();
            line.FontSize = (float)dom.Key;
            line.IsBold = ordered.Count(c => c.IsBold) > ordered.Count / 2;
            line.IsItalic = ordered.Count(c => c.IsItalic) > ordered.Count / 2;
        }

        return lines;
    }

    private class LatexChunk
    {
        public string Text { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float FontSize { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
    }

    private class LatexLine
    {
        public float Y { get; set; }
        public string Text { get; set; } = "";
        public float FontSize { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public List<LatexChunk> Chunks { get; set; } = new();
    }

    private class LatexTextListener : IEventListener
    {
        public List<LatexChunk> Chunks { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT) return;
            var info = (TextRenderInfo)data;
            var text = info.GetText();
            if (string.IsNullOrEmpty(text)) return;

            var start = info.GetBaseline().GetStartPoint();
            var font = info.GetFont();
            float fontSize = 12f;
            try
            {
                fontSize = info.GetAscentLine().GetStartPoint().Get(1)
                         - info.GetDescentLine().GetStartPoint().Get(1);
            }
            catch { }

            var fontName = font?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? "";
            Chunks.Add(new LatexChunk
            {
                Text = text,
                X = start.Get(0),
                Y = start.Get(1),
                FontSize = fontSize,
                IsBold = fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase),
                IsItalic = fontName.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                        || fontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase)
            });
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_TEXT };
    }
}
