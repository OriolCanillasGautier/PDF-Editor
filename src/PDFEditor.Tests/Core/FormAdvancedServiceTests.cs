using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for CalculationFieldService and ConditionalLogicService
/// </summary>
public class FormAdvancedServiceTests
{
    // ===== CalculationFieldService Tests =====

    [Fact]
    public void Calculation_AddRule_StoresRule()
    {
        var service = new CalculationFieldService();
        var rule = new CalculationRule
        {
            TargetField = "total",
            Type = CalculationType.Sum,
            SourceFields = new List<string> { "item1", "item2", "item3" }
        };

        service.AddRule(rule);
        var rules = service.Rules;

        Assert.Single(rules);
        Assert.Equal("total", rules[0].TargetField);
    }

    [Fact]
    public void Calculation_Evaluate_Sum()
    {
        var service = new CalculationFieldService();
        service.AddRule(new CalculationRule
        {
            TargetField = "total",
            Type = CalculationType.Sum,
            SourceFields = new List<string> { "a", "b" }
        });

        var values = new Dictionary<string, string> { { "a", "10" }, { "b", "20" } };
        var results = service.Evaluate(values);

        Assert.Single(results);
        Assert.Equal("30.00", results["total"]);
    }

    [Fact]
    public void Calculation_Evaluate_Average()
    {
        var service = new CalculationFieldService();
        service.AddRule(new CalculationRule
        {
            TargetField = "avg",
            Type = CalculationType.Average,
            SourceFields = new List<string> { "x", "y", "z" }
        });

        var values = new Dictionary<string, string> { { "x", "10" }, { "y", "20" }, { "z", "30" } };
        var results = service.Evaluate(values);

        Assert.Equal("20.00", results["avg"]);
    }

    [Fact]
    public void Calculation_Evaluate_Product()
    {
        var service = new CalculationFieldService();
        service.AddRule(new CalculationRule
        {
            TargetField = "prod",
            Type = CalculationType.Product,
            SourceFields = new List<string> { "a", "b" }
        });

        var values = new Dictionary<string, string> { { "a", "3" }, { "b", "7" } };
        var results = service.Evaluate(values);

        Assert.Equal("21.00", results["prod"]);
    }

    [Fact]
    public void Calculation_Evaluate_MinMax()
    {
        var service = new CalculationFieldService();
        service.AddRule(new CalculationRule
        {
            TargetField = "min",
            Type = CalculationType.Min,
            SourceFields = new List<string> { "a", "b", "c" }
        });
        service.AddRule(new CalculationRule
        {
            TargetField = "max",
            Type = CalculationType.Max,
            SourceFields = new List<string> { "a", "b", "c" }
        });

        var values = new Dictionary<string, string> { { "a", "5" }, { "b", "2" }, { "c", "8" } };
        var results = service.Evaluate(values);

        Assert.Equal("2.00", results["min"]);
        Assert.Equal("8.00", results["max"]);
    }

    [Fact]
    public void Calculation_Evaluate_Count()
    {
        var service = new CalculationFieldService();
        service.AddRule(new CalculationRule
        {
            TargetField = "count",
            Type = CalculationType.Count,
            SourceFields = new List<string> { "a", "b", "c" }
        });

        var values = new Dictionary<string, string> { { "a", "10" }, { "b", "0" }, { "c", "5" } };
        var results = service.Evaluate(values);

        Assert.Equal("3", results["count"]); // Count returns count of parseable numeric values
    }

    [Fact]
    public void Calculation_Evaluate_Concatenate()
    {
        var service = new CalculationFieldService();
        service.AddRule(new CalculationRule
        {
            TargetField = "full",
            Type = CalculationType.Concatenate,
            SourceFields = new List<string> { "first", "last" }
        });

        var values = new Dictionary<string, string> { { "first", "John" }, { "last", "Doe" } };
        var results = service.Evaluate(values);

        Assert.Equal("John Doe", results["full"]);
    }

    [Fact]
    public void Calculation_RemoveRule_Works()
    {
        var service = new CalculationFieldService();
        service.AddRule(new CalculationRule { TargetField = "a", Type = CalculationType.Sum, SourceFields = new List<string> { "x" } });
        service.AddRule(new CalculationRule { TargetField = "b", Type = CalculationType.Sum, SourceFields = new List<string> { "y" } });

        service.RemoveRules("a");

        Assert.Single(service.Rules);
    }

    [Fact]
    public void Calculation_ExportImport_RoundTrips()
    {
        var service = new CalculationFieldService();
        service.AddRule(new CalculationRule
        {
            TargetField = "total",
            Type = CalculationType.Sum,
            SourceFields = new List<string> { "a", "b" }
        });

        var json = service.ExportRules();
        var service2 = new CalculationFieldService();
        service2.ImportRules(json);

        Assert.Single(service2.Rules);
        Assert.Equal("total", service2.Rules[0].TargetField);
    }

    // ===== ConditionalLogicService Tests =====

    [Fact]
    public void Conditional_AddRule_StoresRule()
    {
        var service = new ConditionalLogicService();
        var rule = new ConditionalRule
        {
            TargetField = "address",
            Action = ConditionalAction.Show,
            Conditions = new List<Condition>
            {
                new() { FieldName = "needsAddress", Comparison = ComparisonOperator.Equals, Value = "yes" }
            }
        };

        service.AddRule(rule);
        Assert.Single(service.Rules);
    }

    [Fact]
    public void Conditional_Evaluate_Equals_TrueCondition()
    {
        var service = new ConditionalLogicService();
        service.AddRule(new ConditionalRule
        {
            TargetField = "details",
            Action = ConditionalAction.Show,
            Conditions = new List<Condition>
            {
                new() { FieldName = "status", Comparison = ComparisonOperator.Equals, Value = "active" }
            }
        });

        var values = new Dictionary<string, string> { { "status", "active" } };
        var results = service.Evaluate(values);

        Assert.Single(results);
        Assert.True(results[0].ConditionsMet);
    }

    [Fact]
    public void Conditional_Evaluate_NotEquals_FalseCondition()
    {
        var service = new ConditionalLogicService();
        service.AddRule(new ConditionalRule
        {
            TargetField = "details",
            Action = ConditionalAction.Show,
            Conditions = new List<Condition>
            {
                new() { FieldName = "status", Comparison = ComparisonOperator.Equals, Value = "active" }
            }
        });

        var values = new Dictionary<string, string> { { "status", "inactive" } };
        var results = service.Evaluate(values);

        Assert.Single(results);
        Assert.False(results[0].ConditionsMet);
    }

    [Fact]
    public void Conditional_Evaluate_GreaterThan()
    {
        var service = new ConditionalLogicService();
        service.AddRule(new ConditionalRule
        {
            TargetField = "approval",
            Action = ConditionalAction.SetRequired,
            Conditions = new List<Condition>
            {
                new() { FieldName = "amount", Comparison = ComparisonOperator.GreaterThan, Value = "100" }
            }
        });

        var values = new Dictionary<string, string> { { "amount", "150" } };
        var results = service.Evaluate(values);

        Assert.True(results[0].ConditionsMet);
    }

    [Fact]
    public void Conditional_Evaluate_Contains()
    {
        var service = new ConditionalLogicService();
        service.AddRule(new ConditionalRule
        {
            TargetField = "emailConfirm",
            Action = ConditionalAction.Show,
            Conditions = new List<Condition>
            {
                new() { FieldName = "email", Comparison = ComparisonOperator.Contains, Value = "@" }
            }
        });

        var values = new Dictionary<string, string> { { "email", "test@example.com" } };
        var results = service.Evaluate(values);

        Assert.True(results[0].ConditionsMet);
    }

    [Fact]
    public void Conditional_Evaluate_IsEmpty()
    {
        var service = new ConditionalLogicService();
        service.AddRule(new ConditionalRule
        {
            TargetField = "greeting",
            Action = ConditionalAction.Hide,
            Conditions = new List<Condition>
            {
                new() { FieldName = "name", Comparison = ComparisonOperator.IsEmpty }
            }
        });

        var values = new Dictionary<string, string> { { "name", "" } };
        var results = service.Evaluate(values);

        Assert.True(results[0].ConditionsMet);
    }

    [Fact]
    public void Conditional_ExportImport_RoundTrips()
    {
        var service = new ConditionalLogicService();
        service.AddRule(new ConditionalRule
        {
            TargetField = "z",
            Action = ConditionalAction.Show,
            Conditions = new List<Condition>
            {
                new() { FieldName = "x", Comparison = ComparisonOperator.Equals, Value = "y" }
            }
        });

        var json = service.ExportRules();
        var service2 = new ConditionalLogicService();
        service2.ImportRules(json);

        Assert.Single(service2.Rules);
        Assert.Equal("z", service2.Rules[0].TargetField);
    }
}
