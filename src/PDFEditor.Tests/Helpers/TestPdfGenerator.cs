using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;

namespace PDFEditor.Tests.Helpers;

/// <summary>
/// Helper to generate test PDF files in-memory for unit tests
/// </summary>
public static class TestPdfGenerator
{
    /// <summary>
    /// Creates a simple PDF with the given number of pages, each containing text
    /// </summary>
    public static byte[] CreateSimplePdf(int pageCount = 3, string? textPerPage = null)
    {
        var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        var pdf = new PdfDocument(writer);
        var doc = new Document(pdf);

        for (int i = 0; i < pageCount; i++)
        {
            if (i > 0) doc.Add(new AreaBreak());
            var text = textPerPage ?? $"This is page {i + 1} of the test document.\nIt contains sample text for testing.";
            doc.Add(new Paragraph(text));
        }

        doc.Close();
        return ms.ToArray();
    }

    /// <summary>
    /// Creates a PDF with specific text content on each page
    /// </summary>
    public static byte[] CreatePdfWithContent(params string[] pageContents)
    {
        var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        var pdf = new PdfDocument(writer);
        var doc = new Document(pdf);

        for (int i = 0; i < pageContents.Length; i++)
        {
            if (i > 0) doc.Add(new AreaBreak());
            doc.Add(new Paragraph(pageContents[i]));
        }

        doc.Close();
        return ms.ToArray();
    }

    /// <summary>
    /// Creates a PDF with metadata
    /// </summary>
    public static byte[] CreatePdfWithMetadata(string title, string author, string subject, int pages = 1)
    {
        var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        var pdf = new PdfDocument(writer);
        var info = pdf.GetDocumentInfo();
        info.SetTitle(title);
        info.SetAuthor(author);
        info.SetSubject(subject);
        var doc = new Document(pdf);

        for (int i = 0; i < pages; i++)
        {
            if (i > 0) doc.Add(new AreaBreak());
            doc.Add(new Paragraph($"Page {i + 1}"));
        }

        doc.Close();
        return ms.ToArray();
    }

    /// <summary>
    /// Creates a minimal single-page PDF
    /// </summary>
    public static byte[] CreateMinimalPdf()
    {
        return CreateSimplePdf(1, "Hello World");
    }
}
