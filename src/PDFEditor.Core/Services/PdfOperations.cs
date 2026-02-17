using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace PDFEditor.Core.Services;

/// <summary>
/// PDF manipulation operations using iText7 library.
/// All operations work with in-memory byte arrays for safe editing workflows.
/// </summary>
public class PdfOperations
{
    /// <summary>
    /// Deletes specified pages from a PDF (1-based page numbers)
    /// </summary>
    public byte[] DeletePages(byte[] pdfBytes, int[] pageNumbers)
    {
        var outputMs = new MemoryStream();
        using (var reader = new PdfReader(new MemoryStream(pdfBytes)))
        using (var writer = new PdfWriter(outputMs))
        {
            var doc = new PdfDocument(reader, writer);
            foreach (var page in pageNumbers.OrderByDescending(x => x))
            {
                doc.RemovePage(page);
            }
            doc.Close();
        }
        return outputMs.ToArray();
    }

    /// <summary>
    /// Rotates specified pages by the given degrees (1-based page numbers)
    /// </summary>
    public byte[] RotatePages(byte[] pdfBytes, int[] pageNumbers, int degrees)
    {
        var outputMs = new MemoryStream();
        using (var reader = new PdfReader(new MemoryStream(pdfBytes)))
        using (var writer = new PdfWriter(outputMs))
        {
            var doc = new PdfDocument(reader, writer);
            foreach (var pageNum in pageNumbers)
            {
                var page = doc.GetPage(pageNum);
                int currentRotation = page.GetRotation();
                page.SetRotation((currentRotation + degrees) % 360);
            }
            doc.Close();
        }
        return outputMs.ToArray();
    }

    /// <summary>
    /// Merges two PDF documents into one
    /// </summary>
    public byte[] MergeDocuments(byte[] pdfBytes1, byte[] pdfBytes2)
    {
        var outputMs = new MemoryStream();
        var writer = new PdfWriter(outputMs);
        var destDoc = new PdfDocument(writer);

        using (var reader1 = new PdfReader(new MemoryStream(pdfBytes1)))
        {
            var srcDoc1 = new PdfDocument(reader1);
            srcDoc1.CopyPagesTo(1, srcDoc1.GetNumberOfPages(), destDoc);
            srcDoc1.Close();
        }

        using (var reader2 = new PdfReader(new MemoryStream(pdfBytes2)))
        {
            var srcDoc2 = new PdfDocument(reader2);
            srcDoc2.CopyPagesTo(1, srcDoc2.GetNumberOfPages(), destDoc);
            srcDoc2.Close();
        }

        destDoc.Close();
        return outputMs.ToArray();
    }

    /// <summary>
    /// Extracts text from a specific page (1-based page number)
    /// </summary>
    public string ExtractText(byte[] pdfBytes, int pageNumber)
    {
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        var doc = new PdfDocument(reader);
        var page = doc.GetPage(pageNumber);
        var strategy = new SimpleTextExtractionStrategy();
        var text = PdfTextExtractor.GetTextFromPage(page, strategy);
        doc.Close();
        return text;
    }

    /// <summary>
    /// Gets document metadata (title, author, subject)
    /// </summary>
    public (string? title, string? author, string? subject) GetMetadata(byte[] pdfBytes)
    {
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        var doc = new PdfDocument(reader);
        var info = doc.GetDocumentInfo();
        var result = (info.GetTitle(), info.GetAuthor(), info.GetSubject());
        doc.Close();
        return result;
    }

    /// <summary>
    /// Sets document metadata and returns the modified PDF bytes
    /// </summary>
    public byte[] SetMetadata(byte[] pdfBytes, string? title, string? author, string? subject)
    {
        var outputMs = new MemoryStream();
        using (var reader = new PdfReader(new MemoryStream(pdfBytes)))
        using (var writer = new PdfWriter(outputMs))
        {
            var doc = new PdfDocument(reader, writer);
            var info = doc.GetDocumentInfo();
            if (title != null) info.SetTitle(title);
            if (author != null) info.SetAuthor(author);
            if (subject != null) info.SetSubject(subject);
            doc.Close();
        }
        return outputMs.ToArray();
    }

    /// <summary>
    /// Gets page count from PDF bytes
    /// </summary>
    public int GetPageCount(byte[] pdfBytes)
    {
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        var doc = new PdfDocument(reader);
        var count = doc.GetNumberOfPages();
        doc.Close();
        return count;
    }
}
