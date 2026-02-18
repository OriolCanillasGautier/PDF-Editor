using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for PdfTextEditService, TableEditorService
/// </summary>
public class TextEditServiceTests
{
    // ===== PdfTextEditService Tests =====

    [Fact]
    public void TextEdit_ExtractTextBlocks_ReturnsBlocks()
    {
        var service = new PdfTextEditService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1, "Hello World\nSecond line\nThird line");

        var blocks = service.ExtractTextBlocks(pdf, 0);

        Assert.NotNull(blocks);
        Assert.True(blocks.Count > 0);
    }

    [Fact]
    public void TextEdit_ExtractTextBlocks_InvalidPage_ReturnsEmpty()
    {
        var service = new PdfTextEditService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var blocks = service.ExtractTextBlocks(pdf, 99);

        Assert.Empty(blocks);
    }

    [Fact]
    public void TextEdit_ApplyEdit_ReturnsModifiedPdf()
    {
        var service = new PdfTextEditService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1, "Original text here");

        var edit = new TextEditOperation
        {
            PageIndex = 0,
            X = 50,
            Y = 700,
            Width = 200,
            Height = 20,
            OriginalText = "Original text here",
            NewText = "Replaced text"
        };

        var result = service.ApplyEdit(pdf, edit);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void TextEdit_ApplyEdits_MultiplEdits()
    {
        var service = new PdfTextEditService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1, "Some text content");

        var edits = new List<TextEditOperation>
        {
            new TextEditOperation { PageIndex = 0, X = 50, Y = 700, Width = 200, Height = 20, NewText = "Edit 1" },
            new TextEditOperation { PageIndex = 0, X = 50, Y = 650, Width = 200, Height = 20, NewText = "Edit 2" }
        };

        var result = service.ApplyEdits(pdf, edits);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void TextEdit_FindAndReplace_ReturnsBytes()
    {
        var service = new PdfTextEditService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1, "Hello World Test");

        var result = service.FindAndReplace(pdf, "Hello", "Goodbye");

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void TextEdit_AddTextAtPosition_ReturnsBytes()
    {
        var service = new PdfTextEditService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var result = service.AddTextAtPosition(pdf, 0, "New text", 100, 400);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void TextEdit_AddTextAtPosition_WithParams()
    {
        var service = new PdfTextEditService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var result = service.AddTextAtPosition(pdf, 0, "Styled", 100, 500, fontSize: 18, fontName: "Courier");

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    // ===== TableEditorService Tests =====

    [Fact]
    public void TableEditor_DetectTables_ReturnsResults()
    {
        var service = new TableEditorService();
        var pdf = TestPdfGenerator.CreatePdfWithContent(
            "Name    Age    City\nAlice   30     NYC\nBob     25     LA");

        var tables = service.DetectTables(pdf, 0);

        Assert.NotNull(tables);
    }

    [Fact]
    public void TableEditor_DetectAllTables_MultiPage()
    {
        var service = new TableEditorService();
        var pdf = TestPdfGenerator.CreateSimplePdf(3);

        var allTables = service.DetectAllTables(pdf);

        Assert.NotNull(allTables);
    }

    [Fact]
    public void TableEditor_ExtractTableAsCsv_ReturnsString()
    {
        var service = new TableEditorService();
        var table = new DetectedTable
        {
            Cells = new List<TableCell>
            {
                new() { Row = 0, Column = 0, Text = "Name" },
                new() { Row = 0, Column = 1, Text = "Age" },
                new() { Row = 1, Column = 0, Text = "Alice" },
                new() { Row = 1, Column = 1, Text = "30" }
            },
            RowCount = 2,
            ColumnCount = 2
        };

        var csv = service.ExtractTableAsCsv(table);

        Assert.Contains("Name", csv);
        Assert.Contains("Alice", csv);
    }

    [Fact]
    public void TableEditor_ExtractTableAsHtml_ReturnsHtml()
    {
        var service = new TableEditorService();
        var table = new DetectedTable
        {
            Cells = new List<TableCell>
            {
                new() { Row = 0, Column = 0, Text = "Header1" },
                new() { Row = 1, Column = 0, Text = "Data1" }
            },
            RowCount = 2,
            ColumnCount = 1
        };

        var html = service.ExtractTableAsHtml(table);

        Assert.Contains("<table", html);
        Assert.Contains("Header1", html);
    }

    [Fact]
    public void TableEditor_EditCell_ReturnsBytes()
    {
        var service = new TableEditorService();
        var pdf = TestPdfGenerator.CreatePdfWithContent(
            "Col1    Col2\nVal1    Val2");

        var tables = service.DetectTables(pdf, 0);
        if (tables.Count > 0)
        {
            var result = service.EditCell(pdf, tables[0], 0, 0, "NewVal");
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
        }
    }
}
