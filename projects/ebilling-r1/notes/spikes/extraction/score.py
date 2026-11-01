"""Scoring harness for the task 005 extraction spike (reusable by 161 / 162).

usage: python score.py ARM [ARM...]        -> results/score-<arm>.json + a console summary
       python score.py --all

What it measures (ground-truth format: evaluation-set/manifest.md section 1):
  header accuracy  12 FR-21 header fields per item; GT null + empty extraction counts as correct.
  line accuracy    GT fee and expense lines (adjustment lines excluded: the analyzer carries adjustments in
                   AdjustmentsTotal). Each GT line is matched to at most one extracted line (greedy on date,
                   amount, hours, timekeeper, narrative similarity). A field is correct only on a matched line
                   with an equal value, so a missed line counts as wrong for every field (recall is inside the
                   accuracy). Fee lines: date, timekeeper, hours, rate, amount. Expense lines: date, amount.
                   Fields whose GT value is null (not printed) are not scored.
  line views       "printed": only fields the document prints on the row, values as returned (the extraction
                   measure); "repaired": the same fields after the deterministic completion in complete() (hh:mm
                   hours, rate from the timekeeper summary, arithmetic) - what 043 can store; "complete": every
                   GT field after completion, including values the row does not print.
  calibration      every extracted value that was scored (header values the arm returned, matched-line fields,
                   and fields of extracted lines that match no GT line on items whose GT is complete), binned by
                   confidence; headline = count and rate of wrong values at confidence >= 0.90.
  latency / cost   from runs/<arm>.jsonl (wall time POST -> result; usage tokens and pages x prices.json).

Normalisation: dates to ISO; numbers to 0.01; text compared case/punctuation-insensitively; firm names
ignore legal-form suffixes; an adjustment total equal in magnitude but opposite in sign counts as correct
and is tallied; total_due may equal either the GT total due or the GT current charges (real invoices with a
prior balance; the analyzer is told to return current charges).
No document text is written to results (ADR-015): identifiers, counts and rates only.
"""
from __future__ import annotations

import difflib
import json
import re
import statistics
import sys
from datetime import datetime

from common import RESULTS, load_full_gt, load_gt, load_raw, read_runs, spike_items, HERE

HEADER = ["firm", "firm_invoice_number", "invoice_date", "billing_period_start", "billing_period_end",
          "firm_matter_reference", "po_number", "fees_total", "expenses_total", "adjustments_total", "total_due",
          "currency"]
DATE_F = {"invoice_date", "billing_period_start", "billing_period_end"}
NUM_F = {"fees_total", "expenses_total", "adjustments_total", "total_due"}
FEE_F = ["date", "timekeeper", "hours", "rate", "amount"]
EXP_F = ["date", "amount"]
# GT whose line list is a subset of the document's lines (extra extracted lines are not spurious).
PARTIAL_GT = {"A-07"}
PRICES = json.loads((HERE / "prices.json").read_text(encoding="utf-8"))


# ---------------------------------------------------------------- normalisers

def norm_text(s) -> str:
    return re.sub(r"[^a-z0-9]", "", str(s).lower()) if s not in (None, "") else ""


SUFFIX = re.compile(r"\b(llp|l\.l\.p\.|pllc|p\.l\.l\.c\.|pc|p\.c\.|plc|llc|ltd|lp|the|attorneys at law|law offices of)\b")


def norm_firm(s) -> str:
    return norm_text(SUFFIX.sub(" ", str(s).lower())) if s else ""


def to_date(s):
    if s in (None, ""):
        return None
    s = str(s).strip().split("T")[0]
    for fmt in ("%Y-%m-%d", "%m/%d/%Y", "%m/%d/%y", "%d/%m/%Y", "%B %d, %Y", "%b %d, %Y", "%d %B %Y", "%d %b %Y",
                "%Y/%m/%d", "%m-%d-%Y", "%d.%m.%Y"):
        try:
            return datetime.strptime(s, fmt).date().isoformat()
        except ValueError:
            continue
    m = re.match(r"(\d{4}-\d{2}-\d{2})", s)
    return m.group(1) if m else s


def to_num(v):
    if v in (None, ""):
        return None
    if isinstance(v, (int, float)):
        return round(float(v), 2)
    s = str(v).replace(",", "").replace("$", "").replace("£", "").replace("€", "").strip()
    neg = s.startswith("(") and s.endswith(")")
    s = s.strip("()")
    try:
        x = float(s)
    except ValueError:
        return None
    return round(-x if neg else x, 2)


def num_eq(a, b, tol=0.011):
    return a is not None and b is not None and abs(a - b) <= tol


# ---------------------------------------------------------------- arm adapters -> common shape
# shape: {"header": {field: (value, conf)}, "lines": [{"type", "date", "timekeeper", "hours", "rate", "amount",
#         "narrative", "conf": {field: conf}}], "header_scope": set(fields the arm's schema can carry)}

def _cu_val(f):
    if not isinstance(f, dict):
        return None, None
    for k in ("valueString", "valueDate", "valueNumber", "valueInteger", "valueBoolean", "valueTime"):
        if k in f:
            return f[k], f.get("confidence")
    if f.get("type") == "object" and "valueObject" in f:
        amt = (f["valueObject"].get("Amount") or {})
        return amt.get("valueNumber"), amt.get("confidence", f.get("confidence"))
    return None, f.get("confidence")


def _cu_contents(raw):
    contents = ((raw or {}).get("result") or {}).get("contents") or []
    with_fields = [c for c in contents if c.get("fields")]
    if not with_fields:
        return None
    # routed results: prefer the invoice category; otherwise the content with the most fields
    inv = [c for c in with_fields if (c.get("category") or "invoice") == "invoice"]
    return max(inv or with_fields, key=lambda c: len(json.dumps(c["fields"])))["fields"]


def adapt_custom(raw):
    f = _cu_contents(raw)
    if f is None:
        return None
    m = {"firm": "FirmName", "firm_invoice_number": "FirmInvoiceNumber", "invoice_date": "InvoiceDate",
         "billing_period_start": "BillingPeriodStart", "billing_period_end": "BillingPeriodEnd",
         "firm_matter_reference": "FirmMatterReference", "po_number": "PurchaseOrderAsQuoted",
         "fees_total": "FeesTotal", "expenses_total": "ExpensesTotal", "adjustments_total": "AdjustmentsTotal",
         "total_due": "TotalDue", "currency": "Currency"}
    header = {k: _cu_val(f.get(v)) for k, v in m.items()}
    lines = []
    for row in (f.get("TimeEntries") or {}).get("valueArray") or []:
        o = row.get("valueObject") or {}
        ln = {"type": "fee", "conf": {}}
        for k, src in (("date", "Date"), ("timekeeper", "TimekeeperAsBilled"), ("hours", "Hours"), ("rate", "Rate"),
                       ("amount", "Amount"), ("narrative", "Narrative"), ("activity_cls", "ActivityCodeClassified"),
                       ("activity_billed", "ActivityCodeAsBilled"), ("task_billed", "TaskCodeAsBilled")):
            ln[k], ln["conf"][k] = _cu_val(o.get(src))
        lines.append(ln)
    for row in (f.get("Expenses") or {}).get("valueArray") or []:
        o = row.get("valueObject") or {}
        ln = {"type": "expense", "conf": {}}
        for k, src in (("date", "Date"), ("hours", "Quantity"), ("rate", "UnitCost"), ("amount", "Amount"),
                       ("narrative", "Description")):
            ln[k], ln["conf"][k] = _cu_val(o.get(src))
        lines.append(ln)
    summary = []
    for row in (f.get("TimekeeperSummary") or {}).get("valueArray") or []:
        o = row.get("valueObject") or {}
        summary.append({"name": _cu_val(o.get("Name"))[0], "rate": _cu_val(o.get("Rate"))[0]})
    return {"header": header, "lines": lines, "header_scope": set(HEADER), "summary": summary}


def adapt_prebuilt(raw):
    f = _cu_contents(raw)
    if f is None:
        return None
    due = f.get("AmountDue") or f.get("TotalAmount")
    cur = None
    for src in (due, f.get("TotalAmount"), f.get("SubtotalAmount")):
        if src and (src.get("valueObject") or {}).get("CurrencyCode"):
            cur = _cu_val(src["valueObject"]["CurrencyCode"])
            break
    disc = _cu_val(f.get("TotalDiscountAmount")) if f.get("TotalDiscountAmount") else (None, None)
    header = {"firm": _cu_val(f.get("VendorName")), "firm_invoice_number": _cu_val(f.get("InvoiceId")),
              "invoice_date": _cu_val(f.get("InvoiceDate")), "po_number": _cu_val(f.get("PONumber")),
              "total_due": _cu_val(due), "currency": cur or (None, None),
              "adjustments_total": ((-disc[0] if isinstance(disc[0], (int, float)) else None), disc[1])}
    lines = []
    for row in (f.get("LineItems") or {}).get("valueArray") or []:
        o = row.get("valueObject") or {}
        ln = {"type": "any", "conf": {}}
        for k, src in (("date", "Date"), ("hours", "Quantity"), ("rate", "UnitPrice"), ("amount", "TotalAmount"),
                       ("narrative", "Description")):
            ln[k], ln["conf"][k] = _cu_val(o.get(src))
        ln["timekeeper"], ln["conf"]["timekeeper"] = None, None
        lines.append(ln)
    scope = {"firm", "firm_invoice_number", "invoice_date", "po_number", "total_due", "currency", "adjustments_total"}
    return {"header": header, "lines": lines, "header_scope": scope}


def adapt_legacy(raw):
    p = (raw or {}).get("parsed")
    if not p:
        return None
    c = p.get("extractionConfidence")
    h = p.get("header") or {}
    header = {"firm": (h.get("vendorName"), c), "firm_invoice_number": (h.get("invoiceNumber"), c),
              "invoice_date": (h.get("invoiceDate"), c), "total_due": (h.get("totalAmount"), c),
              "currency": (h.get("currency"), c)}
    lines = []
    for li in p.get("lineItems") or []:
        t = (li.get("costType") or "").lower()
        typ = "fee" if any(w in t for w in ("fee", "time", "professional", "service", "labor", "legal")) else "expense"
        ln = {"type": typ, "date": li.get("eventDate"), "timekeeper": None, "hours": li.get("hours"),
              "rate": li.get("rate"), "amount": li.get("amount"), "narrative": li.get("description")}
        ln["conf"] = {k: c for k in ("date", "hours", "rate", "amount", "narrative")}
        ln["conf"]["timekeeper"] = None
        lines.append(ln)
    return {"header": header, "lines": lines,
            "header_scope": {"firm", "firm_invoice_number", "invoice_date", "total_due", "currency"}}


def adapt_a2(raw):
    p = (raw or {}).get("parsed")
    if not p:
        return None
    header = {k: ((p["header"].get(k) or {}).get("value"), (p["header"].get(k) or {}).get("confidence")) for k in HEADER}
    lines = []
    for li in p.get("fee_lines") or []:
        ln = {"type": "fee", **{k: li.get(k) for k in ("date", "timekeeper", "hours", "rate", "amount", "narrative")}}
        ln["conf"] = {k: li.get("confidence") for k in ("date", "timekeeper", "hours", "rate", "amount")}
        lines.append(ln)
    for li in p.get("expense_lines") or []:
        ln = {"type": "expense", "date": li.get("date"), "timekeeper": None, "hours": li.get("quantity"),
              "rate": li.get("unit_cost"), "amount": li.get("amount"), "narrative": li.get("description")}
        ln["conf"] = {k: li.get("confidence") for k in ("date", "hours", "rate", "amount")}
        lines.append(ln)
    return {"header": header, "lines": lines, "header_scope": set(HEADER)}


ADAPTER = {"a": adapt_legacy, "a2": adapt_a2, "b": adapt_prebuilt, "c": adapt_custom, "cp": adapt_custom,
           "cls": adapt_custom, "rga": adapt_custom, "rpv": adapt_custom, "probe": adapt_custom,
           "crep": adapt_custom, "cprep": adapt_custom}


# ---------------------------------------------------------------- comparison

def header_correct(field, ext, gt) -> tuple[bool, str]:
    g = gt["header"].get(field)
    if field in NUM_F:
        e = to_num(ext)
        gv = to_num(g)
        if field == "total_due":
            alts = [gv, to_num(gt["header"].get("current_charges"))]
            return any(num_eq(e, a) for a in alts if a is not None) or (gv is None and e is None), ""
        if field == "adjustments_total":
            if (gv in (None, 0.0)) and (e in (None, 0.0)):
                return True, ""
            if num_eq(e, gv):
                return True, ""
            if e is not None and gv is not None and num_eq(abs(e), abs(gv)):
                return True, "sign"
            return False, ""
        if gv in (None,) and e in (None,):
            return True, ""
        if gv == 0.0 and e is None and field == "expenses_total":
            return True, ""
        return num_eq(e, gv), ""
    if field in DATE_F:
        return to_date(ext) == to_date(g), ""
    if field == "currency":
        return (str(ext or "").upper() or None) == (str(g or "").upper() or None), ""
    if field == "firm":
        return (norm_firm(ext) == norm_firm(g)) if g else not norm_text(ext), ""
    if field == "firm_invoice_number" and g and "," in str(g):
        return norm_text(ext) in [norm_text(x) for x in re.split(r"[,(]", str(g))], ""
    return norm_text(ext) == norm_text(g), ""


def norm_tk(s) -> str:
    """Timekeeper as billed, ignoring a trailing parenthetical (e.g. a title the group header appends)."""
    return norm_text(re.sub(r"\(.*?\)", "", str(s))) if s else ""


def tk_names(gl) -> set[str]:
    out = set()
    for k in ("timekeeper_as_billed", "timekeeper", "timekeeper_name"):
        if gl.get(k) and not re.fullmatch(r"TK\d+", str(gl[k])):
            out.add(norm_tk(gl[k]))
    return out


def line_field_ok(field, e, g) -> bool:
    if field == "date":
        return to_date(e.get("date")) == to_date(g.get("date"))
    if field == "timekeeper":
        return bool(norm_tk(e.get("timekeeper"))) and norm_tk(e.get("timekeeper")) in tk_names(g)
    return num_eq(to_num(e.get(field)), to_num(g.get(field)))


TEMPLATES = json.loads((HERE / "template_printed.json").read_text(encoding="utf-8"))["templates"]
# Real items: fields the document does not print on the line (from the labels' notes / reconciliation).
NOT_PRINTED_REAL = {"C-03": {"rate"}, "A-07": {"rate", "amount"}}


def printed(gt, line_type) -> set[str]:
    """Line fields the rendered document prints on the row (synthetic: template columns / group headers)."""
    if gt["set"] == "B":
        t = TEMPLATES[gt["document"]["template"]]
        return set(t["fee_printed"] if line_type == "fee" else t["expense_printed"])
    return set(FEE_F if line_type == "fee" else EXP_F) - NOT_PRINTED_REAL.get(gt["id"], set())


def complete(ext):
    """Deterministic completion any pipeline can apply after extraction (same for every arm):
    hh:mm repair (hours*rate != amount but (h + m/60)*rate == amount), rate from the arm's own timekeeper
    summary by timekeeper, else rate = amount / hours; hours = amount / rate when hours are missing;
    amount = hours * rate when the amount is missing."""
    summ = {}
    for row in ext.get("summary") or []:
        if row.get("name") and row.get("rate"):
            summ[norm_tk(row["name"])] = to_num(row["rate"])
            ini = re.search(r"\(([A-Za-z]{1,4})\)", str(row["name"]))   # "Name (AB)" -> rows billed as "AB"
            if ini:
                summ.setdefault(norm_text(ini.group(1)), to_num(row["rate"]))
    out = []
    for ln in ext["lines"]:
        ln = dict(ln)
        if ln["type"] != "expense":
            h, r, a = to_num(ln.get("hours")), to_num(ln.get("rate")), to_num(ln.get("amount"))
            if r is None:                                   # 1. rate from the invoice's own timekeeper summary
                r = summ.get(norm_tk(ln.get("timekeeper")))
            if h is not None and r is not None and a is not None and not num_eq(h * r, a, 0.02):
                hh = int(h) + round((h - int(h)) * 100) / 60    # 2. hh:mm read as a decimal
                if (h - int(h)) < 0.6 and num_eq(round(hh, 2) * r, a, 0.6):
                    h = round(hh, 2)
            if r is None and h and a is not None:           # 3. arithmetic fallbacks
                r = round(a / h, 2)
            if h is None and r and a is not None:
                h = round(a / r, 2)
            if a is None and h is not None and r is not None:
                a = round(h * r, 2)
            ln["hours"], ln["rate"], ln["amount"] = h, r, a
        out.append(ln)
    return out


def pair_score(e, g) -> float:
    s = 0.0
    if line_field_ok("date", e, g):
        s += 3
    if line_field_ok("amount", e, g):
        s += 3
    if line_field_ok("hours", e, g):
        s += 2
    if g.get("line_type") == "fee" and line_field_ok("timekeeper", e, g):
        s += 2
    if line_field_ok("rate", e, g):
        s += 1
    if s >= 3 and g.get("narrative") and e.get("narrative"):
        s += 2 * difflib.SequenceMatcher(None, str(e["narrative"])[:80].lower(), str(g["narrative"])[:80].lower()).ratio()
    return s


def match_lines(ext, gtl):
    pairs = []
    for i, e in enumerate(ext):
        for j, g in enumerate(gtl):
            if e["type"] not in ("any", g["line_type"]):
                continue
            s = pair_score(e, g)
            if s >= 4:
                pairs.append((s, i, j))
    pairs.sort(reverse=True)
    used_e, used_g, m = set(), set(), {}
    for s, i, j in pairs:
        if i in used_e or j in used_g:
            continue
        used_e.add(i)
        used_g.add(j)
        m[j] = i
    return m


def score_item(arm, gt) -> dict:
    raw = load_raw(arm, gt["id"])
    ext = ADAPTER[arm](raw) if raw else None
    full = load_full_gt(gt["id"])
    res = {"id": gt["id"], "extracted": ext is not None}
    gtl = [ln for ln in (full.get("lines") or []) if ln.get("line_type") in ("fee", "expense")]
    cal = []
    # header
    hdr = {}
    sign = 0
    for f in HEADER:
        val, conf = (ext["header"].get(f) or (None, None)) if ext else (None, None)
        ok, note = header_correct(f, val, gt)
        sign += note == "sign"
        hdr[f] = ok
        if ext and val not in (None, "") and conf is not None:
            cal.append((conf, ok, "header"))
    res["header"] = hdr
    res["header_sign_only"] = sign
    res["header_scope"] = sorted(ext["header_scope"]) if ext else []
    # lines: view "printed" = fields printed on the row, values as returned (the extraction measure);
    #        view "complete" = every GT field, after deterministic completion (what the pipeline can store)
    lf, lfc, lfr = {}, {}, {}
    nlines = {"fee": 0, "expense": 0}
    matched = {"fee": 0, "expense": 0}
    if gtl:
        m = match_lines(ext["lines"], gtl) if ext else {}
        comp = complete(ext) if ext else []
        for j, g in enumerate(gtl):
            t = g["line_type"]
            nlines[t] += 1
            fields = FEE_F if t == "fee" else EXP_F
            e = ext["lines"][m[j]] if j in m else None
            ec = comp[m[j]] if j in m else None
            matched[t] += e is not None
            pr = printed(gt, t)
            for f in fields:
                if g.get(f) is None or (f == "timekeeper" and not tk_names(g)):
                    continue
                key = f"{t}.{f}"
                okc = ec is not None and line_field_ok(f, ec, g)
                c, n = lfc.get(key, (0, 0))
                lfc[key] = (c + okc, n + 1)
                if f not in pr:
                    continue
                c, n = lfr.get(key, (0, 0))
                lfr[key] = (c + okc, n + 1)
                ok = e is not None and line_field_ok(f, e, g)
                c, n = lf.get(key, (0, 0))
                lf[key] = (c + ok, n + 1)
                if e is not None and e.get(f) not in (None, "") and (e["conf"].get(f) is not None):
                    cal.append((e["conf"][f], ok, "line"))
        res["extracted_lines"] = len(ext["lines"]) if ext else 0
        if ext and gt["id"] not in PARTIAL_GT:
            spurious = [ext["lines"][i] for i in range(len(ext["lines"])) if i not in set(m.values())]
            res["spurious_lines"] = len(spurious)
            for e in spurious:
                for f in (FEE_F if e["type"] != "expense" else EXP_F):
                    if e.get(f) not in (None, "") and e["conf"].get(f) is not None:
                        cal.append((e["conf"][f], False, "spurious"))
    res["line_fields_complete"] = {k: list(v) for k, v in lfc.items()}
    res["line_fields_repaired"] = {k: list(v) for k, v in lfr.items()}
    res["line_fields"] = {k: list(v) for k, v in lf.items()}
    res["gt_lines"] = nlines
    res["matched_lines"] = matched
    res["calibration"] = cal
    return res


BINS = [(0.0, 0.5), (0.5, 0.7), (0.7, 0.8), (0.8, 0.9), (0.9, 0.95), (0.95, 1.01)]


def pct(c, n):
    return round(100.0 * c / n, 1) if n else None


def p90(xs):
    xs = sorted(xs)
    if not xs:
        return None
    k = 0.9 * (len(xs) - 1)
    lo, hi = int(k), min(int(k) + 1, len(xs) - 1)
    return round(xs[lo] + (xs[hi] - xs[lo]) * (k - lo), 1)


def cost_of(arm, r) -> float | None:
    u = r.get("usage") or {}
    pt = PRICES["per_million_tokens"]
    if arm in ("a", "a2"):
        model = "gpt-4.1-mini" if arm == "a" else "gpt-5.2"
        di = PRICES["document_intelligence"]["read_pages_per_1k" if arm == "a" else "prebuilt_pages_per_1k"]
        cached = u.get("cached_tokens") or 0
        inp = (u.get("prompt_tokens") or 0) - cached
        return (di * (r.get("pages") or 0) / 1000 + inp * pt[model]["input"] / 1e6 + cached * pt[model]["cached_input"] / 1e6
                + (u.get("completion_tokens") or 0) * pt[model]["output"] / 1e6)
    if not u:
        return None
    cu = PRICES["content_understanding"]
    c = (u.get("documentPagesStandard") or 0) * cu["document_pages_standard_per_1k"] / 1000
    c += (u.get("documentPagesBasic") or 0) * cu["document_pages_basic_per_1k"] / 1000
    c += (u.get("documentPagesMinimal") or 0) * cu["document_pages_minimal_per_1k"] / 1000
    c += (u.get("contextualizationTokens") or 0) * cu["contextualization_tokens_standard_per_1k"] / 1000
    c += (u.get("advancedContextualizationTokens") or 0) * cu["contextualization_tokens_advanced_per_1k"] / 1000
    # CU usage.tokens keys: "<deployment>-input", "<deployment>-cached-input" (a subset of input, OpenAI
    # convention) and "<deployment>-output".
    toks = u.get("tokens") or {}
    for k, v in toks.items():
        if k.endswith("-cached-input"):
            continue
        model, kind = k.rsplit("-", 1)
        p = pt.get(model)
        if p is None:
            continue
        if kind == "output":
            c += v * p["output"] / 1e6
        else:
            cached = toks.get(f"{model}-cached-input", 0)
            c += (v - cached) * p["input"] / 1e6 + cached * p["cached_input"] / 1e6
    return c


def summarise(arm, ids=None) -> dict:
    items = [g for g in spike_items() if ids is None or g["id"] in ids]
    runs = {r["id"]: r for r in read_runs(arm)}
    per = [score_item(arm, g) for g in items]
    out = {"arm": arm, "items": len(items), "extracted": sum(p["extracted"] for p in per)}
    # header
    hc = {f: [0, 0] for f in HEADER}
    for p in per:
        for f, ok in p["header"].items():
            hc[f][0] += ok
            hc[f][1] += 1
    out["header_by_field"] = {f: pct(*v) for f, v in hc.items()}
    tot_c = sum(v[0] for v in hc.values())
    tot_n = sum(v[1] for v in hc.values())
    out["header_accuracy"] = pct(tot_c, tot_n)
    scope = set(per[0]["header_scope"]) if per and per[0]["header_scope"] else set()
    for p in per:
        if p["header_scope"]:
            scope = set(p["header_scope"])
            break
    sc = sum(hc[f][0] for f in scope)
    sn = sum(hc[f][1] for f in scope)
    out["header_accuracy_in_schema"] = pct(sc, sn)
    out["header_schema_fields"] = len(scope)
    out["header_sign_only"] = sum(p["header_sign_only"] for p in per)
    # lines
    lf = {}
    for p in per:
        for k, (c, n) in p["line_fields"].items():
            a = lf.setdefault(k, [0, 0])
            a[0] += c
            a[1] += n
    out["line_by_field"] = {k: {"acc": pct(*v), "n": v[1]} for k, v in sorted(lf.items())}
    lfc = {}
    for p in per:
        for k, (c, n) in p["line_fields_complete"].items():
            a = lfc.setdefault(k, [0, 0])
            a[0] += c
            a[1] += n
    out["complete_line_by_field"] = {k: {"acc": pct(*v), "n": v[1]} for k, v in sorted(lfc.items())}
    lfrr = {}
    for p in per:
        for k, (c, n) in p["line_fields_repaired"].items():
            a = lfrr.setdefault(k, [0, 0])
            a[0] += c
            a[1] += n
    out["repaired_line_by_field"] = {k: {"acc": pct(*v), "n": v[1]} for k, v in sorted(lfrr.items())}
    feer = [lfrr.get(f"fee.{f}", [0, 0]) for f in FEE_F]
    out["repaired_fee_line_accuracy"] = pct(sum(x[0] for x in feer), sum(x[1] for x in feer))
    big = {g["id"] for g in items if (g["document"].get("pages") or 0) > 50}
    lfx = {}
    for p in per:
        if p["id"] in big:
            continue
        for k, (c, n) in p["line_fields_repaired"].items():
            a = lfx.setdefault(k, [0, 0])
            a[0] += c
            a[1] += n
    out["repaired_line_by_field_excl_over_50_pages"] = {"excluded": sorted(big),
                                                        **{k: pct(*v) for k, v in sorted(lfx.items())}}
    feec = [lfc.get(f"fee.{f}", [0, 0]) for f in FEE_F]
    out["complete_fee_line_accuracy"] = pct(sum(x[0] for x in feec), sum(x[1] for x in feec))
    fee = [lf.get(f"fee.{f}", [0, 0]) for f in FEE_F]
    out["fee_line_accuracy"] = pct(sum(x[0] for x in fee), sum(x[1] for x in fee))
    allf = list(lf.values())
    out["line_accuracy_all"] = pct(sum(x[0] for x in allf), sum(x[1] for x in allf))
    gl = sum(p["gt_lines"]["fee"] + p["gt_lines"]["expense"] for p in per)
    ml = sum(p["matched_lines"]["fee"] + p["matched_lines"]["expense"] for p in per)
    out["line_recall"] = pct(ml, gl)
    out["gt_lines"] = gl
    out["spurious_lines"] = sum(p.get("spurious_lines", 0) for p in per)
    # calibration
    cal = [c for p in per for c in p["calibration"]]
    bins = []
    for lo, hi in BINS:
        b = [ok for conf, ok, _ in cal if lo <= conf < hi]
        bins.append({"bin": f"{lo:.2f}-{min(hi, 1.0):.2f}", "n": len(b), "wrong": b.count(False),
                     "wrong_rate": pct(b.count(False), len(b))})
    out["calibration_bins"] = bins
    hi = [ok for conf, ok, _ in cal if conf >= 0.90]
    out["conf_ge_090"] = {"n": len(hi), "wrong": hi.count(False), "wrong_rate": pct(hi.count(False), len(hi)),
                          "share_of_scored_values": pct(len(hi), len(cal))}
    hi_line = [ok for conf, ok, src in cal if conf >= 0.90 and src != "header"]
    out["conf_ge_090_lines"] = {"n": len(hi_line), "wrong": hi_line.count(False), "wrong_rate": pct(hi_line.count(False), len(hi_line))}
    hi_hdr = [ok for conf, ok, src in cal if conf >= 0.90 and src == "header"]
    out["conf_ge_090_header"] = {"n": len(hi_hdr), "wrong": hi_hdr.count(False), "wrong_rate": pct(hi_hdr.count(False), len(hi_hdr))}
    correct = [conf for conf, ok, _ in cal if ok]
    out["correct_values_at_ge_090"] = pct(sum(1 for c in correct if c >= 0.90), len(correct))
    # latency / cost
    lat = [runs[g["id"]]["latency_s"] for g in items if runs.get(g["id"], {}).get("status") == "Succeeded"]
    out["succeeded"] = len(lat)
    out["failed"] = [(g["id"], runs.get(g["id"], {}).get("status"), runs.get(g["id"], {}).get("error"))
                     for g in items if runs.get(g["id"], {}).get("status") != "Succeeded"]
    out["latency_p50_s"] = round(statistics.median(lat), 1) if lat else None
    out["latency_p90_s"] = p90(lat)
    out["latency_max_s"] = round(max(lat), 1) if lat else None
    costs = [cost_of(arm, runs[g["id"]]) for g in items if runs.get(g["id"], {}).get("status") == "Succeeded"]
    costs = [c for c in costs if c is not None]
    out["cost_mean_usd"] = round(statistics.mean(costs), 4) if costs else None
    out["cost_median_usd"] = round(statistics.median(costs), 4) if costs else None
    out["cost_total_usd"] = round(sum(costs), 2) if costs else None
    small = [cost_of(arm, runs[g["id"]]) for g in items if g["set"] == "B" and (g["document"].get("pages") or 99) <= 4
             and runs.get(g["id"], {}).get("status") == "Succeeded"]
    small = [c for c in small if c is not None]
    out["cost_mean_usd_typical_1to4_pages"] = round(statistics.mean(small), 4) if small else None
    # per item (numbers only)
    out["per_item"] = [{"id": p["id"], "extracted": p["extracted"],
                        "header_correct": sum(p["header"].values()), "header_n": len(p["header"]),
                        "gt_lines": p["gt_lines"], "matched": p["matched_lines"],
                        "spurious": p.get("spurious_lines"), "extracted_lines": p.get("extracted_lines"),
                        "line_fields": p["line_fields"], "line_fields_complete": p["line_fields_complete"],
                        "line_fields_repaired": p["line_fields_repaired"],
                        "latency_s": runs.get(p["id"], {}).get("latency_s"),
                        "status": runs.get(p["id"], {}).get("status"),
                        "cost_usd": round(cost_of(arm, runs[p["id"]]) or 0, 4) if p["id"] in runs else None}
                       for p in per]
    return out


def main():
    arms = sys.argv[1:]
    if arms == ["--all"]:
        arms = ["a", "a2", "b", "c", "cp"]
    RESULTS.mkdir(exist_ok=True)
    for arm in arms:
        s = summarise(arm)
        (RESULTS / f"score-{arm}.json").write_text(json.dumps(s, indent=1), encoding="utf-8")
        print(f"\n== {arm}: items {s['items']} extracted {s['extracted']} succeeded {s['succeeded']} failed {s['failed']}")
        print(f"header {s['header_accuracy']}% (in-schema {s['header_accuracy_in_schema']}% of {s['header_schema_fields']} fields) "
              f"sign-only {s['header_sign_only']}")
        print("header by field", s["header_by_field"])
        print(f"fee-line acc {s['fee_line_accuracy']}%  all-line acc {s['line_accuracy_all']}%  recall {s['line_recall']}% "
              f"of {s['gt_lines']}  spurious {s['spurious_lines']}")
        print("line by field (printed, as returned)", {k: v["acc"] for k, v in s["line_by_field"].items()})
        print(f"repaired (printed fields, deterministic repair) fee-line acc {s['repaired_fee_line_accuracy']}%",
              {k: v["acc"] for k, v in s["repaired_line_by_field"].items()})
        print("repaired excl >50 pages", s["repaired_line_by_field_excl_over_50_pages"])
        print(f"complete fee-line acc {s['complete_fee_line_accuracy']}%", {k: v["acc"] for k, v in s["complete_line_by_field"].items()})
        print("conf>=0.90", s["conf_ge_090"], "lines", s["conf_ge_090_lines"], "header", s["conf_ge_090_header"],
              "correct@>=0.90", s["correct_values_at_ge_090"])
        print("bins", [(b["bin"], b["n"], b["wrong_rate"]) for b in s["calibration_bins"]])
        print(f"latency p50 {s['latency_p50_s']} p90 {s['latency_p90_s']} max {s['latency_max_s']}  "
              f"cost mean {s['cost_mean_usd']} median {s['cost_median_usd']} typical {s['cost_mean_usd_typical_1to4_pages']} "
              f"total {s['cost_total_usd']}")


if __name__ == "__main__":
    main()
