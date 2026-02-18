using iText.Kernel.Pdf;
using iText.Kernel.XMP;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// PDF/X conformance level
/// </summary>
public enum PdfXConformance
{
    PdfX1a,
    PdfX3,
    PdfX4
}

/// <summary>
/// Result of PDF/X inspection
/// </summary>
public class PdfXInspectionResult
{
    public bool IsPdfX { get; set; }
    public string? ConformanceLevel { get; set; }
    public string? OutputIntentProfile { get; set; }
    public List<string> Issues { get; set; } = new();
    public bool HasTransparency { get; set; }
    public bool AllFontsEmbedded { get; set; }
    public bool HasOutputIntent { get; set; }
}

/// <summary>
/// Service for PDF/X compliance (print production standard).
/// PDF/X ensures a PDF is suitable for reliable print reproduction by requiring:
/// - All fonts embedded
/// - No transparency (PDF/X-1a, X-3) or with caveats (X-4)
/// - ICC color profile output intent
/// - No JavaScript, audio, video
/// - Specific metadata (trapped flag, output intent)
/// </summary>
public class PdfXService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Inspects a PDF for PDF/X compliance
    /// </summary>
    public PdfXInspectionResult Inspect(byte[] pdfBytes)
    {
        Log.Info("Inspecting PDF for PDF/X compliance ({Bytes} bytes)", pdfBytes.Length);
        var result = new PdfXInspectionResult();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var doc = new PdfDocument(reader);
        var catalog = doc.GetCatalog().GetPdfObject();

        // Check XMP metadata for PDF/X conformance
        try
        {
            byte[]? xmpBytes = null;
            var xmpStream = catalog.GetAsStream(PdfName.Metadata);
            if (xmpStream != null)
            {
                xmpBytes = xmpStream.GetBytes();
            }

            if (xmpBytes != null)
            {
                var xmpMeta = XMPMetaFactory.ParseFromBuffer(xmpBytes);
                string? conformance = null;

                try
                {
                    conformance = xmpMeta.GetProperty("http://www.niso.org/schemas/jav/1.0/", "pdfxid:GTS_PDFXVersion")?.GetValue();
                }
                catch { }

                if (!string.IsNullOrEmpty(conformance))
                {
                    result.IsPdfX = true;
                    result.ConformanceLevel = conformance;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to parse XMP for PDF/X metadata");
        }

        // Check output intents
        var outputIntents = catalog.GetAsArray(PdfName.OutputIntents);
        if (outputIntents != null && outputIntents.Size() > 0)
        {
            result.HasOutputIntent = true;
            var firstIntent = outputIntents.GetAsDictionary(0);
            if (firstIntent != null)
            {
                result.OutputIntentProfile = firstIntent.GetAsString(new PdfName("OutputConditionIdentifier"))?.GetValue();
            }
        }
        else
        {
            result.Issues.Add("No OutputIntent defined (required for PDF/X)");
        }

        // Check fonts are embedded
        result.AllFontsEmbedded = CheckAllFontsEmbedded(doc);
        if (!result.AllFontsEmbedded)
            result.Issues.Add("Not all fonts are embedded (required for PDF/X)");

        // Check for transparency (PDF/X-1a doesn't allow it)
        result.HasTransparency = CheckHasTransparency(doc);
        if (result.HasTransparency)
            result.Issues.Add("Document contains transparency (incompatible with PDF/X-1a)");

        // Check for JavaScript
        var names = catalog.GetAsDictionary(PdfName.Names);
        if (names?.GetAsDictionary(PdfName.JavaScript) != null)
            result.Issues.Add("Document contains JavaScript (not allowed in PDF/X)");

        Log.Info("PDF/X inspection: isPdfX={IsPdfX}, issues={Count}", result.IsPdfX, result.Issues.Count);
        return result;
    }

    /// <summary>
    /// Converts a PDF toward PDF/X-4 compliance by:
    /// - Adding an output intent (sRGB)
    /// - Setting trapped flag
    /// - Setting PDF/X metadata
    /// - Removing JavaScript
    /// </summary>
    public byte[] ConvertToPdfX(byte[] pdfBytes, PdfXConformance conformance = PdfXConformance.PdfX4)
    {
        Log.Info("Converting PDF to {Conformance}", conformance);

        var outMs = new MemoryStream();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var writer = new PdfWriter(outMs);
        using var doc = new PdfDocument(reader, writer);
        var catalog = doc.GetCatalog().GetPdfObject();

        // 1. Remove JavaScript
        var names = catalog.GetAsDictionary(PdfName.Names);
        names?.Remove(PdfName.JavaScript);

        // Remove OpenAction
        catalog.Remove(PdfName.OpenAction);

        // 2. Set Trapped flag
        var info = doc.GetDocumentInfo();
        info.SetMoreInfo("Trapped", "False");

        // 3. Add output intent if missing
        var outputIntents = catalog.GetAsArray(PdfName.OutputIntents);
        if (outputIntents == null || outputIntents.Size() == 0)
        {
            AddOutputIntent(doc, catalog);
        }

        // 4. Set PDF/X version in XMP
        try
        {
            string pdfxVersion = conformance switch
            {
                PdfXConformance.PdfX1a => "PDF/X-1a:2003",
                PdfXConformance.PdfX3 => "PDF/X-3:2003",
                PdfXConformance.PdfX4 => "PDF/X-4",
                _ => "PDF/X-4"
            };

            byte[]? xmpBytes = null;
            var xmpStream = catalog.GetAsStream(PdfName.Metadata);
            if (xmpStream != null)
                xmpBytes = xmpStream.GetBytes();

            var xmpMeta = xmpBytes != null
                ? XMPMetaFactory.ParseFromBuffer(xmpBytes)
                : XMPMetaFactory.Create();

            xmpMeta.SetProperty("http://www.niso.org/schemas/jav/1.0/", "pdfxid:GTS_PDFXVersion", pdfxVersion);

            byte[] newXmp = XMPMetaFactory.SerializeToBuffer(xmpMeta, new iText.Kernel.XMP.Options.SerializeOptions());

            var newXmpStream = new PdfStream(newXmp);
            newXmpStream.Put(PdfName.Type, PdfName.Metadata);
            newXmpStream.Put(PdfName.Subtype, new PdfName("XML"));
            catalog.Put(PdfName.Metadata, newXmpStream);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to set PDF/X metadata in XMP");
        }

        doc.Close();
        Log.Info("PDF/X conversion complete");
        return outMs.ToArray();
    }

    /// <summary>
    /// Generates a PDF/X compliance report
    /// </summary>
    public string GenerateReport(byte[] pdfBytes)
    {
        var inspection = Inspect(pdfBytes);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("PDF/X Compliance Report");
        sb.AppendLine("=======================");
        sb.AppendLine($"Is PDF/X: {(inspection.IsPdfX ? "Yes" : "No")}");

        if (inspection.ConformanceLevel != null)
            sb.AppendLine($"Conformance: {inspection.ConformanceLevel}");

        sb.AppendLine($"Output Intent: {(inspection.HasOutputIntent ? "Present" : "Missing")}");
        if (inspection.OutputIntentProfile != null)
            sb.AppendLine($"  Profile: {inspection.OutputIntentProfile}");

        sb.AppendLine($"All Fonts Embedded: {(inspection.AllFontsEmbedded ? "Yes" : "No")}");
        sb.AppendLine($"Has Transparency: {(inspection.HasTransparency ? "Yes" : "No")}");

        if (inspection.Issues.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Issues:");
            foreach (var issue in inspection.Issues)
                sb.AppendLine($"  • {issue}");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("No issues found — document appears PDF/X compliant.");
        }

        return sb.ToString();
    }

    private bool CheckAllFontsEmbedded(PdfDocument doc)
    {
        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var resources = doc.GetPage(i).GetResources();
            var fonts = resources.GetResource(PdfName.Font);
            if (fonts == null) continue;

            foreach (var name in fonts.KeySet())
            {
                var fontDict = fonts.GetAsDictionary(name);
                if (fontDict == null) continue;

                var descriptor = fontDict.GetAsDictionary(PdfName.FontDescriptor);
                if (descriptor == null) continue;

                bool isEmbedded = descriptor.ContainsKey(PdfName.FontFile) ||
                                  descriptor.ContainsKey(PdfName.FontFile2) ||
                                  descriptor.ContainsKey(PdfName.FontFile3);

                if (!isEmbedded)
                    return false;
            }
        }
        return true;
    }

    private bool CheckHasTransparency(PdfDocument doc)
    {
        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);
            var group = page.GetPdfObject().GetAsDictionary(PdfName.Group);
            if (group != null)
            {
                var groupSubtype = group.GetAsName(PdfName.S);
                if (PdfName.Transparency.Equals(groupSubtype))
                    return true;
            }
        }
        return false;
    }

    private void AddOutputIntent(PdfDocument doc, PdfDictionary catalog)
    {
        // Create a minimal sRGB output intent
        var intentDict = new PdfDictionary();
        intentDict.Put(PdfName.Type, PdfName.OutputIntent);
        intentDict.Put(PdfName.S, new PdfName("GTS_PDFX"));
        intentDict.Put(new PdfName("OutputConditionIdentifier"), new PdfString("sRGB IEC61966-2.1"));
        intentDict.Put(new PdfName("OutputCondition"), new PdfString("sRGB"));
        intentDict.Put(new PdfName("RegistryName"), new PdfString("http://www.color.org"));
        intentDict.Put(new PdfName("Info"), new PdfString("sRGB IEC61966-2.1"));

        var array = new PdfArray();
        array.Add(intentDict);
        catalog.Put(PdfName.OutputIntents, array);

        Log.Debug("Added sRGB output intent");
    }
}
