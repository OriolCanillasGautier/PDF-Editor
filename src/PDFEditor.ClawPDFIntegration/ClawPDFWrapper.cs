using System.Diagnostics;
using System.Runtime.InteropServices;
using NLog;

namespace PDFEditor.ClawPDFIntegration;

/// <summary>
/// ClawPDF virtual printer integration.
/// ClawPDF is a free virtual printer that converts any printable document to PDF.
/// Download: https://github.com/clawsoftware/clawPDF
/// </summary>
public class ClawPDFWrapper
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly string _clawPdfExePath;

    private static readonly string[] DefaultSearchPaths =
    [
        @"C:\Program Files\clawPDF\clawPDF.exe",
        @"C:\Program Files (x86)\clawPDF\clawPDF.exe",
        Environment.ExpandEnvironmentVariables(@"%LocalAppData%\clawPDF\clawPDF.exe"),
    ];

    /// <param name="clawPdfExePath">
    ///   Full path to clawPDF.exe. Defaults to auto-detection from well-known install locations.
    /// </param>
    public ClawPDFWrapper(string? clawPdfExePath = null)
    {
        _clawPdfExePath = clawPdfExePath ?? AutoDetect() ?? "clawPDF.exe";
    }

    /// <summary>Returns true if clawPDF.exe can be found.</summary>
    public bool IsAvailable() => File.Exists(_clawPdfExePath) || FindOnPath("clawPDF.exe") != null;

    /// <summary>
    /// Sends <paramref name="inputFilePath"/> to clawPDF and saves the resulting PDF
    /// to <paramref name="outputPdfPath"/>.
    /// </summary>
    public void PrintToPdf(string inputFilePath, string outputPdfPath,
        string? printerName = null, int timeoutSeconds = 60)
    {
        if (!File.Exists(inputFilePath))
            throw new FileNotFoundException("Input file not found.", inputFilePath);

        var outDir = Path.GetDirectoryName(outputPdfPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        Log.Info("ClawPDF: printing {Input} → {Output}", inputFilePath, outputPdfPath);

        var args = BuildArgs(("/PrintFile",  QuotePath(inputFilePath)),
                             ("/OutputFile", QuotePath(outputPdfPath)));
        if (!string.IsNullOrEmpty(printerName))
            args += $" /PrinterName={QuotePath(printerName)}";

        RunProcess(args, timeoutSeconds);

        if (!File.Exists(outputPdfPath))
            throw new InvalidOperationException(
                $"clawPDF did not produce the expected output file: {outputPdfPath}");

        Log.Info("ClawPDF: output written ({Bytes} bytes)", new FileInfo(outputPdfPath).Length);
    }

    /// <summary>
    /// Merges <paramref name="inputFiles"/> into a single PDF by printing each with /Append.
    /// Requires clawPDF 3.x+ which supports the /Append flag.
    /// </summary>
    public void MergeDocuments(string[] inputFiles, string outputPath, int timeoutSeconds = 120)
    {
        if (inputFiles == null || inputFiles.Length == 0)
            throw new ArgumentException("At least one input file is required.", nameof(inputFiles));

        foreach (var f in inputFiles)
            if (!File.Exists(f))
                throw new FileNotFoundException($"Input file not found: {f}");

        var outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        Log.Info("ClawPDF: merging {Count} files → {Output}", inputFiles.Length, outputPath);

        for (int i = 0; i < inputFiles.Length; i++)
        {
            var args = BuildArgs(("/PrintFile",  QuotePath(inputFiles[i])),
                                 ("/OutputFile", QuotePath(outputPath)));
            if (i > 0) args += " /Append";
            RunProcess(args, timeoutSeconds);
        }

        if (!File.Exists(outputPath))
            throw new InvalidOperationException($"Merge did not produce output: {outputPath}");

        Log.Info("ClawPDF: merge complete → {Output}", outputPath);
    }

    /// <summary>Opens the clawPDF settings/configuration UI.</summary>
    public void OpenSettings()
    {
        var exe = File.Exists(_clawPdfExePath) ? _clawPdfExePath : FindOnPath("clawPDF.exe") ?? _clawPdfExePath;
        Process.Start(new ProcessStartInfo(exe, "/Settings") { UseShellExecute = true });
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    private void RunProcess(string arguments, int timeoutSeconds)
    {
        var exe = File.Exists(_clawPdfExePath) ? _clawPdfExePath : FindOnPath("clawPDF.exe") ?? _clawPdfExePath;
        Log.Debug("ClawPDF exec: \"{Exe}\" {Args}", exe, arguments);

        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start clawPDF process.");

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();

        bool finished = proc.WaitForExit(timeoutSeconds * 1000);
        if (!finished)
        {
            try { proc.Kill(); } catch { /* best effort */ }
            throw new TimeoutException($"clawPDF did not finish within {timeoutSeconds} seconds.");
        }

        if (proc.ExitCode != 0)
        {
            Log.Warn("ClawPDF exited {Code}. stderr: {Err}", proc.ExitCode, stderr);
            throw new InvalidOperationException($"clawPDF exited {proc.ExitCode}: {stderr.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(stdout)) Log.Debug("ClawPDF stdout: {Out}", stdout);
    }

    private static string BuildArgs(params (string Flag, string Value)[] flags) =>
        string.Join(" ", flags.Select(f => $"{f.Flag}={f.Value}"));

    private static string QuotePath(string path) =>
        path.Contains(' ') ? $"\"{path}\"" : path;

    private static string? AutoDetect()
    {
        foreach (var p in DefaultSearchPaths)
            if (File.Exists(p)) return p;
        return FindOnPath("clawPDF.exe");
    }

    private static string? FindOnPath(string exeName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';'))
        {
            var full = Path.Combine(dir.Trim(), exeName);
            if (File.Exists(full)) return full;
        }
        return null;
    }
}
