# Copilot Instructions for PDF Editor

**Version:** 1.3  
**Last Updated:** 2026-02-18

---

## CRITICAL: Documentation Maintenance

### Rule 1: Always Update Documentation

**Every single task must update the relevant .md files.** This is not optional.

| When You... | You Must... |
|-------------|-------------|
| Complete a task | Update `PLAN.md` with completion status |
| Fix a bug | Document the bug and fix in relevant .md |
| Make a design change | Update `docs/ARCHITECTURE.md` immediately |
| Discover an issue | Add to Active Issues section in `PLAN.md` |
| Change a dependency | Update `README.md` and `SETUP.md` |
| Add a feature | Update feature documentation in `docs/` |

### Rule 2: Track Changes Made

At the end of EVERY work session, update this section in `PLAN.md`:

```markdown
## Change Log

| Date | Phase/Step | Changes Made | Files Updated |
|------|------------|--------------|---------------|
| 2026-02-18 | Phase 4 | Updated copilot-instructions.md | .github/copilot-instructions.md |
```

### Rule 3: Track Errors and Blockers

Maintain an active issues section in `PLAN.md`:

```markdown
## Active Issues

| ID | Priority | Issue | Impact | Status | Owner |
|----|----------|-------|--------|--------|-------|
| ERR-001 | P2 | ClawPDF wrapper not implemented | No print-to-PDF via virtual printer | Open | - |
| ERR-002 | P3 | Tesseract requires tessdata files | Users must manually download language packs | Open | - |
```

**Update this table:**
- When new errors are discovered
- When error status changes
- When errors are resolved (move to Resolved Issues)

### Rule 4: Track Decisions Made

Document all architectural and design decisions:

```markdown
## Decision Log

| Date | Decision | Rationale | Alternatives Considered |
|------|----------|-----------|------------------------|
| 2026-02-18 | Hybrid DOCX export | Best of both worlds: iText7 fallback + pdf2docx high-fidelity | Pure Python (deployment complexity), Pure iText7 (format issues) |
| 2026-02-18 | Ribbon UI with UXWing SVG icons | Familiar Office-like UX, scalable icons | Keep current toolbar (overloaded), Custom icons (time-consuming) |
| 2026-02-18 | SkiaSharp over Pdfium.Net | No native DLLs, GPU acceleration, built into Avalonia | Keep Pdfium (external dependency), Custom renderer (too complex) |
```

### Rule 5: Phase Status Must Be Accurate

Update phase status in real-time in `PLAN.md`:

```markdown
## Phase Status

| Phase | Status | Started | Completed | Progress |
|-------|--------|---------|-----------|----------|
| Phase 1: Foundation | Complete | 2026-02-01 | 2026-02-14 | 100% |
| Phase 2: Page Operations | Complete | 2026-02-15 | 2026-02-17 | 100% |
| Phase 3: Forms & Signatures | Complete | 2026-02-17 | 2026-02-17 | 100% |
| Phase 4: Advanced Features | In Progress | 2026-02-18 | - | 85% |
| Phase 5: Export Expansion | Not Started | - | - | 0% |
| Phase 6-12: Optimization & Polish | Planned | - | - | 0% |
```

**Update when:**
- Starting a new phase
- Completing a phase
- Significant progress milestones

---

## Project Overview

This is a **cross-platform PDF editor** built with C#, .NET 6+, and Avalonia UI. It integrates multiple PDF libraries (iText7, PDFSharp, Pdfium/Docnet, SkiaSharp) to provide comprehensive PDF manipulation, viewing, annotation, and export capabilities.

**Key Goal:** Production-grade, open-source PDF editor with features comparable to Adobe Acrobat, licensed under AGPL v3.

**Quality Standard:** Clean architecture, modular design, comprehensive testing (282+ passing tests), cross-platform compatibility (Windows, Linux, macOS).

**Current State:** Feature-complete core editor with forms, signatures, OCR, annotations, comparison, redaction, and hybrid DOCX export. Ribbon UI and performance optimizations planned.

---

## Documentation Structure

| File | Purpose | Update Frequency |
|------|---------|------------------|
| `README.md` | Project overview, quick start, features | When adding major features |
| `SETUP.md` | Developer setup, troubleshooting | When changing dependencies |
| `PLAN.md` | Master roadmap, phase tracking, decision log | Every work session |
| `docs/ARCHITECTURE.md` | System design, interfaces, data flow | When changing architecture |
| `docs/ROADMAP.md` | Step-by-step implementation guide | When updating priorities |
| `CONTRIBUTING.md` | Contribution guidelines | When onboarding contributors |
| `CHANGELOG.md` | Version history | Every release |
| `.github/copilot-instructions.md` | This file - AI assistant guidelines | When improving workflows |

**NEVER work without updating documentation first.**

---

## Architecture Essentials

### Layered Architecture

```
┌─────────────────────────────────────────────┐
│         UI Layer (Avalonia/MVVM)            │
│     (PDFEditor.UI Project)                  │
│  - MainWindow.axaml (Ribbon toolbar)        │
│  - ViewModels (ReactiveUI)                  │
│  - Commands & Events                        │
└─────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────┐
│  Core Services Layer (Business Logic)       │
│     (PDFEditor.Core Project)                │
│  - IPdfDocument, IExportProvider            │
│  - IFormService, ISignatureService          │
│  - IOcrEngine, IRedactionService            │
│  - Service Implementations (20+ services)   │
└─────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────┐
│  Integration Layer (External Tools)         │
│  - HybridDocxExportProvider (pdf2docx)      │
│  - TesseractOcrService                      │
│  - SkiaSharp Rendering (planned)            │
└─────────────────────────────────────────────┘
```

### Project Structure

```
PDF-Editor/
├── src/
│   ├── PDFEditor.Core/              # Core business logic, services, abstractions
│   │   ├── Abstractions/            # Interfaces (IPdfDocument, IExportProvider, etc.)
│   │   ├── Services/                # Implementations (20+ services)
│   │   │   ├── Export/              # Export providers (Docx, Xlsx, Html, etc.)
│   │   │   └── ...                  # All service implementations
│   │   └── AppConfig.cs             # Global configuration
│   │
│   ├── PDFEditor.UI/                # Avalonia desktop application
│   │   ├── App.axaml                # Application entry point
│   │   ├── MainWindow.axaml         # Main window UI (Ribbon toolbar)
│   │   ├── ViewModels/              # MVVM ViewModels
│   │   └── Resources/Icons/         # UXWing SVG icons
│   │
│   ├── PDFEditor.ClawPDFIntegration/# Bridge to clawPDF printer
│   │   └── ClawPDFWrapper.cs        # Wrapper for clawPDF.exe
│   │
│   └── PDFEditor.Tests/             # Unit tests (xUnit, 282+ passing)
│
├── docs/                            # Documentation
├── .github/                         # CI/CD, copilot instructions
├── PLAN.md                          # Master roadmap
└── PDFEditor.sln                    # Visual Studio solution
```

### Dependency Injection Pattern

Services registered via extension method in `CoreServiceCollectionExtensions.cs`:

```csharp
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddPDFEditorCore(this IServiceCollection services)
    {
        services.AddSingleton<IPdfDocument, ITextPdfService>();
        services.AddSingleton<IExportProvider, HybridDocxExportProvider>();
        services.AddSingleton<IFormService, PdfFormService>();
        services.AddSingleton<ISignatureService, PdfSignatureService>();
        services.AddSingleton<IOcrEngine, TesseractOcrService>();
        services.AddSingleton<IRedactionService, PdfRedactionService>();
        services.AddSingleton<IComparisonService, PdfComparisonService>();
        // ... all other services
        return services;
    }
}
```

**When adding new services:**
1. Create interface in `PDFEditor.Core/Abstractions/`
2. Implement in `PDFEditor.Core/Services/`
3. Register in `CoreServiceCollectionExtensions.AddPDFEditorCore()`
4. Inject into ViewModel via constructor

### MVVM with ReactiveUI

**ViewModels** (`PDFEditor.UI/ViewModels/`):

| ViewModel | Purpose |
|-----------|---------|
| `MainViewModel` | App-level state: tabs, recent files, theme |
| `DocumentTabViewModel` | Per-document state: pages, annotations, operations |

**Key Patterns:**
- Use `ReactiveObject` as base class
- Use `RaiseAndSetIfChanged(ref field, value)` for properties
- Use `ReactiveCommand.Create(() => { })` for commands
- Commands exposed as `ReactiveCommand<Unit, Unit> PropertyName { get; }`

**Example:**
```csharp
public class MainViewModel : ReactiveObject
{
    private bool _isDarkTheme;
    
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
    }
    
    public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
    
    public MainViewModel()
    {
        ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
    }
    
    private void ToggleTheme() => IsDarkTheme = !IsDarkTheme;
}
```

### Service Layer Pattern

All PDF operations in `PDFEditor.Core/Services/`:

| Service | Purpose | Library | Status |
|---------|---------|---------|--------|
| `ITextPdfService` | Document manipulation (load, save, rotate, remove) | iText7 | ✅ Complete |
| `PdfRenderService` | PDF → image rendering | Docnet.Core | ✅ Complete |
| `PdfExportService` | Export to images/HTML/text | Magick.NET | ✅ Complete |
| `PdfAnnotationService` | Annotation rendering/burning | iText7 | ✅ Complete |
| `PdfSearchService` | Text extraction & search | iText7 | ✅ Complete |
| `PdfSecurityService` | Password protection, encryption | iText7 | ✅ Complete |
| `PdfFormService` | Form field detection, filling, creation | iText7 AcroForm | ✅ Complete |
| `PdfSignatureService` | Digital signatures (PKCS#12) | iText7 + BouncyCastle | ✅ Complete |
| `PdfRedactionService` | Content redaction (area, text, page) | iText7 | ✅ Complete |
| `PdfComparisonService` | Document comparison (LCS diff) | Custom | ✅ Complete |
| `TesseractOcrService` | OCR text recognition | Tesseract.NET | ✅ Complete |
| `SearchablePdfService` | OCR overlay for scanned PDFs | Tesseract + iText7 | ✅ Complete |
| `MeasurementService` | Ruler, area, perimeter tools | Custom (Shoelace) | ✅ Complete |
| `FormValidationService` | Form field validation rules | Custom (13 rule types) | ✅ Complete |
| `VisualDiffService` | Pixel-level page comparison | Magick.NET | ✅ Complete |
| `XfdfAnnotationService` | XFDF annotation import/export | Custom | ✅ Complete |
| `AnnotationExportService` | Annotation reports (text/HTML/CSV) | Custom | ✅ Complete |
| `SessionService` | User session persistence | JSON | ✅ Complete |
| `UndoRedoManager` | Command undo/redo stack | Custom | ✅ Complete |
| `HybridDocxExportProvider` | DOCX export (iText7 + pdf2docx) | iText7 + Python | ✅ Complete |
| `XlsxExportProvider` | Excel export with table detection | DocumentFormat.OpenXml | ✅ Complete |
| `RtfExportProvider` | RTF format export | Custom | ✅ Complete |
| `HtmlExportProvider` | HTML export with embedded images | iText7 | ✅ Complete |
| `PdfOptimizer` | PDF compression, optimization | Planned | 🔲 Phase 12 |

---

## Ribbon UI Architecture

### Tabbed Toolbar Structure

The application uses a **9-tab ribbon interface** (Excel/Word-style) adapted for PDF workflows:

```
[File] [Home] [Edit] [Insert] [Draw] [Form] [Review] [View] [Help]
```

### Ribbon Tabs Definition

| Tab | Purpose | Key Groups |
|-----|---------|------------|
| **File** | Document lifecycle | New, Open, Save, Export, Print, Properties, Close |
| **Home** | Common actions | Undo/Redo, Select All, Copy/Paste, Zoom, Page Nav, Search |
| **Edit** | Page/content editing | Delete, Rotate, Crop, Extract, Merge, Split, Reorder |
| **Insert** | Add new content | Page, Image, Text Box, Signature, Barcode, Header/Footer |
| **Draw** | Annotations & markup | Shapes, Freehand, Highlight, Underline, Note, Stamp, Measurement |
| **Form** | Form handling | Detect, Fill, Add Field (Text/Checkbox/Dropdown), Flatten, Validate |
| **Review** | Quality & security | OCR, Compare, Sign, Verify, Redact, Accessibility, Optimize |
| **View** | Display options | Thumbnails, Bookmarks, Layers, Dark Mode, Full Screen, Fit Width/Height |
| **Help** | Support | About, Keyboard Shortcuts, Documentation, Feedback |

### Icon Strategy (UXWing)

**Source**: https://uxwing.com/

**License**: All icons **free for personal and commercial use** (per UXWing FAQ)

**Available Formats**:
- **Scalable Vector SVG** (recommended for large projects)
- **Transparent background PNG** (fallback option)

**Which Format to Use** (per UXWing):
> "If you have a large project that needs a lot of icons, we suggest you go with **SVG Icons**, because PNG files would increase your overall file size. SVG is a vector format that works well in high-resolution retina display. Even SVG file size is low compared to PNG."

**Implementation Guidelines**:
- **Format**: **SVG** for all toolbar icons
- **Style**: Monochrome, theme-aware (auto-adapts to light/dark)
- **Size**: Design on 24x24 or 32x32 pixel grid for consistency
- **No emojis**: Professional iconography only
- **Labels**: Text labels shown by default; icons-only mode optional
- **Storage**: `src/PDFEditor.UI/Resources/Icons/` folder
- **Coloring**: Theme-aware via Avalonia styles

**Icon List Needed** (approx 60 icons):
```
File: new-document, folder-open, save, export, print, properties, close
Home: undo, redo, select-all, copy, paste, zoom-in, zoom-out, search
Edit: delete, rotate-left, rotate-right, crop, extract, merge, split, move-up, move-down
Insert: image, text-box, signature, barcode, header-footer, page-number, watermark
Draw: highlight, underline, strikethrough, rectangle, ellipse, line, arrow, freehand, note, stamp, ruler, measure
Form: form-detect, form-fill, form-add, form-edit, form-flatten, form-validate
Review: ocr, compare, sign, verify, redact, accessibility, optimize, security
View: thumbnails, bookmarks, layers, fullscreen, fit-width, fit-height, dark-mode
Help: info, keyboard, book, feedback
```

---

## Hybrid DOCX Export Workflow

### Architecture

```
HybridDocxExportProvider
├── Detect: Is Python + pdf2docx available?
│   ├── YES + UseHighFidelityEngine=true → Use pdf2docx (high fidelity)
│   └── NO or false → Fallback to DocxExportProvider (iText7, good fidelity)
└── Always returns valid DOCX (never fails due to optional backend)
```

### Format Preservation Comparison

| Element | iText7 Engine | pdf2docx (Python) |
|---------|--------------|-------------------|
| Text formatting | ✅ Good | ✅ Excellent |
| Font detection | ⚠️ Approximate | ✅ Precise |
| Paragraph layout | ⚠️ Basic | ✅ Advanced (columns, spacing) |
| Tables | ⚠️ Heuristic detection | ✅ Structure-aware |
| Merged cells | ⚠️ Limited | ✅ Full support |
| Images | ✅ Embedded | ✅ Embedded + positioning |
| Hyperlinks | ✅ Preserved | ✅ Preserved |
| Headers/Footers | ❌ Not supported | ⚠️ Partial (TODO in pdf2docx) |
| Complex layouts | ⚠️ May reflow | ✅ Better preservation |

### Requirements for pdf2docx Backend
- Python 3.8+ installed and in PATH
- `pdf2docx` package installed via pip: `pip install pdf2docx`
- AGPL-3.0 license compliance (or commercial license from Artifex)

### UI Integration
In Export Dialog:
```
☑ Use high-fidelity engine (requires Python + pdf2docx)
  ⓘ Better layout preservation for complex documents
  ⓘ Install pdf2docx: pip install pdf2docx
  ⓘ License: AGPL-3.0
```

---

## Technology Stack

### Core Libraries

| Library | Package | Version | Purpose | License |
|---------|---------|---------|---------|---------|
| Avalonia | `Avalonia` | 11.0.0+ | Cross-platform UI framework | MIT |
| ReactiveUI | `Avalonia.ReactiveUI` | 11.0.0+ | MVVM framework | MIT |
| iText7 | `itext7` | 7.2.5 | PDF manipulation | AGPL v3 |
| Docnet | `Docnet.Core` | 2.6.0 | PDF rendering (Pdfium) | Apache 2.0 |
| Magick.NET | `Magick.NET-Q16-AnyCPU` | 14.10.2+ | Image processing | Apache 2.0 |
| Tesseract | `Tesseract` | 5.2.0 | OCR engine | Apache 2.0 |
| NLog | `NLog` | 5.2.8 | Logging | BSD |
| xUnit | `xunit` | 2.6.6 | Testing framework | Apache 2.0 |
| Moq | `Moq` | 4.20.72 | Mocking library | BSD |
| DocumentFormat.OpenXml | `DocumentFormat.OpenXml` | 3.0.1+ | DOCX/XLSX export | MIT |

### Planned Additions

| Library | Purpose | License | Phase |
|---------|---------|---------|-------|
| **SkiaSharp** | Replace Pdfium.Net for rendering | MIT | Phase 11 |
| **QPdfSharp** | Linearization, advanced compression | Apache 2.0 | Phase 12 |
| **QuestPDF** | Template-based PDF generation | MIT (< $1M) | Backlog |

---

## Critical Workflows

### Build & Run Commands

```bash
# Restore NuGet packages
dotnet restore

# Build all projects
dotnet build

# Run tests (282+ passing)
dotnet test

# Run application
dotnet run --project src/PDFEditor.UI/PDFEditor.UI.csproj
```

**Visual Studio:**
1. Open `PDFEditor.sln`
2. Set `PDFEditor.UI` as startup project
3. Press F5 to debug

### Local Development Setup

```powershell
# Clone repository
git clone https://github.com/OriolCanillasGautier/PDF-Editor.git
cd PDF-Editor

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run
cd src/PDFEditor.UI
dotnet run
```

### Testing Workflow

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --logger:trx --verbosity normal

# Run specific test class
dotnet test --filter "FullyQualifiedName~PdfDocumentTests"

# Check code coverage (requires coverlet)
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

### Publishing

```bash
# Windows x64 standalone
dotnet publish src/PDFEditor.UI/PDFEditor.UI.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  --output ./publish/win-x64

# Linux x64 standalone
dotnet publish src/PDFEditor.UI/PDFEditor.UI.csproj `
  --configuration Release `
  --runtime linux-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  --output ./publish/linux-x64
```

---

## Key Files & Entry Points

| File | Purpose |
|------|---------|
| `PDFEditor.sln` | Visual Studio solution file |
| `src/PDFEditor.UI/Program.cs` | Application entry point |
| `src/PDFEditor.UI/App.axaml` | Application resources, styles |
| `src/PDFEditor.UI/MainWindow.axaml` | Main window UI definition (Ribbon) |
| `src/PDFEditor.UI/MainWindow.axaml.cs` | Main window code-behind (event handlers) |
| `src/PDFEditor.UI/ViewModels/MainViewModel.cs` | App-level ViewModel |
| `src/PDFEditor.UI/ViewModels/DocumentTabViewModel.cs` | Per-document ViewModel |
| `src/PDFEditor.Core/CoreServiceCollectionExtensions.cs` | DI registration |
| `src/PDFEditor.Core/AppConfig.cs` | Global constants |
| `src/PDFEditor.Core/Abstractions/` | All service interfaces |
| `src/PDFEditor.Core/Services/Export/` | Export providers (6+ formats) |
| `src/PDFEditor.Core/Services/HybridDocxExportProvider.cs` | Hybrid DOCX export |
| `src/PDFEditor.UI/Resources/Icons/` | UXWing SVG icons |
| `src/PDFEditor.UI/nlog.config` | NLog logging configuration |
| `README.md` | Project overview |
| `SETUP.md` | Developer setup guide |
| `PLAN.md` | Implementation roadmap (master doc) |
| `docs/ARCHITECTURE.md` | Architecture documentation |
| `.github/copilot-instructions.md` | This file - AI assistant guidelines |

---

## Code Patterns & Conventions

### C# Language Features

- **Target:** .NET 6.0+, C# 10+
- **Nullable reference types:** Enabled (`<Nullable>enable</Nullable>`)
- **Implicit usings:** Enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
- **Pattern matching:** Prefer `is` expressions over `as` casts
- **Record types:** Use for DTOs, configuration objects

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Public classes | PascalCase | `PdfDocument`, `MainViewModel` |
| Public methods | PascalCase | `LoadFromFile`, `SavePdf` |
| Public properties | PascalCase | `PageCount`, `FilePath` |
| Private fields | `_camelCase` | `_filePath`, `_pdfDocument` |
| Interfaces | `I` prefix + PascalCase | `IPdfDocument`, `IExportProvider` |
| Private readonly | `_camelCase` + type hint | `_logger`, `_service` |

### Logging Pattern

Use **NLog** with `LogManager.GetCurrentClassLogger()`:

```csharp
private static readonly Logger Log = LogManager.GetCurrentClassLogger();

public void LoadFromFile(string filePath)
{
    Log.Info("Loading PDF: {FilePath}", filePath);
    
    if (!File.Exists(filePath))
    {
        Log.Warn("File not found: {FilePath}", filePath);
        throw new FileNotFoundException($"PDF file not found: {filePath}");
    }
    
    try
    {
        // ... load logic
        Log.Info("PDF loaded successfully: {PageCount} pages", PageCount);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to load PDF: {FilePath}", filePath);
        throw;
    }
}
```

**Log levels:**
- `Trace` - Detailed debugging
- `Debug` - Diagnostic info
- `Info` - Normal operations
- `Warn` - Recoverable issues
- `Error` - Failures
- `Fatal` - Critical errors

### Async/Await Pattern

```csharp
// Good: Async method with CancellationToken
public async Task<byte[]> ExportPageToImageAsync(
    int pageIndex, 
    string format = "PNG", 
    int dpi = 150,
    CancellationToken cancellationToken = default)
{
    await Task.Run(() =>
    {
        // CPU-bound work
    }, cancellationToken);
    
    return imageBytes;
}

// Good: Fire-and-forget with error handling
private async void OnSaveClick(object? sender, RoutedEventArgs e)
{
    try
    {
        await SaveAsync();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Save failed");
        // Show error to user
    }
}
```

### Command Pattern (ReactiveUI)

```csharp
public class DocumentTabViewModel : ReactiveObject
{
    private readonly IPdfDocument _pdfDocument;
    
    // Commands
    public ReactiveCommand<Unit, Unit> RotateRightCommand { get; }
    public ReactiveCommand<Unit, Unit> DeletePageCommand { get; }
    public ReactiveCommand<string, ExportResult> ExportCommand { get; }
    
    public DocumentTabViewModel(IPdfDocument pdfDocument)
    {
        _pdfDocument = pdfDocument;
        
        RotateRightCommand = ReactiveCommand.Create(() => 
            RotatePages(90));
        
        DeletePageCommand = ReactiveCommand.Create(() => 
            RemovePage(CurrentPageIndex));
        
        ExportCommand = ReactiveCommand.CreateFromTask<string>(
            async format => await ExportAsync(format));
    }
}
```

### Error Handling Pattern

```csharp
public void ExecuteTabCommand(Action action)
{
    try 
    { 
        action(); 
    }
    catch (Exception ex) 
    { 
        Log.Error(ex, "Error executing tab command");
        StatusText = $"Error: {ex.Message}";
    }
}

// Usage in MainWindow.axaml.cs
private void OnRotateClick(object? sender, RoutedEventArgs e)
{
    if (Tab?.CanRotate == true && !Tab.IsBusy)
        ExecuteTabCommand(() => Tab.RotateRightCommand.Execute().Subscribe());
}
```

---

## Development Standards

### Code Quality Guidelines

| Rule | Guideline | Enforcement |
|------|-----------|-------------|
| Function length | Max 60 lines | Warning |
| Nesting depth | Max 3 levels | Warning |
| Method parameters | Max 5 parameters | Warning |
| Class responsibility | Single responsibility | Warning |
| XML documentation | Required on public APIs | Warning |
| Line length | Max 120 characters | Warning |
| Test coverage | Target 80%+ | Phase 4+ |

### Code Review Checklist

Before submitting code:

- [ ] Follows naming conventions
- [ ] Includes XML documentation on public APIs
- [ ] Uses logging appropriately
- [ ] Handles errors gracefully
- [ ] Follows MVVM pattern (UI code)
- [ ] Uses dependency injection
- [ ] No hardcoded values (use `AppConfig`)
- [ ] Updates relevant documentation
- [ ] Adds tests for new functionality
- [ ] Builds without warnings

### Git Workflow

```bash
# Create feature branch
git checkout -b feature/your-feature-name

# Commit with conventional commits
git commit -m "feat: add PDF to image export

- Implement ExportPageToImage in PdfExportService
- Add export dialog UI
- Support PNG, JPEG, TIFF formats

Closes #123"

# Push and create PR
git push origin feature/your-feature-name
```

**Commit types:**
- `feat` - New feature
- `fix` - Bug fix
- `docs` - Documentation only
- `refactor` - Code refactoring
- `test` - Adding tests
- `chore` - Build/config changes

---

## Integration Points

### PDF Libraries

| Library | Purpose | License | Status |
|---------|---------|---------|--------|
| **iText7** | PDF manipulation (load, save, merge, split) | AGPL v3 | ✅ Active |
| **PDFSharp** | PDF creation from scratch | MIT | 🔲 Backup |
| **Docnet.Core** | PDF rendering to images (Pdfium wrapper) | Apache 2.0 | ✅ Active |
| **SkiaSharp** | Future rendering (built into Avalonia) | MIT | 🔲 Phase 11 |
| **Magick.NET** | Image format conversion | Apache 2.0 | ✅ Active |

### OCR Integration

```csharp
// Implemented interface
public interface IOcrEngine
{
    Task<string> OcrPdfPageAsync(byte[] pdfBytes, int pageIndex, string language = "eng", int dpi = 300);
    Task<string> OcrEntirePdfAsync(byte[] pdfBytes, string language = "eng", int dpi = 300, IProgress<(int, int)>? progress = null);
    List<string> GetSupportedLanguages();
    bool IsAvailable { get; }
}
```

**Implementation:** `TesseractOcrService` (✅ Complete)

**Libraries:**
- Tesseract.NET (Apache 2.0) - Implemented
- PaddleOCR (Apache 2.0) - Planned alternative

### pdf2docx Integration (Hybrid)

```csharp
// Hybrid provider with Python fallback
public class HybridDocxExportProvider : IExportProvider
{
    public async Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options, CancellationToken ct);
    public bool IsHighFidelityModeAvailable(); // Check if Python + pdf2docx available
    public static Task<bool> InstallPdf2DocxAsync(string? pythonPath = null); // Install helper
}
```

**Status:** ✅ Complete (Phase 10)

**License:** AGPL-3.0 (requires compliance or commercial license from Artifex)

### External Dependencies

See **Technology Stack** table above for complete list.

---

## Common Debugging

### Build & Runtime Issues

| Issue | Resolution |
|-------|------------|
| Avalonia designer not showing | Designer may not work in VS 2022; edit XAML manually. Use preview pane or run app to see changes |
| NuGet restore fails | Run `dotnet nuget locals all --clear`, then `dotnet restore`. Check network/proxy settings |
| `dotnet build` fails with missing references | Run `dotnet restore` first. Check all `.csproj` files have correct `<ProjectReference>` paths |
| Application crashes on startup | Check `nlog.config` is copied to output. Review logs in `%APPDATA%/PDF Editor/logs/` |
| Theme not switching | Ensure `Application.Current.RequestedThemeVariant` is set in both `App.axaml` and code-behind |
| Icons not loading | Verify SVG files in `Resources/Icons/`; check build action is `AvaloniaResource` |

### PDF Operations

| Issue | Resolution |
|-------|------------|
| PDF not rendering | Check Docnet initialization; verify PDF is not corrupted or password-protected |
| Blank pages after export | Verify `PdfRenderService` DPI settings (min 72); check page index is 0-based |
| "File in use" error on save | Ensure PDF is disposed before saving. Use `using` statements for streams |
| Text extraction returns empty | PDF may be scanned image (no text layer); use OCR instead |
| Rotate doesn't persist | Call `SaveToFile` after rotation; verify document is not read-only |
| Merge produces corrupt PDF | Ensure all source PDFs are valid; check iText7 version compatibility |
| DOCX won't open in Word | Check `[Content_Types].xml` structure; verify image formats (JPEG/PNG only) |

### UI & ViewModels

| Issue | Resolution |
|-------|------------|
| Binding not updating | Ensure property uses `RaiseAndSetIfChanged`; check `DataContext` is set |
| Command not executing | Verify `CanExecute` condition; check `Subscribe()` is called on command |
| Tab not closing properly | Call `CloseTab()` not `CloseActiveTab()` for specific tabs; dispose resources |
| Annotations not visible | Verify `AnnotationCanvas` is rendered; check Z-index; ensure `IsAnnotationMode` is true |
| Thumbnail list not updating | Ensure `ObservableCollection` is used; call `PropertyChanged` for collection changes |
| Keyboard shortcuts not working | Check `OnKeyDown` override in `MainWindow`; verify no other control has focus |
| Ribbon tabs not showing | Check `MainWindow.axaml` tab definitions; verify DataContext is set |

### Testing

| Issue | Resolution |
|-------|------------|
| Tests not discovered | Ensure test class is `public`, methods start with `Test` or have `[Fact]` attribute |
| Mock not behaving as expected | Verify `Setup()` is called before using mock; check interface matches |
| Async test hangs | Ensure test method is `async Task`; use `await`; avoid `.Result` or `.Wait()` |
| Test fails with file not found | Use absolute path or copy test files to output directory |
| Coverage not reported | Add `/p:CollectCoverage=true` flag; install `coverlet.msbuild` package |

### Dependencies & Integration

| Issue | Resolution |
|-------|------------|
| Tesseract OCR fails | Download language data files (`.traineddata`); set `TESSDATA_PREFIX` environment variable |
| pdf2docx not detected | Run `pip install pdf2docx`; verify Python in PATH; check `HybridDocxExportProvider.IsHighFidelityModeAvailable()` |
| Image export produces wrong colors | Magick.NET uses BGRA pixel order; verify `PixelMapping` in `ReadPixels` |
| Session not restoring | Check `%APPDATA%/PDF Editor/session.json` exists and is valid JSON |
| NLog not writing | Verify `nlog.config` has correct targets; check file permissions for log directory |

### Performance

| Issue | Resolution |
|-------|------------|
| App freezes on large PDF | Use async operations; implement lazy loading for pages > 100 |
| High memory usage | Dispose `PdfDocument` after use; clear thumbnail cache; limit undo stack size |
| Slow thumbnail rendering | Reduce DPI for thumbnails (72-96); cache rendered images |
| UI lag during export | Run export in background thread; use `Progress<T>` for progress updates |
| Slow startup | Lazy load optional features; async initialization; splash screen with progress |

**When you fix a bug, document it in the Change Log in `PLAN.md`.**

---

## Testing Requirements

### Test Structure

```
src/PDFEditor.Tests/
├── Core/
│   ├── PdfDocumentTests.cs
│   ├── PdfExportServiceTests.cs
│   ├── PdfFormServiceTests.cs
│   ├── PdfSignatureServiceTests.cs
│   ├── PdfRedactionServiceTests.cs
│   ├── PdfComparisonServiceTests.cs
│   ├── AnnotationExportServiceTests.cs
│   ├── XfdfAnnotationServiceTests.cs
│   ├── MeasurementServiceTests.cs
│   ├── FormValidationServiceTests.cs
│   ├── VisualDiffServiceTests.cs
│   └── SearchablePdfServiceTests.cs
├── ViewModels/
│   ├── MainViewModelTests.cs
│   └── DocumentTabViewModelTests.cs
└── Integration/
    └── EndToEndTests.cs
```

**Current Status:** 282+ passing tests across 21 test files

### Test Patterns

**Service Tests (xUnit):**
```csharp
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
        Assert.Equal(testPdfPath, pdfService.FilePath);
    }
    
    [Fact]
    public void LoadFromFile_MissingFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var pdfService = new ITextPdfService();
        
        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => 
            pdfService.LoadFromFile("nonexistent.pdf"));
    }
}
```

**ViewModel Tests (with Moq):**
```csharp
public class MainViewModelTests
{
    [Fact]
    public void OpenFile_AddsNewTab_Correctly()
    {
        // Arrange
        var vm = new MainViewModel();
        var testPdf = "samples/test.pdf";
        
        // Act
        vm.OpenFile(testPdf);
        
        // Assert
        Assert.Single(vm.Tabs);
        Assert.NotNull(vm.ActiveTab);
        Assert.Contains(testPdf, vm.RecentFiles);
    }
    
    [Fact]
    public void ToggleThemeCommand_SwitchesTheme()
    {
        // Arrange
        var vm = new MainViewModel();
        var initialTheme = vm.IsDarkTheme;
        
        // Act
        vm.ToggleThemeCommand.Execute().Subscribe();
        
        // Assert
        Assert.NotEqual(initialTheme, vm.IsDarkTheme);
    }
}
```

**Mocking Services:**
```csharp
public class DocumentTabViewModelTests
{
    [Fact]
    public void DeletePageCommand_CallsService_WithCorrectIndex()
    {
        // Arrange
        var mockPdf = new Mock<IPdfDocument>();
        mockPdf.Setup(p => p.PageCount).Returns(5);
        var vm = new DocumentTabViewModel(mockPdf.Object);
        vm.CurrentPageIndex = 2;
        
        // Act
        vm.DeletePageCommand.Execute().Subscribe();
        
        // Assert
        mockPdf.Verify(p => p.RemovePages(It.Is<int[]>(ids => ids.Contains(3))), Times.Once);
    }
}
```

**Testing Async Commands:**
```csharp
[Fact]
public async Task ExportCommand_Executes_ReturnsImageData()
{
    // Arrange
    var vm = new DocumentTabViewModel();
    vm.LoadPdf("samples/test.pdf");
    
    // Act
    var result = await vm.ExportCommand.Execute("PNG");
    
    // Assert
    Assert.NotNull(result);
    Assert.True(result.Length > 0);
}
```

### Coverage Requirements

| Phase | Coverage Target |
|-------|-----------------|
| Phase 1-2 | 40%+ (core services) |
| Phase 3-4 | 60%+ |
| Phase 5+ | 80%+ |

**Current:** ~75% (Phase 4)

---

## Production Requirements

All code must support these production-grade features:

| Requirement | Implementation |
|-------------|----------------|
| Cross-platform | Test on Windows, Linux, macOS |
| Logging | Structured JSON logs via NLog |
| Error handling | Graceful degradation, user-friendly messages |
| Memory management | Dispose PDF documents; avoid leaks |
| Performance | Lazy loading for large PDFs; async operations |
| Security | Handle encrypted PDFs; validate inputs |
| Accessibility | Keyboard navigation; screen reader support (WCAG 2.1 AA target) |
| Localization | String resources externalized (future) |
| Auto-update | ClickOnce or Squirrel (future) |
| Icon licensing | UXWing SVG (free for commercial use) |

---

## External Resources

### Icon Library
- **UXWing**: https://uxwing.com/
  - All icons free for personal and commercial use
  - Formats: SVG (recommended), PNG
  - SVG recommended for large projects (smaller file size, retina support)
  - Constantly updated with new icons
  - FAQ: https://uxwing.com/frequently-asked-questions/

### Libraries & Tools
- **pdf2docx**: https://github.com/ArtifexSoftware/pdf2docx (AGPL-3.0)
- **QPdfSharp (PdfPig)**: https://github.com/UglyToad/PdfPig (Apache 2.0)
- **QuestPDF**: https://github.com/QuestPDF/QuestPDF (MIT for < $1M revenue)
- **SkiaSharp**: https://github.com/mono/SkiaSharp (MIT)
- **Avalonia UI**: https://github.com/AvaloniaUI/Avalonia (MIT)
- **iText7**: https://github.com/itext/itext7 (AGPL v3)
- **Tesseract.NET**: https://github.com/charlesw/tesseract (Apache 2.0)
- **Magick.NET**: https://github.com/dlemstra/Magick.NET (Apache 2.0)

### Inspiration Projects
- **DesktopPDFConverter**: https://github.com/SirTaphos/DesktopPDFConverter
- **PDF_Editor (Simple)**: https://github.com/topics/pdf?l=c%23
- **Readiris PDF 23**: Avalonia-based production PDF app (proof of concept)

---

## AI Assistant Checklist

When helping with this codebase, ALWAYS:

1. **Check `PLAN.md` first** for architecture decisions, phase priorities, and current status
2. **Review Active Issues** in `PLAN.md` to understand current blockers
3. **Follow coding standards** for C# and ReactiveUI patterns
4. **Prefer production-grade solutions** - avoid quick hacks that compromise reliability
5. **Consider cross-platform compatibility** - changes must work on Windows, Linux, macOS
6. **Suggest tests** for any new functionality
7. **Use dependency injection** - never instantiate services directly in ViewModels
8. **Update documentation** - every change must be reflected in relevant .md files
9. **Log the change** - add entry to Change Log in `PLAN.md`
10. **Track issues** - if something is broken, add to Active Issues table in `PLAN.md`
11. **Use UXWing SVG icons** for any new UI elements (free for commercial use)
12. **Respect license compatibility** - AGPL v3 for iText7/pdf2docx, MIT/Apache for others

---

## Document History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-02-17 | Initial version based on Aerodynamic Pressure Mapping template |
| 1.1 | 2026-02-17 | Added testing examples (service, ViewModel, mocking, async commands); expanded troubleshooting section with 30+ issues across 6 categories |
| 1.2 | 2026-02-18 | Added HybridDocxExportProvider, ribbon UI architecture, UXWing icon guidelines, SkiaSharp migration path, PDF optimization module, updated service table (20+ services), external resources section |
| 1.3 | 2026-02-18 | Consolidated version; added complete ribbon tab definitions, format preservation comparison table, test structure with 282+ tests, production requirements, AI assistant checklist updates |

---

## Quick Reference: What to Update When

| When You... | Update These Files |
|-------------|-------------------|
| Complete a task | `PLAN.md` (Change Log, Phase Status) |
| Fix a bug | `PLAN.md` (Change Log, move issue to Resolved) |
| Find a bug | `PLAN.md` (add to Active Issues) |
| Make design change | `PLAN.md` (Decision Log), `docs/ARCHITECTURE.md` |
| Add feature | `PLAN.md` (Change Log), `README.md` (Features) |
| Defer feature | `maybe-later.md` (create if needed) |
| Change dependency | `README.md` (Technology Stack), `*.csproj` |
| Change UI | `docs/ARCHITECTURE.md` (UI section), ribbon tab definitions |
| Add service | `docs/ARCHITECTURE.md` (Services table), this file |
| Start phase | `PLAN.md` (Phase Status) |
| Complete phase | `PLAN.md` (Phase Status, Change Log) |
| Add icons | Download from UXWing, save to `Resources/Icons/`, update icon list |

**Documentation is not optional. It is part of the definition of done.**

---

## License Reminders

| Component | License | Your Project Impact |
|-----------|---------|-------------------|
| iText7 | AGPL-3.0 | Your code must be AGPL if distributed, or buy commercial license |
| pdf2docx | AGPL-3.0 | Same as iText7 (optional backend) |
| Avalonia | MIT | ✅ No restrictions |
| UXWing Icons | Free for commercial use | ✅ No restrictions (per https://uxwing.com/) |
| PdfPig | Apache 2.0 | ✅ No restrictions |
| SkiaSharp | MIT | ✅ No restrictions |
| Tesseract.NET | Apache 2.0 | ✅ No restrictions |
| Magick.NET | Apache 2.0 | ✅ No restrictions |

**Recommendation:** Keep AGPL components optional where possible. Default to MIT/Apache-licensed libs (PdfPig, SkiaSharp, PDFSharp) for core features if you want permissive licensing options.
