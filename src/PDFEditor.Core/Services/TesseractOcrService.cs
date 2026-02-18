using NLog;
using Tesseract;

namespace PDFEditor.Core.Services;

/// <summary>
/// OCR engine implementation using Tesseract.NET for text recognition from images.
/// Requires tessdata files to be available at the configured path.
/// </summary>
public class TesseractOcrService : Abstractions.IOcrEngine, IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private TesseractEngine? _engine;
    private string _currentLanguage = string.Empty;
    private readonly string _tessDataPath;
    private bool _disposed;

    /// <summary>
    /// Creates a new Tesseract OCR service.
    /// </summary>
    /// <param name="tessDataPath">
    /// Path to the tessdata directory containing trained data files.
    /// If null, searches common locations automatically.
    /// </param>
    public TesseractOcrService(string? tessDataPath = null)
    {
        _tessDataPath = tessDataPath ?? FindTessDataPath();
        Log.Info("TesseractOcrService created. TessData path: {Path}", _tessDataPath);
    }

    /// <summary>
    /// Recognizes text from image data (PNG, JPEG, TIFF, BMP)
    /// </summary>
    public async Task<string> RecognizeText(byte[] imageData, string language = "eng")
    {
        return await Task.Run(() =>
        {
            try
            {
                EnsureEngine(language);
                if (_engine == null)
                    return "[OCR Error: Tesseract engine not initialized. Check tessdata path.]";

                using var pix = Pix.LoadFromMemory(imageData);
                using var page = _engine.Process(pix);
                var text = page.GetText();
                var confidence = page.GetMeanConfidence();
                Log.Info("OCR completed. Confidence: {Confidence:P}, Length: {Length}",
                    confidence, text.Length);
                return text;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "OCR recognition failed");
                return $"[OCR Error: {ex.Message}]";
            }
        });
    }

    /// <summary>
    /// Recognizes text regions with bounding boxes and confidence scores
    /// </summary>
    public async Task<List<Abstractions.OcrResult>> RecognizeTextRegions(
        byte[] imageData, string language = "eng")
    {
        return await Task.Run(() =>
        {
            var results = new List<Abstractions.OcrResult>();
            try
            {
                EnsureEngine(language);
                if (_engine == null) return results;

                using var pix = Pix.LoadFromMemory(imageData);
                using var page = _engine.Process(pix);
                using var iter = page.GetIterator();

                iter.Begin();
                do
                {
                    if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
                    {
                        var word = iter.GetText(PageIteratorLevel.Word);
                        var conf = iter.GetConfidence(PageIteratorLevel.Word);

                        if (!string.IsNullOrWhiteSpace(word))
                        {
                            results.Add(new Abstractions.OcrResult
                            {
                                Text = word.Trim(),
                                Confidence = conf / 100f,
                                BoundingBox = (rect.X1, rect.Y1,
                                    rect.X2 - rect.X1, rect.Y2 - rect.Y1)
                            });
                        }
                    }
                } while (iter.Next(PageIteratorLevel.Word));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "OCR region recognition failed");
            }
            return results;
        });
    }

    /// <summary>
    /// Gets a list of supported languages based on available tessdata files
    /// </summary>
    public List<string> GetSupportedLanguages()
    {
        var languages = new List<string>();
        try
        {
            if (Directory.Exists(_tessDataPath))
            {
                var files = Directory.GetFiles(_tessDataPath, "*.traineddata");
                foreach (var file in files)
                {
                    var lang = Path.GetFileNameWithoutExtension(file);
                    languages.Add(lang);
                }
            }

            if (languages.Count == 0)
            {
                Log.Warn("No tessdata files found at {Path}", _tessDataPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enumerate tessdata languages");
        }
        return languages;
    }

    /// <summary>
    /// Whether the OCR engine is available (tessdata path exists)
    /// </summary>
    public bool IsAvailable => Directory.Exists(_tessDataPath) &&
        Directory.GetFiles(_tessDataPath, "*.traineddata").Length > 0;

    /// <summary>
    /// The configured tessdata path
    /// </summary>
    public string TessDataPath => _tessDataPath;

    /// <summary>
    /// Performs OCR on a rendered PDF page and returns the recognized text
    /// </summary>
    public async Task<string> OcrPdfPage(byte[] pdfBytes, int pageIndex,
        string language = "eng", int dpi = 300)
    {
        var renderService = new PdfRenderService();
        int scaledWidth = (int)(8.5 * dpi);
        int scaledHeight = (int)(11.0 * dpi);
        var (pixels, width, height) = renderService.RenderPage(pdfBytes, pageIndex, scaledWidth, scaledHeight);

        // Convert BGRA to PNG using Magick.NET
        using var image = new ImageMagick.MagickImage();
        var settings = new ImageMagick.PixelReadSettings(
            (uint)width, (uint)height,
            ImageMagick.StorageType.Char,
            ImageMagick.PixelMapping.BGRA);
        image.ReadPixels(pixels, settings);
        image.Format = ImageMagick.MagickFormat.Png;

        using var ms = new MemoryStream();
        image.Write(ms);
        var pngBytes = ms.ToArray();

        return await RecognizeText(pngBytes, language);
    }

    /// <summary>
    /// Performs OCR on all pages of a PDF and returns concatenated text
    /// </summary>
    public async Task<string> OcrEntirePdf(byte[] pdfBytes,
        string language = "eng", int dpi = 300,
        IProgress<(int current, int total)>? progress = null)
    {
        var pdfOps = new PdfOperations();
        int pageCount = pdfOps.GetPageCount(pdfBytes);
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < pageCount; i++)
        {
            progress?.Report((i + 1, pageCount));
            if (i > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"--- Page {i + 1} ---");
                sb.AppendLine();
            }
            var text = await OcrPdfPage(pdfBytes, i, language, dpi);
            sb.Append(text);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Represents the result of batch OCR processing for a single file.
    /// </summary>
    public class BatchOcrFileResult
    {
        public string FileName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int PageCount { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Represents per-page OCR result with confidence scores.
    /// </summary>
    public class PageOcrResult
    {
        public int PageIndex { get; set; }
        public string Text { get; set; } = string.Empty;
        public float MeanConfidence { get; set; }
        public List<Abstractions.OcrResult> WordResults { get; set; } = new();
    }

    /// <summary>
    /// Performs OCR on a single PDF page and returns detailed results with confidence scores.
    /// </summary>
    public async Task<PageOcrResult> OcrPdfPageDetailed(byte[] pdfBytes, int pageIndex,
        string language = "eng", int dpi = 300)
    {
        var result = new PageOcrResult { PageIndex = pageIndex };

        try
        {
            var renderService = new PdfRenderService();
            int scaledWidth = (int)(8.5 * dpi);
            int scaledHeight = (int)(11.0 * dpi);
            var (pixels, width, height) = renderService.RenderPage(pdfBytes, pageIndex, scaledWidth, scaledHeight);

            using var image = new ImageMagick.MagickImage();
            var settings = new ImageMagick.PixelReadSettings(
                (uint)width, (uint)height,
                ImageMagick.StorageType.Char,
                ImageMagick.PixelMapping.BGRA);
            image.ReadPixels(pixels, settings);
            image.Format = ImageMagick.MagickFormat.Png;

            using var ms = new MemoryStream();
            image.Write(ms);
            var pngBytes = ms.ToArray();

            // Get text
            result.Text = await RecognizeText(pngBytes, language);

            // Get word-level results with confidence
            result.WordResults = await RecognizeTextRegions(pngBytes, language);
            result.MeanConfidence = result.WordResults.Count > 0
                ? result.WordResults.Average(w => w.Confidence)
                : 0f;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Detailed OCR failed for page {PageIndex}", pageIndex);
            result.Text = $"[OCR Error: {ex.Message}]";
        }

        return result;
    }

    /// <summary>
    /// Performs OCR on all pages of a PDF with detailed per-page confidence scores.
    /// </summary>
    public async Task<List<PageOcrResult>> OcrAllPagesDetailed(byte[] pdfBytes,
        string language = "eng", int dpi = 300,
        IProgress<(int current, int total, float confidence)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pdfOps = new PdfOperations();
        int pageCount = pdfOps.GetPageCount(pdfBytes);
        var results = new List<PageOcrResult>();

        for (int i = 0; i < pageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageResult = await OcrPdfPageDetailed(pdfBytes, i, language, dpi);
            results.Add(pageResult);
            progress?.Report((i + 1, pageCount, pageResult.MeanConfidence));
        }

        return results;
    }

    /// <summary>
    /// Processes multiple PDF files with OCR in batch, returning results per file.
    /// </summary>
    public async Task<List<BatchOcrFileResult>> BatchOcrAsync(
        List<(byte[] pdfBytes, string fileName)> files,
        string language = "eng", int dpi = 300,
        IProgress<(int fileIndex, int totalFiles, int currentPage, int totalPages)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<BatchOcrFileResult>();
        Log.Info("Starting batch OCR for {Count} files, language: {Lang}, DPI: {Dpi}",
            files.Count, language, dpi);

        for (int fileIdx = 0; fileIdx < files.Count; fileIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (pdfBytes, fileName) = files[fileIdx];
            var result = new BatchOcrFileResult { FileName = fileName };

            try
            {
                var pdfOps = new PdfOperations();
                result.PageCount = pdfOps.GetPageCount(pdfBytes);
                var sb = new System.Text.StringBuilder();

                for (int pageIdx = 0; pageIdx < result.PageCount; pageIdx++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report((fileIdx + 1, files.Count, pageIdx + 1, result.PageCount));

                    if (pageIdx > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"--- Page {pageIdx + 1} ---");
                        sb.AppendLine();
                    }

                    var text = await OcrPdfPage(pdfBytes, pageIdx, language, dpi);
                    sb.Append(text);
                }

                result.Text = sb.ToString();
                result.Success = true;
                Log.Info("Batch OCR completed for {FileName}: {PageCount} pages", fileName, result.PageCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Log.Error(ex, "Batch OCR failed for {FileName}", fileName);
            }

            results.Add(result);
        }

        Log.Info("Batch OCR complete: {Success}/{Total} files processed",
            results.Count(r => r.Success), results.Count);
        return results;
    }

    private void EnsureEngine(string language)
    {
        if (_engine != null && _currentLanguage == language)
            return;

        _engine?.Dispose();
        _engine = null;

        if (!Directory.Exists(_tessDataPath))
        {
            Log.Error("TessData directory not found: {Path}", _tessDataPath);
            return;
        }

        try
        {
            _engine = new TesseractEngine(_tessDataPath, language, EngineMode.Default);
            _currentLanguage = language;
            Log.Info("Tesseract engine initialized for language: {Lang}", language);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize Tesseract engine for language: {Lang}", language);
            _engine = null;
        }
    }

    private static string FindTessDataPath()
    {
        // Search common tessdata locations
        var candidates = new[]
        {
            // App-relative
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "tessdata"),
            // Environment variable
            Environment.GetEnvironmentVariable("TESSDATA_PREFIX") ?? "",
            // Common Windows locations
            @"C:\Program Files\Tesseract-OCR\tessdata",
            @"C:\Program Files (x86)\Tesseract-OCR\tessdata",
            // Common Linux locations
            "/usr/share/tesseract-ocr/4.00/tessdata",
            "/usr/share/tesseract-ocr/5/tessdata",
            "/usr/share/tessdata",
            // macOS Homebrew
            "/usr/local/share/tessdata",
            "/opt/homebrew/share/tessdata",
            // User profile
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PDF Editor", "tessdata"),
        };

        foreach (var path in candidates)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*.traineddata");
                if (files.Length > 0)
                    return path;
            }
        }

        // Default fallback
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _engine?.Dispose();
            _engine = null;
            _disposed = true;
        }
    }
}
