using NLog;
using Microsoft.Extensions.DependencyInjection;
using PDFEditor.Core.Services;
using PDFEditor.Core.Services.Export;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Core;

/// <summary>
/// Dependency injection configuration for Core services
/// </summary>
public static class CoreServiceCollectionExtensions
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    public static IServiceCollection AddPDFEditorCore(this IServiceCollection services)
    {
        try
        {
            // PDF Services
            services.AddSingleton<IPdfDocument, ITextPdfService>();
            
            // Export Provider Registry
            services.AddSingleton<ExportProviderRegistry>(_ => ExportProviderRegistry.CreateDefault());
            
            // Image Processing
            // services.AddSingleton<IImageProcessor, ImageProcessorService>();
            
            // OCR Engine
            services.AddSingleton<IOcrEngine, TesseractOcrService>();
            services.AddSingleton<TesseractOcrService>();

            // Form Service
            services.AddSingleton<IFormService, PdfFormService>();
            services.AddSingleton<PdfFormService>();

            // Signature Service
            services.AddSingleton<ISignatureService, PdfSignatureService>();
            services.AddSingleton<PdfSignatureService>();

            // Redaction Service
            services.AddSingleton<IRedactionService, PdfRedactionService>();
            services.AddSingleton<PdfRedactionService>();

            // Comparison Service
            services.AddSingleton<IComparisonService, PdfComparisonService>();
            services.AddSingleton<PdfComparisonService>();

            // Annotation Export Service
            services.AddSingleton<AnnotationExportService>();

            // Searchable PDF Service
            services.AddSingleton<SearchablePdfService>();

            // Measurement Service
            services.AddSingleton<MeasurementService>();

            // Form Validation Service
            services.AddSingleton<FormValidationService>();

            // Visual Diff Service
            services.AddSingleton<VisualDiffService>();

            // Certificate Manager Service
            services.AddSingleton<CertificateManagerService>();

            // Booklet Service
            services.AddSingleton<PdfBookletService>();

            // Header/Footer Service
            services.AddSingleton<HeaderFooterService>();

            // Image Extraction Service
            services.AddSingleton<ImageExtractionService>();

            // Auto-Crop Service
            services.AddSingleton<AutoCropService>();

            // Table of Contents Service
            services.AddSingleton<TableOfContentsService>();

            // Electronic Signature Service
            services.AddSingleton<ElectronicSignatureService>();

            // Barcode Service
            services.AddSingleton<BarcodeService>();

            // Accessibility Checker Service
            services.AddSingleton<AccessibilityCheckerService>();

            // Metadata Scrubber Service
            services.AddSingleton<MetadataScrubberService>();

            // PDF Archiver Service (PDF/A)
            services.AddSingleton<PdfArchiverService>();

            // Batch Service
            services.AddSingleton<PdfBatchService>();

            // Crop Service
            services.AddSingleton<PdfCropService>();

            // XFDF Annotation Service
            services.AddSingleton<XfdfAnnotationService>();

            // Print to PDF Service
            services.AddSingleton<PrintToPdfService>();

            // --- Phase 6+ New Services ---

            // Deskew Service
            services.AddSingleton<DeskewService>();

            // Background Removal Service
            services.AddSingleton<BackgroundRemovalService>();

            // Image Compress Service
            services.AddSingleton<ImageCompressService>();

            // Image Replace Service
            services.AddSingleton<ImageReplaceService>();

            // Document Sanitizer Service
            services.AddSingleton<DocumentSanitizerService>();

            // Auto-Tag Service
            services.AddSingleton<AutoTagService>();

            // Alt Text Editor Service
            services.AddSingleton<AltTextEditorService>();

            // Font Replacement Service
            services.AddSingleton<FontReplacementService>();

            // PDF Text Edit Service
            services.AddSingleton<PdfTextEditService>();

            // Table Editor Service
            services.AddSingleton<TableEditorService>();

            // Calculation Field Service
            services.AddSingleton<CalculationFieldService>();

            // Conditional Logic Service
            services.AddSingleton<ConditionalLogicService>();

            // Quick Actions Service
            services.AddSingleton<QuickActionsService>();

            // Template Service
            services.AddSingleton<TemplateService>();

            // Watch Folder Service
            services.AddSingleton<WatchFolderService>();

            // PDF/X Service
            services.AddSingleton<PdfXService>();

            // --- Phase 12: Optimization ---
            services.AddSingleton<PdfOptimizer>();

            // Plugin Manager (scans ./Plugins directory at runtime)
            services.AddSingleton<PluginManager>();

            Logger.Info("PDF Editor Core services registered successfully");
            return services;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error registering PDF Editor Core services");
            throw;
        }
    }
}
