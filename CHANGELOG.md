# PDF Editor Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [0.0.3 - PDF→DOCX High-Fidelity Export] - 2026-02-19

### Added
- **Hybrid PDF→DOCX Export**: 3-tier backend system
  - `pdf2docx-cli.exe` sidecar (bundled in MSI, ~80 MB, no Python required)
  - Python + pdf2docx 0.5.9 fallback (auto-detected)
  - iText7 built-in fallback (always available)
  - Superior layout/table/merged-cell preservation vs pure iText7
- **Auto-bundled Sidecar**: pdf2docx-cli built via PyInstaller in CI and included in MSI and standalone artifacts
- **Developer Setup Scripts**:
  - `install.ps1` (Windows) — one-shot setup: .NET restore → build → pdf2docx install → sidecar build → tests
  - `install.sh` (Linux/macOS) — equivalent, also installs system fonts on Ubuntu
- **Installer Script Fixes**: Fixed WiX builder crash when `wix` command resolves to non-Application type

### Changed
- `HybridDocxExportProvider`: Refactored to unified 3-tier backend with single CLI code path
- `Directory.Build.props`: Global `<LangVersion>latest</LangVersion>` enables C# 12+ syntax
- CI workflow: Builds pdf2docx-cli sidecar on Windows and Linux, bundles into artifacts
- `build-installer.ps1`: Auto-builds sidecar and bundles into publish directory before MSI creation

### Fixed
- C# 12 collection expression syntax errors in development-time code (downgraded to C# 10 compatible)
- ElectronicSignatureService: Added font fallback chain (DejaVu Sans → Liberation Sans → FreeSans → Helvetica → Arial → sans-serif)
- Linux CI: Font path issues, Pdfium crash on parallel test execution (xunit parallelism disabled)
- pdf2docx detection: Now works with v0.5.9 (removed `__version__` check)

## [0.0.1 - Initial Setup] - 2026-02-17
- **Export System Refactor**: Provider-based `IExportProvider` architecture with `ExportProviderRegistry`
  - `ImageExportProvider` — PNG, JPEG, TIFF, BMP, WebP export with DPI control
  - `TextExportProvider` — Plain text extraction to .txt
  - `HtmlExportProvider` — Visual HTML with base64-embedded page images
  - `DocxExportProvider` — Microsoft Word export via DocumentFormat.OpenXml
- **Unified Export Dialog**: Format selection, DPI/quality, page range, progress bar
- **OCR Implementation**: `TesseractOcrService` with multi-language support
  - OCR Current Page and OCR All Pages dialogs
  - Language and DPI selection
  - Progress reporting for multi-page OCR
- **About Dialog**: GitHub repository link, version info, license link, library credits
- **Comprehensive Unit Tests**: 97 tests across 9 test files
  - TestPdfGenerator helper for in-memory PDF generation
  - Tests for: PdfOperations, PdfSearchService, PdfSplitService, PdfSecurityService, PdfCropService, PdfWatermarkService, PdfAnnotationService, PdfExportService, UndoRedoManager, ExportProviderRegistry
- **New NuGet Package**: DocumentFormat.OpenXml 3.0.1

### Changed
- `CoreServiceCollectionExtensions` now registers `ExportProviderRegistry` and `TesseractOcrService`
- Tools menu expanded with OCR operations
- Export menu expanded with Export Dialog and DOCX export

### TODO
- Complete ClawPDF integration
- Form handling
- Digital signatures
- Document comparison
- Plugin system

## [0.0.1 - Initial Setup] - 2026-02-17

### Added
- Solution and project structure
- All NuGet package dependencies
- Core interfaces and abstractions
- Basic Avalonia application skeleton
- Comprehensive setup and architecture documentation
- Development roadmap
- Contributing guidelines

### Known Issues
- .NET SDK needs to be installed by user
- Ghostscript and Tesseract are optional
- Avalonia designer not fully integrated in Visual Studio

---

## Format

This changelog uses the format provided by [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

### Conventions
- **Added** - New features
- **Changed** - Changes in existing functionality
- **Deprecated** - Soon-to-be removed features
- **Removed** - Removed features
- **Fixed** - Bug fixes
- **Security** - Security vulnerabilities

### Version Format
- Development: `[Unreleased]`
- Releases: `[VERSION - DESCRIPTION] - YYYY-MM-DD`
- Semantic Versioning: `MAJOR.MINOR.PATCH`
