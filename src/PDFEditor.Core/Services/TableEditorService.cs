using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Font;
using iText.Kernel.Colors;
using iText.Kernel.Geom;
using iText.IO.Font.Constants;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Represents a cell in a PDF table
/// </summary>
public class TableCell
{
    public int Row { get; set; }
    public int Column { get; set; }
    public string Text { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public int RowSpan { get; set; } = 1;
    public int ColSpan { get; set; } = 1;
}

/// <summary>
/// Represents a detected table in a PDF
/// </summary>
public class DetectedTable
{
    public int PageIndex { get; set; }
    public int TableIndex { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<TableCell> Cells { get; set; } = new();
    public List<string> Headers { get; set; } = new();
}

/// <summary>
/// Service for detecting, extracting, and editing table structures in PDF documents.
/// Uses text position clustering to identify table boundaries and cell contents.
/// </summary>
public class TableEditorService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private const float ColumnGapThreshold = 15f; // points
    private const float RowGapThreshold = 3f; // points

    /// <summary>
    /// Detects tables on a specific page
    /// </summary>
    public List<DetectedTable> DetectTables(byte[] pdfBytes, int pageIndex)
    {
        Log.Info("Detecting tables on page {Page}", pageIndex + 1);
        var tables = new List<DetectedTable>();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var doc = new PdfDocument(reader);

        int pageNum = pageIndex + 1;
        if (pageNum < 1 || pageNum > doc.GetNumberOfPages())
            return tables;

        var page = doc.GetPage(pageNum);
        var text = PdfTextExtractor.GetTextFromPage(page);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Heuristic: detect table-like structures by looking for lines with
        // consistent column alignment (multiple tab/space-separated values)
        var tableLines = new List<(int lineIndex, string[] columns)>();
        int tableStart = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            var columns = SplitIntoColumns(lines[i]);
            if (columns.Length >= 2)
            {
                if (tableStart == -1) tableStart = i;
                tableLines.Add((i, columns));
            }
            else if (tableStart != -1 && tableLines.Count >= 2)
            {
                // End of table region — create table
                tables.Add(CreateTableFromLines(pageIndex, tables.Count, tableLines, page));
                tableLines.Clear();
                tableStart = -1;
            }
            else
            {
                tableLines.Clear();
                tableStart = -1;
            }
        }

        // Handle table at end of page
        if (tableLines.Count >= 2)
        {
            tables.Add(CreateTableFromLines(pageIndex, tables.Count, tableLines, page));
        }

        Log.Info("Detected {Count} table(s) on page {Page}", tables.Count, pageIndex + 1);
        return tables;
    }

    /// <summary>
    /// Detects tables across all pages
    /// </summary>
    public List<DetectedTable> DetectAllTables(byte[] pdfBytes)
    {
        Log.Info("Detecting tables across all pages");
        var allTables = new List<DetectedTable>();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var doc = new PdfDocument(reader);

        for (int i = 0; i < doc.GetNumberOfPages(); i++)
        {
            var pageTables = DetectTables(pdfBytes, i);
            allTables.AddRange(pageTables);
        }

        Log.Info("Detected {Count} table(s) total", allTables.Count);
        return allTables;
    }

    /// <summary>
    /// Extracts a table as CSV text
    /// </summary>
    public string ExtractTableAsCsv(DetectedTable table)
    {
        var sb = new System.Text.StringBuilder();
        var rows = table.Cells.GroupBy(c => c.Row).OrderBy(g => g.Key);

        foreach (var row in rows)
        {
            var cells = row.OrderBy(c => c.Column).Select(c =>
            {
                var val = c.Text;
                if (val.Contains(',') || val.Contains('"') || val.Contains('\n'))
                    val = $"\"{val.Replace("\"", "\"\"")}\"";
                return val;
            });
            sb.AppendLine(string.Join(",", cells));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Modifies a cell value in a table using overlay approach
    /// </summary>
    public byte[] EditCell(byte[] pdfBytes, DetectedTable table, int row, int column, string newValue)
    {
        Log.Info("Editing cell [{Row},{Col}] in table on page {Page}", row, column, table.PageIndex + 1);

        var cell = table.Cells.FirstOrDefault(c => c.Row == row && c.Column == column);
        if (cell == null)
            throw new InvalidOperationException($"Cell [{row},{column}] not found in table");

        var textEdit = new PdfTextEditService();
        return textEdit.ApplyEdit(pdfBytes, new TextEditOperation
        {
            PageIndex = table.PageIndex,
            X = cell.X,
            Y = cell.Y,
            Width = cell.Width,
            Height = cell.Height,
            OriginalText = cell.Text,
            NewText = newValue,
            FontSize = 10f
        });
    }

    /// <summary>
    /// Adds a new row to a table
    /// </summary>
    public byte[] AddRow(byte[] pdfBytes, DetectedTable table, string[] cellValues)
    {
        Log.Info("Adding row to table on page {Page}", table.PageIndex + 1);

        if (cellValues.Length != table.ColumnCount)
            throw new ArgumentException($"Expected {table.ColumnCount} cell values, got {cellValues.Length}");

        var outMs = new MemoryStream();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var writer = new PdfWriter(outMs);
        using var doc = new PdfDocument(reader, writer);

        var page = doc.GetPage(table.PageIndex + 1);
        var canvas = new PdfCanvas(page);
        var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

        float newRowY = table.Y - table.Height - 15f; // Below existing table
        float cellWidth = table.Width / table.ColumnCount;

        for (int col = 0; col < cellValues.Length; col++)
        {
            float cellX = table.X + col * cellWidth;

            // Draw cell border
            canvas.SaveState();
            canvas.SetStrokeColor(ColorConstants.BLACK);
            canvas.SetLineWidth(0.5f);
            canvas.Rectangle(cellX, newRowY, cellWidth, 15f);
            canvas.Stroke();

            // Draw text
            canvas.BeginText();
            canvas.SetFontAndSize(font, 10f);
            canvas.SetFillColor(ColorConstants.BLACK);
            canvas.MoveText(cellX + 2f, newRowY + 3f);
            canvas.ShowText(cellValues[col]);
            canvas.EndText();
            canvas.RestoreState();
        }

        canvas.Release();
        doc.Close();
        return outMs.ToArray();
    }

    /// <summary>
    /// Generates an HTML table representation
    /// </summary>
    public string ExtractTableAsHtml(DetectedTable table)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<table border='1' cellpadding='4' cellspacing='0'>");

        var rows = table.Cells.GroupBy(c => c.Row).OrderBy(g => g.Key);
        bool isFirstRow = true;

        foreach (var row in rows)
        {
            sb.AppendLine("  <tr>");
            string tag = isFirstRow && table.Headers.Any() ? "th" : "td";
            foreach (var cell in row.OrderBy(c => c.Column))
            {
                var attrs = "";
                if (cell.ColSpan > 1) attrs += $" colspan='{cell.ColSpan}'";
                if (cell.RowSpan > 1) attrs += $" rowspan='{cell.RowSpan}'";
                sb.AppendLine($"    <{tag}{attrs}>{System.Net.WebUtility.HtmlEncode(cell.Text)}</{tag}>");
            }
            sb.AppendLine("  </tr>");
            isFirstRow = false;
        }

        sb.AppendLine("</table>");
        return sb.ToString();
    }

    private string[] SplitIntoColumns(string line)
    {
        // Split by 2+ spaces or tabs (common in PDF text layout)
        var parts = System.Text.RegularExpressions.Regex.Split(line.Trim(), @"\s{2,}|\t+")
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();
        return parts;
    }

    private DetectedTable CreateTableFromLines(int pageIndex, int tableIndex,
        List<(int lineIndex, string[] columns)> tableLines, PdfPage page)
    {
        var mediaBox = page.GetMediaBox();
        int maxCols = tableLines.Max(l => l.columns.Length);

        var table = new DetectedTable
        {
            PageIndex = pageIndex,
            TableIndex = tableIndex,
            X = 50f,
            Y = mediaBox.GetHeight() - 100f - tableLines[0].lineIndex * 15f,
            Width = mediaBox.GetWidth() - 100f,
            Height = tableLines.Count * 15f,
            RowCount = tableLines.Count,
            ColumnCount = maxCols
        };

        // First row as headers
        if (tableLines.Count > 0)
            table.Headers = tableLines[0].columns.ToList();

        float cellWidth = table.Width / maxCols;

        for (int r = 0; r < tableLines.Count; r++)
        {
            var cols = tableLines[r].columns;
            for (int c = 0; c < cols.Length; c++)
            {
                table.Cells.Add(new TableCell
                {
                    Row = r,
                    Column = c,
                    Text = cols[c],
                    X = table.X + c * cellWidth,
                    Y = table.Y - r * 15f,
                    Width = cellWidth,
                    Height = 15f
                });
            }
        }

        return table;
    }
}
