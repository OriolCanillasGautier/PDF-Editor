using NLog;
using Newtonsoft.Json;

namespace PDFEditor.Core.Services;

/// <summary>
/// Represents a document template
/// </summary>
public class DocumentTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public byte[] PdfTemplate { get; set; } = Array.Empty<byte>();
    public Dictionary<string, string> DefaultFieldValues { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string? ThumbnailBase64 { get; set; }
}

/// <summary>
/// Service for managing document templates. Users can save PDFs as templates,
/// organize them by category, and create new documents from templates with
/// pre-filled form field values.
/// </summary>
public class TemplateService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly List<DocumentTemplate> _templates = new();
    private string? _storagePath;

    /// <summary>
    /// All available templates
    /// </summary>
    public IReadOnlyList<DocumentTemplate> Templates => _templates.AsReadOnly();

    /// <summary>
    /// Saves a PDF as a new template
    /// </summary>
    public DocumentTemplate SaveAsTemplate(byte[] pdfBytes, string name, string description = "",
        string category = "General", List<string>? tags = null)
    {
        Log.Info("Saving template: {Name} (category: {Category})", name, category);

        var template = new DocumentTemplate
        {
            Name = name,
            Description = description,
            Category = category,
            PdfTemplate = pdfBytes,
            Tags = tags ?? new()
        };

        // Extract default form values
        try
        {
            using var reader = new iText.Kernel.Pdf.PdfReader(new MemoryStream(pdfBytes));
            using var doc = new iText.Kernel.Pdf.PdfDocument(reader);
            var form = iText.Forms.PdfAcroForm.GetAcroForm(doc, false);
            if (form != null)
            {
                foreach (var field in form.GetFormFields())
                {
                    var value = field.Value.GetValueAsString();
                    if (!string.IsNullOrEmpty(value))
                        template.DefaultFieldValues[field.Key] = value ?? string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to extract form field defaults");
        }

        _templates.Add(template);
        Log.Info("Template saved: {Id}", template.Id);
        return template;
    }

    /// <summary>
    /// Creates a new document from a template, optionally filling in field values
    /// </summary>
    public byte[] CreateFromTemplate(string templateId, Dictionary<string, string>? fieldValues = null)
    {
        var template = _templates.FirstOrDefault(t => t.Id == templateId)
            ?? throw new InvalidOperationException($"Template '{templateId}' not found");

        Log.Info("Creating document from template '{Name}'", template.Name);

        if (fieldValues == null || !fieldValues.Any())
            return template.PdfTemplate.ToArray(); // Return copy

        // Fill in form fields
        var outMs = new MemoryStream();
        using var reader = new iText.Kernel.Pdf.PdfReader(new MemoryStream(template.PdfTemplate));
        using var writer = new iText.Kernel.Pdf.PdfWriter(outMs);
        using var doc = new iText.Kernel.Pdf.PdfDocument(reader, writer);

        var form = iText.Forms.PdfAcroForm.GetAcroForm(doc, false);
        if (form != null)
        {
            var fields = form.GetFormFields();
            foreach (var kvp in fieldValues)
            {
                if (fields.ContainsKey(kvp.Key))
                {
                    fields[kvp.Key].SetValue(kvp.Value);
                }
            }
        }

        doc.Close();
        return outMs.ToArray();
    }

    /// <summary>
    /// Gets templates filtered by category
    /// </summary>
    public List<DocumentTemplate> GetByCategory(string category)
    {
        return _templates.Where(t =>
            t.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Gets all unique categories
    /// </summary>
    public List<string> GetCategories()
    {
        return _templates.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
    }

    /// <summary>
    /// Searches templates by name or tags
    /// </summary>
    public List<DocumentTemplate> Search(string query)
    {
        return _templates.Where(t =>
            t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            t.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }

    /// <summary>
    /// Removes a template
    /// </summary>
    public void RemoveTemplate(string templateId)
    {
        int removed = _templates.RemoveAll(t => t.Id == templateId);
        Log.Debug("Removed {Count} template(s)", removed);
    }

    /// <summary>
    /// Updates template metadata (not the PDF content)
    /// </summary>
    public void UpdateTemplate(string templateId, string? name = null, string? description = null,
        string? category = null, List<string>? tags = null)
    {
        var template = _templates.FirstOrDefault(t => t.Id == templateId)
            ?? throw new InvalidOperationException($"Template '{templateId}' not found");

        if (name != null) template.Name = name;
        if (description != null) template.Description = description;
        if (category != null) template.Category = category;
        if (tags != null) template.Tags = tags;

        Log.Debug("Updated template {Id}", templateId);
    }

    /// <summary>
    /// Saves all templates to a directory
    /// </summary>
    public void SaveToDirectory(string directoryPath)
    {
        _storagePath = directoryPath;
        Directory.CreateDirectory(directoryPath);

        // Save metadata index
        var index = _templates.Select(t => new
        {
            t.Id, t.Name, t.Description, t.Category, t.CreatedDate, t.Tags, t.DefaultFieldValues
        }).ToList();

        File.WriteAllText(Path.Combine(directoryPath, "index.json"),
            JsonConvert.SerializeObject(index, Formatting.Indented));

        // Save PDF files
        foreach (var template in _templates)
        {
            File.WriteAllBytes(Path.Combine(directoryPath, $"{template.Id}.pdf"), template.PdfTemplate);
        }

        Log.Info("Saved {Count} templates to {Path}", _templates.Count, directoryPath);
    }

    /// <summary>
    /// Loads templates from a directory
    /// </summary>
    public void LoadFromDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Log.Warn("Template directory not found: {Path}", directoryPath);
            return;
        }

        _storagePath = directoryPath;
        var indexPath = Path.Combine(directoryPath, "index.json");
        if (!File.Exists(indexPath))
        {
            Log.Warn("Template index not found at {Path}", indexPath);
            return;
        }

        _templates.Clear();
        var index = JsonConvert.DeserializeObject<List<DocumentTemplate>>(File.ReadAllText(indexPath));
        if (index == null) return;

        foreach (var meta in index)
        {
            var pdfPath = Path.Combine(directoryPath, $"{meta.Id}.pdf");
            if (File.Exists(pdfPath))
            {
                meta.PdfTemplate = File.ReadAllBytes(pdfPath);
                _templates.Add(meta);
            }
        }

        Log.Info("Loaded {Count} templates from {Path}", _templates.Count, directoryPath);
    }
}
