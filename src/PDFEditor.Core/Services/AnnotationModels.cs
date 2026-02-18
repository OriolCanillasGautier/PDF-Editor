namespace PDFEditor.Core.Services;

/// <summary>
/// Types of annotation that can be placed on a PDF page
/// </summary>
public enum AnnotationType
{
    Text,
    Image,
    Highlight,
    Rectangle,
    Ellipse,
    Arrow,
    FreehandDraw,
    Blur,
    Redact,
    Stamp,
    StickyNote,
    Underline,
    Strikethrough,
    MeasureRuler,
    MeasureArea,
    MeasurePerimeter
}

/// <summary>
/// Pre-defined stamp types
/// </summary>
public enum StampType
{
    Approved,
    Rejected,
    Confidential,
    Draft,
    Final,
    ForReview,
    NotApproved,
    Void,
    Custom
}

/// <summary>
/// Represents a single annotation on a PDF page.
/// Coordinates are relative to the page (0-1 normalized).
/// </summary>
public class PdfAnnotation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AnnotationType Type { get; set; }
    public int PageIndex { get; set; }

    // Position & size (0-1 normalized relative to page dimensions)
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    // Text annotation
    public string? Text { get; set; }
    public string FontFamily { get; set; } = "Helvetica";
    public float FontSize { get; set; } = 14f;
    public string Color { get; set; } = "#000000";
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }

    // Image annotation
    public byte[]? ImageData { get; set; }

    // Shape annotations (highlight, rectangle, ellipse)
    public string FillColor { get; set; } = "#FFFF00";
    public float FillOpacity { get; set; } = 0.3f;
    public string StrokeColor { get; set; } = "#000000";
    public float StrokeWidth { get; set; } = 1f;
    public float StrokeOpacity { get; set; } = 1f;

    // Arrow annotations
    public double EndX { get; set; }
    public double EndY { get; set; }

    // Freehand drawing
    public List<(double x, double y)> Points { get; set; } = new();

    // Blur / Redact
    public int BlurRadius { get; set; } = 10;

    // Stamp
    public StampType StampPreset { get; set; } = StampType.Custom;
    public string? StampText { get; set; }

    // Sticky note
    public string? NoteContent { get; set; }
    public string NoteColor { get; set; } = "#FFFACD"; // LemonChiffon

    // Rotation (degrees)
    public double Rotation { get; set; }

    // Measurement annotations
    /// <summary>
    /// Unit for measurement display (e.g., "mm", "cm", "in", "pt").
    /// </summary>
    public string MeasureUnit { get; set; } = "mm";

    /// <summary>
    /// Scale factor for measurements (PDF points per real-world unit).
    /// Default 1pt = 1/72 inch.
    /// </summary>
    public double MeasureScale { get; set; } = 1.0;

    /// <summary>
    /// Computed measurement value (length, area, or perimeter depending on type).
    /// </summary>
    public double MeasuredValue { get; set; }

    /// <summary>
    /// Label text shown on the measurement annotation.
    /// </summary>
    public string? MeasureLabel { get; set; }

    public PdfAnnotation Clone()
    {
        return new PdfAnnotation
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = Type,
            PageIndex = PageIndex,
            X = X, Y = Y,
            Width = Width, Height = Height,
            Text = Text,
            FontFamily = FontFamily,
            FontSize = FontSize,
            Color = Color,
            IsBold = IsBold,
            IsItalic = IsItalic,
            ImageData = ImageData != null ? (byte[])ImageData.Clone() : null,
            FillColor = FillColor,
            FillOpacity = FillOpacity,
            StrokeColor = StrokeColor,
            StrokeWidth = StrokeWidth,
            StrokeOpacity = StrokeOpacity,
            EndX = EndX, EndY = EndY,
            Points = new List<(double, double)>(Points),
            BlurRadius = BlurRadius,
            StampPreset = StampPreset,
            StampText = StampText,
            NoteContent = NoteContent,
            NoteColor = NoteColor,
            Rotation = Rotation,
            MeasureUnit = MeasureUnit,
            MeasureScale = MeasureScale,
            MeasuredValue = MeasuredValue,
            MeasureLabel = MeasureLabel
        };
    }
}
