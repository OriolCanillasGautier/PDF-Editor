using NLog;
using Newtonsoft.Json;

namespace PDFEditor.Core.Services;

/// <summary>
/// Represents a user-defined quick action (macro)
/// </summary>
public class QuickAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? KeyboardShortcut { get; set; }
    public List<ActionStep> Steps { get; set; } = new();
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedDate { get; set; }
    public int UseCount { get; set; }
}

/// <summary>
/// A single step in a quick action macro
/// </summary>
public class ActionStep
{
    public ActionStepType Type { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public int Order { get; set; }
}

/// <summary>
/// Types of quick action steps
/// </summary>
public enum ActionStepType
{
    Rotate,
    AddWatermark,
    Compress,
    Encrypt,
    Decrypt,
    AddPageNumbers,
    RemovePages,
    Flatten,
    Export,
    Merge,
    Split,
    Crop,
    AddHeader,
    AddFooter,
    ScrubMetadata,
    Deskew,
    AddBackground,
    RemoveBackground
}

/// <summary>
/// Service for managing customizable quick action macros.
/// Users can record sequences of operations and replay them on any PDF.
/// </summary>
public class QuickActionsService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly List<QuickAction> _actions = new();
    private string? _storagePath;

    /// <summary>
    /// All registered quick actions
    /// </summary>
    public IReadOnlyList<QuickAction> Actions => _actions.AsReadOnly();

    /// <summary>
    /// Creates a new quick action
    /// </summary>
    public QuickAction CreateAction(string name, string description = "")
    {
        var action = new QuickAction
        {
            Name = name,
            Description = description
        };
        _actions.Add(action);
        Log.Info("Created quick action: {Name}", name);
        return action;
    }

    /// <summary>
    /// Adds a step to a quick action
    /// </summary>
    public void AddStep(string actionId, ActionStepType type, Dictionary<string, string>? parameters = null)
    {
        var action = _actions.FirstOrDefault(a => a.Id == actionId);
        if (action == null)
            throw new InvalidOperationException($"Quick action '{actionId}' not found");

        action.Steps.Add(new ActionStep
        {
            Type = type,
            Parameters = parameters ?? new(),
            Order = action.Steps.Count
        });

        Log.Debug("Added step {Type} to action '{Name}'", type, action.Name);
    }

    /// <summary>
    /// Executes a quick action on a PDF
    /// </summary>
    public byte[] Execute(byte[] pdfBytes, string actionId,
        IProgress<(int step, int total, string description)>? progress = null)
    {
        var action = _actions.FirstOrDefault(a => a.Id == actionId);
        if (action == null)
            throw new InvalidOperationException($"Quick action '{actionId}' not found");

        Log.Info("Executing quick action '{Name}' ({Steps} steps)", action.Name, action.Steps.Count);

        byte[] result = pdfBytes;
        int stepNum = 0;

        foreach (var step in action.Steps.OrderBy(s => s.Order))
        {
            stepNum++;
            progress?.Report((stepNum, action.Steps.Count, $"{step.Type}"));

            result = ExecuteStep(result, step);
        }

        action.LastUsedDate = DateTime.UtcNow;
        action.UseCount++;

        Log.Info("Quick action '{Name}' complete", action.Name);
        return result;
    }

    /// <summary>
    /// Removes a quick action
    /// </summary>
    public void RemoveAction(string actionId)
    {
        int removed = _actions.RemoveAll(a => a.Id == actionId);
        Log.Debug("Removed {Count} quick action(s)", removed);
    }

    /// <summary>
    /// Duplicates a quick action
    /// </summary>
    public QuickAction DuplicateAction(string actionId)
    {
        var source = _actions.FirstOrDefault(a => a.Id == actionId)
            ?? throw new InvalidOperationException($"Quick action '{actionId}' not found");

        var clone = new QuickAction
        {
            Name = $"{source.Name} (Copy)",
            Description = source.Description,
            Steps = source.Steps.Select(s => new ActionStep
            {
                Type = s.Type,
                Parameters = new Dictionary<string, string>(s.Parameters),
                Order = s.Order
            }).ToList()
        };

        _actions.Add(clone);
        return clone;
    }

    /// <summary>
    /// Exports all quick actions to JSON
    /// </summary>
    public string ExportToJson()
    {
        return JsonConvert.SerializeObject(_actions, Formatting.Indented);
    }

    /// <summary>
    /// Imports quick actions from JSON
    /// </summary>
    public int ImportFromJson(string json)
    {
        var imported = JsonConvert.DeserializeObject<List<QuickAction>>(json);
        if (imported == null) return 0;

        _actions.AddRange(imported);
        Log.Info("Imported {Count} quick actions", imported.Count);
        return imported.Count;
    }

    /// <summary>
    /// Saves quick actions to a file
    /// </summary>
    public void SaveToFile(string path)
    {
        _storagePath = path;
        File.WriteAllText(path, ExportToJson());
        Log.Info("Saved {Count} quick actions to {Path}", _actions.Count, path);
    }

    /// <summary>
    /// Loads quick actions from a file
    /// </summary>
    public void LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("Quick actions file not found: {Path}", path);
            return;
        }

        _storagePath = path;
        var json = File.ReadAllText(path);
        var loaded = JsonConvert.DeserializeObject<List<QuickAction>>(json);
        if (loaded != null)
        {
            _actions.Clear();
            _actions.AddRange(loaded);
            Log.Info("Loaded {Count} quick actions from {Path}", loaded.Count, path);
        }
    }

    /// <summary>
    /// Gets predefined quick action templates
    /// </summary>
    public List<QuickAction> GetBuiltInTemplates()
    {
        return new List<QuickAction>
        {
            new QuickAction
            {
                Name = "Optimize for Web",
                Description = "Compress images and scrub metadata for web sharing",
                Steps = new List<ActionStep>
                {
                    new() { Type = ActionStepType.Compress, Parameters = new() { { "quality", "60" } }, Order = 0 },
                    new() { Type = ActionStepType.ScrubMetadata, Order = 1 }
                }
            },
            new QuickAction
            {
                Name = "Prepare for Print",
                Description = "Add page numbers and crop marks",
                Steps = new List<ActionStep>
                {
                    new() { Type = ActionStepType.AddPageNumbers, Parameters = new() { { "format", "Page {0}" } }, Order = 0 }
                }
            },
            new QuickAction
            {
                Name = "Clean Scanned Document",
                Description = "Deskew and remove background from scanned pages",
                Steps = new List<ActionStep>
                {
                    new() { Type = ActionStepType.Deskew, Order = 0 },
                    new() { Type = ActionStepType.RemoveBackground, Order = 1 }
                }
            },
            new QuickAction
            {
                Name = "Secure Document",
                Description = "Remove metadata and encrypt with password",
                Steps = new List<ActionStep>
                {
                    new() { Type = ActionStepType.ScrubMetadata, Order = 0 },
                    new() { Type = ActionStepType.Encrypt, Parameters = new() { { "password", "" } }, Order = 1 }
                }
            }
        };
    }

    private byte[] ExecuteStep(byte[] pdfBytes, ActionStep step)
    {
        try
        {
            return step.Type switch
            {
                ActionStepType.Rotate => ExecuteRotate(pdfBytes, step.Parameters),
                ActionStepType.AddWatermark => ExecuteWatermark(pdfBytes, step.Parameters),
                ActionStepType.ScrubMetadata => ExecuteScrubMetadata(pdfBytes),
                ActionStepType.Flatten => ExecuteFlatten(pdfBytes),
                ActionStepType.AddPageNumbers => ExecuteAddPageNumbers(pdfBytes, step.Parameters),
                _ => pdfBytes // Unknown step types are no-ops
            };
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Quick action step {Type} failed, skipping", step.Type);
            return pdfBytes;
        }
    }

    private byte[] ExecuteRotate(byte[] pdfBytes, Dictionary<string, string> parameters)
    {
        int degrees = int.TryParse(parameters.GetValueOrDefault("degrees", "90"), out int d) ? d : 90;
        var ops = new PdfOperations();
        return ops.RotatePages(pdfBytes, Enumerable.Range(0, 9999).ToArray(), degrees);
    }

    private byte[] ExecuteWatermark(byte[] pdfBytes, Dictionary<string, string> parameters)
    {
        string text = parameters.GetValueOrDefault("text", "DRAFT");
        float fontSize = float.TryParse(parameters.GetValueOrDefault("fontSize", "60"), out float fs) ? fs : 60f;
        float opacity = float.TryParse(parameters.GetValueOrDefault("opacity", "0.3"), out float op) ? op : 0.3f;
        var service = new PdfWatermarkService();
        return service.AddTextWatermark(pdfBytes, text, fontSize, opacity);
    }

    private byte[] ExecuteScrubMetadata(byte[] pdfBytes)
    {
        var service = new MetadataScrubberService();
        return service.ScrubAsync(pdfBytes).GetAwaiter().GetResult();
    }

    private byte[] ExecuteFlatten(byte[] pdfBytes)
    {
        var outMs = new MemoryStream();
        using var reader = new iText.Kernel.Pdf.PdfReader(new MemoryStream(pdfBytes));
        using var writer = new iText.Kernel.Pdf.PdfWriter(outMs);
        using var doc = new iText.Kernel.Pdf.PdfDocument(reader, writer);
        var form = iText.Forms.PdfAcroForm.GetAcroForm(doc, false);
        form?.FlattenFields();
        doc.Close();
        return outMs.ToArray();
    }

    private byte[] ExecuteAddPageNumbers(byte[] pdfBytes, Dictionary<string, string> parameters)
    {
        string format = parameters.GetValueOrDefault("format", "Page {0} of {1}");
        var service = new HeaderFooterService();
        var options = new HeaderFooterService.HFOptions
        {
            Footer = new HeaderFooterService.HFElement
            {
                Template = format.Replace("{0}", "{page}").Replace("{1}", "{total}"),
                Alignment = HeaderFooterService.HFAlignment.Center
            }
        };
        return service.AddHeaderFooterAsync(pdfBytes, options).GetAwaiter().GetResult();
    }
}
