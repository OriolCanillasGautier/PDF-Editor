using NLog;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Manages registered export providers and provides methods to look up providers by format.
/// </summary>
public class ExportProviderRegistry
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly List<IExportProvider> _providers = new();

    /// <summary>
    /// All registered export providers
    /// </summary>
    public IReadOnlyList<IExportProvider> Providers => _providers.AsReadOnly();

    /// <summary>
    /// Registers an export provider
    /// </summary>
    public void Register(IExportProvider provider)
    {
        _providers.Add(provider);
        Log.Info("Registered export provider: {Format} ({Extensions})",
            provider.FormatName, string.Join(", ", provider.SupportedExtensions));
    }

    /// <summary>
    /// Gets all providers that support a given file extension
    /// </summary>
    public IEnumerable<IExportProvider> GetProvidersByExtension(string extension)
    {
        extension = extension.ToLowerInvariant();
        if (!extension.StartsWith("."))
            extension = "." + extension;
        return _providers.Where(p =>
            p.SupportedExtensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Gets a provider by format name
    /// </summary>
    public IExportProvider? GetProviderByName(string formatName)
    {
        return _providers.FirstOrDefault(p =>
            p.FormatName.Equals(formatName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a registry with all built-in providers registered
    /// </summary>
    public static ExportProviderRegistry CreateDefault()
    {
        var registry = new ExportProviderRegistry();
        registry.Register(new ImageExportProvider());
        registry.Register(new TextExportProvider());
        registry.Register(new HtmlExportProvider());
        registry.Register(new DocxExportProvider());
        return registry;
    }
}
