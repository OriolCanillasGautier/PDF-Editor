namespace PDFEditor.Core.Abstractions;

/// <summary>
/// Defines a provider that can export PDF content to a specific format.
/// Implement this interface to add new export formats to the application.
/// </summary>
public interface IExportProvider
{
    /// <summary>
    /// Human-readable format name (e.g., "PNG Image", "Microsoft Word (DOCX)")
    /// </summary>
    string FormatName { get; }

    /// <summary>
    /// File extensions supported by this provider (e.g., ".png", ".docx")
    /// </summary>
    string[] SupportedExtensions { get; }

    /// <summary>
    /// Whether this provider supports exporting multiple pages in a single operation
    /// </summary>
    bool SupportsBatch { get; }

    /// <summary>
    /// Whether this provider supports page-by-page export (one file per page)
    /// </summary>
    bool SupportsPerPageExport { get; }

    /// <summary>
    /// Exports the PDF content according to the given options.
    /// </summary>
    /// <param name="pdfBytes">The source PDF as a byte array</param>
    /// <param name="options">Export options (format, DPI, page range, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Export result with data and metadata</returns>
    Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports pages individually, returning one result per page.
    /// Only valid when <see cref="SupportsPerPageExport"/> is true.
    /// </summary>
    Task<List<ExportResult>> ExportPagesAsync(byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an export operation
/// </summary>
public class ExportResult
{
    /// <summary>
    /// The exported data as bytes
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Suggested file name for the export
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// MIME type of the exported content
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// Whether the export was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the export failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Page number (1-based) if this is a per-page export
    /// </summary>
    public int? PageNumber { get; set; }

    /// <summary>
    /// Creates a successful result
    /// </summary>
    public static ExportResult Ok(byte[] data, string fileName, string mimeType, int? pageNumber = null) =>
        new() { Data = data, FileName = fileName, MimeType = mimeType, Success = true, PageNumber = pageNumber };

    /// <summary>
    /// Creates a failed result
    /// </summary>
    public static ExportResult Fail(string error) =>
        new() { Success = false, ErrorMessage = error };
}

/// <summary>
/// Options for controlling export behavior
/// </summary>
public class ExportOptions
{
    /// <summary>
    /// Resolution in DPI for rasterized exports (images, HTML)
    /// </summary>
    public int Dpi { get; set; } = 150;

    /// <summary>
    /// Quality percentage (1-100) for lossy formats like JPEG
    /// </summary>
    public int Quality { get; set; } = 90;

    /// <summary>
    /// Specific page indices (0-based) to export. If null/empty, all pages are exported.
    /// </summary>
    public int[]? PageIndices { get; set; }

    /// <summary>
    /// The desired output format (e.g., "PNG", "JPEG", "DOCX")
    /// </summary>
    public string OutputFormat { get; set; } = string.Empty;

    /// <summary>
    /// Base file name (without extension) for naming exported files
    /// </summary>
    public string BaseFileName { get; set; } = "export";

    /// <summary>
    /// When true, use high-fidelity export engine (e.g., pdf2docx Python backend) if available.
    /// </summary>
    public bool UseHighFidelityEngine { get; set; } = false;
}

/// <summary>
/// Progress information during export operations
/// </summary>
public class ExportProgress
{
    /// <summary>
    /// Current page being processed (1-based)
    /// </summary>
    public int CurrentPage { get; set; }

    /// <summary>
    /// Total number of pages to process
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    public double ProgressPercent => TotalPages > 0 ? (double)CurrentPage / TotalPages * 100 : 0;

    /// <summary>
    /// Status message
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
