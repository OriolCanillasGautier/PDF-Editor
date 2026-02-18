using NLog;
using PDFEditor.Core.Abstractions;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PDFEditor.Core.Services.Export;

/// <summary>
/// Hybrid DOCX export provider that uses pdf2docx (Python) for high-fidelity conversion
/// when available, falling back to iText7-based DocxExportProvider otherwise.
/// 
/// pdf2docx: https://github.com/ArtifexSoftware/pdf2docx (AGPL-3.0)
/// UXWing Icons: https://uxwing.com/ (Free for commercial use, SVG recommended)
/// </summary>
public class HybridDocxExportProvider : IExportProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly DocxExportProvider _fallbackProvider;
    private readonly string? _pythonPath;
    private readonly bool _pythonAvailable;

    public string FormatName => "Microsoft Word (DOCX)";
    public string[] SupportedExtensions => new[] { ".docx" };
    public bool SupportsBatch => true;
    public bool SupportsPerPageExport => false;

    /// <summary>
    /// Initializes the hybrid provider with optional Python/pdf2docx support.
    /// </summary>
    /// <param name="pythonPath">Optional path to Python executable. If null, auto-detect.</param>
    public HybridDocxExportProvider(string? pythonPath = null)
    {
        _fallbackProvider = new DocxExportProvider();
        _pythonPath = pythonPath;
        _pythonAvailable = DetectPythonAndPdf2Docx();
    }

    /// <summary>
    /// Checks if Python and pdf2docx are available on this system.
    /// </summary>
    private bool DetectPythonAndPdf2Docx()
    {
        try
        {
            var pythonExe = _pythonPath ?? FindPythonExecutable();
            if (string.IsNullOrEmpty(pythonExe))
            {
                Log.Debug("Python not found on system");
                return false;
            }

            // Check if pdf2docx is installed
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = "-c \"import pdf2docx; print(pdf2docx.__version__)\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            proc.WaitForExit(5000); // 5 second timeout

            if (proc.ExitCode == 0)
            {
                var version = proc.StandardOutput.ReadToEnd().Trim();
                Log.Info("pdf2docx available: version {Version}", version);
                return true;
            }
            else
            {
                var error = proc.StandardError.ReadToEnd();
                Log.Debug("pdf2docx not installed. Error: {Error}", error);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to detect Python/pdf2docx");
            return false;
        }
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
        var commonLocations = new[]
        {
            // Windows
            @"C:\Python39\python.exe",
            @"C:\Python310\python.exe",
            @"C:\Python311\python.exe",
            @"C:\Users\Public\Python\python.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python311", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python310", "python.exe"),
            // Linux
            "/usr/bin/python3",
            "/usr/local/bin/python3",
            // macOS
            "/usr/local/bin/python3",
            "/opt/homebrew/bin/python3"
        };

        foreach (var location in commonLocations)
        {
            if (File.Exists(location))
                return location;
        }

        return null;
    }

    /// <summary>
    /// Returns true if high-fidelity Python backend is available.
    /// </summary>
    public bool IsHighFidelityModeAvailable() => _pythonAvailable;

    public async Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        // Use Python backend if available and requested for high fidelity
        if (_pythonAvailable && options.UseHighFidelityEngine)
        {
            try
            {
                Log.Info("Using pdf2docx (Python) for high-fidelity DOCX export");
                return await ConvertWithPdf2DocxAsync(pdfBytes, options, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "pdf2docx conversion failed, falling back to iText7");
                // Fall through to iText7 fallback
            }
        }
        else
        {
            if (!_pythonAvailable)
                Log.Debug("pdf2docx not available, using iText7 fallback");
            else
                Log.Debug("Using iText7 fallback (high-fidelity not requested)");
        }

        // Fallback to iText7-based provider
        return await _fallbackProvider.ExportAsync(pdfBytes, options, cancellationToken);
    }

    /// <summary>
    /// Converts PDF to DOCX using pdf2docx Python library via subprocess.
    /// </summary>
    private async Task<ExportResult> ConvertWithPdf2DocxAsync(byte[] pdfBytes, ExportOptions options,
        CancellationToken cancellationToken)
    {
        var pythonExe = _pythonPath ?? FindPythonExecutable();
        if (string.IsNullOrEmpty(pythonExe))
            throw new InvalidOperationException("Python executable not found");

        // Create temp files for conversion
        var tempPdfPath = Path.Combine(Path.GetTempPath(), $"pdf2docx_{Guid.NewGuid():N}.pdf");
        var tempDocxPath = Path.ChangeExtension(tempPdfPath, ".docx");

        try
        {
            // Write PDF to temp file
            await File.WriteAllBytesAsync(tempPdfPath, pdfBytes, cancellationToken);

            // Build pdf2docx command
            // pdf2docx supports: --pages, --password, --multi_processing
            var args = new StringBuilder();
            args.Append($"-m pdf2docx convert \"{tempPdfPath}\" \"{tempDocxPath}\"");

            // Page range if specified
            if (options.PageIndices != null && options.PageIndices.Length > 0)
            {
                // Convert 0-based indices to pdf2docx page range format (1-based, comma-separated)
                var pages = string.Join(",", options.PageIndices.Select(i => i + 1));
                args.Append($" --pages {pages}");
            }

            // Multi-processing for faster conversion
            args.Append(" --multi_processing 1");

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = args.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath(),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                throw new InvalidOperationException("Failed to start Python process");

            // Wait for completion with cancellation support
            using (cancellationToken.Register(() =>
            {
                try { proc.Kill(); } catch { }
            }))
            {
                await Task.Run(() => proc.WaitForExit(), cancellationToken);
            }

            if (proc.ExitCode != 0)
            {
                var errorOutput = await proc.StandardError.ReadToEndAsync();
                throw new Exception($"pdf2docx failed with exit code {proc.ExitCode}: {errorOutput}");
            }

            if (!File.Exists(tempDocxPath))
                throw new Exception("pdf2docx completed but output file not created");

            // Read result
            var docxBytes = await File.ReadAllBytesAsync(tempDocxPath, cancellationToken);

            Log.Info("pdf2docx conversion successful: {Size} bytes", docxBytes.Length);

            return ExportResult.Ok(
                docxBytes,
                $"{options.BaseFileName}.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        }
        finally
        {
            // Cleanup temp files
            try
            {
                if (File.Exists(tempPdfPath))
                    File.Delete(tempPdfPath);
                if (File.Exists(tempDocxPath))
                    File.Delete(tempDocxPath);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to cleanup temp files after pdf2docx conversion");
            }
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
    public static async Task<bool> InstallPdf2DocxAsync(string? pythonPath = null)
    {
        try
        {
            var pythonExe = pythonPath ?? FindPythonExecutable();
            if (string.IsNullOrEmpty(pythonExe))
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = "-m pip install pdf2docx --upgrade --quiet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

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
        sb.AppendLine("Installation:");
        sb.AppendLine("  1. Install Python from https://www.python.org/downloads/");
        sb.AppendLine("  2. During installation, check 'Add Python to PATH'");
        sb.AppendLine("  3. Open Command Prompt and run:");
        sb.AppendLine("     pip install pdf2docx");
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
