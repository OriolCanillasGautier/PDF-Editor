using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using NLog;
using PDFEditor.Core.Abstractions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF content to XLSX format. Attempts to detect tabular data and
/// places text into rows/columns. When no clear table structure is found,
/// falls back to one line per row.
/// </summary>
public class XlsxExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string FormatName => "Microsoft Excel (XLSX)";
    public string[] SupportedExtensions => new[] { ".xlsx" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    public async Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var xlsxBytes = await Task.Run(() =>
                GenerateXlsx(pdfBytes, options, cancellationToken), cancellationToken);
            return ExportResult.Ok(xlsxBytes, $"{options.BaseFileName}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "XLSX export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("XLSX export produces a single workbook.");
    }

    private byte[] GenerateXlsx(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        using var spreadsheet = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook);

        var workbookPart = spreadsheet.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdfDoc = new PdfDocument(reader);

        int pageCount = pdfDoc.GetNumberOfPages();
        var pageIndices = options.PageIndices ?? Enumerable.Range(0, pageCount).ToArray();

        uint sheetId = 1;
        foreach (int pageIdx in pageIndices)
        {
            ct.ThrowIfCancellationRequested();

            int pageNum = pageIdx + 1;
            if (pageNum < 1 || pageNum > pageCount) continue;

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            // Extract text
            var page = pdfDoc.GetPage(pageNum);
            var strategy = new SimpleTextExtractionStrategy();
            var text = PdfTextExtractor.GetTextFromPage(page, strategy);

            // Split into lines and parse into rows/columns
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            uint rowIndex = 1;
            foreach (var line in lines)
            {
                var row = new Row { RowIndex = rowIndex };
                var columns = SplitIntoColumns(line.Trim());

                for (int col = 0; col < columns.Length; col++)
                {
                    string cellRef = GetCellReference(col, rowIndex);
                    var cell = new Cell
                    {
                        CellReference = cellRef,
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(columns[col].Trim()))
                    };
                    row.AppendChild(cell);
                }

                sheetData.AppendChild(row);
                rowIndex++;
            }

            // Add sheet to workbook
            var sheet = new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId,
                Name = $"Page {pageNum}"
            };
            sheets.AppendChild(sheet);
            sheetId++;
        }

        workbookPart.Workbook.Save();
        spreadsheet.Dispose();

        Log.Info("XLSX export completed: {Pages} sheets", pageIndices.Length);
        return ms.ToArray();
    }

    /// <summary>
    /// Splits a line into columns using tab, pipe, or multiple-space delimiters.
    /// Falls back to the whole line as a single column.
    /// </summary>
    private static string[] SplitIntoColumns(string line)
    {
        // Tab-delimited
        if (line.Contains('\t'))
            return line.Split('\t');

        // Pipe-delimited (common in PDF tables)
        if (line.Contains('|'))
            return line.Split('|', StringSplitOptions.RemoveEmptyEntries);

        // Multiple spaces (3+) suggest column separation
        var multiSpaceColumns = Regex.Split(line, @"\s{3,}");
        if (multiSpaceColumns.Length > 1)
            return multiSpaceColumns;

        // Single column
        return new[] { line };
    }

    private static string GetCellReference(int colIndex, uint rowIndex)
    {
        var sb = new StringBuilder();
        int col = colIndex;
        do
        {
            sb.Insert(0, (char)('A' + (col % 26)));
            col = col / 26 - 1;
        } while (col >= 0);
        sb.Append(rowIndex);
        return sb.ToString();
    }
}
