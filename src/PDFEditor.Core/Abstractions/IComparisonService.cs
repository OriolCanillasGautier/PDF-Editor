namespace PDFEditor.Core.Abstractions;

/// <summary>
/// Represents a difference between two documents
/// </summary>
public class DocumentDifference
{
    /// <summary>Type of difference</summary>
    public DifferenceType Type { get; set; }

    /// <summary>Page number (1-based) in the first/left document</summary>
    public int LeftPageNumber { get; set; }

    /// <summary>Page number (1-based) in the second/right document</summary>
    public int RightPageNumber { get; set; }

    /// <summary>Text content from the left document</summary>
    public string LeftText { get; set; } = string.Empty;

    /// <summary>Text content from the right document</summary>
    public string RightText { get; set; } = string.Empty;

    /// <summary>Line number within the page text</summary>
    public int LineNumber { get; set; }

    /// <summary>Human-readable description of the difference</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Types of differences between documents
/// </summary>
public enum DifferenceType
{
    /// <summary>Text was added in the right document</summary>
    Added,

    /// <summary>Text was removed from the left document</summary>
    Removed,

    /// <summary>Text was modified between documents</summary>
    Modified,

    /// <summary>Page was added in the right document</summary>
    PageAdded,

    /// <summary>Page was removed from the left document</summary>
    PageRemoved,

    /// <summary>Metadata changed</summary>
    MetadataChanged
}

/// <summary>
/// Summary of comparison results
/// </summary>
public class ComparisonResult
{
    public List<DocumentDifference> Differences { get; set; } = new();
    public int TotalDifferences => Differences.Count;
    public int PagesInLeft { get; set; }
    public int PagesInRight { get; set; }
    public int AddedCount { get; set; }
    public int RemovedCount { get; set; }
    public int ModifiedCount { get; set; }
    public bool AreIdentical => TotalDifferences == 0;
    public string LeftFileName { get; set; } = string.Empty;
    public string RightFileName { get; set; } = string.Empty;
}

/// <summary>
/// Service for comparing two PDF documents and identifying differences
/// </summary>
public interface IComparisonService
{
    /// <summary>
    /// Compares two PDF documents and returns a detailed list of differences
    /// </summary>
    ComparisonResult Compare(byte[] leftPdfBytes, byte[] rightPdfBytes,
        string leftFileName = "Document A", string rightFileName = "Document B");

    /// <summary>
    /// Generates a text summary report of the differences
    /// </summary>
    string GenerateReport(ComparisonResult result);

    /// <summary>
    /// Generates an HTML report of the differences with color-coded changes
    /// </summary>
    string GenerateHtmlReport(ComparisonResult result);

    /// <summary>
    /// Quick check if two documents are text-identical
    /// </summary>
    bool AreIdentical(byte[] leftPdfBytes, byte[] rightPdfBytes);
}
