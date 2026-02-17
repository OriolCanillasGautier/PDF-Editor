using NLog;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF content as plain text or Markdown
/// </summary>
public class TextExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string FormatName => "Plain Text (TXT)";
    public string[] SupportedExtensions => new[] { ".txt" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => true;

    public async Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var text = await Task.Run(() =>
            {
                var searchService = new PdfSearchService();
                return searchService.ExtractAllText(pdfBytes);
            }, cancellationToken);

            var data = System.Text.Encoding.UTF8.GetBytes(text);
            return ExportResult.Ok(data, $"{options.BaseFileName}.txt", "text/plain");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Text export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public async Task<List<ExportResult>> ExportPagesAsync(byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ExportResult>();
        var pdfOps = new PdfOperations();
        var pageCount = pdfOps.GetPageCount(pdfBytes);
        var pageIndices = options.PageIndices ?? Enumerable.Range(0, pageCount).ToArray();

        for (int i = 0; i < pageIndices.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageIdx = pageIndices[i];

            progress?.Report(new ExportProgress
            {
                CurrentPage = i + 1,
                TotalPages = pageIndices.Length,
                Message = $"Extracting text from page {pageIdx + 1}..."
            });

            try
            {
                var text = await Task.Run(() =>
                    pdfOps.ExtractText(pdfBytes, pageIdx + 1), cancellationToken);

                var data = System.Text.Encoding.UTF8.GetBytes(text);
                results.Add(ExportResult.Ok(
                    data,
                    $"{options.BaseFileName}_page{pageIdx + 1}.txt",
                    "text/plain",
                    pageIdx + 1));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to extract text from page {Page}", pageIdx + 1);
                results.Add(ExportResult.Fail($"Page {pageIdx + 1}: {ex.Message}"));
            }
        }

        return results;
    }
}
