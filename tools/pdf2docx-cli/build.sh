#!/usr/bin/env bash
# Build pdf2docx-cli (Linux / macOS) with PyInstaller
#
# Output: tools/pdf2docx-cli/dist/pdf2docx-cli  (~80 MB)
# Copy next to PDFEditor.UI binary for zero-Python-install DOCX export.
#
# Usage:
#   chmod +x build.sh && ./build.sh
#   ./build.sh --python /usr/local/bin/python3.11

set -euo pipefail

TOOL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WHEEL_URL="https://github.com/ArtifexSoftware/pdf2docx/releases/download/v0.5.9/pdf2docx-0.5.9-py3-none-any.whl"
PYTHON_EXE=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --python) PYTHON_EXE="$2"; shift 2 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

# ── find Python ───────────────────────────────────────────────────────────────
if [ -z "$PYTHON_EXE" ]; then
    for cmd in python3 python; do
        if command -v "$cmd" &>/dev/null; then
            ver=$("$cmd" --version 2>&1 | grep -oE '[0-9]+\.[0-9]+' | head -1)
            major=$(echo "$ver" | cut -d. -f1)
            minor=$(echo "$ver" | cut -d. -f2)
            if [ "$major" -ge 3 ] && [ "$minor" -ge 8 ]; then
                PYTHON_EXE=$(command -v "$cmd")
                break
            fi
        fi
    done
fi
[ -z "$PYTHON_EXE" ] && { echo "ERROR: Python 3.8+ not found."; exit 1; }
echo "==> Using Python: $PYTHON_EXE"

# ── install build tools ───────────────────────────────────────────────────────
echo "==> Installing pyinstaller and pdf2docx..."
"$PYTHON_EXE" -m pip install pyinstaller --quiet
"$PYTHON_EXE" -m pip install "$WHEEL_URL" --quiet

# ── build ─────────────────────────────────────────────────────────────────────
echo "==> Building pdf2docx-cli with PyInstaller..."
cd "$TOOL_DIR"
"$PYTHON_EXE" -m PyInstaller \
    --onefile \
    --name pdf2docx-cli \
    --distpath "$TOOL_DIR/dist" \
    --workpath "$TOOL_DIR/build" \
    --specpath "$TOOL_DIR" \
    --clean \
    --noconfirm \
    "$TOOL_DIR/main.py"

EXE="$TOOL_DIR/dist/pdf2docx-cli"
[ -f "$EXE" ] || { echo "ERROR: expected output not found: $EXE"; exit 1; }
SIZE=$(du -sh "$EXE" | cut -f1)
chmod +x "$EXE"
echo ""
echo "==> Build succeeded: $EXE ($SIZE)"
echo ""
echo "  Copy pdf2docx-cli next to the PDFEditor binary to enable"
echo "  high-fidelity PDF→DOCX conversion without requiring Python."
echo ""
