using Microsoft.Extensions.DependencyInjection;
using PDFEditor.Core;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Tests.Helpers;

/// <summary>Shared test utilities and minimal service provider for unit tests.</summary>
internal static class TestHelpers
{
    private static IServiceProvider? _provider;

    /// <summary>
    /// Builds (and caches) a minimal DI container suitable for unit tests.
    /// Services that require external tools (Tesseract, Python, etc.) are skipped gracefully.
    /// </summary>
    public static IServiceProvider BuildServiceProvider()
    {
        if (_provider != null) return _provider;

        var services = new ServiceCollection();

        // Register core services; tests that need unavailable services check for null
        try { services.AddPDFEditorCore(); }
        catch { /* Some environments may not have all native deps */ }

        _provider = services.BuildServiceProvider();
        return _provider;
    }

    /// <summary>Returns a minimal but valid single-page PDF as a byte array.</summary>
    public static byte[] MinimalPdfBytes()
    {
        const string minPdf =
            "%PDF-1.4\n" +
            "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
            "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
            "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>endobj\n" +
            "xref\n0 4\n0000000000 65535 f\r\n" +
            "0000000009 00000 n\r\n" +
            "0000000058 00000 n\r\n" +
            "0000000115 00000 n\r\n" +
            "trailer<</Size 4/Root 1 0 R>>\n" +
            "startxref\n190\n%%EOF\n";
        return System.Text.Encoding.ASCII.GetBytes(minPdf);
    }

    /// <summary>Try to get a service, returning null instead of throwing if not registered.</summary>
    public static T? TryGetService<T>(this IServiceProvider provider) where T : class
    {
        try { return provider.GetService(typeof(T)) as T; }
        catch { return null; }
    }
}
