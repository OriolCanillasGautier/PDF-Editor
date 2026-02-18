using System.Text;
using NLog;
using PDFEditor.Core.Abstractions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF text content to Rich Text Format (RTF).
/// Preserves basic paragraph structure with page breaks and headings.
/// </summary>
public class RtfExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string FormatName => "Rich Text Format (RTF)";
    public string[] SupportedExtensions => new[] { ".rtf" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    public async Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rtfBytes = await Task.Run(() =>
                GenerateRtf(pdfBytes, options, cancellationToken), cancellationToken);
            return ExportResult.Ok(rtfBytes, $"{options.BaseFileName}.rtf", "application/rtf");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RTF export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("RTF export produces a single document.");
    }

    private byte[] GenerateRtf(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        var sb = new StringBuilder();

        // RTF header
        sb.AppendLine(@"{\rtf1\ansi\ansicpg1252\deff0");
        sb.AppendLine(@"{\fonttbl{\f0\fswiss\fcharset0 Calibri;}{\f1\fnil\fcharset0 Courier New;}}");
        sb.AppendLine(@"{\colortbl;\red0\green0\blue0;\red70\green70\blue70;\red0\green90\blue200;}");
        sb.AppendLine(@"\viewkind4\uc1\pard\f0\fs22");

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdfDoc = new PdfDocument(reader);

        int pageCount = pdfDoc.GetNumberOfPages();
        var pageIndices = options.PageIndices ?? Enumerable.Range(0, pageCount).ToArray();

        for (int i = 0; i < pageIndices.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            int pageNum = pageIndices[i] + 1;
            if (pageNum < 1 || pageNum > pageCount) continue;

            // Page break before each page (except the first)
            if (i > 0)
                sb.AppendLine(@"\page");

            // Page header
            sb.AppendLine($@"\pard\cf3\b\fs28 Page {pageNum}\b0\cf0\fs22\par");
            sb.AppendLine(@"\pard\par");

            // Extract text
            var page = pdfDoc.GetPage(pageNum);
            var strategy = new SimpleTextExtractionStrategy();
            var text = PdfTextExtractor.GetTextFromPage(page, strategy);

            // Convert text to RTF paragraphs
            var lines = text.Split('\n');
            foreach (var line in lines)
            {
                var escaped = EscapeRtf(line);
                if (string.IsNullOrWhiteSpace(escaped))
                {
                    sb.AppendLine(@"\par");
                }
                else
                {
                    sb.Append(@"\pard ");
                    sb.Append(escaped);
                    sb.AppendLine(@"\par");
                }
            }
        }

        sb.AppendLine("}");

        Log.Info("RTF export completed: {Pages} pages", pageIndices.Length);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Escapes special RTF characters: backslash, braces, and non-ASCII chars.
    /// </summary>
    private static string EscapeRtf(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '{': sb.Append(@"\{"); break;
                case '}': sb.Append(@"\}"); break;
                default:
                    if (c > 127)
                        sb.Append($@"\u{(int)c}?");
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
