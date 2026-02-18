param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$PublishDir = "$PSScriptRoot\..\artifacts\publish",
  # Override the version embedded in the MSI.
  # Leave empty to auto-read from Directory.Build.props (recommended).
  [string]$AppVersion = ""
)

$wixExe = (Get-Command wix -ErrorAction SilentlyContinue).Source
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

dotnet publish "$PSScriptRoot\..\src\PDFEditor.UI\PDFEditor.UI.csproj" -c $Configuration -r $Runtime --self-contained true -o $publishDirFull

$msiPath = "$PSScriptRoot\PDFEditor-$Configuration-$Runtime.msi"

if ($wixExe) {
  & $wixExe build "$installerDir\PDFEditor.Installer.wxs" -dPublishDir=$publishDirFull -dAppVersion=$AppVersion -ext WixToolset.UI.wixext -o $msiPath
  Write-Host "MSI created: $msiPath"
  exit 0
}

$candleExe = (Get-Command candle -ErrorAction SilentlyContinue).Source
$lightExe = (Get-Command light -ErrorAction SilentlyContinue).Source
$heatExe = (Get-Command heat -ErrorAction SilentlyContinue).Source

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

& $heatExe dir $publishDirFull -cg PublishedFiles -dr INSTALLFOLDER -gg -sreg -srd -sfrag -out $harvestedWxs

$baseObj = Join-Path $objDir "base.wixobj"
$harvestedObj = Join-Path $objDir "harvested.wixobj"

& $candleExe -out $baseObj "$installerDir\PDFEditor.Installer.v3.wxs" -dAppVersion=$AppVersion
& $candleExe -out $harvestedObj $harvestedWxs

& $lightExe -ext WixUIExtension -out $msiPath -b publishDir=$publishDirFull -b $publishDirFull -b $installerDir $baseObj $harvestedObj

Write-Host "MSI created: $msiPath"
