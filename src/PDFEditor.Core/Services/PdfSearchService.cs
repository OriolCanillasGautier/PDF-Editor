using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace PDFEditor.Core.Services;

/// <summary>
/// Search results from a PDF text search
/// </summary>
public class PdfSearchResult
{
    public int PageNumber { get; set; }      // 1-based
    public string MatchedText { get; set; } = string.Empty;
    public string ContextBefore { get; set; } = string.Empty;
    public string ContextAfter { get; set; } = string.Empty;
    public int PositionInPageText { get; set; }

    public string DisplayText => $"Page {PageNumber}: ...{ContextBefore}{MatchedText}{ContextAfter}...";
}

/// <summary>
/// Full-text search across PDF pages with context snippets
/// </summary>
public class PdfSearchService
{
    /// <summary>
    /// Searches all pages for the given query string with surrounding context
    /// </summary>
    public List<PdfSearchResult> Search(byte[] pdfBytes, string query, bool caseSensitive = false)
    {
        if (string.IsNullOrEmpty(query)) return new List<PdfSearchResult>();

        var results = new List<PdfSearchResult>();
        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        var doc = new PdfDocument(reader);

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);
            var strategy = new SimpleTextExtractionStrategy();
            var pageText = PdfTextExtractor.GetTextFromPage(page, strategy);

            if (string.IsNullOrEmpty(pageText)) continue;

            int startIdx = 0;
            while (true)
            {
                int idx = pageText.IndexOf(query, startIdx, comparison);
                if (idx < 0) break;

                int ctxLen = 40;
                int beforeStart = Math.Max(0, idx - ctxLen);
                int afterEnd = Math.Min(pageText.Length, idx + query.Length + ctxLen);

                results.Add(new PdfSearchResult
                {
                    PageNumber = i,
                    MatchedText = pageText.Substring(idx, query.Length),
                    ContextBefore = pageText.Substring(beforeStart, idx - beforeStart).Replace("\n", " "),
                    ContextAfter = pageText.Substring(idx + query.Length, afterEnd - (idx + query.Length)).Replace("\n", " "),
                    PositionInPageText = idx
                });

                startIdx = idx + 1;
            }
        }

        doc.Close();
        return results;
    }

    /// <summary>
    /// Counts total occurrences across all pages
    /// </summary>
    public int CountOccurrences(byte[] pdfBytes, string query, bool caseSensitive = false)
    {
        return Search(pdfBytes, query, caseSensitive).Count;
    }

    /// <summary>
    /// Gets all text from all pages concatenated
    /// </summary>
    public string ExtractAllText(byte[] pdfBytes)
    {
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        var doc = new PdfDocument(reader);
        var sb = new System.Text.StringBuilder();

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            if (i > 1) sb.AppendLine().AppendLine($"--- Page {i} ---").AppendLine();
            var strategy = new SimpleTextExtractionStrategy();
            sb.Append(PdfTextExtractor.GetTextFromPage(doc.GetPage(i), strategy));
        }

        doc.Close();
        return sb.ToString();
    }
}
