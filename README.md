# PDF Editor

A comprehensive, open-source PDF editor built with C# and Avalonia UI, integrating multiple leading open-source PDF technologies.

## Features

### Current (Version 1.0.0-alpha)

**PDF Viewing & Editing**
- Multi-tab document interface with page thumbnails
- Page manipulation: rotate, delete, reorder, merge, split, extract
- Text extraction and full-text search with context snippets
- Comprehensive annotation tools (text, highlight, rectangle, ellipse, arrow, freehand, blur, redact, stamps, sticky notes)
- Undo/redo with full state history
- Dark/light theme switching

**Export System**
- Provider-based export architecture (`IExportProvider`) with 15 format providers
- Export formats: PNG, JPEG, TIFF, BMP, WebP, Plain Text, HTML, DOCX, XLSX, RTF, Markdown, CSV, JSON, PPTX, EPUB, LaTeX, ODT, ODP, ODS
- Unified export dialog with format selection, DPI/quality control, page range, and progress
- Images-to-PDF conversion

**OCR (Optical Character Recognition)**
- Tesseract-based OCR engine with multi-language support
- Per-page and full-document OCR
- Configurable DPI for quality control

**Security & Metadata**
- Password protection and AES-256 encryption
- Permission management (print, copy, edit)
- Metadata viewing and editing (title, author, subject)

**Batch Processing**
- Batch rotate, watermark, page numbers, encrypt, export, merge, split
- Progress reporting

**Additional**
- Watermarks (text, diagonal, configurable opacity and rotation)
- Headers, footers, and page number stamping
- Page cropping and resizing (A4, Letter, Legal, A3)
- Session save/restore
- Cross-platform: Windows, Linux, macOS

**Forms & Signatures**
- PDF form detection, reading, filling, and flattening (AcroForms)
- Form field creation (text, checkbox, dropdown, radio button, signature)
- Digital signatures with PKCS#12 certificates (sign, verify, list)
- Electronic signatures (draw, type, upload)
- Form validation (13 rule types), calculation fields, conditional logic
- Barcode generation (QR, Code128, Code39, EAN13, DataMatrix, PDF417)

**Document Enhancement**
- Direct text editing in PDF
- Table detection, editing, and HTML/CSV export
- Auto-crop, deskew, background removal for scanned documents
- Image extraction, replacement, and compression
- Font analysis and replacement
- Table of contents auto-generation from headings
- PDF/A archival conversion, PDF/X print production compliance

**Review & Security**
- Document comparison (text-level LCS diff + pixel-level visual diff)
- Content redaction (area, text, page-level)
- Metadata scrubber, document sanitizer
- Accessibility checker (WCAG/PDF/UA audit), auto-tagging, alt text editor
- Certificate manager (Windows Store, PFX inspection, chain validation)
- XFDF annotation import/export (Adobe standard)

**Productivity**
- Quick actions (customizable macros with step-by-step execution)
- Document templates (save, reuse, categorize, search)
- Watch folder (auto-process dropped files)
- Booklet creation for double-sided printing

### Planned
- ClawPDF printer integration
- Ribbon UI toolbar redesign
- SkiaSharp rendering migration
- PDF optimization module

## Technology Stack

### Core
- **Language**: C# with .NET 6.0+
- **UI Framework**: Avalonia (cross-platform)
- **MVVM**: ReactiveUI
- **License**: AGPL v3

### PDF Libraries
- **iText7** - PDF manipulation (AGPL v3)
- **PDFSharp** - PDF creation (MIT)
- **Pdfium.Net** - PDF rendering (Apache 2.0)

### Additional
- **Image Processing**: Magick.NET
- **OCR**: Tesseract.NET 5.2.0
- **Office Export**: DocumentFormat.OpenXml 3.0.1
- **Logging**: NLog
- **Testing**: xUnit + Moq (97 tests)

## System Requirements

- **OS**: Windows 7+, Linux, macOS
- **.NET Runtime**: 6.0 or later
- **RAM**: 2GB minimum (4GB recommended)
- **Storage**: 500MB for installation + dependencies

## Installation

### For Users (Coming Soon)
```
Download the latest installer from Releases
Run the .msi file
Follow the installation wizard
```

### For Developers

1. **Install Prerequisites**
   ```powershell
   # .NET SDK 6.0+
   # Visual Studio 2022 Community (free)
   # Git
   ```

2. **Clone Repository**
   ```bash
   git clone https://github.com/ocanillas/PDF-Editor.git
   cd PDF-Editor
   ```

3. **Build & Run**
   ```bash
   dotnet restore
   dotnet build
   cd src/PDFEditor.UI
   dotnet run
   ```

## Documentation

- [Setup Guide](SETUP.md) - Complete development setup
- [Architecture Guide](docs/ARCHITECTURE.md) - System design
- [Implementation Roadmap](docs/ROADMAP.md) - Development timeline
- [Project Plan](PROJECT_PLAN.md) - Strategic overview
- [Contributing Guidelines](CONTRIBUTING.md) - How to contribute

## Project Structure

```
PDF-Editor/
├── src/
│   ├── PDFEditor.Core/              # Core business logic
│   │   ├── Abstractions/            # Interfaces (IPdfDocument, IOcrEngine, IExportProvider)
│   │   └── Services/                # Service implementations
│   │       └── Export/              # Export providers (Image, Text, HTML, DOCX)
│   ├── PDFEditor.UI/                # Avalonia user interface
│   │   └── ViewModels/              # MVVM ViewModels (MainViewModel, DocumentTabViewModel)
│   ├── PDFEditor.ClawPDFIntegration/# ClawPDF wrapper (stub)
│   └── PDFEditor.Tests/             # Unit tests (97 tests)
│       ├── Core/                    # Service tests
│       └── Helpers/                 # TestPdfGenerator
├── docs/                            # Documentation
├── installer/                       # WiX installer
└── artifacts/                       # Build artifacts
```

## Quick Start for Developers

1. Open `PDFEditor.sln` in Visual Studio 2022
2. Right-click solution → "Restore NuGet Packages"
3. Build solution (Ctrl+Shift+B)
4. Set `PDFEditor.UI` as startup project
5. Press F5 to run

For command line:
```bash
dotnet restore
dotnet build
dotnet run --project src/PDFEditor.UI
```

## Contributing

We welcome contributions! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Quick Contribution Checklist
- Fork the repo
- Create feature branch: `git checkout -b feature/your-feature`
- Add tests for new functionality
- Ensure all tests pass: `dotnet test`
- Submit pull request with clear description

## License

This project is licensed under the **AGPL v3** license due to its use of:
- clawPDF (AGPL v3)
- iText7 (AGPL v3)
- Ghostscript (AGPL v3)

See [LICENSE](LICENSE) for details.

## Roadmap

- [x] Project initialization
- [x] Architecture design
- [x] PDF viewing with multi-tab interface
- [x] Page operations (rotate, delete, merge, split, extract, reorder)
- [x] Image export (PNG, JPEG, TIFF, BMP, WebP)
- [x] Provider-based export system (Image, Text, HTML, DOCX)
- [x] Full-text search with context snippets
- [x] Annotation tools (10 types)
- [x] OCR support (Tesseract, multi-language)
- [x] Security (encryption, passwords, permissions)
- [x] Batch processing
- [x] Unit test suite (97 tests)
- [ ] ClawPDF integration
- [ ] Interactive form handling
- [ ] Digital signatures
- [ ] Document comparison
- [ ] v1.0 - Production release

## Community & Support

- **Issues**: [GitHub Issues](https://github.com/ocanillas/PDF-Editor/issues)
- **Discussions**: [GitHub Discussions](https://github.com/ocanillas/PDF-Editor/discussions)
- **Documentation**: [See docs/](docs/)

## Credits

Built with inspiration and components from:
- [clawPDF](https://github.com/clawsoftware/clawPDF)
- [iText7](https://github.com/itext/itext7-dotnet)
- [Avalonia UI](https://github.com/AvaloniaUI/Avalonia)
- [ReactiveUI](https://github.com/reactiveui/ReactiveUI)

## Disclaimer

This is an open-source project and is provided as-is. Always test thoroughly before using with important documents.

---

**Status**: Alpha (v1.0.0-alpha)  
**Last Updated**: February 17, 2026  
**Maintainer**: Oriol Canillas