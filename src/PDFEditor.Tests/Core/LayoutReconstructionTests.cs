using PDFEditor.Core.Models.Layout;
using PDFEditor.Core.Services.Export;
using Xunit;
using System.Collections.Generic;
using System.Linq;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for the layout reconstruction engine:
/// PdfRect geometry, TableDetectionEngine, LayoutAnalyzer algorithms.
/// </summary>
public class LayoutReconstructionTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // PdfRect geometry tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PdfRect_Properties_CalculateCorrectly()
    {
        var rect = new PdfRect(10f, 20f, 100f, 50f);

        Assert.Equal(10f, rect.X);
        Assert.Equal(20f, rect.Y);
        Assert.Equal(100f, rect.Width);
        Assert.Equal(50f, rect.Height);
        Assert.Equal(110f, rect.Right);
        Assert.Equal(70f, rect.Bottom);
        Assert.Equal(60f, rect.CenterX);
        Assert.Equal(45f, rect.CenterY);
    }

    [Fact]
    public void PdfRect_Intersects_OverlappingRects_ReturnsTrue()
    {
        var a = new PdfRect(0, 0, 100, 100);
        var b = new PdfRect(50, 50, 100, 100);

        Assert.True(a.Intersects(b));
        Assert.True(b.Intersects(a));
    }

    [Fact]
    public void PdfRect_Intersects_NonOverlapping_ReturnsFalse()
    {
        var a = new PdfRect(0, 0, 50, 50);
        var b = new PdfRect(100, 100, 50, 50);

        Assert.False(a.Intersects(b));
        Assert.False(b.Intersects(a));
    }

    [Fact]
    public void PdfRect_Intersects_TouchingEdges_ReturnsFalse()
    {
        var a = new PdfRect(0, 0, 50, 50);
        var b = new PdfRect(50, 0, 50, 50); // Touching at x=50 but not overlapping

        Assert.False(a.Intersects(b));
    }

    [Fact]
    public void PdfRect_Intersects_ContainedRect_ReturnsTrue()
    {
        var outer = new PdfRect(0, 0, 200, 200);
        var inner = new PdfRect(50, 50, 50, 50);

        Assert.True(outer.Intersects(inner));
        Assert.True(inner.Intersects(outer));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PdfLine tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PdfLine_IsHorizontal_DetectsCorrectly()
    {
        var horizontal = new PdfLine { X1 = 0, Y1 = 100, X2 = 200, Y2 = 100 };
        var vertical = new PdfLine { X1 = 100, Y1 = 0, X2 = 100, Y2 = 200 };
        var diagonal = new PdfLine { X1 = 0, Y1 = 0, X2 = 100, Y2 = 100 };

        Assert.True(horizontal.IsHorizontal);
        Assert.False(horizontal.IsVertical);

        Assert.False(vertical.IsHorizontal);
        Assert.True(vertical.IsVertical);

        Assert.False(diagonal.IsHorizontal);
        Assert.False(diagonal.IsVertical);
    }

    [Fact]
    public void PdfLine_NearlyHorizontal_WithinTolerance_DetectsCorrectly()
    {
        // Y difference < 1.0 should still count as horizontal
        var nearlyH = new PdfLine { X1 = 0, Y1 = 100, X2 = 200, Y2 = 100.5f };
        Assert.True(nearlyH.IsHorizontal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TableDetectionEngine tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TableDetection_SimpleGrid_DetectsTable()
    {
        var engine = new TableDetectionEngine();

        // Create a 2x2 grid (3 horizontal lines × 3 vertical lines)
        var lines = new List<PdfLine>
        {
            // Horizontal lines (top, middle, bottom)
            new PdfLine { X1 = 0, Y1 = 300, X2 = 200, Y2 = 300 },
            new PdfLine { X1 = 0, Y1 = 200, X2 = 200, Y2 = 200 },
            new PdfLine { X1 = 0, Y1 = 100, X2 = 200, Y2 = 100 },
            // Vertical lines (left, middle, right)
            new PdfLine { X1 = 0, Y1 = 100, X2 = 0, Y2 = 300 },
            new PdfLine { X1 = 100, Y1 = 100, X2 = 100, Y2 = 300 },
            new PdfLine { X1 = 200, Y1 = 100, X2 = 200, Y2 = 300 },
        };

        var characters = new List<LayoutCharacter>();
        var tables = engine.DetectTables(lines, characters);

        Assert.Single(tables);
        Assert.Equal(2, tables[0].RowCount);
        Assert.Equal(2, tables[0].ColumnCount);
        Assert.Equal(4, tables[0].Cells.Count);
    }

    [Fact]
    public void TableDetection_NoLines_ReturnsEmpty()
    {
        var engine = new TableDetectionEngine();
        var tables = engine.DetectTables(new List<PdfLine>(), new List<LayoutCharacter>());
        Assert.Empty(tables);
    }

    [Fact]
    public void TableDetection_OnlyHorizontalLines_ReturnsEmpty()
    {
        var engine = new TableDetectionEngine();
        var lines = new List<PdfLine>
        {
            new PdfLine { X1 = 0, Y1 = 100, X2 = 200, Y2 = 100 },
            new PdfLine { X1 = 0, Y1 = 200, X2 = 200, Y2 = 200 },
        };

        var tables = engine.DetectTables(lines, new List<LayoutCharacter>());
        Assert.Empty(tables);
    }

    [Fact]
    public void TableDetection_CharactersMappedToCells()
    {
        var engine = new TableDetectionEngine();

        // Create a simple 1x1 grid (single cell)
        var lines = new List<PdfLine>
        {
            new PdfLine { X1 = 0, Y1 = 200, X2 = 200, Y2 = 200 },
            new PdfLine { X1 = 0, Y1 = 100, X2 = 200, Y2 = 100 },
            new PdfLine { X1 = 0, Y1 = 100, X2 = 0, Y2 = 200 },
            new PdfLine { X1 = 200, Y1 = 100, X2 = 200, Y2 = 200 },
        };

        // Place a character in the center of the cell
        var characters = new List<LayoutCharacter>
        {
            new LayoutCharacter
            {
                Char = 'A',
                BBox = new PdfRect(90, 140, 20, 20), // Center at (100, 150) — inside the cell
                FontName = "Arial",
                FontSize = 12
            }
        };

        var tables = engine.DetectTables(lines, characters);

        Assert.Single(tables);
        var cell = tables[0].Cells[0];
        Assert.Single(cell.Content);
        Assert.Equal('A', cell.Content[0].Char);
    }

    [Fact]
    public void TableDetection_3x3Grid_CorrectCellCount()
    {
        var engine = new TableDetectionEngine();

        var lines = new List<PdfLine>();
        // 4 horizontal lines
        for (int i = 0; i < 4; i++)
            lines.Add(new PdfLine { X1 = 0, Y1 = 100 + i * 100, X2 = 300, Y2 = 100 + i * 100 });
        // 4 vertical lines
        for (int i = 0; i < 4; i++)
            lines.Add(new PdfLine { X1 = i * 100, Y1 = 100, X2 = i * 100, Y2 = 400 });

        var tables = engine.DetectTables(lines, new List<LayoutCharacter>());

        Assert.Single(tables);
        Assert.Equal(3, tables[0].RowCount);
        Assert.Equal(3, tables[0].ColumnCount);
        Assert.Equal(9, tables[0].Cells.Count);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // LayoutAnalyzer tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LayoutAnalyzer_EmptyPage_ReturnsEmptyAnalysis()
    {
        var analyzer = new LayoutAnalyzer();
        var pageData = new LayoutExtractor.PageLayoutData
        {
            PageNumber = 1,
            PageWidth = 612,
            PageHeight = 792,
            Characters = new List<LayoutCharacter>(),
            Lines = new List<PdfLine>()
        };

        var result = analyzer.Analyze(pageData);

        Assert.Equal(1, result.PageNumber);
        Assert.Empty(result.Elements);
    }

    [Fact]
    public void LayoutAnalyzer_SingleLine_ProducesOneBlock()
    {
        var analyzer = new LayoutAnalyzer();

        // Create characters for "Hello" on the same baseline
        var chars = "Hello".Select((c, i) => new LayoutCharacter
        {
            Char = c,
            BBox = new PdfRect(72 + i * 7, 700, 7, 12),
            FontName = "Arial",
            FontSize = 12
        }).ToList();

        var pageData = new LayoutExtractor.PageLayoutData
        {
            PageNumber = 1,
            PageWidth = 612,
            PageHeight = 792,
            Characters = chars,
            Lines = new List<PdfLine>()
        };

        var result = analyzer.Analyze(pageData);

        Assert.Single(result.Elements);
        Assert.Equal(LayoutAnalyzer.PageElementType.TextBlock, result.Elements[0].Type);
        Assert.NotNull(result.Elements[0].Block);
        Assert.Single(result.Elements[0].Block!.Lines);
        Assert.Contains("Hello", result.Elements[0].Block!.Lines[0].Text);
    }

    [Fact]
    public void LayoutAnalyzer_TwoLinesCloseSpacing_SameParagraph()
    {
        var analyzer = new LayoutAnalyzer();

        // Line 1: "Line one" at Y=700
        var line1Chars = "Line one".Select((c, i) => new LayoutCharacter
        {
            Char = c,
            BBox = new PdfRect(72 + i * 7, 700, 7, 12),
            FontName = "Arial",
            FontSize = 12
        }).ToList();

        // Line 2: "Line two" at Y=686 (14pt below — roughly 1.2× font size → same paragraph)
        var line2Chars = "Line two".Select((c, i) => new LayoutCharacter
        {
            Char = c,
            BBox = new PdfRect(72 + i * 7, 686, 7, 12),
            FontName = "Arial",
            FontSize = 12
        }).ToList();

        var pageData = new LayoutExtractor.PageLayoutData
        {
            PageNumber = 1,
            PageWidth = 612,
            PageHeight = 792,
            Characters = line1Chars.Concat(line2Chars).ToList(),
            Lines = new List<PdfLine>()
        };

        var result = analyzer.Analyze(pageData);

        // Should produce one block with two lines
        Assert.Single(result.Elements);
        Assert.Equal(2, result.Elements[0].Block!.Lines.Count);
    }

    [Fact]
    public void LayoutAnalyzer_TwoLinesLargeGap_SeparateParagraphs()
    {
        var analyzer = new LayoutAnalyzer();

        // Line 1 at Y=700
        var line1 = "First".Select((c, i) => new LayoutCharacter
        {
            Char = c,
            BBox = new PdfRect(72 + i * 7, 700, 7, 12),
            FontName = "Arial",
            FontSize = 12
        }).ToList();

        // Line 2 at Y=600 (100pt below — large gap → separate paragraph)
        var line2 = "Second".Select((c, i) => new LayoutCharacter
        {
            Char = c,
            BBox = new PdfRect(72 + i * 7, 600, 7, 12),
            FontName = "Arial",
            FontSize = 12
        }).ToList();

        var pageData = new LayoutExtractor.PageLayoutData
        {
            PageNumber = 1,
            PageWidth = 612,
            PageHeight = 792,
            Characters = line1.Concat(line2).ToList(),
            Lines = new List<PdfLine>()
        };

        var result = analyzer.Analyze(pageData);

        Assert.Equal(2, result.Elements.Count);
    }

    [Fact]
    public void LayoutAnalyzer_LargeFontText_ClassifiedAsHeading()
    {
        var analyzer = new LayoutAnalyzer();

        // Heading text at 24pt
        var headingChars = "Title".Select((c, i) => new LayoutCharacter
        {
            Char = c,
            BBox = new PdfRect(72 + i * 14, 700, 14, 24),
            FontName = "Arial-Bold",
            FontSize = 24
        }).ToList();

        // Body text at 12pt (far enough below to be separate)
        var bodyChars = "Body text here".Select((c, i) => new LayoutCharacter
        {
            Char = c,
            BBox = new PdfRect(72 + i * 7, 600, 7, 12),
            FontName = "Arial",
            FontSize = 12
        }).ToList();

        var pageData = new LayoutExtractor.PageLayoutData
        {
            PageNumber = 1,
            PageWidth = 612,
            PageHeight = 792,
            Characters = headingChars.Concat(bodyChars).ToList(),
            Lines = new List<PdfLine>()
        };

        var result = analyzer.Analyze(pageData);

        Assert.Equal(2, result.Elements.Count);

        // First element should be classified as heading (higher on page)
        var heading = result.Elements[0];
        Assert.Equal(LayoutAnalyzer.PageElementType.Heading, heading.Type);
        Assert.True(heading.HeadingLevel >= 1 && heading.HeadingLevel <= 4);

        // Second should be a text block
        var body = result.Elements[1];
        Assert.Equal(LayoutAnalyzer.PageElementType.TextBlock, body.Type);
    }

    [Fact]
    public void LayoutAnalyzer_TextAndTable_BothDetected()
    {
        var analyzer = new LayoutAnalyzer();

        // Free-floating text above the table
        var textChars = "Header".Select((c, i) => new LayoutCharacter
        {
            Char = c,
            BBox = new PdfRect(72 + i * 7, 700, 7, 12),
            FontName = "Arial",
            FontSize = 12
        }).ToList();

        // Character inside a table cell
        var tableChar = new LayoutCharacter
        {
            Char = 'X',
            BBox = new PdfRect(50, 150, 10, 12),
            FontName = "Arial",
            FontSize = 12
        };

        // Table grid
        var tableLines = new List<PdfLine>
        {
            new PdfLine { X1 = 0, Y1 = 200, X2 = 200, Y2 = 200 },
            new PdfLine { X1 = 0, Y1 = 100, X2 = 200, Y2 = 100 },
            new PdfLine { X1 = 0, Y1 = 100, X2 = 0, Y2 = 200 },
            new PdfLine { X1 = 200, Y1 = 100, X2 = 200, Y2 = 200 },
        };

        var allChars = textChars.Append(tableChar).ToList();

        var pageData = new LayoutExtractor.PageLayoutData
        {
            PageNumber = 1,
            PageWidth = 612,
            PageHeight = 792,
            Characters = allChars,
            Lines = tableLines
        };

        var result = analyzer.Analyze(pageData);

        // Should have at least one text block and one table
        Assert.True(result.Elements.Count >= 2);

        var hasText = result.Elements.Any(e => e.Type == LayoutAnalyzer.PageElementType.TextBlock);
        var hasTable = result.Elements.Any(e => e.Type == LayoutAnalyzer.PageElementType.Table);
        Assert.True(hasText, "Expected at least one text block");
        Assert.True(hasTable, "Expected at least one table");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // NativeDocxExportProvider registration test
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExportProviderRegistry_ContainsNativeDocxProvider()
    {
        var registry = ExportProviderRegistry.CreateDefault();

        var nativeDocx = registry.Providers
            .FirstOrDefault(p => p.FormatName == "Microsoft Word Native (DOCX)");

        Assert.NotNull(nativeDocx);
        Assert.Contains(".docx", nativeDocx!.SupportedExtensions);
        Assert.True(nativeDocx.SupportsBatch);
    }

    [Fact]
    public void NativeDocxExportProvider_ImplementsInterface()
    {
        var provider = new NativeDocxExportProvider();

        Assert.Equal("Microsoft Word Native (DOCX)", provider.FormatName);
        Assert.Contains(".docx", provider.SupportedExtensions);
        Assert.True(provider.SupportsBatch);
        Assert.False(provider.SupportsPerPageExport);
    }
}
