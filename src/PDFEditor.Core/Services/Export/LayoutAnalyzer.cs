using NLog;
using PDFEditor.Core.Models.Layout;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// The "brain" of the layout reconstruction engine. Takes a flat list of
/// <see cref="LayoutCharacter"/> objects scattered across a page and reconstructs
/// the reading order: Characters → Words → Lines → Blocks (Paragraphs/Headings).
/// Also integrates table detection via vector path intersections.
///
/// The algorithms mirror the approach used by Python's pdf2docx library:
///   1. Sort characters by Y then X
///   2. Cluster into lines using baseline proximity
///   3. Merge characters into words using inter-character gap heuristics
///   4. Group lines into paragraphs using vertical spacing thresholds
///   5. Detect tables via horizontal/vertical line intersections
/// </summary>
public class LayoutAnalyzer
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Complete analysis result for a single page.
    /// </summary>
    public class PageAnalysis
    {
        public int PageNumber { get; set; }
        public float PageWidth { get; set; }
        public float PageHeight { get; set; }

        /// <summary>Ordered content elements (paragraphs, headings, tables) in reading order.</summary>
        public List<PageElement> Elements { get; set; } = new();

        /// <summary>Extracted images with position data.</summary>
        public List<LayoutExtractor.ExtractedImageInfo> Images { get; set; } = new();
    }

    /// <summary>
    /// A single content element on a page: a text block or a table.
    /// </summary>
    public class PageElement
    {
        public PageElementType Type { get; set; }

        /// <summary>For Type == TextBlock or Heading: the layout block.</summary>
        public LayoutBlock? Block { get; set; }

        /// <summary>For Type == Table: the detected table region.</summary>
        public TableRegion? Table { get; set; }

        /// <summary>Heading level (1-4) when Type == Heading.</summary>
        public int HeadingLevel { get; set; }

        /// <summary>Predominant bold/italic flags for text blocks.</summary>
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public float FontSize { get; set; }

        /// <summary>Vertical position used for ordering (PDF Y coordinate).</summary>
        public float TopY { get; set; }
    }

    public enum PageElementType { TextBlock, Heading, Table }

    private readonly TableDetectionEngine _tableDetector = new();

    // ──────────────────────────────────────────────────────────────────────────
    // Main entry point
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Analyzes a page's extracted layout data and produces an ordered list of content elements.
    /// </summary>
    public PageAnalysis Analyze(LayoutExtractor.PageLayoutData pageData)
    {
        var result = new PageAnalysis
        {
            PageNumber = pageData.PageNumber,
            PageWidth = pageData.PageWidth,
            PageHeight = pageData.PageHeight,
            Images = pageData.Images
        };

        if (pageData.Characters.Count == 0 && pageData.Lines.Count == 0)
            return result;

        // Step 1: Detect tables from vector paths
        var tables = _tableDetector.DetectTables(pageData.Lines, pageData.Characters);

        // Step 2: Determine which characters belong to tables (so we exclude them from text flow)
        var tableCharacters = new HashSet<LayoutCharacter>();
        foreach (var table in tables)
            foreach (var cell in table.Cells)
                foreach (var ch in cell.Content)
                    tableCharacters.Add(ch);

        // Step 3: Cluster remaining characters into lines and blocks
        var freeCharacters = pageData.Characters
            .Where(c => !tableCharacters.Contains(c))
            .ToList();

        var lines = ClusterCharactersIntoLines(freeCharacters);
        var blocks = ClusterLinesIntoBlocks(lines);

        // Step 4: Classify blocks as headings or paragraphs
        float bodyFontSize = DetermineBodyFontSize(blocks);
        var textElements = ClassifyBlocks(blocks, bodyFontSize);

        // Step 5: Create table elements
        var tableElements = tables.Select(t => new PageElement
        {
            Type = PageElementType.Table,
            Table = t,
            TopY = t.BBox.Y + t.BBox.Height // PDF Y is bottom-up; top of table
        }).ToList();

        // Step 6: Merge and sort all elements by vertical position (top-to-bottom reading order)
        var allElements = new List<PageElement>();
        allElements.AddRange(textElements);
        allElements.AddRange(tableElements);

        // PDF origin is bottom-left, so higher Y = higher on page → sort descending
        result.Elements = allElements.OrderByDescending(e => e.TopY).ToList();

        Log.Debug("Page {Page}: {Blocks} text blocks, {Tables} tables, {Total} elements total",
            pageData.PageNumber, textElements.Count, tableElements.Count, result.Elements.Count);

        return result;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Algorithm 1: Character → Line clustering
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Groups scattered characters into lines using baseline proximity.
    /// Two characters belong to the same line if ΔY &lt; averageFontSize * 0.5.
    /// Within a line, characters are sorted left-to-right, and word breaks are
    /// inserted where the horizontal gap exceeds a font-size-dependent threshold.
    /// </summary>
    private List<LayoutLine> ClusterCharactersIntoLines(List<LayoutCharacter> characters)
    {
        if (characters.Count == 0) return new List<LayoutLine>();

        // Sort by Y descending (top of page first), then X ascending
        var sorted = characters.OrderByDescending(c => c.BBox.Y).ThenBy(c => c.BBox.X).ToList();

        var lines = new List<LayoutLine>();
        var currentLineChars = new List<LayoutCharacter> { sorted[0] };
        float currentBaselineY = sorted[0].BBox.Y;

        for (int i = 1; i < sorted.Count; i++)
        {
            var ch = sorted[i];
            float avgFontSize = currentLineChars.Count > 0
                ? currentLineChars.Average(c => c.FontSize)
                : ch.FontSize;

            // Tolerance for baseline grouping: half the average font size
            float yTolerance = Math.Max(avgFontSize * 0.5f, 2.0f);

            if (Math.Abs(ch.BBox.Y - currentBaselineY) <= yTolerance)
            {
                // Same line
                currentLineChars.Add(ch);
            }
            else
            {
                // New line — finalize the current one
                lines.Add(BuildLine(currentLineChars));
                currentLineChars = new List<LayoutCharacter> { ch };
                currentBaselineY = ch.BBox.Y;
            }
        }

        // Don't forget the last line
        if (currentLineChars.Count > 0)
            lines.Add(BuildLine(currentLineChars));

        return lines;
    }

    /// <summary>
    /// Builds a <see cref="LayoutLine"/> from a set of characters on the same baseline.
    /// Inserts space characters where inter-character gap > threshold.
    /// </summary>
    private LayoutLine BuildLine(List<LayoutCharacter> chars)
    {
        // Sort left-to-right
        var ordered = chars.OrderBy(c => c.BBox.X).ToList();

        // Insert synthetic space characters where needed
        var withSpaces = new List<LayoutCharacter>();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (i > 0)
            {
                var prev = ordered[i - 1];
                var curr = ordered[i];

                // Gap between right edge of previous and left edge of current
                float gap = curr.BBox.X - prev.BBox.Right;

                // Average font size of the two adjacent characters
                float avgSize = (prev.FontSize + curr.FontSize) / 2f;

                // Word break threshold: gap > 30% of font size → insert space
                if (gap > avgSize * 0.3f)
                {
                    withSpaces.Add(new LayoutCharacter
                    {
                        Char = ' ',
                        BBox = new PdfRect(prev.BBox.Right, prev.BBox.Y, gap, prev.BBox.Height),
                        FontName = prev.FontName,
                        FontSize = prev.FontSize
                    });
                }
            }
            withSpaces.Add(ordered[i]);
        }

        // Compute bounding box from all characters
        float minX = ordered.Min(c => c.BBox.X);
        float minY = ordered.Min(c => c.BBox.Y);
        float maxX = ordered.Max(c => c.BBox.Right);
        float maxY = ordered.Max(c => c.BBox.Bottom);

        return new LayoutLine
        {
            Characters = withSpaces,
            BBox = new PdfRect(minX, minY, maxX - minX, maxY - minY)
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Algorithm 2: Line → Block (Paragraph) clustering
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Groups contiguous lines into blocks (paragraphs). Two consecutive lines belong
    /// to the same paragraph if:
    /// 1. The vertical gap between them is ≤ lineSpacingMultiplier × fontSize
    /// 2. Their X positions overlap (roughly the same horizontal column)
    /// </summary>
    private List<LayoutBlock> ClusterLinesIntoBlocks(List<LayoutLine> lines)
    {
        if (lines.Count == 0) return new List<LayoutBlock>();

        // Lines are already sorted top-to-bottom from ClusterCharactersIntoLines
        var blocks = new List<LayoutBlock>();
        var currentBlockLines = new List<LayoutLine> { lines[0] };

        for (int i = 1; i < lines.Count; i++)
        {
            var prevLine = currentBlockLines[^1];
            var currLine = lines[i];

            float avgFontSize = prevLine.Characters.Count > 0
                ? prevLine.Characters.Where(c => c.Char != ' ').Select(c => c.FontSize).DefaultIfEmpty(12f).Average()
                : 12f;

            // Vertical distance: gap between bottom of previous line and top of current line
            // In PDF coordinates (Y increases upward), this is prevLine.BBox.Y - currLine.BBox.Bottom
            float verticalGap = Math.Abs(prevLine.BBox.Y - currLine.BBox.Bottom);

            // Paragraph break threshold: gap > 1.8× the font size
            float maxGap = avgFontSize * 1.8f;

            // Also check horizontal overlap: if lines don't overlap horizontally, they're separate blocks
            bool horizontalOverlap = prevLine.BBox.X < currLine.BBox.Right
                                  && prevLine.BBox.Right > currLine.BBox.X;

            if (verticalGap <= maxGap && horizontalOverlap)
            {
                // Same paragraph
                currentBlockLines.Add(currLine);
            }
            else
            {
                // New paragraph
                blocks.Add(BuildBlock(currentBlockLines));
                currentBlockLines = new List<LayoutLine> { currLine };
            }
        }

        // Finalize last block
        if (currentBlockLines.Count > 0)
            blocks.Add(BuildBlock(currentBlockLines));

        return blocks;
    }

    private LayoutBlock BuildBlock(List<LayoutLine> lines)
    {
        float minX = lines.Min(l => l.BBox.X);
        float minY = lines.Min(l => l.BBox.Y);
        float maxX = lines.Max(l => l.BBox.Right);
        float maxY = lines.Max(l => l.BBox.Bottom);

        return new LayoutBlock
        {
            Lines = lines,
            BBox = new PdfRect(minX, minY, maxX - minX, maxY - minY)
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Block classification: Paragraph vs Heading
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Determines the "body text" font size as the median font size across all blocks.
    /// </summary>
    private float DetermineBodyFontSize(List<LayoutBlock> blocks)
    {
        var sizes = blocks
            .SelectMany(b => b.Lines)
            .SelectMany(l => l.Characters)
            .Where(c => c.Char != ' ')
            .Select(c => c.FontSize)
            .OrderBy(s => s)
            .ToList();

        if (sizes.Count == 0) return 12f;
        return sizes[sizes.Count / 2]; // Median
    }

    /// <summary>
    /// Classifies each block as a heading or paragraph based on font size ratio to body text.
    /// </summary>
    private List<PageElement> ClassifyBlocks(List<LayoutBlock> blocks, float bodyFontSize)
    {
        var elements = new List<PageElement>();

        foreach (var block in blocks)
        {
            var nonSpaceChars = block.Lines
                .SelectMany(l => l.Characters)
                .Where(c => c.Char != ' ')
                .ToList();

            if (nonSpaceChars.Count == 0) continue;

            float avgFontSize = nonSpaceChars.Average(c => c.FontSize);
            float ratio = bodyFontSize > 0 ? avgFontSize / bodyFontSize : 1.0f;

            bool isBold = nonSpaceChars.Count(c =>
                c.FontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                c.FontName.Contains("Heavy", StringComparison.OrdinalIgnoreCase) ||
                c.FontName.Contains("Black", StringComparison.OrdinalIgnoreCase))
                > nonSpaceChars.Count / 2;

            bool isItalic = nonSpaceChars.Count(c =>
                c.FontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                c.FontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase))
                > nonSpaceChars.Count / 2;

            bool isHeading = ratio > 1.2f || (ratio > 1.0f && isBold);
            int headingLevel = ratio >= 2.0f ? 1
                             : ratio >= 1.6f ? 2
                             : ratio >= 1.3f ? 3
                             : (ratio >= 1.15f && isBold) ? 4
                             : 0;

            elements.Add(new PageElement
            {
                Type = isHeading && headingLevel > 0 ? PageElementType.Heading : PageElementType.TextBlock,
                Block = block,
                HeadingLevel = headingLevel,
                IsBold = isBold,
                IsItalic = isItalic,
                FontSize = avgFontSize,
                TopY = block.BBox.Y + block.BBox.Height // Top edge in PDF coordinates
            });
        }

        return elements;
    }
}
