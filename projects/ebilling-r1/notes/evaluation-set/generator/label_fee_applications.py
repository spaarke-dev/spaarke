#!/usr/bin/env python3
"""Label the Chapter 11 fee applications (set A): header, timekeeper summary, line entries, reconciliation.

Run with the generator venv (needs pypdfium2) and `-I` because the inputs are untrusted downloads:
    python -I generator/label_fee_applications.py [A-03 ...]        # all eight by default

Method (label_method "parser+reconciliation"): a row parser rebuilds table rows from character rectangles
(pdfrows.py), one config per document says which pages and columns to read. The cover-sheet header values and the
timekeeper summary come from the filing's own summary tables. Every label records a reconciliation block: the sum of the
parsed entries compared with the totals the filing states, so a reader can see how far the parse is trusted.

Outputs
  ground-truth/fee-applications/A-NN.json   committed: header, timekeepers (aliases), up to 150 sampled lines with
                                            narrative_sha256/narrative_len instead of narrative text, reconciliation
  local/ground-truth-full/A-NN.json         local only: all lines with narratives and timekeeper names as billed
"""
import hashlib
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ES = os.path.dirname(HERE)
sys.path.insert(0, HERE)

import pdfrows  # noqa: E402

MAX_COMMITTED_LINES = 150
NUM = re.compile(r"^\(?\$?\s*-?[\d,]*\.?\d+\)?$")


def num(s):
    s = s.strip()
    neg = s.startswith("(") or s.startswith("-")
    s = re.sub(r"[^\d.]", "", s)
    if not s or s == ".":
        return None
    v = float(s)
    return -v if neg else v


def iso(d, century_cut=70):
    m = re.match(r"(\d{1,2})/(\d{1,2})/(\d{2,4})", d)
    if not m:
        return None
    mo, dy, y = int(m.group(1)), int(m.group(2)), int(m.group(3))
    if y < 100:
        y += 2000 if y < century_cut else 1900
    return f"{y:04d}-{mo:02d}-{dy:02d}"


def parse_entries(pdf, pages, cfg):
    """Walk rows of the given 1-based pages and build entries. cfg keys:
      date_re      regex for a date at the start of the first cell (group 1 = the date)
      date_x       (min, max) x0 of the date cell
      cols         {name: (x0min, x0max)} for cells after the date: tk, code, desc, hours, rate, amount (x0 of the cell)
      date_has_tk  True when the date cell also carries the timekeeper ("05/01/23 A. Bauer")
      stop_re      rows starting with this end the current entry (SUBTOTAL, TOTAL ...); their numbers are recorded
      section_re   rows matching this set the current task code and end the entry
      skip_re      rows to ignore (headers, footers)
      ymin         ignore rows below this y (page footers)
      start_re/end_re  gate: rows are read only after a row matching start_re until one matching end_re (state carries
                   across pages); the end row is recorded as a stop
    """
    import pypdfium2 as pdfium
    doc = pdfium.PdfDocument(pdf)
    date_re = re.compile(cfg["date_re"])
    stop_re = re.compile(cfg["stop_re"]) if cfg.get("stop_re") else None
    sec_re = re.compile(cfg["section_re"]) if cfg.get("section_re") else None
    skip_re = re.compile(cfg["skip_re"]) if cfg.get("skip_re") else None
    start_re = re.compile(cfg["start_re"]) if cfg.get("start_re") else None
    end_re = re.compile(cfg["end_re"]) if cfg.get("end_re") else None
    active = start_re is None
    entries, stops = [], []
    section = None
    for pno in pages:
        cur = None
        for y, cells in pdfrows.page_rows(doc[pno - 1]):
            if y < cfg.get("ymin", 40):
                continue
            first = cells[0][2]
            joined = " ".join(c[2] for c in cells)
            if start_re and not active:
                if start_re.search(joined):
                    active = True
                continue
            if end_re and active and end_re.search(joined):
                nums = [num(c[2]) for c in cells if NUM.match(c[2].replace(" ", ""))]
                stops.append({"page": pno, "text": joined[:60], "numbers": nums, "section": section})
                cur = None
                active = start_re is None
                continue
            if skip_re and skip_re.search(joined) and not date_re.match(first):
                continue
            if sec_re and sec_re.match(joined):
                section = sec_re.match(joined).group(1)
                cur = None
                continue
            if stop_re and stop_re.match(joined):
                nums = [num(c[2]) for c in cells if NUM.match(c[2].replace(" ", ""))]
                stops.append({"page": pno, "text": joined[:60], "numbers": nums, "section": section})
                cur = None
                continue
            m = date_re.match(first)
            if m and cfg["date_x"][0] <= cells[0][0] <= cfg["date_x"][1]:
                cur = {"page": pno, "date_raw": m.group(1), "section": section, "tk": "", "code": "", "desc": [], "hours": None,
                       "rate": None, "amount": None}
                entries.append(cur)
                rest = first[m.end():].strip()
                if cfg.get("date_has_tk") and rest:
                    cur["tk"] = rest
                cells = cells[1:]
            elif cur is None:
                continue
            for x0, x1, t in cells:
                col = None
                for name, (lo, hi) in cfg["cols"].items():
                    if lo <= x0 <= hi:
                        col = name
                        break
                if col is None:
                    continue
                if col == "desc":
                    cur["desc"].append(t)
                elif col in ("hours", "rate", "amount"):
                    v = num(t)
                    if v is not None and cur[col] is None:
                        cur[col] = v
                elif col in ("tk", "code") and not cur[col]:
                    cur[col] = t
    return entries, stops, doc


def sha(s):
    return hashlib.sha256(s.encode("utf-8")).hexdigest()


# ---------------------------------------------------------------------------------------------- summary tables
TITLE_RE = re.compile(r"(?i)\b(of counsel|senior associate|summer associate|associate|senior paralegal|paralegal|legal assistant|"
                      r"law clerk|project manager|shareholder|partner|member|counsel|attorney|information technology|"
                      r"special counsel|representative)\b")
MONEY = re.compile(r"^\(?\$?\s?[\d,]*\.\d\d\)?$")
DEC = re.compile(r"^\d+(\.\d+)?$|^\.\d+$")


def tk_rows(pdf, pages, c):
    """Timekeeper-summary rows. A row is a timekeeper row when it has a plain-number cell in c['hours'] and a money cell in
    c['total'] (x0 ranges). Rows with no name cell but a rate (second rate step) merge into the previous timekeeper.
    Title = nearest row within 32pt whose left cells match TITLE_RE (same row first)."""
    import pypdfium2 as pdfium
    doc = pdfium.PdfDocument(pdf)
    out, all_rows = [], []
    for pno in pages:
        for y, cells in pdfrows.page_rows(doc[pno - 1], ytol=c.get("ytol", 2.5)):
            all_rows.append((pno, y, cells))
    for idx, (pno, y, cells) in enumerate(all_rows):
        h = next((num(t) for x0, x1, t in cells if c["hours"][0] <= x0 <= c["hours"][1] and DEC.match(t.strip())), None)
        tot = next((num(t) for x0, x1, t in cells if c["total"][0] <= x0 <= c["total"][1] and MONEY.match(t.strip().replace(" ", ""))), None)
        if h is None or tot is None:
            continue
        rate = None
        if c.get("rate"):
            rate = next((num(t) for x0, x1, t in cells if c["rate"][0] <= x0 <= c["rate"][1] and MONEY.match(t.strip().replace(" ", "").rstrip("*"))), None)
        name = " ".join(t for x0, x1, t in cells if c["name"][0] <= x0 <= c["name"][1] and not TITLE_RE.fullmatch(t.strip())).strip()
        if re.search(r"(?i)\btotals?\b", name):
            continue
        title = None
        for dy in (0, 1, 2, 3):
            cand = []
            for pno2, y2, cells2 in all_rows:
                if pno2 != pno or abs(y2 - y) > 32:
                    continue
                if dy == 0 and abs(y2 - y) > 3:
                    continue
                for x0, x1, t in cells2:
                    m = TITLE_RE.search(t) if x0 < c.get("title_xmax", 330) else None
                    if m and not re.search(r"(?i)total|summary|blended", t):
                        cand.append((abs(y2 - y), m.group(1).title()))
            if cand:
                title = sorted(cand)[0][1]
                break
        out.append({"page": pno, "name": name, "title": title, "rate": rate, "hours": h, "total": tot})
    return out


def merge_tk_rows(rows):
    """Rows without a name belong to the previous timekeeper (a second rate step)."""
    merged = []
    for r in rows:
        if not r["name"] and merged:
            m = merged[-1]
            m["hours"] = round(m["hours"] + r["hours"], 2)
            m["total"] = round(m["total"] + r["total"], 2)
            if r["rate"] is not None:
                m["rates"].append(r["rate"])
            continue
        merged.append({"name": r["name"], "title": r["title"], "rates": [r["rate"]] if r["rate"] is not None else [],
                       "hours": r["hours"], "total": r["total"]})
    return merged


def initials_of(name):
    parts = re.sub(r"[^A-Za-z .'-]", "", name).replace(",", " ").split()
    return "".join(p[0].upper() for p in parts if p[0].isalpha())


# ---------------------------------------------------------------------------------------------- label assembly
def sampled(lines, k=MAX_COMMITTED_LINES):
    if len(lines) <= k:
        return lines
    step = len(lines) / float(k)
    return [lines[int(i * step)] for i in range(k)]


def recon(lines, tks, stated, tol=0.011, skip_tk_total=False):
    fee = [l for l in lines if l["line_type"] == "fee"]
    r = {"lines_parsed": len(lines), "fee_lines": len(fee), "expense_lines": sum(1 for l in lines if l["line_type"] == "expense"),
         "sum_fee_hours": round(sum(l["hours"] or 0 for l in fee), 2),
         "sum_fee_amount": round(sum(l["amount"] or 0 for l in fee), 2) if all(l["amount"] is not None for l in fee) else None,
         "sum_expense_amount": round(sum(l["amount"] or 0 for l in lines if l["line_type"] == "expense"), 2)}
    r["stated"] = stated
    checks = []
    if "hours" in stated and fee:
        checks.append(abs(r["sum_fee_hours"] - stated["hours"]) <= tol)
    if "fees" in stated and fee and r["sum_fee_amount"] is not None:
        checks.append(abs(r["sum_fee_amount"] - stated["fees"]) <= tol)
    if "expenses" in stated:
        checks.append(abs(r["sum_expense_amount"] - stated["expenses"]) <= tol)
    if tks:
        r["timekeeper_summary_hours"] = round(sum(t["hours"] for t in tks), 2)
        r["timekeeper_summary_total"] = round(sum(t["total"] for t in tks if t.get("total") is not None), 2)
        if "hours" in stated:
            checks.append(abs(r["timekeeper_summary_hours"] - stated["hours"]) <= tol)
        if "fees" in stated and r["timekeeper_summary_total"] and not skip_tk_total:
            checks.append(abs(r["timekeeper_summary_total"] - stated["fees"]) <= tol)
    r["checks_passed"], r["checks_total"] = sum(checks), len(checks)
    r["status"] = "exact" if checks and all(checks) else ("partial" if any(checks) else "unreconciled")
    return r


def make_line(n, e, tk_alias, line_type="fee", amount=None, rate=None):
    narrative = " ".join(e["desc"]).strip() if isinstance(e["desc"], list) else e["desc"]
    narrative = re.sub(r"\s+", " ", narrative)
    return {"n": n, "line_type": line_type, "date": iso(e["date_raw"]), "timekeeper": tk_alias, "title": None,
            "hours": e.get("hours"), "rate": rate if rate is not None else e.get("rate"),
            "amount": amount if amount is not None else e.get("amount"), "narrative": narrative,
            "codes": [e["code"]] if e.get("code") else ([e["section"]] if e.get("section") else []), "page": e["page"]}


def finish(spec, header, tks, lines, rec, document, notes, aliases):
    """Build (committed, full) label dicts. tks: dicts with name/title/rates/hours/total. aliases: name-or-initials -> alias."""
    meta = dict(spec["meta"])
    committed_tks = [{"alias": t["alias"], "title": t.get("title"), "rates": t.get("rates", []), "hours": t["hours"], "total": t.get("total")}
                     for t in tks]
    clines = []
    for l in sampled(lines):
        d = {k: v for k, v in l.items() if k not in ("narrative", "timekeeper_as_billed")}
        d["narrative_sha256"] = sha(l["narrative"]) if l["narrative"] else None
        d["narrative_len"] = len(l["narrative"] or "")
        clines.append(d)
    base = {"schema": "ebilling-eval/1", "kind": "fee-application", "id": spec["id"], "set": "A", "meta": meta, "header": header,
            "timekeepers": committed_tks, "lines": clines, "document": document, "reconciliation": rec, "notes": notes}
    full = json.loads(json.dumps(base))
    full["lines"] = lines
    full["timekeepers"] = [{**ct, "name_or_initials_as_billed": t.get("name") or t.get("initials")} for ct, t in zip(committed_tks, tks)]
    full["alias_map"] = aliases
    return base, full


def header_block(firm, inv, inv_date, p0, p1, matter, fees, exp, total, adj=0.0, cur="USD"):
    return {"firm": firm, "firm_invoice_number": inv, "invoice_date": inv_date, "billing_period_start": p0, "billing_period_end": p1,
            "firm_matter_reference": matter, "po_number": None, "fees_total": fees, "expenses_total": exp, "adjustments_total": adj,
            "total_due": total, "currency": cur}


def alias_tks(tks, key):
    for i, t in enumerate(sorted(tks, key=lambda t: -t["hours"]), 1):
        t["alias"] = f"TK{i:02d}"
    return {t[key]: t["alias"] for t in tks if t.get(key)}


# ---------------------------------------------------------------------------------------------- per-document builders
def b_polsinelli(spec, pdf):
    cfg = {"date_re": r"^(\d{2}/\d{2}/\d{2})", "date_x": (30, 45), "cols": {"desc": (80, 120), "tk": (390, 440), "hours": (455, 500), "amount": (505, 600)},
           "stop_re": r"^(SUBTOTAL|TOTAL|Grand)", "section_re": r"^(B\d{3}) [A-Z]", "skip_re": r"^Date\b|Page Number|Invoice|Matter No"}
    ents, stops, doc = parse_entries(pdf, range(11, 29), cfg)
    tks = merge_tk_rows(tk_rows(pdf, [4, 5], {"hours": (345, 392), "rate": (400, 460), "total": (470, 540), "name": (78, 175)}))
    # match billing initials to summary rows by (hours, amount)
    by_ini = {}
    for e in ents:
        g = by_ini.setdefault(e["tk"], {"hours": 0.0, "amount": 0.0})
        g["hours"] += e["hours"] or 0
        g["amount"] += e["amount"] or 0
    for t in tks:
        for ini, g in by_ini.items():
            if abs(g["hours"] - t["hours"]) < 0.011 and abs(g["amount"] - t["total"]) < 0.011:
                t["initials"] = ini
    alias = alias_tks(tks, "initials")
    lines = [make_line(i, e, alias.get(e["tk"], "UNMATCHED")) for i, e in enumerate(ents, 1)]
    for l, e in zip(lines, ents):
        l["timekeeper_as_billed"] = e["tk"]
    stated = {"hours": round(sum(s["numbers"][0] for s in stops), 2), "fees": 237817.00}
    rec = recon(lines, tks, stated)
    hdr = header_block("Polsinelli PC", "2480853", "2024-07-18", "2024-05-01", "2024-05-31", "122619-764706", 237817.00, 113.59, 237930.59)
    notes = ["Fee statement amounts are after a voluntary reduction of 16536.52 (10743.00 fees, 5793.52 expenses) disclosed in a footnote; line amounts are post-reduction.",
             "Task code is carried in 'codes' from the section heading; expense detail is not itemised in the filing text layer."]
    return finish(spec, hdr, tks, lines, rec, {"pages": len(doc), "text_layer": True, "line_detail": "full", "tier": "line-level"}, notes, alias)


def b_whburkley(spec, pdf):
    fee = {"date_re": r"^(\d{1,2}/\d{1,2}/\d{2})", "date_x": (45, 55), "cols": {"tk": (95, 130), "desc": (138, 500), "hours": (540, 580)},
           "start_re": r"^PROFESSIONAL SERVICES$", "end_re": r"TOTAL PROFESSIONAL SERVICES", "skip_re": r"^Date\b|Invoice #|Matter #|BURKLEY|Exhibit B"}
    exp = {"date_re": r"^(\d{1,2}/\d{1,2}/\d{2})", "date_x": (45, 55), "cols": {"desc": (95, 500), "amount": (530, 580)},
           "start_re": r"^EXPENSES ADVANCED$", "end_re": r"TOTAL EXPENSES ADVANCED", "skip_re": r"^Date\b|Invoice #|Matter #|BURKLEY|Exhibit B"}
    ents, stops, doc = parse_entries(pdf, range(13, 29), fee)
    exps, _, _ = parse_entries(pdf, range(13, 29), exp)
    # per-invoice summary tables give name, title, hours, rate, total
    rows = tk_rows(pdf, range(13, 29), {"hours": (270, 420), "rate": (421, 520), "total": (521, 600), "name": (30, 200), "title_xmax": 330})
    agg = {}
    for r in rows:
        if not r["name"]:
            continue
        a = agg.setdefault(r["name"], {"name": r["name"], "title": r["title"], "rates": [], "hours": 0.0, "total": 0.0})
        a["hours"] = round(a["hours"] + r["hours"], 2)
        a["total"] = round(a["total"] + r["total"], 2)
        if r["rate"] is not None and r["rate"] not in a["rates"]:
            a["rates"].append(r["rate"])
    tks = list(agg.values())
    for t in tks:
        t["initials"] = initials_of(t["name"])
    alias = alias_tks(tks, "initials")
    rate_of = {t["initials"]: (t["rates"][0] if t["rates"] else None) for t in tks}
    lines = []
    for i, e in enumerate(ents, 1):
        r = rate_of.get(e["tk"])
        l = make_line(i, e, alias.get(e["tk"], "UNMATCHED"), amount=round(e["hours"] * r, 2) if r else None, rate=r)
        l["timekeeper_as_billed"] = e["tk"]
        lines.append(l)
    for e in exps:
        l = make_line(len(lines) + 1, e, None, "expense")
        lines.append(l)
    stated = {"hours": 71.20, "fees": 38199.50, "expenses": 528.90}
    rec = recon(lines, tks, stated)
    hdr = header_block("WH Burkley, LLP", "592783-592790 (seven category invoices)", "2026-08-19", "2026-04-03", "2026-04-30", "35485 (matters 00001-00020)",
                       38199.50, 528.90, 38728.40)
    notes = ["Fee line amounts are derived (hours x the timekeeper's rate from the per-invoice summary); the filing prints hours only per entry.",
             "Seven invoices (one per project category) are attached; header lists the application-level totals."]
    return finish(spec, hdr, tks, lines, rec, {"pages": len(doc), "text_layer": True, "line_detail": "full", "tier": "line-level"}, notes, alias)


def b_foley(spec, pdf):
    cfg = {"date_re": r"^(\d{2}/\d{2}/\d{2})", "date_x": (70, 82), "date_has_tk": True, "cols": {"desc": (230, 445), "hours": (450, 476), "amount": (485, 545)},
           "stop_re": r"^(Total|TOTAL|Subtotal|Current)", "skip_re": r"Case 23-10429|Invoice #|Page \d|Pear Thera|Restructuring|Date\s+Timekeeper|This Invoice"}
    ents, stops, doc = parse_entries(pdf, range(23, 59), cfg)
    tks = merge_tk_rows(tk_rows(pdf, [4, 5, 6], {"hours": (410, 452), "rate": (340, 396), "total": (470, 540), "name": (78, 215), "title_xmax": 310}))
    for t in tks:
        m = re.match(r"([A-Za-z' -]+?)(?: Jr\.)?,", t["name"])
        t["last"] = (m.group(1).strip() if m else t["name"]).split()[-1]
    alias = {}
    for t in sorted(tks, key=lambda t: -t["hours"]):
        pass
    alias = alias_tks(tks, "last")
    lines = []
    for i, e in enumerate(ents, 1):
        is_exp = not e["tk"] and e["hours"] is None
        last = e["tk"].split()[-1] if e["tk"] else ""
        l = make_line(i, e, None if is_exp else alias.get(last, "UNMATCHED"), "expense" if is_exp else "fee")
        l["timekeeper_as_billed"] = e["tk"]
        lines.append(l)
    # fee lines carry the line's own fees; expense lines carry costs
    stated = {"hours": 680.1, "fees": 578155.50, "expenses": 811.43}
    rec = recon(lines, tks, stated, skip_tk_total=True)
    rec["anomalies"] = ["The filing's timekeeper summary prints 14256.00 for one associate at 740.00 an hour for 19.4 hours (should be 14356.00); its own total row (578155.50) and the line amounts agree with the corrected value, so the summary-table total is excluded from the checks."]
    hdr = header_block("Foley Hoag LLP", "4143852", "2023-06-29", "2023-05-01", "2023-05-31", "32056.00034", 578155.50, 811.43, 578966.93)
    notes = ["Amount on a fee line is as printed (a discounted 50 percent rate applies to one timekeeper's travel); line amounts sum to the stated fees.",
             "Expense lines (PACER, copies and similar) appear on the last invoice pages."]
    return finish(spec, hdr, tks, lines, rec, {"pages": len(doc), "text_layer": True, "line_detail": "full", "tier": "line-level"}, notes, alias)


def b_ellis(spec, pdf):
    fee = {"date_re": r"^(\d{1,2}/\d{1,2}/\d{4})", "date_x": (25, 40), "cols": {"tk": (85, 105), "code": (125, 155), "desc": (190, 420), "hours": (425, 456),
                                                                               "rate": (455, 505), "amount": (506, 570)},
           "start_re": r"^SERVICES$", "end_re": r"Total Professional Services", "skip_re": r"^Date\b|Invoice #|Client:|Matter:|Resp\. Atty|^Page:|Case 24-90505"}
    exp = {"date_re": r"^(\d{1,2}/\d{1,2}/\d{4})", "date_x": (25, 40), "cols": {"code": (125, 155), "desc": (190, 505), "amount": (506, 570)},
           "start_re": r"^EXPENSES$", "end_re": r"Total Expenses", "skip_re": r"^Date\b|Invoice #|Client:|Matter:|Resp\. Atty|^Page:|Case 24-90505"}
    ents, stops, doc = parse_entries(pdf, range(9, 15), fee)
    exps, _, _ = parse_entries(pdf, range(9, 15), exp)
    tks = [{"name": "RE", "initials": "RE", "title": "Future Claimants Representative", "rates": [1250.0], "hours": 104.5, "total": 130625.00}]
    alias = alias_tks(tks, "initials")
    lines = []
    for i, e in enumerate(ents, 1):
        l = make_line(i, e, "TK01")
        l["timekeeper_as_billed"] = e["tk"]
        lines.append(l)
    for e in exps:
        lines.append(make_line(len(lines) + 1, e, None, "expense"))
    stated = {"hours": 104.5, "fees": 130625.00, "expenses": 1797.62}
    rec = recon(lines, tks, stated)
    hdr = header_block("Randi S. Ellis (Future Claimants' Representative)", "408", "2025-02-04", "2024-12-01", "2024-12-31", "000001 (client 005032)",
                       130625.00, 1797.62, 132422.62)
    notes = ["Single-timekeeper fee statement: the filing's own timekeeper summary lists one person at 1250.00 an hour.",
             "Task code is carried in 'codes' (B-codes for fees, E-codes for expenses)."]
    return finish(spec, hdr, tks, lines, rec, {"pages": len(doc), "text_layer": True, "line_detail": "full", "tier": "line-level"}, notes, alias)


def b_covington(spec, pdf):
    cfg = {"date_re": r"^(\d{1,2}/\d{1,2}/\d{2})", "date_x": (25, 70), "cols": {"code": (80, 110), "desc": (135, 420), "hours": (425, 458), "tk": (459, 520)},
           "stop_re": r"^TOTALS FOR", "skip_re": r"^Date\b|Case 13|Exhibit A|Page \d|PLEASE RETURN|Robert J|Chapter 11|Keach"}
    ents, stops, doc = parse_entries(pdf, [10, 11], cfg)
    tks = [{"name": "Michael Baxter", "initials": "MB", "title": "Partner", "rates": [950.0, 915.0], "hours": 13.2, "total": 12309.00},
           {"name": "Warren G. Caywood", "initials": "WGC", "title": "Partner", "rates": [915.0], "hours": 0.5, "total": 457.50}]
    tks[0]["hours"] = 13.2
    alias = alias_tks(tks, "initials")
    lines = []
    for i, e in enumerate(ents, 1):
        e["code"] = re.sub(r"^8", "B", e["code"])  # OCR reads B as 8
        who = "Caywood" if e["tk"].startswith("Warren") else "Baxter"
        l = make_line(i, e, "TK01" if who == "Baxter" else "TK02")
        l["codes"] = [e["code"]]
        l["timekeeper_as_billed"] = e["tk"]
        lines.append(l)
    stated = {"hours": 13.7}
    rec = recon(lines, [], stated)
    stated_codes = {"B160": 3.0, "B161": 9.9, "B280": 0.8}
    got = {}
    for l in lines:
        got[l["codes"][0]] = round(got.get(l["codes"][0], 0) + (l["hours"] or 0), 2)
    rec["task_code_subtotals_stated"] = stated_codes
    rec["task_code_subtotals_parsed"] = got
    rec["checks_total"] += len(stated_codes)
    rec["checks_passed"] += sum(1 for k, v in stated_codes.items() if abs(got.get(k, 0) - v) < 0.011)
    rec["status"] = "exact" if rec["checks_passed"] == rec["checks_total"] else ("partial" if rec["checks_passed"] else "unreconciled")
    rec["note"] = "OCR text layer: the last entry (0.5 hours for the second timekeeper) is printed as 0.00, so parsed hours are 13.2 against 13.7 stated."
    hdr = header_block("Covington & Burling LLP", "60702310", "2016-01-12", "2014-05-01", "2015-12-31", "036068.00101", 12766.50, 84.00, 12393.00,
                       adj=-457.50)
    notes = ["Scanned filing with an OCR text layer: letters in task codes and column headings are misread (B160 read as 8160) and corrected in the label.",
             "The cover sheet states interim fees of 8766.50 while the attached invoice totals 12766.50 before a 457.50 reduction; the invoice figures are used.",
             "Hours per line are as printed; amounts are not printed per line."]
    return finish(spec, hdr, tks, lines, rec, {"pages": len(doc), "text_layer": True, "ocr_text_layer": True, "line_detail": "full (small)", "tier": "line-level-partial"}, notes, alias)


def b_cb(spec, pdf):
    """Berger Singerman final application. The text stream is clean: 'dd/dd/dd INITIALS narrative ... hours' per entry, a
    SUB-TOTAL line per project category and a rate-summary block (name, hours, rate, amount) after it."""
    import pypdfium2 as pdfium
    doc = pdfium.PdfDocument(pdf)
    raw = []
    for i in range(30, len(doc)):
        t = doc[i].get_textpage().get_text_range().replace("￾", "-")
        for l in t.splitlines():
            l = l.strip()
            if l:
                raw.append((i + 1, l))
    date_line = re.compile(r"^(\d\d/\d\d/\d\d) ([A-Z]{2,4}) (.*)$")
    exp_line = re.compile(r"^\d\d/\d\d/\d\d [A-Z][A-Z &/.-]+? +\d[\d,]*\.\d\d +\d[\d,]*\.\d\d$")
    rate_line = re.compile(r"^(.+?)\s+(\d+\.\d\d) HOURS\s+(\d[\d,]*\.\d\d)/HR\s+(\d[\d,]*\.\d\d)$")
    ents, subs, rate_rows = [], [], []
    cur, inv, sec = None, None, None
    section_entries, awaiting = [], []
    for pg, l in raw:
        m = re.search(r"INVOICE NO\. (\d+)", l)
        if m:
            inv = m.group(1)
        m = re.match(r"^([A-Z][A-Z/ &-]{5,}?)\s+MATTER ID: (\S+)$", l)
        if m:
            sec = m.group(1).strip()
        if l.startswith("SUB-TOTAL FEES"):
            subs.append([float(x.replace(",", "")) for x in re.findall(r"[\d,]+\.\d\d", l)])
            cur = None
            awaiting, section_entries = section_entries, []
            continue
        m = rate_line.match(l)
        if m:
            for e2 in awaiting:
                if e2["tk"] == initials_of(m.group(1)) and e2.get("rate") is None:
                    e2["rate"] = float(m.group(3).replace(",", ""))
            rate_rows.append((initials_of(m.group(1)), m.group(1), float(m.group(2)), float(m.group(3).replace(",", ""))))
            continue
        mm = date_line.match(l)
        if mm and re.search(r"[A-Z]", mm.group(3)) and not exp_line.match(l):
            e = {"page": pg, "date_raw": mm.group(1), "tk": mm.group(2), "desc": [mm.group(3)], "hours": None, "inv": inv, "section": sec, "code": ""}
            tail = re.search(r"\s(\d+\.\d\d)(?: N/C)?$", mm.group(3))
            if tail:
                e["hours"] = float(tail.group(1))
                e["desc"][0] = mm.group(3)[:tail.start()]
                cur = None
            else:
                cur = e
            ents.append(e)
            section_entries.append(e)
            continue
        if cur is not None:
            if re.fullmatch(r"\d+\.\d\d( N/C)?", l):
                cur["hours"] = float(l.split()[0])
                cur = None
            elif not l.startswith(("Case 25-", "CONSOLIDATED", "MATTER ID", "PAGE", "RATE SUMMARY")):
                cur["desc"].append(l)
    rates_by_ini, names = {}, {}
    for ini, nm, hrs, rt in rate_rows:
        rates_by_ini.setdefault(ini, set()).add(rt)
        names[ini] = nm
    tkagg, lines = {}, []
    for i, e in enumerate(ents, 1):
        cands = rates_by_ini.get(e["tk"])
        rt = e.get("rate")
        l = make_line(i, e, None, amount=round(e["hours"] * rt, 2) if (rt and e["hours"] is not None) else None, rate=rt)
        l["codes"] = [e["section"]] if e["section"] else []
        l["timekeeper_as_billed"] = e["tk"]
        lines.append(l)
        a = tkagg.setdefault(e["tk"], {"initials": e["tk"], "name": names.get(e["tk"]), "title": None, "rates": sorted(cands) if cands else [], "hours": 0.0, "total": 0.0})
        a["hours"] = round(a["hours"] + (e["hours"] or 0), 2)
        a["total"] = round(a["total"] + (l["amount"] or 0), 2)
    tks = list(tkagg.values())
    alias = alias_tks(tks, "initials")
    for l in lines:
        l["timekeeper"] = alias.get(l["timekeeper_as_billed"], "UNMATCHED")
    stated = {"hours": 207.5, "fees": 162161.50}
    rec = recon(lines, tks, stated)
    rec["subtotal_fees_lines"] = {"count": len(subs), "sum_hours": round(sum(s[0] for s in subs), 2), "sum_amount": round(sum(s[-1] for s in subs), 2)}
    missing = [l["n"] for l in lines if l["hours"] is None]
    if missing:
        rec["lines_without_hours"] = missing
    hdr = header_block("Berger Singerman LLP", "303435, 304667, 305903, 307222, 308509, 308992 (six monthly invoices)", "2026-01-06", "2025-12-01", "2026-05-31",
                       "35606-0001", 162161.50, 1859.44, 164020.94)
    notes = ["Final application with six monthly invoices attached; header lists application-level totals and the first invoice date.",
             "Fee line amounts are derived (hours x the timekeeper's rate from the rate summary printed under each project category); they sum to the stated fees.",
             "Expense detail (PACER, reproductions) runs to many pages and is not itemised in this label; the stated expense total is 1859.44."]
    return finish(spec, hdr, tks, lines, rec, {"pages": len(doc), "text_layer": True, "line_detail": "full", "tier": "line-level"}, notes, alias)


def b_klgates(spec, pdf):
    rows = tk_rows(pdf, range(13, 19), {"hours": (405, 455), "total": (480, 560), "name": (75, 150), "title_xmax": 215})
    tks = []
    for r in rows:
        if r["hours"] and r["total"]:
            tks.append({"name": r["name"], "title": r["title"], "rates": [], "hours": r["hours"], "total": r["total"], "initials": r["name"]})
    # keep timekeeper rows only (drop the category-summary and total rows picked up on later pages)
    alias = alias_tks(tks, "initials") if tks else {}
    stated = {"fees": 3319049.05}
    rec = recon([], tks, stated)
    hdr = header_block("K&L Gates LLP", "First interim fee application (monthly applications attached separately)", "2025-04-30", "2024-12-16", "2025-03-31",
                       "Akoustis 24-12796", 3319049.05, 29781.64, 3348830.69)
    notes = ["Summary-level label: the filing attaches the timekeeper summary, category summary and expense summary but no entry-level time detail.",
             "Hourly rates change on 2025-03-01 (step increase) so several rates are printed per timekeeper; rates are not listed per timekeeper here."]
    return finish(spec, hdr, tks, [], rec, {"pages": __import__("pypdfium2").PdfDocument(pdf).__len__(), "text_layer": True, "line_detail": "none (summary only)", "tier": "summary-level"}, notes, alias)


def b_zetta(spec, pdf):
    rows = tk_rows(pdf, [25], {"hours": (300, 340), "rate": (345, 390), "total": (480, 560), "name": (80, 200), "title_xmax": 300, "ytol": 4.5})
    tks = []
    for r in rows:
        tks.append({"name": r["name"], "title": r["title"], "rates": [r["rate"]] if r["rate"] else [], "hours": r["hours"], "total": r["total"], "initials": r["name"]})
    alias = alias_tks(tks, "initials") if tks else {}
    stated = {"hours": 167.5}
    rec = recon([], tks, stated)
    rec["stated_sgd_before_gst_and_discount"] = 70045.00
    hdr = header_block("WongPartnership LLP", "180749", "2018-04-05", "2017-11-17", "2017-12-04", "Zetta Jet USA (2:17-bk-21386)", 28295.34, 243.44, 28538.78)
    notes = ["Summary-level label: the entry-level billing records (Exhibit A) are page images with no text layer, so lines are not labelled.",
             "Rates and the timekeeper summary are in Singapore dollars before 7 percent GST and a goodwill discount; header figures are the US dollar amounts requested on the cover sheet.",
             "Invoice 180749 is dated 5 April 2018; the fee period on the application is 17 November to 4 December 2017."]
    return finish(spec, hdr, tks, [], rec, {"pages": __import__("pypdfium2").PdfDocument(pdf).__len__(), "text_layer": True, "line_detail": "none (image-only exhibit)", "tier": "summary-level"}, notes, alias)


BUILDERS = {"A-01": b_klgates, "A-02": b_foley, "A-03": b_polsinelli, "A-04": b_covington, "A-05": b_whburkley, "A-06": b_ellis,
            "A-07": b_cb, "A-08": b_zetta}
SPIKE = {"A-02", "A-07"}


def write_one(path, obj):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    comp = lambda o: json.dumps(o, ensure_ascii=False, separators=(",", ":"))
    nl = chr(10)
    parts = []
    for k, v in obj.items():
        if k == "lines" and isinstance(v, list):
            body = ("," + nl).join("  " + comp(x) for x in v)
            parts.append(" " + json.dumps(k) + ":[" + nl + body + nl + " ]" if v else " " + json.dumps(k) + ":[]")
        else:
            parts.append(" " + json.dumps(k) + ":" + comp(v))
    with open(path, "w", encoding="utf-8", newline=nl) as fh:
        fh.write("{" + nl + ("," + nl).join(parts) + nl + "}" + nl)


def main(ids):
    import pii_scan
    with open(os.path.join(HERE, "sources.json"), encoding="utf-8") as fh:
        reg = json.load(fh)
    summary = []
    for src in reg["fee_applications"]:
        rid = src["id"]
        if ids and rid not in ids:
            continue
        local = os.path.join(ES, src["local_path"])
        scan = pii_scan.scan(local)
        c = scan["counts"]
        nz = {k: v for k, v in c.items() if v}
        scan["disposition"] = ("REVIEW: identifier-like strings found" if (c.get("ssn") or c.get("card_number") or c.get("dob_label"))
                               else "Court filing: professional names, firm addresses and case parties only. Narratives are never committed; timekeepers are aliased.")
        meta = {"title": src["title"], "source_url": src["source_url"], "licence": src["licence"], "licence_status": src["licence_status"],
                "pii_note": "Court record. Counts only: " + (", ".join(f"{k} {v}" for k, v in nz.items()) or "no pattern hits")
                            + ". Committed label has narrative hashes and timekeeper aliases only; full label is local.",
                "pii_scan": scan, "storage": {"location": "local-only", "path": src["local_path"]}, "spike": rid in SPIKE,
                "label_method": "parser+reconciliation"}
        spec = {"id": rid, "meta": meta}
        base, full = BUILDERS[rid](spec, local)
        write_one(os.path.join(ES, "ground-truth", "fee-applications", rid + ".json"), base)
        write_one(os.path.join(ES, "local", "ground-truth-full", rid + ".json"), full)
        r = base["reconciliation"]
        summary.append((rid, base["document"]["tier"], r["status"], r["lines_parsed"], f"{r['checks_passed']}/{r['checks_total']}"))
        print(rid, base["header"]["firm"], "|", base["document"]["tier"], "|", r["status"], f"lines={r['lines_parsed']} checks={r['checks_passed']}/{r['checks_total']}",
              f"hours={r['sum_fee_hours']}", f"fees={r['sum_fee_amount']}", f"tks={len(base['timekeepers'])}")
    return summary


if __name__ == "__main__":
    main(set(sys.argv[1:]))
