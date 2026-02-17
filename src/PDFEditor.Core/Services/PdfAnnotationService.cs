using iText.Kernel.Colors;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Extgstate;
using iText.Layout;
using ImageMagick;

namespace PDFEditor.Core.Services;

/// <summary>
/// Service to burn (flatten) annotations into a PDF document.
/// Converts PdfAnnotation objects into actual PDF content.
/// </summary>
public class PdfAnnotationService
{
    /// <summary>
    /// Burns all annotations into the PDF bytes, returning new PDF bytes.
    /// </summary>
    public byte[] BurnAnnotations(byte[] pdfBytes, List<PdfAnnotation> annotations)
    {
        if (annotations.Count == 0) return pdfBytes;

        var outputMs = new MemoryStream();
        using (var reader = new PdfReader(new MemoryStream(pdfBytes)))
        using (var writer = new PdfWriter(outputMs))
        {
            var doc = new PdfDocument(reader, writer);

            var byPage = annotations
                .GroupBy(a => a.PageIndex)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var (pageIdx, pageAnnotations) in byPage)
            {
                if (pageIdx < 0 || pageIdx >= doc.GetNumberOfPages()) continue;

                var page = doc.GetPage(pageIdx + 1);
                var pageSize = page.GetPageSize();
                float pageW = pageSize.GetWidth();
                float pageH = pageSize.GetHeight();

                var canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), doc);

                foreach (var ann in pageAnnotations)
                {
                    try
                    {
                        BurnAnnotation(canvas, ann, pageW, pageH, doc);
                    }
                    catch
                    {
                        // Skip failed annotations silently
                    }
                }
            }

            doc.Close();
        }

        return outputMs.ToArray();
    }

    private void BurnAnnotation(PdfCanvas canvas, PdfAnnotation ann,
        float pageW, float pageH, PdfDocument doc)
    {
        // Convert normalized coords to PDF points (origin = bottom-left in PDF)
        float x = (float)(ann.X * pageW);
        float y = pageH - (float)((ann.Y + ann.Height) * pageH); // flip Y
        float w = (float)(ann.Width * pageW);
        float h = (float)(ann.Height * pageH);

        switch (ann.Type)
        {
            case AnnotationType.Text:
                BurnText(canvas, ann, x, y, w, h, pageW, pageH);
                break;
            case AnnotationType.Highlight:
                BurnHighlight(canvas, ann, x, y, w, h);
                break;
            case AnnotationType.Rectangle:
                BurnRectangle(canvas, ann, x, y, w, h);
                break;
            case AnnotationType.Ellipse:
                BurnEllipse(canvas, ann, x, y, w, h);
                break;
            case AnnotationType.Arrow:
                BurnArrow(canvas, ann, pageW, pageH);
                break;
            case AnnotationType.FreehandDraw:
                BurnFreehand(canvas, ann, pageW, pageH);
                break;
            case AnnotationType.Image:
                BurnImage(canvas, ann, x, y, w, h, doc);
                break;
            case AnnotationType.Redact:
                BurnRedact(canvas, ann, x, y, w, h);
                break;
            // Blur is handled at the image level before burning
        }
    }

    private void BurnText(PdfCanvas canvas, PdfAnnotation ann,
        float x, float y, float w, float h, float pageW, float pageH)
    {
        if (string.IsNullOrEmpty(ann.Text)) return;

        var color = ParseColor(ann.Color);
        float fontSize = ann.FontSize;
        // Place text at the top of the annotation box
        float textY = pageH - (float)(ann.Y * pageH) - fontSize;

        canvas.SaveState();
        canvas.SetFillColor(color);
        canvas.BeginText();

        var fontName = ann.FontFamily?.ToLower() switch
        {
            "courier" or "consolas" => iText.IO.Font.Constants.StandardFonts.COURIER,
            "times" or "times new roman" => ann.IsBold
                ? iText.IO.Font.Constants.StandardFonts.TIMES_BOLD
                : iText.IO.Font.Constants.StandardFonts.TIMES_ROMAN,
            _ => ann.IsBold
                ? iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD
                : iText.IO.Font.Constants.StandardFonts.HELVETICA
        };

        var font = iText.Kernel.Font.PdfFontFactory.CreateFont(fontName);
        canvas.SetFontAndSize(font, fontSize);
        canvas.MoveText(x, textY);
        canvas.ShowText(ann.Text);
        canvas.EndText();
        canvas.RestoreState();
    }

    private void BurnHighlight(PdfCanvas canvas, PdfAnnotation ann,
        float x, float y, float w, float h)
    {
        canvas.SaveState();
        var gs = new PdfExtGState().SetFillOpacity(ann.FillOpacity);
        canvas.SetExtGState(gs);
        canvas.SetFillColor(ParseColor(ann.FillColor));
        canvas.Rectangle(x, y, w, h);
        canvas.Fill();
        canvas.RestoreState();
    }

    private void BurnRectangle(PdfCanvas canvas, PdfAnnotation ann,
        float x, float y, float w, float h)
    {
        canvas.SaveState();

        if (ann.FillOpacity > 0)
        {
            var gs = new PdfExtGState().SetFillOpacity(ann.FillOpacity);
            canvas.SetExtGState(gs);
            canvas.SetFillColor(ParseColor(ann.FillColor));
            canvas.Rectangle(x, y, w, h);
            canvas.Fill();
        }

        var gs2 = new PdfExtGState().SetStrokeOpacity(ann.StrokeOpacity);
        canvas.SetExtGState(gs2);
        canvas.SetStrokeColor(ParseColor(ann.StrokeColor));
        canvas.SetLineWidth(ann.StrokeWidth);
        canvas.Rectangle(x, y, w, h);
        canvas.Stroke();

        canvas.RestoreState();
    }

    private void BurnEllipse(PdfCanvas canvas, PdfAnnotation ann,
        float x, float y, float w, float h)
    {
        canvas.SaveState();

        if (ann.FillOpacity > 0)
        {
            var gs = new PdfExtGState().SetFillOpacity(ann.FillOpacity);
            canvas.SetExtGState(gs);
            canvas.SetFillColor(ParseColor(ann.FillColor));
            canvas.Ellipse(x, y, x + w, y + h);
            canvas.Fill();
        }

        var gs2 = new PdfExtGState().SetStrokeOpacity(ann.StrokeOpacity);
        canvas.SetExtGState(gs2);
        canvas.SetStrokeColor(ParseColor(ann.StrokeColor));
        canvas.SetLineWidth(ann.StrokeWidth);
        canvas.Ellipse(x, y, x + w, y + h);
        canvas.Stroke();

        canvas.RestoreState();
    }

    private void BurnArrow(PdfCanvas canvas, PdfAnnotation ann,
        float pageW, float pageH)
    {
        float x1 = (float)(ann.X * pageW);
        float y1 = pageH - (float)(ann.Y * pageH);
        float x2 = (float)(ann.EndX * pageW);
        float y2 = pageH - (float)(ann.EndY * pageH);

        canvas.SaveState();
        canvas.SetStrokeColor(ParseColor(ann.StrokeColor));
        canvas.SetLineWidth(ann.StrokeWidth);
        canvas.MoveTo(x1, y1);
        canvas.LineTo(x2, y2);
        canvas.Stroke();

        // Arrowhead
        double angle = Math.Atan2(y2 - y1, x2 - x1);
        double headLen = 10 + ann.StrokeWidth * 2;
        double ax1 = x2 - headLen * Math.Cos(angle - Math.PI / 6);
        double ay1 = y2 - headLen * Math.Sin(angle - Math.PI / 6);
        double ax2 = x2 - headLen * Math.Cos(angle + Math.PI / 6);
        double ay2 = y2 - headLen * Math.Sin(angle + Math.PI / 6);

        canvas.SetFillColor(ParseColor(ann.StrokeColor));
        canvas.MoveTo(x2, y2);
        canvas.LineTo(ax1, ay1);
        canvas.LineTo(ax2, ay2);
        canvas.ClosePath();
        canvas.Fill();

        canvas.RestoreState();
    }

    private void BurnFreehand(PdfCanvas canvas, PdfAnnotation ann,
        float pageW, float pageH)
    {
        if (ann.Points.Count < 2) return;

        canvas.SaveState();
        canvas.SetStrokeColor(ParseColor(ann.StrokeColor));
        canvas.SetLineWidth(ann.StrokeWidth);
        canvas.SetLineJoinStyle(PdfCanvasConstants.LineJoinStyle.ROUND);
        canvas.SetLineCapStyle(PdfCanvasConstants.LineCapStyle.ROUND);

        var first = ann.Points[0];
        canvas.MoveTo(first.x * pageW, pageH - first.y * pageH);
        for (int i = 1; i < ann.Points.Count; i++)
        {
            var p = ann.Points[i];
            canvas.LineTo(p.x * pageW, pageH - p.y * pageH);
        }

        canvas.Stroke();
        canvas.RestoreState();
    }

    private void BurnImage(PdfCanvas canvas, PdfAnnotation ann,
        float x, float y, float w, float h, PdfDocument doc)
    {
        if (ann.ImageData == null || ann.ImageData.Length == 0) return;

        var imageData = iText.IO.Image.ImageDataFactory.Create(ann.ImageData);
        canvas.SaveState();
        canvas.AddImageFittedIntoRectangle(imageData,
            new iText.Kernel.Geom.Rectangle(x, y, w, h), false);
        canvas.RestoreState();
    }

    private void BurnRedact(PdfCanvas canvas, PdfAnnotation ann,
        float x, float y, float w, float h)
    {
        canvas.SaveState();
        canvas.SetFillColor(ColorConstants.BLACK);
        canvas.Rectangle(x, y, w, h);
        canvas.Fill();
        canvas.RestoreState();
    }

    /// <summary>
    /// Applies blur effect to a specific region of a rendered page image.
    /// Returns the modified image bytes.
    /// </summary>
    public byte[] ApplyBlurToRegion(byte[] imageBytes, PdfAnnotation blurAnnotation,
        int imageWidth, int imageHeight)
    {
        using var image = new MagickImage(imageBytes);

        int x = (int)(blurAnnotation.X * imageWidth);
        int y = (int)(blurAnnotation.Y * imageHeight);
        int w = (int)(blurAnnotation.Width * imageWidth);
        int h = (int)(blurAnnotation.Height * imageHeight);

        // Clamp
        x = Math.Max(0, Math.Min(x, (int)image.Width - 1));
        y = Math.Max(0, Math.Min(y, (int)image.Height - 1));
        w = Math.Min(w, (int)image.Width - x);
        h = Math.Min(h, (int)image.Height - y);

        if (w <= 0 || h <= 0) return imageBytes;

        // Extract region, blur it, composite back
        var region = image.Clone(new MagickGeometry(x, y, (uint)w, (uint)h));
        region.GaussianBlur(blurAnnotation.BlurRadius, blurAnnotation.BlurRadius / 2.0);
        image.Composite(region, x, y, CompositeOperator.Over);

        var ms = new MemoryStream();
        image.Write(ms, MagickFormat.Png);
        return ms.ToArray();
    }

    private static DeviceRgb ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length < 6) hex = "000000";
        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return new DeviceRgb(r, g, b);
    }
}
