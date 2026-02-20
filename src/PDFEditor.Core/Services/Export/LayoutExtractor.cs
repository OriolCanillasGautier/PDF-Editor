using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using NLog;
using PDFEditor.Core.Models.Layout;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Extracts character-level glyph positions and vector paths (lines) from a PDF page
/// using iText7's event listener system. This is the "eyes" of the layout reconstruction
/// engine — the raw positional data for every glyph and drawn line on a page.
/// </summary>
public class LayoutExtractor
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Result of extracting layout data from a single PDF page.
    /// </summary>
    public class PageLayoutData
    {
        public int PageNumber { get; set; }
        public float PageWidth { get; set; }
        public float PageHeight { get; set; }
        public List<LayoutCharacter> Characters { get; set; } = new();
        public List<PdfLine> Lines { get; set; } = new();
        public List<ExtractedImageInfo> Images { get; set; } = new();
    }

    /// <summary>
    /// Extracted image metadata and bytes.
    /// </summary>
    public class ExtractedImageInfo
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public float Width { get; set; }
        public float Height { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }

    /// <summary>
    /// Extracts layout data from the specified pages of a PDF.
    /// </summary>
    /// <param name="pdfBytes">Raw PDF bytes.</param>
    /// <param name="pageIndices">0-based page indices to extract. Null = all pages.</param>
    /// <returns>Per-page layout data.</returns>
    public List<PageLayoutData> ExtractPages(byte[] pdfBytes, int[]? pageIndices = null)
    {
        var results = new List<PageLayoutData>();

        using var reader = new PdfReader(new System.IO.MemoryStream(pdfBytes));
        using var pdfDoc = new PdfDocument(reader);

        int total = pdfDoc.GetNumberOfPages();
        int[] indices = pageIndices ?? Enumerable.Range(0, total).ToArray();

        foreach (int idx in indices)
        {
            int pageNum = idx + 1; // iText7 is 1-based
            if (pageNum < 1 || pageNum > total)
            {
                Log.Warn("Skipping invalid page index {Index} (document has {Total} pages)", idx, total);
                continue;
            }

            var page = pdfDoc.GetPage(pageNum);
            var pageSize = page.GetPageSize();

            var charListener = new CharacterExtractionListener();
            var pathListener = new PathExtractionListener();
            var imgListener = new ImageExtractionListener();
            var composite = new CompositeListener(charListener, pathListener, imgListener);

            new PdfCanvasProcessor(composite).ProcessPageContent(page);

            results.Add(new PageLayoutData
            {
                PageNumber = pageNum,
                PageWidth = pageSize.GetWidth(),
                PageHeight = pageSize.GetHeight(),
                Characters = charListener.Characters,
                Lines = pathListener.Lines,
                Images = imgListener.Images
            });

            Log.Debug("Page {Page}: {Chars} characters, {Lines} lines, {Images} images extracted",
                pageNum, charListener.Characters.Count, pathListener.Lines.Count, imgListener.Images.Count);
        }

        return results;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Character-level extraction listener
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Listens for RENDER_TEXT events and breaks each chunk into individual characters
    /// with precise bounding boxes computed from font ascent/descent.
    /// </summary>
    private class CharacterExtractionListener : IEventListener
    {
        public List<LayoutCharacter> Characters { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT) return;

            var renderInfo = (TextRenderInfo)data;
            var text = renderInfo.GetText();
            if (string.IsNullOrEmpty(text)) return;

            var font = renderInfo.GetFont();
            var fontName = font?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? "Unknown";
            
            bool isBold = fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                          fontName.Contains("Heavy", StringComparison.OrdinalIgnoreCase) ||
                          fontName.Contains("Black", StringComparison.OrdinalIgnoreCase);
            bool isItalic = fontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                            fontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

            string colorHex = "#000000";
            try
            {
                var color = renderInfo.GetFillColor();
                if (color != null)
                {
                    var rgb = color.GetColorValue();
                    if (rgb != null && rgb.Length >= 3)
                    {
                        int r = (int)(rgb[0] * 255);
                        int g = (int)(rgb[1] * 255);
                        int b = (int)(rgb[2] * 255);
                        colorHex = $"#{r:X2}{g:X2}{b:X2}";
                    }
                }
            }
            catch { /* ignore color extraction errors */ }

            // Try character-level extraction first (most precise)
            try
            {
                var characterBoxes = renderInfo.GetCharacterRenderInfos();
                foreach (var charInfo in characterBoxes)
                {
                    var charText = charInfo.GetText();
                    if (string.IsNullOrEmpty(charText)) continue;

                    var ascentLine = charInfo.GetAscentLine();
                    var descentLine = charInfo.GetDescentLine();

                    float x = descentLine.GetStartPoint().Get(0);
                    float y = descentLine.GetStartPoint().Get(1);
                    float width = ascentLine.GetEndPoint().Get(0) - ascentLine.GetStartPoint().Get(0);
                    float height = ascentLine.GetStartPoint().Get(1) - descentLine.GetStartPoint().Get(1);

                    // Guard against degenerate boxes
                    if (width < 0) { x += width; width = Math.Abs(width); }
                    if (height < 0) { y += height; height = Math.Abs(height); }

                    float fontSize = height; // Best approximation from ascent - descent
                    try { fontSize = charInfo.GetFontSize(); } catch { /* keep ascent-descent */ }

                    Characters.Add(new LayoutCharacter
                    {
                        Char = charText[0],
                        BBox = new PdfRect(x, y, Math.Max(width, 0.1f), Math.Max(height, 0.1f)),
                        FontName = fontName,
                        FontSize = fontSize,
                        Color = colorHex,
                        IsBold = isBold,
                        IsItalic = isItalic
                    });
                }
            }
            catch
            {
                // Fallback: treat entire chunk as one positioned block and synthesize character boxes
                FallbackChunkExtraction(renderInfo, text, fontName, colorHex, isBold, isItalic);
            }
        }

        private void FallbackChunkExtraction(TextRenderInfo renderInfo, string text, string fontName, string colorHex, bool isBold, bool isItalic)
        {
            try
            {
                var ascentLine = renderInfo.GetAscentLine();
                var descentLine = renderInfo.GetDescentLine();

                float x = descentLine.GetStartPoint().Get(0);
                float y = descentLine.GetStartPoint().Get(1);
                float totalWidth = ascentLine.GetEndPoint().Get(0) - ascentLine.GetStartPoint().Get(0);
                float height = ascentLine.GetStartPoint().Get(1) - descentLine.GetStartPoint().Get(1);

                float fontSize = 12f;
                try { fontSize = renderInfo.GetFontSize(); } catch { /* keep default */ }

                // Distribute characters evenly across the total width
                float charWidth = text.Length > 0 ? totalWidth / text.Length : totalWidth;

                for (int i = 0; i < text.Length; i++)
                {
                    Characters.Add(new LayoutCharacter
                    {
                        Char = text[i],
                        BBox = new PdfRect(x + (i * charWidth), y, Math.Max(charWidth, 0.1f), Math.Max(height, 0.1f)),
                        FontName = fontName,
                        FontSize = fontSize,
                        Color = colorHex,
                        IsBold = isBold,
                        IsItalic = isItalic
                    });
                }
            }
            catch (Exception ex)
            {
                LogManager.GetCurrentClassLogger().Debug(ex, "Fallback character extraction failed for chunk");
            }
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_TEXT };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Vector path extraction listener (for table borders)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Listens for RENDER_PATH events to extract horizontal and vertical lines
    /// (typically drawn as table borders or ruling lines).
    /// Parses path operators: MoveTo (m), LineTo (l), Rectangle (re).
    /// </summary>
    private class PathExtractionListener : IEventListener
    {
        public List<PdfLine> Lines { get; } = new();

        // Minimum length (in points) for a line to be considered a table border
        private const float MinLineLength = 10f;

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_PATH) return;

            try
            {
                var pathInfo = (PathRenderInfo)data;

                // We care about stroked paths (borders) and filled rects (shaded cells)
                int op = pathInfo.GetOperation();
                if (op != PathRenderInfo.STROKE
                    && op != PathRenderInfo.FILL
                    && op != (PathRenderInfo.STROKE | PathRenderInfo.FILL))
                    return;

                var path = pathInfo.GetPath();
                var ctm = pathInfo.GetCtm();

                foreach (var subpath in path.GetSubpaths())
                {
                    var segments = subpath.GetSegments();
                    if (segments == null || segments.Count == 0) continue;

                    var startPoint = subpath.GetStartPoint();
                    float curX = (float)startPoint.GetX();
                    float curY = (float)startPoint.GetY();

                    // Apply CTM to the start point
                    TransformPoint(ctm, ref curX, ref curY);

                    foreach (var segment in segments)
                    {
                        var basePoints = segment.GetBasePoints();
                        if (basePoints == null || basePoints.Count == 0) continue;

                        // A line segment has exactly 2 base points: start and end
                        if (basePoints.Count == 2)
                        {
                            float endX = (float)basePoints[1].GetX();
                            float endY = (float)basePoints[1].GetY();
                            TransformPoint(ctm, ref endX, ref endY);

                            var line = new PdfLine { X1 = curX, Y1 = curY, X2 = endX, Y2 = endY };

                            // Only keep straight horizontal or vertical lines longer than threshold
                            if ((line.IsHorizontal || line.IsVertical) && LineLength(line) >= MinLineLength)
                            {
                                Lines.Add(line);
                            }

                            curX = endX;
                            curY = endY;
                        }
                        else
                        {
                            // Bezier curves or other complex segments — move cursor to end point
                            var lastPt = basePoints[basePoints.Count - 1];
                            curX = (float)lastPt.GetX();
                            curY = (float)lastPt.GetY();
                            TransformPoint(ctm, ref curX, ref curY);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.GetCurrentClassLogger().Debug(ex, "Failed to extract path from PDF page");
            }
        }

        private static void TransformPoint(iText.Kernel.Geom.Matrix ctm, ref float x, ref float y)
        {
            float newX = ctm.Get(iText.Kernel.Geom.Matrix.I11) * x
                       + ctm.Get(iText.Kernel.Geom.Matrix.I21) * y
                       + ctm.Get(iText.Kernel.Geom.Matrix.I31);
            float newY = ctm.Get(iText.Kernel.Geom.Matrix.I12) * x
                       + ctm.Get(iText.Kernel.Geom.Matrix.I22) * y
                       + ctm.Get(iText.Kernel.Geom.Matrix.I32);
            x = newX;
            y = newY;
        }

        private static float LineLength(PdfLine line)
        {
            float dx = line.X2 - line.X1;
            float dy = line.Y2 - line.Y1;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_PATH };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Image extraction listener
    // ──────────────────────────────────────────────────────────────────────────

    private class ImageExtractionListener : IEventListener
    {
        public List<ExtractedImageInfo> Images { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_IMAGE) return;
            try
            {
                var info = (ImageRenderInfo)data;
                var img = info.GetImage();
                if (img == null) return;
                var bytes = img.GetImageBytes(true);
                if (bytes == null || bytes.Length < 100) return;

                var ctm = info.GetImageCtm();
                Images.Add(new ExtractedImageInfo
                {
                    Data = bytes,
                    Width = ctm.Get(0),
                    Height = ctm.Get(4),
                    X = ctm.Get(6),
                    Y = ctm.Get(7)
                });
            }
            catch (Exception ex)
            {
                LogManager.GetCurrentClassLogger().Debug(ex, "Failed to extract PDF image");
            }
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_IMAGE };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Composite listener (routes events to multiple sub-listeners)
    // ──────────────────────────────────────────────────────────────────────────

    private class CompositeListener : IEventListener
    {
        private readonly IEventListener[] _listeners;
        public CompositeListener(params IEventListener[] listeners) => _listeners = listeners;

        public void EventOccurred(IEventData data, EventType type)
        {
            foreach (var listener in _listeners)
                if (listener.GetSupportedEvents().Contains(type))
                    listener.EventOccurred(data, type);
        }

        public ICollection<EventType> GetSupportedEvents()
        {
            var all = new HashSet<EventType>();
            foreach (var l in _listeners)
                foreach (var e in l.GetSupportedEvents())
                    all.Add(e);
            return all;
        }
    }
}
