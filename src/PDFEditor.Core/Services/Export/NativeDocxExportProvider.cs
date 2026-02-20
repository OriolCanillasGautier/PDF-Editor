using ImageMagick;
using NLog;
using PDFEditor.Core.Abstractions;
using PDFEditor.Core.Models.Layout;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Pure C# DOCX export provider that uses the layout reconstruction engine
/// (LayoutExtractor → LayoutAnalyzer → TableDetectionEngine) to produce
/// high-fidelity Word documents without any Python dependency.
///
/// This replaces the need for the Python pdf2docx sidecar by implementing
/// the same algorithmic approach natively:
///   1. Character-level glyph extraction via iText7 event listeners
///   2. Spatial clustering (chars → lines → paragraphs) via proximity heuristics
///   3. Table detection via horizontal/vertical line intersection analysis
///   4. OpenXML generation with precise indentation and spacing from PDF coordinates
///
/// The DOCX is built as hand-crafted XML inside a ZipArchive (same approach as
/// DocxExportProvider) to avoid repair dialogs in Word.
/// </summary>
public class NativeDocxExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly LayoutExtractor _extractor = new();
    private readonly LayoutAnalyzer _analyzer = new();

    // ──────────────────────────────────────────────────────────────────────────
    // OOXML namespace URIs (same as DocxExportProvider)
    // ──────────────────────────────────────────────────────────────────────────
    private const string NsOpcCt  = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string NsOpcRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string NsOffRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsW      = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string NsWp     = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string NsA      = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string NsPic    = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    private const string RelTypeDocument    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string RelTypeStyles      = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    private const string RelTypeSettings    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";
    private const string RelTypeFontTable   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable";
    private const string RelTypeWebSettings = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/webSettings";
    private const string RelTypeImage       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

    private const int ImageRIdBase = 5;

    // ──────────────────────────────────────────────────────────────────────────
    // Unit conversions
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Convert PDF points to Word twips: twips = points × 20.</summary>
    private static int PointsToTwips(float points) => (int)(points * 20f);

    /// <summary>Convert PDF points to OpenXML half-points: half-points = points × 2.</summary>
    private static int PointsToHalfPoints(float points) => Math.Max(12, (int)(points * 2f));

    /// <summary>Convert PDF points to EMUs (English Metric Units): EMUs = points × 12700.</summary>
    private static long PointsToEmu(float points) => (long)(points * 12700);

    // ──────────────────────────────────────────────────────────────────────────
    // IExportProvider
    // ──────────────────────────────────────────────────────────────────────────

    public string FormatName => "Microsoft Word Native (DOCX)";
    public string[] SupportedExtensions => new[] { ".docx" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    public async Task<ExportResult> ExportAsync(
        byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var docxBytes = await Task.Run(
                () => GenerateDocx(pdfBytes, options, cancellationToken),
                cancellationToken);

            return ExportResult.Ok(
                docxBytes,
                $"{options.BaseFileName}.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Native DOCX export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(
        byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("DOCX export produces a single document. Use ExportAsync instead.");

    // ──────────────────────────────────────────────────────────────────────────
    // Internal image record
    // ──────────────────────────────────────────────────────────────────────────

    private sealed record ImagePart(string RId, string ZipRelPath, byte[] Data, string ContentType);

    // ──────────────────────────────────────────────────────────────────────────
    // Top-level DOCX builder
    // ──────────────────────────────────────────────────────────────────────────

    private byte[] GenerateDocx(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        Log.Info("Starting native DOCX export (layout reconstruction engine)");

        // Phase 1: Extract raw layout data from PDF
        var pageDataList = _extractor.ExtractPages(pdfBytes, options.PageIndices);

        // Phase 2: Analyze each page → ordered content elements
        var analyses = new List<LayoutAnalyzer.PageAnalysis>();
        foreach (var pageData in pageDataList)
        {
            ct.ThrowIfCancellationRequested();
            analyses.Add(_analyzer.Analyze(pageData));
        }

        // Phase 3: Build DOCX body XML from analyzed pages
        var (bodyXml, imageParts) = BuildBodyXml(analyses, options, ct);

        // Phase 4: Assemble the DOCX ZIP
        return AssembleDocxZip(bodyXml, imageParts);
    }

    private (StringBuilder bodyXml, List<ImagePart> imageParts) BuildBodyXml(
        List<LayoutAnalyzer.PageAnalysis> pages, ExportOptions options, CancellationToken ct)
    {
        var body = new StringBuilder(64 * 1024);
        var imageParts = new List<ImagePart>();
        int imgSeq = 0;

        // Optional title
        AppendTitleParagraph(body, options.BaseFileName);

        float? prevBottomY = null; // Track previous element bottom for vertical spacing

        for (int pi = 0; pi < pages.Count; pi++)
        {
            ct.ThrowIfCancellationRequested();
            var page = pages[pi];
            prevBottomY = null; // Reset for each page

            if (page.Elements.Count == 0 && page.Images.Count == 0)
            {
                AppendPlaceholder(body, $"[No extractable content on page {page.PageNumber}]");
            }
            else
            {
                // Write content elements in reading order
                foreach (var element in page.Elements)
                {
                    switch (element.Type)
                    {
                        case LayoutAnalyzer.PageElementType.Heading:
                            AppendHeadingElement(body, element, prevBottomY);
                            break;
                        case LayoutAnalyzer.PageElementType.TextBlock:
                            AppendTextBlockElement(body, element, prevBottomY);
                            break;
                        case LayoutAnalyzer.PageElementType.Table:
                            AppendTableElement(body, element);
                            break;
                    }

                    // Update the previous bottom Y for spacing calculations
                    prevBottomY = element.Block?.BBox.Y ?? element.Table?.BBox.Y;
                }

                // Write images
                foreach (var img in page.Images)
                {
                    try
                    {
                        var (normalized, isJpeg) = NormalizeImage(img.Data);
                        if (normalized.Length == 0) continue;

                        string ext = isJpeg ? "jpg" : "png";
                        string ct2 = isJpeg ? "image/jpeg" : "image/png";
                        string path = $"media/image{imgSeq + 1}.{ext}";
                        string rId = $"rId{ImageRIdBase + imgSeq}";

                        imageParts.Add(new ImagePart(rId, path, normalized, ct2));

                        long wEmu = PointsToEmu(Math.Abs(img.Width));
                        long hEmu = PointsToEmu(Math.Abs(img.Height));
                        ClampImageEmu(ref wEmu, ref hEmu);

                        AppendImageXml(body, rId, wEmu, hEmu, (uint)(imgSeq + 1));
                        imgSeq++;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Failed to embed image in DOCX");
                    }
                }
            }

            // Page break between pages (not after the last one)
            if (pi < pages.Count - 1)
                body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
        }

        // Mandatory: SectionProperties as last element in <w:body>
        body.Append(
            "<w:sectPr>" +
            "<w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
            "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" " +
                     "w:left=\"1440\" w:header=\"720\" w:footer=\"720\"/>" +
            "</w:sectPr>");

        return (body, imageParts);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Content element → XML
    // ──────────────────────────────────────────────────────────────────────────

    private void AppendTextBlockElement(StringBuilder sb, LayoutAnalyzer.PageElement element, float? prevBottomY)
    {
        if (element.Block == null) return;
        var block = element.Block;

        string text = Xe(SanitizeXml(string.Join(" ", block.Lines.Select(l => l.Text))));
        if (string.IsNullOrWhiteSpace(text)) return;

        // Convert PDF X coordinate to left indentation (twips)
        // Subtract page margin (1 inch = 72 points) so content at x=72 has zero indent
        int leftIndentTwips = Math.Max(0, PointsToTwips(block.BBox.X - 72f));

        // Calculate vertical spacing from previous element
        int spaceBeforeTwips = CalculateSpaceBefore(element.TopY, prevBottomY);

        int fontSizeHalf = PointsToHalfPoints(element.FontSize);

        sb.Append("<w:p>");
        sb.Append("<w:pPr>");
        sb.Append($"<w:spacing w:before=\"{spaceBeforeTwips}\" w:after=\"80\" w:line=\"276\" w:lineRule=\"auto\"/>");
        if (leftIndentTwips > 0)
            sb.Append($"<w:ind w:left=\"{leftIndentTwips}\"/>");
        sb.Append("</w:pPr>");

        // Emit runs grouped by font properties for mixed formatting
        EmitFormattedRuns(sb, block, fontSizeHalf);

        sb.Append("</w:p>");
    }

    private void AppendHeadingElement(StringBuilder sb, LayoutAnalyzer.PageElement element, float? prevBottomY)
    {
        if (element.Block == null) return;

        string styleId = element.HeadingLevel switch
        {
            1 => "PDFHeading1",
            2 => "PDFHeading2",
            3 => "PDFHeading3",
            _ => "PDFHeading4"
        };

        string text = Xe(SanitizeXml(string.Join(" ", element.Block.Lines.Select(l => l.Text))));
        if (string.IsNullOrWhiteSpace(text)) return;

        int spaceBeforeTwips = CalculateSpaceBefore(element.TopY, prevBottomY);

        sb.Append("<w:p>");
        sb.Append($"<w:pPr><w:pStyle w:val=\"{styleId}\"/>");
        sb.Append($"<w:spacing w:before=\"{Math.Max(spaceBeforeTwips, 240)}\"/>");
        sb.Append("</w:pPr>");
        sb.Append($"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>");
        sb.Append("</w:p>");
    }

    private void AppendTableElement(StringBuilder sb, LayoutAnalyzer.PageElement element)
    {
        if (element.Table == null) return;
        var table = element.Table;

        if (table.Cells.Count == 0 || table.ColumnCount == 0) return;

        sb.Append("<w:tbl>");

        // Table properties: full width with borders
        sb.Append("<w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/>");
        sb.Append("<w:tblBorders>");
        foreach (var side in new[] { "top", "bottom", "left", "right", "insideH", "insideV" })
        {
            string color = side.StartsWith("inside", StringComparison.Ordinal) ? "CCCCCC" : "999999";
            sb.Append($"<w:{side} w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"{color}\"/>");
        }
        sb.Append("</w:tblBorders></w:tblPr>");

        // Calculate column widths in twips
        var colWidths = new int[table.ColumnCount];
        int totalTableWidth = 9360; // Default: 6.5 inches in twips (page width minus margins)
        for (int col = 0; col < table.ColumnCount; col++)
        {
            var cell = table.Cells.FirstOrDefault(c => c.Column == col);
            if (cell != null)
                colWidths[col] = PointsToTwips(cell.BBox.Width);
            else
                colWidths[col] = totalTableWidth / table.ColumnCount;
        }

        // Grid definition
        sb.Append("<w:tblGrid>");
        foreach (var w in colWidths)
            sb.Append($"<w:gridCol w:w=\"{w}\"/>");
        sb.Append("</w:tblGrid>");

        // Track row spans
        var rowSpans = new int[table.ColumnCount];

        // Rows
        for (int row = 0; row < table.RowCount; row++)
        {
            sb.Append("<w:tr>");

            for (int col = 0; col < table.ColumnCount; col++)
            {
                var cell = table.Cells.FirstOrDefault(c => c.Row == row && c.Column == col);
                
                if (cell == null && rowSpans[col] > 0)
                {
                    // This cell is part of a vertical merge (continuation)
                    sb.Append("<w:tc>");
                    sb.Append("<w:tcPr>");
                    sb.Append($"<w:tcW w:w=\"{colWidths[col]}\" w:type=\"dxa\"/>");
                    sb.Append("<w:vMerge/>");
                    sb.Append("</w:tcPr>");
                    sb.Append("<w:p/>");
                    sb.Append("</w:tc>");
                    rowSpans[col]--;
                    continue;
                }
                
                if (cell == null)
                {
                    // Empty cell
                    sb.Append("<w:tc>");
                    sb.Append("<w:tcPr>");
                    sb.Append($"<w:tcW w:w=\"{colWidths[col]}\" w:type=\"dxa\"/>");
                    sb.Append("</w:tcPr>");
                    sb.Append("<w:p/>");
                    sb.Append("</w:tc>");
                    continue;
                }

                sb.Append("<w:tc>");
                sb.Append("<w:tcPr>");
                
                // Calculate width for spanned columns
                int cellWidth = 0;
                for (int i = 0; i < cell.ColSpan && col + i < table.ColumnCount; i++)
                {
                    cellWidth += colWidths[col + i];
                }
                
                sb.Append($"<w:tcW w:w=\"{cellWidth}\" w:type=\"dxa\"/>");
                
                if (cell.ColSpan > 1)
                {
                    sb.Append($"<w:gridSpan w:val=\"{cell.ColSpan}\"/>");
                }
                
                if (cell.RowSpan > 1)
                {
                    sb.Append("<w:vMerge w:val=\"restart\"/>");
                    rowSpans[col] = cell.RowSpan - 1;
                }
                
                if (row == 0)
                    sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"E8EDF2\"/>");
                sb.Append("</w:tcPr>");

                var cellBlock = ReconstructCellBlock(cell);
                if (cellBlock.Lines.Count == 0)
                {
                    sb.Append("<w:p/>");
                }
                else
                {
                    for (int li = 0; li < cellBlock.Lines.Count; li++)
                    {
                        sb.Append("<w:p>");
                        sb.Append("<w:pPr>");
                        sb.Append("<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>");
                        sb.Append("</w:pPr>");
                        
                        // Create a temporary block with just this line to reuse EmitFormattedRuns
                        var tempBlock = new LayoutBlock { Lines = new List<LayoutLine> { cellBlock.Lines[li] } };
                        int defaultFontSizeHalf = cellBlock.Lines[li].Characters.Count > 0 
                            ? PointsToHalfPoints(cellBlock.Lines[li].Characters.Average(c => c.FontSize)) 
                            : 20;
                            
                        EmitFormattedRuns(sb, tempBlock, defaultFontSizeHalf);
                        sb.Append("</w:p>");
                    }
                }

                sb.Append("</w:tc>");
                
                // Skip columns that are part of a horizontal merge
                if (cell.ColSpan > 1)
                {
                    col += cell.ColSpan - 1;
                }
            }

            sb.Append("</w:tr>");
        }

        sb.Append("</w:tbl>");
        sb.Append("<w:p/>"); // Spacing after table
    }

    /// <summary>
    /// Reconstructs a LayoutBlock from a table cell's layout characters,
    /// clustering them into lines and sorting within each line.
    /// </summary>
    private LayoutBlock ReconstructCellBlock(LayoutTableCell cell)
    {
        var block = new LayoutBlock();
        if (cell.Content.Count == 0) return block;

        // Group characters by Y baseline (line clustering within the cell)
        var sorted = cell.Content.OrderByDescending(c => c.BBox.Y).ThenBy(c => c.BBox.X).ToList();
        var currentLineChars = new List<LayoutCharacter> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            float yDiff = Math.Abs(sorted[i].BBox.Y - currentLineChars[0].BBox.Y);
            float avgSize = currentLineChars.Average(c => c.FontSize);

            if (yDiff <= avgSize * 0.5f)
            {
                currentLineChars.Add(sorted[i]);
            }
            else
            {
                block.Lines.Add(BuildCellLine(currentLineChars));
                currentLineChars = new List<LayoutCharacter> { sorted[i] };
            }
        }
        if (currentLineChars.Count > 0)
            block.Lines.Add(BuildCellLine(currentLineChars));

        return block;
    }

    private LayoutLine BuildCellLine(List<LayoutCharacter> chars)
    {
        var ordered = chars.OrderBy(c => c.BBox.X).ToList();
        var line = new LayoutLine();

        for (int i = 0; i < ordered.Count; i++)
        {
            if (i > 0)
            {
                float gap = ordered[i].BBox.X - ordered[i - 1].BBox.Right;
                float avgSize = (ordered[i].FontSize + ordered[i - 1].FontSize) / 2f;
                if (gap > avgSize * 0.3f)
                {
                    // Insert a space character
                    line.Characters.Add(new LayoutCharacter
                    {
                        Char = ' ',
                        FontName = ordered[i].FontName,
                        FontSize = ordered[i].FontSize,
                        Color = ordered[i].Color,
                        IsBold = ordered[i].IsBold,
                        IsItalic = ordered[i].IsItalic,
                        BBox = new PdfRect(ordered[i - 1].BBox.Right, ordered[i].BBox.Y, gap, ordered[i].BBox.Height)
                    });
                }
            }
            line.Characters.Add(ordered[i]);
        }

        return line;
    }

    /// <summary>
    /// Emits OpenXML runs for a block, grouping characters by font properties
    /// so that bold, italic, color, and font-size changes are handled with separate runs.
    /// </summary>
    private void EmitFormattedRuns(StringBuilder sb, LayoutBlock block, int defaultFontSizeHalf)
    {
        for (int li = 0; li < block.Lines.Count; li++)
        {
            var line = block.Lines[li];
            if (line.Characters.Count == 0) continue;

            var currentRunChars = new List<LayoutCharacter>();
            LayoutCharacter? currentProps = null;

            foreach (var ch in line.Characters)
            {
                if (currentProps == null)
                {
                    currentProps = ch;
                    currentRunChars.Add(ch);
                    continue;
                }

                // Check if properties match (ignore spaces for property changes)
                bool propsMatch = ch.Char == ' ' || (
                    ch.FontName == currentProps.FontName &&
                    Math.Abs(ch.FontSize - currentProps.FontSize) < 1.0f &&
                    ch.Color == currentProps.Color &&
                    ch.IsBold == currentProps.IsBold &&
                    ch.IsItalic == currentProps.IsItalic
                );

                if (propsMatch)
                {
                    currentRunChars.Add(ch);
                    if (ch.Char != ' ') currentProps = ch; // Update props to latest non-space
                }
                else
                {
                    // Emit current run
                    EmitSingleRun(sb, currentRunChars, currentProps);
                    
                    // Start new run
                    currentRunChars.Clear();
                    currentRunChars.Add(ch);
                    currentProps = ch;
                }
            }

            // Emit final run
            if (currentRunChars.Count > 0 && currentProps != null)
            {
                EmitSingleRun(sb, currentRunChars, currentProps);
            }

            // Line break between lines of the same paragraph (but not after the last)
            if (li < block.Lines.Count - 1)
                sb.Append("<w:r><w:br/></w:r>");
        }
    }

    private void EmitSingleRun(StringBuilder sb, List<LayoutCharacter> chars, LayoutCharacter props)
    {
        string text = Xe(SanitizeXml(string.Concat(chars.Select(c => c.Char))));
        if (string.IsNullOrEmpty(text)) return;

        int fontSizeHalf = PointsToHalfPoints(props.FontSize);
        string fontName = CleanFontName(props.FontName);
        string colorHex = props.Color.TrimStart('#');

        sb.Append("<w:r><w:rPr>");
        sb.Append($"<w:sz w:val=\"{fontSizeHalf}\"/><w:szCs w:val=\"{fontSizeHalf}\"/>");
        sb.Append($"<w:rFonts w:ascii=\"{Xe(fontName)}\" w:hAnsi=\"{Xe(fontName)}\"/>");
        if (props.IsBold) sb.Append("<w:b/><w:bCs/>");
        if (props.IsItalic) sb.Append("<w:i/><w:iCs/>");
        if (!string.IsNullOrEmpty(colorHex) && colorHex != "000000") sb.Append($"<w:color w:val=\"{colorHex}\"/>");
        sb.Append("</w:rPr>");
        sb.Append($"<w:t xml:space=\"preserve\">{text}</w:t>");
        sb.Append("</w:r>");
    }

    /// <summary>
    /// Strips font subset prefixes (e.g., "ABCDEF+" in front of the font name)
    /// and falls back to Calibri if the name is empty.
    /// </summary>
    private static string CleanFontName(string fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName)) return "Calibri";

        // Remove subset prefix: "ABCDEF+FontName" → "FontName"
        int plusIndex = fontName.IndexOf('+');
        if (plusIndex >= 0 && plusIndex < fontName.Length - 1)
            fontName = fontName.Substring(plusIndex + 1);

        // Remove common style suffixes for the font family name
        foreach (var suffix in new[] { "-Bold", "-Italic", "-BoldItalic", ",Bold", ",Italic" })
        {
            int idx = fontName.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                fontName = fontName.Substring(0, idx);
        }

        return string.IsNullOrWhiteSpace(fontName) ? "Calibri" : fontName.Trim();
    }

    /// <summary>
    /// Calculates the SpaceBefore value in twips from the vertical distance
    /// between the current element and the previous element.
    /// </summary>
    private int CalculateSpaceBefore(float currentTopY, float? prevBottomY)
    {
        if (prevBottomY == null) return 0;

        // In PDF coordinates (Y = 0 at bottom), larger Y = higher on page.
        // Gap = prevBottomY - currentTopY (prev is above current in reading order)
        float gapPoints = prevBottomY.Value - currentTopY;
        if (gapPoints < 0) gapPoints = 0;

        int twips = PointsToTwips(gapPoints);
        return Math.Clamp(twips, 0, 1440); // Cap at 1 inch
    }

    // ──────────────────────────────────────────────────────────────────────────
    // XML helpers (identical to DocxExportProvider)
    // ──────────────────────────────────────────────────────────────────────────

    private static string Xe(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
    }

    private static string SanitizeXml(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c == '\t' || c == '\n' || c == '\r'
                || (c >= '\x20' && c <= '\uD7FF')
                || (c >= '\uE000' && c <= '\uFFFD'))
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static void AddZipEntry(ZipArchive zip, string entryPath, string xml)
    {
        var e = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var s = e.Open();
        var b = Encoding.UTF8.GetBytes(xml);
        s.Write(b, 0, b.Length);
    }

    private static void AddZipEntryBytes(ZipArchive zip, string entryPath, byte[] data)
    {
        var e = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var s = e.Open();
        s.Write(data, 0, data.Length);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DOCX ZIP assembly
    // ──────────────────────────────────────────────────────────────────────────

    private byte[] AssembleDocxZip(StringBuilder bodyXml, List<ImagePart> imageParts)
    {
        string contentTypesXml = BuildContentTypesXml(imageParts);
        string rootRelsXml     = BuildRootRelsXml();
        string docRelsXml      = BuildDocumentRelsXml(imageParts);
        string documentXml     = BuildDocumentXml(bodyXml);
        string stylesXml       = BuildStylesXml();
        string settingsXml     = BuildSettingsXml();
        string fontTableXml    = BuildFontTableXml();
        string webSettingsXml  = BuildWebSettingsXml();

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(zip, "[Content_Types].xml",          contentTypesXml);
            AddZipEntry(zip, "_rels/.rels",                  rootRelsXml);
            AddZipEntry(zip, "word/document.xml",            documentXml);
            AddZipEntry(zip, "word/_rels/document.xml.rels", docRelsXml);
            AddZipEntry(zip, "word/styles.xml",              stylesXml);
            AddZipEntry(zip, "word/settings.xml",            settingsXml);
            AddZipEntry(zip, "word/fontTable.xml",           fontTableXml);
            AddZipEntry(zip, "word/webSettings.xml",         webSettingsXml);

            foreach (var img in imageParts)
                AddZipEntryBytes(zip, "word/" + img.ZipRelPath, img.Data);
        }

        var result = ms.ToArray();
        Log.Info("Native DOCX built: {Bytes} bytes, {Images} image(s)", result.Length, imageParts.Count);
        return result;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Body element helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static void AppendTitleParagraph(StringBuilder sb, string? title)
    {
        sb.Append("<w:p>");
        sb.Append("<w:pPr><w:pStyle w:val=\"PDFTitle\"/><w:jc w:val=\"center\"/>");
        sb.Append("<w:spacing w:after=\"200\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr>");
        sb.Append("<w:r><w:rPr>");
        sb.Append("<w:b/><w:sz w:val=\"56\"/><w:szCs w:val=\"56\"/>");
        sb.Append("<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/>");
        sb.Append("</w:rPr>");
        sb.Append($"<w:t xml:space=\"preserve\">{Xe(SanitizeXml(title))}</w:t>");
        sb.Append("</w:r></w:p>");
    }

    private static void AppendPlaceholder(StringBuilder sb, string message)
    {
        sb.Append("<w:p><w:r><w:rPr><w:i/><w:color w:val=\"999999\"/></w:rPr>");
        sb.Append($"<w:t>{Xe(SanitizeXml(message))}</w:t></w:r></w:p>");
    }

    private static void AppendImageXml(StringBuilder sb, string rId, long wEmu, long hEmu, uint docId)
    {
        sb.Append("<w:p>");
        sb.Append("<w:pPr><w:jc w:val=\"center\"/><w:spacing w:before=\"120\" w:after=\"120\"/></w:pPr>");
        sb.Append("<w:r><w:drawing>");
        sb.Append($"<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">");
        sb.Append($"<wp:extent cx=\"{wEmu}\" cy=\"{hEmu}\"/>");
        sb.Append("<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>");
        sb.Append($"<wp:docPr id=\"{docId}\" name=\"PDFImage{docId}\"/>");
        sb.Append("<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>");
        sb.Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">");
        sb.Append("<pic:pic><pic:nvPicPr>");
        sb.Append($"<pic:cNvPr id=\"{docId + 10000u}\" name=\"Image{docId}\"/>");
        sb.Append("<pic:cNvPicPr/></pic:nvPicPr>");
        sb.Append("<pic:blipFill>");
        sb.Append($"<a:blip r:embed=\"{Xe(rId)}\" cstate=\"print\"/>");
        sb.Append("<a:stretch><a:fillRect/></a:stretch>");
        sb.Append("</pic:blipFill>");
        sb.Append("<pic:spPr>");
        sb.Append($"<a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{wEmu}\" cy=\"{hEmu}\"/></a:xfrm>");
        sb.Append("<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>");
        sb.Append("</pic:spPr></pic:pic>");
        sb.Append("</a:graphicData></a:graphic>");
        sb.Append("</wp:inline>");
        sb.Append("</w:drawing></w:r></w:p>");
    }

    private static void ClampImageEmu(ref long wEmu, ref long hEmu)
    {
        const long maxW = 5943600L;
        const long maxH = 8229600L;
        const long minDim = 457200L;

        if (wEmu < minDim) wEmu = maxW / 2;
        if (hEmu < minDim) hEmu = maxH / 2;

        if (wEmu > maxW) { hEmu = (long)(hEmu * ((double)maxW / wEmu)); wEmu = maxW; }
        if (hEmu > maxH) { wEmu = (long)(wEmu * ((double)maxH / hEmu)); hEmu = maxH; }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Image normalisation
    // ──────────────────────────────────────────────────────────────────────────

    private static (byte[] data, bool isJpeg) NormalizeImage(byte[] data)
    {
        if (data == null || data.Length < 4) return (Array.Empty<byte>(), false);

        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return (data, true); // JPEG

        if (data.Length >= 8
            && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            return (data, false); // PNG

        try
        {
            using var mgk = new MagickImage(data);
            using var outMs = new MemoryStream();
            mgk.Format = MagickFormat.Png;
            mgk.Write(outMs);
            var result = outMs.ToArray();
            return (result.Length > 0 ? result : Array.Empty<byte>(), false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Magick.NET could not convert image to PNG");
            return (Array.Empty<byte>(), false);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // OOXML file builders
    // ──────────────────────────────────────────────────────────────────────────

    private static string BuildContentTypesXml(List<ImagePart> imageParts)
    {
        var sb = new StringBuilder(1024);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append($"<Types xmlns=\"{NsOpcCt}\">");
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        if (imageParts.Any(i => i.ContentType == "image/jpeg"))
            sb.Append("<Default Extension=\"jpg\" ContentType=\"image/jpeg\"/>");
        if (imageParts.Any(i => i.ContentType == "image/png"))
            sb.Append("<Default Extension=\"png\" ContentType=\"image/png\"/>");
        sb.Append("<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>");
        sb.Append("<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>");
        sb.Append("<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>");
        sb.Append("<Override PartName=\"/word/fontTable.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml\"/>");
        sb.Append("<Override PartName=\"/word/webSettings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.webSettings+xml\"/>");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string BuildRootRelsXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<Relationships xmlns=\"{NsOpcRel}\">" +
        $"<Relationship Id=\"rId1\" Type=\"{RelTypeDocument}\" Target=\"word/document.xml\"/>" +
        "</Relationships>";

    private static string BuildDocumentRelsXml(List<ImagePart> imageParts)
    {
        var sb = new StringBuilder(512);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append($"<Relationships xmlns=\"{NsOpcRel}\">");
        sb.Append($"<Relationship Id=\"rId1\" Type=\"{RelTypeStyles}\" Target=\"styles.xml\"/>");
        sb.Append($"<Relationship Id=\"rId2\" Type=\"{RelTypeSettings}\" Target=\"settings.xml\"/>");
        sb.Append($"<Relationship Id=\"rId3\" Type=\"{RelTypeFontTable}\" Target=\"fontTable.xml\"/>");
        sb.Append($"<Relationship Id=\"rId4\" Type=\"{RelTypeWebSettings}\" Target=\"webSettings.xml\"/>");
        foreach (var img in imageParts)
            sb.Append($"<Relationship Id=\"{Xe(img.RId)}\" Type=\"{RelTypeImage}\" Target=\"{Xe(img.ZipRelPath)}\"/>");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string BuildDocumentXml(StringBuilder bodyContent)
    {
        var sb = new StringBuilder(bodyContent.Length + 512);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append(
            $"<w:document xmlns:w=\"{NsW}\" " +
            $"xmlns:r=\"{NsOffRel}\" " +
            $"xmlns:wp=\"{NsWp}\" " +
            $"xmlns:a=\"{NsA}\" " +
            $"xmlns:pic=\"{NsPic}\">");
        sb.Append("<w:body>");
        sb.Append(bodyContent);
        sb.Append("</w:body>");
        sb.Append("</w:document>");
        return sb.ToString();
    }

    private static string BuildStylesXml()
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            $"<w:styles xmlns:w=\"{NsW}\">" +
            "<w:docDefaults>" +
            "<w:rPrDefault><w:rPr>" +
            "<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Calibri\"/>" +
            "<w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/>" +
            "</w:rPr></w:rPrDefault>" +
            "<w:pPrDefault><w:pPr>" +
            "<w:spacing w:after=\"160\" w:line=\"259\" w:lineRule=\"auto\"/>" +
            "</w:pPr></w:pPrDefault>" +
            "</w:docDefaults>" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\">" +
            "<w:name w:val=\"Normal\"/>" +
            "<w:rPr>" +
            "<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/>" +
            "<w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/>" +
            "</w:rPr>" +
            "</w:style>" +
            MakeStyle("PDFTitle",    "PDF Title",    "56", "1B2A4A", "480", "240") +
            MakeStyle("PDFHeading1", "PDF Heading 1","40", "1B2A4A", "480", "160") +
            MakeStyle("PDFHeading2", "PDF Heading 2","34", "2E4057", "400", "120") +
            MakeStyle("PDFHeading3", "PDF Heading 3","28", "3D5A80", "320", "80")  +
            MakeStyle("PDFHeading4", "PDF Heading 4","24", "4A7C9B", "240", "60")  +
            "</w:styles>";
    }

    private static string MakeStyle(
        string id, string name, string szVal, string color, string before, string after) =>
        $"<w:style w:type=\"paragraph\" w:customStyle=\"1\" w:styleId=\"{Xe(id)}\">" +
        $"<w:name w:val=\"{Xe(name)}\"/>" +
        "<w:basedOn w:val=\"Normal\"/>" +
        "<w:next w:val=\"Normal\"/>" +
        $"<w:pPr><w:spacing w:before=\"{before}\" w:after=\"{after}\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr>" +
        "<w:rPr>" +
        "<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/>" +
        "<w:b/><w:bCs/>" +
        $"<w:color w:val=\"{color}\"/>" +
        $"<w:sz w:val=\"{szVal}\"/><w:szCs w:val=\"{szVal}\"/>" +
        "</w:rPr>" +
        "</w:style>";

    private static string BuildSettingsXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:settings xmlns:w=\"{NsW}\" xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\" xmlns:o=\"urn:schemas-microsoft-com:office:office\">" +
        "<w:zoom w:percent=\"100\"/>" +
        "<w:defaultTabStop w:val=\"720\"/>" +
        "<w:characterSpacingControl w:val=\"doNotCompress\"/>" +
        "<w:compat>" +
        "<w:compatSetting w:name=\"compatibilityMode\" " +
            "w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/>" +
        "<w:compatSetting w:name=\"overrideTableStyleFontSizeAndJustification\" " +
            "w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"1\"/>" +
        "<w:compatSetting w:name=\"enableOpenTypeFeatures\" " +
            "w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"1\"/>" +
        "<w:compatSetting w:name=\"doNotFlipMirrorIndents\" " +
            "w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"1\"/>" +
        "<w:compatSetting w:name=\"differentiateMultirowTableHeaders\" " +
            "w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"1\"/>" +
        "</w:compat>" +
        "</w:settings>";

    private static string BuildFontTableXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:fonts xmlns:w=\"{NsW}\" xmlns:r=\"{NsOffRel}\">" +
        "<w:font w:name=\"Calibri\">" +
        "<w:panose1 w:val=\"020F0502020204030204\"/>" +
        "<w:charset w:val=\"00\"/>" +
        "<w:family w:val=\"swiss\"/>" +
        "<w:pitch w:val=\"variable\"/>" +
        "<w:sig w:usb0=\"E4002EFF\" w:usb1=\"C000247B\" w:usb2=\"00000009\" w:usb3=\"00000000\" w:csb0=\"000001FF\" w:csb1=\"00000000\"/>" +
        "</w:font>" +
        "<w:font w:name=\"Times New Roman\">" +
        "<w:panose1 w:val=\"02020603050405020304\"/>" +
        "<w:charset w:val=\"00\"/>" +
        "<w:family w:val=\"roman\"/>" +
        "<w:pitch w:val=\"variable\"/>" +
        "<w:sig w:usb0=\"E0002EFF\" w:usb1=\"C000785B\" w:usb2=\"00000009\" w:usb3=\"00000000\" w:csb0=\"000001FF\" w:csb1=\"00000000\"/>" +
        "</w:font>" +
        "<w:font w:name=\"Calibri Light\">" +
        "<w:panose1 w:val=\"020F0302020204030204\"/>" +
        "<w:charset w:val=\"00\"/>" +
        "<w:family w:val=\"swiss\"/>" +
        "<w:pitch w:val=\"variable\"/>" +
        "<w:sig w:usb0=\"E4002EFF\" w:usb1=\"C000247B\" w:usb2=\"00000009\" w:usb3=\"00000000\" w:csb0=\"000001FF\" w:csb1=\"00000000\"/>" +
        "</w:font>" +
        "</w:fonts>";

    private static string BuildWebSettingsXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:webSettings xmlns:w=\"{NsW}\" xmlns:r=\"{NsOffRel}\">" +
        "<w:optimizeForBrowser/>" +
        "<w:allowPNG/>" +
        "</w:webSettings>";
}



