using System.Reflection;
using NLog;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Core.Services;

/// <summary>
/// Scans a directory for plugin assemblies, loads them, and manages their lifecycle.
/// Plugins must implement <see cref="IPlugin"/> and expose a public no-arg constructor.
/// </summary>
public class PluginManager
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly string _pluginDirectory;
    private readonly IPluginContext _context;
    private readonly List<IPlugin> _loaded = [];

    public IReadOnlyList<IPlugin> Loaded => _loaded.AsReadOnly();

    public PluginManager(IPluginContext context, string? pluginDirectory = null)
    {
        _context = context;
        _pluginDirectory = pluginDirectory
            ?? Path.Combine(AppContext.BaseDirectory, "Plugins");
    }

    /// <summary>Scan the plugin directory, load all valid plugin assemblies, and call InitializeAsync.</summary>
    public async Task LoadAllAsync()
    {
        if (!Directory.Exists(_pluginDirectory))
        {
            Log.Info("Plugin directory not found; skipping plugin load: {Dir}", _pluginDirectory);
            return;
        }

        var dlls = Directory.GetFiles(_pluginDirectory, "*.dll", SearchOption.AllDirectories);
        Log.Info("Scanning {Count} DLLs for plugins in {Dir}", dlls.Length, _pluginDirectory);

        foreach (var dll in dlls)
        {
            try
            {
                var asm = Assembly.LoadFrom(dll);
                var pluginTypes = asm.GetExportedTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false });

                foreach (var type in pluginTypes)
                {
                    try
                    {
                        var plugin = (IPlugin)Activator.CreateInstance(type)!;
                        await plugin.InitializeAsync(_context);
                        _loaded.Add(plugin);
                        Log.Info("Loaded plugin: {Id} v{Ver}", plugin.Id, plugin.Version);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to initialise plugin type: {Type}", type.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to load assembly: {Dll}", dll);
            }
        }

        Log.Info("Plugin load complete: {Count} plugin(s) active.", _loaded.Count);
    }

    /// <summary>Execute the plugin with the given <paramref name="pluginId"/>.</summary>
    public async Task ExecuteAsync(string pluginId)
    {
        var plugin = _loaded.FirstOrDefault(p => p.Id == pluginId)
            ?? throw new InvalidOperationException($"Plugin not found: {pluginId}");

        Log.Info("Executing plugin: {Id}", pluginId);
        await plugin.ExecuteAsync(_context);
    }

    /// <summary>Shut down all plugins gracefully.</summary>
    public async Task UnloadAllAsync()
    {
        foreach (var plugin in _loaded)
        {
            try
            {
                Log.Info("Shutting down plugin: {Id}", plugin.Id);
                await plugin.ShutdownAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error shutting down plugin: {Id}", plugin.Id);
            }
        }
        _loaded.Clear();
    }
}
