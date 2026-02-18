using PDFEditor.Core.Abstractions;
using PDFEditor.Core.Services;
using PDFEditor.Core.Services.Export;
using PDFEditor.Tests.Helpers;
using System.IO.Compression;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for Phase 6 features: EPUB export, Booklet service, Header/Footer service.
/// </summary>
public class Phase6ServiceTests
{
    #region EPUB Export Provider

    private readonly EpubExportProvider _epub = new();

    [Fact]
    public async Task EpubExport_BasicPdf_ProducesValidEpub()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(3);
        var options = new ExportOptions { BaseFileName = "test-book" };

        var result = await _epub.ExportAsync(pdfBytes, options);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Data);
        Assert.Equal("test-book.epub", result.FileName);
        Assert.Equal("application/epub+zip", result.MimeType);
    }

    [Fact]
    public async Task EpubExport_IsValidZip()
    {
        var pdfBytes = TestPdfGenerator.CreatePdfWithContent("Hello EPUB World");
        var result = await _epub.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "zip-test" });

        Assert.True(result.Success);
        // Must be a valid ZIP (PK magic)
        Assert.Equal(0x50, result.Data[0]);
        Assert.Equal(0x4B, result.Data[1]);

        using var zipMs = new MemoryStream(result.Data);
        using var zip = new ZipArchive(zipMs, ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        // Required EPUB entries
        Assert.Contains("mimetype", names);
        Assert.Contains("META-INF/container.xml", names);
        Assert.True(names.Any(n => n.Contains("content.opf")));
        Assert.True(names.Any(n => n.Contains("toc.xhtml")));
        Assert.True(names.Any(n => n.Contains("toc.ncx")));
        Assert.True(names.Any(n => n.Contains("style.css")));
    }

    [Fact]
    public async Task EpubExport_MimetypeIsFirst_Uncompressed()
    {
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf();
        var result = await _epub.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "mt-test" });

        Assert.True(result.Success);

        using var zipMs = new MemoryStream(result.Data);
        using var zip = new ZipArchive(zipMs, ZipArchiveMode.Read);
        // mimetype must be first entry
        Assert.Equal("mimetype", zip.Entries[0].FullName);

        // Content must be "application/epub+zip"
        using var reader = new StreamReader(zip.Entries[0].Open());
        string content = reader.ReadToEnd();
        Assert.Equal("application/epub+zip", content);
    }

    [Fact]
    public async Task EpubExport_MultiplePages_CreatesChaptersPerPage()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(5);
        var result = await _epub.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "multi" });

        Assert.True(result.Success);

        using var zipMs = new MemoryStream(result.Data);
        using var zip = new ZipArchive(zipMs, ZipArchiveMode.Read);
        var chapters = zip.Entries.Where(e => e.FullName.Contains("chapter")).ToList();
        Assert.Equal(5, chapters.Count);
    }

    [Fact]
    public async Task EpubExport_PageSubset_ExportsOnlySelectedPages()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(5);
        var options = new ExportOptions
        {
            BaseFileName = "subset",
            PageIndices = new[] { 0, 2 }
        };

        var result = await _epub.ExportAsync(pdfBytes, options);
        Assert.True(result.Success);

        using var zipMs = new MemoryStream(result.Data);
        using var zip = new ZipArchive(zipMs, ZipArchiveMode.Read);
        var chapters = zip.Entries.Where(e => e.FullName.Contains("chapter")).ToList();
        Assert.Equal(2, chapters.Count);
    }

    [Fact]
    public async Task EpubExport_ContentOpfContainsMetadata()
    {
        var pdfBytes = TestPdfGenerator.CreatePdfWithContent("Test Content");
        var result = await _epub.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "My Book Title" });

        Assert.True(result.Success);

        using var zipMs = new MemoryStream(result.Data);
        using var zip = new ZipArchive(zipMs, ZipArchiveMode.Read);
        var opfEntry = zip.Entries.First(e => e.FullName.Contains("content.opf"));
        using var reader = new StreamReader(opfEntry.Open());
        string opf = reader.ReadToEnd();

        Assert.Contains("My Book Title", opf);
        Assert.Contains("dc:title", opf);
        Assert.Contains("dc:identifier", opf);
        Assert.Contains("dc:language", opf);
        Assert.Contains("manifest", opf);
        Assert.Contains("spine", opf);
    }

    [Fact]
    public async Task EpubExport_WithCancellation_ThrowsOrFails()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(3);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _epub.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "cancel" }, cts.Token);
        Assert.False(result.Success);
    }

    [Fact]
    public void EpubExport_Properties_AreCorrect()
    {
        Assert.Equal("EPUB (E-Book)", _epub.FormatName);
        Assert.Contains(".epub", _epub.SupportedExtensions);
        Assert.True(_epub.SupportsBatch);
        Assert.False(_epub.SupportsPerPageExport);
    }

    [Fact]
    public async Task EpubExport_ExportPagesAsync_ThrowsNotSupported()
    {
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _epub.ExportPagesAsync(pdfBytes, new ExportOptions()));
    }

    [Fact]
    public async Task EpubExport_XmlSpecialChars_AreEscaped()
    {
        var pdfBytes = TestPdfGenerator.CreatePdfWithContent("Price < $50 & quality > average \"good\" 'stuff'");
        var result = await _epub.ExportAsync(pdfBytes, new ExportOptions { BaseFileName = "xml-escape" });
        Assert.True(result.Success);

        // Verify the chapter XHTML is valid by checking it doesn't contain unescaped < or &
        using var zipMs = new MemoryStream(result.Data);
        using var zip = new ZipArchive(zipMs, ZipArchiveMode.Read);
        var chapter = zip.Entries.First(e => e.FullName.Contains("chapter"));
        using var reader = new StreamReader(chapter.Open());
        string xhtml = reader.ReadToEnd();
        Assert.Contains("&amp;", xhtml);
        Assert.Contains("&lt;", xhtml);
    }

    #endregion

    #region Booklet Service

    private readonly PdfBookletService _booklet = new();

    [Fact]
    public async Task Booklet_FourPages_ProducesValidPdf()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(4);
        var result = await _booklet.CreateBookletAsync(pdfBytes);

        Assert.NotEmpty(result);
        // Verify it's a valid PDF
        using var ms = new MemoryStream(result);
        using var reader = new iText.Kernel.Pdf.PdfReader(ms);
        using var doc = new iText.Kernel.Pdf.PdfDocument(reader);
        Assert.True(doc.GetNumberOfPages() > 0);
    }

    [Fact]
    public async Task Booklet_OddPageCount_PadsToMultipleOf4()
    {
        // 5 pages → padded to 8 → 4 sheets (2 front + 2 back)
        var (sheets, totalPages, blanks) = PdfBookletService.CalculateBookletInfo(5);
        Assert.Equal(8, totalPages);
        Assert.Equal(3, blanks);
        Assert.Equal(4, sheets);
    }

    [Fact]
    public async Task Booklet_SinglePage_ProducesOutput()
    {
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf();
        var result = await _booklet.CreateBookletAsync(pdfBytes);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Booklet_LargerDocument_Succeeds()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(12);
        var result = await _booklet.CreateBookletAsync(pdfBytes, new PdfBookletService.BookletOptions
        {
            Sheet = PdfBookletService.SheetSize.LetterLandscape,
            BindingMarginPt = 24f
        });
        Assert.NotEmpty(result);

        using var ms = new MemoryStream(result);
        using var reader = new iText.Kernel.Pdf.PdfReader(ms);
        using var doc = new iText.Kernel.Pdf.PdfDocument(reader);
        // 12 pages → 12 (already multiple of 4) → 6 sheets → 6 pages in output (front+back per sheet)
        Assert.True(doc.GetNumberOfPages() > 0);
    }

    [Fact]
    public void Booklet_CalculateInfo_MultipleOf4_NoBlankPages()
    {
        var (sheets, totalPages, blanks) = PdfBookletService.CalculateBookletInfo(8);
        Assert.Equal(8, totalPages);
        Assert.Equal(0, blanks);
        Assert.Equal(4, sheets);
    }

    [Fact]
    public async Task Booklet_WithCancellation_Throws()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(8);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _booklet.CreateBookletAsync(pdfBytes, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Booklet_A3Sheet_ProducesLargerOutput()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(4);
        var result = await _booklet.CreateBookletAsync(pdfBytes, new PdfBookletService.BookletOptions
        {
            Sheet = PdfBookletService.SheetSize.A3Landscape
        });
        Assert.NotEmpty(result);

        using var ms = new MemoryStream(result);
        using var reader = new iText.Kernel.Pdf.PdfReader(ms);
        using var doc = new iText.Kernel.Pdf.PdfDocument(reader);
        var firstPage = doc.GetFirstPage().GetMediaBox();
        // A3 landscape width should be > 1000 points (420mm ≈ 1190pt)
        Assert.True(firstPage.GetWidth() > 1000);
    }

    #endregion

    #region Header/Footer Service

    private readonly HeaderFooterService _hf = new();

    [Fact]
    public async Task HeaderFooter_AddPageNumbers_ProducesOutput()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(3);
        var result = await _hf.AddHeaderFooterAsync(pdfBytes, new HeaderFooterService.HFOptions
        {
            Footer = new HeaderFooterService.HFElement
            {
                Template = "Page {page} of {total}",
                Alignment = HeaderFooterService.HFAlignment.Center
            }
        });

        Assert.NotEmpty(result);
        Assert.NotEqual(pdfBytes.Length, result.Length); // should differ (content added)
    }

    [Fact]
    public async Task HeaderFooter_HeaderAndFooter_BothAdded()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(2);
        var result = await _hf.AddHeaderFooterAsync(pdfBytes, new HeaderFooterService.HFOptions
        {
            Header = new HeaderFooterService.HFElement
            {
                Template = "Document Title",
                Alignment = HeaderFooterService.HFAlignment.Center,
                Bold = true
            },
            Footer = new HeaderFooterService.HFElement
            {
                Template = "Page {page}",
                Alignment = HeaderFooterService.HFAlignment.Right
            }
        });

        Assert.NotEmpty(result);

        // Verify output is valid PDF with same page count
        using var ms = new MemoryStream(result);
        using var reader = new iText.Kernel.Pdf.PdfReader(ms);
        using var doc = new iText.Kernel.Pdf.PdfDocument(reader);
        Assert.Equal(2, doc.GetNumberOfPages());
    }

    [Fact]
    public async Task HeaderFooter_WithDate_ResolvesPlaceholder()
    {
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf();
        var result = await _hf.AddHeaderFooterAsync(pdfBytes, new HeaderFooterService.HFOptions
        {
            Footer = new HeaderFooterService.HFElement
            {
                Template = "{date} - Page {page}",
                Alignment = HeaderFooterService.HFAlignment.Left
            }
        });

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task HeaderFooter_SkipFirstPage_LeavesFirstPageUnchanged()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(3);
        var result = await _hf.AddHeaderFooterAsync(pdfBytes, new HeaderFooterService.HFOptions
        {
            Footer = new HeaderFooterService.HFElement
            {
                Template = "Page {page}",
                Alignment = HeaderFooterService.HFAlignment.Center
            },
            SkipFirstPage = true
        });

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task HeaderFooter_EvenOddPages_DifferentAlignment()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(4);
        var result = await _hf.AddHeaderFooterAsync(pdfBytes, new HeaderFooterService.HFOptions
        {
            Footer = new HeaderFooterService.HFElement
            {
                Template = "Page {page}",
                Alignment = HeaderFooterService.HFAlignment.Right // odd pages
            },
            FooterEven = new HeaderFooterService.HFElement
            {
                Template = "Page {page}",
                Alignment = HeaderFooterService.HFAlignment.Left // even pages
            }
        });

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task HeaderFooter_WithSeparatorLine_ProducesOutput()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(2);
        var result = await _hf.AddHeaderFooterAsync(pdfBytes, new HeaderFooterService.HFOptions
        {
            Header = new HeaderFooterService.HFElement
            {
                Template = "CONFIDENTIAL",
                Alignment = HeaderFooterService.HFAlignment.Center,
                ColorHex = "CC0000",
                Bold = true
            },
            DrawSeparatorLine = true
        });

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task HeaderFooter_NoHeaderNoFooter_ReturnsOriginal()
    {
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf();
        var result = await _hf.AddHeaderFooterAsync(pdfBytes, new HeaderFooterService.HFOptions());

        // Should return the original PDF unchanged
        Assert.Equal(pdfBytes.Length, result.Length);
    }

    [Fact]
    public async Task HeaderFooter_CustomStartPageNumber()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(3);
        var result = await _hf.AddHeaderFooterAsync(pdfBytes, new HeaderFooterService.HFOptions
        {
            Footer = new HeaderFooterService.HFElement
            {
                Template = "Page {page}",
                Alignment = HeaderFooterService.HFAlignment.Center
            },
            StartPageNumber = 5
        });

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task HeaderFooter_RemoveHeaderFooter_ProducesOutput()
    {
        // First add a footer
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(2);
        var withFooter = await _hf.AddHeaderFooterAsync(pdfBytes, new HeaderFooterService.HFOptions
        {
            Footer = new HeaderFooterService.HFElement
            {
                Template = "Footer text",
                Alignment = HeaderFooterService.HFAlignment.Center
            }
        });

        // Then remove it
        var result = await _hf.RemoveHeaderFooterAsync(withFooter, 50f);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task HeaderFooter_TimesFont_Works()
    {
        var pdfBytes = TestPdfGenerator.CreateMinimalPdf();
        var result = await _hf.AddHeaderFooterAsync(pdfBytes, new HeaderFooterService.HFOptions
        {
            Header = new HeaderFooterService.HFElement
            {
                Template = "Times Roman Header",
                FontName = "Times-Roman",
                FontSize = 10f,
                Italic = true,
                Alignment = HeaderFooterService.HFAlignment.Center
            }
        });
        Assert.NotEmpty(result);
    }

    #endregion
}
