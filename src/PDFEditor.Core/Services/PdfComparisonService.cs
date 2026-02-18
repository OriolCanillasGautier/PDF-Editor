using System.Text;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using NLog;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Core.Services;

/// <summary>
/// Text-based document comparison service.
/// Compares extracted text content between two PDF documents line-by-line.
/// </summary>
public class PdfComparisonService : IComparisonService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <inheritdoc />
    public ComparisonResult Compare(byte[] leftPdfBytes, byte[] rightPdfBytes,
        string leftFileName = "Document A", string rightFileName = "Document B")
    {
        Log.Info("Comparing documents: \"{Left}\" vs \"{Right}\"", leftFileName, rightFileName);

        var result = new ComparisonResult
        {
            LeftFileName = leftFileName,
            RightFileName = rightFileName
        };

        // Extract text per page from both documents
        var leftPages = ExtractPageTexts(leftPdfBytes);
        var rightPages = ExtractPageTexts(rightPdfBytes);

        result.PagesInLeft = leftPages.Count;
        result.PagesInRight = rightPages.Count;

        // Compare metadata
        CompareMetadata(leftPdfBytes, rightPdfBytes, result);

        // Compare pages
        int maxPages = Math.Max(leftPages.Count, rightPages.Count);

        for (int i = 0; i < maxPages; i++)
        {
            if (i >= leftPages.Count)
            {
                // Page exists only in right document
                result.Differences.Add(new DocumentDifference
                {
                    Type = DifferenceType.PageAdded,
                    LeftPageNumber = 0,
                    RightPageNumber = i + 1,
                    RightText = rightPages[i],
                    Description = $"Page {i + 1} added in \"{rightFileName}\""
                });
                continue;
            }

            if (i >= rightPages.Count)
            {
                // Page exists only in left document
                result.Differences.Add(new DocumentDifference
                {
                    Type = DifferenceType.PageRemoved,
                    LeftPageNumber = i + 1,
                    RightPageNumber = 0,
                    LeftText = leftPages[i],
                    Description = $"Page {i + 1} removed from \"{leftFileName}\""
                });
                continue;
            }

            // Both pages exist — compare line by line
            ComparePageLines(leftPages[i], rightPages[i], i + 1, result);
        }

        result.AddedCount = result.Differences.Count(d =>
            d.Type == DifferenceType.Added || d.Type == DifferenceType.PageAdded);
        result.RemovedCount = result.Differences.Count(d =>
            d.Type == DifferenceType.Removed || d.Type == DifferenceType.PageRemoved);
        result.ModifiedCount = result.Differences.Count(d =>
            d.Type == DifferenceType.Modified);

        Log.Info("Comparison complete: {Total} difference(s) ({Added} added, {Removed} removed, {Modified} modified)",
            result.TotalDifferences, result.AddedCount, result.RemovedCount, result.ModifiedCount);

        return result;
    }

    /// <inheritdoc />
    public string GenerateReport(ComparisonResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════════════");
        sb.AppendLine("                  DOCUMENT COMPARISON REPORT              ");
        sb.AppendLine("═══════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"  Left document:  {result.LeftFileName} ({result.PagesInLeft} pages)");
        sb.AppendLine($"  Right document: {result.RightFileName} ({result.PagesInRight} pages)");
        sb.AppendLine($"  Total differences: {result.TotalDifferences}");
        sb.AppendLine($"    Added: {result.AddedCount}  |  Removed: {result.RemovedCount}  |  Modified: {result.ModifiedCount}");
        sb.AppendLine();

        if (result.AreIdentical)
        {
            sb.AppendLine("  ✓ Documents are text-identical.");
            return sb.ToString();
        }

        sb.AppendLine("───────────────────────────────────────────────────────────");
        sb.AppendLine("  DIFFERENCES");
        sb.AppendLine("───────────────────────────────────────────────────────────");

        int diffNum = 1;
        foreach (var diff in result.Differences)
        {
            sb.AppendLine();
            sb.AppendLine($"  [{diffNum}] {DiffTypeSymbol(diff.Type)} {diff.Description}");

            if (diff.Type == DifferenceType.Modified)
            {
                sb.AppendLine($"      - (Left)  \"{TruncateText(diff.LeftText, 120)}\"");
                sb.AppendLine($"      + (Right) \"{TruncateText(diff.RightText, 120)}\"");
            }
            else if (diff.Type == DifferenceType.Added || diff.Type == DifferenceType.PageAdded)
            {
                if (!string.IsNullOrEmpty(diff.RightText))
                    sb.AppendLine($"      + \"{TruncateText(diff.RightText, 120)}\"");
            }
            else if (diff.Type == DifferenceType.Removed || diff.Type == DifferenceType.PageRemoved)
            {
                if (!string.IsNullOrEmpty(diff.LeftText))
                    sb.AppendLine($"      - \"{TruncateText(diff.LeftText, 120)}\"");
            }

            diffNum++;
        }

        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════");
        return sb.ToString();
    }

    /// <inheritdoc />
    public string GenerateHtmlReport(ComparisonResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset='utf-8'/>");
        sb.AppendLine("<title>Document Comparison Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("  body { font-family: 'Segoe UI', Arial, sans-serif; margin: 20px; background: #f5f5f5; }");
        sb.AppendLine("  .header { background: #2c3e50; color: white; padding: 20px; border-radius: 8px; margin-bottom: 20px; }");
        sb.AppendLine("  .summary { display: flex; gap: 15px; margin: 15px 0; }");
        sb.AppendLine("  .stat { background: white; padding: 12px 20px; border-radius: 6px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }");
        sb.AppendLine("  .stat.added { border-left: 4px solid #27ae60; }");
        sb.AppendLine("  .stat.removed { border-left: 4px solid #e74c3c; }");
        sb.AppendLine("  .stat.modified { border-left: 4px solid #f39c12; }");
        sb.AppendLine("  .diff { background: white; margin: 8px 0; padding: 12px 16px; border-radius: 6px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }");
        sb.AppendLine("  .diff-added { border-left: 4px solid #27ae60; }");
        sb.AppendLine("  .diff-removed { border-left: 4px solid #e74c3c; }");
        sb.AppendLine("  .diff-modified { border-left: 4px solid #f39c12; }");
        sb.AppendLine("  .diff-meta { border-left: 4px solid #3498db; }");
        sb.AppendLine("  .diff-title { font-weight: 600; margin-bottom: 6px; }");
        sb.AppendLine("  .text-left { background: #fce4e4; padding: 4px 8px; border-radius: 4px; font-family: monospace; word-break: break-all; }");
        sb.AppendLine("  .text-right { background: #e4fce4; padding: 4px 8px; border-radius: 4px; font-family: monospace; word-break: break-all; }");
        sb.AppendLine("  .identical { color: #27ae60; font-size: 1.2em; text-align: center; padding: 30px; }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<div class='header'>");
        sb.AppendLine("  <h1>Document Comparison Report</h1>");
        sb.AppendLine($"  <p>Left: <strong>{Escape(result.LeftFileName)}</strong> ({result.PagesInLeft} pages) &nbsp;|&nbsp; ");
        sb.AppendLine($"  Right: <strong>{Escape(result.RightFileName)}</strong> ({result.PagesInRight} pages)</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='summary'>");
        sb.AppendLine($"  <div class='stat added'><strong>{result.AddedCount}</strong> Added</div>");
        sb.AppendLine($"  <div class='stat removed'><strong>{result.RemovedCount}</strong> Removed</div>");
        sb.AppendLine($"  <div class='stat modified'><strong>{result.ModifiedCount}</strong> Modified</div>");
        sb.AppendLine("</div>");

        if (result.AreIdentical)
        {
            sb.AppendLine("<div class='identical'>Documents are text-identical.</div>");
        }
        else
        {
            foreach (var diff in result.Differences)
            {
                var cssClass = diff.Type switch
                {
                    DifferenceType.Added or DifferenceType.PageAdded => "diff-added",
                    DifferenceType.Removed or DifferenceType.PageRemoved => "diff-removed",
                    DifferenceType.Modified => "diff-modified",
                    DifferenceType.MetadataChanged => "diff-meta",
                    _ => ""
                };

                sb.AppendLine($"<div class='diff {cssClass}'>");
                sb.AppendLine($"  <div class='diff-title'>{DiffTypeSymbol(diff.Type)} {Escape(diff.Description)}</div>");

                if (diff.Type == DifferenceType.Modified)
                {
                    sb.AppendLine($"  <div class='text-left'>- {Escape(TruncateText(diff.LeftText, 300))}</div>");
                    sb.AppendLine($"  <div class='text-right'>+ {Escape(TruncateText(diff.RightText, 300))}</div>");
                }
                else if (!string.IsNullOrEmpty(diff.RightText) &&
                         (diff.Type == DifferenceType.Added || diff.Type == DifferenceType.PageAdded))
                {
                    sb.AppendLine($"  <div class='text-right'>+ {Escape(TruncateText(diff.RightText, 300))}</div>");
                }
                else if (!string.IsNullOrEmpty(diff.LeftText) &&
                         (diff.Type == DifferenceType.Removed || diff.Type == DifferenceType.PageRemoved))
                {
                    sb.AppendLine($"  <div class='text-left'>- {Escape(TruncateText(diff.LeftText, 300))}</div>");
                }

                sb.AppendLine("</div>");
            }
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <inheritdoc />
    public bool AreIdentical(byte[] leftPdfBytes, byte[] rightPdfBytes)
    {
        var leftPages = ExtractPageTexts(leftPdfBytes);
        var rightPages = ExtractPageTexts(rightPdfBytes);

        if (leftPages.Count != rightPages.Count) return false;

        for (int i = 0; i < leftPages.Count; i++)
        {
            if (leftPages[i] != rightPages[i]) return false;
        }
        return true;
    }

    // ── Private helpers ──────────────────────────────────────────────

    private static List<string> ExtractPageTexts(byte[] pdfBytes)
    {
        var texts = new List<string>();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        var doc = new PdfDocument(reader);

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var strategy = new SimpleTextExtractionStrategy();
            var text = PdfTextExtractor.GetTextFromPage(doc.GetPage(i), strategy);
            texts.Add(text ?? string.Empty);
        }

        doc.Close();
        return texts;
    }

    private static void CompareMetadata(byte[] leftBytes, byte[] rightBytes, ComparisonResult result)
    {
        using var lr = new PdfReader(new MemoryStream(leftBytes));
        var ld = new PdfDocument(lr);
        using var rr = new PdfReader(new MemoryStream(rightBytes));
        var rd = new PdfDocument(rr);

        var leftInfo = ld.GetDocumentInfo();
        var rightInfo = rd.GetDocumentInfo();

        CompareMetadataField("Title", leftInfo.GetTitle(), rightInfo.GetTitle(), result);
        CompareMetadataField("Author", leftInfo.GetAuthor(), rightInfo.GetAuthor(), result);
        CompareMetadataField("Subject", leftInfo.GetSubject(), rightInfo.GetSubject(), result);

        ld.Close();
        rd.Close();
    }

    private static void CompareMetadataField(string field, string? left, string? right, ComparisonResult result)
    {
        left ??= string.Empty;
        right ??= string.Empty;

        if (left != right)
        {
            result.Differences.Add(new DocumentDifference
            {
                Type = DifferenceType.MetadataChanged,
                LeftText = left,
                RightText = right,
                Description = $"Metadata '{field}' changed: \"{left}\" → \"{right}\""
            });
        }
    }

    private static void ComparePageLines(string leftText, string rightText, int pageNumber, ComparisonResult result)
    {
        var leftLines = SplitIntoLines(leftText);
        var rightLines = SplitIntoLines(rightText);

        // Use simple LCS-based diff
        var diff = ComputeDiff(leftLines, rightLines);

        foreach (var entry in diff)
        {
            switch (entry.Type)
            {
                case DiffEntryType.Added:
                    result.Differences.Add(new DocumentDifference
                    {
                        Type = DifferenceType.Added,
                        LeftPageNumber = pageNumber,
                        RightPageNumber = pageNumber,
                        RightText = entry.Text,
                        LineNumber = entry.LineNumber,
                        Description = $"Page {pageNumber}, line {entry.LineNumber}: text added"
                    });
                    break;

                case DiffEntryType.Removed:
                    result.Differences.Add(new DocumentDifference
                    {
                        Type = DifferenceType.Removed,
                        LeftPageNumber = pageNumber,
                        RightPageNumber = pageNumber,
                        LeftText = entry.Text,
                        LineNumber = entry.LineNumber,
                        Description = $"Page {pageNumber}, line {entry.LineNumber}: text removed"
                    });
                    break;

                case DiffEntryType.Modified:
                    result.Differences.Add(new DocumentDifference
                    {
                        Type = DifferenceType.Modified,
                        LeftPageNumber = pageNumber,
                        RightPageNumber = pageNumber,
                        LeftText = entry.LeftText ?? string.Empty,
                        RightText = entry.Text,
                        LineNumber = entry.LineNumber,
                        Description = $"Page {pageNumber}, line {entry.LineNumber}: text modified"
                    });
                    break;
            }
        }
    }

    private static string[] SplitIntoLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
    }

    /// <summary>
    /// Computes a simplified line-by-line diff using Longest Common Subsequence (LCS).
    /// </summary>
    private static List<DiffEntry> ComputeDiff(string[] left, string[] right)
    {
        var entries = new List<DiffEntry>();

        // Build LCS matrix
        int m = left.Length, n = right.Length;
        var lcs = new int[m + 1, n + 1];

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (left[i - 1] == right[j - 1])
                    lcs[i, j] = lcs[i - 1, j - 1] + 1;
                else
                    lcs[i, j] = Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
            }
        }

        // Backtrack to find differences
        int li = m, ri = n;
        var reversedEntries = new List<DiffEntry>();

        while (li > 0 || ri > 0)
        {
            if (li > 0 && ri > 0 && left[li - 1] == right[ri - 1])
            {
                // Same line — no difference
                li--;
                ri--;
            }
            else if (ri > 0 && (li == 0 || lcs[li, ri - 1] >= lcs[li - 1, ri]))
            {
                // Line added in right
                reversedEntries.Add(new DiffEntry
                {
                    Type = DiffEntryType.Added,
                    Text = right[ri - 1],
                    LineNumber = ri
                });
                ri--;
            }
            else if (li > 0)
            {
                // Check if this is a modification (next right line differs) or removal
                if (ri > 0 && lcs[li - 1, ri - 1] >= lcs[li - 1, ri] && lcs[li - 1, ri - 1] >= lcs[li, ri - 1])
                {
                    // Modified line
                    reversedEntries.Add(new DiffEntry
                    {
                        Type = DiffEntryType.Modified,
                        Text = right[ri - 1],
                        LeftText = left[li - 1],
                        LineNumber = li
                    });
                    li--;
                    ri--;
                }
                else
                {
                    // Removed line
                    reversedEntries.Add(new DiffEntry
                    {
                        Type = DiffEntryType.Removed,
                        Text = left[li - 1],
                        LineNumber = li
                    });
                    li--;
                }
            }
        }

        reversedEntries.Reverse();
        return reversedEntries;
    }

    private static string DiffTypeSymbol(DifferenceType type) => type switch
    {
        DifferenceType.Added or DifferenceType.PageAdded => "[+]",
        DifferenceType.Removed or DifferenceType.PageRemoved => "[-]",
        DifferenceType.Modified => "[~]",
        DifferenceType.MetadataChanged => "[M]",
        _ => "[?]"
    };

    private static string TruncateText(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        text = text.Replace("\n", " ").Replace("\r", "");
        return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private enum DiffEntryType { Added, Removed, Modified }

    private class DiffEntry
    {
        public DiffEntryType Type { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? LeftText { get; set; }
        public int LineNumber { get; set; }
    }
}
