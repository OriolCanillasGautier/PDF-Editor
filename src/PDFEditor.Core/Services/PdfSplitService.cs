using iText.Kernel.Pdf;

namespace PDFEditor.Core.Services;

/// <summary>
/// Split, extract, and rearrange pages within PDF documents
/// </summary>
public class PdfSplitService
{
    /// <summary>
    /// Extracts a range of pages into a new PDF (1-based inclusive)
    /// </summary>
    public byte[] ExtractPages(byte[] pdfBytes, int startPage, int endPage)
    {
        var outputMs = new MemoryStream();
        using (var reader = new PdfReader(new MemoryStream(pdfBytes)))
        {
            var srcDoc = new PdfDocument(reader);
            var writer = new PdfWriter(outputMs);
            var destDoc = new PdfDocument(writer);
            srcDoc.CopyPagesTo(startPage, endPage, destDoc);
            srcDoc.Close();
            destDoc.Close();
        }
        return outputMs.ToArray();
    }

    /// <summary>
    /// Extracts specific pages (1-based) into a new PDF
    /// </summary>
    public byte[] ExtractSpecificPages(byte[] pdfBytes, int[] pageNumbers)
    {
        var outputMs = new MemoryStream();
        using (var reader = new PdfReader(new MemoryStream(pdfBytes)))
        {
            var srcDoc = new PdfDocument(reader);
            var writer = new PdfWriter(outputMs);
            var destDoc = new PdfDocument(writer);

            var pagesList = new iText.Kernel.Utils.PdfSplitter(srcDoc);
            foreach (var pageNum in pageNumbers.OrderBy(x => x))
            {
                srcDoc.CopyPagesTo(pageNum, pageNum, destDoc);
            }

            srcDoc.Close();
            destDoc.Close();
        }
        return outputMs.ToArray();
    }

    /// <summary>
    /// Splits a PDF into individual single-page PDFs
    /// </summary>
    public List<byte[]> SplitAll(byte[] pdfBytes)
    {
        var result = new List<byte[]>();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        var srcDoc = new PdfDocument(reader);

        for (int i = 1; i <= srcDoc.GetNumberOfPages(); i++)
        {
            var ms = new MemoryStream();
            var writer = new PdfWriter(ms);
            var destDoc = new PdfDocument(writer);
            srcDoc.CopyPagesTo(i, i, destDoc);
            destDoc.Close();
            result.Add(ms.ToArray());
        }

        srcDoc.Close();
        return result;
    }

    /// <summary>
    /// Reorders pages in a PDF according to the given order (1-based page numbers)
    /// </summary>
    public byte[] ReorderPages(byte[] pdfBytes, int[] newOrder)
    {
        var outputMs = new MemoryStream();
        using (var reader = new PdfReader(new MemoryStream(pdfBytes)))
        {
            var srcDoc = new PdfDocument(reader);
            var writer = new PdfWriter(outputMs);
            var destDoc = new PdfDocument(writer);

            foreach (var pageNum in newOrder)
            {
                srcDoc.CopyPagesTo(pageNum, pageNum, destDoc);
            }

            srcDoc.Close();
            destDoc.Close();
        }
        return outputMs.ToArray();
    }

    /// <summary>
    /// Moves a page from one position to another (0-based indices)
    /// </summary>
    public byte[] MovePage(byte[] pdfBytes, int fromIndex, int toIndex)
    {
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        var doc = new PdfDocument(reader);
        int pageCount = doc.GetNumberOfPages();
        doc.Close();

        if (fromIndex < 0 || fromIndex >= pageCount || toIndex < 0 || toIndex >= pageCount)
            return pdfBytes;

        var order = Enumerable.Range(1, pageCount).ToList();
        int page = order[fromIndex];
        order.RemoveAt(fromIndex);
        order.Insert(toIndex, page);

        return ReorderPages(pdfBytes, order.ToArray());
    }

    /// <summary>
    /// Inserts pages from another PDF at a specific position (0-based insert index)
    /// </summary>
    public byte[] InsertPages(byte[] targetPdf, byte[] sourcePdf, int insertAtIndex)
    {
        var outputMs = new MemoryStream();
        using var targetReader = new PdfReader(new MemoryStream(targetPdf));
        using var sourceReader = new PdfReader(new MemoryStream(sourcePdf));

        var writer = new PdfWriter(outputMs);
        var destDoc = new PdfDocument(writer);
        var targetDoc = new PdfDocument(targetReader);
        var sourceDoc = new PdfDocument(sourceReader);

        // Copy pages before insertion point
        if (insertAtIndex > 0)
            targetDoc.CopyPagesTo(1, Math.Min(insertAtIndex, targetDoc.GetNumberOfPages()), destDoc);

        // Copy all source pages
        sourceDoc.CopyPagesTo(1, sourceDoc.GetNumberOfPages(), destDoc);

        // Copy remaining pages after insertion point
        if (insertAtIndex < targetDoc.GetNumberOfPages())
            targetDoc.CopyPagesTo(insertAtIndex + 1, targetDoc.GetNumberOfPages(), destDoc);

        targetDoc.Close();
        sourceDoc.Close();
        destDoc.Close();
        return outputMs.ToArray();
    }
}
