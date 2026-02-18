using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for QuickActionsService, TemplateService, WatchFolderService, PdfXService
/// </summary>
public class ProductivityServiceTests
{
    // ===== QuickActionsService Tests =====

    [Fact]
    public void QuickActions_CreateAction_ReturnsAction()
    {
        var service = new QuickActionsService();

        var action = service.CreateAction("Test Action", "Test description");

        Assert.NotNull(action);
        Assert.Equal("Test Action", action.Name);
        Assert.False(string.IsNullOrEmpty(action.Id));
    }

    [Fact]
    public void QuickActions_AddStep_AddsStep()
    {
        var service = new QuickActionsService();
        var action = service.CreateAction("My Action");

        service.AddStep(action.Id, ActionStepType.Compress, new Dictionary<string, string> { { "quality", "75" } });

        Assert.Single(action.Steps);
        Assert.Equal(ActionStepType.Compress, action.Steps[0].Type);
    }

    [Fact]
    public void QuickActions_GetBuiltInTemplates_ReturnsTemplates()
    {
        var service = new QuickActionsService();

        var templates = service.GetBuiltInTemplates();

        Assert.NotNull(templates);
        Assert.True(templates.Count > 0);
    }

    [Fact]
    public void QuickActions_DuplicateAction_CreatesACopy()
    {
        var service = new QuickActionsService();
        var action = service.CreateAction("Original");
        service.AddStep(action.Id, ActionStepType.Rotate, new Dictionary<string, string> { { "angle", "90" } });

        var duplicate = service.DuplicateAction(action.Id);

        Assert.NotNull(duplicate);
        Assert.NotEqual(action.Id, duplicate.Id);
        Assert.Single(duplicate.Steps);
    }

    [Fact]
    public void QuickActions_ExportImport_RoundTrips()
    {
        var service = new QuickActionsService();
        var action = service.CreateAction("Exportable");
        service.AddStep(action.Id, ActionStepType.AddWatermark, new Dictionary<string, string> { { "text", "DRAFT" } });

        var json = service.ExportToJson();
        Assert.NotNull(json);

        var service2 = new QuickActionsService();
        var count = service2.ImportFromJson(json);
        Assert.Equal(1, count);
        Assert.Equal("Exportable", service2.Actions[0].Name);
    }

    [Fact]
    public void QuickActions_RemoveAction_Works()
    {
        var service = new QuickActionsService();
        var action = service.CreateAction("To Remove");

        service.RemoveAction(action.Id);
        var all = service.Actions;

        Assert.DoesNotContain(all, a => a.Id == action.Id);
    }

    [Fact]
    public void QuickActions_GetAll_IncludesCreated()
    {
        var service = new QuickActionsService();
        service.CreateAction("A");
        service.CreateAction("B");

        var all = service.Actions;

        Assert.Equal(2, all.Count);
    }

    // ===== TemplateService Tests =====

    [Fact]
    public void Template_SaveAsTemplate_CreatesTemplate()
    {
        var service = new TemplateService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var template = service.SaveAsTemplate(pdf, "Test Template", "A test", "General", new List<string> { "test" });

        Assert.NotNull(template);
        Assert.Equal("Test Template", template.Name);
        Assert.Equal("General", template.Category);
    }

    [Fact]
    public void Template_CreateFromTemplate_ReturnsPdf()
    {
        var service = new TemplateService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var template = service.SaveAsTemplate(pdf, "Template1");

        var result = service.CreateFromTemplate(template.Id);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void Template_GetByCategory_Filters()
    {
        var service = new TemplateService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        service.SaveAsTemplate(pdf, "A", category: "Forms");
        service.SaveAsTemplate(pdf, "B", category: "Reports");
        service.SaveAsTemplate(pdf, "C", category: "Forms");

        var forms = service.GetByCategory("Forms");

        Assert.Equal(2, forms.Count);
    }

    [Fact]
    public void Template_GetCategories_ReturnsDistinct()
    {
        var service = new TemplateService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        service.SaveAsTemplate(pdf, "A", category: "Cat1");
        service.SaveAsTemplate(pdf, "B", category: "Cat2");
        service.SaveAsTemplate(pdf, "C", category: "Cat1");

        var categories = service.GetCategories();

        Assert.Equal(2, categories.Count);
    }

    [Fact]
    public void Template_Search_FindsByName()
    {
        var service = new TemplateService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        service.SaveAsTemplate(pdf, "Invoice Template");
        service.SaveAsTemplate(pdf, "Report Template");

        var results = service.Search("invoice");

        Assert.Single(results);
        Assert.Equal("Invoice Template", results[0].Name);
    }

    [Fact]
    public void Template_RemoveTemplate_Works()
    {
        var service = new TemplateService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var template = service.SaveAsTemplate(pdf, "Removable");

        service.RemoveTemplate(template.Id);

        Assert.Throws<InvalidOperationException>(() => service.CreateFromTemplate(template.Id));
    }

    // ===== PdfXService Tests =====

    [Fact]
    public void PdfX_Inspect_ReturnsResult()
    {
        var service = new PdfXService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var result = service.Inspect(pdf);

        Assert.NotNull(result);
        Assert.False(result.IsPdfX); // Simple PDF is not PDF/X
    }

    [Fact]
    public void PdfX_Inspect_ChecksOutputIntent()
    {
        var service = new PdfXService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var result = service.Inspect(pdf);

        Assert.False(result.HasOutputIntent);
        Assert.Contains(result.Issues, i => i.Contains("OutputIntent"));
    }

    [Fact]
    public void PdfX_ConvertToPdfX_ReturnsBytes()
    {
        var service = new PdfXService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var result = service.ConvertToPdfX(pdf);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void PdfX_ConvertToPdfX_AddsOutputIntent()
    {
        var service = new PdfXService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var result = service.ConvertToPdfX(pdf);
        var inspection = service.Inspect(result);

        Assert.True(inspection.HasOutputIntent);
    }

    [Fact]
    public void PdfX_GenerateReport_ReturnsString()
    {
        var service = new PdfXService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var report = service.GenerateReport(pdf);

        Assert.NotNull(report);
        Assert.Contains("PDF/X Compliance Report", report);
    }

    [Fact]
    public void PdfX_ConvertToPdfX_DifferentConformanceLevels()
    {
        var service = new PdfXService();
        var pdf = TestPdfGenerator.CreateSimplePdf(1);

        var resultX1a = service.ConvertToPdfX(pdf, PdfXConformance.PdfX1a);
        var resultX3 = service.ConvertToPdfX(pdf, PdfXConformance.PdfX3);
        var resultX4 = service.ConvertToPdfX(pdf, PdfXConformance.PdfX4);

        Assert.True(resultX1a.Length > 0);
        Assert.True(resultX3.Length > 0);
        Assert.True(resultX4.Length > 0);
    }

    // ===== WatchFolderService Tests =====

    [Fact]
    public void WatchFolder_ConstructsWithoutError()
    {
        using var service = new WatchFolderService();
        Assert.NotNull(service);
    }

    [Fact]
    public void WatchFolder_GetHistory_ReturnsEmpty()
    {
        using var service = new WatchFolderService();
        var history = service.ProcessHistory;
        Assert.Empty(history);
    }
}
