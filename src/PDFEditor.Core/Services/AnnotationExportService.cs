using System.Text;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Service for exporting annotations to summary reports (text and HTML formats).
/// </summary>
public class AnnotationExportService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Generates a plain text summary report of all annotations.
    /// </summary>
    public string GenerateTextReport(IEnumerable<PdfAnnotation> annotations, string? documentName = null)
    {
        var annList = annotations.ToList();
        Log.Info("Generating text annotation report for {Count} annotation(s)", annList.Count);

        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════════════");
        sb.AppendLine("              ANNOTATION SUMMARY REPORT                   ");
        sb.AppendLine("═══════════════════════════════════════════════════════════");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(documentName))
            sb.AppendLine($"  Document: {documentName}");

        sb.AppendLine($"  Total annotations: {annList.Count}");
        sb.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        if (annList.Count == 0)
        {
            sb.AppendLine("  No annotations found.");
            return sb.ToString();
        }

        // Group by type
        var byType = annList.GroupBy(a => a.Type).OrderBy(g => g.Key.ToString());
        sb.AppendLine("  Summary by Type:");
        foreach (var group in byType)
        {
            sb.AppendLine($"    {group.Key,-20} {group.Count(),4} annotation(s)");
        }
        sb.AppendLine();

        // Group by page
        var byPage = annList.GroupBy(a => a.PageIndex).OrderBy(g => g.Key);
        foreach (var pageGroup in byPage)
        {
            sb.AppendLine("───────────────────────────────────────────────────────────");
            sb.AppendLine($"  Page {pageGroup.Key + 1} ({pageGroup.Count()} annotation(s))");
            sb.AppendLine("───────────────────────────────────────────────────────────");

            int num = 1;
            foreach (var ann in pageGroup.OrderBy(a => a.Y).ThenBy(a => a.X))
            {
                sb.AppendLine($"  [{num}] {ann.Type}");
                sb.AppendLine($"      Position: ({ann.X:F2}, {ann.Y:F2})  Size: ({ann.Width:F2} x {ann.Height:F2})");

                if (!string.IsNullOrEmpty(ann.Text))
                    sb.AppendLine($"      Text: \"{ann.Text}\"");

                if (ann.Type == AnnotationType.StickyNote && !string.IsNullOrEmpty(ann.NoteContent))
                    sb.AppendLine($"      Note: \"{ann.NoteContent}\"");

                if (ann.Type == AnnotationType.Stamp)
                    sb.AppendLine($"      Stamp: {ann.StampPreset}" +
                        (!string.IsNullOrEmpty(ann.StampText) ? $" (\"{ann.StampText}\")" : ""));

                if (ann.Type == AnnotationType.Highlight || ann.Type == AnnotationType.Underline ||
                    ann.Type == AnnotationType.Strikethrough)
                    sb.AppendLine($"      Color: {ann.FillColor}, Opacity: {ann.FillOpacity:P0}");

                if (ann.Type == AnnotationType.Rectangle || ann.Type == AnnotationType.Ellipse)
                    sb.AppendLine($"      Fill: {ann.FillColor} ({ann.FillOpacity:P0}), Stroke: {ann.StrokeColor} ({ann.StrokeWidth}pt)");

                if (ann.Type == AnnotationType.FreehandDraw)
                    sb.AppendLine($"      Points: {ann.Points.Count}, Color: {ann.Color}, Width: {ann.StrokeWidth}pt");

                if (ann.Type == AnnotationType.Arrow)
                    sb.AppendLine($"      From: ({ann.X:F2}, {ann.Y:F2}) To: ({ann.EndX:F2}, {ann.EndY:F2})");

                if (ann.Rotation != 0)
                    sb.AppendLine($"      Rotation: {ann.Rotation}°");

                sb.AppendLine();
                num++;
            }
        }

        sb.AppendLine("═══════════════════════════════════════════════════════════");
        return sb.ToString();
    }

    /// <summary>
    /// Generates an HTML summary report of all annotations with styling.
    /// </summary>
    public string GenerateHtmlReport(IEnumerable<PdfAnnotation> annotations, string? documentName = null)
    {
        var annList = annotations.ToList();
        Log.Info("Generating HTML annotation report for {Count} annotation(s)", annList.Count);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset='utf-8'/>");
        sb.AppendLine("<title>Annotation Summary Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("  body { font-family: 'Segoe UI', Arial, sans-serif; margin: 20px; background: #f9f9f9; color: #333; }");
        sb.AppendLine("  .header { background: #34495e; color: white; padding: 20px; border-radius: 8px; margin-bottom: 20px; }");
        sb.AppendLine("  .header h1 { margin: 0 0 8px 0; }");
        sb.AppendLine("  .summary { display: flex; flex-wrap: wrap; gap: 10px; margin-bottom: 20px; }");
        sb.AppendLine("  .badge { background: white; padding: 8px 16px; border-radius: 20px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); font-size: 13px; }");
        sb.AppendLine("  .page-section { background: white; margin: 12px 0; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); overflow: hidden; }");
        sb.AppendLine("  .page-header { background: #ecf0f1; padding: 10px 16px; font-weight: 600; font-size: 14px; }");
        sb.AppendLine("  .ann-item { padding: 10px 16px; border-bottom: 1px solid #f0f0f0; }");
        sb.AppendLine("  .ann-item:last-child { border-bottom: none; }");
        sb.AppendLine("  .ann-type { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 11px; font-weight: 600; color: white; margin-right: 8px; }");
        sb.AppendLine("  .type-text { background: #3498db; } .type-highlight { background: #f1c40f; color: #333; }");
        sb.AppendLine("  .type-rectangle { background: #2ecc71; } .type-ellipse { background: #1abc9c; }");
        sb.AppendLine("  .type-arrow { background: #e67e22; } .type-stamp { background: #e74c3c; }");
        sb.AppendLine("  .type-stickynote { background: #9b59b6; } .type-freehanddraw { background: #34495e; }");
        sb.AppendLine("  .type-image { background: #16a085; } .type-redact { background: #c0392b; }");
        sb.AppendLine("  .type-blur { background: #7f8c8d; } .type-underline { background: #2980b9; }");
        sb.AppendLine("  .type-strikethrough { background: #8e44ad; }");
        sb.AppendLine("  .ann-detail { font-size: 12px; color: #666; margin-top: 4px; }");
        sb.AppendLine("  .ann-text { font-style: italic; color: #555; }");
        sb.AppendLine("  table { border-collapse: collapse; width: 100%; margin-top: 10px; }");
        sb.AppendLine("  th, td { text-align: left; padding: 6px 12px; border-bottom: 1px solid #eee; font-size: 13px; }");
        sb.AppendLine("  th { background: #f5f5f5; font-weight: 600; }");
        sb.AppendLine("</style></head><body>");

        // Header
        sb.AppendLine("<div class='header'>");
        sb.AppendLine("  <h1>Annotation Summary Report</h1>");
        if (!string.IsNullOrEmpty(documentName))
            sb.AppendLine($"  <p>Document: {Escape(documentName)}</p>");
        sb.AppendLine($"  <p>Total annotations: {annList.Count} &nbsp;|&nbsp; Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
        sb.AppendLine("</div>");

        if (annList.Count == 0)
        {
            sb.AppendLine("<p>No annotations found.</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        // Type summary badges
        sb.AppendLine("<div class='summary'>");
        foreach (var group in annList.GroupBy(a => a.Type).OrderBy(g => g.Key.ToString()))
        {
            sb.AppendLine($"  <div class='badge'><span class='ann-type type-{group.Key.ToString().ToLower()}'>{group.Key}</span> {group.Count()}</div>");
        }
        sb.AppendLine("</div>");

        // Per-page sections
        var byPage = annList.GroupBy(a => a.PageIndex).OrderBy(g => g.Key);
        foreach (var pageGroup in byPage)
        {
            sb.AppendLine("<div class='page-section'>");
            sb.AppendLine($"  <div class='page-header'>Page {pageGroup.Key + 1} ({pageGroup.Count()} annotations)</div>");

            foreach (var ann in pageGroup.OrderBy(a => a.Y).ThenBy(a => a.X))
            {
                var typeClass = $"type-{ann.Type.ToString().ToLower()}";
                sb.AppendLine("  <div class='ann-item'>");
                sb.AppendLine($"    <span class='ann-type {typeClass}'>{ann.Type}</span>");

                if (!string.IsNullOrEmpty(ann.Text))
                    sb.AppendLine($"    <span class='ann-text'>\"{Escape(ann.Text)}\"</span>");
                else if (ann.Type == AnnotationType.StickyNote && !string.IsNullOrEmpty(ann.NoteContent))
                    sb.AppendLine($"    <span class='ann-text'>\"{Escape(ann.NoteContent)}\"</span>");
                else if (ann.Type == AnnotationType.Stamp)
                    sb.AppendLine($"    <span>{ann.StampPreset}{(!string.IsNullOrEmpty(ann.StampText) ? $" - \"{Escape(ann.StampText)}\"" : "")}</span>");

                sb.AppendLine($"    <div class='ann-detail'>Position: ({ann.X:F2}, {ann.Y:F2}) &nbsp; Size: {ann.Width:F2} × {ann.Height:F2}</div>");
                sb.AppendLine("  </div>");
            }

            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Generates a CSV export of all annotations.
    /// </summary>
    public string GenerateCsvReport(IEnumerable<PdfAnnotation> annotations)
    {
        var annList = annotations.ToList();
        Log.Info("Generating CSV annotation report for {Count} annotation(s)", annList.Count);

        var sb = new StringBuilder();
        sb.AppendLine("Page,Type,X,Y,Width,Height,Text,Color,StampPreset,NoteContent");

        foreach (var ann in annList.OrderBy(a => a.PageIndex).ThenBy(a => a.Y).ThenBy(a => a.X))
        {
            sb.AppendLine(string.Join(",",
                ann.PageIndex + 1,
                ann.Type,
                ann.X.ToString("F4"),
                ann.Y.ToString("F4"),
                ann.Width.ToString("F4"),
                ann.Height.ToString("F4"),
                CsvEscape(ann.Text ?? ann.NoteContent ?? ""),
                ann.Color,
                ann.Type == AnnotationType.Stamp ? ann.StampPreset.ToString() : "",
                CsvEscape(ann.NoteContent ?? "")
            ));
        }

        return sb.ToString();
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string CsvEscape(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Contains(',') || text.Contains('"') || text.Contains('\n'))
            return $"\"{text.Replace("\"", "\"\"")}\"";
        return text;
    }
}
