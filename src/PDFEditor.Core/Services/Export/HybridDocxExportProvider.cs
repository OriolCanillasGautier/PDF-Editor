using NLog;
using PDFEditor.Core.Abstractions;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Hybrid DOCX export provider.  Detection priority:
///   1. pdf2docx-cli sidecar exe  (next to app binary, no Python required)
///   2. Python 3.8+ in PATH/common locations with pdf2docx installed
///   3. iText7-based DocxExportProvider (built-in fallback)
///
/// Build the sidecar with:  tools\pdf2docx-cli\build.ps1  (Windows)
///                           tools/pdf2docx-cli/build.sh   (Linux/macOS)
///
/// pdf2docx: https://github.com/ArtifexSoftware/pdf2docx (AGPL-3.0)
/// Pinned release: https://github.com/ArtifexSoftware/pdf2docx/releases/tag/v0.5.9
/// </summary>
public class HybridDocxExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Pinned pdf2docx 0.5.9 wheel – pure-Python, no compiler needed.</summary>
    public const string Pdf2DocxWheelUrl =
        "https://github.com/ArtifexSoftware/pdf2docx/releases/download/v0.5.9/pdf2docx-0.5.9-py3-none-any.whl";

    private enum Backend { Sidecar, Python, IText7 }

    private readonly DocxExportProvider _fallbackProvider;
    private readonly Backend _backend;
    /// <summary>
    /// Path to the converter executable:
    ///   Sidecar mode → full path to pdf2docx-cli(.exe)
    ///   Python mode  → full path to python(.exe)
    /// </summary>
    private readonly string? _converterExe;

    public string FormatName => "Microsoft Word (DOCX)";
    public string[] SupportedExtensions => new[] { ".docx" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    /// <param name="pythonPath">Override Python executable path (optional).</param>
    public HybridDocxExportProvider(string? pythonPath = null)
    {
        _fallbackProvider = new DocxExportProvider();
        (_backend, _converterExe) = DetectBackend(pythonPath);
        Log.Info("HybridDocxExportProvider backend: {Backend} ({Exe})",
            _backend, _converterExe ?? "(none)");
    }

    // ── backend detection ────────────────────────────────────────────────────

    private static (Backend backend, string? exe) DetectBackend(string? pythonOverride)
    {
        // 1. Sidecar – copy pdf2docx-cli.exe next to the app and it just works
        var sidecar = FindSidecarExe();
        if (sidecar != null)
        {
            Log.Info("pdf2docx-cli sidecar found: {Path}", sidecar);
            return (Backend.Sidecar, sidecar);
        }

        // 2. Python + pdf2docx installed in environment
        var python = pythonOverride ?? FindPythonExecutable();
        if (!string.IsNullOrEmpty(python) && Pdf2DocxIsInstalled(python))
        {
            Log.Info("pdf2docx via Python: {Python}", python);
            return (Backend.Python, python);
        }

        // 3. Fallback
        Log.Debug("No pdf2docx backend found; using iText7 fallback");
        return (Backend.IText7, null);
    }

    /// <summary>
    /// Looks for pdf2docx-cli(.exe) in the same folder as the application binary.
    /// Copy the file there after running tools/pdf2docx-cli/build.ps1.
    /// </summary>
    private static string? FindSidecarExe()
    {
        var sidecarNames = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "pdf2docx-cli.exe" }
            : new[] { "pdf2docx-cli" };

        var searchDirs = new[]
        {
            AppContext.BaseDirectory,
            // When running under dotnet run the binary lives several levels deep
            Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? ""),
        };

        foreach (var dir in searchDirs)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            foreach (var name in sidecarNames)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    private static bool Pdf2DocxIsInstalled(string pythonExe)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = "-c \"import pdf2docx; print('ok')\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(5000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Finds Python executable in common locations.
    /// </summary>
    private static string? FindPythonExecutable()
    {
        // Check PATH first
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        var pythonNames = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "python.exe", "python3.exe", "python3.9.exe", "python3.10.exe", "python3.11.exe" }
            : new[] { "python3", "python" };

        foreach (var path in paths)
        {
            foreach (var name in pythonNames)
            {
                var candidate = Path.Combine(path, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        // Check common installation locations
        var commonLocations = new List<string>
        {
            // .venv in repo/app root (development convenience)
            Path.Combine(AppContext.BaseDirectory, ".venv", "Scripts", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, ".venv", "bin", "python3"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".venv", "Scripts", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".venv", "bin", "python3"),
            // Windows system installs
            @"C:\Python39\python.exe",
            @"C:\Python310\python.exe",
            @"C:\Python311\python.exe",
            @"C:\Python312\python.exe",
            @"C:\Python313\python.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python313", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python312", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python311", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python310", "python.exe"),
            // Linux / macOS
            "/usr/bin/python3",
            "/usr/local/bin/python3",
            "/opt/homebrew/bin/python3",
        };

        foreach (var location in commonLocations)
        {
            if (File.Exists(location))
                return location;
        }

        return null;
    }

    /// <summary>Returns true if any pdf2docx backend is available (sidecar or Python).</summary>
    public bool IsHighFidelityModeAvailable() => _backend != Backend.IText7;

    /// <summary>Returns true when the self-contained sidecar exe is in use (no Python required).</summary>
    public bool IsSidecarMode => _backend == Backend.Sidecar;

    public async Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        if (_backend != Backend.IText7 && options.UseHighFidelityEngine)
        {
            try
            {
                Log.Info("Using pdf2docx ({Mode}) for high-fidelity DOCX export", _backend);
                return await ConvertWithPdf2DocxAsync(pdfBytes, options, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "pdf2docx conversion failed, falling back to iText7");
            }
        }
        else
        {
            Log.Debug(_backend == Backend.IText7
                ? "No pdf2docx backend available; using iText7"
                : "UseHighFidelityEngine=false; using iText7");
        }

        return await _fallbackProvider.ExportAsync(pdfBytes, options, cancellationToken);
    }

    /// <summary>
    /// Converts PDF to DOCX by calling either the pdf2docx-cli sidecar or
    /// Python with -m pdf2docx.  Both accept the same CLI arguments.
    /// </summary>
    private async Task<ExportResult> ConvertWithPdf2DocxAsync(byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken)
    {
        if (_converterExe == null)
            throw new InvalidOperationException("No pdf2docx converter exe configured");

        var tempPdfPath  = Path.Combine(Path.GetTempPath(), $"pdf2docx_{Guid.NewGuid():N}.pdf");
        var tempDocxPath = Path.ChangeExtension(tempPdfPath, ".docx");

        try
        {
            await File.WriteAllBytesAsync(tempPdfPath, pdfBytes, cancellationToken);

            // Build argument list
            // Sidecar:  pdf2docx-cli convert in.pdf out.docx [--pages 1,2,3]
            // Python:   python         -m pdf2docx convert in.pdf out.docx [--pages 1,2,3]
            var args = new StringBuilder();
            if (_backend == Backend.Python)
                args.Append("-m pdf2docx ");
            args.Append($"convert \"{tempPdfPath}\" \"{tempDocxPath}\"");

            if (options.PageIndices is { Length: > 0 })
            {
                var pages = string.Join(",", options.PageIndices.Select(i => i + 1)); // 0-based → 1-based
                args.Append($" --pages {pages}");
            }

            var psi = new ProcessStartInfo
            {
                FileName = _converterExe,
                Arguments = args.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute  = false,
                CreateNoWindow   = true,
                WorkingDirectory = Path.GetTempPath(),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start converter process");

            using (cancellationToken.Register(() => { try { proc.Kill(); } catch { } }))
                await Task.Run(() => proc.WaitForExit(), cancellationToken);

            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync();
                throw new Exception($"pdf2docx exited {proc.ExitCode}: {err}");
            }

            if (!File.Exists(tempDocxPath))
                throw new Exception("pdf2docx completed but output file was not created");

            var docxBytes = await File.ReadAllBytesAsync(tempDocxPath, cancellationToken);
            Log.Info("pdf2docx conversion OK: {Bytes} bytes", docxBytes.Length);

            return ExportResult.Ok(
                docxBytes,
                $"{options.BaseFileName}.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        }
        finally
        {
            try { if (File.Exists(tempPdfPath))  File.Delete(tempPdfPath);  } catch { }
            try { if (File.Exists(tempDocxPath)) File.Delete(tempDocxPath); } catch { }
        }
    }

    public Task<List<ExportResult>> ExportPagesAsync(byte[] pdfBytes, ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("DOCX export produces a single document. Use ExportAsync instead.");
    }

    /// <summary>
    /// Installs pdf2docx via pip (requires Python with pip).
    /// </summary>
    /// <param name="pythonPath">Optional path to Python executable. If null, auto-detect.</param>
    /// <param name="wheelUrlOrPath">
    /// Optional path or URL to a pdf2docx wheel file. Defaults to the pinned 0.5.9 release wheel.
    /// Pass null to install the latest version from PyPI instead.
    /// </param>
    public static async Task<bool> InstallPdf2DocxAsync(
        string? pythonPath = null,
        string? wheelUrlOrPath = Pdf2DocxWheelUrl)
    {
        try
        {
            var pythonExe = pythonPath ?? FindPythonExecutable();
            if (string.IsNullOrEmpty(pythonExe))
                return false;

            // Use the pinned wheel when provided, otherwise latest from PyPI
            var installTarget = string.IsNullOrEmpty(wheelUrlOrPath)
                ? "pdf2docx"
                : wheelUrlOrPath;

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"-m pip install \"{installTarget}\" --quiet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Log.Info("Installing pdf2docx from: {Source}", installTarget);
            using var proc = Process.Start(psi);
            if (proc == null) return false;

            await Task.Run(() => proc.WaitForExit(120000)); // 2 minute timeout for pip

            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to install pdf2docx");
            return false;
        }
    }

    /// <summary>
    /// Gets installation instructions for pdf2docx.
    /// </summary>
    public static string GetInstallationInstructions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("pdf2docx Installation Instructions");
        sb.AppendLine("===================================");
        sb.AppendLine();
        sb.AppendLine("pdf2docx is an optional high-fidelity PDF to DOCX converter.");
        sb.AppendLine("It provides better layout preservation than the built-in iText7 engine.");
        sb.AppendLine();
        sb.AppendLine("Requirements:");
        sb.AppendLine("  - Python 3.8 or later");
        sb.AppendLine("  - pip (Python package manager)");
        sb.AppendLine();
        sb.AppendLine("Installation (recommended — pinned v0.5.9 wheel, no compiler needed):");
        sb.AppendLine($"  pip install {Pdf2DocxWheelUrl}");
        sb.AppendLine();
        sb.AppendLine("Or install the latest available version from PyPI:");
        sb.AppendLine("  pip install pdf2docx");
        sb.AppendLine();
        sb.AppendLine("If you don't have Python yet:");
        sb.AppendLine("  1. Install Python from https://www.python.org/downloads/");
        sb.AppendLine("  2. During installation, check 'Add Python to PATH'");
        sb.AppendLine("  3. Then run one of the pip commands above");
        sb.AppendLine();
        sb.AppendLine("License: AGPL-3.0 (https://github.com/ArtifexSoftware/pdf2docx)");
        sb.AppendLine("Note: Using pdf2docx requires compliance with AGPL license terms.");
        sb.AppendLine();
        sb.AppendLine("Alternatives:");
        sb.AppendLine("  - Built-in iText7 engine (no Python required, good for most documents)");
        sb.AppendLine("  - Commercial license from Artifex for proprietary use cases");
        return sb.ToString();
    }
}
