#!/usr/bin/env bash
# PDF Editor – one-shot dev environment setup (Linux / macOS)
#
# Usage:
#   chmod +x install.sh
#   ./install.sh
#   ./install.sh --skip-tests
#   ./install.sh --skip-pdf2docx
#   ./install.sh --skip-build

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WHEEL_URL="https://github.com/ArtifexSoftware/pdf2docx/releases/download/v0.5.9/pdf2docx-0.5.9-py3-none-any.whl"

SKIP_BUILD=0
SKIP_TESTS=0
SKIP_PDF2DOCX=0

# parse flags
for arg in "$@"; do
  case $arg in
    --skip-build)     SKIP_BUILD=1 ;;
    --skip-tests)     SKIP_TESTS=1 ;;
    --skip-pdf2docx)  SKIP_PDF2DOCX=1 ;;
    -h|--help)
      echo "Usage: $0 [--skip-build] [--skip-tests] [--skip-pdf2docx]"
      exit 0
      ;;
  esac
done

step()  { echo; echo "==> $*"; }
ok()    { echo "    OK  $*"; }
warn()  { echo "    WARN $*"; }

cd "$REPO_ROOT"

# ── 1. .NET SDK ───────────────────────────────────────────────────────────────
step "Checking .NET SDK"
if ! command -v dotnet &>/dev/null; then
  echo "    FAIL .NET SDK not found."
  echo "    Install from: https://dotnet.microsoft.com/download"
  exit 1
fi
DOTNET_VER=$(dotnet --version)
DOTNET_MAJOR=$(echo "$DOTNET_VER" | cut -d. -f1)
if [ "$DOTNET_MAJOR" -lt 6 ]; then
  echo "    FAIL .NET SDK $DOTNET_VER found but version 6+ is required."
  exit 1
fi
ok ".NET SDK $DOTNET_VER"

# ── 2. Python (optional) ──────────────────────────────────────────────────────
PYTHON_EXE=""
if [ "$SKIP_PDF2DOCX" -eq 0 ]; then
  step "Checking Python 3.8+"
  for cmd in python3 python; do
    if command -v "$cmd" &>/dev/null; then
      ver=$("$cmd" --version 2>&1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)
      major=$(echo "$ver" | cut -d. -f1)
      minor=$(echo "$ver" | cut -d. -f2)
      if [ "$major" -ge 3 ] && [ "$minor" -ge 8 ]; then
        PYTHON_EXE="$cmd"
        ok "Python $ver → $cmd"
        break
      fi
    fi
  done
  if [ -z "$PYTHON_EXE" ]; then
    warn "Python 3.8+ not found — skipping pdf2docx installation."
    echo "    Install Python from https://www.python.org/downloads/"
    SKIP_PDF2DOCX=1
  fi
fi

# Linux: install system fonts if missing (needed by Magick.NET / ElectronicSignatureService)
if [ "$(uname -s)" = "Linux" ] && command -v apt-get &>/dev/null; then
  step "Installing system fonts (Linux only)"
  sudo apt-get update -qq
  sudo apt-get install -y \
    fontconfig libfontconfig1 libgdiplus \
    fonts-dejavu-core fonts-dejavu-extra \
    fonts-liberation fonts-freefont-ttf 2>/dev/null || warn "apt-get failed — skipping font install"
  fc-cache -fv &>/dev/null
  ok "Fonts installed"
fi

# ── 3. NuGet restore ─────────────────────────────────────────────────────────
step "Restoring NuGet packages"
dotnet restore
ok "NuGet restore complete"

# ── 4. build ──────────────────────────────────────────────────────────────────
if [ "$SKIP_BUILD" -eq 0 ]; then
  step "Building solution (Release)"
  dotnet build --configuration Release --no-restore
  ok "Build succeeded"
fi

# ── 5. pdf2docx 0.5.9 ────────────────────────────────────────────────────────
if [ "$SKIP_PDF2DOCX" -eq 0 ]; then
  step "Installing pdf2docx 0.5.9 (high-fidelity PDF→DOCX backend)"
  echo "    Source: $WHEEL_URL"

  # check if already at the right version
  ALREADY=""
  ALREADY=$("$PYTHON_EXE" -m pip show pdf2docx 2>/dev/null | grep "Version: 0\.5\.9" || true)
  if [ -n "$ALREADY" ]; then
    ok "pdf2docx 0.5.9 already installed"
  else
    "$PYTHON_EXE" -m pip install "$WHEEL_URL" --quiet \
      && ok "pdf2docx 0.5.9 installed" \
      || warn "pdf2docx installation failed — DOCX export will use the built-in iText7 fallback."
  fi
fi

# ── 6. tests ──────────────────────────────────────────────────────────────────
if [ "$SKIP_TESTS" -eq 0 ]; then
  step "Running test suite"
  echo "    This may take a minute..."
  dotnet test --configuration Release --no-build --verbosity normal --blame-hang-timeout 120s \
    && ok "All tests passed" \
    || warn "Some tests failed — check output above."
fi

# ── done ──────────────────────────────────────────────────────────────────────
step "Setup complete!"
echo ""
echo "  Run the app:   dotnet run --project src/PDFEditor.UI/PDFEditor.UI.csproj"
echo "  Open in IDE:   open PDFEditor.sln  (or code .)"
echo "  Run tests:     dotnet test"
echo ""
