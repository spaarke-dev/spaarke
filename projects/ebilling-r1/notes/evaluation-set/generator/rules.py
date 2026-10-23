"""Rule sets, the reference evaluator (ground-truth oracle) and the synthetic invoice builder.

The evaluator is the single definition of what a seeded or clean invoice must produce. Set F ground truth is the
evaluator's output on the final invoice, and every generated item is verified two ways: clean invoices must yield
zero findings, seeded invoices must yield exactly one finding per injection. `gen_eval_set.py verify` re-runs the
evaluator over the committed files. Evaluation tooling only - production rule schemas are task 070's.
"""
import datetime as dt
from decimal import Decimal, ROUND_HALF_UP

import layout
import refdata as R

D = Decimal


def r2(x):
    return float(D(str(x)).quantize(D("0.01"), ROUND_HALF_UP))


def amount_of(hours, rate):
    return r2(D(str(hours)) * D(str(rate)))


def iso(d):
    return d.isoformat()


def parse(s):
    return dt.date.fromisoformat(s)


# ------------------------------------------------------------------ rule sets
# Keyword phrases are exact substrings of refdata.CLERICAL / INTERNAL narratives; clean narratives must not contain them.
KW_CLERICAL = ["scheduled conference call", "calendar entries", "docketed deadlines", "conflict check",
               "invoice preparation", "photocopied exhibits", "organized and indexed"]
KW_INTERNAL = ["internal conference"]


def _rule(rs_id, n, rule_type, params, consequence):
    return {"rule_id": f"{rs_id}-R{n:02d}", "rule_type": rule_type, "params": params, "consequence": consequence,
            "expressible": True}


def _caps(scale):
    base = {"Partner": 650, "Of Counsel": 575, "Senior Associate": 450, "Associate": 350, "Paralegal": 185,
            "Legal Assistant": 150}
    return {k: int(round(v * scale / 5.0)) * 5 for k, v in base.items()}


def make_rule_sets():
    sets = []

    def add(rs_id, name, modelled_on, specs):
        rules = [_rule(rs_id, i + 1, t, p, c) for i, (t, p, c) in enumerate(specs)]
        sets.append({"schema": "ebilling-eval/1", "kind": "rule-set", "id": rs_id, "set": "F",
                     "meta": {"title": name, "generator_id": layout.GENERATOR_ID, "licence": "Synthetic - authored for this evaluation set",
                              "licence_status": "synthetic", "pii_note": "No personal data.", "pii_scan": None,
                              "storage": {"location": "committed", "path": f"ground-truth/rule-sets/{rs_id}.json"},
                              "spike": False, "label_method": "generator"},
                     "modelled_on": modelled_on, "rules": rules, "unmapped_provisions": []})

    add("RS-01", "County-style guideline", ["E-OCG-01", "E-OCG-05", "E-OCG-12"], [
        ("TimeIncrement", {"increment_hours": 0.1}, "Reduce"),
        ("AiObservation", {"observations": ["block_billing", "vague_description"]}, "Flag"),
        ("NonBillableActivity", {"keywords": KW_CLERICAL}, "Disallow"),
        ("ExpenseRule", {"non_reimbursable": ["E103", "E104", "E106"]}, "Disallow"),
        ("ExpenseRule", {"caps": {"E101": 0.15}}, "Reduce"),
        ("InvoiceAge", {"max_days_after_period_end": 45}, "Flag"),
        ("Duplicate", {"match_on": ["timekeeper", "date", "hours", "narrative"]}, "Disallow"),
        ("RequiredData", {"fields": ["po_number"]}, "Reject")])
    add("RS-02", "University-style guideline", ["E-OCG-10", "E-OCG-16"], [
        ("ApprovedTimekeeper", {"approved_names": "context.approved_timekeepers"}, "Reject"),
        ("ApprovedRate", {"rates": _caps(1.0)}, "Reduce"),
        ("TimeIncrement", {"increment_hours": 0.1}, "Reduce"),
        ("DailyHoursCap", {"max_hours_per_timekeeper_per_day": 10}, "Reduce"),
        ("RequiredData", {"codes_required": True}, "Reject"),
        ("CoreRecordWindow", {"require_within_matter_dates": True, "budget_cap": True}, "Flag"),
        ("Duplicate", {"match_on": ["timekeeper", "date", "hours", "narrative"]}, "Disallow"),
        ("InvoiceAge", {"max_days_after_period_end": 30, "max_days_work_age": 90}, "Flag")])
    add("RS-03", "State-agency style guideline", ["E-OCG-09", "E-OCG-14"], [
        ("ApprovedRate", {"rates": _caps(0.8)}, "Reduce"),
        ("TimeIncrement", {"increment_hours": 0.1}, "Reduce"),
        ("DailyHoursCap", {"max_hours_per_timekeeper_per_day": 8}, "Reduce"),
        ("NonBillableActivity", {"keywords": KW_CLERICAL + KW_INTERNAL}, "Disallow"),
        ("ExpenseRule", {"non_reimbursable": ["E103", "E106"]}, "Disallow"),
        ("ExpenseRule", {"caps": {"E101": 0.10, "E111": 60.0}}, "Reduce"),
        ("AiObservation", {"observations": ["block_billing"]}, "Flag"),
        ("RequiredData", {"fields": ["po_number"]}, "Reject")])
    add("RS-04", "Quarter-hour panel guideline", ["E-OCG-03", "E-OCG-07"], [
        ("ApprovedTimekeeper", {"approved_names": "context.approved_timekeepers"}, "Reject"),
        ("ApprovedRate", {"rates": _caps(1.15)}, "Reduce"),
        ("TimeIncrement", {"increment_hours": 0.25}, "Reduce"),
        ("CoreRecordWindow", {"require_within_matter_dates": True, "budget_cap": True}, "Flag"),
        ("InvoiceAge", {"max_days_after_period_end": 60}, "Flag"),
        ("Duplicate", {"match_on": ["timekeeper", "date", "hours", "narrative"]}, "Disallow"),
        ("ExpenseRule", {"non_reimbursable": ["E104", "E106"], "caps": {"E101": 0.15}}, "Disallow"),
        ("NonBillableActivity", {"keywords": KW_CLERICAL}, "Disallow")])
    add("RS-05", "Insurer-style guideline", ["E-OCG-04", "E-OCG-13"], [
        ("ApprovedRate", {"rates": _caps(0.9)}, "Reduce"),
        ("TimeIncrement", {"increment_hours": 0.1}, "Reduce"),
        ("AiObservation", {"observations": ["block_billing", "vague_description"]}, "Reduce"),
        ("NonBillableActivity", {"keywords": KW_CLERICAL + KW_INTERNAL}, "Disallow"),
        ("Duplicate", {"match_on": ["timekeeper", "date", "hours", "narrative"]}, "Disallow"),
        ("DailyHoursCap", {"max_hours_per_timekeeper_per_day": 12}, "Flag"),
        ("RequiredData", {"codes_required": True, "fields": ["po_number"]}, "Reject"),
        ("InvoiceAge", {"max_days_work_age": 120}, "Disallow")])
    add("RS-06", "Public-defender style guideline", ["E-OCG-02", "E-OCG-06", "E-OCG-08"], [
        ("ApprovedTimekeeper", {"approved_names": "context.approved_timekeepers"}, "Reject"),
        ("ApprovedRate", {"rates": _caps(0.7)}, "Reduce"),
        ("ExpenseRule", {"non_reimbursable": ["E103", "E104", "E106"]}, "Disallow"),
        ("ExpenseRule", {"caps": {"E101": 0.15, "E107": 40.0}}, "Reduce"),
        ("CoreRecordWindow", {"require_within_matter_dates": True, "budget_cap": True}, "Flag"),
        ("InvoiceAge", {"max_days_after_period_end": 30}, "Flag"),
        ("DailyHoursCap", {"max_hours_per_timekeeper_per_day": 10}, "Reduce"),
        ("TimeIncrement", {"increment_hours": 0.1}, "Reduce"),
        ("Duplicate", {"match_on": ["timekeeper", "date", "hours", "narrative"]}, "Disallow"),
        ("RequiredData", {"codes_required": True}, "Reject"),
        ("AiObservation", {"observations": ["vague_description"]}, "Flag")])
    return sets


def rules_of(rs, rule_type):
    return [r for r in rs["rules"] if r["rule_type"] == rule_type]


def check_clean_bank(rule_sets):
    """Clean narratives must not contain any NonBillableActivity keyword."""
    kws = {k for rs in rule_sets for r in rules_of(rs, "NonBillableActivity") for k in r["params"]["keywords"]}
    for code, items in R.CLEAR.items():
        for _, text in items:
            low = text.lower()
            for k in kws:
                assert k.lower() not in low, f"clean narrative {code} contains keyword {k}"


# ------------------------------------------------------------------ evaluator
def evaluate(inv, rs):
    """Return findings for invoice dict `inv` (ground-truth shape) under rule set `rs`."""
    ctx = inv.get("context", {})
    hdr = inv["header"]
    lines = inv["lines"]
    fee = [l for l in lines if l["line_type"] == "fee"]
    exp = [l for l in lines if l["line_type"] == "expense"]
    out = []

    def add(rule, scope, n=None, related=None):
        f = {"rule_id": rule["rule_id"], "rule_type": rule["rule_type"], "scope": scope,
             "line_refs": [n] if n is not None else [], "consequence": rule["consequence"]}
        if related:
            f["related_line_refs"] = related
        out.append(f)

    for rule in rs["rules"]:
        t, p = rule["rule_type"], rule["params"]
        if t == "ApprovedTimekeeper":
            approved = set(ctx.get("approved_timekeepers", []))
            for l in fee:
                if l.get("timekeeper_name") and l["timekeeper_name"] not in approved:
                    add(rule, "line", l["n"])
        elif t == "ApprovedRate":
            for l in fee:
                cap = p["rates"].get(l.get("timekeeper_title"))
                if cap is not None and l["rate"] is not None and l["rate"] > cap:
                    add(rule, "line", l["n"])
        elif t == "TimeIncrement":
            inc = D(str(p["increment_hours"]))
            for l in fee:
                if l["hours"] is not None and D(str(l["hours"])) % inc != 0:
                    add(rule, "line", l["n"])
        elif t == "DailyHoursCap":
            cap = D(str(p["max_hours_per_timekeeper_per_day"]))
            cum, hit = {}, set()
            for l in sorted(fee, key=lambda x: x["n"]):
                if l["hours"] is None or not l.get("timekeeper_name"):
                    continue
                k = (l["timekeeper_name"], l["date"])
                cum[k] = cum.get(k, D(0)) + D(str(l["hours"]))
                if cum[k] > cap and k not in hit:
                    hit.add(k)
                    add(rule, "line", l["n"])
        elif t == "NonBillableActivity":
            for l in fee:
                nar = (l.get("narrative") or "").lower()
                if any(k.lower() in nar for k in p.get("keywords", [])) or \
                        any(c in p.get("activity_codes", []) for c in l["codes"]):
                    add(rule, "line", l["n"])
        elif t == "ExpenseRule":
            for l in exp:
                code = l["codes"][0] if l["codes"] else None
                if code in p.get("non_reimbursable", []):
                    add(rule, "line", l["n"])
                elif code in p.get("caps", {}) and l["rate"] is not None and l["rate"] > p["caps"][code]:
                    add(rule, "line", l["n"])
        elif t == "InvoiceAge":
            rec = parse(ctx["invoice_received_date"])
            if "max_days_after_period_end" in p and \
                    (rec - parse(hdr["billing_period_end"])).days > p["max_days_after_period_end"]:
                add(rule, "invoice")
            if "max_days_work_age" in p:
                for l in fee + exp:
                    if l["date"] and (rec - parse(l["date"])).days > p["max_days_work_age"]:
                        add(rule, "line", l["n"])
        elif t == "CoreRecordWindow":
            if p.get("require_within_matter_dates"):
                lo, hi = ctx["matter_open"], ctx["matter_close"]
                for l in fee + exp:
                    if l["date"] and not (lo <= l["date"] <= hi):
                        add(rule, "line", l["n"])
            if p.get("budget_cap") and hdr["total_due"] > ctx["matter_budget"]:
                add(rule, "invoice")
        elif t == "Duplicate":
            seen = {}
            for l in sorted(fee, key=lambda x: x["n"]):
                key = tuple(l.get({"timekeeper": "timekeeper_name"}.get(m, m)) for m in p["match_on"])
                if key in seen:
                    add(rule, "line", l["n"], [seen[key]])
                else:
                    seen[key] = l["n"]
        elif t == "RequiredData":
            if p.get("codes_required"):
                for l in fee:
                    if l["hours"] is not None and not any(c.startswith("L") for c in l["codes"]):
                        add(rule, "line", l["n"])
            for f in p.get("fields", []):
                if hdr.get(f) in (None, ""):
                    add(rule, "invoice")
        elif t == "AiObservation":
            want = {"block_billing": "block_billed", "vague_description": "vague"}
            styles = {want[o] for o in p["observations"]}
            for l in fee:
                if l.get("narrative_style") in styles:
                    add(rule, "line", l["n"])
    return out


# ------------------------------------------------------------------ builder
def make_firm(rng, caps=None):
    name = f"{rng.choice(R.FIRM_A)} {rng.choice(['&', 'and'])} {rng.choice(R.FIRM_B)} LLP"
    if rng.random() < 0.25:
        name = f"{rng.choice(R.FIRM_A)} {rng.choice(R.FIRM_B)} PC"
    partner = rng.choice(range(450, 951, 25))
    tks, used = [], set()
    titles = ["Partner", "Partner", "Of Counsel", "Senior Associate", "Associate", "Associate", "Associate",
              "Paralegal", "Paralegal", "Legal Assistant"]
    rng.shuffle(titles)
    for title in titles[: rng.randint(6, 9)]:
        for _ in range(20):
            nm = f"{rng.choice(R.FIRST)} {rng.choice(R.LAST)}"
            if nm not in used:
                used.add(nm)
                break
        factor = [t[2] for t in R.TITLES if t[0] == title][0]
        rate = int(round(partner * factor * rng.uniform(0.92, 1.05) / 5.0)) * 5
        if caps:
            rate = int(round(caps[title] * rng.uniform(0.72, 1.0) / 5.0)) * 5
        tks.append({"name": nm, "title": title, "rate": float(rate)})
    return {"name": name, "address": [f"{rng.randint(10, 999)} {rng.choice(R.STREETS)}", f"{rng.choice(R.CITIES)}",
                                      f"Tax ID {rng.randint(10, 99)}-{rng.randint(1000000, 9999999)}"], "tks": tks}


def business_days(start, end):
    d, out = start, []
    while d <= end:
        if d.weekday() < 5:
            out.append(d)
        d += dt.timedelta(days=1)
    return out


def fill(rng, text, tk_name=None, n=None):
    return text.format(topic=rng.choice(R.TOPICS), party=rng.choice(R.PARTIES), doc=rng.choice(R.DOCS),
                       city=rng.choice(R.CITIES), tk=tk_name or "associate", n=n if n is not None else rng.randint(30, 900))


def narrative(rng, style, task_codes, tk_name=None):
    """Return (text, activity, task, style_used)."""
    if style == "vague":
        return rng.choice(R.VAGUE), rng.choice(["A104", "A111", "A108"]), rng.choice(task_codes), "vague"
    if style == "clerical":
        act, text = rng.choice(R.CLERICAL)
        return text, act, rng.choice(task_codes), "clerical"
    if style == "internal":
        return fill(rng, R.INTERNAL, tk_name), "A105", rng.choice(task_codes), "internal"
    if style == "travel":
        return fill(rng, "Travel to {city} for deposition of {party} witness"), "A112", "L330", "travel"
    if style == "block_billed":
        picks = [rng.choice(R.CLEAR[rng.choice(task_codes)]) for _ in range(rng.randint(3, 4))]
        task = rng.choice(task_codes)
        text = "; ".join(fill(rng, t[1]).rstrip(".") for t in picks) + "."
        return text, picks[0][0], task, "block_billed"
    task = rng.choice(task_codes)
    act, text = rng.choice(R.CLEAR[task])
    text = fill(rng, text)
    if style == "non_english":
        text = fill(rng, rng.choice(R.NON_ENGLISH))
    return text, act, task, "clear" if style != "non_english" else "non_english"


def pick_increment_hours(rng, inc, style):
    lo, hi = (0.1, 3.0) if style not in ("block_billed",) else (2.0, 5.0)
    if style == "vague":
        hi = 1.0
    steps = max(1, int(round(rng.uniform(lo, hi) / inc)))
    return float(D(str(inc)) * steps)


def build_invoice(rng, inv_id, set_letter, tpl_idx, rs=None, n_fee=None, n_exp=None, profile=None, start_date=None,
                  seed_note=""):
    """Build a clean invoice satisfying every rule of `rs` (if given) in ground-truth shape.

    `profile` (set B only) controls style mix, currency, code mode, hour increment and special features."""
    tpl = layout.TEMPLATES[tpl_idx]
    p = profile or {}
    caps = None
    if rs:
        rr = rules_of(rs, "ApprovedRate")
        caps = rr[0]["params"]["rates"] if rr else None
    firm = make_firm(rng, caps)
    tks = firm["tks"]
    base = start_date or dt.date(2025, 3, 1) + dt.timedelta(days=rng.randint(0, 520))
    pstart = base.replace(day=1)
    nm = (pstart.replace(day=28) + dt.timedelta(days=4)).replace(day=1)
    pend = nm - dt.timedelta(days=1)
    days = business_days(pstart, pend)

    # rule-derived knobs
    inc = 0.1
    daily_soft = 8.0
    non_reimb, ecaps, max_after, max_age = set(), {}, None, None
    code_mode = p.get("code_mode", "task_activity")
    if rs:
        ti = rules_of(rs, "TimeIncrement")
        inc = ti[0]["params"]["increment_hours"] if ti else 0.1
        dh = rules_of(rs, "DailyHoursCap")
        if dh:
            daily_soft = min(daily_soft, dh[0]["params"]["max_hours_per_timekeeper_per_day"] * 0.7)
        for r in rules_of(rs, "ExpenseRule"):
            non_reimb |= set(r["params"].get("non_reimbursable", []))
            ecaps.update(r["params"].get("caps", {}))
        for r in rules_of(rs, "InvoiceAge"):
            max_after = r["params"].get("max_days_after_period_end", max_after)
            max_age = r["params"].get("max_days_work_age", max_age)
        code_mode = "task_activity"
    else:
        inc = p.get("increment", 0.1)

    style_w = p.get("styles", {"clear": 1.0}) if not rs else {"clear": 1.0}
    styles, weights = zip(*style_w.items())
    phase = rng.choice(list(R.CODES_BY_PHASE))
    task_codes = R.CODES_BY_PHASE[phase]
    n_fee = n_fee or rng.randint(8, 16)
    n_exp = rng.randint(1, 4) if n_exp is None else n_exp
    load = {}
    lines, uid = [], 0
    weights_tk = [{"Partner": 2, "Of Counsel": 1, "Senior Associate": 3, "Associate": 4, "Paralegal": 3,
                   "Legal Assistant": 1}[t["title"]] for t in tks]
    appr = None
    if rs and rules_of(rs, "ApprovedTimekeeper"):
        k = max(4, len(tks) - 1)
        appr = [t["name"] for t in tks[:k]]
        tks_pool = tks[:k]
        weights_pool = weights_tk[:k]
    else:
        tks_pool, weights_pool = tks, weights_tk
    used_tk = {}
    kw_all = {k.lower() for r in (rules_of(rs, "NonBillableActivity") if rs else []) for k in r["params"]["keywords"]}

    for _ in range(n_fee):
        style = rng.choices(styles, weights)[0]
        for _try in range(30):
            tk = rng.choices(tks_pool, weights_pool)[0]
            d = rng.choice(days)
            text, act, task, used_style = narrative(rng, style, task_codes, tk["name"])
            hrs = pick_increment_hours(rng, inc, used_style)
            if used_style == "travel":
                hrs = pick_increment_hours(rng, inc, "clear")
            if load.get((tk["name"], d), 0.0) + hrs <= daily_soft and not any(k in text.lower() for k in kw_all):
                break
        load[(tk["name"], d)] = load.get((tk["name"], d), 0.0) + hrs
        uid += 1
        codes = [task, act] if code_mode == "task_activity" else ([task] if code_mode == "task_only" else [])
        lines.append({"_uid": uid, "line_type": "fee", "date": iso(d), "tk": tk, "hours": hrs, "rate": tk["rate"],
                      "amount": amount_of(hrs, tk["rate"]), "narrative": text, "codes": codes,
                      "intended": [task, act], "narrative_style": used_style})
        used_tk[tk["name"]] = tk
    # flat fee line (set B feature)
    if p.get("flat_fee"):
        uid += 1
        amt = float(rng.choice([1500, 2500, 4800, 7500]))
        lines.append({"_uid": uid, "line_type": "fee", "date": iso(rng.choice(days)), "tk": None, "hours": None,
                      "rate": None, "amount": amt, "narrative": fill(rng, "Flat fee for preparation of {doc} in {topic}"),
                      "codes": ["L210"] if code_mode != "none" else [], "intended": ["L210", "A103"],
                      "narrative_style": "clear"})
    # expenses
    pool = [c for c in R.EXPENSE_BANK if c not in non_reimb]
    for _ in range(n_exp):
        code = rng.choice(pool)
        desc, (ulo, uhi), (qlo, qhi) = R.EXPENSE_BANK[code]
        unit = r2(rng.uniform(ulo, uhi))
        if code in ecaps:
            unit = min(unit, r2(ecaps[code] * rng.uniform(0.6, 0.95)))
        qty = rng.randint(qlo, qhi)
        uid += 1
        text = fill(rng, desc, n=qty)
        lines.append({"_uid": uid, "line_type": "expense", "date": iso(rng.choice(days)), "tk": None,
                      "hours": float(qty), "rate": unit, "amount": amount_of(qty, unit), "narrative": text,
                      "codes": [code], "intended": [code], "narrative_style": "clear"})
    # adjustments (set B feature)
    fees_sum = r2(sum(l["amount"] for l in lines if l["line_type"] == "fee"))
    if p.get("adjustment"):
        uid += 1
        lines.append({"_uid": uid, "line_type": "adjustment", "date": None, "tk": None, "hours": None, "rate": None,
                      "amount": -r2(fees_sum * p["adjustment"]), "narrative": "Courtesy discount",
                      "codes": [], "intended": [], "narrative_style": "clear"})

    rec_gap = rng.randint(3, 20 if not max_after else min(20, max_after - 3))
    inv_date = pend + dt.timedelta(days=rng.randint(1, 4))
    received = pend + dt.timedelta(days=rec_gap)
    if received < inv_date:
        received = inv_date
    if max_after is not None and (received - pend).days > max_after:
        received = pend + dt.timedelta(days=max_after - 1)
        inv_date = min(inv_date, received)
    cur = p.get("currency", "USD")
    inv = {
        "schema": "ebilling-eval/1", "kind": "invoice", "id": inv_id, "set": set_letter,
        "firm": firm, "tpl": tpl_idx, "_tks": used_tk,
        "header": {
            "firm": firm["name"], "firm_invoice_number": layout.invoice_number(tpl, rng),
            "invoice_date": iso(inv_date), "billing_period_start": iso(pstart), "billing_period_end": iso(pend),
            "firm_matter_reference": f"{rng.randint(10000, 99999)}-{rng.randint(1, 40):04d}",
            "po_number": (f"PO-{rng.randint(4500000, 4599999)}" if (rs or p.get("po", rng.random() < 0.5)) else None),
            "currency": cur},
        "lines": lines,
        "context": {"matter_open": iso(pstart - dt.timedelta(days=rng.randint(200, 500))),
                    "matter_close": iso(pend + dt.timedelta(days=90)),
                    "matter_budget": 0.0, "approved_timekeepers": appr or [t["name"] for t in tks],
                    "invoice_received_date": iso(received)},
        "_pool": tks_pool, "_task_codes": task_codes, "_days": days, "_inc": inc, "_caps": caps,
    }
    return inv


def finalize(inv, rs=None):
    """Order lines as printed, number them, compute header totals and the as-billed fields."""
    tpl = layout.TEMPLATES[inv["tpl"]]
    lines = layout.order_lines(inv["lines"], tpl)
    out_lines = []
    for n, l in enumerate(lines, 1):
        tk = l["tk"]
        cap = layout.code_capability(tpl)
        codes = l["codes"]
        if cap == "none" or (cap == "task_only" and l["line_type"] != "fee"):
            codes = []
        elif cap == "task_only":
            codes = [l["intended"][0]]
        o = {"n": n, "line_type": l["line_type"], "date": l["date"],
             "timekeeper": layout.billed_name(tk["name"], tpl) if tk else None,
             "timekeeper_name": tk["name"] if tk else None, "timekeeper_title": tk["title"] if tk else None,
             "title": layout.billed_title(tk["title"], tpl) if tk else None,
             "hours": l["hours"], "rate": l["rate"], "amount": l["amount"], "narrative": l["narrative"],
             "codes": codes, "page": None, "narrative_style": l["narrative_style"], "_uid": l["_uid"],
             "intended": l["intended"]}
        out_lines.append(o)
    inv["lines"] = out_lines
    fees = r2(sum(l["amount"] for l in out_lines if l["line_type"] == "fee"))
    exps = r2(sum(l["amount"] for l in out_lines if l["line_type"] == "expense"))
    adj = r2(sum(l["amount"] for l in out_lines if l["line_type"] == "adjustment"))
    h = inv["header"]
    h.update({"fees_total": fees, "expenses_total": exps, "adjustments_total": adj, "total_due": r2(fees + exps + adj)})
    if inv.get("_budget_breach"):
        inv["context"]["matter_budget"] = float(int(h["total_due"] * 0.8))
    elif inv["context"]["matter_budget"] == 0.0:
        inv["context"]["matter_budget"] = float(int(h["total_due"] * 4 + 25000))
    return inv


# ------------------------------------------------------------------ injectors (set F)
def _pick_fee(rng, inv, exclude=()):
    c = [l for l in inv["lines"] if l["line_type"] == "fee" and l["hours"] is not None and l["_uid"] not in exclude]
    return rng.choice(c)


def _new_uid(inv):
    return max(l["_uid"] for l in inv["lines"]) + 1


def inject(rng, inv, rs, rule_type, taken):
    """Mutate `inv` (pre-finalize, internal shape) to violate `rule_type`. Returns True if done."""
    rule = rules_of(rs, rule_type)
    if not rule:
        return False
    p = rule[0]["params"]
    fee = [l for l in inv["lines"] if l["line_type"] == "fee" and l["hours"] is not None and l["_uid"] not in taken]
    if rule_type != "InvoiceAge" and rule_type != "CoreRecordWindow" and not fee and rule_type not in ("ExpenseRule", "RequiredData"):
        return False
    if rule_type == "TimeIncrement":
        l = rng.choice(fee)
        inc = D(str(p["increment_hours"]))
        for _ in range(50):
            cand = D(str(round(rng.randint(2, 40) * 0.05, 2)))
            if cand % inc != 0:
                break
        l["hours"] = float(cand)
        l["amount"] = amount_of(l["hours"], l["rate"])
        taken.add(l["_uid"])
    elif rule_type == "ApprovedRate":
        l = rng.choice(fee)
        cap = p["rates"][l["tk"]["title"]]
        l["rate"] = float(int(round(cap * rng.uniform(1.1, 1.4) / 5.0)) * 5)
        l["amount"] = amount_of(l["hours"], l["rate"])
        taken.add(l["_uid"])
    elif rule_type == "ApprovedTimekeeper":
        if len(inv["context"]["approved_timekeepers"]) >= len(inv["firm"]["tks"]):
            nm = f"{rng.choice(R.FIRST)} {rng.choice(R.LAST)}"
            title = rng.choice(["Associate", "Paralegal", "Senior Associate"])
            cap = inv["_caps"].get(title, 300) if inv.get("_caps") else None
            rate = float(int(round((cap or 300) * 0.85 / 5.0)) * 5)
            new = {"name": nm, "title": title, "rate": rate}
            inv["firm"]["tks"].append(new)
        else:
            nm_out = [t for t in inv["firm"]["tks"] if t["name"] not in inv["context"]["approved_timekeepers"]]
            new = rng.choice(nm_out)
        l = rng.choice(fee)
        l["tk"] = new
        l["rate"] = new["rate"]
        l["amount"] = amount_of(l["hours"], l["rate"])
        taken.add(l["_uid"])
        inv["_tks"][new["name"]] = new
    elif rule_type == "DailyHoursCap":
        cap = p["max_hours_per_timekeeper_per_day"]
        tk = rng.choice(inv["_pool"])
        d = rng.choice(inv["_days"])
        inc = inv["_inc"]
        total = sum(l["hours"] for l in inv["lines"] if l["line_type"] == "fee" and l["hours"] is not None
                    and l["tk"] and l["tk"]["name"] == tk["name"] and l["date"] == iso(d))
        want = cap - total + rng.uniform(0.6, 2.0)
        task_codes = inv["_task_codes"]
        while want > 0:
            h = min(4.0, max(inc, float(D(str(inc)) * max(1, int(round(min(want, 4.0) / inc))))))
            text, act, task, _ = narrative(rng, "clear", task_codes, tk["name"])
            inv["lines"].append({"_uid": _new_uid(inv), "line_type": "fee", "date": iso(d), "tk": tk, "hours": h,
                                 "rate": tk["rate"], "amount": amount_of(h, tk["rate"]), "narrative": text,
                                 "codes": [task, act], "intended": [task, act], "narrative_style": "clear"})
            inv["_tks"][tk["name"]] = tk
            want -= h
        # keep the added lines out of other injections
        for l in inv["lines"]:
            if l["tk"] is tk and l["date"] == iso(d):
                taken.add(l["_uid"])
    elif rule_type == "NonBillableActivity":
        l = rng.choice(fee)
        if rng.random() < 0.7 or not any(k in p["keywords"] for k in KW_INTERNAL):
            kw = [c for c in R.CLERICAL if any(k in c[1].lower() for k in p["keywords"])]
            act, text = rng.choice(kw)
            l["narrative"], l["codes"][:] = text, ([l["codes"][0], act] if l["codes"] else [])
        else:
            l["narrative"] = fill(rng, R.INTERNAL, l["tk"]["name"])
            if l["codes"]:
                l["codes"][:] = [l["codes"][0], "A105"]
        l["narrative_style"] = "clerical"
        taken.add(l["_uid"])
    elif rule_type == "ExpenseRule":
        rr = [r for r in rule if True]
        r = rng.choice(rr)
        pp = r["params"]
        if pp.get("non_reimbursable"):
            code = rng.choice(pp["non_reimbursable"])
            desc, (ulo, uhi), (qlo, qhi) = R.EXPENSE_OVERHEAD[code]
            unit, qty = r2(rng.uniform(ulo, uhi)), rng.randint(qlo, qhi)
        else:
            code = rng.choice(list(pp["caps"]))
            src = R.EXPENSE_BANK.get(code)
            desc, (ulo, uhi), (qlo, qhi) = src
            unit = r2(pp["caps"][code] * rng.uniform(1.3, 2.2))
            qty = rng.randint(max(qlo, 1), max(qhi, 1))
        uid = _new_uid(inv)
        inv["lines"].append({"_uid": uid, "line_type": "expense", "date": rng.choice(inv["lines"])["date"] or inv["lines"][0]["date"],
                             "tk": None, "hours": float(qty), "rate": unit, "amount": amount_of(qty, unit),
                             "narrative": fill(rng, desc, n=qty), "codes": [code], "intended": [code], "narrative_style": "clear"})
        taken.add(uid)
    elif rule_type == "InvoiceAge":
        pend = parse(inv["header"]["billing_period_end"])
        if "max_days_after_period_end" in p and (not "max_days_work_age" in p or rng.random() < 0.5):
            rec = pend + dt.timedelta(days=p["max_days_after_period_end"] + rng.randint(5, 40))
            inv["context"]["invoice_received_date"] = iso(rec)
            inv["header"]["invoice_date"] = iso(rec - dt.timedelta(days=1))
        else:
            l = rng.choice(fee)
            rec = parse(inv["context"]["invoice_received_date"])
            l["date"] = iso(rec - dt.timedelta(days=p["max_days_work_age"] + rng.randint(5, 30)))
            taken.add(l["_uid"])
    elif rule_type == "CoreRecordWindow":
        if rng.random() < 0.5:
            inv["_budget_breach"] = True
        else:
            l = rng.choice(fee)
            l["date"] = iso(parse(inv["context"]["matter_close"]) + dt.timedelta(days=rng.randint(5, 25)))
            taken.add(l["_uid"])
    elif rule_type == "Duplicate":
        l = rng.choice(fee)
        inv["lines"].append(dict(l, _uid=_new_uid(inv), codes=list(l["codes"])))
        taken.add(l["_uid"])
        taken.add(inv["lines"][-1]["_uid"])
    elif rule_type == "RequiredData":
        if p.get("codes_required") and (not p.get("fields") or rng.random() < 0.6):
            l = rng.choice(fee)
            l["codes"][:] = []
            taken.add(l["_uid"])
        elif p.get("fields"):
            inv["header"][p["fields"][0]] = None
        else:
            return False
    elif rule_type == "AiObservation":
        obs = p["observations"]
        want = rng.choice(obs)
        l = rng.choice(fee)
        if want == "vague_description":
            l["narrative"], l["narrative_style"] = rng.choice(R.VAGUE), "vague"
        else:
            picks = [rng.choice(R.CLEAR[rng.choice(inv["_task_codes"])]) for _ in range(3)]
            l["narrative"] = "; ".join(fill(rng, t[1]).rstrip(".") for t in picks) + "."
            l["narrative_style"] = "block_billed"
            l["hours"] = float(D(str(inv["_inc"])) * max(1, int(round(rng.uniform(2.0, 4.5) / inv["_inc"]))))
            l["amount"] = amount_of(l["hours"], l["rate"])
        taken.add(l["_uid"])
    else:
        return False
    return True


def strip_internal(inv):
    for k in [k for k in inv if k.startswith("_")]:
        del inv[k]
    for l in inv["lines"]:
        l.pop("_uid", None)
    return inv
