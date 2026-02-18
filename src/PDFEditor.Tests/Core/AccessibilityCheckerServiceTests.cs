using Xunit;
using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for AccessibilityCheckerService — PDF accessibility auditing.
/// </summary>
public class AccessibilityCheckerServiceTests
{
    private readonly AccessibilityCheckerService _service = new();

    [Fact]
    public void CheckAccessibility_SimplePdf_ReturnsReport()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);

        var report = _service.CheckAccessibility(pdf);

        Assert.NotNull(report);
        Assert.Equal(2, report.TotalPages);
        Assert.NotEmpty(report.Issues);
    }

    [Fact]
    public void CheckAccessibility_NoTitle_ReportsDocStructureError()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var report = _service.CheckAccessibility(pdf);

        Assert.Contains(report.Issues, i => i.RuleId == "DOC-001");
    }

    [Fact]
    public void CheckAccessibility_NoLanguage_ReportsLanguageError()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var report = _service.CheckAccessibility(pdf);

        Assert.Contains(report.Issues, i => i.RuleId == "LANG-001");
    }

    [Fact]
    public void CheckAccessibility_NotTagged_ReportsTagError()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var report = _service.CheckAccessibility(pdf);

        Assert.Contains(report.Issues, i => i.RuleId == "TAG-001");
    }

    [Fact]
    public void CheckAccessibility_MultiPageNoBookmarks_ReportsNavWarning()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(6);

        var report = _service.CheckAccessibility(pdf);

        Assert.Contains(report.Issues, i => i.RuleId == "NAV-001");
    }

    [Fact]
    public void CheckAccessibility_WithMetadata_FewerIssues()
    {
        var pdf = TestPdfGenerator.CreatePdfWithMetadata("My Title", "Author", "Subject", 2);

        var report = _service.CheckAccessibility(pdf);

        // Title issue should NOT appear
        Assert.DoesNotContain(report.Issues, i => i.RuleId == "DOC-001");
        // Author and subject issues should NOT appear
        Assert.DoesNotContain(report.Issues, i => i.RuleId == "META-001");
        Assert.DoesNotContain(report.Issues, i => i.RuleId == "META-002");
    }

    [Fact]
    public void CheckAccessibility_ErrorCountMatchesIssues()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var report = _service.CheckAccessibility(pdf);

        Assert.Equal(report.Issues.Count(i => i.Severity == AccessibilityCheckerService.Severity.Error),
            report.ErrorCount);
        Assert.Equal(report.Issues.Count(i => i.Severity == AccessibilityCheckerService.Severity.Warning),
            report.WarningCount);
        Assert.Equal(report.Issues.Count(i => i.Severity == AccessibilityCheckerService.Severity.Info),
            report.InfoCount);
    }

    [Fact]
    public void CheckAccessibility_ComplianceScore_IsReasonable()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var report = _service.CheckAccessibility(pdf);

        Assert.InRange(report.ComplianceScore, 0, 100);
    }

    [Fact]
    public void CheckAccessibility_IsCompliant_FalseWhenErrors()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var report = _service.CheckAccessibility(pdf);

        // Generic test PDF will have errors (no tags, no title, no language)
        Assert.True(report.ErrorCount > 0);
        Assert.False(report.IsCompliant);
    }

    [Fact]
    public void CheckAccessibility_IssuesByCategory_Populated()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var report = _service.CheckAccessibility(pdf);

        Assert.NotEmpty(report.IssuesByCategory);
    }

    [Fact]
    public void CheckAccessibility_FileNotFound_ThrowsException()
    {
        Assert.Throws<FileNotFoundException>(() =>
            _service.CheckAccessibility("nonexistent_file.pdf"));
    }

    [Fact]
    public void GenerateReportText_ProducesReadableOutput()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(2);
        var report = _service.CheckAccessibility(pdf);

        var text = _service.GenerateReportText(report);

        Assert.NotEmpty(text);
        Assert.Contains("ACCESSIBILITY REPORT", text);
        Assert.Contains("Compliance Score", text);
        Assert.Contains("document.pdf", text);
    }

    [Fact]
    public void CheckAccessibility_EmptyReport_HighComplianceScore()
    {
        var report = new AccessibilityCheckerService.AccessibilityReport();

        Assert.Equal(100.0, report.ComplianceScore);
        Assert.True(report.IsCompliant);
    }

    [Fact]
    public void CheckAccessibility_AllIssuesHaveRuleId()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(3);

        var report = _service.CheckAccessibility(pdf);

        foreach (var issue in report.Issues)
        {
            Assert.False(string.IsNullOrEmpty(issue.RuleId), "Every issue must have a RuleId");
            Assert.False(string.IsNullOrEmpty(issue.Description), "Every issue must have a Description");
            Assert.False(string.IsNullOrEmpty(issue.Recommendation), "Every issue must have a Recommendation");
        }
    }

    [Fact]
    public void CheckAccessibility_CheckDateIsRecent()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var report = _service.CheckAccessibility(pdf);

        Assert.InRange(report.CheckDate, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));
    }
}
