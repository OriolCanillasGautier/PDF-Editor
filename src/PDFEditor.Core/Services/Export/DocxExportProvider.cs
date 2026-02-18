using ImageMagick;
using NLog;
using PDFEditor.Core.Abstractions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.IO.Compression;
using System.Text;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Exports PDF content to Microsoft Word DOCX format.
/// Builds the DOCX ZIP entirely from hand-crafted XML strings — no DocumentFormat.OpenXml SDK —
/// guaranteeing that Word can always open the result without repair dialogs.
/// </summary>
public class DocxExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // ──────────────────────────────────────────────────────────────────────────
    // OOXML namespace URIs
    // ──────────────────────────────────────────────────────────────────────────
    private const string NsOpcCt  = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string NsOpcRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string NsOffRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsW      = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string NsWp     = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string NsA      = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string NsPic    = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    // Relationship type URIs used in .rels files
    private const string RelTypeDocument   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string RelTypeStyles     = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    private const string RelTypeSettings   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";
    private const string RelTypeFontTable  = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable";
    private const string RelTypeWebSettings = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/webSettings";
    private const string RelTypeImage      = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

    // rId1 = styles, rId2 = settings, rId3 = fontTable, rId4 = webSettings, images start at rId5
    private const int ImageRIdBase = 5;

    // ──────────────────────────────────────────────────────────────────────────
    // IExportProvider
    // ──────────────────────────────────────────────────────────────────────────

    public string   FormatName          => "Microsoft Word (DOCX)";
    public string[] SupportedExtensions => new[] { ".docx" };
    public bool     SupportsBatch       => true;
    public bool     SupportsPerPageExport => false;

    public async Task<ExportResult> ExportAsync(
        byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var docxBytes = await Task.Run(
                () => GenerateDocxRaw(pdfBytes, options, cancellationToken),
                cancellationToken);

            return ExportResult.Ok(
                docxBytes,
                $"{options.BaseFileName}.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DOCX export failed");
            return ExportResult.Fail(ex.Message);
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(
        byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("DOCX export produces a single document. Use ExportAsync instead.");

    // ──────────────────────────────────────────────────────────────────────────
    // Internal models
    // ──────────────────────────────────────────────────────────────────────────

    private class TextChunkInfo
    {
        public string Text     { get; set; } = string.Empty;
        public float  X        { get; set; }
        public float  Y        { get; set; }
        public float  FontSize { get; set; }
        public string FontName { get; set; } = string.Empty;
        public bool   IsBold   { get; set; }
        public bool   IsItalic { get; set; }
    }

    private class ExtractedImage
    {
        public byte[] Data   { get; set; } = Array.Empty<byte>();
        public float  Width  { get; set; }
        public float  Height { get; set; }
    }

    private class TextLine
    {
        public float               Y                   { get; set; }
        public float               PredominantFontSize { get; set; }
        public bool                PredominantBold     { get; set; }
        public bool                PredominantItalic   { get; set; }
        public string              Text                { get; set; } = string.Empty;
        public List<TextChunkInfo> Chunks              { get; set; } = new();
    }

    private class ContentBlock
    {
        public ContentBlockType  BlockType    { get; set; }
        public int               HeadingLevel { get; set; }
        public List<TextLine>    Lines        { get; set; } = new();
        public bool              IsBold       { get; set; }
        public bool              IsItalic     { get; set; }
        public float             FontSize     { get; set; }
    }

    private enum ContentBlockType { Paragraph, Heading }

    private class DetectedTable
    {
        public float              TopY             { get; set; }
        public float              BottomY          { get; set; }
        public List<float>        ColumnBoundaries { get; set; } = new();
        public List<List<string>> Rows             { get; set; } = new();
    }

    /// <summary>Represents a single image binary to be written into the DOCX ZIP.</summary>
    private sealed record ImagePart(string RId, string ZipRelPath, byte[] Data, string ContentType);

    // ──────────────────────────────────────────────────────────────────────────
    // iText7 event listeners (unchanged from original — they work correctly)
    // ──────────────────────────────────────────────────────────────────────────

    private class TextExtractionListener : IEventListener
    {
        public List<TextChunkInfo> Chunks { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT) return;
            var info = (TextRenderInfo)data;
            var text = info.GetText();
            if (string.IsNullOrEmpty(text)) return;

            var start    = info.GetBaseline().GetStartPoint();
            var font     = info.GetFont();
            float fontSize = 12f;
            try
            {
                fontSize = info.GetAscentLine().GetStartPoint().Get(1)
                         - info.GetDescentLine().GetStartPoint().Get(1);
            }
            catch { /* use fallback 12 */ }

            var  fontName = font?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? "";
            bool bold     = fontName.Contains("Bold",    StringComparison.OrdinalIgnoreCase)
                         || fontName.Contains("Heavy",   StringComparison.OrdinalIgnoreCase)
                         || fontName.Contains("Black",   StringComparison.OrdinalIgnoreCase);
            bool italic   = fontName.Contains("Italic",  StringComparison.OrdinalIgnoreCase)
                         || fontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

            Chunks.Add(new TextChunkInfo
            {
                Text     = text,
                X        = start.Get(0),
                Y        = start.Get(1),
                FontSize = fontSize,
                FontName = fontName,
                IsBold   = bold,
                IsItalic = italic
            });
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_TEXT };
    }

    private class ImageExtractionListener : IEventListener
    {
        public List<ExtractedImage> Images { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_IMAGE) return;
            try
            {
                var info   = (ImageRenderInfo)data;
                var img    = info.GetImage();
                if (img == null) return;
                var bytes  = img.GetImageBytes(true);
                if (bytes == null || bytes.Length < 100) return;

                var m = info.GetImageCtm();
                Images.Add(new ExtractedImage { Data = bytes, Width = m.Get(0), Height = m.Get(4) });
            }
            catch (Exception ex)
            {
                LogManager.GetCurrentClassLogger().Debug(ex, "Failed to extract PDF image");
            }
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_IMAGE };
    }

    private class CompositeEventListener : IEventListener
    {
        private readonly IEventListener[] _ls;
        public CompositeEventListener(params IEventListener[] ls) => _ls = ls;

        public void EventOccurred(IEventData data, EventType type)
        {
            foreach (var l in _ls)
                if (l.GetSupportedEvents().Contains(type))
                    l.EventOccurred(data, type);
        }

        public ICollection<EventType> GetSupportedEvents()
        {
            var s = new HashSet<EventType>();
            foreach (var l in _ls) foreach (var e in l.GetSupportedEvents()) s.Add(e);
            return s;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // XML helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>XML-escape a string for use in element content or attribute values.</summary>
    private static string Xe(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
    }

    /// <summary>Remove characters illegal in XML 1.0 (control chars except tab/LF/CR).</summary>
    private static string SanitizeXml(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c == '\t' || c == '\n' || c == '\r'
                || (c >= '\x20' && c <= '\uD7FF')
                || (c >= '\uE000' && c <= '\uFFFD'))
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static void AddZipEntry(ZipArchive zip, string entryPath, string xml)
    {
        var e = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var s = e.Open();
        var b = Encoding.UTF8.GetBytes(xml);
        s.Write(b, 0, b.Length);
    }

    private static void AddZipEntryBytes(ZipArchive zip, string entryPath, byte[] data)
    {
        var e = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var s = e.Open();
        s.Write(data, 0, data.Length);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Top-level DOCX builder
    // ──────────────────────────────────────────────────────────────────────────

    private byte[] GenerateDocxRaw(byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        // 1. Extract PDF content → build body XML and collect image parts
        var (bodyXml, imageParts) = BuildBodyXml(pdfBytes, options, ct);

        // 2. Build all XML files
        string contentTypesXml = BuildContentTypesXml(imageParts);
        string rootRelsXml     = BuildRootRelsXml();
        string docRelsXml      = BuildDocumentRelsXml(imageParts);
        string documentXml     = BuildDocumentXml(bodyXml);
        string stylesXml       = BuildStylesXml();
        string settingsXml     = BuildSettingsXml();
        string fontTableXml    = BuildFontTableXml();
        string webSettingsXml  = BuildWebSettingsXml();

        // 3. Pack everything into a ZIP (= DOCX)
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(zip, "[Content_Types].xml",          contentTypesXml);
            AddZipEntry(zip, "_rels/.rels",                  rootRelsXml);
            AddZipEntry(zip, "word/document.xml",            documentXml);
            AddZipEntry(zip, "word/_rels/document.xml.rels", docRelsXml);
            AddZipEntry(zip, "word/styles.xml",              stylesXml);
            AddZipEntry(zip, "word/settings.xml",            settingsXml);
            AddZipEntry(zip, "word/fontTable.xml",           fontTableXml);
            AddZipEntry(zip, "word/webSettings.xml",         webSettingsXml);

            foreach (var img in imageParts)
                AddZipEntryBytes(zip, "word/" + img.ZipRelPath, img.Data);
        }

        var result = ms.ToArray();
        Log.Debug("DOCX built: {Bytes} bytes, {Images} image(s)", result.Length, imageParts.Count);
        return result;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PDF content extraction → body XML
    // ──────────────────────────────────────────────────────────────────────────

    private (StringBuilder bodyXml, List<ImagePart> imageParts) BuildBodyXml(
        byte[] pdfBytes, ExportOptions options, CancellationToken ct)
    {
        var body       = new StringBuilder(64 * 1024);
        var imageParts = new List<ImagePart>();
        int imgSeq     = 0; // 0-based; rId = ImageRIdBase + imgSeq

        // Document title paragraph
        AppendTitleParagraph(body, options.BaseFileName);

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);

        int   total       = pdfDoc.GetNumberOfPages();
        int[] pageIndices = options.PageIndices ?? Enumerable.Range(0, total).ToArray();

        for (int pi = 0; pi < pageIndices.Length; pi++)
        {
            ct.ThrowIfCancellationRequested();
            int pageNum = pageIndices[pi] + 1;      // iText7 is 1-based
            if (pageNum < 1 || pageNum > total) continue;

            var page         = pdfDoc.GetPage(pageNum);
            var textListener = new TextExtractionListener();
            var imgListener  = new ImageExtractionListener();

            new PdfCanvasProcessor(new CompositeEventListener(textListener, imgListener))
                .ProcessPageContent(page);

            bool hasText = textListener.Chunks.Count > 0;
            bool hasImgs = imgListener.Images.Count  > 0;

            if (!hasText && !hasImgs)
            {
                var fallback = PdfTextExtractor.GetTextFromPage(page, new SimpleTextExtractionStrategy());
                if (!string.IsNullOrWhiteSpace(fallback))
                    AppendFallbackText(body, fallback);
                else
                    AppendPlaceholder(body, $"[No extractable content on page {pageNum}]");
            }
            else
            {
                if (hasText)
                {
                    var lines  = AssembleTextLines(textListener.Chunks);
                    var blocks = AnalyzeContentBlocks(lines);
                    var tables = DetectTables(lines);

                    // Build set of Y-coordinates that belong to detected tables
                    var tableYSet = new HashSet<float>();
                    foreach (var t in tables)
                        foreach (var l in lines.Where(l => l.Y <= t.TopY + 1f && l.Y >= t.BottomY - 1f))
                            tableYSet.Add(l.Y);

                    int tableIdx = 0;
                    foreach (var block in blocks)
                    {
                        if (block.Lines.Any(l => tableYSet.Contains(l.Y)) && tableIdx < tables.Count)
                        {
                            AppendTableXml(body, tables[tableIdx++]);
                            continue;
                        }
                        if (block.BlockType == ContentBlockType.Heading)
                            AppendHeadingXml(body, block);
                        else
                            AppendParagraphXml(body, block);
                    }
                }

                if (hasImgs)
                {
                    foreach (var img in imgListener.Images)
                    {
                        try
                        {
                            var norm = NormalizeImageForDocx(img.Data, out bool isJpeg);
                            if (norm.Length == 0) continue;

                            string ext  = isJpeg ? "jpg"        : "png";
                            string ct2  = isJpeg ? "image/jpeg" : "image/png";
                            string path = $"media/image{imgSeq + 1}.{ext}";
                            string rId  = $"rId{ImageRIdBase + imgSeq}";

                            imageParts.Add(new ImagePart(rId, path, norm, ct2));

                            long wEmu = (long)(Math.Abs(img.Width)  * 12700);
                            long hEmu = (long)(Math.Abs(img.Height) * 12700);
                            ClampImageEmu(ref wEmu, ref hEmu);

                            AppendImageXml(body, rId, wEmu, hEmu, (uint)(imgSeq + 1));
                            imgSeq++;
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "Failed to embed image in DOCX");
                        }
                    }
                }
            }

            // Page break between pages (but not after the last one)
            if (pi < pageIndices.Length - 1)
                body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
        }

        // Mandatory: SectionProperties must be the last element in <w:body>
        body.Append(
            "<w:sectPr>" +
            "<w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
            "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" " +
                     "w:left=\"1440\" w:header=\"720\" w:footer=\"720\"/>" +
            "</w:sectPr>");

        return (body, imageParts);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // XML file builders — every byte of every file is under our control
    // ──────────────────────────────────────────────────────────────────────────

    private static string BuildContentTypesXml(List<ImagePart> imageParts)
    {
        var sb = new StringBuilder(1024);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append($"<Types xmlns=\"{NsOpcCt}\">");

        // Generic defaults by file extension
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");

        // Image defaults
        bool hasJpeg = imageParts.Any(i => i.ContentType == "image/jpeg");
        bool hasPng  = imageParts.Any(i => i.ContentType == "image/png");
        if (hasJpeg) sb.Append("<Default Extension=\"jpg\" ContentType=\"image/jpeg\"/>");
        if (hasPng)  sb.Append("<Default Extension=\"png\" ContentType=\"image/png\"/>");

        // Specific overrides by part name (this is the correct OOXML approach)
        sb.Append("<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>");
        sb.Append("<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>");
        sb.Append("<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>");
        sb.Append("<Override PartName=\"/word/fontTable.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml\"/>");
        sb.Append("<Override PartName=\"/word/webSettings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.webSettings+xml\"/>");

        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string BuildRootRelsXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<Relationships xmlns=\"{NsOpcRel}\">" +
        $"<Relationship Id=\"rId1\" Type=\"{RelTypeDocument}\" Target=\"word/document.xml\"/>" +
        "</Relationships>";

    private static string BuildDocumentRelsXml(List<ImagePart> imageParts)
    {
        var sb = new StringBuilder(512);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append($"<Relationships xmlns=\"{NsOpcRel}\">");
        sb.Append($"<Relationship Id=\"rId1\" Type=\"{RelTypeStyles}\" Target=\"styles.xml\"/>");
        sb.Append($"<Relationship Id=\"rId2\" Type=\"{RelTypeSettings}\" Target=\"settings.xml\"/>");
        sb.Append($"<Relationship Id=\"rId3\" Type=\"{RelTypeFontTable}\" Target=\"fontTable.xml\"/>");
        sb.Append($"<Relationship Id=\"rId4\" Type=\"{RelTypeWebSettings}\" Target=\"webSettings.xml\"/>");
        foreach (var img in imageParts)
            sb.Append($"<Relationship Id=\"{Xe(img.RId)}\" Type=\"{RelTypeImage}\" Target=\"{Xe(img.ZipRelPath)}\"/>");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string BuildDocumentXml(StringBuilder bodyContent)
    {
        var sb = new StringBuilder(bodyContent.Length + 512);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append(
            $"<w:document xmlns:w=\"{NsW}\" " +
            $"xmlns:r=\"{NsOffRel}\" " +
            $"xmlns:wp=\"{NsWp}\" " +
            $"xmlns:a=\"{NsA}\" " +
            $"xmlns:pic=\"{NsPic}\">");
        sb.Append("<w:body>");
        sb.Append(bodyContent);
        sb.Append("</w:body>");
        sb.Append("</w:document>");
        return sb.ToString();
    }

    private static string BuildStylesXml()
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            $"<w:styles xmlns:w=\"{NsW}\">" +

            // Document defaults: Calibri 11pt, 1.15 line spacing
            "<w:docDefaults>" +
            "<w:rPrDefault><w:rPr>" +
            "<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Calibri\"/>" +
            "<w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/>" +
            "</w:rPr></w:rPrDefault>" +
            "<w:pPrDefault><w:pPr>" +
            "<w:spacing w:after=\"160\" w:line=\"259\" w:lineRule=\"auto\"/>" +
            "</w:pPr></w:pPrDefault>" +
            "</w:docDefaults>" +

            // Normal style — required by Word; all other styles inherit from it
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\">" +
            "<w:name w:val=\"Normal\"/>" +
            "<w:rPr>" +
            "<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/>" +
            "<w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/>" +
            "</w:rPr>" +
            "</w:style>" +

            // Custom heading / title styles
            MakeStyle("PDFTitle",    "PDF Title",    "56", "1B2A4A", "480", "240") +
            MakeStyle("PDFHeading1", "PDF Heading 1","40", "1B2A4A", "480", "160") +
            MakeStyle("PDFHeading2", "PDF Heading 2","34", "2E4057", "400", "120") +
            MakeStyle("PDFHeading3", "PDF Heading 3","28", "3D5A80", "320", "80")  +
            MakeStyle("PDFHeading4", "PDF Heading 4","24", "4A7C9B", "240", "60")  +

            "</w:styles>";
    }

    private static string MakeStyle(
        string id, string name, string szVal, string color, string before, string after) =>
        $"<w:style w:type=\"paragraph\" w:customStyle=\"1\" w:styleId=\"{Xe(id)}\">" +
        $"<w:name w:val=\"{Xe(name)}\"/>" +
        "<w:basedOn w:val=\"Normal\"/>" +
        "<w:next w:val=\"Normal\"/>" +
        $"<w:pPr><w:spacing w:before=\"{before}\" w:after=\"{after}\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr>" +
        "<w:rPr>" +
        "<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/>" +
        "<w:b/><w:bCs/>" +
        $"<w:color w:val=\"{color}\"/>" +
        $"<w:sz w:val=\"{szVal}\"/><w:szCs w:val=\"{szVal}\"/>" +
        "</w:rPr>" +
        "</w:style>";

    private static string BuildSettingsXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:settings xmlns:w=\"{NsW}\" xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\" xmlns:o=\"urn:schemas-microsoft-com:office:office\">" +
        "<w:zoom w:percent=\"100\"/>" +
        "<w:defaultTabStop w:val=\"720\"/>" +
        "<w:characterSpacingControl w:val=\"doNotCompress\"/>" +
        "<w:compat>" +
        "<w:compatSetting w:name=\"compatibilityMode\" " +
            "w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/>" +
        "<w:compatSetting w:name=\"overrideTableStyleFontSizeAndJustification\" " +
            "w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"1\"/>" +
        "<w:compatSetting w:name=\"enableOpenTypeFeatures\" " +
            "w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"1\"/>" +
        "<w:compatSetting w:name=\"doNotFlipMirrorIndents\" " +
            "w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"1\"/>" +
        "<w:compatSetting w:name=\"differentiateMultirowTableHeaders\" " +
            "w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"1\"/>" +
        "</w:compat>" +
        "</w:settings>";

    /// <summary>
    /// Word requires a fontTable part listing the fonts used in the document.
    /// Without this, some versions show "repair" or "error" dialogs.
    /// </summary>
    private static string BuildFontTableXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:fonts xmlns:w=\"{NsW}\" xmlns:r=\"{NsOffRel}\">" +
        "<w:font w:name=\"Calibri\">" +
        "<w:panose1 w:val=\"020F0502020204030204\"/>" +
        "<w:charset w:val=\"00\"/>" +
        "<w:family w:val=\"swiss\"/>" +
        "<w:pitch w:val=\"variable\"/>" +
        "<w:sig w:usb0=\"E4002EFF\" w:usb1=\"C000247B\" w:usb2=\"00000009\" w:usb3=\"00000000\" w:csb0=\"000001FF\" w:csb1=\"00000000\"/>" +
        "</w:font>" +
        "<w:font w:name=\"Times New Roman\">" +
        "<w:panose1 w:val=\"02020603050405020304\"/>" +
        "<w:charset w:val=\"00\"/>" +
        "<w:family w:val=\"roman\"/>" +
        "<w:pitch w:val=\"variable\"/>" +
        "<w:sig w:usb0=\"E0002EFF\" w:usb1=\"C000785B\" w:usb2=\"00000009\" w:usb3=\"00000000\" w:csb0=\"000001FF\" w:csb1=\"00000000\"/>" +
        "</w:font>" +
        "<w:font w:name=\"Calibri Light\">" +
        "<w:panose1 w:val=\"020F0302020204030204\"/>" +
        "<w:charset w:val=\"00\"/>" +
        "<w:family w:val=\"swiss\"/>" +
        "<w:pitch w:val=\"variable\"/>" +
        "<w:sig w:usb0=\"E4002EFF\" w:usb1=\"C000247B\" w:usb2=\"00000009\" w:usb3=\"00000000\" w:csb0=\"000001FF\" w:csb1=\"00000000\"/>" +
        "</w:font>" +
        "</w:fonts>";

    /// <summary>
    /// Web settings part — minimal but required by some Word versions.
    /// </summary>
    private static string BuildWebSettingsXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:webSettings xmlns:w=\"{NsW}\" xmlns:r=\"{NsOffRel}\">" +
        "<w:optimizeForBrowser/>" +
        "<w:allowPNG/>" +
        "</w:webSettings>";

    // ──────────────────────────────────────────────────────────────────────────
    // Body element helpers (append raw XML to the body StringBuilder)
    // ──────────────────────────────────────────────────────────────────────────

    private static void AppendTitleParagraph(StringBuilder sb, string? title)
    {
        sb.Append("<w:p>");
        sb.Append("<w:pPr><w:pStyle w:val=\"PDFTitle\"/><w:jc w:val=\"center\"/>");
        sb.Append("<w:spacing w:after=\"200\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr>");
        sb.Append("<w:r><w:rPr>");
        sb.Append("<w:b/><w:sz w:val=\"56\"/><w:szCs w:val=\"56\"/>");
        sb.Append("<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/>");
        sb.Append("</w:rPr>");
        sb.Append($"<w:t xml:space=\"preserve\">{Xe(SanitizeXml(title))}</w:t>");
        sb.Append("</w:r></w:p>");
    }

    private static void AppendHeadingXml(StringBuilder sb, ContentBlock block)
    {
        string styleId = block.HeadingLevel switch
        {
            1 => "PDFHeading1",
            2 => "PDFHeading2",
            3 => "PDFHeading3",
            _ => "PDFHeading4"
        };
        string text = Xe(SanitizeXml(string.Join(" ", block.Lines.Select(l => l.Text))));
        sb.Append($"<w:p><w:pPr><w:pStyle w:val=\"{styleId}\"/></w:pPr>");
        sb.Append($"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>");
    }

    private static void AppendParagraphXml(StringBuilder sb, ContentBlock block)
    {
        string text = Xe(SanitizeXml(string.Join(" ", block.Lines.Select(l => l.Text))));
        if (string.IsNullOrWhiteSpace(text)) return;

        int sz = Math.Max(16, Math.Min(96, (int)(block.FontSize * 1.5f)));
        sb.Append("<w:p>");
        sb.Append("<w:pPr><w:spacing w:after=\"80\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr>");
        sb.Append("<w:r><w:rPr>");
        sb.Append($"<w:sz w:val=\"{sz}\"/><w:szCs w:val=\"{sz}\"/>");
        sb.Append("<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/>");
        if (block.IsBold)   sb.Append("<w:b/><w:bCs/>");
        if (block.IsItalic) sb.Append("<w:i/><w:iCs/>");
        sb.Append("</w:rPr>");
        sb.Append($"<w:t xml:space=\"preserve\">{text}</w:t>");
        sb.Append("</w:r></w:p>");
    }

    private static void AppendFallbackText(StringBuilder sb, string text)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = Xe(SanitizeXml(rawLine.TrimEnd('\r')));
            if (string.IsNullOrWhiteSpace(line)) { sb.Append("<w:p/>"); continue; }

            sb.Append("<w:p>");
            sb.Append("<w:pPr><w:spacing w:after=\"60\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr>");
            sb.Append("<w:r><w:rPr>");
            sb.Append("<w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/>");
            sb.Append("<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/>");
            sb.Append("</w:rPr>");
            sb.Append($"<w:t xml:space=\"preserve\">{line}</w:t>");
            sb.Append("</w:r></w:p>");
        }
    }

    private static void AppendPlaceholder(StringBuilder sb, string message)
    {
        sb.Append("<w:p><w:r><w:rPr><w:i/><w:color w:val=\"999999\"/></w:rPr>");
        sb.Append($"<w:t>{Xe(SanitizeXml(message))}</w:t></w:r></w:p>");
    }

    private static void AppendTableXml(StringBuilder sb, DetectedTable table)
    {
        if (table.Rows.Count == 0) return;
        int cols = table.Rows.Max(r => r.Count);
        if (cols == 0) return;

        sb.Append("<w:tbl>");
        sb.Append("<w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/>");
        sb.Append("<w:tblBorders>");
        foreach (var side in new[] { "top","bottom","left","right","insideH","insideV" })
        {
            string color = side.StartsWith("inside", StringComparison.Ordinal) ? "CCCCCC" : "999999";
            sb.Append($"<w:{side} w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"{color}\"/>");
        }
        sb.Append("</w:tblBorders></w:tblPr>");

        for (int ri = 0; ri < table.Rows.Count; ri++)
        {
            var row = table.Rows[ri];
            sb.Append("<w:tr>");
            for (int ci = 0; ci < cols; ci++)
            {
                string cell = ci < row.Count ? row[ci] : "";
                sb.Append("<w:tc>");
                if (ri == 0)
                    sb.Append("<w:tcPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"E8EDF2\"/></w:tcPr>");
                sb.Append("<w:p><w:r><w:rPr>");
                sb.Append("<w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/>");
                sb.Append("<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/>");
                if (ri == 0) sb.Append("<w:b/><w:bCs/>");
                sb.Append("</w:rPr>");
                sb.Append($"<w:t xml:space=\"preserve\">{Xe(SanitizeXml(cell))}</w:t>");
                sb.Append("</w:r></w:p></w:tc>");
            }
            sb.Append("</w:tr>");
        }

        sb.Append("</w:tbl><w:p/>"); // empty paragraph for spacing after table
    }

    private static void AppendImageXml(StringBuilder sb, string rId, long wEmu, long hEmu, uint docId)
    {
        // All namespaces (w:, wp:, a:, pic:, r:) are declared on the root <w:document> element,
        // so we can use them freely here without re-declaring.
        sb.Append("<w:p>");
        sb.Append("<w:pPr><w:jc w:val=\"center\"/><w:spacing w:before=\"120\" w:after=\"120\"/></w:pPr>");
        sb.Append("<w:r><w:drawing>");
        sb.Append($"<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">");
        sb.Append($"<wp:extent cx=\"{wEmu}\" cy=\"{hEmu}\"/>");
        sb.Append("<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>");
        sb.Append($"<wp:docPr id=\"{docId}\" name=\"PDFImage{docId}\"/>");
        sb.Append("<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>");
        sb.Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">");
        sb.Append("<pic:pic><pic:nvPicPr>");
        sb.Append($"<pic:cNvPr id=\"{docId + 10000u}\" name=\"Image{docId}\"/>");
        sb.Append("<pic:cNvPicPr/></pic:nvPicPr>");
        sb.Append("<pic:blipFill>");
        sb.Append($"<a:blip r:embed=\"{Xe(rId)}\" cstate=\"print\"/>");
        sb.Append("<a:stretch><a:fillRect/></a:stretch>");
        sb.Append("</pic:blipFill>");
        sb.Append("<pic:spPr>");
        sb.Append($"<a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{wEmu}\" cy=\"{hEmu}\"/></a:xfrm>");
        sb.Append("<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>");
        sb.Append("</pic:spPr></pic:pic>");
        sb.Append("</a:graphicData></a:graphic>");
        sb.Append("</wp:inline>");
        sb.Append("</w:drawing></w:r></w:p>");
    }

    private static void ClampImageEmu(ref long wEmu, ref long hEmu)
    {
        const long maxW   = 5943600L; // 6.5 inches in EMU
        const long maxH   = 8229600L; // 9 inches in EMU
        const long minDim = 457200L;  // 0.5 inches minimum

        if (wEmu < minDim) wEmu = maxW / 2;
        if (hEmu < minDim) hEmu = maxH / 2;

        if (wEmu > maxW) { hEmu = (long)(hEmu * ((double)maxW / wEmu)); wEmu = maxW; }
        if (hEmu > maxH) { wEmu = (long)(wEmu * ((double)maxH / hEmu)); hEmu = maxH; }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Image normalisation
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures image bytes are in JPEG or PNG format — the only formats Word reliably
    /// accepts inside a DOCX zip. PDF can contain JBIG2, JPEG2000, CCITT-Fax and other
    /// encodings that iText7's GetImageBytes(true) returns as raw pixels, not image files.
    /// </summary>
    private static byte[] NormalizeImageForDocx(byte[] data, out bool isJpeg)
    {
        isJpeg = false;
        if (data == null || data.Length < 4) return Array.Empty<byte>();

        // JPEG magic: FF D8 FF
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            isJpeg = true;
            return data;
        }

        // PNG magic: 89 50 4E 47 0D 0A 1A 0A
        if (data.Length >= 8
            && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            return data;

        // Unknown / PDF-internal format → Magick.NET → PNG
        try
        {
            using var mgk   = new MagickImage(data);
            using var outMs = new MemoryStream();
            mgk.Format = MagickFormat.Png;
            mgk.Write(outMs);
            var result = outMs.ToArray();
            return result.Length > 0 ? result : Array.Empty<byte>();
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger()
                .Debug(ex, "Magick.NET could not convert PDF image to PNG — skipping");
            return Array.Empty<byte>();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Content analysis: lines → blocks → tables
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TextLine> AssembleTextLines(List<TextChunkInfo> chunks)
    {
        if (chunks.Count == 0) return new();

        var sorted  = chunks.OrderByDescending(c => c.Y).ThenBy(c => c.X).ToList();
        var lines   = new List<TextLine>();
        TextLine? cur = null;

        foreach (var chunk in sorted)
        {
            if (cur == null || Math.Abs(chunk.Y - cur.Y) > 2.0f)
            {
                cur = new TextLine { Y = chunk.Y, Chunks = new() { chunk } };
                lines.Add(cur);
            }
            else
            {
                cur.Chunks.Add(chunk);
            }
        }

        foreach (var line in lines)
        {
            var ordered   = line.Chunks.OrderBy(c => c.X).ToList();
            var sb        = new StringBuilder();
            float lastR   = float.MinValue;

            foreach (var chunk in ordered)
            {
                if (lastR > float.MinValue && chunk.X - lastR > chunk.FontSize * 0.45f)
                    sb.Append(' ');
                sb.Append(chunk.Text);
                lastR = chunk.X + chunk.Text.Length * chunk.FontSize * 0.5f;
            }

            line.Text = sb.ToString().Trim();

            var dom = ordered.GroupBy(c => Math.Round(c.FontSize, 1))
                             .OrderByDescending(g => g.Sum(c => c.Text.Length))
                             .First();
            line.PredominantFontSize = (float)dom.Key;
            line.PredominantBold     = ordered.Count(c => c.IsBold)   > ordered.Count / 2;
            line.PredominantItalic   = ordered.Count(c => c.IsItalic) > ordered.Count / 2;
        }

        return lines;
    }

    private static List<ContentBlock> AnalyzeContentBlocks(List<TextLine> lines)
    {
        if (lines.Count == 0) return new();

        var sizes    = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text))
                            .Select(l => l.PredominantFontSize).OrderBy(s => s).ToList();
        float body   = sizes.Count > 0 ? sizes[sizes.Count / 2] : 12f;
        var blocks   = new List<ContentBlock>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text)) continue;

            float ratio   = line.PredominantFontSize / body;
            bool heading  = ratio > 1.2f || (ratio > 1.0f && line.PredominantBold);
            int  level    = ratio >= 2.0f ? 1
                          : ratio >= 1.6f ? 2
                          : ratio >= 1.3f ? 3
                          : (ratio >= 1.15f && line.PredominantBold) ? 4
                          : 0;

            if (heading && level > 0)
            {
                blocks.Add(new ContentBlock
                {
                    BlockType    = ContentBlockType.Heading,
                    HeadingLevel = level,
                    Lines        = new() { line },
                    IsBold       = true,
                    FontSize     = line.PredominantFontSize
                });
            }
            else
            {
                var last = blocks.Count > 0 ? blocks[^1] : null;
                if (last?.BlockType == ContentBlockType.Paragraph)
                {
                    var ll = last.Lines[^1];
                    if (Math.Abs(ll.Y - line.Y) < ll.PredominantFontSize * 3f)
                    {
                        last.Lines.Add(line);
                        continue;
                    }
                }
                blocks.Add(new ContentBlock
                {
                    BlockType = ContentBlockType.Paragraph,
                    Lines     = new() { line },
                    IsBold    = line.PredominantBold,
                    IsItalic  = line.PredominantItalic,
                    FontSize  = line.PredominantFontSize
                });
            }
        }

        return blocks;
    }

    private static List<DetectedTable> DetectTables(List<TextLine> lines)
    {
        var tables = new List<DetectedTable>();
        if (lines.Count < 2) return tables;

        var cands = lines.Select((l, i) =>
                (idx: i, xs: l.Chunks.OrderBy(c => c.X).Select(c => c.X).ToList()))
            .Where(t => t.xs.Count >= 2)
            .ToList();

        for (int i = 0; i < cands.Count - 1; i++)
        {
            var group   = new List<int> { cands[i].idx };
            var refCols = cands[i].xs;

            for (int j = i + 1; j < cands.Count; j++)
            {
                if (Math.Abs(cands[j].xs.Count - refCols.Count) <= 1
                    && AreColumnsAligned(refCols, cands[j].xs, 15f))
                    group.Add(cands[j].idx);
                else
                    break;
            }

            if (group.Count >= 3)
            {
                var t = new DetectedTable
                {
                    TopY             = lines[group[0]].Y,
                    BottomY          = lines[group[^1]].Y,
                    ColumnBoundaries = refCols
                };
                foreach (var li in group)
                    t.Rows.Add(BuildTableRow(lines[li], refCols));

                tables.Add(t);
                i = cands.FindIndex(c => c.idx == group[^1]);
            }
        }

        return tables;
    }

    private static bool AreColumnsAligned(List<float> a, List<float> b, float tol)
        => a.Count(ax => b.Any(bx => Math.Abs(ax - bx) < tol))
           >= Math.Min(a.Count, b.Count) * 0.7;

    private static List<string> BuildTableRow(TextLine line, List<float> cols)
    {
        var row = Enumerable.Repeat("", cols.Count).ToList();
        foreach (var chunk in line.Chunks.OrderBy(c => c.X))
        {
            int best = 0; float bestD = float.MaxValue;
            for (int c = 0; c < cols.Count; c++)
            {
                float d = Math.Abs(chunk.X - cols[c]);
                if (d < bestD) { bestD = d; best = c; }
            }
            row[best] += (row[best].Length > 0 ? " " : "") + chunk.Text;
        }
        return row;
    }
}
