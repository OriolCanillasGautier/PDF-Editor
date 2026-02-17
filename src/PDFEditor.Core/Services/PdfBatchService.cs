using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Result of a single batch operation
/// </summary>
public class BatchOperationResult
{
    public string FilePath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string OperationType { get; set; } = string.Empty;
}

/// <summary>
/// Configuration for a batch operation
/// </summary>
public class BatchOperationConfig
{
    public string OperationType { get; set; } = string.Empty;
    public List<string> InputFiles { get; set; } = new();
    public string OutputFolder { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = "pdf";

    // Operation-specific parameters
    public int RotationDegrees { get; set; }
    public string? WatermarkText { get; set; }
    public float WatermarkFontSize { get; set; } = 40f;
    public float WatermarkOpacity { get; set; } = 0.3f;
    public string? PageNumberFormat { get; set; }
    public string? OwnerPassword { get; set; }
    public string? UserPassword { get; set; }
    public int ExportDpi { get; set; } = 150;
    public string ExportImageFormat { get; set; } = "PNG";
}

/// <summary>
/// Service for batch processing multiple PDF files
/// </summary>
public class PdfBatchService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly PdfOperations _pdfOps = new();
    private readonly PdfWatermarkService _watermarkService = new();
    private readonly PdfSecurityService _securityService = new();
    private readonly PdfExportService _exportService = new();
    private readonly PdfSplitService _splitService = new();

    /// <summary>
    /// Processes a batch of files with the given configuration.
    /// Reports progress via the callback (fileIndex, totalFiles, currentFileName).
    /// </summary>
    public List<BatchOperationResult> ProcessBatch(
        BatchOperationConfig config,
        Action<int, int, string>? progressCallback = null)
    {
        var results = new List<BatchOperationResult>();
        var total = config.InputFiles.Count;

        for (int i = 0; i < total; i++)
        {
            var inputFile = config.InputFiles[i];
            var fileName = Path.GetFileName(inputFile);
            progressCallback?.Invoke(i, total, fileName);

            var result = new BatchOperationResult
            {
                FilePath = inputFile,
                OperationType = config.OperationType
            };

            try
            {
                var pdfBytes = File.ReadAllBytes(inputFile);
                var baseName = Path.GetFileNameWithoutExtension(inputFile);

                switch (config.OperationType.ToLowerInvariant())
                {
                    case "rotate":
                        var pageCount = _pdfOps.GetPageCount(pdfBytes);
                        var allPages = Enumerable.Range(1, pageCount).ToArray();
                        var rotated = _pdfOps.RotatePages(pdfBytes, allPages, config.RotationDegrees);
                        result.OutputPath = Path.Combine(config.OutputFolder, $"{baseName}_rotated.pdf");
                        File.WriteAllBytes(result.OutputPath, rotated);
                        result.Success = true;
                        break;

                    case "watermark":
                        var watermarked = _watermarkService.AddTextWatermark(
                            pdfBytes,
                            config.WatermarkText ?? "WATERMARK",
                            config.WatermarkFontSize,
                            config.WatermarkOpacity);
                        result.OutputPath = Path.Combine(config.OutputFolder, $"{baseName}_watermarked.pdf");
                        File.WriteAllBytes(result.OutputPath, watermarked);
                        result.Success = true;
                        break;

                    case "pagenumbers":
                        var numbered = _watermarkService.AddPageNumbers(
                            pdfBytes,
                            config.PageNumberFormat ?? "Page {0} of {1}");
                        result.OutputPath = Path.Combine(config.OutputFolder, $"{baseName}_numbered.pdf");
                        File.WriteAllBytes(result.OutputPath, numbered);
                        result.Success = true;
                        break;

                    case "encrypt":
                        if (string.IsNullOrWhiteSpace(config.OwnerPassword))
                        {
                            result.ErrorMessage = "Owner password required";
                            break;
                        }
                        var encrypted = _securityService.Encrypt(
                            pdfBytes, config.UserPassword, config.OwnerPassword,
                            allowPrinting: true, allowCopying: false, allowEditing: false);
                        result.OutputPath = Path.Combine(config.OutputFolder, $"{baseName}_encrypted.pdf");
                        File.WriteAllBytes(result.OutputPath, encrypted);
                        result.Success = true;
                        break;

                    case "export_images":
                        var imgPageCount = _pdfOps.GetPageCount(pdfBytes);
                        var subFolder = Path.Combine(config.OutputFolder, baseName);
                        Directory.CreateDirectory(subFolder);
                        for (int p = 0; p < imgPageCount; p++)
                        {
                            var img = _exportService.ExportPageToImage(pdfBytes, p, config.ExportImageFormat, config.ExportDpi);
                            var ext = config.ExportImageFormat.ToLowerInvariant();
                            File.WriteAllBytes(Path.Combine(subFolder, $"page_{p + 1}.{ext}"), img);
                        }
                        result.OutputPath = subFolder;
                        result.Success = true;
                        break;

                    case "merge":
                        // For merge, all files get merged into one output
                        // Handled separately — this case just marks files as processed
                        result.Success = true;
                        break;

                    case "split":
                        var pages = _splitService.SplitAll(pdfBytes);
                        var splitFolder = Path.Combine(config.OutputFolder, baseName);
                        Directory.CreateDirectory(splitFolder);
                        for (int p = 0; p < pages.Count; p++)
                        {
                            File.WriteAllBytes(
                                Path.Combine(splitFolder, $"{baseName}_page{p + 1}.pdf"),
                                pages[p]);
                        }
                        result.OutputPath = splitFolder;
                        result.Success = true;
                        break;

                    default:
                        result.ErrorMessage = $"Unknown operation: {config.OperationType}";
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Batch operation failed for {File}", inputFile);
                result.ErrorMessage = ex.Message;
            }

            results.Add(result);
        }

        // Special handling for merge: combine all input files into one
        if (config.OperationType.Equals("merge", StringComparison.OrdinalIgnoreCase) && config.InputFiles.Count > 1)
        {
            try
            {
                var merged = File.ReadAllBytes(config.InputFiles[0]);
                for (int i = 1; i < config.InputFiles.Count; i++)
                {
                    var next = File.ReadAllBytes(config.InputFiles[i]);
                    merged = _pdfOps.MergeDocuments(merged, next);
                }
                var outPath = Path.Combine(config.OutputFolder, "merged_output.pdf");
                File.WriteAllBytes(outPath, merged);
                results.Add(new BatchOperationResult
                {
                    FilePath = "Multiple files",
                    OutputPath = outPath,
                    OperationType = "merge",
                    Success = true
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Batch merge failed");
                results.Add(new BatchOperationResult
                {
                    FilePath = "Multiple files",
                    OperationType = "merge",
                    ErrorMessage = ex.Message
                });
            }
        }

        progressCallback?.Invoke(total, total, "Complete");
        return results;
    }
}
