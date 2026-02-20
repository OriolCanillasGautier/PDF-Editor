#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build pdf2docx-cli.exe with PyInstaller (Windows).

.DESCRIPTION
    Creates a self-contained single-file exe that bundles CPython +
    pdf2docx + all its dependencies.  No Python installation required
    on the target machine.

    Output: tools\pdf2docx-cli\dist\pdf2docx-cli.exe  (~80 MB)
    Copy that file next to PDFEditor.UI.exe and HybridDocxExportProvider
    will automatically use it instead of requiring Python in PATH.

.PARAMETER PythonExe
    Path to Python 3.8+ executable. Defaults to auto-detect.

.PARAMETER WheelUrl
    pdf2docx wheel to install before building.
    Defaults to the pinned v0.5.9 release.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -PythonExe "C:\Python313\python.exe"
#>
param(
    [string]$PythonExe = "",
    [string]$WheelUrl  = "https://github.com/ArtifexSoftware/pdf2docx/releases/download/v0.5.9/pdf2docx-0.5.9-py3-none-any.whl"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$toolDir = $PSScriptRoot

# ── find Python ───────────────────────────────────────────────────────────────
if (-not $PythonExe) {
    foreach ($cmd in @("python3", "python")) {
        try {
            $exePath = (Get-Command $cmd -ErrorAction SilentlyContinue)
            if ($exePath -and $exePath.CommandType -eq 'Application') {
                $ver = (& $exePath.Source --version 2>&1).Trim()
                if ($ver -match "Python (\d+)\.(\d+)" -and [int]$matches[1] -ge 3 -and [int]$matches[2] -ge 8) {
                    $PythonExe = $exePath.Source
                    break
                }
            }
        } catch {}
    }
}
if (-not $PythonExe -or -not (Test-Path $PythonExe)) {
    throw "Python 3.8+ not found. Install from https://www.python.org/downloads/ and retry."
}
Write-Host "Using Python: $PythonExe" -ForegroundColor Cyan

# ── ensure pyinstaller & pdf2docx installed ───────────────────────────────────
Write-Host "`n==> Installing/updating build tools..." -ForegroundColor Cyan
& $PythonExe -m pip install pyinstaller --quiet
& $PythonExe -m pip install "$WheelUrl" --quiet
Write-Host "    OK" -ForegroundColor Green

# ── PyInstaller build ─────────────────────────────────────────────────────────
Write-Host "`n==> Building pdf2docx-cli.exe with PyInstaller..." -ForegroundColor Cyan

Push-Location $toolDir
try {
    & $PythonExe -m PyInstaller `
        --onefile `
        --name pdf2docx-cli `
        --distpath "$toolDir\dist" `
        --workpath "$toolDir\build" `
        --specpath "$toolDir" `
        --clean `
        --noconfirm `
        "$toolDir\main.py"

    if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed (exit $LASTEXITCODE)" }
} finally {
    Pop-Location
}

$exePath = "$toolDir\dist\pdf2docx-cli.exe"
if (-not (Test-Path $exePath)) {
    throw "Expected output not found: $exePath"
}

$sizeMb = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
Write-Host "`n==> Build succeeded: $exePath ($sizeMb MB)" -ForegroundColor Green
Write-Host ""
Write-Host "  Copy pdf2docx-cli.exe next to PDFEditor.UI.exe to enable" -ForegroundColor White
Write-Host "  high-fidelity PDF→DOCX conversion without requiring Python." -ForegroundColor White
Write-Host ""
