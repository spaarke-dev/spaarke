#!/usr/bin/env python3
"""Cut each real invoice (set C) out of its downloaded filing into local/real-invoices/C-NN.pdf (git-ignored).

Raw filings are untrusted downloads: run with `python -I` and the pypdfium2 venv. Re-downloading is described in real_sources.py.
    python -I generator/cut_real_invoices.py
"""
import os
import sys

import pypdfium2 as pdfium

HERE = os.path.dirname(os.path.abspath(__file__))
ES = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import real_sources as S  # noqa: E402


def main():
    out_dir = os.path.join(ES, "local", "real-invoices")
    os.makedirs(out_dir, exist_ok=True)
    for cid, key, a, b, inv in S.INVOICES:
        src = pdfium.PdfDocument(os.path.join(ES, "local", "real-invoices-raw", S.FILINGS[key]["raw"]))
        dst = pdfium.PdfDocument.new()
        dst.import_pages(src, list(range(a - 1, b)))
        path = os.path.join(out_dir, cid + ".pdf")
        dst.save(path)
        print(cid, S.FILINGS[key]["firm"], inv, f"pages {a}-{b}", os.path.getsize(path), "bytes")


if __name__ == "__main__":
    main()
