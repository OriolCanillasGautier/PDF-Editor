namespace PDFEditor.Core.Abstractions;

/// <summary>
/// Represents a single form field in a PDF document.
/// </summary>
public class FormFieldInfo
{
    public string Name { get; set; } = string.Empty;
    public FormFieldType FieldType { get; set; }
    public string Value { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; }
    public bool IsRequired { get; set; }
    public int PageIndex { get; set; }

    /// <summary>
    /// For dropdown/listbox fields: available options.
    /// </summary>
    public List<string> Options { get; set; } = new();

    /// <summary>
    /// For checkbox/radio fields: whether the field is checked.
    /// </summary>
    public bool IsChecked { get; set; }

    /// <summary>
    /// Position on page (points from bottom-left in PDF coordinate system).
    /// </summary>
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}

/// <summary>
/// Supported PDF form field types.
/// </summary>
public enum FormFieldType
{
    Text,
    Checkbox,
    RadioButton,
    Dropdown,
    ListBox,
    PushButton,
    Signature,
    Unknown
}

/// <summary>
/// Result of a form data export/import operation.
/// </summary>
public class FormDataResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> FieldValues { get; set; } = new();
}

/// <summary>
/// Service interface for PDF form field operations.
/// </summary>
public interface IFormService
{
    /// <summary>
    /// Detects whether the PDF contains interactive AcroForm fields.
    /// </summary>
    bool HasFormFields(byte[] pdfBytes);

    /// <summary>
    /// Extracts all form fields from a PDF document.
    /// </summary>
    List<FormFieldInfo> GetFormFields(byte[] pdfBytes);

    /// <summary>
    /// Sets the value of a single form field and returns the modified PDF bytes.
    /// </summary>
    byte[] SetFieldValue(byte[] pdfBytes, string fieldName, string value);

    /// <summary>
    /// Fills multiple form fields at once and returns the modified PDF bytes.
    /// </summary>
    byte[] FillForm(byte[] pdfBytes, Dictionary<string, string> fieldValues);

    /// <summary>
    /// Flattens form fields into static content (no longer editable).
    /// </summary>
    byte[] FlattenForm(byte[] pdfBytes);

    /// <summary>
    /// Exports form field data to JSON format.
    /// </summary>
    FormDataResult ExportFormData(byte[] pdfBytes);

    /// <summary>
    /// Imports form data from JSON and fills the fields.
    /// </summary>
    byte[] ImportFormData(byte[] pdfBytes, Dictionary<string, string> fieldValues);

    /// <summary>
    /// Adds a new text form field to the specified page.
    /// </summary>
    byte[] AddTextField(byte[] pdfBytes, int pageIndex, string fieldName,
        float x, float y, float width, float height, string defaultValue = "");

    /// <summary>
    /// Adds a new checkbox form field to the specified page.
    /// </summary>
    byte[] AddCheckboxField(byte[] pdfBytes, int pageIndex, string fieldName,
        float x, float y, float width, float height, bool defaultChecked = false);

    /// <summary>
    /// Adds a new dropdown form field to the specified page.
    /// </summary>
    byte[] AddDropdownField(byte[] pdfBytes, int pageIndex, string fieldName,
        float x, float y, float width, float height, string[] options, string defaultValue = "");
    byte[] AddRadioButtonField(byte[] pdfBytes, int pageIndex, string groupName,
        float x, float y, float width, float height, string[] options);
    byte[] AddSignatureField(byte[] pdfBytes, int pageIndex, string fieldName,
        float x, float y, float width, float height);
    byte[] SetFieldProperties(byte[] pdfBytes, string fieldName,
        bool? isReadOnly = null, bool? isRequired = null, string? defaultValue = null);
}
