# Copilot Instructions for PDF Editor

**Version:** 1.0  
**Last Updated:** 2026-02-17

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
| 2026-02-17 | Phase 1 | Created copilot-instructions.md | .github/copilot-instructions.md |
```

### Rule 3: Track Errors and Blockers

Maintain an active issues section in `PLAN.md`:

```markdown
## Active Issues

| ID | Priority | Issue | Impact | Status | Owner |
|----|----------|-------|--------|--------|-------|
| ERR-001 | P1 | clawPDF wrapper not implemented | Blocking print-to-PDF | Open | - |
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
| 2026-02-17 | Avalonia + ReactiveUI | Cross-platform, strong MVVM support | WPF (Windows-only), MAUI (immature) |
```

### Rule 5: Phase Status Must Be Accurate

Update phase status in real-time in `PLAN.md`:

```markdown
## Phase Status

| Phase | Status | Started | Completed | Progress |
|-------|--------|---------|-----------|----------|
| Phase 1: Foundation | Complete | 2026-02-01 | 2026-02-14 | 100% |
| Phase 2: Page Operations | In Progress | 2026-02-15 | - | 60% |
| Phase 3: Image Processing | Not Started | - | - | 0% |
```

**Update when:**
- Starting a new phase
- Completing a phase
- Significant progress milestones

---

## Project Overview

This is a **cross-platform PDF editor** built with C#, .NET 6, and Avalonia UI. It integrates multiple PDF libraries (iText7, PDFSharp, Pdfium/Docnet) to provide comprehensive PDF manipulation, viewing, annotation, and export capabilities.

**Key Goal:** Production-grade, open-source PDF editor with features comparable to Adobe Acrobat, licensed under AGPL v3.

**Quality Standard:** Clean architecture, modular design, comprehensive testing, cross-platform compatibility (Windows, Linux, macOS).

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
│  - MainWindow.axaml                         │
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
│  - Service Implementations                  │
└─────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────┐
│  Integration Layer (External Tools)         │
│  - ClawPDFIntegration                       │
│  - Ghostscript Bridge                       │
│  - Tesseract Bridge                         │
└─────────────────────────────────────────────┘
```

### Project Structure

```
PDF-Editor/
├── src/
│   ├── PDFEditor.Core/              # Core business logic, services, abstractions
│   │   ├── Abstractions/            # Interfaces (IPdfDocument, etc.)
│   │   ├── Services/                # Implementations (ITextPdfService, etc.)
│   │   └── AppConfig.cs             # Global configuration
│   │
│   ├── PDFEditor.UI/                # Avalonia desktop application
│   │   ├── App.axaml                # Application entry point
│   │   ├── MainWindow.axaml         # Main window UI
│   │   └── ViewModels/              # MVVM ViewModels
│   │
│   ├── PDFEditor.ClawPDFIntegration/# Bridge to clawPDF printer
│   │   └── ClawPDFWrapper.cs        # Wrapper for clawPDF.exe
│   │
│   └── PDFEditor.Tests/             # Unit tests (xUnit)
│
├── docs/                            # Documentation
├── .github/workflows/               # CI/CD
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
        // services.AddSingleton<IImageProcessor, ImageProcessorService>();
        // services.AddSingleton<IOcrEngine, OcrEngineService>();
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

| Service | Purpose | Library |
|---------|---------|---------|
| `ITextPdfService` | Document manipulation (load, save, rotate, remove) | iText7 |
| `PdfRenderService` | PDF → image rendering | Docnet.Core |
| `PdfExportService` | Export to images/HTML/text | Magick.NET |
| `PdfAnnotationService` | Annotation rendering/burning | iText7 |
| `PdfSearchService` | Text extraction & search | iText7 |
| `PdfSecurityService` | Password protection, encryption | iText7 |
| `SessionService` | User session persistence | JSON |
| `UndoRedoManager` | Command undo/redo stack | Custom |

---

## Critical Workflows

### Build & Run Commands

```bash
# Restore NuGet packages
dotnet restore

# Build all projects
dotnet build

# Run tests
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
git clone https://github.com/ocanillas/PDF-Editor.git
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
| `src/PDFEditor.UI/MainWindow.axaml` | Main window UI definition |
| `src/PDFEditor.UI/MainWindow.axaml.cs` | Main window code-behind (event handlers) |
| `src/PDFEditor.UI/ViewModels/MainViewModel.cs` | App-level ViewModel |
| `src/PDFEditor.UI/ViewModels/DocumentTabViewModel.cs` | Per-document ViewModel |
| `src/PDFEditor.Core/CoreServiceCollectionExtensions.cs` | DI registration |
| `src/PDFEditor.Core/AppConfig.cs` | Global constants |
| `src/PDFEditor.Core/Abstractions/IPdfDocument.cs` | Core document interface |
| `src/PDFEditor.Core/Services/ITextPdfService.cs` | iText7 implementation |
| `src/PDFEditor.ClawPDFIntegration/ClawPDFWrapper.cs` | clawPDF printer wrapper |
| `src/PDFEditor.UI/nlog.config` | NLog logging configuration |
| `README.md` | Project overview |
| `SETUP.md` | Developer setup guide |
| `PLAN.md` | Implementation roadmap |
| `docs/ARCHITECTURE.md` | Architecture documentation |
| `docs/ROADMAP.md` | Phase-by-phase guide |

---

## Code Patterns & Conventions

### C# Language Features

- **Target:** .NET 6.0, C# 10+
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
| Interfaces | `I` prefix + PascalCase | `IPdfDocument`, `IImageProcessor` |
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
| **iText7** | PDF manipulation (load, save, merge, split) | AGPL v3 | Active |
| **PDFSharp** | PDF creation from scratch | MIT |备用 |
| **Docnet.Core** | PDF rendering to images (Pdfium wrapper) | Apache 2.0 | Active |
| **Magick.NET** | Image format conversion | Apache 2.0 | Active |

### OCR Integration (Planned)

```csharp
// Interface (not yet implemented)
public interface IOcrEngine
{
    Task<string> RecognizeTextAsync(byte[] imageData, string language = "eng");
    Task<List<OcrResult>> RecognizeTextRegionsAsync(byte[] imageData, string language = "eng");
    List<string> GetSupportedLanguages();
}
```

**Libraries:**
- Tesseract.NET (Apache 2.0)
- PaddleOCR (Apache 2.0)

### clawPDF Integration (Partial)

```csharp
// Wrapper exists but not fully implemented
public class ClawPDFWrapper
{
    public void PrintToPdf(string inputFile, string outputPath, string? printerName = null);
    public void MergeDocuments(string[] inputFiles, string outputPath);
}
```

**Status:** Wrapper class exists; full integration pending Phase 5.

### External Dependencies

| Component | Package | Version | Purpose |
|-----------|---------|---------|---------|
| Avalonia | `Avalonia` | 11.0.0 | Cross-platform UI framework |
| ReactiveUI | `Avalonia.ReactiveUI` | 11.0.0 | MVVM framework |
| iText7 | `itext7` | 7.2.5 | PDF manipulation |
| Docnet | `Docnet.Core` | 2.6.0 | PDF rendering |
| Magick.NET | `Magick.NET-Q16-AnyCPU` | 14.10.2 | Image processing |
| Tesseract | `Tesseract` | 5.2.0 | OCR engine |
| NLog | `NLog` | 5.2.8 | Logging |
| xUnit | `xunit` | 2.6.6 | Testing framework |
| Moq | `Moq` | 4.20.72 | Mocking library |

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

### PDF Operations

| Issue | Resolution |
|-------|------------|
| PDF not rendering | Check Docnet initialization; verify PDF is not corrupted or password-protected |
| Blank pages after export | Verify `PdfRenderService` DPI settings (min 72); check page index is 0-based |
| "File in use" error on save | Ensure PDF is disposed before saving. Use `using` statements for streams |
| Text extraction returns empty | PDF may be scanned image (no text layer); use OCR instead |
| Rotate doesn't persist | Call `SaveToFile` after rotation; verify document is not read-only |
| Merge produces corrupt PDF | Ensure all source PDFs are valid; check iText7 version compatibility |

### UI & ViewModels

| Issue | Resolution |
|-------|------------|
| Binding not updating | Ensure property uses `RaiseAndSetIfChanged`; check `DataContext` is set |
| Command not executing | Verify `CanExecute` condition; check `Subscribe()` is called on command |
| Tab not closing properly | Call `CloseTab()` not `CloseActiveTab()` for specific tabs; dispose resources |
| Annotations not visible | Verify `AnnotationCanvas` is rendered; check Z-index; ensure `IsAnnotationMode` is true |
| Thumbnail list not updating | Ensure `ObservableCollection` is used; call `PropertyChanged` for collection changes |
| Keyboard shortcuts not working | Check `OnKeyDown` override in `MainWindow`; verify no other control has focus |

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
| clawPDF not detected | Verify `clawPDF.exe` path; check printer is installed in Windows; run as administrator |
| Tesseract OCR fails | Download language data files (`.traineddata`); set `TESSDATA_PREFIX` environment variable |
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

**When you fix a bug, document it in the Change Log in `PLAN.md`.**

---

## Testing Requirements

### Test Structure

```
src/PDFEditor.Tests/
├── Core/
│   ├── PdfDocumentTests.cs
│   ├── PdfExportServiceTests.cs
│   └── PdfAnnotationServiceTests.cs
├── ViewModels/
│   ├── MainViewModelTests.cs
│   └── DocumentTabViewModelTests.cs
└── Integration/
    └── EndToEndTests.cs
```

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
| Accessibility | Keyboard navigation; screen reader support |
| Localization | String resources externalized (future) |
| Auto-update | ClickOnce or Squirrel (future) |

---

## Repository Structure

```
PDF-Editor/
├── .github/
│   ├── copilot-instructions.md   # This file
│   └── workflows/
│       └── build.yml             # CI/CD: build, test, publish
├── src/                          # Main projects (exists)
├── docs/                         # Documentation (exists)
├── artifacts/                    # Build artifacts (exists)
├── installer/                    # WiX installer (exists)
├── .gitignore                    # Git ignore rules (exists)
├── PDFEditor.sln                 # Solution file (exists)
├── README.md                     # Project overview (exists)
├── SETUP.md                      # Developer setup (exists)
├── PLAN.md                       # Master roadmap (exists)
├── CONTRIBUTING.md               # Contribution guidelines (exists)
├── CHANGELOG.md                  # Version history (exists)
└── LICENSE                       # AGPL v3 license (exists)
```

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

---

## Document History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-02-17 | Initial version based on Aerodynamic Pressure Mapping template |
| 1.1 | 2026-02-17 | Added testing examples (service, ViewModel, mocking, async commands); expanded troubleshooting section with 30+ issues across 6 categories |

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
| Change UI | `docs/ARCHITECTURE.md` (UI section) |
| Add service | `docs/ARCHITECTURE.md` (Services table) |
| Start phase | `PLAN.md` (Phase Status) |
| Complete phase | `PLAN.md` (Phase Status, Change Log) |

**Documentation is not optional. It is part of the definition of done.**
