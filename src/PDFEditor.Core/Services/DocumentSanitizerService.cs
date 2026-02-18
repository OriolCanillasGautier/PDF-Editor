using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Action;
using NLog;

namespace PDFEditor.Core.Services;

/// <summary>
/// Report of document sanitization
/// </summary>
public class SanitizationReport
{
    public int JavaScriptActionsRemoved { get; set; }
    public int EmbeddedFilesRemoved { get; set; }
    public int ExternalLinksRemoved { get; set; }
    public int FormActionsRemoved { get; set; }
    public int MultiMediaRemoved { get; set; }
    public int MetadataFieldsCleaned { get; set; }
    public bool HadOpenAction { get; set; }
    public List<string> Details { get; set; } = new();
    public int TotalItemsRemoved =>
        JavaScriptActionsRemoved + EmbeddedFilesRemoved + ExternalLinksRemoved +
        FormActionsRemoved + MultiMediaRemoved + MetadataFieldsCleaned + (HadOpenAction ? 1 : 0);
}

/// <summary>
/// Options controlling which elements to sanitize
/// </summary>
public class SanitizationOptions
{
    public bool RemoveJavaScript { get; set; } = true;
    public bool RemoveEmbeddedFiles { get; set; } = true;
    public bool RemoveExternalLinks { get; set; } = false;
    public bool RemoveFormActions { get; set; } = true;
    public bool RemoveMultiMedia { get; set; } = true;
    public bool RemoveOpenActions { get; set; } = true;
    public bool ScrubMetadata { get; set; } = true;
    public bool RemoveXFA { get; set; } = true;
}

/// <summary>
/// Service for sanitizing PDF documents by removing potentially dangerous elements:
/// JavaScript, embedded files, external links, form actions, multimedia, XFA forms.
/// </summary>
public class DocumentSanitizerService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Inspects a PDF and reports what would be sanitized
    /// </summary>
    public SanitizationReport Inspect(byte[] pdfBytes)
    {
        Log.Info("Inspecting PDF for sanitization ({Bytes} bytes)", pdfBytes.Length);
        var report = new SanitizationReport();

        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var doc = new PdfDocument(reader);
        var catalog = doc.GetCatalog().GetPdfObject();

        // Check for open actions (JavaScript on open)
        var openAction = catalog.Get(PdfName.OpenAction);
        if (openAction != null)
        {
            report.HadOpenAction = true;
            report.Details.Add("Document has OpenAction (auto-execute on open)");
        }

        // Check for JavaScript name tree
        var names = catalog.GetAsDictionary(PdfName.Names);
        if (names != null)
        {
            var jsNames = names.GetAsDictionary(PdfName.JavaScript);
            if (jsNames != null)
            {
                report.JavaScriptActionsRemoved++;
                report.Details.Add("Document contains JavaScript name tree");
            }

            var efNames = names.GetAsDictionary(PdfName.EmbeddedFiles);
            if (efNames != null)
            {
                report.EmbeddedFilesRemoved++;
                report.Details.Add("Document contains embedded files");
            }
        }

        // Check each page for annotations with actions
        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);
            var annots = page.GetAnnotations();
            foreach (var annot in annots)
            {
                var annotObj = annot.GetPdfObject();
                var action = annotObj.GetAsDictionary(PdfName.A);
                if (action != null)
                {
                    var actionType = action.GetAsName(PdfName.S);
                    if (PdfName.JavaScript.Equals(actionType))
                    {
                        report.JavaScriptActionsRemoved++;
                        report.Details.Add($"Page {i}: JavaScript action in annotation");
                    }
                    else if (PdfName.URI.Equals(actionType))
                    {
                        report.ExternalLinksRemoved++;
                    }
                }
            }
        }

        // Check for AcroForm with XFA
        var acroForm = catalog.GetAsDictionary(PdfName.AcroForm);
        if (acroForm != null)
        {
            var xfa = acroForm.Get(new PdfName("XFA"));
            if (xfa != null)
            {
                report.FormActionsRemoved++;
                report.Details.Add("Document contains XFA form data");
            }
        }

        Log.Info("Inspection complete: {Total} potential issues found", report.TotalItemsRemoved);
        return report;
    }

    /// <summary>
    /// Sanitizes a PDF by removing dangerous elements
    /// </summary>
    public (byte[] sanitizedPdf, SanitizationReport report) Sanitize(byte[] pdfBytes, SanitizationOptions? options = null)
    {
        options ??= new SanitizationOptions();
        Log.Info("Sanitizing PDF ({Bytes} bytes)", pdfBytes.Length);
        var report = new SanitizationReport();

        var outMs = new MemoryStream();
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var writer = new PdfWriter(outMs);
        using var doc = new PdfDocument(reader, writer);
        var catalog = doc.GetCatalog().GetPdfObject();

        // 1. Remove OpenAction
        if (options.RemoveOpenActions)
        {
            var openAction = catalog.Get(PdfName.OpenAction);
            if (openAction != null)
            {
                catalog.Remove(PdfName.OpenAction);
                report.HadOpenAction = true;
                report.Details.Add("Removed OpenAction");
                Log.Debug("Removed OpenAction");
            }
        }

        // 2. Remove JavaScript and embedded files from Names tree
        var names = catalog.GetAsDictionary(PdfName.Names);
        if (names != null)
        {
            if (options.RemoveJavaScript)
            {
                var jsNames = names.GetAsDictionary(PdfName.JavaScript);
                if (jsNames != null)
                {
                    names.Remove(PdfName.JavaScript);
                    report.JavaScriptActionsRemoved++;
                    report.Details.Add("Removed JavaScript name tree");
                    Log.Debug("Removed JavaScript name tree");
                }
            }

            if (options.RemoveEmbeddedFiles)
            {
                var efNames = names.GetAsDictionary(PdfName.EmbeddedFiles);
                if (efNames != null)
                {
                    names.Remove(PdfName.EmbeddedFiles);
                    report.EmbeddedFilesRemoved++;
                    report.Details.Add("Removed embedded files name tree");
                    Log.Debug("Removed embedded files");
                }
            }
        }

        // 3. Remove XFA from AcroForm
        if (options.RemoveXFA)
        {
            var acroForm = catalog.GetAsDictionary(PdfName.AcroForm);
            if (acroForm != null)
            {
                var xfa = acroForm.Get(new PdfName("XFA"));
                if (xfa != null)
                {
                    acroForm.Remove(new PdfName("XFA"));
                    report.FormActionsRemoved++;
                    report.Details.Add("Removed XFA form data");
                    Log.Debug("Removed XFA data");
                }
            }
        }

        // 4. Process each page — remove JS annotations, external links
        for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        {
            var page = doc.GetPage(i);
            var annots = page.GetAnnotations();
            var toRemove = new List<iText.Kernel.Pdf.Annot.PdfAnnotation>();

            foreach (var annot in annots)
            {
                var annotObj = annot.GetPdfObject();
                var action = annotObj.GetAsDictionary(PdfName.A);
                if (action == null) continue;

                var actionType = action.GetAsName(PdfName.S);

                if (options.RemoveJavaScript && PdfName.JavaScript.Equals(actionType))
                {
                    toRemove.Add(annot);
                    report.JavaScriptActionsRemoved++;
                    report.Details.Add($"Removed JavaScript annotation on page {i}");
                }

                if (options.RemoveExternalLinks && PdfName.URI.Equals(actionType))
                {
                    toRemove.Add(annot);
                    report.ExternalLinksRemoved++;
                }

                // Remove Launch actions (can execute local files)
                if (options.RemoveFormActions && new PdfName("Launch").Equals(actionType))
                {
                    toRemove.Add(annot);
                    report.FormActionsRemoved++;
                    report.Details.Add($"Removed Launch action on page {i}");
                }
            }

            foreach (var annot in toRemove)
            {
                page.RemoveAnnotation(annot);
            }

            // Remove additional actions (AA) from page
            if (options.RemoveJavaScript)
            {
                var aa = page.GetPdfObject().GetAsDictionary(new PdfName("AA"));
                if (aa != null)
                {
                    page.GetPdfObject().Remove(new PdfName("AA"));
                    report.JavaScriptActionsRemoved++;
                    report.Details.Add($"Removed additional actions from page {i}");
                }
            }
        }

        // 5. Scrub metadata
        if (options.ScrubMetadata)
        {
            var info = doc.GetDocumentInfo();
            info.SetAuthor(string.Empty);
            info.SetCreator(string.Empty);
            info.SetKeywords(string.Empty);
            info.SetSubject(string.Empty);
            info.SetProducer(string.Empty);
            report.MetadataFieldsCleaned = 5;
            report.Details.Add("Scrubbed document metadata");
        }

        doc.Close();

        Log.Info("Sanitization complete: {Total} items removed", report.TotalItemsRemoved);
        return (outMs.ToArray(), report);
    }
}
