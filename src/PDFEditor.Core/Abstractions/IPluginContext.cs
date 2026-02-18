namespace PDFEditor.Core.Abstractions;

/// <summary>Context injected into plugins at load time, giving access to core editor services.</summary>
public interface IPluginContext
{
    /// <summary>The currently open PDF document, or null if no document is loaded.</summary>
    IPdfDocument? ActiveDocument { get; }

    /// <summary>Invoke the registered export provider for the given format.</summary>
    Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options, CancellationToken ct = default);

    /// <summary>Display a status-bar message.</summary>
    void ShowStatus(string message);

    /// <summary>Write a message to the application log.</summary>
    void Log(string level, string message, Exception? ex = null);
}
