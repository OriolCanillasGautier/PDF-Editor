# PDF Editor - Architecture & Implementation Guide

## System Architecture

The PDF Editor is structured using a **layered architecture** with clear separation of concerns:

```
┌─────────────────────────────────────────────┐
│         UI Layer (Avalonia/MVVM)            │
│     (PDFEditor.UI Project)                  │
│  - MainWindow (XAML + code-behind)          │
│  - ViewModels (ReactiveUI)                  │
│  - Commands & Events                        │
└─────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────┐
│  Core Services Layer (Business Logic)       │
│     (PDFEditor.Core Project)                │
│  - IPdfDocument / ITextPdfService           │
│  - IOcrEngine / TesseractOcrService         │
│  - IExportProvider / ExportProviderRegistry │
│  - PdfOperations, PdfRenderService          │
│  - PdfSearchService, PdfSplitService        │
│  - PdfSecurityService, PdfCropService       │
│  - PdfWatermarkService, PdfAnnotationService│
│  - PdfExportService, PdfBatchService        │
│  - SessionService, UndoRedoManager          │
└─────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────┐
│  Integration Layer (External Tools)         │
│  - ClawPDFIntegration (stub)                │
│  - Tesseract.NET (OCR)                      │
│  - Docnet.Core / PDFium (rendering)         │
│  - Magick.NET (image processing)            │
│  - DocumentFormat.OpenXml (DOCX export)     │
└─────────────────────────────────────────────┘
```

## Core Services Reference

| Service | Purpose | Library |
|---------|---------|---------|
| `ITextPdfService` | Document load/save, IPdfDocument impl | iText7 |
| `PdfOperations` | Page manipulation (rotate, delete, merge, metadata) | iText7 |
| `PdfRenderService` | PDF → BGRA pixel rendering | Docnet.Core (PDFium) |
| `PdfExportService` | Export to images, text, HTML, images-to-PDF | Magick.NET + Docnet |
| `PdfSearchService` | Full-text search with context, text extraction | iText7 |
| `PdfSplitService` | Split, extract, reorder, move, insert pages | iText7 |
| `PdfSecurityService` | AES-256 encryption, password protection | iText7 |
| `PdfCropService` | Crop, margins, resize to standard sizes | iText7 |
| `PdfWatermarkService` | Text watermarks, headers, footers, page numbers | iText7 |
| `PdfAnnotationService` | Burn annotations into PDF (10 types) | iText7 + Magick.NET |
| `PdfBatchService` | Batch operations on multiple files | All above |
| `TesseractOcrService` | OCR text recognition with multi-language | Tesseract.NET 5.2.0 |
| `SessionService` | User session persistence (JSON) | Newtonsoft.Json |
| `UndoRedoManager` | Undo/redo stack with state snapshots | Custom |
| `ExportProviderRegistry` | Registry for pluggable export format providers | Custom |

## Export Provider System

The export system uses a provider-based architecture for extensibility:

```
IExportProvider (interface)
├── ImageExportProvider    → PNG, JPEG, TIFF, BMP, WebP
├── TextExportProvider     → TXT
├── HtmlExportProvider     → HTML (visual with base64 images)
└── DocxExportProvider     → DOCX (Microsoft Word)
```

**Adding a new export format:**
1. Create a class implementing `IExportProvider` in `Core/Services/Export/`
2. Register it in `ExportProviderRegistry.CreateDefault()`
3. It automatically appears in the Export Dialog UI

## Core Interfaces

### 1. IPdfDocument
Main interface for PDF document operations:

```csharp
public interface IPdfDocument
{
    // Properties
    string? FilePath { get; }
    int PageCount { get; }
    string? Title { get; set; }
    string? Author { get; set; }
    Dictionary<string, object> Metadata { get; }
    
    // Methods
    void LoadFromFile(string filePath);
    void SaveToFile(string outputPath);
    void AddPages(params IPdfPage[] pages);
    void RemovePages(params int[] pageNumbers);
    void MovePages(int[] pageNumbers, int targetPosition);
    IPdfPage? GetPage(int pageNumber);
    List<IPdfPage> GetPages(int startPage, int endPage);
    void Merge(IPdfDocument other);
    void RotatePages(int[] pageNumbers, int degrees);
}
```

**Current Implementation:** `ITextPdfService` (uses iText7)

### 2. IPdfPage
Interface for individual PDF pages:

```csharp
public interface IPdfPage
{
    int PageNumber { get; }
    double Width { get; }
    double Height { get; }
    bool HasTextLayer { get; }
    string? ExtractedText { get; }
    
    void ExtractText();
    byte[] RenderToImage(float dpi = 300f);
    void RotatePage(int degrees);
}
```

### 3. IOcrEngine
Optical Character Recognition:

```csharp
public interface IOcrEngine
{
    Task<string> RecognizeText(byte[] imageData, string language = "eng");
    Task<List<OcrResult>> RecognizeTextRegions(byte[] imageData, string language = "eng");
    List<string> GetSupportedLanguages();
}
```

**Current Implementation:** `TesseractOcrService`
- Uses Tesseract.NET 5.2.0 for OCR
- Auto-discovers tessdata directory from common locations or `TESSDATA_PREFIX` env var
- Supports per-page PDF OCR (`OcrPdfPage`) and full-document OCR (`OcrEntirePdf`)
- Configurable DPI for render quality (default: 300)
- Progress reporting for multi-page operations
- Lazy engine initialization with language switching

### 4. IImageProcessor
Image manipulation:

```csharp
public interface IImageProcessor
{
    byte[] ConvertPdfPageToImage(byte[] pdfPageData, string format = "PNG", float dpi = 300f);
    byte[] ResizeImage(byte[] imageData, int width, int height);
    byte[] ConvertImageFormat(byte[] imageData, string targetFormat);
    byte[] ApplyOcr(byte[] imageData);
}
```

## Dependency Injection Setup

The project uses Microsoft.Extensions.DependencyInjection for loose coupling:

```csharp
// In App.xaml.cs or Program.cs
var services = new ServiceCollection();
services.AddPDFEditorCore();  // Registers Core services
services.AddUI();              // Registers UI services
var provider = services.BuildServiceProvider();
```

## Data Flow Example: Opening a PDF

1. **User Action** → MainWindow → "File > Open"
2. **ViewModel** → Calls `IPdfDocument.LoadFromFile(path)`
3. **Core Service** (ITextPdfService) → Loads PDF using iText7
4. **UI Update** → Bind to PageCount, Title, Author
5. **Display** → Render first page thumbnail

## Adding New Features

### Example: Implement Image Processing

#### Step 1: Create Service Implementation

```csharp
// PDFEditor.Core/Services/ImageProcessorService.cs
public class ImageProcessorService : IImageProcessor
{
    private readonly ILogger _logger;
    
    public ImageProcessorService()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }
    
    public byte[] ConvertPdfPageToImage(byte[] pdfPageData, string format = "PNG", float dpi = 300f)
    {
        // Implementation using Magick.NET or Pdfium.Net
    }
}
```

#### Step 2: Register in DI Container

```csharp
// CoreServiceCollectionExtensions.cs
services.AddSingleton<IImageProcessor, ImageProcessorService>();
```

#### Step 3: Use in ViewModel

```csharp
public class MainViewModel : ViewModelBase
{
    private readonly IImageProcessor _imageProcessor;
    
    public MainViewModel(IImageProcessor imageProcessor)
    {
        _imageProcessor = imageProcessor;
    }
    
    public async Task ConvertToImage()
    {
        var imageBytes = _imageProcessor.ConvertPdfPageToImage(...);
    }
}
```

## Testing Strategy

Using **xUnit** + **Moq** for unit tests. **97 tests** across 9 test files.

### Test Structure
```
src/PDFEditor.Tests/
├── Helpers/
│   └── TestPdfGenerator.cs        # In-memory PDF generation for tests
└── Core/
    ├── PdfOperationsTests.cs       # Page count, delete, rotate, merge, text, metadata
    ├── PdfSearchServiceTests.cs    # Search, case sensitivity, multi-page, count, extract
    ├── PdfSplitServiceTests.cs     # Extract, split all, move, insert, reorder
    ├── PdfSecurityServiceTests.cs  # Encrypt, decrypt, is-encrypted, try-open
    ├── PdfCropServiceTests.cs      # Crop, margins, resize, standard sizes
    ├── PdfWatermarkServiceTests.cs # Watermark, header/footer, page numbers
    ├── PdfAnnotationServiceTests.cs# Burn annotations, clone, out-of-range
    ├── PdfExportServiceTests.cs    # Image export, text, HTML, images-to-PDF
    ├── UndoRedoManagerTests.cs     # Undo, redo, clear, history, multi-step
    └── ExportProviderRegistryTests.cs # Registry, providers, async export
```

### TestPdfGenerator
Helper class that generates test PDFs in-memory using iText7:
```csharp
TestPdfGenerator.CreateSimplePdf(pageCount);     // N pages with sample text
TestPdfGenerator.CreatePdfWithContent("text");    // Specific content per page
TestPdfGenerator.CreatePdfWithMetadata(t, a, s);  // With title/author/subject
TestPdfGenerator.CreateMinimalPdf();               // Single page, "Hello World"
```

## Build & Compile Targets

### Development Build
```powershell
dotnet build --configuration Debug
```

### Production Build
```powershell
dotnet build --configuration Release
```

### Publish as Standalone
```powershell
dotnet publish -c Release -o publish/
```

This creates a self-contained executable that doesn't require .NET Runtime installed.

## Performance Considerations

1. **Large PDFs:** Implement lazy-loading for pages
2. **OCR:** Run asynchronously to avoid UI blocking
3. **Image Processing:** Consider parallel processing for batch operations
4. **Memory:** Unload pages not currently displayed
5. **Caching:** Cache rendered thumbnails

## Security Considerations

1. **Encrypted PDFs:** Handle password-protected PDFs gracefully
2. **Malicious PDFs:** Validate PDF structure before processing
3. **Temporary Files:** Clean up sensitive temp files after processing
4. **Data Privacy:** Log sensitive data (OCR results, metadata) carefully

## Logging Configuration

NLog is configured for structured logging:

```csharp
// Key log levels
Logger.Info("User opened file: {0}", filePath);
Logger.Warn("Large PDF detected: {0} pages", pageCount);
Logger.Error(ex, "Failed to process PDF");
```

Logs are written to:
- Console (Debug mode)
- File: `%APPDATA%/PDF Editor/logs/`

