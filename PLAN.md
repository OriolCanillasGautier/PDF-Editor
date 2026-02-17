# PDF Editor - Improvement Plan

This document outlines the planned improvements and features for the PDF Editor project.

---

## 1. About Dialog - GitHub Link

**Priority:** High  
**Status:** Pending

### Current State
The About dialog is very basic with no links to external resources.

### Required Changes

Update the `OnAboutClick` method in `MainWindow.axaml.cs` to include:
- Application version information
- Link to GitHub repository: https://github.com/OriolCanillasGautier/PDF-Editor
- Link to license information
- Credits for used libraries (Avalonia UI, iText7, Docnet, etc.)

### Implementation
```csharp
// Add button with click handler that opens GitHub URL
System.Diagnostics.Process.Start(new ProcessStartInfo
{
    FileName = "https://github.com/OriolCanillasGautier/PDF-Editor",
    UseShellExecute = true
});
```

---

## 2. Enhanced Export System

**Priority:** High  
**Status:** Partial Implementation

### Current State
The current export system (`PdfExportService.cs`) supports:
- PDF → Images (PNG, JPEG, TIFF, BMP, WebP)
- PDF → Plain Text
- PDF → HTML (visual, images embedded as base64)

### Required Improvements

#### 2.1 Architecture Refactor

Create a provider-based export system for extensibility:

**New Interface** (`PDFEditor.Core/Abstractions/IExportProvider.cs`):
```csharp
public interface IExportProvider
{
    string FormatName { get; }
    string[] SupportedExtensions { get; }
    Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options);
    bool SupportsBatch { get; }
}

public class ExportResult
{
    public byte[] Data { get; set; }
    public string FileName { get; set; }
    public string MimeType { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
}

public class ExportOptions
{
    public int Dpi { get; set; } = 150;
    public int Quality { get; set; } = 90;
    public int[] PageIndices { get; set; }
    public string OutputFormat { get; set; }
}
```

#### 2.2 New Export Providers to Implement

| Provider | Format | Library | Status |
|----------|--------|---------|--------|
| `ImageExportProvider` | PNG, JPEG, TIFF, BMP, WebP | Magick.NET | Existing (refactor) |
| `TextExportProvider` | TXT, Markdown | iText7 | Existing (refactor) |
| `HtmlExportProvider` | HTML (structured) | iText7 | Existing (improve) |
| `DocxExportProvider` | DOCX | DocumentFormat.OpenXml + iText7 | **New** |
| `OdtExportProvider` | ODT | AODL / ODF | **New** |
| `RtfExportProvider` | RTF | Custom | **New** |
| `EpubExportProvider` | EPUB | iText7 + Custom | **New** |
| `XlsxExportProvider` | XLSX (tables) | DocumentFormat.OpenXml | **New** |

#### 2.3 DOCX Export Implementation

**Package to add:**
```xml
<PackageReference Include="DocumentFormat.OpenXml" Version="3.0.1" />
```

**Implementation outline** (`PDFEditor.Core/Services/Export/DocxExportProvider.cs`):
```csharp
public class DocxExportProvider : IExportProvider
{
    public string FormatName => "Microsoft Word (DOCX)";
    public string[] SupportedExtensions => new[] { ".docx" };
    public bool SupportsBatch => true;

    public Task<ExportResult> ExportAsync(byte[] pdfBytes, ExportOptions options)
    {
        using var ms = new MemoryStream();
        using var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());

        // Extract text with basic structure using iText7
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
        
        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
        {
            var strategy = new SimpleTextExtractionStrategy();
            var text = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i), strategy);
            
            var para = new Paragraph(text.Replace("\n", "\n\r"));
            mainPart.Document.Body?.AppendChild(para);
        }
        
        mainPart.Document.Save();
        return Task.FromResult(new ExportResult 
        { 
            Data = ms.ToArray(), 
            FileName = "export.docx",
            MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            Success = true
        });
    }
}
```

#### 2.4 Export Dialog UI

Create a new export dialog (`ExportDialog.axaml`) with:
- Format selection dropdown (populated from registered `IExportProvider` instances)
- Format-specific options panel (dynamic based on selected format)
- Page range selection (all, current, custom range)
- Quality/DPI settings for image exports
- Progress indicator for batch operations
- Preview option (where applicable)

---

## 3. UI/UX Improvements

**Priority:** High  
**Status:** Functional but needs polish

### 3.1 Theme & Styling

**Current Issues:**
- Basic styling without consistent design language
- Inline SVG icons difficult to maintain
- No animations or visual feedback
- Limited theme customization

**Required Changes:**

1. **Implement Fluent Design Theme:**
```xml
<!-- App.axaml -->
<Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://Avalonia.Themes.Fluent/Accents/Base.xaml"/>
    <StyleInclude Source="avares://Avalonia.Themes.Fluent/Accents/BaseLight.xaml"/>
</Application.Styles>
```

2. **Move Icons to External Resources:**
```
src/PDFEditor.UI/Resources/Icons/
├── file-open.svg
├── file-save.svg
├── file-print.svg
├── edit-undo.svg
├── edit-redo.svg
├── export.svg
├── annotation-*.svg
└── ...
```

3. **Add Subtle Animations:**
- Fade-in for notifications and dialogs
- Smooth transitions for panel collapse/expand
- Hover effects on buttons and menu items
- Loading spinners for async operations

4. **Improve Toolbar:**
- Visual grouping with subtle separators
- Better tooltips with keyboard shortcuts
- Collapsible groups for compact mode
- Contextual tool visibility based on current state

### 3.2 Layout Improvements

1. **Resizable Panels:**
- Ensure all GridSplitters work smoothly
- Save panel widths in user settings
- Add collapse/expand buttons for side panels

2. **Tab Improvements:**
- Show close button on hover
- Add "Close other tabs" context menu
- Show file icon in tab
- Indicate unsaved changes with asterisk

3. **Status Bar Enhancements:**
- Show operation progress
- Add quick zoom slider
- Show current tool mode prominently

### 3.3 Accessibility

- [ ] Full keyboard navigation for all controls
- [ ] Screen reader support (ARIA labels)
- [ ] High contrast mode
- [ ] Configurable font sizes
- [ ] Focus indicators on all interactive elements

---

## 4. Features Pending Implementation

### 4.1 OCR Integration (Partial)

**Status:** Tesseract NuGet installed, implementation incomplete

**Required:**
- [ ] Complete `IOcrEngine` implementation
- [ ] Multi-language support (ENG, SPA, FRA, DEU, CAT)
- [ ] UI for OCR operations:
  - Language selection dropdown
  - Progress indicator for large documents
  - Confidence score display
- [ ] Create searchable PDFs with embedded text layer
- [ ] Batch OCR processing

### 4.2 ClawPDF Integration (Partial)

**Status:** Wrapper class exists, not fully integrated

**Required:**
- [ ] Complete `ClawPDFWrapper` implementation
- [ ] Virtual printer detection and configuration
- [ ] Print-to-PDF functionality
- [ ] Command-line integration
- [ ] Print profiles/settings management
- [ ] Batch printing support

### 4.3 Security Features (Partial)

**Status:** Basic password protection implemented

**Required:**
- [ ] Digital signatures with certificate support
- [ ] Permanent redaction (not just visual overlay)
- [ ] Permission management UI (print, copy, edit, annotate)
- [ ] Encryption level selection (128-bit, 256-bit AES)
- [ ] Certificate management for signatures

### 4.4 Advanced Annotation Features

**Status:** Basic annotations working

**Required:**
- [ ] Annotation properties panel
- [ ] Annotation list/management view
- [ ] Export annotations to summary report
- [ ] Import/export annotations (XFDF format)
- [ ] Measurement tools (ruler, area, perimeter)
- [ ] Custom stamp creation

### 4.5 Form Handling

**Status:** Not implemented

**Required:**
- [ ] Fill interactive PDF forms
- [ ] Create form fields (text, checkbox, radio, dropdown, signature)
- [ ] Form field properties editor
- [ ] Form data import/export (FDF, XFDF, JSON)
- [ ] Form validation rules
- [ ] Flatten forms to static content

### 4.6 Document Comparison

**Status:** Not implemented

**Required:**
- [ ] Side-by-side document view
- [ ] Visual diff highlighting
- [ ] Change summary report
- [ ] Merge changes option

---

## 5. Performance Optimizations

### 5.1 Large Document Handling

- [ ] Implement virtual scrolling for page thumbnails
- [ ] Lazy loading for page renders
- [ ] Background rendering with cancellation support
- [ ] Memory-efficient caching strategy
- [ ] Progress reporting for operations on large files

### 5.2 Startup Performance

- [ ] Lazy load optional features
- [ ] Async initialization where possible
- [ ] Splash screen with progress
- [ ] Session restore optimization

---

## 6. Testing & Quality

### 6.1 Unit Tests

**Current:** Basic test project exists (`PDFEditor.Tests`)

**Required:**
- [ ] Core service tests (PDF operations, export, OCR)
- [ ] View model tests
- [ ] Edge case handling tests
- [ ] Target: 80%+ code coverage for core library

### 6.2 Integration Tests

- [ ] Full workflow tests
- [ ] File I/O tests with various PDF types
- [ ] Error recovery tests
- [ ] Performance benchmark tests

### 6.3 Manual Testing Checklist

- [ ] Open/save various PDF types (text, scanned, forms, encrypted)
- [ ] All export formats
- [ ] All annotation tools
- [ ] Undo/redo functionality
- [ ] Multi-document operations
- [ ] Keyboard shortcuts
- [ ] Theme switching
- [ ] Session save/restore

---

## 7. Documentation

### 7.1 User Documentation

- [ ] User manual (PDF + online)
- [ ] Keyboard shortcuts reference
- [ ] FAQ / Troubleshooting guide
- [ ] Video tutorials (optional)

### 7.2 Developer Documentation

- [ ] API documentation (XML comments → DocFX)
- [ ] Plugin development guide
- [ ] Contribution guidelines (update CONTRIBUTING.md)
- [ ] Architecture decision records (ADRs)

---

## 8. Distribution & Deployment

### 8.1 Installer Improvements

**Current:** WiX installer exists

**Required:**
- [ ] Auto-update mechanism
- [ ] Portable version option
- [ ] MSIX package for Windows Store
- [ ] Linux packages (.deb, .rpm)
- [ ] macOS package (.dmg)

### 8.2 CI/CD Pipeline

**Current:** GitHub Actions workflow exists (`.github/workflows/build.yml`)

**Required:**
- [ ] Automated testing on PR
- [ ] Automated release builds on tag
- [ ] Code quality checks (SonarQube or similar)
- [ ] Artifact publishing to GitHub Releases

---

## 9. Future Enhancements (Backlog)

### 9.1 Cloud Integration

- [ ] OneDrive/Google Drive/Dropbox integration
- [ ] Cloud sync for settings and sessions
- [ ] Share via link with permissions

### 9.2 Plugin System

- [ ] Plugin API definition
- [ ] Plugin manager UI
- [ ] Sample plugins (export formats, annotation tools)
- [ ] Plugin marketplace (optional)

### 9.3 Command-Line Interface

- [ ] CLI for batch operations
- [ ] Scripting support (PowerShell, C#)
- [ ] Watch folder for automatic processing

### 9.4 Collaboration Features

- [ ] Comment threading and replies
- [ ] Change tracking
- [ ] Real-time collaboration (optional, complex)

### 9.5 AI-Powered Features

- [ ] Smart text extraction with layout preservation
- [ ] Automatic form field detection
- [ ] Document classification
- [ ] Smart redaction suggestions

---

## 10. Implementation Priority

### Phase 1 (Immediate)
1. About dialog with GitHub link
2. Export system refactor with `IExportProvider`
3. DOCX export provider (basic)
4. UI theme improvements (Fluent)

### Phase 2 (Short-term)
1. Complete OCR implementation
2. Export dialog UI
3. Icon resource migration
4. Unit test coverage improvement

### Phase 3 (Medium-term)
1. ClawPDF full integration
2. Form handling
3. Digital signatures
4. Annotation management panel

### Phase 4 (Long-term)
1. Document comparison
2. Plugin system
3. Cloud integration
4. Mobile companion app (optional)

---

## Notes

- All new code should follow existing coding standards
- Maintain AGPL v3 license compatibility
- Keep cross-platform compatibility (Windows, Linux, macOS)
- Prioritize stability over new features
- Gather user feedback before major UI changes

---

**Last Updated:** February 18, 2026  
**Maintainer:** Oriol Canillas
