namespace PDFEditor.Core.Abstractions;

/// <summary>Contract every plugin must implement.</summary>
public interface IPlugin
{
    /// <summary>Unique plugin identifier, e.g. "com.example.MyPlugin".</summary>
    string Id { get; }

    /// <summary>Human-readable display name shown in the Plugin Manager UI.</summary>
    string Name { get; }

    /// <summary>Semantic-version string, e.g. "1.0.0".</summary>
    string Version { get; }

    /// <summary>Short description shown in the Plugin Manager UI.</summary>
    string Description { get; }

    /// <summary>Called once after the plugin assembly is loaded. Initialize resources here.</summary>
    Task InitializeAsync(IPluginContext context);

    /// <summary>Called when the application shuts down. Release resources here.</summary>
    Task ShutdownAsync();

    /// <summary>
    /// Execute the plugin's primary action (e.g., open a dialog, process the active document).
    /// </summary>
    Task ExecuteAsync(IPluginContext context);
}
