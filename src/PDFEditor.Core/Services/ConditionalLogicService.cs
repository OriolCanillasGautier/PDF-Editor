using iText.Kernel.Pdf;
using iText.Forms;
using iText.Forms.Fields;
using NLog;
using Newtonsoft.Json;

namespace PDFEditor.Core.Services;

/// <summary>
/// Defines a conditional logic rule for showing/hiding or enabling/disabling form fields
/// </summary>
public class ConditionalRule
{
    public string TargetField { get; set; } = string.Empty;
    public ConditionalAction Action { get; set; } = ConditionalAction.Show;
    public List<Condition> Conditions { get; set; } = new();
    public LogicOperator Operator { get; set; } = LogicOperator.And;
}

/// <summary>
/// Represents a single condition in a conditional rule
/// </summary>
public class Condition
{
    public string FieldName { get; set; } = string.Empty;
    public ComparisonOperator Comparison { get; set; } = ComparisonOperator.Equals;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Actions that can be triggered by conditional logic
/// </summary>
public enum ConditionalAction
{
    Show,
    Hide,
    Enable,
    Disable,
    SetValue,
    SetRequired,
    SetReadOnly
}

/// <summary>
/// Comparison operators for conditions
/// </summary>
public enum ComparisonOperator
{
    Equals,
    NotEquals,
    Contains,
    NotContains,
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual,
    IsEmpty,
    IsNotEmpty,
    StartsWith,
    EndsWith
}

/// <summary>
/// Logical operators for combining conditions
/// </summary>
public enum LogicOperator
{
    And,
    Or
}

/// <summary>
/// Result of evaluating conditional logic
/// </summary>
public class ConditionalResult
{
    public string TargetField { get; set; } = string.Empty;
    public ConditionalAction Action { get; set; }
    public bool ConditionsMet { get; set; }
    public string? SetValueTo { get; set; }
}

/// <summary>
/// Service for managing conditional logic rules on PDF form fields.
/// Evaluates conditions based on field values and determines which fields
/// should be shown/hidden, enabled/disabled, or have values set.
/// </summary>
public class ConditionalLogicService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly List<ConditionalRule> _rules = new();

    /// <summary>
    /// All registered conditional rules
    /// </summary>
    public IReadOnlyList<ConditionalRule> Rules => _rules.AsReadOnly();

    /// <summary>
    /// Adds a conditional rule
    /// </summary>
    public void AddRule(ConditionalRule rule)
    {
        _rules.Add(rule);
        Log.Debug("Added conditional rule: {Action} '{Target}' when conditions met",
            rule.Action, rule.TargetField);
    }

    /// <summary>
    /// Removes all rules for a target field
    /// </summary>
    public void RemoveRules(string targetField)
    {
        int removed = _rules.RemoveAll(r => r.TargetField == targetField);
        Log.Debug("Removed {Count} conditional rules for field '{Field}'", removed, targetField);
    }

    /// <summary>
    /// Clears all rules
    /// </summary>
    public void ClearRules() => _rules.Clear();

    /// <summary>
    /// Evaluates all conditional rules against current form values
    /// </summary>
    public List<ConditionalResult> Evaluate(Dictionary<string, string> currentValues)
    {
        Log.Info("Evaluating {Count} conditional rules", _rules.Count);
        var results = new List<ConditionalResult>();

        foreach (var rule in _rules)
        {
            bool conditionsMet = EvaluateConditions(rule, currentValues);

            results.Add(new ConditionalResult
            {
                TargetField = rule.TargetField,
                Action = rule.Action,
                ConditionsMet = conditionsMet
            });

            Log.Debug("Rule for '{Target}': conditions {Met}",
                rule.TargetField, conditionsMet ? "MET" : "NOT MET");
        }

        return results;
    }

    /// <summary>
    /// Evaluates rules and applies actions to the PDF form
    /// </summary>
    public byte[] EvaluateAndApply(byte[] pdfBytes, Dictionary<string, string>? overrideValues = null)
    {
        Log.Info("Evaluating and applying conditional logic");

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

        var fields = form.GetFormFields();

        // Get current values
        var currentValues = overrideValues ?? new Dictionary<string, string>();
        if (overrideValues == null)
        {
            foreach (var kvp in fields)
                currentValues[kvp.Key] = kvp.Value.GetValueAsString() ?? string.Empty;
        }

        var results = Evaluate(currentValues);

        foreach (var result in results.Where(r => r.ConditionsMet))
        {
            if (!fields.ContainsKey(result.TargetField))
                continue;

            var field = fields[result.TargetField];
            switch (result.Action)
            {
                case ConditionalAction.Hide:
                    // Set hidden flag
                    field.SetVisibility(PdfFormField.HIDDEN);
                    Log.Debug("Hidden field '{Field}'", result.TargetField);
                    break;

                case ConditionalAction.Show:
                    field.SetVisibility(PdfFormField.VISIBLE);
                    Log.Debug("Shown field '{Field}'", result.TargetField);
                    break;

                case ConditionalAction.Disable:
                case ConditionalAction.SetReadOnly:
                    field.SetReadOnly(true);
                    Log.Debug("Disabled field '{Field}'", result.TargetField);
                    break;

                case ConditionalAction.Enable:
                    field.SetReadOnly(false);
                    Log.Debug("Enabled field '{Field}'", result.TargetField);
                    break;

                case ConditionalAction.SetRequired:
                    // Set required flag via widget annotation
                    break;

                case ConditionalAction.SetValue:
                    if (result.SetValueTo != null)
                    {
                        field.SetValue(result.SetValueTo);
                        Log.Debug("Set field '{Field}' value to '{Value}'", result.TargetField, result.SetValueTo);
                    }
                    break;
            }
        }

        doc.Close();
        return outMs.ToArray();
    }

    /// <summary>
    /// Exports conditional rules to JSON
    /// </summary>
    public string ExportRules()
    {
        return JsonConvert.SerializeObject(_rules, Formatting.Indented);
    }

    /// <summary>
    /// Imports conditional rules from JSON
    /// </summary>
    public void ImportRules(string json)
    {
        var rules = JsonConvert.DeserializeObject<List<ConditionalRule>>(json);
        if (rules != null)
        {
            _rules.Clear();
            _rules.AddRange(rules);
            Log.Info("Imported {Count} conditional rules", rules.Count);
        }
    }

    private bool EvaluateConditions(ConditionalRule rule, Dictionary<string, string> values)
    {
        if (!rule.Conditions.Any())
            return false;

        var conditionResults = rule.Conditions.Select(c => EvaluateCondition(c, values));

        return rule.Operator switch
        {
            LogicOperator.And => conditionResults.All(r => r),
            LogicOperator.Or => conditionResults.Any(r => r),
            _ => false
        };
    }

    private bool EvaluateCondition(Condition condition, Dictionary<string, string> values)
    {
        string fieldValue = values.GetValueOrDefault(condition.FieldName, string.Empty);
        string compareValue = condition.Value;

        return condition.Comparison switch
        {
            ComparisonOperator.Equals => fieldValue.Equals(compareValue, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.NotEquals => !fieldValue.Equals(compareValue, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.Contains => fieldValue.Contains(compareValue, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.NotContains => !fieldValue.Contains(compareValue, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.IsEmpty => string.IsNullOrEmpty(fieldValue),
            ComparisonOperator.IsNotEmpty => !string.IsNullOrEmpty(fieldValue),
            ComparisonOperator.StartsWith => fieldValue.StartsWith(compareValue, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.EndsWith => fieldValue.EndsWith(compareValue, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.GreaterThan => double.TryParse(fieldValue, out var a) && double.TryParse(compareValue, out var b) && a > b,
            ComparisonOperator.LessThan => double.TryParse(fieldValue, out var c) && double.TryParse(compareValue, out var d) && c < d,
            ComparisonOperator.GreaterOrEqual => double.TryParse(fieldValue, out var e) && double.TryParse(compareValue, out var f) && e >= f,
            ComparisonOperator.LessOrEqual => double.TryParse(fieldValue, out var g) && double.TryParse(compareValue, out var h) && g <= h,
            _ => false
        };
    }
}
