using PDFEditor.Core.Services;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for FormValidationService (rule-based form field validation)
/// </summary>
public class FormValidationServiceTests
{
    private readonly FormValidationService _service = new();

    #region Required Rule Tests

    [Fact]
    public void Validate_RequiredField_Empty_Fails()
    {
        _service.AddRequiredRule("name");
        var result = _service.Validate(new Dictionary<string, string> { { "name", "" } });
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("name", result.Errors[0].FieldName);
    }

    [Fact]
    public void Validate_RequiredField_HasValue_Passes()
    {
        _service.AddRequiredRule("name");
        var result = _service.Validate(new Dictionary<string, string> { { "name", "John" } });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RequiredField_Missing_Fails()
    {
        _service.AddRequiredRule("email");
        var result = _service.Validate(new Dictionary<string, string> { { "name", "John" } });
        Assert.False(result.IsValid);
    }

    #endregion

    #region Regex Rule Tests

    [Fact]
    public void Validate_RegexRule_Matches_Passes()
    {
        _service.AddRegexRule("zip", @"^\d{5}$");
        var result = _service.Validate(new Dictionary<string, string> { { "zip", "12345" } });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RegexRule_NoMatch_Fails()
    {
        _service.AddRegexRule("zip", @"^\d{5}$");
        var result = _service.Validate(new Dictionary<string, string> { { "zip", "abc" } });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RegexRule_EmptyValue_Passes()
    {
        // Regex alone doesn't enforce required
        _service.AddRegexRule("zip", @"^\d{5}$");
        var result = _service.Validate(new Dictionary<string, string> { { "zip", "" } });
        Assert.True(result.IsValid);
    }

    #endregion

    #region Length Rule Tests

    [Fact]
    public void Validate_MinLength_TooShort_Fails()
    {
        _service.AddLengthRule("password", minLength: 8);
        var result = _service.Validate(new Dictionary<string, string> { { "password", "abc" } });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_MaxLength_TooLong_Fails()
    {
        _service.AddLengthRule("code", maxLength: 5);
        var result = _service.Validate(new Dictionary<string, string> { { "code", "123456" } });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_LengthInRange_Passes()
    {
        _service.AddLengthRule("code", minLength: 3, maxLength: 10);
        var result = _service.Validate(new Dictionary<string, string> { { "code", "ABCDE" } });
        Assert.True(result.IsValid);
    }

    #endregion

    #region Email Rule Tests

    [Fact]
    public void Validate_Email_Valid_Passes()
    {
        _service.AddEmailRule("email");
        var result = _service.Validate(new Dictionary<string, string> { { "email", "user@example.com" } });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Email_Invalid_Fails()
    {
        _service.AddEmailRule("email");
        var result = _service.Validate(new Dictionary<string, string> { { "email", "not-an-email" } });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Email_Empty_Passes()
    {
        // Empty doesn't fail email check (use Required for that)
        _service.AddEmailRule("email");
        var result = _service.Validate(new Dictionary<string, string> { { "email", "" } });
        Assert.True(result.IsValid);
    }

    #endregion

    #region Numeric Rule Tests

    [Fact]
    public void Validate_Numeric_ValidNumber_Passes()
    {
        _service.AddNumericRule("age");
        var result = _service.Validate(new Dictionary<string, string> { { "age", "25" } });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Numeric_NonNumeric_Fails()
    {
        _service.AddNumericRule("age");
        var result = _service.Validate(new Dictionary<string, string> { { "age", "twenty" } });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NumericRange_InRange_Passes()
    {
        _service.AddRangeRule("score", 0, 100);
        var result = _service.Validate(new Dictionary<string, string> { { "score", "75" } });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NumericRange_BelowMin_Fails()
    {
        _service.AddRangeRule("score", 0, 100);
        var result = _service.Validate(new Dictionary<string, string> { { "score", "-5" } });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NumericRange_AboveMax_Fails()
    {
        _service.AddRangeRule("score", 0, 100);
        var result = _service.Validate(new Dictionary<string, string> { { "score", "150" } });
        Assert.False(result.IsValid);
    }

    #endregion

    #region URL Rule Tests

    [Fact]
    public void Validate_Url_Valid_Passes()
    {
        _service.AddUrlRule("website");
        var result = _service.Validate(new Dictionary<string, string> { { "website", "https://example.com" } });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Url_Invalid_Fails()
    {
        _service.AddUrlRule("website");
        var result = _service.Validate(new Dictionary<string, string> { { "website", "not a url" } });
        Assert.False(result.IsValid);
    }

    #endregion

    #region Date Rule Tests

    [Fact]
    public void Validate_Date_ValidFormat_Passes()
    {
        _service.AddDateRule("dob", "yyyy-MM-dd");
        var result = _service.Validate(new Dictionary<string, string> { { "dob", "2000-01-15" } });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Date_InvalidFormat_Fails()
    {
        _service.AddDateRule("dob", "yyyy-MM-dd");
        var result = _service.Validate(new Dictionary<string, string> { { "dob", "15/01/2000" } });
        Assert.False(result.IsValid);
    }

    #endregion

    #region MatchField Rule Tests

    [Fact]
    public void Validate_MatchField_SameValue_Passes()
    {
        _service.AddMatchFieldRule("confirm_email", "email");
        var fields = new Dictionary<string, string>
        {
            { "email", "user@example.com" },
            { "confirm_email", "user@example.com" }
        };
        var result = _service.Validate(fields);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MatchField_DifferentValue_Fails()
    {
        _service.AddMatchFieldRule("confirm_email", "email");
        var fields = new Dictionary<string, string>
        {
            { "email", "user@example.com" },
            { "confirm_email", "different@example.com" }
        };
        var result = _service.Validate(fields);
        Assert.False(result.IsValid);
    }

    #endregion

    #region Custom Validator Tests

    [Fact]
    public void Validate_CustomValidator_ReturnsTrue_Passes()
    {
        _service.AddCustomValidator("status", v => v == "active" || v == "inactive");
        var result = _service.Validate(new Dictionary<string, string> { { "status", "active" } });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_CustomValidator_ReturnsFalse_Fails()
    {
        _service.AddCustomValidator("status", v => v == "active" || v == "inactive");
        var result = _service.Validate(new Dictionary<string, string> { { "status", "unknown" } });
        Assert.False(result.IsValid);
    }

    #endregion

    #region Multiple Rules Tests

    [Fact]
    public void Validate_MultipleRules_AllPass()
    {
        _service.AddRequiredRule("name");
        _service.AddEmailRule("email");
        _service.AddRequiredRule("email");

        var fields = new Dictionary<string, string>
        {
            { "name", "John" },
            { "email", "john@example.com" }
        };
        var result = _service.Validate(fields);
        Assert.True(result.IsValid);
        Assert.Equal(3, result.TotalRulesChecked);
    }

    [Fact]
    public void Validate_MultipleRules_SomeFail()
    {
        _service.AddRequiredRule("name");
        _service.AddEmailRule("email");
        _service.AddRequiredRule("email");

        var fields = new Dictionary<string, string>
        {
            { "name", "" },
            { "email", "not-email" }
        };
        var result = _service.Validate(fields);
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count); // required(name), email(email)
    }

    #endregion

    #region Rule Management Tests

    [Fact]
    public void RemoveRulesForField_RemovesCorrectly()
    {
        _service.AddRequiredRule("name");
        _service.AddRequiredRule("email");
        _service.AddEmailRule("email");

        _service.RemoveRulesForField("email");
        Assert.Single(_service.Rules);
        Assert.Equal("name", _service.Rules[0].FieldName);
    }

    [Fact]
    public void ClearRules_RemovesAll()
    {
        _service.AddRequiredRule("a");
        _service.AddRequiredRule("b");
        _service.ClearRules();
        Assert.Empty(_service.Rules);
    }

    [Fact]
    public void ExportImportRules_RoundTrips()
    {
        _service.AddRequiredRule("name");
        _service.AddEmailRule("email");

        var exported = _service.ExportRules();
        Assert.Equal(2, exported.Count);

        var newService = new FormValidationService();
        newService.ImportRules(exported);
        Assert.Equal(2, newService.Rules.Count);
    }

    #endregion

    #region FormValidationResult Tests

    [Fact]
    public void GetErrorsByField_GroupsCorrectly()
    {
        _service.AddRequiredRule("name");
        _service.AddLengthRule("name", minLength: 3);

        var result = _service.Validate(new Dictionary<string, string> { { "name", "" } });
        var byField = result.GetErrorsByField();
        Assert.Single(byField); // both errors on "name"
        Assert.Equal(2, byField["name"].Count);
    }

    [Fact]
    public void GetSummary_ValidForm_ReturnsPassMessage()
    {
        _service.AddRequiredRule("name");
        var result = _service.Validate(new Dictionary<string, string> { { "name", "John" } });
        Assert.Contains("valid", result.GetSummary(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSummary_InvalidForm_ContainsErrors()
    {
        _service.AddRequiredRule("name");
        var result = _service.Validate(new Dictionary<string, string> { { "name", "" } });
        Assert.Contains("failed", result.GetSummary(), StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Disabled Rules Tests

    [Fact]
    public void Validate_DisabledRule_IsSkipped()
    {
        var rule = new FormValidationRule
        {
            FieldName = "name",
            RuleType = ValidationRuleType.Required,
            IsEnabled = false
        };
        _service.AddRule(rule);

        var result = _service.Validate(new Dictionary<string, string> { { "name", "" } });
        Assert.True(result.IsValid); // rule is disabled, so no error
    }

    #endregion
}
