using NLog;
using Microsoft.Extensions.DependencyInjection;
using PDFEditor.Core.Services;
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
            
            // Image Processing
            // services.AddSingleton<IImageProcessor, ImageProcessorService>();
            
            // OCR Engine
            // services.AddSingleton<IOcrEngine, OcrEngineService>();

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
