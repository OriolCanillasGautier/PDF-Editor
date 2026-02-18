using iText.Kernel.Pdf;
using iText.Kernel.Font;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Information about a font used in a PDF
/// </summary>
public class PdfFontInfo
{
    public string FontName { get; set; } = string.Empty;
    public string? BaseFont { get; set; }
    public bool IsEmbedded { get; set; }
    public bool IsSubset { get; set; }
    public string Encoding { get; set; } = string.Empty;
    public int UsageCount { get; set; }
    public List<int> PagesUsedOn { get; set; } = new();
}

/// <summary>
/// Options for font replacement
/// </summary>
public class FontReplacementOptions
{
    public string SourceFontName { get; set; } = string.Empty;
    public string TargetFontName { get; set; } = StandardFonts.HELVETICA;
    public string? TargetFontPath { get; set; }
    public bool EmbedFont { get; set; } = true;
    public int[]? PageIndices { get; set; }
}

/// <summary>
/// Service for analyzing and replacing fonts in PDF documents.
/// Lists all fonts, identifies missing/non-embedded fonts, and replaces fonts throughout.
/// </summary>
public class FontReplacementService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Standard fonts available for replacement
    /// </summary>
    public static readonly Dictionary<string, string> StandardFonts = new()
    {
        { "Helvetica", iText.IO.Font.Constants.StandardFonts.HELVETICA },
        { "Helvetica Bold", iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD },
        { "Helvetica Italic", iText.IO.Font.Constants.StandardFonts.HELVETICA_OBLIQUE },
        { "Times Roman", iText.IO.Font.Constants.StandardFonts.TIMES_ROMAN },
        { "Times Bold", iText.IO.Font.Constants.StandardFonts.TIMES_BOLD },
        { "Times Italic", iText.IO.Font.Constants.StandardFonts.TIMES_ITALIC },
        { "Courier", iText.IO.Font.Constants.StandardFonts.COURIER },
        { "Courier Bold", iText.IO.Font.Constants.StandardFonts.COURIER_BOLD },
        { "Symbol", iText.IO.Font.Constants.StandardFonts.SYMBOL },
        { "ZapfDingbats", iText.IO.Font.Constants.StandardFonts.ZAPFDINGBATS }
    };

    /// <summary>
    /// Analyzes all fonts in a PDF document
    /// </summary>
    public List<PdfFontInfo> AnalyzeFonts(byte[] pdfBytes)
    {
        Log.Info("Analyzing fonts in PDF ({Bytes} bytes)", pdfBytes.Length);
        var fontMap = new Dictionary<string, PdfFontInfo>();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var doc = new PdfDocument(reader);

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);
            var resources = page.GetResources();
            var fonts = resources.GetResource(PdfName.Font);
            if (fonts == null) continue;

            foreach (var fontName in fonts.KeySet())
            {
                var fontDict = fonts.GetAsDictionary(fontName);
                if (fontDict == null) continue;

                var baseFont = fontDict.GetAsName(PdfName.BaseFont)?.GetValue() ?? "Unknown";
                var encoding = fontDict.GetAsName(PdfName.Encoding)?.GetValue() ?? "";
                bool isEmbedded = fontDict.ContainsKey(PdfName.FontFile) ||
                                  fontDict.ContainsKey(PdfName.FontFile2) ||
                                  fontDict.ContainsKey(PdfName.FontFile3);

                // Check descriptor for embedded font
                var descriptor = fontDict.GetAsDictionary(PdfName.FontDescriptor);
                if (descriptor != null)
                {
                    isEmbedded = isEmbedded ||
                                 descriptor.ContainsKey(PdfName.FontFile) ||
                                 descriptor.ContainsKey(PdfName.FontFile2) ||
                                 descriptor.ContainsKey(PdfName.FontFile3);
                }

                bool isSubset = baseFont.Contains('+');
                string displayName = isSubset ? baseFont.Substring(baseFont.IndexOf('+') + 1) : baseFont;

                if (!fontMap.ContainsKey(baseFont))
                {
                    fontMap[baseFont] = new PdfFontInfo
                    {
                        FontName = displayName,
                        BaseFont = baseFont,
                        IsEmbedded = isEmbedded,
                        IsSubset = isSubset,
                        Encoding = encoding
                    };
                }

                fontMap[baseFont].UsageCount++;
                if (!fontMap[baseFont].PagesUsedOn.Contains(i - 1))
                    fontMap[baseFont].PagesUsedOn.Add(i - 1);
            }
        }

        var results = fontMap.Values.OrderByDescending(f => f.UsageCount).ToList();
        Log.Info("Found {Count} unique fonts", results.Count);
        return results;
    }

    /// <summary>
    /// Gets a text report of all fonts in the document
    /// </summary>
    public string GenerateFontReport(byte[] pdfBytes)
    {
        var fonts = AnalyzeFonts(pdfBytes);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Font Analysis Report");
        sb.AppendLine("====================");
        sb.AppendLine($"Total unique fonts: {fonts.Count}");
        sb.AppendLine($"Embedded: {fonts.Count(f => f.IsEmbedded)}");
        sb.AppendLine($"Not embedded: {fonts.Count(f => !f.IsEmbedded)}");
        sb.AppendLine();

        foreach (var font in fonts)
        {
            sb.AppendLine($"  {font.FontName}");
            sb.AppendLine($"    Base: {font.BaseFont}");
            sb.AppendLine($"    Embedded: {(font.IsEmbedded ? "Yes" : "No")}");
            sb.AppendLine($"    Subset: {(font.IsSubset ? "Yes" : "No")}");
            sb.AppendLine($"    Encoding: {font.Encoding}");
            sb.AppendLine($"    Used on {font.PagesUsedOn.Count} page(s), {font.UsageCount} reference(s)");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Replaces font references in the PDF. Note: this replaces font resource dictionaries
    /// but may not change the visual appearance of already-encoded glyphs.
    /// For full font replacement, re-rendering may be needed.
    /// </summary>
    public byte[] ReplaceFont(byte[] pdfBytes, FontReplacementOptions options)
    {
        Log.Info("Replacing font '{Source}' with '{Target}'", options.SourceFontName, options.TargetFontName);

        var outMs = new MemoryStream();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var writer = new PdfWriter(outMs);
        using var doc = new PdfDocument(reader, writer);

        // Create replacement font
        PdfFont replacementFont;
        if (!string.IsNullOrEmpty(options.TargetFontPath) && File.Exists(options.TargetFontPath))
        {
            replacementFont = PdfFontFactory.CreateFont(options.TargetFontPath,
                PdfEncodings.IDENTITY_H, options.EmbedFont
                    ? PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED
                    : PdfFontFactory.EmbeddingStrategy.PREFER_NOT_EMBEDDED);
        }
        else
        {
            replacementFont = PdfFontFactory.CreateFont(options.TargetFontName);
        }

        int replacements = 0;

        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            if (options.PageIndices != null && !options.PageIndices.Contains(i - 1))
                continue;

            var page = doc.GetPage(i);
            var resources = page.GetResources();
            var fonts = resources.GetResource(PdfName.Font);
            if (fonts == null) continue;

            var keysToReplace = new List<PdfName>();
            foreach (var fontName in fonts.KeySet())
            {
                var fontDict = fonts.GetAsDictionary(fontName);
                if (fontDict == null) continue;

                var baseFont = fontDict.GetAsName(PdfName.BaseFont)?.GetValue() ?? "";
                if (baseFont.Contains(options.SourceFontName, StringComparison.OrdinalIgnoreCase))
                {
                    keysToReplace.Add(fontName);
                }
            }

            foreach (var key in keysToReplace)
            {
                fonts.Put(key, replacementFont.GetPdfObject());
                replacements++;
                Log.Debug("Replaced font reference on page {Page}: {Key}", i, key.GetValue());
            }
        }

        doc.Close();
        Log.Info("Font replacement complete: {Count} references replaced", replacements);
        return outMs.ToArray();
    }

    /// <summary>
    /// Embeds all non-embedded standard fonts in the document
    /// </summary>
    public byte[] EmbedAllFonts(byte[] pdfBytes)
    {
        Log.Info("Embedding all fonts in PDF");
        var fonts = AnalyzeFonts(pdfBytes);
        byte[] result = pdfBytes;

        foreach (var font in fonts.Where(f => !f.IsEmbedded))
        {
            // Try to find a matching standard font to embed
            var stdMatch = StandardFonts.Keys.FirstOrDefault(k =>
                font.FontName.Contains(k, StringComparison.OrdinalIgnoreCase));

            if (stdMatch != null)
            {
                try
                {
                    result = ReplaceFont(result, new FontReplacementOptions
                    {
                        SourceFontName = font.FontName,
                        TargetFontName = StandardFonts[stdMatch],
                        EmbedFont = true
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Failed to embed font '{Font}'", font.FontName);
                }
            }
        }

        return result;
    }
}
