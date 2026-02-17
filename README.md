# PDF Editor

A comprehensive, open-source PDF editor built with C# and Avalonia UI, integrating multiple leading open-source PDF technologies.

## Features

### Current (Version 0.0.1)
- Multi-library architecture (iText7, PDFSharp, Pdfium.Net)
- Avalonia cross-platform UI framework
- Modular, extensible design
- Dependency injection for loose coupling

### In Development (MVP)
- PDF viewing and navigation
- Page thumbnail previews
- Text extraction
- Page manipulation (rotation, removal, reordering)

### Planned
- OCR text layer creation (Tesseract/PaddleOCR)
- Image conversion (PDF to/from PNG/JPEG/TIFF)
- ClawPDF printer integration
- Batch processing
- Document encryption
- Metadata editing
- Command-line interface

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
- **OCR**: Tesseract.NET / PaddleOCR
- **Logging**: NLog
- **Testing**: xUnit + Moq

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
│   ├── PDFEditor.UI/                # Avalonia user interface
│   ├── PDFEditor.ClawPDFIntegration/# ClawPDF wrapper
│   └── PDFEditor.Tests/             # Unit tests
├── docs/                            # Documentation
├── libs/                            # External libraries
└── samples/                         # Sample PDF files
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
- [ ] MVP (0.1) - Basic PDF viewing
- [ ] v0.2 - Page operations
- [ ] v0.3 - Image processing
- [ ] v0.4 - OCR support
- [ ] v0.5 - ClawPDF integration
- [ ] v1.0 - Production release

## Key Features Planned

### Phase 1: PDF Basics
- Open/save PDFs
- View pages with zoom/pan
- Extract text
- Rotate/remove/reorder pages

### Phase 2: Image Operations
- Convert PDF pages to images (PNG, JPEG, TIFF)
- Convert images to PDF
- Batch processing

### Phase 3: Advanced Features
- OCR text recognition
- Create searchable PDFs
- ClawPDF printer integration
- Document encryption
- Metadata editing

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

**Status**: Early Development (v0.0.1)  
**Last Updated**: February 17, 2026  
**Maintainer**: Oriol Canillas