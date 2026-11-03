"""Writes one rate-card workbook (.xlsx, task 078 format: sheets `Timekeepers` and `Rates`) per reference-data firm KEY.

usage:  python -I build_ratecard_workbooks.py --out <folder> [--reference-data ../../evaluation-set/guidelines/reference-data]

One workbook per `key` (never per firm name): six firm names appear in two rule sets with different rosters and caps, and sharing a
workbook would approve one invoice's timekeepers for another (guidelines/README.md). Stdlib only (zipfile + inline strings); no openpyxl.
The output holds synthetic names only but is still written to a git-ignored folder (default evaluation-set/local/guidelines/ratecards).
Local file output only; no network. Not yet read back by RateCardImportService: the first import (a validate-only 400 lists every bad
row with nothing written) is the real check, and is part of the owner-approved seed run.
"""
from __future__ import annotations

import argparse
import json
import sys
import zipfile
from pathlib import Path
from xml.sax.saxutils import escape

sys.stdout.reconfigure(encoding="utf-8")
HERE = Path(__file__).resolve().parent
DEFAULT_REF = HERE.parents[1] / "evaluation-set" / "guidelines" / "reference-data"
DEFAULT_OUT = HERE.parents[1] / "evaluation-set" / "local" / "guidelines" / "ratecards"
NUMERIC = {"Approved Rate", "Rack Rate"}


def col(i: int) -> str:
    s = ""
    i += 1
    while i:
        i, r = divmod(i - 1, 26)
        s = chr(65 + r) + s
    return s


def sheet_xml(rows: list[dict]) -> str:
    headers = list(rows[0].keys()) if rows else []
    out = ['<?xml version="1.0" encoding="UTF-8" standalone="yes"?>',
           '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>']

    def cell(ref, v, numeric):
        if v is None or v == "":
            return ""
        if numeric:
            return f'<c r="{ref}"><v>{float(v)}</v></c>'
        return f'<c r="{ref}" t="inlineStr"><is><t>{escape(str(v))}</t></is></c>'

    out.append('<row r="1">' + "".join(cell(f"{col(i)}1", h, False) for i, h in enumerate(headers)) + "</row>")
    for n, r in enumerate(rows, start=2):
        out.append(f'<row r="{n}">' + "".join(cell(f"{col(i)}{n}", r.get(h), h in NUMERIC) for i, h in enumerate(headers)) + "</row>")
    out.append("</sheetData></worksheet>")
    return "".join(out)


def write_workbook(path: Path, timekeepers: list[dict], rates: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml",
                   '<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                   '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
                   '<Default Extension="xml" ContentType="application/xml"/>'
                   '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
                   '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
                   '<Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
                   "</Types>")
        z.writestr("_rels/.rels",
                   '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                   '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>'
                   "</Relationships>")
        z.writestr("xl/workbook.xml",
                   '<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
                   'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>'
                   '<sheet name="Timekeepers" sheetId="1" r:id="rId1"/><sheet name="Rates" sheetId="2" r:id="rId2"/></sheets></workbook>')
        z.writestr("xl/_rels/workbook.xml.rels",
                   '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                   '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>'
                   '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>'
                   "</Relationships>")
        z.writestr("xl/worksheets/sheet1.xml", sheet_xml(timekeepers))
        z.writestr("xl/worksheets/sheet2.xml", sheet_xml(rates))


def build_all(ref_dir: Path, out_dir: Path) -> dict:
    seen: dict[str, str] = {}
    written = 0
    for p in sorted(ref_dir.glob("RS-*.json")):
        ref = json.loads(p.read_text(encoding="utf-8"))
        for firm in ref["firms"]:
            key = firm["key"]
            if key in seen:
                raise SystemExit(f"duplicate firm key {key} in {p.name} and {seen[key]}: one record per key")
            seen[key] = p.name
            write_workbook(out_dir / f"{key}.xlsx", firm["workbook"]["Timekeepers"], firm["workbook"]["Rates"])
            written += 1
    return {"workbooks": written, "keys": len(seen)}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--reference-data", default=str(DEFAULT_REF))
    ap.add_argument("--out", default=str(DEFAULT_OUT))
    a = ap.parse_args()
    print(build_all(Path(a.reference_data), Path(a.out)))


if __name__ == "__main__":
    main()
