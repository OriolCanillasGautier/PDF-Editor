namespace PDFEditor.Core.Services;

using PDFEditor.Core.Abstractions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// PDF document operations using iText7
/// </summary>
public class ITextPdfService : IPdfDocument
{
    private PdfDocument? _pdfDocument;
    private string? _filePath;
    private Dictionary<string, object> _metadata = new();

    public string? FilePath => _filePath;
    public int PageCount => _pdfDocument?.GetNumberOfPages() ?? 0;
    public string? Title 
    { 
        get => _metadata.ContainsKey("Title") ? _metadata["Title"].ToString() : null;
        set 
        { 
            if (value != null)
                _metadata["Title"] = value;
        }
    }
    public string? Author 
    { 
        get => _metadata.ContainsKey("Author") ? _metadata["Author"].ToString() : null;
        set 
        { 
            if (value != null)
                _metadata["Author"] = value;
        }
    }
    public Dictionary<string, object> Metadata => _metadata;

    public void LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}");

        _filePath = filePath;
        _pdfDocument = new PdfDocument(new PdfReader(filePath));
        LoadMetadata();
    }

    public void SaveToFile(string outputPath)
    {
        if (_pdfDocument == null)
            throw new InvalidOperationException("No PDF document loaded");

        using var writer = new PdfWriter(outputPath);
        _pdfDocument.CopyPagesTo(1, _pdfDocument.GetNumberOfPages(), 
            new PdfDocument(writer));
    }

    public void AddPages(params IPdfPage[] pages)
    {
        throw new NotImplementedException();
    }

    public void RemovePages(params int[] pageNumbers)
    {
        if (_pdfDocument == null)
            throw new InvalidOperationException("No PDF document loaded");

        var sortedPages = pageNumbers.OrderByDescending(x => x).ToArray();
        foreach (var pageNum in sortedPages)
        {
            _pdfDocument.RemovePage(pageNum);
        }
    }

    public void MovePages(int[] pageNumbers, int targetPosition)
    {
        throw new NotImplementedException();
    }

    public IPdfPage? GetPage(int pageNumber)
    {
        if (_pdfDocument == null)
            throw new InvalidOperationException("No PDF document loaded");

        if (pageNumber < 1 || pageNumber > PageCount)
            return null;

        return new ITextPdfPage(_pdfDocument.GetPage(pageNumber), pageNumber);
    }

    public List<IPdfPage> GetPages(int startPage, int endPage)
    {
        var pages = new List<IPdfPage>();
        for (int i = startPage; i <= endPage && i <= PageCount; i++)
        {
            var page = GetPage(i);
            if (page != null)
                pages.Add(page);
        }
        return pages;
    }

    public void Merge(IPdfDocument other)
    {
        throw new NotImplementedException();
    }

    public void RotatePages(int[] pageNumbers, int degrees)
    {
        if (_pdfDocument == null)
            throw new InvalidOperationException("No PDF document loaded");

        foreach (var pageNum in pageNumbers)
        {
            var page = _pdfDocument.GetPage(pageNum);
            page.SetRotation(degrees);
        }
    }

    private void LoadMetadata()
    {
        if (_pdfDocument?.GetDocumentInfo() is PdfDocumentInfo info)
        {
            if (info.GetTitle() != null)
                _metadata["Title"] = info.GetTitle();
            if (info.GetAuthor() != null)
                _metadata["Author"] = info.GetAuthor();
            if (info.GetSubject() != null)
                _metadata["Subject"] = info.GetSubject();
        }
    }

    public void Dispose()
    {
        _pdfDocument?.Close();
    }
}

/// <summary>
/// Wrapper for a single PDF page using iText7
/// </summary>
internal class ITextPdfPage : IPdfPage
{
    private readonly iText.Kernel.Pdf.PdfPage _page;

    public int PageNumber { get; }
    public double Width => _page.GetMediaBox().GetWidth();
    public double Height => _page.GetMediaBox().GetHeight();
    public bool HasTextLayer => !string.IsNullOrEmpty(ExtractedText);
    public string? ExtractedText { get; private set; }

    internal ITextPdfPage(iText.Kernel.Pdf.PdfPage page, int pageNumber)
    {
        _page = page;
        PageNumber = pageNumber;
    }

    public void ExtractText()
    {
        var strategy = new SimpleTextExtractionStrategy();
        ExtractedText = PdfTextExtractor.GetTextFromPage(_page, strategy);
    }

    public byte[] RenderToImage(float dpi = 300f)
    {
        throw new NotImplementedException("Use Pdfium.Net for rendering");
    }

    public void RotatePage(int degrees)
    {
        _page.SetRotation(degrees);
    }
}
