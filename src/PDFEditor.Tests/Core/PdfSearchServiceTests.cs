using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for PdfSearchService (full-text search and text extraction)
/// </summary>
public class PdfSearchServiceTests
{
    private readonly PdfSearchService _searchService = new();

    [Fact]
    public void Search_FindsExactMatch_ReturnsSingleResult()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("The quick brown fox jumps over the lazy dog.");
        var results = _searchService.Search(pdf, "brown fox");
        Assert.Single(results);
        Assert.Equal(1, results[0].PageNumber);
        Assert.Equal("brown fox", results[0].MatchedText);
    }

    [Fact]
    public void Search_CaseInsensitive_FindsMatch()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("Hello World");
        var results = _searchService.Search(pdf, "hello world", caseSensitive: false);
        Assert.Single(results);
    }

    [Fact]
    public void Search_CaseSensitive_NoMatch()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("Hello World");
        var results = _searchService.Search(pdf, "hello world", caseSensitive: true);
        Assert.Empty(results);
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var pdf = TestPdfGenerator.CreateMinimalPdf();
        var results = _searchService.Search(pdf, "");
        Assert.Empty(results);
    }

    [Fact]
    public void Search_MultiplePages_FindsOnCorrectPage()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("Page one content", "Page two target text", "Page three content");
        var results = _searchService.Search(pdf, "target text");
        Assert.Single(results);
        Assert.Equal(2, results[0].PageNumber);
    }

    [Fact]
    public void Search_MultipleOccurrences_FindsAll()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("test word test word test");
        var results = _searchService.Search(pdf, "test");
        Assert.True(results.Count >= 3);
    }

    [Fact]
    public void CountOccurrences_ReturnsCorrectCount()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("abc def abc ghi abc");
        var count = _searchService.CountOccurrences(pdf, "abc");
        Assert.Equal(3, count);
    }

    [Fact]
    public void ExtractAllText_ReturnsPageText()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("First page text", "Second page text");
        var text = _searchService.ExtractAllText(pdf);
        Assert.Contains("First page text", text);
        Assert.Contains("Second page text", text);
    }

    [Fact]
    public void Search_ContextSnippets_ArePopulated()
    {
        var pdf = TestPdfGenerator.CreatePdfWithContent("Before marker word after text here.");
        var results = _searchService.Search(pdf, "marker");
        Assert.Single(results);
        Assert.NotEmpty(results[0].DisplayText);
    }
}
