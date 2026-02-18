using PDFEditor.Core.Services;
using PDFEditor.Core.Abstractions;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for PdfFormService: form field detection, reading, filling, flattening, export/import
/// </summary>
public class PdfFormServiceTests
{
    private readonly PdfFormService _sut = new();

    #region HasFormFields

    [Fact]
    public void HasFormFields_SimplePdf_ReturnsFalse()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        Assert.False(_sut.HasFormFields(pdf));
    }

    [Fact]
    public void HasFormFields_PdfWithTextField_ReturnsTrue()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var withField = _sut.AddTextField(pdf, 0, "Name", 50, 700, 200, 20, "");
        Assert.True(_sut.HasFormFields(withField));
    }

    #endregion

    #region GetFormFields

    [Fact]
    public void GetFormFields_NonePresent_ReturnsEmpty()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var fields = _sut.GetFormFields(pdf);
        Assert.Empty(fields);
    }

    [Fact]
    public void GetFormFields_AfterAddTextField_ReturnsField()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var withField = _sut.AddTextField(pdf, 0, "FirstName", 50, 700, 200, 20, "John");
        var fields = _sut.GetFormFields(withField);

        Assert.Single(fields);
        Assert.Equal("FirstName", fields[0].Name);
        Assert.Equal(FormFieldType.Text, fields[0].FieldType);
    }

    [Fact]
    public void GetFormFields_MultipleFields_ReturnsAll()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        pdf = _sut.AddTextField(pdf, 0, "Name", 50, 700, 200, 20, "");
        pdf = _sut.AddCheckboxField(pdf, 0, "Agree", 50, 670, 15, 15);
        pdf = _sut.AddDropdownField(pdf, 0, "Country", 50, 640, 150, 20, new[] { "US", "UK", "ES" });

        var fields = _sut.GetFormFields(pdf);
        Assert.Equal(3, fields.Count);
    }

    #endregion

    #region FillForm

    [Fact]
    public void FillForm_SetTextFieldValue_Succeeds()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        pdf = _sut.AddTextField(pdf, 0, "Name", 50, 700, 200, 20, "");

        var filled = _sut.FillForm(pdf, new Dictionary<string, string> { { "Name", "Alice" } });
        var fields = _sut.GetFormFields(filled);

        Assert.Single(fields);
        Assert.Equal("Alice", fields[0].Value);
    }

    [Fact]
    public void SetFieldValue_UpdatesToNewValue()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        pdf = _sut.AddTextField(pdf, 0, "City", 50, 700, 200, 20, "Barcelona");

        var updated = _sut.SetFieldValue(pdf, "City", "Madrid");
        var fields = _sut.GetFormFields(updated);

        Assert.Equal("Madrid", fields[0].Value);
    }

    [Fact]
    public void FillForm_NonexistentField_ReturnsOriginal()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        pdf = _sut.AddTextField(pdf, 0, "Name", 50, 700, 200, 20, "");

        // Should not throw, just log warning
        var filled = _sut.FillForm(pdf, new Dictionary<string, string> { { "NonExistent", "val" } });
        Assert.NotNull(filled);
    }

    #endregion

    #region FlattenForm

    [Fact]
    public void FlattenForm_RemovesInteractiveFields()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        pdf = _sut.AddTextField(pdf, 0, "Name", 50, 700, 200, 20, "TestValue");

        Assert.True(_sut.HasFormFields(pdf));

        var flattened = _sut.FlattenForm(pdf);
        Assert.False(_sut.HasFormFields(flattened));
    }

    [Fact]
    public void FlattenForm_NullForm_ReturnsValidPdf()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var flattened = _sut.FlattenForm(pdf);
        Assert.NotNull(flattened);
        Assert.True(flattened.Length > 0);
    }

    #endregion

    #region ExportFormData / ImportFormData

    [Fact]
    public void ExportFormData_ReturnsFieldValues()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        pdf = _sut.AddTextField(pdf, 0, "Name", 50, 700, 200, 20, "ExportTest");

        var result = _sut.ExportFormData(pdf);
        Assert.True(result.Success);
        Assert.Single(result.FieldValues);
        Assert.Equal("ExportTest", result.FieldValues["Name"]);
    }

    [Fact]
    public void ExportFormData_NoFields_ReturnsEmptySuccess()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var result = _sut.ExportFormData(pdf);
        Assert.True(result.Success);
        Assert.Empty(result.FieldValues);
    }

    [Fact]
    public void ImportFormData_RoundTrip_PreservesValues()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        pdf = _sut.AddTextField(pdf, 0, "Name", 50, 700, 200, 20, "");
        pdf = _sut.AddTextField(pdf, 0, "Email", 50, 670, 200, 20, "");

        var data = new Dictionary<string, string> { { "Name", "Bob" }, { "Email", "bob@test.com" } };
        var imported = _sut.ImportFormData(pdf, data);

        var exported = _sut.ExportFormData(imported);
        Assert.Equal("Bob", exported.FieldValues["Name"]);
        Assert.Equal("bob@test.com", exported.FieldValues["Email"]);
    }

    #endregion

    #region AddField Operations

    [Fact]
    public void AddTextField_CreatesValidField()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var result = _sut.AddTextField(pdf, 0, "TestField", 100, 500, 200, 25, "default");

        var fields = _sut.GetFormFields(result);
        Assert.Single(fields);
        Assert.Equal("TestField", fields[0].Name);
        Assert.Equal(FormFieldType.Text, fields[0].FieldType);
    }

    [Fact]
    public void AddCheckboxField_CreatesValidField()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var result = _sut.AddCheckboxField(pdf, 0, "AcceptTerms", 50, 500, 15, 15, true);

        var fields = _sut.GetFormFields(result);
        Assert.Single(fields);
        Assert.Equal("AcceptTerms", fields[0].Name);
        Assert.Equal(FormFieldType.Checkbox, fields[0].FieldType);
    }

    [Fact]
    public void AddDropdownField_CreatesValidField()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var options = new[] { "Red", "Green", "Blue" };
        var result = _sut.AddDropdownField(pdf, 0, "Color", 50, 500, 150, 20, options, "Green");

        var fields = _sut.GetFormFields(result);
        Assert.Single(fields);
        Assert.Equal("Color", fields[0].Name);
        Assert.Equal(FormFieldType.Dropdown, fields[0].FieldType);
    }

    [Fact]
    public void AddTextField_InvalidPageIndex_Throws()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        Assert.ThrowsAny<Exception>(() =>
            _sut.AddTextField(pdf, 99, "BadField", 50, 700, 200, 20));
    }

    #endregion

    #region RadioButton and Signature Fields

    [Fact]
    public void AddRadioButtonField_CreatesRadioGroup()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var options = new[] { "Yes", "No", "Maybe" };
        var result = _sut.AddRadioButtonField(pdf, 0, "Response", 50, 700, 15, 15, options);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        Assert.True(_sut.HasFormFields(result));
    }

    [Fact]
    public void AddSignatureField_CreatesSignatureField()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var result = _sut.AddSignatureField(pdf, 0, "Sig1", 50, 50, 200, 80);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        Assert.True(_sut.HasFormFields(result));

        var fields = _sut.GetFormFields(result);
        Assert.Single(fields);
        Assert.Equal("Sig1", fields[0].Name);
        Assert.Equal(FormFieldType.Signature, fields[0].FieldType);
    }

    [Fact]
    public void AddRadioButtonField_InvalidPage_Throws()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        Assert.ThrowsAny<Exception>(() =>
            _sut.AddRadioButtonField(pdf, 99, "BadRadio", 50, 700, 15, 15, new[] { "A", "B" }));
    }

    [Fact]
    public void AddSignatureField_InvalidPage_Throws()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        Assert.ThrowsAny<Exception>(() =>
            _sut.AddSignatureField(pdf, 99, "BadSig", 50, 50, 200, 80));
    }

    #endregion

    #region SetFieldProperties

    [Fact]
    public void SetFieldProperties_SetReadOnly_MakesFieldReadOnly()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var withField = _sut.AddTextField(pdf, 0, "Name", 50, 700, 200, 20, "default");
        var result = _sut.SetFieldProperties(withField, "Name", isReadOnly: true);

        var fields = _sut.GetFormFields(result);
        Assert.Single(fields);
        Assert.True(fields[0].IsReadOnly);
    }

    [Fact]
    public void SetFieldProperties_NonexistentField_ThrowsKeyNotFound()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        var withField = _sut.AddTextField(pdf, 0, "Name", 50, 700, 200, 20);
        Assert.Throws<KeyNotFoundException>(() =>
            _sut.SetFieldProperties(withField, "NoSuchField", isReadOnly: true));
    }

    [Fact]
    public void SetFieldProperties_NullPdf_ThrowsInvalidOperation()
    {
        var pdf = TestPdfGenerator.CreateSimplePdf(1);
        Assert.Throws<InvalidOperationException>(() =>
            _sut.SetFieldProperties(pdf, "AnyField", isReadOnly: true));
    }

    #endregion
}
