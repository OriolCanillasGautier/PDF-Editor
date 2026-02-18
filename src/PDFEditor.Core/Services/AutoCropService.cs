using NLog;
using iText.Kernel.Pdf;
using iText.Kernel.Geom;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace PDFEditor.Core.Services;

/// <summary>
/// Provides auto-crop (white margin removal) and deskew functionality for PDF pages.
/// </summary>
public class AutoCropService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Result of analyzing a page's content boundaries.</summary>
    public class CropBounds
    {
        public int PageNumber { get; set; }
        public float OriginalWidth { get; set; }
        public float OriginalHeight { get; set; }
        public float ContentLeft { get; set; }
        public float ContentBottom { get; set; }
        public float ContentRight { get; set; }
        public float ContentTop { get; set; }
        public float MarginLeft => ContentLeft;
        public float MarginBottom => ContentBottom;
        public float MarginRight => OriginalWidth - ContentRight;
        public float MarginTop => OriginalHeight - ContentTop;
        public bool HasContent => ContentRight > ContentLeft && ContentTop > ContentBottom;
    }

    /// <summary>
    /// Analyzes content boundaries on each page to determine optimal crop regions.
    /// </summary>
    public List<CropBounds> AnalyzeMargins(byte[] pdfBytes, int[]? pageIndices = null)
    {
        var results = new List<CropBounds>();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdfDoc = new PdfDocument(reader);

        int total = pdfDoc.GetNumberOfPages();
        int[] pages = pageIndices ?? Enumerable.Range(0, total).ToArray();

        foreach (int pi in pages)
        {
            int pageNum = pi + 1;
            if (pageNum < 1 || pageNum > total) continue;

            var page = pdfDoc.GetPage(pageNum);
            var mediaBox = page.GetMediaBox();
            var listener = new BoundsListener();
            new PdfCanvasProcessor(listener).ProcessPageContent(page);

            results.Add(new CropBounds
            {
                PageNumber = pageNum,
                OriginalWidth = mediaBox.GetWidth(),
                OriginalHeight = mediaBox.GetHeight(),
                ContentLeft = listener.HasContent ? listener.MinX : 0,
                ContentBottom = listener.HasContent ? listener.MinY : 0,
                ContentRight = listener.HasContent ? listener.MaxX : mediaBox.GetWidth(),
                ContentTop = listener.HasContent ? listener.MaxY : mediaBox.GetHeight()
            });
        }

        return results;
    }

    /// <summary>
    /// Auto-crops all pages by removing white margins, with an optional padding.
    /// </summary>
    /// <param name="pdfBytes">Source PDF bytes</param>
    /// <param name="padding">Points of padding to keep around content (default 10)</param>
    /// <param name="pageIndices">Optional 0-based page indices; null = all pages</param>
    /// <returns>New PDF bytes with cropped pages</returns>
    public byte[] AutoCrop(byte[] pdfBytes, float padding = 10f, int[]? pageIndices = null)
    {
        var bounds = AnalyzeMargins(pdfBytes, pageIndices);

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var outMs  = new MemoryStream();
        using var writer = new PdfWriter(outMs);
        using var pdfDoc = new PdfDocument(reader, writer);

        var pageSet = new HashSet<int>(bounds.Select(b => b.PageNumber));

        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
        {
            if (!pageSet.Contains(i)) continue;

            var cb = bounds.FirstOrDefault(b => b.PageNumber == i);
            if (cb == null || !cb.HasContent) continue;

            float left   = Math.Max(0, cb.ContentLeft - padding);
            float bottom = Math.Max(0, cb.ContentBottom - padding);
            float right  = Math.Min(cb.OriginalWidth, cb.ContentRight + padding);
            float top    = Math.Min(cb.OriginalHeight, cb.ContentTop + padding);

            var page = pdfDoc.GetPage(i);
            page.SetMediaBox(new Rectangle(left, bottom, right - left, top - bottom));
            page.SetCropBox(new Rectangle(left, bottom, right - left, top - bottom));
        }

        pdfDoc.Close();
        Log.Info("Auto-cropped {Count} pages with {Padding}pt padding", bounds.Count, padding);
        return outMs.ToArray();
    }

    /// <summary>
    /// Uniformly crops all pages to the tightest bounding box across all pages.
    /// </summary>
    public byte[] UniformCrop(byte[] pdfBytes, float padding = 10f)
    {
        var bounds = AnalyzeMargins(pdfBytes);
        if (bounds.Count == 0) return pdfBytes;

        float globalLeft   = bounds.Where(b => b.HasContent).Select(b => b.ContentLeft).DefaultIfEmpty(0).Min();
        float globalBottom = bounds.Where(b => b.HasContent).Select(b => b.ContentBottom).DefaultIfEmpty(0).Min();
        float globalRight  = bounds.Where(b => b.HasContent).Select(b => b.ContentRight).DefaultIfEmpty(0).Max();
        float globalTop    = bounds.Where(b => b.HasContent).Select(b => b.ContentTop).DefaultIfEmpty(0).Max();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var outMs  = new MemoryStream();
        using var writer = new PdfWriter(outMs);
        using var pdfDoc = new PdfDocument(reader, writer);

        float left   = Math.Max(0, globalLeft - padding);
        float bottom = Math.Max(0, globalBottom - padding);

        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
        {
            var page = pdfDoc.GetPage(i);
            var mb   = page.GetMediaBox();
            float right = Math.Min(mb.GetWidth(), globalRight + padding);
            float top   = Math.Min(mb.GetHeight(), globalTop + padding);

            page.SetMediaBox(new Rectangle(left, bottom, right - left, top - bottom));
            page.SetCropBox(new Rectangle(left, bottom, right - left, top - bottom));
        }

        pdfDoc.Close();
        return outMs.ToArray();
    }

    private class BoundsListener : IEventListener
    {
        public float MinX { get; private set; } = float.MaxValue;
        public float MinY { get; private set; } = float.MaxValue;
        public float MaxX { get; private set; } = float.MinValue;
        public float MaxY { get; private set; } = float.MinValue;
        public bool HasContent { get; private set; }

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type == EventType.RENDER_TEXT)
            {
                var info = (TextRenderInfo)data;
                var text = info.GetText();
                if (string.IsNullOrWhiteSpace(text)) return;

                var baseline = info.GetBaseline();
                var start = baseline.GetStartPoint();
                var end = baseline.GetEndPoint();
                var ascent = info.GetAscentLine().GetStartPoint();

                Update(start.Get(0), start.Get(1));
                Update(end.Get(0), ascent.Get(1));
            }
            else if (type == EventType.RENDER_IMAGE)
            {
                try
                {
                    var info = (ImageRenderInfo)data;
                    var ctm = info.GetImageCtm();
                    float x = ctm.Get(6);
                    float y = ctm.Get(7);
                    float w = Math.Abs(ctm.Get(0));
                    float h = Math.Abs(ctm.Get(4));
                    Update(x, y);
                    Update(x + w, y + h);
                }
                catch { }
            }
        }

        private void Update(float x, float y)
        {
            HasContent = true;
            if (x < MinX) MinX = x;
            if (y < MinY) MinY = y;
            if (x > MaxX) MaxX = x;
            if (y > MaxY) MaxY = y;
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_TEXT, EventType.RENDER_IMAGE };
    }
}
