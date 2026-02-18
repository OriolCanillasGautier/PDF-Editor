# PDF Editor Documentation

Welcome to the **PDF Editor** developer documentation — an open-source, cross-platform PDF editor
built with C# / .NET 6, Avalonia UI, and iText7.

## Quick Links

- [Architecture Overview](ARCHITECTURE.md)
- [Roadmap](ROADMAP.md)
- [Setup Guide](../SETUP.md)
- [Contributing](../CONTRIBUTING.md)
- [API Reference](api/index.md)

## Getting Started

```bash
git clone https://github.com/OriolCanillasGautier/PDF-Editor.git
cd PDF-Editor
dotnet restore
dotnet build
dotnet run --project src/PDFEditor.UI/PDFEditor.UI.csproj
```

## Core Features

| Feature | Service | Status |
|---------|---------|--------|
| PDF Viewing | `PdfRenderService` | ✅ |
| Annotations | `PdfAnnotationService` | ✅ |
| Forms | `PdfFormService` | ✅ |
| Digital Signatures | `PdfSignatureService` | ✅ |
| OCR | `TesseractOcrService` | ✅ |
| Redaction | `PdfRedactionService` | ✅ |
| Comparison | `PdfComparisonService` | ✅ |
| DOCX Export | `HybridDocxExportProvider` | ✅ |
| PDF Optimization | `PdfOptimizer` | ✅ |
| Plugins | `PluginManager` | ✅ |
| Cloud Storage | `ICloudStorageProvider` | 🔲 |

## License

**AGPL v3** — See [LICENSE](../LICENSE) for details.
