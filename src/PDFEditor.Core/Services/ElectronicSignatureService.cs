using ImageMagick;
using NLog;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Geom;
using iText.Kernel.Pdf.Xobject;
using iText.IO.Image;

namespace PDFEditor.Core.Services;

/// <summary>
/// Provides electronic (image-based) signature functionality — draw, type, upload, and embed.
/// These are visual signatures (not cryptographic), suitable for informal approval workflows.
/// For legally-binding signatures, use PdfSignatureService (certificate-based PKCS#12).
/// </summary>
public class ElectronicSignatureService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Electronic signature metadata.</summary>
    public class ElectronicSignature
    {
        public string SignerName { get; set; } = "";
        public DateTime SignedDate { get; set; } = DateTime.UtcNow;
        public string Reason { get; set; } = "";
        public string Location { get; set; } = "";
        /// <summary>Signature image bytes (PNG preferred, with transparency).</summary>
        public byte[] SignatureImage { get; set; } = Array.Empty<byte>();
        /// <summary>Horizontal position as fraction of page width (0.0–1.0).</summary>
        public float X { get; set; } = 0.6f;
        /// <summary>Vertical position as fraction of page height (0.0–1.0).</summary>
        public float Y { get; set; } = 0.1f;
        /// <summary>Width as fraction of page width (0.0–1.0).</summary>
        public float Width { get; set; } = 0.25f;
        /// <summary>Height as fraction of page height (0.0–1.0).</summary>
        public float Height { get; set; } = 0.06f;
    }

    /// <summary>
    /// Generates a typed signature image from text (e.g., name) rendered as dark blue text on transparent background.
    /// </summary>
    public byte[] CreateTypedSignature(string name, string fontFamily = "DejaVu Sans", int fontSize = 48)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Signer name cannot be empty", nameof(name));

        // Approximate width based on character count
        int approxWidth = Math.Max(200, name.Length * (fontSize * 2 / 3) + 40);
        int height = fontSize + 30;

        // Create a white image and render text by writing each char as pixels
        // Since the Drawables API is not available, use MagickImage.Annotate or simple rendering
        using var image = new MagickImage(MagickColors.White, (uint)approxWidth, (uint)height);
        image.Settings.FontPointsize = fontSize;
        image.Settings.Font = fontFamily;
        image.Settings.FillColor = new MagickColor(0, 0, 24576, ushort.MaxValue); // dark blue (Q16)
        image.Settings.BackgroundColor = MagickColors.Transparent;

        image.Annotate(name, Gravity.West);
        image.Trim();

        // Replace white background with transparent
        image.ColorFuzz = new Percentage(10);
        image.Transparent(MagickColors.White);

        using var ms = new MemoryStream();
        image.Write(ms, MagickFormat.Png);
        return ms.ToArray();
    }

    /// <summary>
    /// Generates a drawn signature from a series of points (ink strokes).
    /// Each inner list represents a continuous stroke (points in 0.0-1.0 normalized coords).
    /// </summary>
    public byte[] CreateDrawnSignature(List<List<(float x, float y)>> strokes,
        int width = 400, int height = 120)
    {
        if (strokes == null || strokes.Count == 0 || strokes.All(s => s.Count == 0))
            throw new ArgumentException("At least one stroke with points is required", nameof(strokes));

        // Render strokes as dark blue lines on white, then make white transparent
        using var image = new MagickImage(MagickColors.White, (uint)width, (uint)height);
        using var pixels = image.GetPixels();
        var darkBlue = new ushort[] { 0, 0, 24576 }; // Q16 dark blue

        foreach (var stroke in strokes)
        {
            if (stroke.Count < 2) continue;
            for (int i = 0; i < stroke.Count - 1; i++)
            {
                // Bresenham-style line drawing between consecutive points
                int x0 = (int)(stroke[i].x * (width - 1));
                int y0 = (int)(stroke[i].y * (height - 1));
                int x1 = (int)(stroke[i + 1].x * (width - 1));
                int y1 = (int)(stroke[i + 1].y * (height - 1));
                DrawLine(pixels, x0, y0, x1, y1, darkBlue, width, height, strokeWidth: 3);
            }
        }

        image.Trim();

        // Replace white background with transparent
        image.ColorFuzz = new Percentage(10);
        image.Transparent(MagickColors.White);

        using var ms = new MemoryStream();
        image.Write(ms, MagickFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Simple line drawing using Bresenham's algorithm with thickness.</summary>
    private static void DrawLine(IPixelCollection<ushort> pixels, int x0, int y0, int x1, int y1,
        ushort[] color, int imgW, int imgH, int strokeWidth = 1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        int half = strokeWidth / 2;
        while (true)
        {
            // Draw a small square around the point for thickness
            for (int ox = -half; ox <= half; ox++)
                for (int oy = -half; oy <= half; oy++)
                {
                    int px = x0 + ox, py = y0 + oy;
                    if (px >= 0 && px < imgW && py >= 0 && py < imgH)
                        pixels.SetPixel(px, py, color);
                }

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>
    /// Embeds an electronic signature image onto a specific page of the PDF.
    /// </summary>
    public byte[] AddSignature(byte[] pdfBytes, ElectronicSignature sig, int pageIndex)
    {
        if (sig.SignatureImage.Length == 0)
            throw new ArgumentException("Signature image is required", nameof(sig));

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var outMs  = new MemoryStream();
        using var writer = new PdfWriter(outMs);
        using var pdfDoc = new PdfDocument(reader, writer);

        int pageNum = pageIndex + 1;
        if (pageNum < 1 || pageNum > pdfDoc.GetNumberOfPages())
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        var page = pdfDoc.GetPage(pageNum);
        var mediaBox = page.GetMediaBox();
        float pageW = mediaBox.GetWidth();
        float pageH = mediaBox.GetHeight();

        // Convert normalized coordinates to absolute
        float x = sig.X * pageW;
        float y = sig.Y * pageH;
        float w = sig.Width * pageW;
        float h = sig.Height * pageH;

        // Add image to page content
        var imgData = ImageDataFactory.Create(sig.SignatureImage);
        var imgXObj = new PdfImageXObject(imgData);

        var canvas = new iText.Kernel.Pdf.Canvas.PdfCanvas(page);
        canvas.SaveState();
        canvas.AddXObjectFittedIntoRectangle(imgXObj, new Rectangle(x, y, w, h));
        canvas.RestoreState();

        // Add metadata annotation (invisible, stores signer info)
        var sigDict = new PdfDictionary();
        sigDict.Put(new PdfName("SignerName"), new PdfString(sig.SignerName));
        sigDict.Put(new PdfName("SignedDate"), new PdfString(sig.SignedDate.ToString("O")));
        sigDict.Put(new PdfName("Reason"), new PdfString(sig.Reason));
        sigDict.Put(new PdfName("Location"), new PdfString(sig.Location));

        var annot = new PdfTextAnnotation(new Rectangle(x, y + h, 0, 0))
            .SetContents($"Signed by {sig.SignerName} on {sig.SignedDate:yyyy-MM-dd}");
        // Set flag 0x02 (Hidden) to make the annotation invisible
        annot.SetFlag(0x0002);
        page.AddAnnotation(annot);

        pdfDoc.Close();
        Log.Info("Electronic signature added by {Signer} on page {Page}",
            sig.SignerName, pageNum);
        return outMs.ToArray();
    }

    /// <summary>Validates that an image is suitable as a signature (PNG/JPEG, reasonable size).</summary>
    public (bool isValid, string message) ValidateSignatureImage(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            return (false, "Image data is empty");

        if (imageBytes.Length > 5 * 1024 * 1024)
            return (false, "Image too large (max 5 MB)");

        try
        {
            using var mgk = new MagickImage(imageBytes);
            if (mgk.Width > 4000 || mgk.Height > 4000)
                return (false, $"Image dimensions too large ({mgk.Width}x{mgk.Height}). Maximum 4000x4000.");

            return (true, "Valid signature image");
        }
        catch
        {
            return (false, "Unrecognized image format");
        }
    }
}
