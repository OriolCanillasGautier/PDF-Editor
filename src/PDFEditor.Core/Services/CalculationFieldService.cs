using iText.Kernel.Pdf;
using iText.Forms;
using iText.Forms.Fields;
using NLog;
using Newtonsoft.Json;

namespace PDFEditor.Core.Services;

/// <summary>
/// Defines a calculation rule for a form field
/// </summary>
public class CalculationRule
{
    public string TargetField { get; set; } = string.Empty;
    public CalculationType Type { get; set; }
    public List<string> SourceFields { get; set; } = new();
    public string? CustomExpression { get; set; }
    public int DecimalPlaces { get; set; } = 2;
    public string? FormatString { get; set; }
}

/// <summary>
/// Types of calculations supported
/// </summary>
public enum CalculationType
{
    Sum,
    Product,
    Average,
    Min,
    Max,
    Count,
    Concatenate,
    Custom
}

/// <summary>
/// Service for managing calculated form fields in PDFs.
/// Evaluates calculation rules against form field values and updates target fields.
/// </summary>
public class CalculationFieldService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly List<CalculationRule> _rules = new();

    /// <summary>
    /// All registered calculation rules
    /// </summary>
    public IReadOnlyList<CalculationRule> Rules => _rules.AsReadOnly();

    /// <summary>
    /// Adds a calculation rule
    /// </summary>
    public void AddRule(CalculationRule rule)
    {
        _rules.Add(rule);
        Log.Debug("Added calculation rule: {Target} = {Type}({Sources})",
            rule.TargetField, rule.Type, string.Join(", ", rule.SourceFields));
    }

    /// <summary>
    /// Removes all rules for a target field
    /// </summary>
    public void RemoveRules(string targetField)
    {
        int removed = _rules.RemoveAll(r => r.TargetField == targetField);
        Log.Debug("Removed {Count} rules for field '{Field}'", removed, targetField);
    }

    /// <summary>
    /// Clears all rules
    /// </summary>
    public void ClearRules() => _rules.Clear();

    /// <summary>
    /// Evaluates all rules against current form values and returns calculated values
    /// </summary>
    public Dictionary<string, string> Evaluate(Dictionary<string, string> currentValues)
    {
        Log.Info("Evaluating {Count} calculation rules", _rules.Count);
        var results = new Dictionary<string, string>();

        foreach (var rule in _rules)
        {
            try
            {
                var sourceValues = rule.SourceFields
                    .Where(f => currentValues.ContainsKey(f))
                    .Select(f => currentValues[f])
                    .ToList();

                string result = EvaluateRule(rule, sourceValues);
                results[rule.TargetField] = result;
                Log.Debug("Calculated {Field} = {Value}", rule.TargetField, result);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to evaluate rule for field '{Field}'", rule.TargetField);
                results[rule.TargetField] = "ERROR";
            }
        }

        return results;
    }

    /// <summary>
    /// Evaluates rules and applies calculated values to the PDF form
    /// </summary>
    public byte[] EvaluateAndApply(byte[] pdfBytes)
    {
        Log.Info("Evaluating and applying calculations to PDF form");

        var outMs = new MemoryStream();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var writer = new PdfWriter(outMs);
        using var doc = new PdfDocument(reader, writer);

        var form = PdfAcroForm.GetAcroForm(doc, false);
        if (form == null)
        {
            Log.Warn("PDF has no form fields");
            doc.Close();
            return outMs.ToArray();
        }

        // Get current values
        var fields = form.GetFormFields();
        var currentValues = new Dictionary<string, string>();
        foreach (var kvp in fields)
        {
            currentValues[kvp.Key] = kvp.Value.GetValueAsString() ?? string.Empty;
        }

        // Calculate
        var results = Evaluate(currentValues);

        // Apply
        foreach (var kvp in results)
        {
            if (fields.ContainsKey(kvp.Key))
            {
                fields[kvp.Key].SetValue(kvp.Value);
                Log.Debug("Applied: {Field} = {Value}", kvp.Key, kvp.Value);
            }
        }

        doc.Close();
        return outMs.ToArray();
    }

    /// <summary>
    /// Exports calculation rules to JSON
    /// </summary>
    public string ExportRules()
    {
        return JsonConvert.SerializeObject(_rules, Formatting.Indented);
    }

    /// <summary>
    /// Imports calculation rules from JSON
    /// </summary>
    public void ImportRules(string json)
    {
        var rules = JsonConvert.DeserializeObject<List<CalculationRule>>(json);
        if (rules != null)
        {
            _rules.Clear();
            _rules.AddRange(rules);
            Log.Info("Imported {Count} calculation rules", rules.Count);
        }
    }

    private string EvaluateRule(CalculationRule rule, List<string> sourceValues)
    {
        var numbers = sourceValues
            .Select(v => double.TryParse(v, out double n) ? n : (double?)null)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .ToList();

        return rule.Type switch
        {
            CalculationType.Sum => FormatResult(numbers.Sum(), rule),
            CalculationType.Product => FormatResult(numbers.Aggregate(1.0, (a, b) => a * b), rule),
            CalculationType.Average => FormatResult(numbers.Any() ? numbers.Average() : 0, rule),
            CalculationType.Min => FormatResult(numbers.Any() ? numbers.Min() : 0, rule),
            CalculationType.Max => FormatResult(numbers.Any() ? numbers.Max() : 0, rule),
            CalculationType.Count => numbers.Count.ToString(),
            CalculationType.Concatenate => string.Join(" ", sourceValues),
            CalculationType.Custom => EvaluateCustom(rule.CustomExpression, sourceValues),
            _ => "0"
        };
    }

    private string FormatResult(double value, CalculationRule rule)
    {
        if (!string.IsNullOrEmpty(rule.FormatString))
            return value.ToString(rule.FormatString, System.Globalization.CultureInfo.InvariantCulture);
        return value.ToString($"F{rule.DecimalPlaces}", System.Globalization.CultureInfo.InvariantCulture);
    }

    private string EvaluateCustom(string? expression, List<string> values)
    {
        if (string.IsNullOrEmpty(expression))
            return "0";

        // Simple expression evaluator: supports {0}, {1}, etc. as placeholders
        string result = expression;
        for (int i = 0; i < values.Count; i++)
        {
            result = result.Replace($"{{{i}}}", values[i]);
        }

        // Try to evaluate simple arithmetic
        try
        {
            // Very basic: handle "a + b", "a - b", "a * b", "a / b"
            if (double.TryParse(result, out double directValue))
                return directValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { }

        return result;
    }
}
