using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Provides measurement calculations for PDF annotation tools.
/// Supports ruler (distance), area, and perimeter measurements.
/// All input coordinates are in normalized (0-1) page coordinates.
/// </summary>
public class MeasurementService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Supported measurement units.
    /// </summary>
    public static readonly Dictionary<string, double> UnitConversions = new()
    {
        { "pt", 1.0 },         // PDF points (1/72 inch)
        { "in", 72.0 },        // Inches
        { "cm", 72.0 / 2.54 }, // Centimeters
        { "mm", 72.0 / 25.4 }, // Millimeters
        { "px", 1.0 }          // Pixels (treated as points)
    };

    /// <summary>
    /// Gets available measurement unit names.
    /// </summary>
    public List<string> GetAvailableUnits() => UnitConversions.Keys.ToList();

    /// <summary>
    /// Calculates the distance between two points (ruler measurement).
    /// </summary>
    /// <param name="x1">Start X (normalized 0-1)</param>
    /// <param name="y1">Start Y (normalized 0-1)</param>
    /// <param name="x2">End X (normalized 0-1)</param>
    /// <param name="y2">End Y (normalized 0-1)</param>
    /// <param name="pageWidthPt">Page width in PDF points</param>
    /// <param name="pageHeightPt">Page height in PDF points</param>
    /// <param name="unit">Output unit (pt, in, cm, mm)</param>
    /// <param name="scale">Scale factor (default 1.0)</param>
    /// <returns>Distance in the specified unit</returns>
    public double MeasureDistance(double x1, double y1, double x2, double y2,
        double pageWidthPt, double pageHeightPt,
        string unit = "mm", double scale = 1.0)
    {
        // Convert normalized coords to points
        double dx = (x2 - x1) * pageWidthPt;
        double dy = (y2 - y1) * pageHeightPt;
        double distancePt = Math.Sqrt(dx * dx + dy * dy);

        return ConvertFromPoints(distancePt * scale, unit);
    }

    /// <summary>
    /// Calculates the area of a polygon defined by points.
    /// Uses the Shoelace formula for polygon area.
    /// </summary>
    /// <param name="points">Polygon vertices (normalized 0-1 coordinates)</param>
    /// <param name="pageWidthPt">Page width in PDF points</param>
    /// <param name="pageHeightPt">Page height in PDF points</param>
    /// <param name="unit">Output unit (area in unit²)</param>
    /// <param name="scale">Scale factor</param>
    /// <returns>Area in the specified unit squared</returns>
    public double MeasureArea(List<(double x, double y)> points,
        double pageWidthPt, double pageHeightPt,
        string unit = "mm", double scale = 1.0)
    {
        if (points.Count < 3)
        {
            Log.Warn("Area measurement requires at least 3 points, got {Count}", points.Count);
            return 0;
        }

        // Convert to points
        var ptPoints = points.Select(p => (
            x: p.x * pageWidthPt,
            y: p.y * pageHeightPt
        )).ToList();

        // Shoelace formula
        double area = 0;
        int n = ptPoints.Count;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += ptPoints[i].x * ptPoints[j].y;
            area -= ptPoints[j].x * ptPoints[i].y;
        }
        area = Math.Abs(area) / 2.0;

        // Apply scale² (area scales quadratically)
        area *= scale * scale;

        // Convert from pt² to unit²
        double unitFactor = GetUnitFactor(unit);
        return area / (unitFactor * unitFactor);
    }

    /// <summary>
    /// Calculates the perimeter of a polygon defined by points.
    /// </summary>
    /// <param name="points">Polygon vertices (normalized 0-1 coordinates)</param>
    /// <param name="pageWidthPt">Page width in PDF points</param>
    /// <param name="pageHeightPt">Page height in PDF points</param>
    /// <param name="unit">Output unit</param>
    /// <param name="scale">Scale factor</param>
    /// <returns>Perimeter in the specified unit</returns>
    public double MeasurePerimeter(List<(double x, double y)> points,
        double pageWidthPt, double pageHeightPt,
        string unit = "mm", double scale = 1.0)
    {
        if (points.Count < 2)
        {
            Log.Warn("Perimeter measurement requires at least 2 points, got {Count}", points.Count);
            return 0;
        }

        double perimeterPt = 0;
        for (int i = 0; i < points.Count; i++)
        {
            int j = (i + 1) % points.Count;
            double dx = (points[j].x - points[i].x) * pageWidthPt;
            double dy = (points[j].y - points[i].y) * pageHeightPt;
            perimeterPt += Math.Sqrt(dx * dx + dy * dy);
        }

        return ConvertFromPoints(perimeterPt * scale, unit);
    }

    /// <summary>
    /// Calculates the area of a rectangle from annotation coordinates.
    /// </summary>
    public double MeasureRectangleArea(double x, double y, double width, double height,
        double pageWidthPt, double pageHeightPt,
        string unit = "mm", double scale = 1.0)
    {
        double wPt = width * pageWidthPt * scale;
        double hPt = height * pageHeightPt * scale;
        double areaPt2 = wPt * hPt;

        double unitFactor = GetUnitFactor(unit);
        return areaPt2 / (unitFactor * unitFactor);
    }

    /// <summary>
    /// Creates a ruler measurement annotation between two points.
    /// </summary>
    public PdfAnnotation CreateRulerAnnotation(int pageIndex,
        double x1, double y1, double x2, double y2,
        double pageWidthPt, double pageHeightPt,
        string unit = "mm", double scale = 1.0,
        string color = "#FF0000")
    {
        double distance = MeasureDistance(x1, y1, x2, y2, pageWidthPt, pageHeightPt, unit, scale);
        string label = FormatMeasurement(distance, unit);

        return new PdfAnnotation
        {
            Type = AnnotationType.MeasureRuler,
            PageIndex = pageIndex,
            X = Math.Min(x1, x2),
            Y = Math.Min(y1, y2),
            Width = Math.Abs(x2 - x1),
            Height = Math.Abs(y2 - y1),
            EndX = x2,
            EndY = y2,
            Points = new List<(double, double)> { (x1, y1), (x2, y2) },
            Color = color,
            StrokeColor = color,
            StrokeWidth = 2f,
            MeasureUnit = unit,
            MeasureScale = scale,
            MeasuredValue = distance,
            MeasureLabel = label,
            Text = label
        };
    }

    /// <summary>
    /// Creates an area measurement annotation from polygon points.
    /// </summary>
    public PdfAnnotation CreateAreaAnnotation(int pageIndex,
        List<(double x, double y)> points,
        double pageWidthPt, double pageHeightPt,
        string unit = "mm", double scale = 1.0,
        string fillColor = "#FF000033", string strokeColor = "#FF0000")
    {
        if (points.Count < 3)
            throw new ArgumentException("Area measurement requires at least 3 points.");

        double area = MeasureArea(points, pageWidthPt, pageHeightPt, unit, scale);
        string label = FormatAreaMeasurement(area, unit);

        // Bounding box
        double minX = points.Min(p => p.x);
        double minY = points.Min(p => p.y);
        double maxX = points.Max(p => p.x);
        double maxY = points.Max(p => p.y);

        return new PdfAnnotation
        {
            Type = AnnotationType.MeasureArea,
            PageIndex = pageIndex,
            X = minX,
            Y = minY,
            Width = maxX - minX,
            Height = maxY - minY,
            Points = new List<(double, double)>(points),
            FillColor = fillColor,
            FillOpacity = 0.2f,
            StrokeColor = strokeColor,
            StrokeWidth = 2f,
            MeasureUnit = unit,
            MeasureScale = scale,
            MeasuredValue = area,
            MeasureLabel = label,
            Text = label
        };
    }

    /// <summary>
    /// Creates a perimeter measurement annotation from polygon points.
    /// </summary>
    public PdfAnnotation CreatePerimeterAnnotation(int pageIndex,
        List<(double x, double y)> points,
        double pageWidthPt, double pageHeightPt,
        string unit = "mm", double scale = 1.0,
        string color = "#0000FF")
    {
        if (points.Count < 2)
            throw new ArgumentException("Perimeter measurement requires at least 2 points.");

        double perimeter = MeasurePerimeter(points, pageWidthPt, pageHeightPt, unit, scale);
        string label = FormatMeasurement(perimeter, unit);

        double minX = points.Min(p => p.x);
        double minY = points.Min(p => p.y);
        double maxX = points.Max(p => p.x);
        double maxY = points.Max(p => p.y);

        return new PdfAnnotation
        {
            Type = AnnotationType.MeasurePerimeter,
            PageIndex = pageIndex,
            X = minX,
            Y = minY,
            Width = maxX - minX,
            Height = maxY - minY,
            Points = new List<(double, double)>(points),
            StrokeColor = color,
            StrokeWidth = 2f,
            MeasureUnit = unit,
            MeasureScale = scale,
            MeasuredValue = perimeter,
            MeasureLabel = label,
            Text = label
        };
    }

    /// <summary>
    /// Updates measurement value on an existing measurement annotation.
    /// Call after points have been modified.
    /// </summary>
    public void RecalculateMeasurement(PdfAnnotation annotation,
        double pageWidthPt, double pageHeightPt)
    {
        if (annotation.Points.Count < 2) return;

        switch (annotation.Type)
        {
            case AnnotationType.MeasureRuler:
                var p1 = annotation.Points[0];
                var p2 = annotation.Points[1];
                annotation.MeasuredValue = MeasureDistance(
                    p1.x, p1.y, p2.x, p2.y,
                    pageWidthPt, pageHeightPt,
                    annotation.MeasureUnit, annotation.MeasureScale);
                annotation.MeasureLabel = FormatMeasurement(annotation.MeasuredValue, annotation.MeasureUnit);
                break;

            case AnnotationType.MeasureArea:
                annotation.MeasuredValue = MeasureArea(
                    annotation.Points, pageWidthPt, pageHeightPt,
                    annotation.MeasureUnit, annotation.MeasureScale);
                annotation.MeasureLabel = FormatAreaMeasurement(annotation.MeasuredValue, annotation.MeasureUnit);
                break;

            case AnnotationType.MeasurePerimeter:
                annotation.MeasuredValue = MeasurePerimeter(
                    annotation.Points, pageWidthPt, pageHeightPt,
                    annotation.MeasureUnit, annotation.MeasureScale);
                annotation.MeasureLabel = FormatMeasurement(annotation.MeasuredValue, annotation.MeasureUnit);
                break;
        }

        annotation.Text = annotation.MeasureLabel;
    }

    #region Unit Conversion Helpers

    private double ConvertFromPoints(double valuePt, string unit)
    {
        double factor = GetUnitFactor(unit);
        return valuePt / factor;
    }

    private double GetUnitFactor(string unit)
    {
        return UnitConversions.TryGetValue(unit.ToLowerInvariant(), out var factor)
            ? factor
            : 1.0; // default to points
    }

    /// <summary>
    /// Formats a linear measurement value with unit label.
    /// </summary>
    public static string FormatMeasurement(double value, string unit)
    {
        return $"{value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} {unit}";
    }

    /// <summary>
    /// Formats an area measurement value with unit² label.
    /// </summary>
    public static string FormatAreaMeasurement(double value, string unit)
    {
        return $"{value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} {unit}²";
    }

    #endregion
}
