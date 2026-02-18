using System.Xml.Linq;
using NLog;
using PDFEditor.Core.Services;

namespace PDFEditor.Core.Services;

/// <summary>
/// Imports and exports PDF annotations in XFDF (XML Forms Data Format) format.
/// XFDF is an Adobe standard for exchanging annotations and form data.
/// </summary>
public class XfdfAnnotationService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static readonly XNamespace XfdfNs = "http://ns.adobe.com/xfdf/";

    /// <summary>
    /// Exports annotations to XFDF XML string.
    /// </summary>
    public string ExportToXfdf(List<PdfAnnotation> annotations, string? pdfFilePath = null)
    {
        Log.Info("Exporting {Count} annotations to XFDF", annotations.Count);

        var xfdf = new XElement(XfdfNs + "xfdf",
            new XAttribute("xmlns", "http://ns.adobe.com/xfdf/"),
            new XAttribute(XNamespace.Xml + "space", "preserve"));

        // f element references the source PDF
        if (!string.IsNullOrEmpty(pdfFilePath))
            xfdf.Add(new XElement(XfdfNs + "f", new XAttribute("href", pdfFilePath)));

        var annots = new XElement(XfdfNs + "annots");

        foreach (var ann in annotations)
        {
            var elem = AnnotationToXfdfElement(ann);
            if (elem != null)
                annots.Add(elem);
        }

        xfdf.Add(annots);

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), xfdf);
        return doc.Declaration + Environment.NewLine + doc.ToString();
    }

    /// <summary>
    /// Imports annotations from an XFDF XML string.
    /// </summary>
    public List<PdfAnnotation> ImportFromXfdf(string xfdfContent)
    {
        Log.Info("Importing annotations from XFDF");
        var result = new List<PdfAnnotation>();

        try
        {
            var doc = XDocument.Parse(xfdfContent);
            var root = doc.Root;
            if (root == null) return result;

            // Handle both namespace and no-namespace
            var annots = root.Element(XfdfNs + "annots") ?? root.Element("annots");
            if (annots == null) return result;

            foreach (var elem in annots.Elements())
            {
                var ann = XfdfElementToAnnotation(elem);
                if (ann != null)
                    result.Add(ann);
            }

            Log.Info("Imported {Count} annotations from XFDF", result.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to parse XFDF content");
        }

        return result;
    }

    /// <summary>
    /// Exports annotations to XFDF and writes to a file.
    /// </summary>
    public async Task ExportToFileAsync(List<PdfAnnotation> annotations, string outputPath, string? pdfFilePath = null)
    {
        var xfdf = ExportToXfdf(annotations, pdfFilePath);
        await File.WriteAllTextAsync(outputPath, xfdf);
        Log.Info("XFDF exported to: {Path}", outputPath);
    }

    /// <summary>
    /// Imports annotations from an XFDF file.
    /// </summary>
    public async Task<List<PdfAnnotation>> ImportFromFileAsync(string xfdfPath)
    {
        var content = await File.ReadAllTextAsync(xfdfPath);
        return ImportFromXfdf(content);
    }

    private XElement? AnnotationToXfdfElement(PdfAnnotation ann)
    {
        // Map AnnotationType → XFDF element name
        string elemName = ann.Type switch
        {
            AnnotationType.Text => "freetext",
            AnnotationType.Highlight => "highlight",
            AnnotationType.Underline => "underline",
            AnnotationType.Strikethrough => "strikeout",
            AnnotationType.Rectangle => "square",
            AnnotationType.Ellipse => "circle",
            AnnotationType.Arrow => "line",
            AnnotationType.StickyNote => "text",
            AnnotationType.Stamp => "stamp",
            AnnotationType.FreehandDraw => "ink",
            AnnotationType.Redact => "redact",
            _ => "freetext"
        };

        var elem = new XElement(XfdfNs + elemName);

        // Common attributes
        elem.Add(new XAttribute("page", ann.PageIndex.ToString()));
        elem.Add(new XAttribute("color", ann.Color));
        elem.Add(new XAttribute("opacity", ann.FillOpacity.ToString("F2")));
        elem.Add(new XAttribute("date", DateTime.UtcNow.ToString("D:yyyyMMddHHmmss")));
        elem.Add(new XAttribute("name", ann.Id));

        // Rect: left,bottom,right,top (XFDF uses PDF coordinate system)
        double left = ann.X;
        double bottom = ann.Y;
        double right = ann.X + ann.Width;
        double top = ann.Y + ann.Height;
        elem.Add(new XAttribute("rect",
            $"{left:F2},{bottom:F2},{right:F2},{top:F2}"));

        // Type-specific properties
        switch (ann.Type)
        {
            case AnnotationType.Text:
            case AnnotationType.StickyNote:
                if (!string.IsNullOrEmpty(ann.Text))
                    elem.Add(new XElement(XfdfNs + "contents", ann.Text));
                if (!string.IsNullOrEmpty(ann.NoteContent))
                    elem.Add(new XElement(XfdfNs + "contents", ann.NoteContent));
                elem.Add(new XAttribute("fontsize", ann.FontSize.ToString("F1")));
                break;

            case AnnotationType.Highlight:
            case AnnotationType.Underline:
            case AnnotationType.Strikethrough:
                elem.Add(new XAttribute("interior-color", ann.FillColor));
                break;

            case AnnotationType.Rectangle:
            case AnnotationType.Ellipse:
                elem.Add(new XAttribute("interior-color", ann.FillColor));
                elem.Add(new XAttribute("width", ann.StrokeWidth.ToString("F1")));
                break;

            case AnnotationType.Arrow:
                elem.Add(new XAttribute("start",
                    $"{ann.X:F2},{ann.Y:F2}"));
                elem.Add(new XAttribute("end",
                    $"{ann.EndX:F2},{ann.EndY:F2}"));
                break;

            case AnnotationType.Stamp:
                if (!string.IsNullOrEmpty(ann.StampText))
                    elem.Add(new XElement(XfdfNs + "contents", ann.StampText));
                elem.Add(new XAttribute("icon", ann.StampPreset.ToString()));
                break;

            case AnnotationType.FreehandDraw:
                if (ann.Points.Count > 0)
                {
                    var pointStr = string.Join(";",
                        ann.Points.Select(p => $"{p.x:F2},{p.y:F2}"));
                    var inklist = new XElement(XfdfNs + "inklist",
                        new XElement(XfdfNs + "gesture", pointStr));
                    elem.Add(inklist);
                }
                elem.Add(new XAttribute("width", ann.StrokeWidth.ToString("F1")));
                break;

            case AnnotationType.Redact:
                elem.Add(new XAttribute("interior-color", "#000000"));
                break;
        }

        // Rotation
        if (ann.Rotation != 0)
            elem.Add(new XAttribute("rotation", ann.Rotation.ToString("F0")));

        return elem;
    }

    private PdfAnnotation? XfdfElementToAnnotation(XElement elem)
    {
        try
        {
            var localName = elem.Name.LocalName.ToLowerInvariant();
            var ann = new PdfAnnotation();

            // Map XFDF element name → AnnotationType
            ann.Type = localName switch
            {
                "freetext" => AnnotationType.Text,
                "highlight" => AnnotationType.Highlight,
                "underline" => AnnotationType.Underline,
                "strikeout" => AnnotationType.Strikethrough,
                "square" => AnnotationType.Rectangle,
                "circle" => AnnotationType.Ellipse,
                "line" => AnnotationType.Arrow,
                "text" => AnnotationType.StickyNote,
                "stamp" => AnnotationType.Stamp,
                "ink" => AnnotationType.FreehandDraw,
                "redact" => AnnotationType.Redact,
                _ => AnnotationType.Text
            };

            // Page
            var pageAttr = elem.Attribute("page");
            if (pageAttr != null && int.TryParse(pageAttr.Value, out int page))
                ann.PageIndex = page;

            // Name / ID
            var nameAttr = elem.Attribute("name");
            if (nameAttr != null)
                ann.Id = nameAttr.Value;

            // Color
            var colorAttr = elem.Attribute("color");
            if (colorAttr != null)
                ann.Color = colorAttr.Value;

            // Opacity
            var opacityAttr = elem.Attribute("opacity");
            if (opacityAttr != null && float.TryParse(opacityAttr.Value, out float opacity))
                ann.FillOpacity = opacity;

            // Rect
            var rectAttr = elem.Attribute("rect");
            if (rectAttr != null)
            {
                var parts = rectAttr.Value.Split(',');
                if (parts.Length == 4)
                {
                    double.TryParse(parts[0], out double left);
                    double.TryParse(parts[1], out double bottom);
                    double.TryParse(parts[2], out double right);
                    double.TryParse(parts[3], out double top);
                    ann.X = left;
                    ann.Y = bottom;
                    ann.Width = right - left;
                    ann.Height = top - bottom;
                }
            }

            // Contents
            var contents = elem.Element(XfdfNs + "contents") ?? elem.Element("contents");
            if (contents != null)
            {
                if (ann.Type == AnnotationType.StickyNote)
                    ann.NoteContent = contents.Value;
                else if (ann.Type == AnnotationType.Stamp)
                    ann.StampText = contents.Value;
                else
                    ann.Text = contents.Value;
            }

            // FontSize
            var fontSizeAttr = elem.Attribute("fontsize");
            if (fontSizeAttr != null && float.TryParse(fontSizeAttr.Value, out float fs))
                ann.FontSize = fs;

            // Interior color
            var interiorAttr = elem.Attribute("interior-color");
            if (interiorAttr != null)
                ann.FillColor = interiorAttr.Value;

            // Stroke width
            var widthAttr = elem.Attribute("width");
            if (widthAttr != null && float.TryParse(widthAttr.Value, out float sw))
                ann.StrokeWidth = sw;

            // Arrow start/end
            if (ann.Type == AnnotationType.Arrow)
            {
                var startAttr = elem.Attribute("start");
                var endAttr = elem.Attribute("end");
                if (startAttr != null)
                {
                    var sp = startAttr.Value.Split(',');
                    if (sp.Length == 2)
                    {
                        double.TryParse(sp[0], out double sx);
                        double.TryParse(sp[1], out double sy);
                        ann.X = sx; ann.Y = sy;
                    }
                }
                if (endAttr != null)
                {
                    var ep = endAttr.Value.Split(',');
                    if (ep.Length == 2)
                    {
                        double.TryParse(ep[0], out double ex);
                        double.TryParse(ep[1], out double ey);
                        ann.EndX = ex; ann.EndY = ey;
                    }
                }
            }

            // Ink points
            if (ann.Type == AnnotationType.FreehandDraw)
            {
                var inklist = elem.Element(XfdfNs + "inklist") ?? elem.Element("inklist");
                var gesture = inklist?.Element(XfdfNs + "gesture") ?? inklist?.Element("gesture");
                if (gesture != null)
                {
                    var pointPairs = gesture.Value.Split(';');
                    foreach (var pair in pointPairs)
                    {
                        var coords = pair.Split(',');
                        if (coords.Length == 2 &&
                            double.TryParse(coords[0], out double px) &&
                            double.TryParse(coords[1], out double py))
                        {
                            ann.Points.Add((px, py));
                        }
                    }
                }
            }

            // Stamp icon
            if (ann.Type == AnnotationType.Stamp)
            {
                var iconAttr = elem.Attribute("icon");
                if (iconAttr != null && Enum.TryParse<StampType>(iconAttr.Value, true, out var stamp))
                    ann.StampPreset = stamp;
            }

            // Rotation
            var rotAttr = elem.Attribute("rotation");
            if (rotAttr != null && double.TryParse(rotAttr.Value, out double rot))
                ann.Rotation = rot;

            return ann;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to parse XFDF annotation element: {Name}", elem.Name.LocalName);
            return null;
        }
    }
}
