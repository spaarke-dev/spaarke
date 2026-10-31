#!/usr/bin/env python3
"""PII scan for evaluation-set documents (ADR-015 pre-prompt check).

Usage:  python -I pii_scan.py <file.pdf|file.txt|file.html|file.docx> [...]   (JSON to stdout)

Counts regex hits per category; NEVER prints matched text (so output is safe to paste into the
manifest). Reads PDFs through the `pdftotext` binary, docx through zipfile. Exit code 0 always;
the caller decides the disposition. Evaluation tooling only - no runtime dependency on the BFF.

Categories (counts only):
  email, phone_us, ssn, card_number, dob_label, iban, street_address, case_party_marker
`case_party_marker` counts phrases that usually precede a private person's name in narratives
("patient", "plaintiff", "claimant", "employee", "Mr.", "Ms.", "Mrs.") so a reviewer knows
whether narratives need redaction before use in a prompt.
"""
import json
import re
import subprocess
import sys
import zipfile
from datetime import date

PATTERNS = {
    "email": re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}"),
    "phone_us": re.compile(r"(?<!\d)(?:\+?1[ .-]?)?\(?\d{3}\)?[ .-]\d{3}[ .-]\d{4}(?!\d)"),
    "ssn": re.compile(r"(?<!\d)\d{3}-\d{2}-\d{4}(?!\d)"),
    "card_number": re.compile(r"(?<!\d)(?:\d[ -]?){13,16}(?!\d)"),
    "dob_label": re.compile(r"\b(?:DOB|date of birth|born on)\b", re.I),
    "iban": re.compile(r"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b"),
    "street_address": re.compile(
        r"\b\d{1,5}\s+(?:[A-Z][a-z]+\s+){1,3}(?:Street|St\.|Avenue|Ave\.|Road|Rd\.|Boulevard|Blvd\.|Drive|Dr\.|Lane|Ln\.|Way|Court|Ct\.)\b"
    ),
    "case_party_marker": re.compile(r"\b(?:patient|plaintiff|claimant|employee|Mr\.|Ms\.|Mrs\.)\b", re.I),
}


def read_text(path: str) -> str:
    low = path.lower()
    if low.endswith(".pdf"):
        out = subprocess.run(["pdftotext", "-layout", path, "-"], capture_output=True, timeout=120)
        return out.stdout.decode("utf-8", errors="ignore")
    if low.endswith(".docx"):
        with zipfile.ZipFile(path) as z:
            xml = z.read("word/document.xml").decode("utf-8", errors="ignore")
        return re.sub(r"<[^>]+>", " ", xml.replace("</w:p>", "\n"))
    with open(path, "rb") as fh:
        raw = fh.read().decode("utf-8", errors="ignore")
    if low.endswith((".html", ".htm")):
        raw = re.sub(r"<[^>]+>", " ", raw)
    return raw


def scan(path: str) -> dict:
    text = read_text(path)
    counts = {k: len(p.findall(text)) for k, p in PATTERNS.items()}
    return {
        "file": path.replace("\\", "/").rsplit("/", 1)[-1],
        "tool": "pii_scan.py@1",
        "scanned": date.today().isoformat(),
        "chars": len(text),
        "counts": counts,
    }


if __name__ == "__main__":
    print(json.dumps([scan(p) for p in sys.argv[1:]], indent=2))
