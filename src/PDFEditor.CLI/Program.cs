using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using PDFEditor.Core;
using PDFEditor.Core.Abstractions;
using PDFEditor.Core.Services;

// ─────────────────────────────────────────────────────────────────────────────
// PDF Editor CLI — batch PDF operations from the command line
// Usage: pdfeditor <command> [options]
// ─────────────────────────────────────────────────────────────────────────────

var services = new ServiceCollection()
    .AddPDFEditorCore()
    .BuildServiceProvider();

var root = new RootCommand("PDF Editor command-line batch tool");

// ── merge ─────────────────────────────────────────────────────────────────────
{
    var inputsOpt = new Option<FileInfo[]>("--inputs", "Input PDF files to merge")
        { IsRequired = true, AllowMultipleArgumentsPerToken = true };
    var outputOpt = new Option<FileInfo>("--output", "Output PDF path") { IsRequired = true };
    var cmd = new Command("merge", "Merge multiple PDFs into one") { inputsOpt, outputOpt };
    cmd.SetHandler((FileInfo[] inputs, FileInfo output) =>
    {
        var pdf = services.GetRequiredService<IPdfDocument>();
        pdf.LoadFromFile(inputs[0].FullName);
        foreach (var file in inputs.Skip(1))
        {
            var extra = services.GetRequiredService<IPdfDocument>();
            extra.LoadFromFile(file.FullName);
            pdf.Merge(extra);
        }
        pdf.SaveToFile(output.FullName);
        Console.WriteLine($"Merged {inputs.Length} files → {output.FullName}");
    }, inputsOpt, outputOpt);
    root.AddCommand(cmd);
}

// ── info ──────────────────────────────────────────────────────────────────────
{
    var inputOpt = new Option<FileInfo>("--input", "PDF file") { IsRequired = true };
    var cmd = new Command("info", "Show metadata and page count") { inputOpt };
    cmd.SetHandler((FileInfo input) =>
    {
        var pdf = services.GetRequiredService<IPdfDocument>();
        pdf.LoadFromFile(input.FullName);
        Console.WriteLine($"File    : {input.FullName}");
        Console.WriteLine($"Pages   : {pdf.PageCount}");
        Console.WriteLine($"Size    : {input.Length:N0} bytes");
        Console.WriteLine($"Title   : {pdf.Title ?? "(none)"}");
        Console.WriteLine($"Author  : {pdf.Author ?? "(none)"}");
        foreach (var kv in pdf.Metadata)
            Console.WriteLine($"{kv.Key,-12}: {kv.Value}");
    }, inputOpt);
    root.AddCommand(cmd);
}

// ── compress ──────────────────────────────────────────────────────────────────
{
    var inputOpt   = new Option<FileInfo>("--input",   "Input PDF")  { IsRequired = true };
    var outputOpt  = new Option<FileInfo>("--output",  "Output PDF") { IsRequired = true };
    var qualityOpt = new Option<int>("--quality", () => 75, "JPEG quality 1-100");
    var cmd = new Command("compress", "Compress/optimise a PDF") { inputOpt, outputOpt, qualityOpt };
    cmd.SetHandler(async (FileInfo input, FileInfo output, int quality) =>
    {
        var optimizer = services.GetRequiredService<PdfOptimizer>();
        var bytes  = await File.ReadAllBytesAsync(input.FullName);
        var opts   = new PdfOptimizationOptions { CompressImages = true, ImageQuality = quality, OptimizeStreams = true };
        var result = optimizer.Optimize(bytes, opts);
        await File.WriteAllBytesAsync(output.FullName, result);
        double pct = 100.0 * (1 - (double)result.Length / bytes.Length);
        Console.WriteLine($"Compressed: {bytes.Length:N0} → {result.Length:N0} bytes ({pct:F1}% saved)");
    }, inputOpt, outputOpt, qualityOpt);
    root.AddCommand(cmd);
}

// ── redact ────────────────────────────────────────────────────────────────────
{
    var inputOpt  = new Option<FileInfo>("--input",   "Input PDF")  { IsRequired = true };
    var outputOpt = new Option<FileInfo>("--output",  "Output PDF") { IsRequired = true };
    var searchOpt = new Option<string[]>("--search", "Text patterns to redact")
        { IsRequired = true, AllowMultipleArgumentsPerToken = true };
    var cmd = new Command("redact", "Permanently redact text patterns from a PDF") { inputOpt, outputOpt, searchOpt };
    cmd.SetHandler(async (FileInfo input, FileInfo output, string[] patterns) =>
    {
        var redact = services.GetRequiredService<IRedactionService>();
        var result = await File.ReadAllBytesAsync(input.FullName);
        foreach (var p in patterns)
            result = redact.RedactText(result, p);
        await File.WriteAllBytesAsync(output.FullName, result);
        Console.WriteLine($"Redacted {patterns.Length} pattern(s) → {output.FullName}");
    }, inputOpt, outputOpt, searchOpt);
    root.AddCommand(cmd);
}

// ── watermark ─────────────────────────────────────────────────────────────────
{
    var inputOpt  = new Option<FileInfo>("--input",  "Input PDF")  { IsRequired = true };
    var outputOpt = new Option<FileInfo>("--output", "Output PDF") { IsRequired = true };
    var textOpt   = new Option<string>("--text",     "Watermark text") { IsRequired = true };
    var opacOpt   = new Option<float>("--opacity",   () => 0.3f, "Opacity 0-1");
    var cmd = new Command("watermark", "Add a diagonal text watermark to every page") { inputOpt, outputOpt, textOpt, opacOpt };
    cmd.SetHandler(async (FileInfo input, FileInfo output, string text, float opacity) =>
    {
        var wm     = new PdfWatermarkService();
        var bytes  = await File.ReadAllBytesAsync(input.FullName);
        var result = wm.AddTextWatermark(bytes, text, opacity: opacity);
        await File.WriteAllBytesAsync(output.FullName, result);
        Console.WriteLine($"Watermarked → {output.FullName}");
    }, inputOpt, outputOpt, textOpt, opacOpt);
    root.AddCommand(cmd);
}

// ── ocr ───────────────────────────────────────────────────────────────────────
{
    var inputOpt  = new Option<FileInfo>("--input",   "Input PDF")     { IsRequired = true };
    var outputOpt = new Option<FileInfo>("--output",  "Output text file") { IsRequired = true };
    var langOpt   = new Option<string>("--language",  () => "eng", "Tesseract language code");
    var cmd = new Command("ocr", "Extract text from a scanned PDF page-by-page") { inputOpt, outputOpt, langOpt };
    cmd.SetHandler(async (FileInfo input, FileInfo output, string lang) =>
    {
        var ocr = services.GetRequiredService<IOcrEngine>();
        var pdf = services.GetRequiredService<IPdfDocument>();
        pdf.LoadFromFile(input.FullName);
        var sb = new System.Text.StringBuilder();
        for (int i = 1; i <= pdf.PageCount; i++)
        {
            var page = pdf.GetPage(i);
            if (page == null) continue;
            var imgBytes = page.RenderToImage(300f);
            var text     = await ocr.RecognizeText(imgBytes, lang);
            sb.AppendLine($"=== Page {i} ===").AppendLine(text);
        }
        await File.WriteAllTextAsync(output.FullName, sb.ToString());
        Console.WriteLine($"OCR complete ({pdf.PageCount} pages) → {output.FullName}");
    }, inputOpt, outputOpt, langOpt);
    root.AddCommand(cmd);
}

return await root.InvokeAsync(args);
