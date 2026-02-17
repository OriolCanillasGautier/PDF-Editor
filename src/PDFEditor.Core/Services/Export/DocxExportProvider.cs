using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using NLog;
using PDFEditor.Core.Abstractions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF content to Microsoft Word DOCX format.
/// Extracts text with basic paragraph structure using iText7.
/// </summary>
public class DocxExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string FormatName => "Microsoft Word (DOCX)";
    public string[] SupportedExtensions => new[] { ".docx" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    public async Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var docxBytes = await Task.Run(() =>
                GenerateDocx(pdfBytes, options, cancellationToken), cancellationToken);

            return ExportResult.Ok(
                docxBytes,
                $"{options.BaseFileName}.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DOCX export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("DOCX export produces a single document. Use ExportAsync instead.");
    }

    private byte[] GenerateDocx(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            // Add document title
            var titlePara = new Paragraph(
                new ParagraphProperties(
                    new Justification { Val = JustificationValues.Center },
                    new SpacingBetweenLines { After = "200" }),
                new Run(
                    new RunProperties(
                        new Bold(),
                        new FontSize { Val = "36" }),
                    new Text(options.BaseFileName)));
            body.AppendChild(titlePara);

            // Extract text from PDF using iText7
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);

            int totalPages = pdfDoc.GetNumberOfPages();
            var pageIndices = options.PageIndices ?? Enumerable.Range(0, totalPages).ToArray();

            for (int i = 0; i < pageIndices.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                int pageNum = pageIndices[i] + 1; // iText uses 1-based
                if (pageNum < 1 || pageNum > totalPages) continue;

                // Add page header
                var pageHeader = new Paragraph(
                    new ParagraphProperties(
                        new SpacingBetweenLines { Before = "400", After = "100" },
                        new ParagraphBorders(
                            new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "999999" })),
                    new Run(
                        new RunProperties(
                            new Bold(),
                            new FontSize { Val = "24" },
                            new Color { Val = "333333" }),
                        new Text($"Page {pageNum}")));
                body.AppendChild(pageHeader);

                // Extract text from the page
                var page = pdfDoc.GetPage(pageNum);
                var strategy = new SimpleTextExtractionStrategy();
                var pageText = PdfTextExtractor.GetTextFromPage(page, strategy);

                if (string.IsNullOrWhiteSpace(pageText))
                {
                    var emptyPara = new Paragraph(
                        new Run(
                            new RunProperties(
                                new Italic(),
                                new Color { Val = "999999" }),
                            new Text("[No text content on this page]")));
                    body.AppendChild(emptyPara);
                    continue;
                }

                // Split text into paragraphs and add to document
                var lines = pageText.Split('\n', StringSplitOptions.None);
                foreach (var line in lines)
                {
                    var trimmed = line.TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        // Empty paragraph for blank lines
                        body.AppendChild(new Paragraph());
                        continue;
                    }

                    var para = new Paragraph(
                        new ParagraphProperties(
                            new SpacingBetweenLines { After = "60" }),
                        new Run(
                            new RunProperties(
                                new FontSize { Val = "22" }),
                            new Text(trimmed) { Space = SpaceProcessingModeValues.Preserve }));
                    body.AppendChild(para);
                }

                // Add page break between pages (except after the last)
                if (i < pageIndices.Length - 1)
                {
                    var breakPara = new Paragraph(
                        new Run(new Break { Type = BreakValues.Page }));
                    body.AppendChild(breakPara);
                }
            }

            mainPart.Document.Save();
        }

        return ms.ToArray();
    }
}
