# PDF Editor - Architecture & Implementation Guide

## System Architecture

The PDF Editor is structured using a **layered architecture** with clear separation of concerns:

```
┌─────────────────────────────────────────────┐
│         UI Layer (Avalonia/MVVM)            │
│     (PDFEditor.UI Project)                  │
│  - MainWindow (XAML)                        │
│  - ViewModels (ReactiveUI)                  │
│  - Commands & Events                        │
└─────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────┐
│  Core Services Layer (Business Logic)       │
│     (PDFEditor.Core Project)                │
│  - IPdfDocument                             │
│  - IImageProcessor                          │
│  - IOcrEngine                               │
│  - ITextPdfService (Impl)                   │
└─────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────┐
│  Integration Layer (External Tools)         │
│  - ClawPDFIntegration                       │
│  - Ghostscript Bridge                       │
│  - Tesseract Bridge                         │
└─────────────────────────────────────────────┘
```

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

Using **xUnit** + **Moq** for unit tests:

```csharp
// PDFEditor.Tests/PdfDocumentTests.cs
public class PdfDocumentTests
{
    [Fact]
    public void LoadFromFile_ValidPath_LoadsDocument()
    {
        // Arrange
        var pdfService = new ITextPdfService();
        var testPdfPath = "samples/test.pdf";
        
        // Act
        pdfService.LoadFromFile(testPdfPath);
        
        // Assert
        Assert.True(pdfService.PageCount > 0);
    }
}
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

