# PDF Editor

A comprehensive, open-source PDF editor built with C# and Avalonia UI, integrating multiple leading open-source PDF technologies.

## Features

### Current (Version 0.0.1)

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
- **DOCX Backend**: pdf2docx 0.5.9 (Python, optional – high-fidelity export)
- **Logging**: NLog
- **Testing**: xUnit + Moq (470+ tests)

## System Requirements

- **OS**: Windows 10+, Linux (Ubuntu 22.04+ recommended), macOS 12+
- **.NET Runtime**: 6.0 or later
- **Python**: 3.8+ (optional — required only for high-fidelity PDF→DOCX export)
- **RAM**: 2 GB minimum (4 GB recommended for large PDFs)
- **Storage**: 500 MB for installation + dependencies

## Quick Start

### One-Command Setup

**Windows (PowerShell):**
```powershell
git clone https://github.com/OriolCanillasGautier/PDF-Editor.git
cd PDF-Editor
.\install.ps1
```

**Linux / macOS (bash):**
```bash
git clone https://github.com/OriolCanillasGautier/PDF-Editor.git
cd PDF-Editor
chmod +x install.sh && ./install.sh
```

The install script will:
1. Verify .NET 6+ and Python 3.8+ are present
2. Restore all NuGet packages
3. Build the solution (Release)
4. Install **pdf2docx 0.5.9** from the pinned GitHub wheel (Python backend for high-fidelity DOCX export)
5. Run the full test suite

Skip individual steps if needed:
```powershell
.\install.ps1 -SkipTests         # skip the test run
.\install.ps1 -SkipPdf2Docx     # skip Python/pdf2docx install
.\install.ps1 -SkipBuild        # restore packages only
```

Then run the app:
```powershell
dotnet run --project src/PDFEditor.UI/PDFEditor.UI.csproj
```

### For Developers

#### Prerequisites

Before getting started, ensure you have the following installed:

1. **[.NET SDK 6.0+](https://dotnet.microsoft.com/en-us/download)** — Required to build and run
2. **[Python 3.8+](https://www.python.org/downloads/)** — Optional, needed for high-fidelity PDF→DOCX export via pdf2docx
3. **[Visual Studio 2022 Community](https://visualstudio.microsoft.com/vs/community/)** (recommended) or **Visual Studio Code**
4. **[Git](https://git-scm.com/)** — Version control
5. **[WiX Toolset v3.x](https://wixtoolset.org/download/)** — For building the MSI installer (Windows only)

#### Step 1: Clone & Run the Install Script

```powershell
# Windows
git clone https://github.com/OriolCanillasGautier/PDF-Editor.git
cd PDF-Editor
.\install.ps1
```
```bash
# Linux / macOS
git clone https://github.com/OriolCanillasGautier/PDF-Editor.git
cd PDF-Editor
chmod +x install.sh && ./install.sh
```

This does everything: NuGet restore, build, pdf2docx install, and test run in one shot.

#### Step 2 (manual alternative): Restore & Build

```bash
dotnet restore
dotnet build --configuration Release
```

#### Step 3 (manual alternative): Install pdf2docx

```bash
# Pinned 0.5.9 wheel — pure-Python, no compiler required
pip install https://github.com/ArtifexSoftware/pdf2docx/releases/download/v0.5.9/pdf2docx-0.5.9-py3-none-any.whl
```

The app will automatically detect Python and use pdf2docx for DOCX export when available. If Python is not installed, it falls back to the built-in iText7-based exporter (good for most documents).

#### Step 4: Run the Application

**Option A: Use dotnet**
```bash
dotnet run --project src/PDFEditor.UI/PDFEditor.UI.csproj
```

**Option B: Use Visual Studio**
1. Open `PDFEditor.sln` in Visual Studio 2022
2. Set `PDFEditor.UI` as the startup project (right-click → Set as Startup Project)
3. Press `F5` to debug or `Ctrl+F5` to run

#### Step 5: Run Tests

```bash
# Run all unit tests
dotnet test

# Run with detailed output
dotnet test --verbosity normal

# Run specific test project
dotnet test src/PDFEditor.Tests/PDFEditor.Tests.csproj

# Run with code coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

## PDF→DOCX Export

The DOCX exporter uses a **hybrid approach** with two backends:

| Feature | iText7 (built-in) | pdf2docx 0.5.9 (Python) |
|---------|------------------|--------------------------|
| Requires Python | No | Yes (3.8+) |
| Text & fonts | ✅ Good | ✅ Excellent |
| Tables | ⚠️ Heuristic | ✅ Structure-aware |
| Merged cells | ⚠️ Limited | ✅ Full support |
| Complex layouts | ⚠️ May reflow | ✅ Better preservation |
| License | AGPL v3 | AGPL v3 |

Install pdf2docx (once) to unlock high-fidelity export:
```bash
pip install https://github.com/ArtifexSoftware/pdf2docx/releases/download/v0.5.9/pdf2docx-0.5.9-py3-none-any.whl
```
The app auto-detects Python and switches to pdf2docx automatically. No configuration needed.

### No Python? Use the self-contained sidecar

Build `pdf2docx-cli.exe` once — it bundles CPython + pdf2docx into a single ~80 MB executable. Copy it next to `PDFEditor.UI.exe` and high-fidelity export works with no Python installation on the client machine.

```powershell
# Windows — builds tools\pdf2docx-cli\dist\pdf2docx-cli.exe
.\tools\pdf2docx-cli\build.ps1

# Copy to app output
Copy-Item .\tools\pdf2docx-cli\dist\pdf2docx-cli.exe .\artifacts\publish\
```

```bash
# Linux / macOS — builds tools/pdf2docx-cli/dist/pdf2docx-cli
chmod +x tools/pdf2docx-cli/build.sh && ./tools/pdf2docx-cli/build.sh
cp tools/pdf2docx-cli/dist/pdf2docx-cli artifacts/publish/
```

The app checks for the sidecar automatically (sidecar → Python in PATH → iText7 fallback).

## Building the MSI Installer (Windows Only)

To create a Windows installer (.msi):

```powershell
# Make sure WiX Toolset is installed
# Then run the build script from PowerShell:

cd installer
.\build-installer.ps1
```

**What it does:**
1. Publishes the app to `artifacts/publish/`
2. Reads the version from `Directory.Build.props`
3. Builds the MSI using WiX Toolset
4. Output: `installer/PDFEditor-Setup-win-x64.msi`

**To build with a custom version:**
```powershell
.\build-installer.ps1 -AppVersion "1.2.3"
```

**Version Management:**
- The app version is defined in [`Directory.Build.props`](Directory.Build.props) at the repository root
- Change the `<Version>` tag to update the version for all assemblies and the MSI
- This version will automatically flow to NuGet packages, the MSI, and GitHub releases

#### Making a GitHub Release

**Automatic via Git Tags (Recommended):**

```bash
# Ensure all changes are committed
git add .
git commit -m "Release v1.2.3"

# Create an annotated tag
git tag -a v1.2.3 -m "Version 1.2.3"

# Push the tag to GitHub
git push origin v1.2.3
```

This triggers the **GitHub Actions CI/CD pipeline**, which will:
1. Build the project on Windows and Linux
2. Run all tests
3. Generate code coverage reports
4. Build the MSI installer
5. Create a GitHub Release with the MSI attached

**To view the release:**
- Go to [Releases](https://github.com/OriolCanillasGautier/PDF-Editor/releases)
- The MSI will be automatically attached

**Manual Release (if needed):**
1. Build locally: `.\installer\build-installer.ps1`
2. Go to [GitHub Releases](https://github.com/OriolCanillasGautier/PDF-Editor/releases)
3. Click "Draft a new release"
4. Name it `v1.2.3`, attach the MSI, and publish

#### Key Files for Developers

- **[Directory.Build.props](Directory.Build.props)** — Central version management
- **[PDFEditor.sln](PDFEditor.sln)** — Main solution file
- **[src/PDFEditor.UI/](src/PDFEditor.UI/)** — Avalonia UI application
- **[src/PDFEditor.Core/](src/PDFEditor.Core/)** — Business logic & services
- **[src/PDFEditor.Tests/](src/PDFEditor.Tests/)** — Unit tests (500+ tests)
- **[installer/](installer/)** — MSI installer source
- **[SETUP.md](SETUP.md)** — Detailed setup troubleshooting
- **[.github/workflows/](../.github/workflows/)** — CI/CD configuration

#### Troubleshooting

**Build fails with missing NuGet packages:**
```bash
dotnet nuget locals all --clear
dotnet restore
```

**Visual Studio designer doesn't show UI:**
- The Avalonia designer may not render in VS 2022; edit the XAML directly or run the app to see changes

**MSI build fails:**
- Ensure WiX Toolset v3.x is installed: `choco install wixtoolset`
- Set the Windows PATH to include WiX bin folder

**Tests fail with "Tesseract not found":**
- Install Tesseract OCR and download language data files
- Set the `TESSDATA_PREFIX` environment variable to point to the tessdata directory

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

- **Issues**: [GitHub Issues](https://github.com/OriolCanillasGautier/PDF-Editor/issues)
- **Discussions**: [GitHub Discussions](https://github.com/OriolCanillasGautier/PDF-Editor/discussions)
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