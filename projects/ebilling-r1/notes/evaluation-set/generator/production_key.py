"""Billing-guideline compliance test: the PRODUCTION key for the subset.

Set F's expected findings come from rules.evaluate(), whose parameters are evaluation-time (manifest 1.2), not the production rule
schemas (src/server/api/Sprk.Bff.Api/Services/Signals/Schemas/billing-*.schema.json). This script re-labels the SAME invoices under
the production rule bodies a correct drafter would produce from each guideline (PRODUCTION_RULES below) and the synthetic reference
data (build_guideline_data.py). It reads Set F and never writes to it; it imports nothing from the BFF.

Each Set F finding is classified:
  reproduced            production flags the same rule on the same line(s)
  moved                 production flags the same rule on another line of the same group (DailyHoursCap: the group's first line)
  not_expressible       no R1 rule type can state the provision; the reason says which
  missed                expressible, but production's semantics do not flag it (the reason says why)
Production findings with no Set F counterpart are listed as production_only, with a reason.

The production semantics mirror the handlers' documented behaviour (Handlers/*Handler.cs, 2026-11-03). Two inputs are proxies, so those
rows are PREDICTIONS, marked `proxy`: NonBillableActivity judges AI-assigned codes (the billed codes stand in), and AiObservation judges
the AI's observation (the generator's narrative_style stands in). ApprovedRate for a timekeeper who is not on the roster raises a "no
approved rate" finding (RateCardResolver returns NotFound.TimekeeperUnmatched without a timekeeper id; ApprovedRateHandler reports it as
RateTimekeeperUnmatched), so it fires beside ApprovedTimekeeper on the same line.

Each production rule also carries the consequence a correct drafter can choose (production_consequence): production allows only Reject,
Flag or SuggestAdjustment, per rule type (BillingRuleCatalogue.AllowedConsequences, 2026-11-03).

Run from the evaluation-set folder:  python -I generator/production_key.py
Writes ../guidelines/production-key/RS-0n.json and ../guidelines/production-key/summary.json.
"""
import collections
import json
from datetime import date
from decimal import Decimal as D
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
GT = ROOT / "ground-truth"
GL = ROOT / "guidelines"

NE_PO = "RequiredData has no PO-number field (closed vocabulary: firmInvoiceNumber, invoiceDate, billingPeriodStart, billingPeriodEnd, timekeeper, lineDate, narrative, taskCode, activityCode, expenseCode)"
NE_PERIOD = "InvoiceAge is invoice date minus line date per line; no rule type measures received date against the billing period end"
NE_CAP_CODE = "ExpenseRule maxAmountPerItem applies to every expense line; it cannot be limited to one expense code"
NE_CAP_UNIT = "ExpenseRule caps the line amount; it cannot cap a unit rate (cost per page)"
NE_BUDGET = "CoreRecordWindow checks dates only; no rule type compares an invoice with the core record's budget"
NE_KEYWORD = ("NonBillableActivity lists activity codes only, and no code identifies clerical work: A110 (Manage data/files) also covers "
              "legitimate document work, and invoice preparation is coded A111")

# The production rule bodies a correct drafter produces from each guideline, keyed by the Set F rule id. A list holds the bodies one Set F
# rule becomes (AiObservation is one kind per rule). {"not_expressible": reason} marks a provision no R1 type can state; a dict with
# both "rules" and "not_expressible" means part of it converts.
OBS = {"block_billing": "blockBilling", "vague_description": "vagueDescription"}
MIN_CONF = 0.7
DUP = {"type": "Duplicate", "matchFields": ["timekeeper", "lineDate", "hours", "narrative"]}
CODES = {"type": "RequiredData", "fields": ["taskCode", "activityCode"]}
PRODUCTION_RULES = {
    "RS-01": {
        "RS-01-R01": [{"type": "TimeIncrement", "incrementHours": 0.1}],
        "RS-01-R02": [{"type": "AiObservation", "observation": "blockBilling", "minConfidence": MIN_CONF},
                      {"type": "AiObservation", "observation": "vagueDescription", "minConfidence": MIN_CONF}],
        "RS-01-R03": {"not_expressible": NE_KEYWORD},
        "RS-01-R04": [{"type": "ExpenseRule", "nonReimbursableCodes": ["E103", "E104", "E106"]}],
        "RS-01-R05": {"not_expressible": NE_CAP_UNIT},
        "RS-01-R06": {"not_expressible": NE_PERIOD},
        "RS-01-R07": [DUP],
        "RS-01-R08": {"not_expressible": NE_PO},
    },
    "RS-02": {
        "RS-02-R01": [{"type": "ApprovedTimekeeper"}],
        "RS-02-R02": [{"type": "ApprovedRate", "tolerancePercent": 0}],
        "RS-02-R03": [{"type": "TimeIncrement", "incrementHours": 0.1}],
        "RS-02-R04": [{"type": "DailyHoursCap", "maxHoursPerDay": 10}],
        "RS-02-R05": [CODES],
        "RS-02-R06": {"rules": [{"type": "CoreRecordWindow", "graceDays": 0}], "not_expressible": NE_BUDGET},
        "RS-02-R07": [DUP],
        "RS-02-R08": {"rules": [{"type": "InvoiceAge", "maxAgeDays": 90}], "not_expressible": NE_PERIOD},
    },
    "RS-03": {
        "RS-03-R01": [{"type": "ApprovedRate", "tolerancePercent": 0}],
        "RS-03-R02": [{"type": "TimeIncrement", "incrementHours": 0.1}],
        "RS-03-R03": [{"type": "DailyHoursCap", "maxHoursPerDay": 8}],
        "RS-03-R04": {"rules": [{"type": "NonBillableActivity", "codes": ["A105"]}], "not_expressible": NE_KEYWORD},
        "RS-03-R05": [{"type": "ExpenseRule", "nonReimbursableCodes": ["E103", "E106"]}],
        "RS-03-R06": {"not_expressible": NE_CAP_UNIT + "; " + NE_CAP_CODE},
        "RS-03-R07": [{"type": "AiObservation", "observation": "blockBilling", "minConfidence": MIN_CONF}],
        "RS-03-R08": {"not_expressible": NE_PO},
    },
    "RS-04": {
        "RS-04-R01": [{"type": "ApprovedTimekeeper"}],
        "RS-04-R02": [{"type": "ApprovedRate", "tolerancePercent": 0}],
        "RS-04-R03": [{"type": "TimeIncrement", "incrementHours": 0.25}],
        "RS-04-R04": {"rules": [{"type": "CoreRecordWindow", "graceDays": 0}], "not_expressible": NE_BUDGET},
        "RS-04-R05": {"not_expressible": NE_PERIOD},
        "RS-04-R06": [DUP],
        "RS-04-R07": {"rules": [{"type": "ExpenseRule", "nonReimbursableCodes": ["E104", "E106"]}], "not_expressible": NE_CAP_UNIT},
        "RS-04-R08": {"not_expressible": NE_KEYWORD},
    },
    "RS-05": {
        "RS-05-R01": [{"type": "ApprovedRate", "tolerancePercent": 0}],
        "RS-05-R02": [{"type": "TimeIncrement", "incrementHours": 0.1}],
        "RS-05-R03": [{"type": "AiObservation", "observation": "blockBilling", "minConfidence": MIN_CONF},
                      {"type": "AiObservation", "observation": "vagueDescription", "minConfidence": MIN_CONF}],
        "RS-05-R04": {"rules": [{"type": "NonBillableActivity", "codes": ["A105"]}], "not_expressible": NE_KEYWORD},
        "RS-05-R05": [DUP],
        "RS-05-R06": [{"type": "DailyHoursCap", "maxHoursPerDay": 12}],
        "RS-05-R07": {"rules": [CODES], "not_expressible": NE_PO},
        "RS-05-R08": [{"type": "InvoiceAge", "maxAgeDays": 120}],
    },
    "RS-06": {
        "RS-06-R01": [{"type": "ApprovedTimekeeper"}],
        "RS-06-R02": [{"type": "ApprovedRate", "tolerancePercent": 0}],
        "RS-06-R03": [{"type": "ExpenseRule", "nonReimbursableCodes": ["E103", "E104", "E106"]}],
        "RS-06-R04": {"not_expressible": NE_CAP_UNIT + "; " + NE_CAP_CODE},
        "RS-06-R05": {"rules": [{"type": "CoreRecordWindow", "graceDays": 0}], "not_expressible": NE_BUDGET},
        "RS-06-R06": {"not_expressible": NE_PERIOD},
        "RS-06-R07": [{"type": "DailyHoursCap", "maxHoursPerDay": 10}],
        "RS-06-R08": [{"type": "TimeIncrement", "incrementHours": 0.1}],
        "RS-06-R09": [DUP],
        "RS-06-R10": [CODES],
        "RS-06-R11": [{"type": "AiObservation", "observation": "vagueDescription", "minConfidence": MIN_CONF}],
    },
}

# BillingRuleCatalogue.AllowedConsequences (src/server/api/Sprk.Bff.Api/Services/Signals/Billing/BillingRuleCatalogue.cs, 2026-11-03).
ALLOWED_CONSEQUENCES = {
    "ApprovedTimekeeper": {"Flag"}, "ApprovedRate": {"Reject", "Flag", "SuggestAdjustment"}, "TimeIncrement": {"Flag"},
    "DailyHoursCap": {"Flag"}, "NonBillableActivity": {"Flag", "SuggestAdjustment"}, "ExpenseRule": {"Reject", "Flag"},
    "InvoiceAge": {"Reject", "Flag"}, "CoreRecordWindow": {"Reject", "Flag"}, "Duplicate": {"Reject", "Flag"},
    "RequiredData": {"Reject", "Flag"}, "AiObservation": {"Flag"},
}


def production_consequence(rule_type, guideline_consequence):
    """The nearest consequence production allows. Reject stays Reject where the type may reject (ApprovedTimekeeper holds instead, D-89).
    Reduce and Disallow change an amount: SuggestAdjustment where a handler suggests one, else Flag (production never rejects a whole
    invoice for a line it would only adjust). Flag stays Flag."""
    allowed = ALLOWED_CONSEQUENCES[rule_type]
    if guideline_consequence == "Reject" and "Reject" in allowed:
        return "Reject"
    if guideline_consequence in ("Reduce", "Disallow") and "SuggestAdjustment" in allowed:
        return "SuggestAdjustment"
    return "Flag"


def bodies(spec):
    return spec if isinstance(spec, list) else spec.get("rules", [])


def parse(s):
    return date.fromisoformat(s)


def norm(s):
    return " ".join((s or "").split()).lower()


def is_credit(line):
    return (line.get("hours") is not None and line["hours"] < 0) or (line.get("amount") is not None and line["amount"] < 0)


def task_code(line):
    return next((c for c in line["codes"] if c[:1] in "LPBC" and c[1:].isdigit()), None)


def activity_code(line):
    return next((c for c in line["codes"] if c.startswith("A") and c[1:].isdigit()), None)


def evaluate(inv, rs_id, matter, roster, rates):
    """Production findings: dicts {rule_id, type, scope, line_refs, basis}. `basis` is exact or proxy."""
    out = []
    lines = inv["lines"]
    fee = [l for l in lines if l["line_type"] == "fee"]
    exp = [l for l in lines if l["line_type"] == "expense"]
    inv_date = parse(inv["header"]["invoice_date"])

    def add(rule_id, body, refs, basis="exact", **extra):
        out.append({"rule_id": rule_id, "type": body["type"], "scope": "line" if refs else "invoice", "line_refs": refs,
                    "basis": basis, **extra})

    for rule_id, spec in PRODUCTION_RULES[rs_id].items():
        for body in bodies(spec):
            t = body["type"]
            if t == "ApprovedTimekeeper":
                for l in fee:
                    if l.get("timekeeper_name") not in roster:
                        add(rule_id, body, [l["n"]])
            elif t == "ApprovedRate":
                for l in fee:
                    if is_credit(l) or l.get("rate") is None:
                        continue
                    name = l.get("timekeeper_name")
                    if name not in roster:
                        add(rule_id, body, [l["n"]], reason="no_approved_rate")  # NotFound.TimekeeperUnmatched
                    elif name in rates and D(str(l["rate"])) > D(str(rates[name])) * (1 + D(str(body["tolerancePercent"])) / 100):
                        add(rule_id, body, [l["n"]])
            elif t == "TimeIncrement":
                inc = D(str(body["incrementHours"]))
                for l in fee:
                    if l.get("hours") is not None and D(str(l["hours"])) % inc != 0:
                        add(rule_id, body, [l["n"]])
            elif t == "DailyHoursCap":
                groups = collections.OrderedDict()
                for l in fee:
                    if l.get("hours") is None or not l.get("date") or not l.get("timekeeper_name"):
                        continue
                    groups.setdefault((norm(l["timekeeper_name"]), l["date"]), []).append(l)
                for g in groups.values():
                    if sum(D(str(l["hours"])) for l in g) > D(str(body["maxHoursPerDay"])):
                        add(rule_id, body, [g[0]["n"]])  # one finding per timekeeper-day, on the group's first line
            elif t == "NonBillableActivity":
                listed = {c.upper() for c in body["codes"]}
                for l in fee:
                    if listed & {c.upper() for c in l["codes"]}:
                        add(rule_id, body, [l["n"]], "proxy")
            elif t == "ExpenseRule":
                listed = {c.upper() for c in body.get("nonReimbursableCodes", [])}
                for l in exp:
                    if is_credit(l):
                        continue
                    code = (l["codes"][0] if l["codes"] else "").upper()
                    if code in listed:
                        add(rule_id, body, [l["n"]])
                    if "maxAmountPerItem" in body and l.get("amount") is not None and l["amount"] > body["maxAmountPerItem"]:
                        add(rule_id, body, [l["n"]])
            elif t == "InvoiceAge":
                for l in fee + exp:
                    if l.get("date") and not is_credit(l) and (inv_date - parse(l["date"])).days > body["maxAgeDays"]:
                        add(rule_id, body, [l["n"]])
            elif t == "CoreRecordWindow":
                lo, hi = parse(matter["open_date"]), parse(matter["close_date"]) if matter["close_date"] else None
                for l in fee + exp:
                    if not l.get("date") or is_credit(l):
                        continue
                    d = parse(l["date"])
                    if (d - lo).days < -body["graceDays"] or (matter["billing_status"] == "Final" and hi and (d - hi).days > body["graceDays"]):
                        add(rule_id, body, [l["n"]])
            elif t == "Duplicate":
                seen = set()
                for l in fee:
                    key = (norm(l.get("timekeeper_name")), l.get("date"), l.get("hours"), norm(l.get("narrative")))
                    if None in key or "" in key:
                        continue
                    if key in seen:
                        add(rule_id, body, [l["n"]])
                    else:
                        seen.add(key)
            elif t == "RequiredData":
                # The handler writes one finding per missing field; they map to ONE Signal per (policy, line), the grain scored here.
                for l in fee:
                    missing = [f for f, present in (("taskCode", task_code(l)), ("activityCode", activity_code(l)))
                               if f in body["fields"] and not present]
                    if missing:
                        add(rule_id, body, [l["n"]], fields=missing)
            elif t == "AiObservation":
                style = {"blockBilling": "block_billed", "vagueDescription": "vague"}[body["observation"]]
                for l in fee:
                    if l.get("narrative_style") == style:
                        add(rule_id, body, [l["n"]], "proxy")
    return out


def classify(expected, produced, rs_id):
    """Pairs each Set F finding with a production finding (same rule id, same line or invoice) and labels the rest."""
    remaining = list(produced)
    rows = []
    for e in expected:
        spec = PRODUCTION_RULES[rs_id][e["rule_id"]]
        match = next((p for p in remaining if p["rule_id"] == e["rule_id"] and sorted(p["line_refs"]) == sorted(e["line_refs"])), None)
        if match:
            remaining.remove(match)
            rows.append({**e, "status": "reproduced", "basis": match["basis"]})
            continue
        if e["rule_type"] == "DailyHoursCap":
            moved = next((p for p in remaining if p["rule_id"] == e["rule_id"] and p["type"] == "DailyHoursCap"), None)
            if moved:
                remaining.remove(moved)
                rows.append({**e, "status": "moved", "production_line_refs": moved["line_refs"],
                             "reason": "production reports a timekeeper-day on the group's first line"})
                continue
        reason = expressible_reason(e, spec)
        if reason:
            rows.append({**e, "status": "not_expressible", "reason": reason})
        else:
            rows.append({**e, "status": "missed", "reason": missed_reason(e)})
    extra = [{**p, "status": "production_only", "reason": extra_reason(p)} for p in remaining]
    return rows, extra


def expressible_reason(e, spec):
    if isinstance(spec, dict) and "not_expressible" in spec:
        if not spec.get("rules"):
            return spec["not_expressible"]
        t = e["rule_type"]
        if t == "CoreRecordWindow" and e["scope"] == "invoice":
            return NE_BUDGET
        if t == "InvoiceAge" and e["scope"] == "invoice":
            return NE_PERIOD
        if t == "RequiredData" and e["scope"] == "invoice":
            return NE_PO
        if t == "ExpenseRule":
            return spec["not_expressible"]
        if t == "NonBillableActivity":
            return NE_KEYWORD
    return None


def missed_reason(e):
    return {
        "InvoiceAge": "the work-age limit is measured from the invoice date in production, from the received date in Set F",
        "NonBillableActivity": "the line's codes are not in the listed non-billable codes (Set F flagged it by narrative keyword)",
    }.get(e["rule_type"], "production semantics differ; see the line")


def extra_reason(p):
    if p["type"] == "NonBillableActivity":
        return "a line carries a listed code that Set F did not flag"
    if p["type"] == "ApprovedRate" and p.get("reason") == "no_approved_rate":
        return "the timekeeper is not on the roster, so no approved rate is in force (fires beside ApprovedTimekeeper)"
    if p["type"] == "RequiredData":
        return "the line has a task code but no activity code; Set F required the task code only"
    if p["type"] == "DailyHoursCap":
        return "a timekeeper-day over the cap that Set F reports on a different day or not at all"
    if p["type"] == "InvoiceAge":
        return "the work-age limit is measured from the invoice date in production, from the received date in Set F"
    if p["type"] == "AiObservation":
        return "a narrative style the rule lists, not seeded as a finding in Set F"
    return "no Set F counterpart"


def consequences_of(rs_id):
    rs = json.loads((GT / "rule-sets" / f"{rs_id}.json").read_text(encoding="utf-8"))
    return {r["rule_id"]: production_consequence(r["rule_type"], r["consequence"]) for r in rs["rules"] if bodies(PRODUCTION_RULES[rs_id][r["rule_id"]])}


def main():
    subset = json.loads((GL / "subset.json").read_text(encoding="utf-8"))
    (GL / "production-key").mkdir(parents=True, exist_ok=True)
    summary = {"by_rule_set": {}, "by_rule_type": collections.defaultdict(collections.Counter), "not_expressible": collections.Counter()}
    for rs_id, ids in subset.items():
        ref = json.loads((GL / "reference-data" / f"{rs_id}.json").read_text(encoding="utf-8"))
        firms = {f["name"]: f for f in ref["firms"]}
        matters = {m["test_id"]: m for m in ref["matters"]}
        invoices = []
        for inv_id in ids["seeded"] + ids["clean"]:
            inv = json.loads((GT / "compliance-invoices" / f"{inv_id}.json").read_text(encoding="utf-8"))
            wb = firms[inv["header"]["firm"]]["workbook"]
            roster = {r["Name"] for r in wb["Timekeepers"]}
            rates = {r["Timekeeper"]: float(r["Approved Rate"]) for r in wb["Rates"] if r["Timekeeper"]}
            produced = evaluate(inv, rs_id, matters[inv_id], roster, rates)
            rows, extra = classify(inv["expected_findings"], produced, rs_id)
            invoices.append({"id": inv_id, "clean": not inv["expected_findings"], "set_f_findings": rows,
                             "production_findings": produced, "production_only": extra})
            for r in rows:
                summary["by_rule_type"][r["rule_type"]][r["status"]] += 1
                if r["status"] == "not_expressible":
                    summary["not_expressible"][r["reason"]] += 1
            for p in extra:
                summary["by_rule_type"][p["type"]]["production_only"] += 1
        doc = {"schema": "ebilling-eval/1", "kind": "production-key", "id": f"{rs_id}-PK", "rule_set_id": rs_id,
               "meta": {"generator_id": "production_key.py", "label_method": "generator",
                        "note": "Predicted production findings; basis proxy marks rows that depend on AI output."},
               "production_rules": PRODUCTION_RULES[rs_id], "production_consequences": consequences_of(rs_id), "invoices": invoices}
        (GL / "production-key" / f"{rs_id}.json").write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")
        clean_hits = sum(1 for i in invoices if i["clean"] and i["production_findings"])
        summary["by_rule_set"][rs_id] = {
            "invoices": len(invoices),
            "set_f_findings": sum(len(i["set_f_findings"]) for i in invoices),
            "production_findings": sum(len(i["production_findings"]) for i in invoices),
            "clean_invoices_with_production_findings": clean_hits,
        }
    summary["by_rule_type"] = {k: dict(v) for k, v in sorted(summary["by_rule_type"].items())}
    summary["not_expressible"] = dict(summary["not_expressible"].most_common())
    (GL / "production-key" / "summary.json").write_text(json.dumps(summary, indent=1) + "\n", encoding="utf-8")
    print(json.dumps(summary, indent=1))


if __name__ == "__main__":
    main()
