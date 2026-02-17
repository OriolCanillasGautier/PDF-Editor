# PDF Editor Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added
- Project initialization and scaffolding
- Core architecture and design
- PDF document interface (IPdfDocument)
- iText7-based PDF service
- Avalonia UI framework setup
- Dependency injection configuration
- Comprehensive documentation

### TODO
- Implement PDF rendering with Pdfium.Net
- Add image processing capabilities
- Integrate OCR engine
- Implement clawPDF wrapper
- Create UI components

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
