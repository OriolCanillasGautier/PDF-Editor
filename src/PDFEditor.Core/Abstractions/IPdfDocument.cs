namespace PDFEditor.Core.Abstractions;

/// <summary>
/// Represents a PDF document
/// </summary>
public interface IPdfDocument
{
    string? FilePath { get; }
    int PageCount { get; }
    string? Title { get; set; }
    string? Author { get; set; }
    Dictionary<string, object> Metadata { get; }
    
    void LoadFromFile(string filePath);
    void SaveToFile(string outputPath);
    void AddPages(params IPdfPage[] pages);
    void RemovePages(params int[] pageNumbers);
    void MovePages(int[] pageNumbers, int targetPosition);
    IPdfPage? GetPage(int pageNumber);
    List<IPdfPage> GetPages(int startPage, int endPage);
    void Merge(IPdfDocument other);
    void RotatePages(int[] pageNumbers, int degrees);
}

/// <summary>
/// Represents a single page in a PDF document
/// </summary>
public interface IPdfPage
{
    int PageNumber { get; }
    double Width { get; }
    double Height { get; }
    bool HasTextLayer { get; }
    string? ExtractedText { get; }
    
    void ExtractText();
    byte[] RenderToImage(float dpi = 300f);
    void RotatePage(int degrees);
}
