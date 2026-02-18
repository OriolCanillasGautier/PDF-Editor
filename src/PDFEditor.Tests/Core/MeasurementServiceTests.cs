using PDFEditor.Core.Services;
using PDFEditor.Tests.Helpers;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for MeasurementService (ruler, area, perimeter measurements)
/// </summary>
public class MeasurementServiceTests
{
    private readonly MeasurementService _service = new();

    #region Distance Tests

    [Fact]
    public void MeasureDistance_HorizontalLine_ReturnsCorrectDistance()
    {
        // 100pt horizontal line on a 612x792pt page (US Letter)
        // x1=0.1, x2=0.1+100/612, y same
        double x1 = 0.1, y1 = 0.5, x2 = 0.1 + 100.0 / 612, y2 = 0.5;
        double distance = _service.MeasureDistance(x1, y1, x2, y2, 612, 792, "pt");
        Assert.InRange(distance, 99.5, 100.5); // ~100pt
    }

    [Fact]
    public void MeasureDistance_VerticalLine_ReturnsCorrectDistance()
    {
        double x1 = 0.5, y1 = 0.1, x2 = 0.5, y2 = 0.1 + 72.0 / 792;
        double distance = _service.MeasureDistance(x1, y1, x2, y2, 612, 792, "in");
        Assert.InRange(distance, 0.95, 1.05); // ~1 inch
    }

    [Fact]
    public void MeasureDistance_Diagonal_ReturnsCorrectDistance()
    {
        // Diagonal: 3-4-5 triangle scaled to normalized coords
        double x1 = 0, y1 = 0;
        double x2 = 3.0 / 612, y2 = 4.0 / 792;
        double distance = _service.MeasureDistance(x1, y1, x2, y2, 612, 792, "pt");
        Assert.InRange(distance, 4.9, 5.1); // ~5pt (3² + 4² = 25 → √25 = 5)
    }

    [Fact]
    public void MeasureDistance_ZeroLength_ReturnsZero()
    {
        double dist = _service.MeasureDistance(0.5, 0.5, 0.5, 0.5, 612, 792, "mm");
        Assert.Equal(0, dist);
    }

    [Fact]
    public void MeasureDistance_WithScale_AppliesScale()
    {
        double distNoScale = _service.MeasureDistance(0, 0, 0.5, 0, 612, 792, "pt", 1.0);
        double distWithScale = _service.MeasureDistance(0, 0, 0.5, 0, 612, 792, "pt", 2.0);
        Assert.InRange(distWithScale / distNoScale, 1.95, 2.05);
    }

    #endregion

    #region Area Tests

    [Fact]
    public void MeasureArea_Square_ReturnsCorrectArea()
    {
        // 100x100pt square on a 612x792 page
        var points = new List<(double x, double y)>
        {
            (0, 0),
            (100.0 / 612, 0),
            (100.0 / 612, 100.0 / 792),
            (0, 100.0 / 792)
        };
        double area = _service.MeasureArea(points, 612, 792, "pt");
        Assert.InRange(area, 9900, 10100); // ~10000 pt²
    }

    [Fact]
    public void MeasureArea_Triangle_ReturnsCorrectArea()
    {
        // Triangle: base 100pt, height 50pt → area = 2500 pt²
        var points = new List<(double x, double y)>
        {
            (0, 0),
            (100.0 / 612, 0),
            (50.0 / 612, 50.0 / 792)
        };
        double area = _service.MeasureArea(points, 612, 792, "pt");
        Assert.InRange(area, 2400, 2600);
    }

    [Fact]
    public void MeasureArea_TooFewPoints_ReturnsZero()
    {
        var points = new List<(double x, double y)> { (0, 0), (1, 1) };
        double area = _service.MeasureArea(points, 612, 792, "mm");
        Assert.Equal(0, area);
    }

    [Fact]
    public void MeasureArea_InMillimeters_ConvertsCorrectly()
    {
        // 1 inch × 1 inch square = 25.4mm × 25.4mm = 645.16 mm²
        var points = new List<(double x, double y)>
        {
            (0, 0),
            (72.0 / 612, 0),
            (72.0 / 612, 72.0 / 792),
            (0, 72.0 / 792)
        };
        double area = _service.MeasureArea(points, 612, 792, "mm");
        Assert.InRange(area, 640, 650);
    }

    #endregion

    #region Perimeter Tests

    [Fact]
    public void MeasurePerimeter_Square_ReturnsFourSides()
    {
        // 72pt square (1 inch)
        var points = new List<(double x, double y)>
        {
            (0, 0),
            (72.0 / 612, 0),
            (72.0 / 612, 72.0 / 792),
            (0, 72.0 / 792)
        };
        double perimeter = _service.MeasurePerimeter(points, 612, 792, "in");
        Assert.InRange(perimeter, 3.9, 4.1); // 4 inches
    }

    [Fact]
    public void MeasurePerimeter_TooFewPoints_ReturnsZero()
    {
        var points = new List<(double x, double y)> { (0, 0) };
        Assert.Equal(0, _service.MeasurePerimeter(points, 612, 792, "mm"));
    }

    #endregion

    #region Annotation Creation Tests

    [Fact]
    public void CreateRulerAnnotation_SetsCorrectProperties()
    {
        var ann = _service.CreateRulerAnnotation(0, 0.1, 0.2, 0.5, 0.2, 612, 792, "mm");
        Assert.Equal(AnnotationType.MeasureRuler, ann.Type);
        Assert.Equal(0, ann.PageIndex);
        Assert.Equal("mm", ann.MeasureUnit);
        Assert.True(ann.MeasuredValue > 0);
        Assert.Contains("mm", ann.MeasureLabel);
    }

    [Fact]
    public void CreateAreaAnnotation_SetsCorrectProperties()
    {
        var points = new List<(double x, double y)> { (0, 0), (0.5, 0), (0.5, 0.5), (0, 0.5) };
        var ann = _service.CreateAreaAnnotation(0, points, 612, 792, "cm");
        Assert.Equal(AnnotationType.MeasureArea, ann.Type);
        Assert.Equal("cm", ann.MeasureUnit);
        Assert.True(ann.MeasuredValue > 0);
        Assert.Contains("cm²", ann.MeasureLabel);
    }

    [Fact]
    public void CreateAreaAnnotation_TooFewPoints_Throws()
    {
        var points = new List<(double x, double y)> { (0, 0), (0.5, 0) };
        Assert.Throws<ArgumentException>(() =>
            _service.CreateAreaAnnotation(0, points, 612, 792));
    }

    [Fact]
    public void CreatePerimeterAnnotation_SetsCorrectProperties()
    {
        var points = new List<(double x, double y)>
            { (0, 0), (0.5, 0), (0.5, 0.5), (0, 0.5) };
        var ann = _service.CreatePerimeterAnnotation(0, points, 612, 792, "in");
        Assert.Equal(AnnotationType.MeasurePerimeter, ann.Type);
        Assert.True(ann.MeasuredValue > 0);
    }

    [Fact]
    public void RecalculateMeasurement_UpdatesValue()
    {
        var ann = _service.CreateRulerAnnotation(0, 0.1, 0.2, 0.5, 0.2, 612, 792);
        double original = ann.MeasuredValue;

        ann.Points = new List<(double, double)> { (0.1, 0.2), (0.9, 0.2) };
        _service.RecalculateMeasurement(ann, 612, 792);

        Assert.NotEqual(original, ann.MeasuredValue);
        Assert.True(ann.MeasuredValue > original);
    }

    #endregion

    #region Unit Conversion Tests

    [Fact]
    public void GetAvailableUnits_ReturnsExpectedUnits()
    {
        var units = _service.GetAvailableUnits();
        Assert.Contains("pt", units);
        Assert.Contains("in", units);
        Assert.Contains("cm", units);
        Assert.Contains("mm", units);
    }

    [Fact]
    public void FormatMeasurement_FormatsCorrectly()
    {
        Assert.Equal("12.34 mm", MeasurementService.FormatMeasurement(12.34, "mm"));
    }

    [Fact]
    public void FormatAreaMeasurement_FormatsCorrectly()
    {
        Assert.Equal("56.78 cm²", MeasurementService.FormatAreaMeasurement(56.78, "cm"));
    }

    #endregion

    #region RectangleArea Tests

    [Fact]
    public void MeasureRectangleArea_ReturnsCorrectArea()
    {
        // 2 inch × 3 inch rectangle = 6 in²
        double area = _service.MeasureRectangleArea(
            0, 0, 144.0 / 612, 216.0 / 792, 612, 792, "in");
        Assert.InRange(area, 5.8, 6.2);
    }

    #endregion
}
