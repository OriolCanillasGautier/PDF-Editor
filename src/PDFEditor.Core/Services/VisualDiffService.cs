using System.Text;
using ImageMagick;
using NLog;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Core.Services;

/// <summary>
/// Provides visual comparison of PDF documents by rendering pages to images
/// and producing pixel-level diff images with highlighted differences.
/// Extends PdfComparisonService with visual comparison capabilities.
/// </summary>
public class VisualDiffService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly PdfRenderService _renderService = new();
    private readonly PdfComparisonService _comparisonService;

    /// <summary>
    /// Result of a visual page comparison.
    /// </summary>
    public class VisualDiffPageResult
    {
        public int PageNumber { get; set; }
        public byte[]? LeftImage { get; set; }
        public byte[]? RightImage { get; set; }
        public byte[]? DiffImage { get; set; }
        public double DifferencePercent { get; set; }
        public bool HasDifferences => DifferencePercent > 0.01;
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Full visual comparison result across all pages.
    /// </summary>
    public class VisualDiffResult
    {
        public string LeftFileName { get; set; } = string.Empty;
        public string RightFileName { get; set; } = string.Empty;
        public List<VisualDiffPageResult> Pages { get; set; } = new();
        public int TotalPages => Pages.Count;
        public int PagesWithDifferences => Pages.Count(p => p.HasDifferences);
        public double OverallDifferencePercent => Pages.Count > 0
            ? Pages.Average(p => p.DifferencePercent) : 0;
        public ComparisonResult? TextComparison { get; set; }
    }

    /// <summary>
    /// Options for visual diff rendering.
    /// </summary>
    public class VisualDiffOptions
    {
        public int Dpi { get; set; } = 150;
        public string HighlightColor { get; set; } = "#FF0000";
        public double HighlightOpacity { get; set; } = 0.5;
        public double Threshold { get; set; } = 0.05; // pixel difference threshold (0-1)
        public bool IncludeTextComparison { get; set; } = true;
        public bool GenerateSideBySide { get; set; } = true;
    }

    public VisualDiffService(PdfComparisonService? comparisonService = null)
    {
        _comparisonService = comparisonService ?? new PdfComparisonService();
    }

    /// <summary>
    /// Performs a visual comparison of two PDF documents page by page.
    /// Renders each page to images and computes pixel-level differences.
    /// </summary>
    public async Task<VisualDiffResult> CompareVisuallyAsync(
        byte[] leftPdfBytes, byte[] rightPdfBytes,
        string leftFileName = "Document A", string rightFileName = "Document B",
        VisualDiffOptions? options = null,
        IProgress<(int current, int total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new VisualDiffOptions();
        Log.Info("Starting visual diff: {Left} vs {Right} at {Dpi} DPI",
            leftFileName, rightFileName, options.Dpi);

        var result = new VisualDiffResult
        {
            LeftFileName = leftFileName,
            RightFileName = rightFileName
        };

        // Optionally include text comparison
        if (options.IncludeTextComparison)
        {
            result.TextComparison = _comparisonService.Compare(
                leftPdfBytes, rightPdfBytes, leftFileName, rightFileName);
        }

        // Get page counts
        var pdfOps = new PdfOperations();
        int leftPages = pdfOps.GetPageCount(leftPdfBytes);
        int rightPages = pdfOps.GetPageCount(rightPdfBytes);
        int maxPages = Math.Max(leftPages, rightPages);

        for (int i = 0; i < maxPages; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((i + 1, maxPages));

            var pageResult = new VisualDiffPageResult { PageNumber = i + 1 };

            if (i >= leftPages)
            {
                // Page only in right document
                pageResult.RightImage = await RenderPageAsync(rightPdfBytes, i, options.Dpi);
                pageResult.DifferencePercent = 100.0;
                pageResult.Status = "Added in right document";
            }
            else if (i >= rightPages)
            {
                // Page only in left document
                pageResult.LeftImage = await RenderPageAsync(leftPdfBytes, i, options.Dpi);
                pageResult.DifferencePercent = 100.0;
                pageResult.Status = "Removed from right document";
            }
            else
            {
                // Both pages exist — render and compare
                pageResult.LeftImage = await RenderPageAsync(leftPdfBytes, i, options.Dpi);
                pageResult.RightImage = await RenderPageAsync(rightPdfBytes, i, options.Dpi);

                var (diffImage, diffPercent) = await ComputePixelDiffAsync(
                    pageResult.LeftImage, pageResult.RightImage, options);
                pageResult.DiffImage = diffImage;
                pageResult.DifferencePercent = diffPercent;
                pageResult.Status = diffPercent < 0.01 ? "Identical" : $"{diffPercent:F2}% different";
            }

            result.Pages.Add(pageResult);
        }

        Log.Info("Visual diff complete: {TotalPages} pages, {DiffPages} with differences ({OverallDiff:F2}%)",
            result.TotalPages, result.PagesWithDifferences, result.OverallDifferencePercent);

        return result;
    }

    /// <summary>
    /// Generates a side-by-side comparison image for a specific page.
    /// Left image, diff overlay, right image combined into one.
    /// </summary>
    public byte[] GenerateSideBySideImage(byte[] leftImage, byte[] rightImage, byte[]? diffImage)
    {
        using var left = new MagickImage(leftImage);
        using var right = new MagickImage(rightImage);

        // Normalize sizes
        uint targetHeight = Math.Max(left.Height, right.Height);
        uint targetWidth = Math.Max(left.Width, right.Width);

        if (left.Height != targetHeight || left.Width != targetWidth)
            left.Resize(new MagickGeometry(targetWidth, targetHeight) { IgnoreAspectRatio = false });
        if (right.Height != targetHeight || right.Width != targetWidth)
            right.Resize(new MagickGeometry(targetWidth, targetHeight) { IgnoreAspectRatio = false });

        MagickImage? diff = null;
        if (diffImage != null)
        {
            diff = new MagickImage(diffImage);
            if (diff.Height != targetHeight || diff.Width != targetWidth)
                diff.Resize(new MagickGeometry(targetWidth, targetHeight) { IgnoreAspectRatio = false });
        }

        uint totalWidth = diff != null ? targetWidth * 3 + 20 : targetWidth * 2 + 10;
        using var canvas = new MagickImage(MagickColors.White, totalWidth, targetHeight + 40);

        // Add labels
        var labelSettings = new MagickReadSettings
        {
            BackgroundColor = MagickColors.Transparent,
            FillColor = MagickColors.Black,
            FontPointsize = 14,
            Width = targetWidth,
            Height = 30
        };

        // Composite left
        canvas.Composite(left, 0, 35, CompositeOperator.Over);

        // Composite diff (if present)
        if (diff != null)
        {
            canvas.Composite(diff, (int)(targetWidth + 10), 35, CompositeOperator.Over);
            canvas.Composite(right, (int)(targetWidth * 2 + 20), 35, CompositeOperator.Over);
        }
        else
        {
            canvas.Composite(right, (int)(targetWidth + 10), 35, CompositeOperator.Over);
        }

        diff?.Dispose();

        using var ms = new MemoryStream();
        canvas.Format = MagickFormat.Png;
        canvas.Write(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Generates a detailed HTML report with visual diffs embedded as base64 images.
    /// </summary>
    public string GenerateVisualDiffHtmlReport(VisualDiffResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
        sb.AppendLine("<title>Visual Comparison Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("  body { font-family: 'Segoe UI', sans-serif; margin: 20px; background: #f5f5f5; }");
        sb.AppendLine("  .header { background: #2c3e50; color: white; padding: 20px; border-radius: 8px; }");
        sb.AppendLine("  .page { background: white; margin: 15px 0; padding: 15px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
        sb.AppendLine("  .page.identical { border-left: 4px solid #27ae60; }");
        sb.AppendLine("  .page.different { border-left: 4px solid #e74c3c; }");
        sb.AppendLine("  .images { display: flex; gap: 10px; justify-content: center; flex-wrap: wrap; }");
        sb.AppendLine("  .images img { max-width: 30%; border: 1px solid #ddd; }");
        sb.AppendLine("  .label { text-align: center; font-weight: bold; margin-bottom: 5px; }");
        sb.AppendLine("  .stats { display: flex; gap: 12px; margin: 15px 0; }");
        sb.AppendLine("  .stat { background: white; padding: 10px 18px; border-radius: 6px; }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<div class='header'>");
        sb.AppendLine($"  <h1>Visual Comparison Report</h1>");
        sb.AppendLine($"  <p>{HtmlEncode(result.LeftFileName)} vs {HtmlEncode(result.RightFileName)}</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='stats'>");
        sb.AppendLine($"  <div class='stat'><strong>{result.TotalPages}</strong> Pages</div>");
        sb.AppendLine($"  <div class='stat'><strong>{result.PagesWithDifferences}</strong> Different</div>");
        sb.AppendLine($"  <div class='stat'><strong>{result.OverallDifferencePercent:F2}%</strong> Overall Diff</div>");
        sb.AppendLine("</div>");

        foreach (var page in result.Pages)
        {
            string cssClass = page.HasDifferences ? "different" : "identical";
            sb.AppendLine($"<div class='page {cssClass}'>");
            sb.AppendLine($"  <h3>Page {page.PageNumber} — {HtmlEncode(page.Status)}</h3>");

            sb.AppendLine("  <div class='images'>");
            if (page.LeftImage != null)
            {
                sb.AppendLine("    <div><div class='label'>Left</div>");
                sb.AppendLine($"    <img src='data:image/png;base64,{Convert.ToBase64String(page.LeftImage)}'/></div>");
            }
            if (page.DiffImage != null)
            {
                sb.AppendLine("    <div><div class='label'>Differences</div>");
                sb.AppendLine($"    <img src='data:image/png;base64,{Convert.ToBase64String(page.DiffImage)}'/></div>");
            }
            if (page.RightImage != null)
            {
                sb.AppendLine("    <div><div class='label'>Right</div>");
                sb.AppendLine($"    <img src='data:image/png;base64,{Convert.ToBase64String(page.RightImage)}'/></div>");
            }
            sb.AppendLine("  </div>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Merges differences from the right document into the left document.
    /// Takes specific pages from the right document and replaces them in the left.
    /// </summary>
    /// <param name="leftPdfBytes">Base (left) PDF document</param>
    /// <param name="rightPdfBytes">Source (right) PDF document</param>
    /// <param name="pagesToMerge">0-based page indices from right document to merge into left</param>
    /// <returns>Merged PDF bytes</returns>
    public byte[] MergeChanges(byte[] leftPdfBytes, byte[] rightPdfBytes, int[] pagesToMerge)
    {
        Log.Info("Merging {Count} pages from right document into left", pagesToMerge.Length);

        var outputMs = new MemoryStream();
        using (var leftReader = new iText.Kernel.Pdf.PdfReader(new MemoryStream(leftPdfBytes)))
        using (var rightReader = new iText.Kernel.Pdf.PdfReader(new MemoryStream(rightPdfBytes)))
        using (var writer = new iText.Kernel.Pdf.PdfWriter(outputMs))
        {
            var leftDoc = new iText.Kernel.Pdf.PdfDocument(leftReader, writer);
            var rightDoc = new iText.Kernel.Pdf.PdfDocument(rightReader);

            int leftPages = leftDoc.GetNumberOfPages();
            int rightPages = rightDoc.GetNumberOfPages();

            foreach (var pageIdx in pagesToMerge.OrderBy(p => p))
            {
                int pageNum = pageIdx + 1;
                if (pageNum < 1 || pageNum > rightPages) continue;

                if (pageNum <= leftPages)
                {
                    // Replace existing page: remove old, copy new
                    var rightPage = rightDoc.GetPage(pageNum);
                    var copiedPages = rightPage.CopyTo(leftDoc);
                    leftDoc.RemovePage(pageNum);

                    // Insert at the same position
                    if (pageNum <= leftDoc.GetNumberOfPages())
                        leftDoc.AddPage(pageNum, copiedPages);
                    else
                        leftDoc.AddPage(copiedPages);
                }
                else
                {
                    // Add new page at end
                    var rightPage = rightDoc.GetPage(pageNum);
                    rightPage.CopyTo(leftDoc);
                }
            }

            leftDoc.Close();
            rightDoc.Close();
        }

        return outputMs.ToArray();
    }

    #region Private Helpers

    private async Task<byte[]> RenderPageAsync(byte[] pdfBytes, int pageIndex, int dpi)
    {
        return await Task.Run(() =>
        {
            int scaledWidth = (int)(8.5 * dpi);
            int scaledHeight = (int)(11.0 * dpi);
            var (pixels, width, height) = _renderService.RenderPage(
                pdfBytes, pageIndex, scaledWidth, scaledHeight);

            using var image = new MagickImage();
            var settings = new PixelReadSettings(
                (uint)width, (uint)height, StorageType.Char, PixelMapping.BGRA);
            image.ReadPixels(pixels, settings);
            image.Format = MagickFormat.Png;

            using var ms = new MemoryStream();
            image.Write(ms);
            return ms.ToArray();
        });
    }

    private async Task<(byte[] diffImage, double diffPercent)> ComputePixelDiffAsync(
        byte[] leftPng, byte[] rightPng, VisualDiffOptions options)
    {
        return await Task.Run(() =>
        {
            using var left = new MagickImage(leftPng);
            using var right = new MagickImage(rightPng);

            // Normalize sizes to the max of both
            uint maxW = Math.Max(left.Width, right.Width);
            uint maxH = Math.Max(left.Height, right.Height);

            if (left.Width != maxW || left.Height != maxH)
            {
                var bg = new MagickImage(MagickColors.White, maxW, maxH);
                bg.Composite(left, 0, 0, CompositeOperator.Over);
                left.Read(bg.ToByteArray(MagickFormat.Png));
            }
            if (right.Width != maxW || right.Height != maxH)
            {
                var bg = new MagickImage(MagickColors.White, maxW, maxH);
                bg.Composite(right, 0, 0, CompositeOperator.Over);
                right.Read(bg.ToByteArray(MagickFormat.Png));
            }

            // Compute difference
            using var diff = new MagickImage(left);
            diff.Composite(right, CompositeOperator.Difference);

            // Threshold to highlight significant differences
            diff.Threshold(new Percentage(options.Threshold * 100));

            // Colorize differences
            diff.Opaque(MagickColors.White, MagickColors.Transparent);

            // Recolor black (different pixels) to red highlight
            var highlightColor = new MagickColor(options.HighlightColor);
            diff.Opaque(MagickColors.Black, highlightColor);

            // Compute difference percentage
            var stats = diff.Statistics();
            double diffPercent = 0;
            var redChannel = stats.GetChannel(PixelChannel.Red);
            if (redChannel != null)
            {
                diffPercent = (redChannel.Mean / (double)Quantum.Max) * 100.0;
            }

            // Create overlay: left image with red highlights
            using var overlay = new MagickImage(left);
            overlay.Composite(diff, CompositeOperator.Over);

            using var ms = new MemoryStream();
            overlay.Format = MagickFormat.Png;
            overlay.Write(ms);

            return (ms.ToArray(), diffPercent);
        });
    }

    private static string HtmlEncode(string text)
    {
        return text.Replace("&", "&amp;")
                   .Replace("<", "&lt;")
                   .Replace(">", "&gt;")
                   .Replace("\"", "&quot;");
    }

    #endregion
}
