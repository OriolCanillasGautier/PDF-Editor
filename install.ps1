#!/usr/bin/env pwsh
<#
.SYNOPSIS
    PDF Editor – one-shot dev environment setup script (Windows / Linux / macOS)

.DESCRIPTION
    1. Checks prerequisites (.NET SDK 6+, Python 3.8+)
    2. Restores NuGet packages
    3. Builds the solution (Release)
    4. Installs pdf2docx 0.5.9 (pinned wheel – pure-Python, no compiler needed)
    5. Optionally runs all tests

.PARAMETER SkipBuild
    Skip the dotnet build step (useful if you only want to install pdf2docx).

.PARAMETER SkipTests
    Skip running the test suite after build.

.PARAMETER SkipPdf2Docx
    Skip the pdf2docx installation (e.g. no Python on this machine).

.EXAMPLE
    .\install.ps1
    .\install.ps1 -SkipTests
    .\install.ps1 -SkipPdf2Docx
#>
param(
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$SkipPdf2Docx
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── helpers ──────────────────────────────────────────────────────────────────
function Write-Step  { param($msg) Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Ok    { param($msg) Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Warn  { param($msg) Write-Host "    WARN $msg" -ForegroundColor Yellow }
function Write-Fail  { param($msg) Write-Host "    FAIL $msg" -ForegroundColor Red }

# ── 0. move to repo root ─────────────────────────────────────────────────────
$repoRoot = $PSScriptRoot
Push-Location $repoRoot

try {

# ── 1. prerequisite: .NET SDK ────────────────────────────────────────────────
Write-Step "Checking .NET SDK"
try {
    $dotnetVer = (dotnet --version 2>&1).Trim()
    $major = [int]($dotnetVer -split '\.')[0]
    if ($major -lt 6) {
        Write-Fail ".NET SDK $dotnetVer found but version 6+ is required."
        Write-Host "    Download: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
        exit 1
    }
    Write-Ok ".NET SDK $dotnetVer"
} catch {
    Write-Fail ".NET SDK not found. Install from https://dotnet.microsoft.com/download"
    exit 1
}

# ── 2. prerequisite: Python (optional) ───────────────────────────────────────
$pythonExe = $null
if (-not $SkipPdf2Docx) {
    Write-Step "Checking Python 3.8+"
    foreach ($cmd in @("python3", "python")) {
        try {
            $ver = (& $cmd --version 2>&1).Trim()
            if ($ver -match "Python (\d+)\.(\d+)") {
                $maj = [int]$matches[1]; $min = [int]$matches[2]
                if ($maj -ge 3 -and $min -ge 8) {
                    $pythonExe = $cmd
                    Write-Ok "$ver → $cmd"
                    break
                }
            }
        } catch { }
    }
    if (-not $pythonExe) {
        Write-Warn "Python 3.8+ not found — skipping pdf2docx installation."
        Write-Host "    Install Python from https://www.python.org/downloads/" -ForegroundColor Yellow
        Write-Host "    Then re-run: .\install.ps1 (or just: pip install <wheel>)" -ForegroundColor Yellow
        $SkipPdf2Docx = $true
    }
}

# ── 3. NuGet restore ─────────────────────────────────────────────────────────
Write-Step "Restoring NuGet packages"
dotnet restore
Write-Ok "NuGet restore complete"

# ── 4. build ──────────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Step "Building solution (Release)"
    dotnet build --configuration Release --no-restore
    Write-Ok "Build succeeded"
}

# ── 5. pdf2docx 0.5.9 ────────────────────────────────────────────────────────
$wheelUrl = "https://github.com/ArtifexSoftware/pdf2docx/releases/download/v0.5.9/pdf2docx-0.5.9-py3-none-any.whl"

if (-not $SkipPdf2Docx) {
    Write-Step "Installing pdf2docx 0.5.9 (high-fidelity PDF→DOCX backend)"
    Write-Host "    Source: $wheelUrl" -ForegroundColor DarkGray

    # check if already installed at the right version
    $alreadyInstalled = $false
    try {
        $installed = (& $pythonExe -m pip show pdf2docx 2>&1) -join " "
        if ($installed -match "Version: 0\.5\.9") {
            $alreadyInstalled = $true
            Write-Ok "pdf2docx 0.5.9 already installed"
        }
    } catch { }

    if (-not $alreadyInstalled) {
        & $pythonExe -m pip install "$wheelUrl" --quiet
        if ($LASTEXITCODE -eq 0) {
            Write-Ok "pdf2docx 0.5.9 installed"
        } else {
            Write-Warn "pdf2docx installation failed — DOCX export will use the built-in iText7 fallback."
        }
    }
}

# ── 6. build pdf2docx-cli sidecar (optional) ─────────────────────────────────
Write-Step "Building pdf2docx-cli sidecar exe (optional, ~80 MB)"
Write-Host "    This bundles CPython + pdf2docx into a standalone exe." -ForegroundColor DarkGray
Write-Host "    Copy it next to PDFEditor.UI.exe to enable DOCX export without Python." -ForegroundColor DarkGray

$buildSidecar = $true
if ($SkipPdf2Docx) {
    Write-Warn "Skipping sidecar build (--skip-pdf2docx was set)."
    $buildSidecar = $false
}

if ($buildSidecar) {
    try {
        Write-Host "    Installing PyInstaller..." -ForegroundColor DarkGray
        & $pythonExe -m pip install pyinstaller --quiet
        & "$repoRoot\tools\pdf2docx-cli\build.ps1" -PythonExe $pythonExe
        $distExe = "$repoRoot\tools\pdf2docx-cli\dist\pdf2docx-cli.exe"
        if (Test-Path $distExe) {
            Write-Ok "Sidecar built: $distExe"
            Write-Host "    To ship it: copy pdf2docx-cli.exe next to PDFEditor.UI.exe" -ForegroundColor DarkGray
        }
    } catch {
        Write-Warn "Sidecar build failed: $_"
    }
}

# ── 7. tests (optional) ───────────────────────────────────────────────────────
if (-not $SkipTests) {
  Write-Step "Running test suite"
  Write-Host "    This may take a minute..." -ForegroundColor DarkGray
  dotnet test --configuration Release --no-build --verbosity normal --blame-hang-timeout 120s
  if ($LASTEXITCODE -eq 0) {
    Write-Ok "All tests passed"
  } else {
    Write-Warn "Some tests failed (exit code $LASTEXITCODE). Check output above."
  }
}

# ── done ──────────────────────────────────────────────────────────────────────
Write-Step "Setup complete!"
Write-Host ""
Write-Host "  Run the app:   dotnet run --project src/PDFEditor.UI/PDFEditor.UI.csproj" -ForegroundColor White
Write-Host "  Open in IDE:   start PDFEditor.sln" -ForegroundColor White
Write-Host "  Run tests:     dotnet test" -ForegroundColor White
Write-Host ""

} finally {
    Pop-Location
}
