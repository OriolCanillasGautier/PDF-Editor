param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$PublishDir = "$PSScriptRoot\..\artifacts\publish",
  # Override the version embedded in the MSI.
  # Leave empty to auto-read from Directory.Build.props (recommended).
  [string]$AppVersion = ""
)

# Get-Command can return aliases/functions that lack a .Source property.
# Only use .Source when the resolved command is an actual executable.
function Resolve-ExeCommand([string]$Name) {
  $cmd = Get-Command $Name -ErrorAction SilentlyContinue
  if ($cmd -and $cmd.CommandType -eq 'Application') { return $cmd.Source }
  return $null
}

$wixExe = Resolve-ExeCommand 'wix'
if (-not $wixExe) {
  $candidatePaths = @(
    "$env:ProgramFiles\WiX Toolset v4.0\bin\wix.exe",
    "${env:ProgramFiles(x86)}\WiX Toolset v4.0\bin\wix.exe"
  )

  foreach ($candidate in $candidatePaths) {
    if (Test-Path $candidate) {
      $wixExe = $candidate
      break
    }
  }
}

$installerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDirFull = (Resolve-Path (New-Item -ItemType Directory -Force -Path $PublishDir)).Path

# ─── Resolve app version ──────────────────────────────────────────────────────
if (-not $AppVersion) {
  $propsFile = "$PSScriptRoot\..\Directory.Build.props"
  $AppVersion = ([xml](Get-Content $propsFile)).Project.PropertyGroup.Version
  Write-Host "Version read from Directory.Build.props: $AppVersion"
} else {
  Write-Host "Using supplied version: $AppVersion"
}

Write-Host "Publishing to $publishDirFull"

# Workaround: dotnet publish fails with spaces in path (MSBuild limitation on Windows).
# Publish to temp directory first, then copy to final location.
$tempPubDir = Join-Path $env:TEMP "pdf-editor-publish-$([System.IO.Path]::GetRandomFileName())"
New-Item -ItemType Directory -Force -Path $tempPubDir | Out-Null

try {
  Write-Host "  (using temp: $tempPubDir)"
  dotnet publish "$PSScriptRoot\..\src\PDFEditor.UI\PDFEditor.UI.csproj" -c $Configuration -r $Runtime --self-contained true -o "$tempPubDir"
  
  # Copy published files to final location
  New-Item -ItemType Directory -Force -Path $publishDirFull | Out-Null
  Copy-Item -Path "$tempPubDir\*" -Destination $publishDirFull -Recurse -Force
  Write-Host "  Published to: $publishDirFull"
} finally {
  if (Test-Path $tempPubDir) { Remove-Item $tempPubDir -Recurse -Force }
}

# ─── Build & bundle pdf2docx-cli sidecar ─────────────────────────────────────
$sidecarExe = Join-Path $publishDirFull "pdf2docx-cli.exe"
if (-not (Test-Path $sidecarExe)) {
  Write-Host "`n==> Building pdf2docx-cli sidecar..."
  $toolDir = "$PSScriptRoot\..\tools\pdf2docx-cli"
  $distExe = "$toolDir\dist\pdf2docx-cli.exe"

  # Prefer a pre-built exe (dev workflow: run build.ps1 once, reuse it)
  if (-not (Test-Path $distExe)) {
    $py = (Get-Command python3 -ErrorAction SilentlyContinue).Source `
          ?? (Get-Command python  -ErrorAction SilentlyContinue).Source
    if ($py) {
      & $py -m pip install pyinstaller --quiet
      & $py -m pip install "$PSScriptRoot\..\tools\pdf2docx-cli\.." --quiet 2>$null  # no-op if already installed
      & $py -m pip install 'https://github.com/ArtifexSoftware/pdf2docx/releases/download/v0.5.9/pdf2docx-0.5.9-py3-none-any.whl' --quiet
      & $py -m PyInstaller --onefile --name pdf2docx-cli `
            --distpath "$toolDir\dist" --workpath "$toolDir\build" `
            --specpath "$toolDir" --clean --noconfirm "$toolDir\main.py"
    } else {
      Write-Warning "Python not found; skipping sidecar build. DOCX export will use the iText7 fallback."
    }
  }

  if (Test-Path $distExe) {
    Copy-Item $distExe $publishDirFull
    $sizeMb = [math]::Round((Get-Item $sidecarExe).Length / 1MB, 1)
    Write-Host "  Sidecar bundled: pdf2docx-cli.exe ($sizeMb MB)"
  }
} else {
  Write-Host "  Sidecar already present in publish dir, skipping rebuild."
}

$msiPath = "$PSScriptRoot\PDFEditor-$Configuration-$Runtime.msi"

if ($wixExe) {
  & $wixExe build "$installerDir\PDFEditor.Installer.wxs" -dPublishDir=$publishDirFull -dAppVersion=$AppVersion -ext WixToolset.UI.wixext -o $msiPath
  Write-Host "MSI created: $msiPath"
  exit 0
}

$candleExe = Resolve-ExeCommand 'candle'
$lightExe  = Resolve-ExeCommand 'light'
$heatExe   = Resolve-ExeCommand 'heat'

if (-not ($candleExe -and $lightExe -and $heatExe)) {
  $wixV3Dirs = @(
    "${env:ProgramFiles(x86)}\WiX Toolset v3.14\bin",
    "$env:ProgramFiles\WiX Toolset v3.14\bin",
    "${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin",
    "$env:ProgramFiles\WiX Toolset v3.11\bin"
  )

  foreach ($dir in $wixV3Dirs) {
    if (-not $candleExe) {
      $candidate = Join-Path $dir "candle.exe"
      if (Test-Path $candidate) { $candleExe = $candidate }
    }
    if (-not $lightExe) {
      $candidate = Join-Path $dir "light.exe"
      if (Test-Path $candidate) { $lightExe = $candidate }
    }
    if (-not $heatExe) {
      $candidate = Join-Path $dir "heat.exe"
      if (Test-Path $candidate) { $heatExe = $candidate }
    }
  }
}

if (-not ($candleExe -and $lightExe -and $heatExe)) {
  throw "WiX Toolset v4 was not found, and WiX v3 tools are not available. Install WiX v4 or v3 and reopen the terminal."
}

$objDir = Join-Path $installerDir "obj"
New-Item -ItemType Directory -Force -Path $objDir | Out-Null

$harvestedWxs = Join-Path $objDir "harvested.wxs"

& $heatExe dir "$publishDirFull" -cg PublishedFiles -dr INSTALLFOLDER -gg -sreg -srd -sfrag -out "$harvestedWxs"

$baseObj = Join-Path $objDir "base.wixobj"
$harvestedObj = Join-Path $objDir "harvested.wixobj"

& $candleExe "$installerDir\PDFEditor.Installer.v3.wxs" "-dPublishDir=$publishDirFull" "-dAppVersion=$AppVersion" -ext WixUIExtension -out "$baseObj"
& $candleExe "$harvestedWxs" -out "$harvestedObj"

& $lightExe -ext WixUIExtension -out "$msiPath" -b "$publishDirFull" -b "$installerDir" "$baseObj" "$harvestedObj"

Write-Host "MSI created: $msiPath"
