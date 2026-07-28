#!/usr/bin/env bash
# WS-5 spike (task 050) — LibreOffice headless out-of-process pagination prototype.
#
# Drives `soffice --headless` as a SEPARATE PROCESS (never linked/imported into the BFF —
# NFR-03 / ADR Tension T-1 Path A) to convert every corpus .docx to PDF using LibreOffice's
# own layout engine. The PDF is the page/line map: page boundaries are explicit page objects;
# line numbers (only for docs with `w:sectPr/w:lnNumType` enabled) are rendered as visible text
# extractable from the PDF text layer via `pdftotext` (poppler — a measurement tool used ONLY in
# this throwaway spike, not proposed as a BFF dependency).
#
# Usage: ./convert-corpus.sh <path-to-soffice.exe> <corpus-dir> <out-dir>
# Example (Windows, from Git Bash):
#   ./convert-corpus.sh "/c/Program Files/LibreOffice/program/soffice.exe" \
#     "../../../../tests/fixtures/compose-corpus" "./pdf-out"

set -euo pipefail

SOFFICE="${1:?soffice.exe path required}"
CORPUS="${2:?corpus dir required}"
OUTDIR="${3:?output dir required}"
PROFILE_DIR="$(mktemp -d)"  # isolated, throwaway LibreOffice user profile — deleted after run

mkdir -p "$OUTDIR"

for f in "$CORPUS"/*.docx; do
  echo "=== $(basename "$f") ==="
  "$SOFFICE" --headless --norestore \
    -env:UserInstallation="file:///$(cygpath -m "$PROFILE_DIR" 2>/dev/null || echo "$PROFILE_DIR")" \
    --convert-to pdf --outdir "$OUTDIR" "$f"
done

rm -rf "$PROFILE_DIR"
echo "Done. PDFs in $OUTDIR — run page-line-map.py against them to extract the page/line map."
