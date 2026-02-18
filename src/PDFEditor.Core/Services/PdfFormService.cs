using iText.Kernel.Pdf;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Geom;
using NLog;
using PDFEditor.Core.Abstractions;
using iTextFormField = iText.Forms.Fields.PdfFormField;

namespace PDFEditor.Core.Services;

/// <summary>
/// PDF form field operations using iText7 AcroForm support.
/// Supports reading, filling, flattening, creating, and importing/exporting form data.
/// </summary>
public class PdfFormService : IFormService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <inheritdoc/>
    public bool HasFormFields(byte[] pdfBytes)
    {
        try
        {
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var doc = new PdfDocument(reader);
            var form = PdfAcroForm.GetAcroForm(doc, false);
            return form != null && form.GetFormFields().Count > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking for form fields");
            return false;
        }
    }

    /// <inheritdoc/>
    public List<FormFieldInfo> GetFormFields(byte[] pdfBytes)
    {
        var result = new List<FormFieldInfo>();
        try
        {
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var doc = new PdfDocument(reader);
            var form = PdfAcroForm.GetAcroForm(doc, false);
            if (form == null) return result;

            var fields = form.GetFormFields();
            foreach (var kvp in fields)
            {
                var field = kvp.Value;
                var formField = new FormFieldInfo
                {
                    Name = kvp.Key,
                    FieldType = DetectFieldType(field),
                    Value = field.GetValueAsString() ?? string.Empty,
                    DefaultValue = field.GetDefaultValue()?.ToString() ?? string.Empty,
                    IsReadOnly = field.IsReadOnly(),
                };

                // Get position from the first widget annotation
                var widgets = field.GetWidgets();
                if (widgets != null && widgets.Count > 0)
                {
                    var widget = widgets[0];
                    var rect = widget.GetRectangle();
                    if (rect != null)
                    {
                        formField.X = rect.GetAsNumber(0)?.FloatValue() ?? 0;
                        formField.Y = rect.GetAsNumber(1)?.FloatValue() ?? 0;
                        float x2 = rect.GetAsNumber(2)?.FloatValue() ?? 0;
                        float y2 = rect.GetAsNumber(3)?.FloatValue() ?? 0;
                        formField.Width = x2 - formField.X;
                        formField.Height = y2 - formField.Y;
                    }

                    // Determine page index
                    var page = widget.GetPage();
                    if (page != null)
                    {
                        formField.PageIndex = doc.GetPageNumber(page) - 1; // 0-based
                    }
                }

                // Handle dropdown/listbox options
                if (formField.FieldType == FormFieldType.Dropdown ||
                    formField.FieldType == FormFieldType.ListBox)
                {
                    if (field is PdfChoiceFormField choiceField)
                    {
                        var options = choiceField.GetOptions();
                        if (options != null)
                        {
                            for (int i = 0; i < options.Size(); i++)
                            {
                                var opt = options.Get(i);
                                formField.Options.Add(opt?.ToString() ?? string.Empty);
                            }
                        }
                    }
                }

                // Handle checkbox state
                if (formField.FieldType == FormFieldType.Checkbox)
                {
                    var val = field.GetValueAsString();
                    formField.IsChecked = val != null && val != "Off" && val != "";
                }

                result.Add(formField);
            }

            Log.Info("Extracted {Count} form fields from PDF", result.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error extracting form fields");
        }
        return result;
    }

    /// <inheritdoc/>
    public byte[] SetFieldValue(byte[] pdfBytes, string fieldName, string value)
    {
        return FillForm(pdfBytes, new Dictionary<string, string> { { fieldName, value } });
    }

    /// <inheritdoc/>
    public byte[] FillForm(byte[] pdfBytes, Dictionary<string, string> fieldValues)
    {
        try
        {
            var outputMs = new MemoryStream();
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var writer = new PdfWriter(outputMs);
            using var doc = new PdfDocument(reader, writer);

            var form = PdfAcroForm.GetAcroForm(doc, false);
            if (form == null)
            {
                Log.Warn("PDF has no form fields to fill");
                return pdfBytes;
            }

            var fields = form.GetFormFields();
            int filledCount = 0;

            foreach (var kvp in fieldValues)
            {
                if (fields.TryGetValue(kvp.Key, out var field))
                {
                    // Handle checkbox special case
                    if (DetectFieldType(field) == FormFieldType.Checkbox)
                    {
                        bool check = kvp.Value == "true" || kvp.Value == "Yes" || kvp.Value == "1";
                        field.SetValue(check ? "Yes" : "Off");
                    }
                    else
                    {
                        field.SetValue(kvp.Value);
                    }
                    filledCount++;
                }
                else
                {
                    Log.Warn("Form field '{FieldName}' not found in PDF", kvp.Key);
                }
            }

            doc.Close();
            Log.Info("Filled {Count} form fields", filledCount);
            return outputMs.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error filling form fields");
            throw;
        }
    }

    /// <inheritdoc/>
    public byte[] FlattenForm(byte[] pdfBytes)
    {
        try
        {
            var outputMs = new MemoryStream();
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var writer = new PdfWriter(outputMs);
            using var doc = new PdfDocument(reader, writer);

            var form = PdfAcroForm.GetAcroForm(doc, false);
            if (form != null)
            {
                form.FlattenFields();
                Log.Info("Form fields flattened successfully");
            }
            else
            {
                Log.Warn("No form fields to flatten");
            }

            doc.Close();
            return outputMs.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error flattening form fields");
            throw;
        }
    }

    /// <inheritdoc/>
    public FormDataResult ExportFormData(byte[] pdfBytes)
    {
        var result = new FormDataResult();
        try
        {
            var fields = GetFormFields(pdfBytes);
            foreach (var field in fields)
            {
                result.FieldValues[field.Name] = field.FieldType == FormFieldType.Checkbox
                    ? (field.IsChecked ? "true" : "false")
                    : field.Value;
            }
            result.Success = true;
            Log.Info("Exported {Count} form field values", result.FieldValues.Count);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            Log.Error(ex, "Error exporting form data");
        }
        return result;
    }

    /// <inheritdoc/>
    public byte[] ImportFormData(byte[] pdfBytes, Dictionary<string, string> fieldValues)
    {
        return FillForm(pdfBytes, fieldValues);
    }

    /// <inheritdoc/>
    public byte[] AddTextField(byte[] pdfBytes, int pageIndex, string fieldName,
        float x, float y, float width, float height, string defaultValue = "")
    {
        try
        {
            var outputMs = new MemoryStream();
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var writer = new PdfWriter(outputMs);
            using var doc = new PdfDocument(reader, writer);

            var form = PdfAcroForm.GetAcroForm(doc, true);
            var page = doc.GetPage(pageIndex + 1); // 1-based
            var rect = new Rectangle(x, y, width, height);

            var textField = iTextFormField.CreateText(doc, rect, fieldName, defaultValue);
            form.AddField(textField, page);

            doc.Close();
            Log.Info("Added text field '{FieldName}' on page {Page}", fieldName, pageIndex + 1);
            return outputMs.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding text field '{FieldName}'", fieldName);
            throw;
        }
    }

    /// <inheritdoc/>
    public byte[] AddCheckboxField(byte[] pdfBytes, int pageIndex, string fieldName,
        float x, float y, float width, float height, bool defaultChecked = false)
    {
        try
        {
            var outputMs = new MemoryStream();
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var writer = new PdfWriter(outputMs);
            using var doc = new PdfDocument(reader, writer);

            var form = PdfAcroForm.GetAcroForm(doc, true);
            var page = doc.GetPage(pageIndex + 1);
            var rect = new Rectangle(x, y, width, height);

            var checkBox = iTextFormField.CreateCheckBox(doc, rect, fieldName,
                defaultChecked ? "Yes" : "Off");
            form.AddField(checkBox, page);

            doc.Close();
            Log.Info("Added checkbox field '{FieldName}' on page {Page}", fieldName, pageIndex + 1);
            return outputMs.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding checkbox field '{FieldName}'", fieldName);
            throw;
        }
    }

    /// <inheritdoc/>
    public byte[] AddDropdownField(byte[] pdfBytes, int pageIndex, string fieldName,
        float x, float y, float width, float height, string[] options, string defaultValue = "")
    {
        try
        {
            var outputMs = new MemoryStream();
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var writer = new PdfWriter(outputMs);
            using var doc = new PdfDocument(reader, writer);

            var form = PdfAcroForm.GetAcroForm(doc, true);
            var page = doc.GetPage(pageIndex + 1);
            var rect = new Rectangle(x, y, width, height);

            var combo = iTextFormField.CreateComboBox(doc, rect, fieldName,
                string.IsNullOrEmpty(defaultValue) ? (options.Length > 0 ? options[0] : "") : defaultValue,
                options);
            form.AddField(combo, page);

            doc.Close();
            Log.Info("Added dropdown field '{FieldName}' on page {Page}", fieldName, pageIndex + 1);
            return outputMs.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding dropdown field '{FieldName}'", fieldName);
            throw;
        }
    }

    /// <inheritdoc/>
    public byte[] AddRadioButtonField(byte[] pdfBytes, int pageIndex, string groupName,
        float x, float y, float width, float height, string[] options)
    {
        try
        {
            var outputMs = new MemoryStream();
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var writer = new PdfWriter(outputMs);
            using var doc = new PdfDocument(reader, writer);

            var form = PdfAcroForm.GetAcroForm(doc, true);
            var page = doc.GetPage(pageIndex + 1);

            var radioGroup = iTextFormField.CreateRadioGroup(doc, groupName,
                options.Length > 0 ? options[0] : "");

            float currentY = y;
            foreach (var option in options)
            {
                var rect = new Rectangle(x, currentY, width, height);
                var radioButton = iTextFormField.CreateRadioButton(doc, rect, radioGroup, option);
                currentY -= (height + 5); // Stack radio buttons vertically
            }

            form.AddField(radioGroup, page);

            doc.Close();
            Log.Info("Added radio button group '{GroupName}' with {Count} options on page {Page}",
                groupName, options.Length, pageIndex + 1);
            return outputMs.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding radio button group '{GroupName}'", groupName);
            throw;
        }
    }

    /// <inheritdoc/>
    public byte[] AddSignatureField(byte[] pdfBytes, int pageIndex, string fieldName,
        float x, float y, float width, float height)
    {
        try
        {
            var outputMs = new MemoryStream();
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var writer = new PdfWriter(outputMs);
            using var doc = new PdfDocument(reader, writer);

            var form = PdfAcroForm.GetAcroForm(doc, true);
            var page = doc.GetPage(pageIndex + 1);
            var rect = new Rectangle(x, y, width, height);

            var sigField = PdfSignatureFormField.CreateSignature(doc, rect);
            sigField.SetFieldName(fieldName);
            form.AddField(sigField, page);

            doc.Close();
            Log.Info("Added signature field '{FieldName}' on page {Page}", fieldName, pageIndex + 1);
            return outputMs.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding signature field '{FieldName}'", fieldName);
            throw;
        }
    }

    /// <inheritdoc/>
    public byte[] SetFieldProperties(byte[] pdfBytes, string fieldName,
        bool? isReadOnly = null, bool? isRequired = null, string? defaultValue = null)
    {
        try
        {
            var outputMs = new MemoryStream();
            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var writer = new PdfWriter(outputMs);
            using var doc = new PdfDocument(reader, writer);

            var form = PdfAcroForm.GetAcroForm(doc, false);
            if (form == null)
                throw new InvalidOperationException("PDF has no form fields.");

            var fields = form.GetFormFields();
            if (!fields.TryGetValue(fieldName, out var field))
                throw new KeyNotFoundException($"Form field '{fieldName}' not found.");

            if (isReadOnly.HasValue)
                field.SetReadOnly(isReadOnly.Value);

            if (isRequired.HasValue)
            {
                if (isRequired.Value)
                    field.SetFieldFlag(iTextFormField.FF_REQUIRED, true);
                else
                    field.SetFieldFlag(iTextFormField.FF_REQUIRED, false);
            }

            if (defaultValue != null)
                field.SetDefaultValue(new iText.Kernel.Pdf.PdfString(defaultValue));

            doc.Close();
            Log.Info("Updated properties for field '{FieldName}'", fieldName);
            return outputMs.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error setting field properties for '{FieldName}'", fieldName);
            throw;
        }
    }

    /// <summary>
    /// Determines the form field type from an iText7 PdfFormField object.
    /// </summary>
    private static FormFieldType DetectFieldType(iTextFormField field)
    {
        if (field is PdfButtonFormField buttonField)
        {
            if (buttonField.IsPushButton())
                return FormFieldType.PushButton;
            if (buttonField.IsRadio())
                return FormFieldType.RadioButton;
            return FormFieldType.Checkbox;
        }

        if (field is PdfChoiceFormField choiceField)
        {
            if (choiceField.IsCombo())
                return FormFieldType.Dropdown;
            return FormFieldType.ListBox;
        }

        if (field is PdfTextFormField)
            return FormFieldType.Text;

        if (field is PdfSignatureFormField)
            return FormFieldType.Signature;

        return FormFieldType.Unknown;
    }
}
