namespace PDFEditor.Core.Abstractions;

/// <summary>
/// Represents a rectangular area on a specific page to be redacted
/// </summary>
public class RedactionArea
{
    /// <summary>Page index (0-based)</summary>
    public int PageIndex { get; set; }

    /// <summary>Left coordinate in PDF points</summary>
    public float X { get; set; }

    /// <summary>Bottom coordinate in PDF points</summary>
    public float Y { get; set; }

    /// <summary>Width in PDF points</summary>
    public float Width { get; set; }

    /// <summary>Height in PDF points</summary>
    public float Height { get; set; }

    /// <summary>Optional replacement text to show after redaction</summary>
    public string? ReplacementText { get; set; }
}

/// <summary>
/// Result of a text-based redaction search
/// </summary>
public class RedactionMatch
{
    public int PageIndex { get; set; }
    public string MatchedText { get; set; } = string.Empty;
    public int OccurrenceIndex { get; set; }
}

/// <summary>
/// Service for permanently removing content from PDF documents.
/// Unlike visual overlays, redaction removes underlying text/image data.
/// </summary>
public interface IRedactionService
{
    /// <summary>
    /// Permanently redacts (removes) content within specified rectangular areas.
    /// This removes actual text/image data, not just overlays.
    /// </summary>
    byte[] RedactAreas(byte[] pdfBytes, List<RedactionArea> areas);

    /// <summary>
    /// Finds and permanently redacts all occurrences of the specified text.
    /// </summary>
    byte[] RedactText(byte[] pdfBytes, string textToRedact, bool caseSensitive = false);

    /// <summary>
    /// Finds occurrences of text that would be redacted (preview before applying).
    /// </summary>
    List<RedactionMatch> FindRedactionTargets(byte[] pdfBytes, string textToRedact, bool caseSensitive = false);

    /// <summary>
    /// Redacts all text on specified pages (full-page redaction).
    /// </summary>
    byte[] RedactPages(byte[] pdfBytes, int[] pageIndices);
}
