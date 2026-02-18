using System.Text.RegularExpressions;
using NLog;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Core.Services;

/// <summary>
/// Defines a validation rule that can be applied to a form field.
/// </summary>
public class FormValidationRule
{
    /// <summary>
    /// Unique identifier for this rule.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Name of the form field this rule applies to.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Type of validation to perform.
    /// </summary>
    public ValidationRuleType RuleType { get; set; }

    /// <summary>
    /// Parameter for the rule (e.g., regex pattern, min/max value, min/max length).
    /// </summary>
    public string? Parameter { get; set; }

    /// <summary>
    /// Second parameter for range-type rules (e.g., max value, max length).
    /// </summary>
    public string? Parameter2 { get; set; }

    /// <summary>
    /// Custom error message to display when validation fails.
    /// </summary>
    public string ErrorMessage { get; set; } = "Validation failed.";

    /// <summary>
    /// Whether this rule is currently active.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Types of validation rules supported.
/// </summary>
public enum ValidationRuleType
{
    /// <summary>Field must not be empty.</summary>
    Required,

    /// <summary>Field value must match a regex pattern (Parameter = pattern).</summary>
    Regex,

    /// <summary>Field value length must be at least Parameter characters.</summary>
    MinLength,

    /// <summary>Field value length must be at most Parameter characters.</summary>
    MaxLength,

    /// <summary>Field value must be a valid email address.</summary>
    Email,

    /// <summary>Field value must be numeric.</summary>
    Numeric,

    /// <summary>Field value must be >= Parameter (numeric).</summary>
    MinValue,

    /// <summary>Field value must be &lt;= Parameter (numeric).</summary>
    MaxValue,

    /// <summary>Field value must be between Parameter and Parameter2 (numeric range).</summary>
    Range,

    /// <summary>Field value must match a date format (Parameter = format string).</summary>
    DateFormat,

    /// <summary>Field value must be a valid URL.</summary>
    Url,

    /// <summary>Field value must match another field's value (Parameter = other field name).</summary>
    MatchField,

    /// <summary>Custom validation via delegate (for programmatic use).</summary>
    Custom
}

/// <summary>
/// Result of validating a single field against one rule.
/// </summary>
public class FieldValidationResult
{
    public string FieldName { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public ValidationRuleType RuleType { get; set; }
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string FieldValue { get; set; } = string.Empty;
}

/// <summary>
/// Result of validating all fields in a form.
/// </summary>
public class FormValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<FieldValidationResult> Errors { get; set; } = new();
    public List<FieldValidationResult> AllResults { get; set; } = new();
    public int TotalFieldsValidated { get; set; }
    public int TotalRulesChecked { get; set; }

    /// <summary>
    /// Gets error messages grouped by field name.
    /// </summary>
    public Dictionary<string, List<string>> GetErrorsByField()
    {
        return Errors
            .GroupBy(e => e.FieldName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToList());
    }

    /// <summary>
    /// Returns a human-readable summary of validation errors.
    /// </summary>
    public string GetSummary()
    {
        if (IsValid)
            return "All fields are valid.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Validation failed: {Errors.Count} error(s) in {GetErrorsByField().Count} field(s).");
        foreach (var (field, messages) in GetErrorsByField())
        {
            sb.AppendLine($"  {field}:");
            foreach (var msg in messages)
                sb.AppendLine($"    - {msg}");
        }
        return sb.ToString();
    }
}

/// <summary>
/// Service for defining and evaluating validation rules on PDF form fields.
/// Supports predefined rule types (required, regex, numeric, email, etc.)
/// and can validate form data against a rule set.
/// </summary>
public class FormValidationService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly List<FormValidationRule> _rules = new();
    private readonly Dictionary<string, Func<string, bool>> _customValidators = new();

    /// <summary>
    /// Gets the current set of validation rules.
    /// </summary>
    public IReadOnlyList<FormValidationRule> Rules => _rules.AsReadOnly();

    /// <summary>
    /// Adds a validation rule.
    /// </summary>
    public void AddRule(FormValidationRule rule)
    {
        _rules.Add(rule);
        Log.Debug("Added validation rule: {RuleType} for field '{Field}'", rule.RuleType, rule.FieldName);
    }

    /// <summary>
    /// Adds a "required" rule for a field.
    /// </summary>
    public void AddRequiredRule(string fieldName, string? errorMessage = null)
    {
        AddRule(new FormValidationRule
        {
            FieldName = fieldName,
            RuleType = ValidationRuleType.Required,
            ErrorMessage = errorMessage ?? $"'{fieldName}' is required."
        });
    }

    /// <summary>
    /// Adds a regex pattern rule for a field.
    /// </summary>
    public void AddRegexRule(string fieldName, string pattern, string? errorMessage = null)
    {
        AddRule(new FormValidationRule
        {
            FieldName = fieldName,
            RuleType = ValidationRuleType.Regex,
            Parameter = pattern,
            ErrorMessage = errorMessage ?? $"'{fieldName}' does not match the expected pattern."
        });
    }

    /// <summary>
    /// Adds length constraints for a field.
    /// </summary>
    public void AddLengthRule(string fieldName, int? minLength = null, int? maxLength = null,
        string? errorMessage = null)
    {
        if (minLength.HasValue)
        {
            AddRule(new FormValidationRule
            {
                FieldName = fieldName,
                RuleType = ValidationRuleType.MinLength,
                Parameter = minLength.Value.ToString(),
                ErrorMessage = errorMessage ?? $"'{fieldName}' must be at least {minLength} characters."
            });
        }

        if (maxLength.HasValue)
        {
            AddRule(new FormValidationRule
            {
                FieldName = fieldName,
                RuleType = ValidationRuleType.MaxLength,
                Parameter = maxLength.Value.ToString(),
                ErrorMessage = errorMessage ?? $"'{fieldName}' must be at most {maxLength} characters."
            });
        }
    }

    /// <summary>
    /// Adds an email validation rule for a field.
    /// </summary>
    public void AddEmailRule(string fieldName, string? errorMessage = null)
    {
        AddRule(new FormValidationRule
        {
            FieldName = fieldName,
            RuleType = ValidationRuleType.Email,
            ErrorMessage = errorMessage ?? $"'{fieldName}' must be a valid email address."
        });
    }

    /// <summary>
    /// Adds a numeric validation rule for a field.
    /// </summary>
    public void AddNumericRule(string fieldName, double? min = null, double? max = null,
        string? errorMessage = null)
    {
        AddRule(new FormValidationRule
        {
            FieldName = fieldName,
            RuleType = ValidationRuleType.Numeric,
            ErrorMessage = errorMessage ?? $"'{fieldName}' must be a number."
        });

        if (min.HasValue)
        {
            AddRule(new FormValidationRule
            {
                FieldName = fieldName,
                RuleType = ValidationRuleType.MinValue,
                Parameter = min.Value.ToString(),
                ErrorMessage = errorMessage ?? $"'{fieldName}' must be at least {min}."
            });
        }

        if (max.HasValue)
        {
            AddRule(new FormValidationRule
            {
                FieldName = fieldName,
                RuleType = ValidationRuleType.MaxValue,
                Parameter = max.Value.ToString(),
                ErrorMessage = errorMessage ?? $"'{fieldName}' must be at most {max}."
            });
        }
    }

    /// <summary>
    /// Adds a numeric range rule for a field.
    /// </summary>
    public void AddRangeRule(string fieldName, double min, double max, string? errorMessage = null)
    {
        AddRule(new FormValidationRule
        {
            FieldName = fieldName,
            RuleType = ValidationRuleType.Range,
            Parameter = min.ToString(),
            Parameter2 = max.ToString(),
            ErrorMessage = errorMessage ?? $"'{fieldName}' must be between {min} and {max}."
        });
    }

    /// <summary>
    /// Adds a URL validation rule for a field.
    /// </summary>
    public void AddUrlRule(string fieldName, string? errorMessage = null)
    {
        AddRule(new FormValidationRule
        {
            FieldName = fieldName,
            RuleType = ValidationRuleType.Url,
            ErrorMessage = errorMessage ?? $"'{fieldName}' must be a valid URL."
        });
    }

    /// <summary>
    /// Adds a date format validation rule for a field.
    /// </summary>
    public void AddDateRule(string fieldName, string format = "yyyy-MM-dd", string? errorMessage = null)
    {
        AddRule(new FormValidationRule
        {
            FieldName = fieldName,
            RuleType = ValidationRuleType.DateFormat,
            Parameter = format,
            ErrorMessage = errorMessage ?? $"'{fieldName}' must be a valid date in '{format}' format."
        });
    }

    /// <summary>
    /// Adds a cross-field matching rule (e.g., confirm password).
    /// </summary>
    public void AddMatchFieldRule(string fieldName, string otherFieldName, string? errorMessage = null)
    {
        AddRule(new FormValidationRule
        {
            FieldName = fieldName,
            RuleType = ValidationRuleType.MatchField,
            Parameter = otherFieldName,
            ErrorMessage = errorMessage ?? $"'{fieldName}' must match '{otherFieldName}'."
        });
    }

    /// <summary>
    /// Registers a custom validator function for a field.
    /// </summary>
    public void AddCustomValidator(string fieldName, Func<string, bool> validator, string? errorMessage = null)
    {
        var ruleId = Guid.NewGuid().ToString("N");
        _customValidators[ruleId] = validator;
        AddRule(new FormValidationRule
        {
            Id = ruleId,
            FieldName = fieldName,
            RuleType = ValidationRuleType.Custom,
            ErrorMessage = errorMessage ?? $"'{fieldName}' failed custom validation."
        });
    }

    /// <summary>
    /// Removes all rules for a specific field.
    /// </summary>
    public void RemoveRulesForField(string fieldName)
    {
        var removed = _rules.RemoveAll(r => r.FieldName == fieldName);
        Log.Debug("Removed {Count} rules for field '{Field}'", removed, fieldName);
    }

    /// <summary>
    /// Removes a specific rule by ID.
    /// </summary>
    public void RemoveRule(string ruleId)
    {
        _rules.RemoveAll(r => r.Id == ruleId);
    }

    /// <summary>
    /// Clears all validation rules.
    /// </summary>
    public void ClearRules()
    {
        _rules.Clear();
        _customValidators.Clear();
    }

    /// <summary>
    /// Validates a form's field values against all configured rules.
    /// </summary>
    /// <param name="fieldValues">Dictionary of field name → field value</param>
    /// <returns>Validation result with errors if any</returns>
    public FormValidationResult Validate(Dictionary<string, string> fieldValues)
    {
        var result = new FormValidationResult();
        var activeRules = _rules.Where(r => r.IsEnabled).ToList();
        result.TotalRulesChecked = activeRules.Count;
        result.TotalFieldsValidated = activeRules.Select(r => r.FieldName).Distinct().Count();

        foreach (var rule in activeRules)
        {
            fieldValues.TryGetValue(rule.FieldName, out var value);
            value ??= string.Empty;

            var fieldResult = ValidateField(rule, value, fieldValues);
            result.AllResults.Add(fieldResult);
            if (!fieldResult.IsValid)
                result.Errors.Add(fieldResult);
        }

        Log.Info("Form validation complete: {Valid}, {ErrorCount} error(s)",
            result.IsValid ? "PASSED" : "FAILED", result.Errors.Count);
        return result;
    }

    /// <summary>
    /// Validates form fields from a PDF document directly using configured rules.
    /// </summary>
    /// <param name="formService">The form service to extract field values</param>
    /// <param name="pdfBytes">PDF file data</param>
    /// <returns>Validation result</returns>
    public FormValidationResult ValidateForm(IFormService formService, byte[] pdfBytes)
    {
        var formData = formService.ExportFormData(pdfBytes);
        if (!formData.Success)
        {
            return new FormValidationResult
            {
                Errors = { new FieldValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Could not read form data: {formData.ErrorMessage}"
                }}
            };
        }

        return Validate(formData.FieldValues);
    }

    /// <summary>
    /// Auto-generates validation rules from form field properties.
    /// Creates "Required" rules for required fields.
    /// </summary>
    public void AutoGenerateRules(List<FormFieldInfo> fields)
    {
        foreach (var field in fields)
        {
            if (field.IsRequired)
            {
                AddRequiredRule(field.Name, $"'{field.Name}' is required.");
            }
        }

        Log.Info("Auto-generated {Count} validation rules from {FieldCount} fields",
            _rules.Count, fields.Count);
    }

    /// <summary>
    /// Exports current rules to a JSON-serializable list.
    /// </summary>
    public List<FormValidationRule> ExportRules() => new(_rules);

    /// <summary>
    /// Imports rules from a list, replacing current rules.
    /// </summary>
    public void ImportRules(List<FormValidationRule> rules)
    {
        _rules.Clear();
        _rules.AddRange(rules);
        Log.Info("Imported {Count} validation rules", rules.Count);
    }

    #region Private Validation Logic

    private FieldValidationResult ValidateField(FormValidationRule rule, string value,
        Dictionary<string, string> allValues)
    {
        var result = new FieldValidationResult
        {
            FieldName = rule.FieldName,
            RuleId = rule.Id,
            RuleType = rule.RuleType,
            FieldValue = value,
            ErrorMessage = rule.ErrorMessage
        };

        result.IsValid = rule.RuleType switch
        {
            ValidationRuleType.Required => !string.IsNullOrWhiteSpace(value),
            ValidationRuleType.Regex => ValidateRegex(value, rule.Parameter),
            ValidationRuleType.MinLength => ValidateMinLength(value, rule.Parameter),
            ValidationRuleType.MaxLength => ValidateMaxLength(value, rule.Parameter),
            ValidationRuleType.Email => ValidateEmail(value),
            ValidationRuleType.Numeric => ValidateNumeric(value),
            ValidationRuleType.MinValue => ValidateMinValue(value, rule.Parameter),
            ValidationRuleType.MaxValue => ValidateMaxValue(value, rule.Parameter),
            ValidationRuleType.Range => ValidateRange(value, rule.Parameter, rule.Parameter2),
            ValidationRuleType.DateFormat => ValidateDateFormat(value, rule.Parameter),
            ValidationRuleType.Url => ValidateUrl(value),
            ValidationRuleType.MatchField => ValidateMatchField(value, rule.Parameter, allValues),
            ValidationRuleType.Custom => ValidateCustom(rule.Id, value),
            _ => true
        };

        return result;
    }

    private static bool ValidateRegex(string value, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return true;
        if (string.IsNullOrEmpty(value)) return true; // regex is about pattern, not required
        try { return Regex.IsMatch(value, pattern); }
        catch { return false; }
    }

    private static bool ValidateMinLength(string value, string? param)
    {
        if (!int.TryParse(param, out int min)) return true;
        return value.Length >= min;
    }

    private static bool ValidateMaxLength(string value, string? param)
    {
        if (!int.TryParse(param, out int max)) return true;
        return value.Length <= max;
    }

    private static bool ValidateEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true; // use Required for empty check
        return Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    }

    private static bool ValidateNumeric(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return double.TryParse(value, out _);
    }

    private static bool ValidateMinValue(string value, string? param)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!double.TryParse(value, out double val)) return false;
        if (!double.TryParse(param, out double min)) return true;
        return val >= min;
    }

    private static bool ValidateMaxValue(string value, string? param)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!double.TryParse(value, out double val)) return false;
        if (!double.TryParse(param, out double max)) return true;
        return val <= max;
    }

    private static bool ValidateRange(string value, string? minParam, string? maxParam)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!double.TryParse(value, out double val)) return false;
        if (!double.TryParse(minParam, out double min) || !double.TryParse(maxParam, out double max))
            return true;
        return val >= min && val <= max;
    }

    private static bool ValidateDateFormat(string value, string? format)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        format ??= "yyyy-MM-dd";
        return DateTime.TryParseExact(value, format, null,
            System.Globalization.DateTimeStyles.None, out _);
    }

    private static bool ValidateUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool ValidateMatchField(string value, string? otherFieldName,
        Dictionary<string, string> allValues)
    {
        if (string.IsNullOrEmpty(otherFieldName)) return true;
        allValues.TryGetValue(otherFieldName, out var otherValue);
        return value == (otherValue ?? string.Empty);
    }

    private bool ValidateCustom(string ruleId, string value)
    {
        if (_customValidators.TryGetValue(ruleId, out var validator))
        {
            try { return validator(value); }
            catch { return false; }
        }
        return true;
    }

    #endregion
}
