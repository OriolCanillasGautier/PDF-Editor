# PDF Editor - Developer Setup & Getting Started

## Prerequisites

Before you can build and run the PDF Editor, ensure you have the following installed:

### Required Software

1. **.NET SDK 6.0 or later**
   - Download: https://dotnet.microsoft.com/download
   - Verify: Open PowerShell and run `dotnet --version`

2. **Visual Studio 2022** (Community Edition is free)
   - Download: https://visualstudio.microsoft.com/downloads/
   - Choose: ".NET desktop development" workload during installation
   - Alternative: VS Code with C# DevKit extension

3. **Git** (for version control)
   - Download: https://git-scm.com/download/win

### Optional but Recommended

- **Ghostscript** (for advanced PDF rendering)
  - Download: https://www.ghostscript.com/download/gsdnld.html
  - Add to PATH after installation

- **Tesseract OCR** (for OCR functionality)
  - Download: https://github.com/UB-Mannheim/tesseract/wiki
  - Add to PATH after installation

## Initial Setup Steps

### 1. Clone/Open the Repository

```powershell
# If starting fresh
git clone <repository-url>
cd PDF-Editor

# Or just navigate to the existing folder
cd C:\Users\YOUR_USERNAME\Documents\GitHub\PDF-Editor
```

### 2. Restore NuGet Dependencies

```powershell
dotnet restore
```

This will download all required NuGet packages specified in the .csproj files.

### 3. Build the Solution

**From Visual Studio:**
- Open `PDFEditor.sln`
- Right-click Solution → Rebuild Solution

**From Command Line:**
```powershell
dotnet build
```

### 4. Run the Application

**From Visual Studio:**
- Set `PDFEditor.UI` as startup project
- Press F5 or Debug → Start Debugging

**From Command Line:**
```powershell
cd src/PDFEditor.UI
dotnet run
```

## Project Structure Explanation

```
PDF-Editor/
├── src/
│   ├── PDFEditor.Core/              # Core PDF operations library
│   │   ├── Abstractions/            # Interfaces (IPdfDocument, IOcrEngine, etc.)
│   │   ├── Services/                # Implementations (ITextPdfService)
│   │   └── AppConfig.cs             # Global configuration
│   │
│   ├── PDFEditor.UI/                # Avalonia desktop application
│   │   ├── App.axaml                # Application entry point
│   │   ├── MainWindow.axaml         # Main window UI
│   │   └── ViewModels/              # (To be created) MVVM ViewModels
│   │
│   ├── PDFEditor.ClawPDFIntegration/# Bridge to clawPDF printer
│   │   └── ClawPDFWrapper.cs        # Wrapper for clawPDF.exe
│   │
│   └── PDFEditor.Tests/             # Unit tests (xUnit)
│
├── libs/                            # External libraries folder
├── docs/                            # Documentation
├── samples/                         # Sample PDF files for testing
├── PDFEditor.sln                    # Visual Studio solution file
└── README.md                        # Project README
```

## NuGet Packages Included

### Core PDF Libraries
- **iText7** (7.2.5) - AGPL v3 - PDF manipulation
- **PdfSharp** (6.1.1) - MIT - PDF creation
- **Pdfium.Net** (6.2.1) - Apache 2.0 - PDF rendering

### Image Processing
- **Magick.NET** (13.5.0) - Apache 2.0 - Image manipulation

### OCR
- **Tesseract.NET** (1.0.0) - Free text recognition
- **PaddleOCR** (2.0.5) - Modern deep learning OCR

### UI & MVVM
- **Avalonia** (11.0.0) - MIT - Cross-platform UI
- **ReactiveUI** (19.5.91) - MIT - MVVM framework

### Testing
- **xUnit** (2.6.6) - .NET testing framework
- **Moq** (4.20.70) - Mocking library

### Utilities
- **NLog** (5.2.8) - Structured logging
- **Newtonsoft.Json** (13.0.3) - JSON serialization

## Troubleshooting

### Issue: ".NET SDK not found"
**Solution:** 
1. Download and install .NET SDK: https://dotnet.microsoft.com/download
2. Restart PowerShell/VS Code
3. Run `dotnet --version` to verify

### Issue: NuGet package download fails
**Solution:**
1. Clear NuGet cache:
   ```powershell
   dotnet nuget locals all --clear
   ```
2. Try restore again:
   ```powershell
   dotnet restore
   ```

### Issue: Avalonia designer not showing in Visual Studio
**Solution:**
1. VS 2022 might not support Avalonia designer yet. Use the XAML preview files instead.
2. Alternatively, edit XAML by hand (fully supported)

### Issue: Missing Tesseract or Ghostscript
**Solution:**
1. These are optional for basic functionality
2. Install them via package managers:
   ```powershell
   # Using Chocolatey (if installed)
   choco install ghostscript tesseract
   ```

## Next Steps

1. **Implement Core Features** (Week 1-2)
   - [ ] Basic PDF viewing with Pdfium.Net
   - [ ] Text extraction from PDFs
   - [ ] Page navigation UI

2. **Add Image Processing** (Week 3-4)
   - [ ] PDF → Image conversion
   - [ ] Image → PDF conversion
   - [ ] Format conversion

3. **Integrate OCR** (Week 5-6)
   - [ ] Implement IOcrEngine interface
   - [ ] Add OCR text layer creation
   - [ ] Language support

4. **ClawPDF Integration** (Week 7-8)
   - [ ] Implement ClawPDFWrapper methods
   - [ ] Virtual printer functionality
   - [ ] Merge documents feature

5. **Polish & Distribution** (Week 9+)
   - [ ] Unit tests
   - [ ] Error handling
   - [ ] Create installer (WiX)
   - [ ] Documentation

## Additional Resources

- **Avalonia Documentation**: https://docs.avaloniaui.net/
- **iText7 Documentation**: https://itextpdf.com/en/products/itext-7/itext-7-core
- **ReactiveUI Documentation**: https://www.reactiveui.net/docs
- **clawPDF Repository**: https://github.com/clawsoftware/clawPDF
- **NLog Documentation**: https://nlog-project.org/

## License

This project is licensed under **AGPL v3** due to its use of clawPDF, iText7, and Ghostscript.
