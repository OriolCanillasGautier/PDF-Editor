using Docnet.Core;
using Docnet.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using ImageMagick;
using iText.Kernel.Pdf;
using NLog;
using PDFEditor.Core.Abstractions;
using System.IO.Compression;
using System.Text;

// Disambiguate conflicting OpenXml/Drawing names
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports each PDF page as a full-page PNG image embedded in a PowerPoint (.pptx) slide.
/// One slide per page. Slides are sized to match the PDF page dimensions (in EMUs).
/// </summary>
public class PptxExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // EMU = English Metric Units: 914400 EMU per inch, 12700 EMU per point
    private const long EmuPerPt = 12700L;

    public string FormatName => "PowerPoint (PPTX)";
    public string[] SupportedExtensions => new[] { ".pptx" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    // ─── IExportProvider ────────────────────────────────────────────────────────

    public async Task<ExportResult> ExportAsync(
        byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        Log.Info("PPTX export started — {Bytes} bytes", pdfBytes.Length);

        return await Task.Run(() =>
        {
            try
            {
                int dpi = Math.Max(96, options.Dpi);

                using var iTextMs = new MemoryStream(pdfBytes, writable: false);
                using var reader = new PdfReader(iTextMs);
                using var pdfDoc = new PdfDocument(reader);

                int pageCount = pdfDoc.GetNumberOfPages();
                int[]? indices = options.PageIndices;
                if (indices == null || indices.Length == 0)
                    indices = Enumerable.Range(0, pageCount).ToArray();

                // Use first indexed page dimensions for slide size (or default 16:9)
                var firstPage = pdfDoc.GetPage(indices[0] + 1);
                var mediaBox = firstPage.GetMediaBox();
                long slideWidthEmu  = (long)(mediaBox.GetWidth()  * EmuPerPt);
                long slideHeightEmu = (long)(mediaBox.GetHeight() * EmuPerPt);

                using var outMs = new MemoryStream();
                using (var prs = PresentationDocument.Create(outMs, PresentationDocumentType.Presentation, autoSave: false))
                {
                    BuildPresentation(prs, pdfBytes, pdfDoc, indices, dpi, slideWidthEmu, slideHeightEmu, cancellationToken);
                    prs.Save();
                }

                byte[] pptxBytes = FixPptxContentTypes(outMs.ToArray());
                string fileName  = $"{options.BaseFileName}.pptx";
                Log.Info("PPTX export complete — {Pages} slides, {Bytes} bytes", indices.Length, pptxBytes.Length);
                return ExportResult.Ok(pptxBytes, fileName, "application/vnd.openxmlformats-officedocument.presentationml.presentation");
            }
            catch (OperationCanceledException)
            {
                return ExportResult.Fail("Export cancelled.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PPTX export failed");
                return ExportResult.Fail($"PPTX export failed: {ex.Message}");
            }
        }, cancellationToken);
    }

    public Task<List<ExportResult>> ExportPagesAsync(
        byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExportAsync(pdfBytes, options, cancellationToken)
               .ContinueWith(t => new List<ExportResult> { t.Result }, cancellationToken);
    }

    // ─── Presentation builder ───────────────────────────────────────────────────

    private static void BuildPresentation(
        PresentationDocument prs,
        byte[] pdfBytes,
        PdfDocument pdfDoc,
        int[] indices,
        int dpi,
        long slideWidthEmu,
        long slideHeightEmu,
        CancellationToken ct)
    {
        var presPart = prs.AddPresentationPart();
        presPart.Presentation = new P.Presentation();

        // Slide size
        presPart.Presentation.SlideSize = new P.SlideSize
        {
            Cx = (Int32Value)(int)Math.Min(slideWidthEmu,  int.MaxValue),
            Cy = (Int32Value)(int)Math.Min(slideHeightEmu, int.MaxValue),
            Type = SlideSizeValues.Custom,
        };

        presPart.Presentation.SlideIdList        = new P.SlideIdList();
        presPart.Presentation.SlideMasterIdList  = new P.SlideMasterIdList();

        // Add a minimal slide master (required by PowerPoint)
        AddMinimalSlideMaster(presPart, slideWidthEmu, slideHeightEmu);

        uint slideId = 256;
        int imageSeq = 1;

        foreach (int idx in indices)
        {
            ct.ThrowIfCancellationRequested();

            int pageNum = idx + 1;

            // Render page to PNG bytes using Docnet + Magick.NET
            byte[] pngBytes = RenderPageToPng(pdfBytes, idx, dpi);

            // Add slide
            var slidePart = presPart.AddNewPart<SlidePart>();
            var imgPart   = slidePart.AddImagePart(ImagePartType.Png);
            using (var imgMs = new MemoryStream(pngBytes))
                imgPart.FeedData(imgMs);

            string imgRid = slidePart.GetIdOfPart(imgPart);

            slidePart.Slide = BuildSlide(imgRid, slideWidthEmu, slideHeightEmu, imageSeq++);
            slidePart.Slide.Save();

            // Register slide in presentation
            var slideRelId = presPart.GetIdOfPart(slidePart);
            presPart.Presentation.SlideIdList.Append(
                new P.SlideId { Id = slideId++, RelationshipId = slideRelId });
        }

        presPart.Presentation.Save();
    }

    private static P.Slide BuildSlide(string imageRelId, long widthEmu, long heightEmu, int imageIndex)
    {
        string imageName = $"pdfPage{imageIndex}";

        return new P.Slide(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new D.TransformGroup()),
                    new P.Picture(
                        new P.NonVisualPictureProperties(
                            new P.NonVisualDrawingProperties { Id = 2U, Name = imageName },
                            new P.NonVisualPictureDrawingProperties(
                                new D.PictureLocks { NoChangeAspect = true }),
                            new P.ApplicationNonVisualDrawingProperties()),
                        new P.BlipFill(
                            new D.Blip { Embed = imageRelId },
                            new D.Stretch(new D.FillRectangle())),
                        new P.ShapeProperties(
                            new D.Transform2D(
                                new D.Offset { X = 0L, Y = 0L },
                                new D.Extents { Cx = (Int64Value)(int)Math.Min(widthEmu,  int.MaxValue),
                                                Cy = (Int64Value)(int)Math.Min(heightEmu, int.MaxValue) }),
                            new D.PresetGeometry { Preset = D.ShapeTypeValues.Rectangle })))),
            new P.ColorMapOverride(new D.MasterColorMapping()));
    }

    private static void AddMinimalSlideMaster(PresentationPart presPart, long widthEmu, long heightEmu)
    {
        var masterPart = presPart.AddNewPart<SlideMasterPart>();

        masterPart.SlideMaster = new P.SlideMaster(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new D.TransformGroup()))),
            new P.ColorMap
            {
                Background1   = D.ColorSchemeIndexValues.Light1,
                Text1         = D.ColorSchemeIndexValues.Dark1,
                Background2   = D.ColorSchemeIndexValues.Light2,
                Text2         = D.ColorSchemeIndexValues.Dark2,
                Accent1       = D.ColorSchemeIndexValues.Accent1,
                Accent2       = D.ColorSchemeIndexValues.Accent2,
                Accent3       = D.ColorSchemeIndexValues.Accent3,
                Accent4       = D.ColorSchemeIndexValues.Accent4,
                Accent5       = D.ColorSchemeIndexValues.Accent5,
                Accent6       = D.ColorSchemeIndexValues.Accent6,
                Hyperlink     = D.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = D.ColorSchemeIndexValues.FollowedHyperlink,
            },
            new P.SlideLayoutIdList());
        masterPart.SlideMaster.Save();

        // Add slide master theme (minimal)
        var themePart = masterPart.AddNewPart<ThemePart>();
        themePart.Theme = BuildMinimalTheme();
        themePart.Theme.Save();

        // Add a blank slide layout (required)
        var layoutPart = masterPart.AddNewPart<SlideLayoutPart>();
        masterPart.SlideMaster.SlideLayoutIdList!.Append(
            new P.SlideLayoutId { RelationshipId = masterPart.GetIdOfPart(layoutPart), Id = 2049U });
        layoutPart.SlideLayout = new P.SlideLayout(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new D.TransformGroup()))),
            new P.ColorMapOverride(new D.MasterColorMapping()));
        layoutPart.SlideLayout.Save();
        layoutPart.AddPart(masterPart); // layout → master back-link

        // Register master in presentation
        presPart.Presentation.SlideMasterIdList!.Append(
            new P.SlideMasterId
            {
                RelationshipId = presPart.GetIdOfPart(masterPart),
                Id = 2147483648U,
            });
    }

    private static D.Theme BuildMinimalTheme()
    {
        return new D.Theme(
            new D.ThemeElements(
                new D.ColorScheme(
                    new D.Dark1Color(new D.SystemColor { LastColor = "000000", Val = D.SystemColorValues.WindowText }),
                    new D.Light1Color(new D.SystemColor { LastColor = "FFFFFF", Val = D.SystemColorValues.Window }),
                    new D.Dark2Color(new D.RgbColorModelHex { Val = "44546A" }),
                    new D.Light2Color(new D.RgbColorModelHex { Val = "E7E6E6" }),
                    new D.Accent1Color(new D.RgbColorModelHex { Val = "4472C4" }),
                    new D.Accent2Color(new D.RgbColorModelHex { Val = "ED7D31" }),
                    new D.Accent3Color(new D.RgbColorModelHex { Val = "A9D18E" }),
                    new D.Accent4Color(new D.RgbColorModelHex { Val = "FFC000" }),
                    new D.Accent5Color(new D.RgbColorModelHex { Val = "5A96CF" }),
                    new D.Accent6Color(new D.RgbColorModelHex { Val = "70AD47" }),
                    new D.Hyperlink(new D.RgbColorModelHex { Val = "0563C1" }),
                    new D.FollowedHyperlinkColor(new D.RgbColorModelHex { Val = "954F72" }))
                { Name = "PDF Editor" },
                new D.FontScheme(
                    new D.MajorFont(new D.LatinFont { Typeface = "Calibri Light" },
                                   new D.EastAsianFont { Typeface = string.Empty },
                                   new D.ComplexScriptFont { Typeface = string.Empty }),
                    new D.MinorFont(new D.LatinFont { Typeface = "Calibri" },
                                   new D.EastAsianFont { Typeface = string.Empty },
                                   new D.ComplexScriptFont { Typeface = string.Empty }))
                { Name = "Office Theme" },
                new D.FormatScheme(
                    new D.FillStyleList(
                        new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.PhColor }),
                        new D.GradientFill(new D.GradientStopList(
                            new D.GradientStop(new D.SchemeColor { Val = D.SchemeColorValues.PhColor }) { Position = 0 },
                            new D.GradientStop(new D.SchemeColor { Val = D.SchemeColorValues.PhColor }) { Position = 100000 }),
                            new D.LinearGradientFill { Angle = 16200000, Scaled = false }),
                        new D.NoFill()),
                    new D.LineStyleList(
                        new D.Outline(new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.PhColor })) { Width = 6350 },
                        new D.Outline(new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.PhColor })) { Width = 12700 },
                        new D.Outline(new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.PhColor })) { Width = 19050 }),
                    new D.EffectStyleList(
                        new D.EffectStyle(new D.EffectList()),
                        new D.EffectStyle(new D.EffectList()),
                        new D.EffectStyle(new D.EffectList())),
                    new D.BackgroundFillStyleList(
                        new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.PhColor }),
                        new D.NoFill(),
                        new D.NoFill()))
                { Name = "Office" }))
        { Name = "PDF Editor Theme" };
    }

    // ─── Page rendering ─────────────────────────────────────────────────────────

    private static byte[] RenderPageToPng(byte[] pdfBytes, int pageIndex, int dpi)
    {
        // Pixel dimensions: approx. (dpi * pageWidthIn)
        // Use fixed max dimensions proportional to DPI
        int maxDim = (int)(dpi * 11); // ~11 inches at chosen DPI

        using var lib = DocLib.Instance;
        using var docReader = lib.GetDocReader(pdfBytes, new PageDimensions(maxDim, maxDim));
        using var pageReader = docReader.GetPageReader(pageIndex);

        int w = pageReader.GetPageWidth();
        int h = pageReader.GetPageHeight();
        byte[] bgra = pageReader.GetImage();

        // Alpha-composite onto white
        for (int i = 0; i < bgra.Length; i += 4)
        {
            byte a = bgra[i + 3];
            if (a < 255)
            {
                float alpha  = a / 255f;
                float iAlpha = 1f - alpha;
                bgra[i]     = (byte)(bgra[i]     * alpha + 255 * iAlpha);
                bgra[i + 1] = (byte)(bgra[i + 1] * alpha + 255 * iAlpha);
                bgra[i + 2] = (byte)(bgra[i + 2] * alpha + 255 * iAlpha);
                bgra[i + 3] = 255;
            }
        }

        // Convert BGRA → PNG via Magick.NET using PixelReadSettings for raw pixels
        var pixelSettings = new PixelReadSettings((uint)w, (uint)h, StorageType.Char, PixelMapping.BGRA);

        using var img = new MagickImage();
        img.ReadPixels(bgra, pixelSettings);
        img.Format = MagickFormat.Png;

        using var outMs = new MemoryStream();
        img.Write(outMs);
        return outMs.ToArray();
    }

    // ─── Content-Types fix (same SDK v3 regression as DOCX) ────────────────────

    private static byte[] FixPptxContentTypes(byte[] pptxBytes)
    {
        // The DocumentFormat.OpenXml SDK v3.0.x may set wrong Default content type
        // for .xml entries. For PPTX we ensure the presentation main part gets an Override.
        const string presOverride =
            "<Override PartName=\"/ppt/presentation.xml\" " +
            "ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\" />";
        const string wrongDefault = "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml";
        const string correctXml   = "application/xml";

        using var inMs  = new MemoryStream(pptxBytes, writable: false);
        using var outMs = new MemoryStream();

        using (var inZip  = new ZipArchive(inMs,  ZipArchiveMode.Read,   leaveOpen: true))
        using (var outZip = new ZipArchive(outMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in inZip.Entries)
            {
                var outEntry = outZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                outEntry.LastWriteTime = entry.LastWriteTime;
                using var src = entry.Open();
                using var dst = outEntry.Open();

                if (entry.FullName == "[Content_Types].xml")
                {
                    using var sr = new StreamReader(src, Encoding.UTF8);
                    var xml = sr.ReadToEnd();

                    // Fix SDK v3 regression: wrong Default type for xml files
                    if (xml.Contains($"<Default Extension=\"xml\" ContentType=\"{wrongDefault}\""))
                    {
                        xml = xml.Replace(
                            $"<Default Extension=\"xml\" ContentType=\"{wrongDefault}\" />",
                            $"<Default Extension=\"xml\" ContentType=\"{correctXml}\" />");

                        // Ensure Override for presentation.xml is present
                        if (!xml.Contains("PartName=\"/ppt/presentation.xml\""))
                        {
                            xml = xml.Replace("</Types>", $"{presOverride}</Types>");
                        }
                    }

                    var bytes = Encoding.UTF8.GetBytes(xml);
                    dst.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    src.CopyTo(dst);
                }
            }
        }

        return outMs.ToArray();
    }
}
