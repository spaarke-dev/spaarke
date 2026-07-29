#!/usr/bin/env python3
"""
WS-5 spike (task 050) — extract a page/line map from a LibreOffice-headless PDF.

Depends on the `pdftotext` CLI (poppler) — a measurement tool used only for this throwaway
spike, NOT proposed as a BFF/production dependency (NFR-04: no pagination package is added
to the BFF publish; this script never runs inside the BFF process).

Page boundaries: pdftotext (poppler) emits one form-feed (\\f, U+000C) character per PDF page,
including after the final page — so `count('\\f')` (NOT `+1`) is the true page count. Verified
empirically against the corpus (see ws5-libreoffice-spike.md Methodology note).

Line numbers: only present for docs whose OOXML enables `w:sectPr/w:lnNumType` (e.g.
line-numbered-pleading.docx). LibreOffice renders Word's line-number margin as visible text in
the PDF; each rendered text line therefore begins with "<N> <line text>" and can be regexed out.
Docs without `w:lnNumType` show no such prefix (confirmed: nda-interrupted-clauses.docx etc. carry
no line-number column in their LibreOffice PDF).

Usage: python3 page-line-map.py <pdf-path>
"""
import re
import subprocess
import sys


def page_line_map(pdf_path: str) -> None:
    raw = subprocess.run(
        ["pdftotext", "-layout", pdf_path, "-"],
        capture_output=True, text=True, check=True,
    ).stdout
    pages = raw.split("\f")
    # pdftotext emits a trailing \f after the LAST page too, so len(pages) == n_pages + 1
    # (final element is the empty tail after the last \f).
    n_pages = raw.count("\f")
    print(f"{pdf_path}: {n_pages} page(s)")

    for i, page in enumerate(pages[:n_pages], start=1):
        lines_with_numbers = []
        for line in page.split("\n"):
            m = re.match(r"^\s*(\d+)\s+(.*)$", line)
            if m:
                lineno, text = m.groups()
                lines_with_numbers.append((lineno, text.strip()))
        if lines_with_numbers:
            print(f"  page {i}: {len(lines_with_numbers)} numbered line(s) — "
                  f"first={lines_with_numbers[0][0]!r} last={lines_with_numbers[-1][0]!r}")
        else:
            first_line = next((l for l in page.split("\n") if l.strip()), "")
            print(f"  page {i}: (no rendered line numbers) starts: {first_line[:70]!r}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(1)
    page_line_map(sys.argv[1])
