#!/usr/bin/env python3
"""Label the real invoices (set C): header, totals, timekeeper summary and, where the layout allows, line entries.

Run with the generator venv (pypdfium2) and `-I` because the inputs are untrusted downloads:
    python -I generator/label_real_invoices.py [C-03 ...]        # all twelve by default

Method (label_method "extracted+reviewed"): header, totals and timekeeper summaries were read from each invoice's own
summary pages (text layer, OCR where the filing is a scan) and then checked two ways. First, arithmetic closure: the
timekeeper amounts add up to the fees, fees + adjustments + expenses give the current charges, and current charges plus the
unpaid prior balance give the amount due. An OCR digit error breaks the closure and is then fixed from the page image (that
is how a dropped discount line on C-03 was found). Second, the page image of the
summary page was read for C-03, C-04 and C-05. Line entries are parsed for the Goss Samford invoices (date, timekeeper, hours, amount,
OCR narrative hash) and the Dinsmore invoices (native text). Stoll Keenon Ogden invoices are labelled at invoice level because the OCR column order is interleaved.

Outputs
  ground-truth/real-invoices/C-NN.json  committed: header, aliased timekeepers, lines with narrative hashes only
  local/ground-truth-full/C-NN.json     local only: timekeeper names/initials as billed and narratives
Real documents stay in local/real-invoices/ (git-ignored); see real_sources.py and cut_real_invoices.py.
"""
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ES = os.path.dirname(HERE)
sys.path.insert(0, HERE)

import label_fee_applications as L  # noqa: E402
import pdfrows  # noqa: E402
import pii_scan  # noqa: E402
import real_sources as S  # noqa: E402

LICENCE = ("No licence stated. Public record: filed by the utility with the Kentucky Public Service Commission in a rate-case docket; "
           "used only as a public record for internal evaluation, not redistributed.")

# Verified header facts per invoice. fees/exp/adj are the amounts on the invoice; prior is the unpaid prior balance carried on it.
FACTS = {
    "C-01": dict(date="2019-02-05", end="2019-01-31", matter="8605", fees=3906.50, exp=0.0, adj=0.0, prior=0.0, due=3906.50, hours=17.60,
                 parse="goss", kind="scan+ocr"),
    "C-02": dict(date="2019-03-04", end="2019-02-28", matter="8605", fees=5526.00, exp=26.88, adj=0.0, prior=3906.50, due=9459.38, hours=24.60,
                 parse="goss", kind="scan+ocr"),
    "C-03": dict(date="2019-04-08", end="2019-03-31", matter="8605", fees=36883.00, exp=581.80, adj=-2000.00, prior=0.0, due=35464.80, hours=154.05,
                 parse="goss", kind="scan+ocr", note="A 2,000.00 discount on legal fees is on the summary page image but missing from the OCR text layer."),
    "C-04": dict(date="2019-05-02", end="2019-04-30", matter="8605", fees=9243.00, exp=29.88, adj=0.0, prior=0.0, due=9272.88, hours=39.00,
                 parse="goss", kind="scan+ocr", note="The OCR text layer drops the Current Fees and Billable Hours figures; both come from the page image and the timekeeper lines."),
    "C-05": dict(date="2019-06-03", end="2019-05-31", matter="8605", fees=17052.50, exp=148.44, adj=0.0, prior=0.0, due=17200.94, hours=70.70,
                 parse="goss", kind="scan+ocr", note="Fees include a 50.00 flat charge for one timekeeper (1.50 flat-charge hours) on top of 17,002.50 of hourly fees."),
    "C-06": dict(date="2018-06-12", end=None, matter="2018 Rate Case", fees=5776.00, exp=0.0, adj=0.0, prior=0.0, due=5776.00, hours=17.20,
                 parse=None, kind="native text, columns interleaved"),
    "C-07": dict(date="2018-07-13", end=None, matter="2018 Rate Case", fees=4383.50, exp=0.30, adj=0.0, prior=5776.00, due=10159.80, hours=14.30,
                 parse=None, kind="native text, columns interleaved"),
    "C-08": dict(date="2018-09-13", end=None, matter="2018 Rate Case", fees=4152.50, exp=1.10, adj=0.0, prior=0.0, due=4153.60, hours=12.10,
                 parse=None, kind="native text, columns interleaved"),
    "C-09": dict(date="2018-10-03", end=None, matter="2018 Rate Case", fees=6745.00, exp=1.20, adj=0.0, prior=4153.60, due=10899.80, hours=19.00,
                 parse=None, kind="native text, columns interleaved"),
    "C-10": dict(date="2018-11-05", end=None, matter="2018 Rate Case", fees=28251.00, exp=21019.40, adj=0.0, prior=6746.20, due=56016.60, hours=85.40,
                 parse=None, kind="native text, columns interleaved",
                 note="Prior balance is 10,899.80 less credits of 4,153.60; the expense total includes large duplicating charges."),
    "C-11": dict(date="2026-04-28", end="2026-03-31", matter="2025 Rate Case", fees=4262.00, exp=0.0, adj=0.0, prior=71591.40, due=75853.40, hours=8.80,
                 parse="ds", kind="native text"),
    "C-12": dict(date="2026-05-14", end="2026-04-30", matter="2025 Rate Case", fees=13184.00, exp=0.0, adj=0.0, prior=73207.40, due=86391.40, hours=25.40,
                 parse="ds", kind="native text"),
}

# Stoll Keenon Ogden attorney/paralegal summaries (initials/name as billed -> local label only): name, rank, hours, rate
SKO_TKS = {
    "C-06": [("M. Braun", "Member", 3.00, 245.00), ("L. W Ingram, III", "Member", 14.20, 355.00)],
    "C-07": [("M. Braun", "Member", 6.30, 245.00), ("L. W Ingram, III", "Member", 8.00, 355.00)],
    "C-08": [("M. Braun", "Member", 1.30, 245.00), ("L. W Ingram, III", "Member", 10.80, 355.00)],
    "C-09": [("L. W Ingram, III", "Member", 19.00, 355.00)],
    "C-10": [("M. Braun", "Member", 3.60, 245.00), ("L. W Ingram, III", "Member", 67.90, 355.00), ("K. R Riggs", "Member", 0.40, 455.00),
             ("M. Wimberly", "Associate", 9.10, 240.00), ("G. E Wuetcher", "Of Counsel", 0.30, 330.00), ("M. M Loy", "Paralegal", 4.10, 195.00)],
}
# Dinsmore summaries: name, level, hours (rates are not printed)
DS_TKS = {
    "C-11": [("Evan Buckley", "Partner", 4.60), ("R. Brooks Herrick", "Partner", 0.70), ("Hannah D. Thompson", "Associate", 3.50)],
    "C-12": [("Evan Buckley", "Partner", 5.80), ("R. Brooks Herrick", "Partner", 15.80), ("Hannah D. Thompson", "Associate", 3.80)],
}
DS_INITIALS = {"EB": "Evan Buckley", "BRH": "R. Brooks Herrick", "HDT": "Hannah D. Thompson"}


# Members of the task 005 extraction spike: a scan with OCR noise, a native invoice with interleaved columns and a clean native invoice.
SPIKE = {"C-03", "C-10", "C-11"}


def money(s):
    return float(re.sub(r"\s+", "", s).replace(",", "").replace("$", ""))


# ------------------------------------------------------------------------------------------------ Goss Samford
def fixnum(t):
    """OCR repair for numeric cells: letter look-alikes, stray spaces and doubled dots."""
    t = t.replace("I", "1").replace("l", "1").replace("S", "5").replace("O", "0").replace("o", "0")
    t = re.sub(r"[^\d.,$/]", "", t)
    return re.sub(r"\.{2,}", ".", t)


def goss_entries(pdf):
    """Entry rows from character rectangles: date | timekeeper | narrative block | hours | $amount. OCR noise is repaired."""
    import pypdfium2 as pdfium
    doc = pdfium.PdfDocument(pdf)
    date_re = re.compile(r"^\d{1,2}/\d{1,2}/\d{4}$")
    stop = re.compile(r"Continued|Billable Hours|Timekeeper Summary|Cost Detail|Current Invoice Summary|Payment Detail|Total Costs")
    fix = {"MES": "MEB"}
    entries, cur = [], None
    for pi in range(len(doc)):
        for y, cells in pdfrows.page_rows(doc[pi]):
            txt = " ".join(c[2] for c in cells)
            if stop.search(txt):
                cur = None
                continue
            dtxt = fixnum("".join(c[2] for c in cells if 60 <= c[0] < 125))
            tk = next((c for c in cells if 125 <= c[0] < 200 and re.fullmatch(r"[A-Z]{2,4}", c[2])), None)
            htxt = fixnum("".join(c[2] for c in cells if 430 < c[0] < 490))
            atxt = fixnum("".join(c[2] for c in cells if c[0] >= 490)).lstrip("$")
            if date_re.match(dtxt) and tk and re.fullmatch(r"\d+\.\d\d", htxt) and re.fullmatch(r"[\d,]+\.\d\d", atxt):
                narr = " ".join(c[2] for c in cells if 200 < c[0] < 430)
                cur = {"date_raw": dtxt, "tk": fix.get(tk[2], tk[2]), "hours": float(htxt), "amount": money(atxt), "desc": [narr], "page": pi + 1}
                entries.append(cur)
                continue
            if cells and re.search(r"\d{1,2}\s*/\s*\d{1,2}\s*/\s*20\d\d", txt) and cells[0][0] < 125 and len(cells) > 2:
                cur = None
                entries_missed.append((pdf, pi + 1, txt[:80]))
                continue
            if cur is not None and cells and 200 < cells[0][0] < 440:
                cur["desc"].append(" ".join(c[2] for c in cells if c[0] < 440))
    return entries


entries_missed = []


def goss_parse(pdf):
    txt = subprocess.run(["pdftotext", "-layout", pdf, "-"], capture_output=True, timeout=120).stdout.decode("utf-8", "ignore")
    tks, flat = {}, []
    tkl = re.compile(r"Timekeeper\s+([A-Z]{2,4})\s+worked\s+(\d+\.\d\d)\s+hours at\s+\$([\d,]+\.\d\d)\s+per hour,\s+totaling\s+\$([\d,]+\s?\.\d\d)")
    flatl = re.compile(r"Timekeeper\s+([A-Z]{2,4})\s+worked\s+(\d+\.\d\d)\s+flat charge hours totaling\s+\$([\d,]+\.\d\d)")
    fix = {"MES": "MEB"}
    for ln in txt.splitlines():
        m = tkl.search(ln)
        if m:
            ini = fix.get(m.group(1), m.group(1))
            tks[ini] = {"initials": ini, "hours": float(m.group(2)), "rate": money(m.group(3)), "total": money(m.group(4)), "flat": 0.0, "flat_hours": 0.0}
            continue
        m = flatl.search(ln)
        if m:
            flat.append((fix.get(m.group(1), m.group(1)), float(m.group(2)), money(m.group(3))))
    for ini, hrs, amt in flat:
        tks[ini]["flat"] = amt
        tks[ini]["flat_hours"] = hrs
    return tks, goss_entries(pdf)


def build_goss(cid, pdf, f):
    tkd, entries = goss_parse(pdf)
    tks = []
    for ini, t in tkd.items():
        tks.append({"initials": ini, "name": None, "title": None, "rates": [t["rate"]], "hours": t["hours"], "total": round(t["total"] + t["flat"], 2),
                    "flat_charge_hours": t["flat_hours"] or None})
    alias = L.alias_tks(tks, "initials")
    lines = []
    for i, e in enumerate(entries, 1):
        rate = tkd[e["tk"]]["rate"] if e["tk"] in tkd else None
        ln = L.make_line(i, e, alias.get(e["tk"], "UNMATCHED"), amount=e["amount"], rate=rate)
        ln["timekeeper_as_billed"] = e["tk"]
        t = tkd.get(e["tk"])
        if t and t["flat"] and abs(e["amount"] - t["flat"]) < 0.011 and abs(e["hours"] - t["flat_hours"]) < 0.011:
            ln["flat_charge"], ln["rate"] = True, None
        lines.append(ln)
    flat_hours = round(sum(t["flat_hours"] for t in tkd.values()), 2)
    stated = {"hours": round(f["hours"] + flat_hours, 2), "fees": f["fees"]}
    rec = L.recon(lines, [dict(t, hours=round(t["hours"] + (t["flat_charge_hours"] or 0), 2)) for t in tks], stated)
    if flat_hours:
        rec["stated_hours_note"] = f"stated hours {stated['hours']} = {f['hours']} billable plus {flat_hours} flat-charge hours"
    unk = sorted({e["tk"] for e in entries} - set(tkd))
    if unk:
        rec["entry_timekeepers_not_in_summary"] = unk
    per = {}
    for l in lines:
        a = per.setdefault(l["timekeeper_as_billed"], 0.0)
        per[l["timekeeper_as_billed"]] = round(a + l["hours"], 2)
    rec["per_timekeeper_hours_mismatch"] = {k: [per.get(k, 0.0), t["hours"] + t["flat_hours"]] for k, t in tkd.items() if abs(per.get(k, 0.0) - t["hours"] - t["flat_hours"]) > 0.011}
    return tks, lines, rec, alias


# ------------------------------------------------------------------------------------------------ Dinsmore & Shohl
def ds_parse(pdf):
    import pypdfium2 as pdfium
    doc = pdfium.PdfDocument(pdf)
    date_re = re.compile(r"^\d\d/\d\d/\d\d$")
    entries, cur = [], None
    in_detail = False
    for pi in range(len(doc)):
        rows = pdfrows.page_rows(doc[pi])
        for y, cells in rows:
            txt = " ".join(c[2] for c in cells)
            if "Detail of Current Hours Worked" in txt:
                in_detail = True
                continue
            if not in_detail:
                continue
            if txt.startswith("Total Hours"):
                in_detail = False
                cur = None
                continue
            if cells and date_re.match(cells[0][2]) and len(cells) >= 3 and re.fullmatch(r"[A-Z]{2,4}", cells[1][2]) and re.fullmatch(r"\d+\.\d\d", cells[2][2]):
                cur = {"date_raw": cells[0][2], "tk": cells[1][2], "hours": float(cells[2][2]), "desc": [" ".join(c[2] for c in cells[3:])], "page": pi + 1}
                entries.append(cur)
            elif cur is not None and cells and cells[0][0] > 260 and not re.match(r"^(April|May|March) \d", txt) and "Invoice #" not in txt:
                cur["desc"].append(txt)
    return entries


def build_ds(cid, pdf, f):
    entries = ds_parse(pdf)
    tks = [{"initials": ini, "name": nm, "title": lvl, "rates": [], "hours": hrs, "total": None}
           for (nm, lvl, hrs), ini in zip(DS_TKS[cid], ["EB", "BRH", "HDT"])]
    alias = L.alias_tks(tks, "initials")
    lines = []
    for i, e in enumerate(entries, 1):
        ln = L.make_line(i, e, alias.get(e["tk"], "UNMATCHED"))
        ln["timekeeper_as_billed"] = e["tk"]
        lines.append(ln)
    stated = {"hours": f["hours"]}
    rec = L.recon(lines, tks, stated)
    per = {}
    for l in lines:
        per[l["timekeeper_as_billed"]] = round(per.get(l["timekeeper_as_billed"], 0.0) + l["hours"], 2)
    rec["per_timekeeper_hours_mismatch"] = {t["initials"]: [per.get(t["initials"], 0.0), t["hours"]] for t in tks if abs(per.get(t["initials"], 0.0) - t["hours"]) > 0.011}
    rec["note_no_amounts"] = "Line amounts and hourly rates are not printed on this invoice; fees are stated only as a total."
    return tks, lines, rec, alias


def build_sko(cid, pdf, f):
    rows = SKO_TKS[cid]
    tks = [{"initials": nm, "name": nm, "title": rk, "rates": [rt], "hours": hrs, "total": round(hrs * rt, 2)} for nm, rk, hrs, rt in rows]
    alias = L.alias_tks(tks, "initials")
    rec = L.recon([], tks, {"hours": f["hours"], "fees": f["fees"]})
    return tks, [], rec, alias


def main(ids):
    import pypdfium2 as pdfium
    src_by = {cid: (key, a, b, inv) for cid, key, a, b, inv in S.INVOICES}
    summary = []
    for cid in sorted(FACTS):
        if ids and cid not in ids:
            continue
        f = FACTS[cid]
        key, a, b, inv = src_by[cid]
        filing = S.FILINGS[key]
        rel = f"local/real-invoices/{cid}.pdf"
        pdf = os.path.join(ES, rel)
        scan = pii_scan.scan(pdf)
        c = scan["counts"]
        nz = {k: v for k, v in c.items() if v}
        scan["disposition"] = ("REVIEW: identifier-like strings found" if (c.get("ssn") or c.get("card_number") or c.get("dob_label"))
                               else "Law-firm invoice in a public docket: professional names, business contacts and bank remittance details only; no private-person data "
                                    "expected beyond an expense payee on some Goss Samford invoices. Narratives are never committed; timekeepers are aliased.")
        meta = {"title": f"{filing['firm']} invoice {inv} ({filing['client_type']}, {filing['docket']})", "source_url": filing["url"],
                "source_pages": [a, b], "licence": LICENCE, "licence_status": "public-record-no-licence",
                "pii_note": "Public regulatory filing. Counts only: " + (", ".join(f"{k} {v}" for k, v in nz.items()) or "no pattern hits")
                            + ". The source URL carries the filer's business e-mail address as part of the public PSC path. Committed label has timekeeper aliases and narrative hashes only; names and narratives are in the local full label.",
                "pii_scan": scan, "storage": {"location": "local-only", "path": rel}, "spike": cid in SPIKE, "label_method": "extracted+reviewed"}
        spec = {"id": cid, "meta": meta}
        builder = {"goss": build_goss, "ds": build_ds, None: build_sko}[f["parse"]]
        tks, lines, rec, alias = builder(cid, pdf, f)
        current = round(f["fees"] + f["adj"] + f["exp"], 2)
        expected_due = round(current + f["prior"], 2)
        hdr = L.header_block(filing["firm"], inv, f["date"], None, f["end"], f["matter"], f["fees"], f["exp"], f["due"], adj=f["adj"])
        hdr["current_charges"] = current
        hdr["prior_balance_due"] = f["prior"]
        hdr["client_type"] = filing["client_type"]
        rec["header_arithmetic"] = {"fees_plus_adjustments_plus_expenses": current, "plus_prior_balance": expected_due, "stated_total_due": f["due"],
                                    "ok": abs(expected_due - f["due"]) < 0.011}
        tier = "invoice-level" if f["parse"] is None else "line-level"
        notes = []
        if f.get("note"):
            notes.append(f["note"])
        if tier == "invoice-level":
            notes.append("Invoice-level label: the OCR column order interleaves dates, hours, rates and amounts, so time entries are not labelled; "
                         "the timekeeper summary, subtotal hours and the cover totals are.")
        if f["parse"] == "goss":
            notes.append("Scanned invoice read through its OCR text layer: line narratives are OCR text and may carry character errors; amounts and hours are repaired "
                         "for OCR noise and reconciled to the stated totals. Cost (expense) lines are not itemised; the stated cost total is in the header.")
        if f["parse"] == "ds":
            notes.append("Native-text invoice: lines carry date, timekeeper, hours and narrative hash; no rates or line amounts are printed, so fees are checked at total level only.")
        pages = len(pdfium.PdfDocument(pdf))
        document = {"pages": pages, "text_layer": True, "source_kind": f["kind"], "tier": tier, "line_detail": "full" if lines else "none"}
        base, full = L.finish(spec, hdr, tks, lines, rec, document, notes, alias)
        base["kind"], base["set"] = "real-invoice", "C"
        full["kind"], full["set"] = "real-invoice", "C"
        L.write_one(os.path.join(ES, "ground-truth", "real-invoices", cid + ".json"), base)
        L.write_one(os.path.join(ES, "local", "ground-truth-full", cid + ".json"), full)
        r = base["reconciliation"]
        ok = r["header_arithmetic"]["ok"]
        print(cid, filing["firm"], inv, "|", tier, "|", r["status"], f"lines={r['lines_parsed']} checks={r['checks_passed']}/{r['checks_total']}",
              f"hours={r['sum_fee_hours']} fees={r['sum_fee_amount']} tk_total={r.get('timekeeper_summary_total')}", f"arith={'ok' if ok else 'FAIL'}",
              f"mismatch={r.get('per_timekeeper_hours_mismatch')}", f"unk={r.get('entry_timekeepers_not_in_summary')}")
        summary.append((cid, tier, r["status"], ok))
    return summary


if __name__ == "__main__":
    main(set(sys.argv[1:]))
