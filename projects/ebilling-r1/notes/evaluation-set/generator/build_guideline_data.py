"""Billing-guideline compliance test: the invoice subset and the synthetic reference data (firms, matters, rate-card workbooks).

Reads Set F ground truth and the RS rule sets; never writes to them. Output goes to ../guidelines/:
  subset.json                       the 120 invoices (per rule set: 15 seeded, 5 clean)
  reference-data/RS-0n.json         firms, matters and one 078 rate-card workbook per firm, for every invoice in the subset

Run from the evaluation-set folder:  python -I generator/build_guideline_data.py
Deterministic: the same inputs give byte-identical output.
"""
import collections
import math
import json
import random
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
GT = ROOT / "ground-truth"
OUT = ROOT / "guidelines"
SEED = 20261103
PER_SET_SEEDED = 15
PER_SET_CLEAN = 5

# LEDES classifications the 078 workbook accepts (RateCardWorkbook.TimekeeperClassifications). Set F's "Senior Associate" is not one of
# them, so a senior associate is rostered as Associate; their approved rate is set per timekeeper, so the classification never decides it.
CLASSIFICATION = {
    "Partner": "Partner",
    "Of Counsel": "Of Counsel",
    "Senior Associate": "Associate",
    "Associate": "Associate",
    "Paralegal": "Paralegal",
    "Legal Assistant": "Legal Assistant",
}
# Classification rows (for a billed timekeeper who is not on the roster) use these RS titles.
CLASSIFICATION_RATE_TITLE = {
    "Partner": "Partner",
    "Of Counsel": "Of Counsel",
    "Associate": "Associate",
    "Paralegal": "Paralegal",
    "Legal Assistant": "Legal Assistant",
}
ROSTER_FROM = "2024-01-01"  # wide and open-ended, so the roster never decides a CoreRecordWindow or InvoiceAge case

SUBJECT = re.compile(
    r"\b(?:in|for|of|concerning|regarding|on) the ((?:[a-z]+ ){0,3}"
    r"(?:dispute|claim|action|audit|incident|litigation|case|investigation|arbitration|lawsuit|matter))\b")


def load():
    rule_sets = {p.stem: json.loads(p.read_text(encoding="utf-8")) for p in sorted((GT / "rule-sets").glob("RS-*.json"))}
    invoices = collections.defaultdict(list)
    for p in sorted((GT / "compliance-invoices").glob("F-*.json")):
        d = json.loads(p.read_text(encoding="utf-8"))
        invoices[d["rule_set_id"]].append(d)
    return rule_sets, invoices


def has_rule(rs, rule_type):
    return any(r["rule_type"] == rule_type for r in rs["rules"])


def non_billable_codes(rs):
    """The codes the production NonBillableActivity rule lists for this guideline (see production_key.PRODUCTION_RULES)."""
    for r in rs["rules"]:
        if r["rule_type"] == "NonBillableActivity":
            return {"A110", "A105"} if "internal conference" in r["params"].get("keywords", []) else {"A110"}
    return set()


def a110_clean_lines(inv, codes):
    flagged = {n for e in inv["expected_findings"] if e["rule_type"] == "NonBillableActivity" for n in e["line_refs"]}
    return sum(1 for l in inv["lines"] if l["line_type"] == "fee" and l["n"] not in flagged and codes & set(l["codes"]))


def pick(rs, invs, rng):
    """15 seeded invoices covering every rule of the set at least twice (greedy), then 5 clean ones. Where the guideline has a
    NonBillableActivity rule, the clean invoices are those with the most legitimate A110/A105 lines (agreed with ebilling 2026-11-03),
    so a drafter or admin who states the clerical-work clause as an A110 code rule shows false alarms on clean invoices."""
    seeded = [i for i in invs if i["expected_findings"]]
    clean = [i for i in invs if not i["expected_findings"]]
    rng.shuffle(seeded)
    need = collections.Counter({r["rule_id"]: 2 for r in rs["rules"]})
    chosen = []
    pool = seeded[:]
    while len(chosen) < PER_SET_SEEDED and pool:
        best = max(pool, key=lambda i: (sum(1 for r in {e["rule_id"] for e in i["expected_findings"]} if need[r] > 0), -len(i["lines"]), i["id"]))
        pool.remove(best)
        chosen.append(best)
        for r in {e["rule_id"] for e in best["expected_findings"]}:
            need[r] -= 1
    codes = non_billable_codes(rs)
    rng.shuffle(clean)
    clean_sorted = sorted(clean, key=lambda i: (-a110_clean_lines(i, codes), i["id"])) if codes else clean
    return sorted(chosen, key=lambda i: i["id"]), sorted(clean_sorted[:PER_SET_CLEAN], key=lambda i: i["id"])


def slug(name):
    """'&' and 'and' stay distinct: Set F has both "Pryce & Whitcombe LLP" and "Pryce and Whitcombe LLP"."""
    return re.sub(r"[^a-z0-9]+", "-", name.lower().replace("&", " amp ")).strip("-")


def rack_of(approved):
    """At least 12% above the approved rate, rounded UP to $5, so a 10% discount off rack never lands below the approved rate."""
    return math.ceil(approved * 1.12 / 5) * 5


def email(person, firm):
    local = re.sub(r"[^a-z]+", ".", person.lower()).strip(".")
    return f"{local}@{slug(firm)}.example"


def matter_subject(inv):
    counts = collections.Counter()
    for l in inv["lines"]:
        for m in SUBJECT.finditer((l.get("narrative") or "").lower()):
            counts[m.group(1)] += 1
    if not counts:
        return "commercial litigation"
    return sorted(counts.items(), key=lambda kv: (-kv[1], kv[0]))[0][0]


def money(x):
    return f"{x:.2f}"


def workbook(inv, rs, roster):
    """The 078 workbook for the invoice's firm, as JSON: sheet name -> array of rows keyed by the exact column names."""
    rates_rule = next((r for r in rs["rules"] if r["rule_type"] == "ApprovedRate"), None)
    caps = rates_rule["params"]["rates"] if rates_rule else None
    billed = {t["name"]: t for t in inv["timekeepers"]}
    timekeepers, rates = [], []
    for name, title in roster:
        timekeepers.append({
            "Name": name,
            "Classification": CLASSIFICATION[title],
            "Effective From": ROSTER_FROM,
            "Effective To": None,
            "Contact Email": email(name, inv["header"]["firm"]),
        })
        billed_rate = billed[name]["rate"] if name in billed else None
        approved = caps[title] if caps else (billed_rate if billed_rate is not None else None)
        if approved is None:
            continue  # an approved timekeeper who bills nothing on this invoice and whose rate the guideline does not set
        rack = max(billed_rate or 0, rack_of(approved))
        rates.append({
            "Timekeeper": name, "Classification": None, "Approved Rate": money(approved), "Rack Rate": money(rack),
            "Currency": "USD", "Effective From": ROSTER_FROM, "Effective To": None,
        })
    if caps:
        for cls, title in CLASSIFICATION_RATE_TITLE.items():
            if title in caps:
                rates.append({
                    "Timekeeper": None, "Classification": cls, "Approved Rate": money(caps[title]),
                    "Rack Rate": money(rack_of(caps[title])), "Currency": "USD",
                    "Effective From": ROSTER_FROM, "Effective To": None,
                })
    return {"Timekeepers": timekeepers, "Rates": rates}


def roster_of(inv, rs, rng):
    """(name, title) pairs. A guideline with ApprovedTimekeeper: exactly the invoice's approved list (titles from the invoice where the
    person bills on it, else a plausible title chosen deterministically). Otherwise every timekeeper on the invoice."""
    titles = {t["name"]: t["title"] for t in inv["timekeepers"]}
    for l in inv["lines"]:
        if l.get("timekeeper_name") and l.get("timekeeper_title"):
            titles.setdefault(l["timekeeper_name"], l["timekeeper_title"])
    if has_rule(rs, "ApprovedTimekeeper"):
        names = inv["context"]["approved_timekeepers"]
        return [(n, titles.get(n) or rng.choice(["Associate", "Paralegal", "Senior Associate"])) for n in names]
    return sorted(titles.items())


def build():
    rule_sets, invoices = load()
    rng = random.Random(SEED)
    subset = {}
    OUT.joinpath("reference-data").mkdir(parents=True, exist_ok=True)
    for rs_id in sorted(rule_sets):
        rs = rule_sets[rs_id]
        seeded, clean = pick(rs, invoices[rs_id], rng)
        subset[rs_id] = {"seeded": [i["id"] for i in seeded], "clean": [i["id"] for i in clean]}
        cwin = has_rule(rs, "CoreRecordWindow")
        firms, matters = {}, []
        for inv in seeded + clean:
            h, ctx = inv["header"], inv["context"]
            firm = h["firm"]
            roster = roster_of(inv, rs, rng)
            if firm not in firms:
                firms[firm] = {
                    "name": firm,
                    # Unique per rule set: six firm names appear in two rule sets with different rate caps and rosters.
                    "key": f"{rs_id.lower()}-{slug(firm)}",
                    "address": inv["document"].get("letterhead", []),
                    "billing_contact_email": f"billing@{slug(firm)}.example",
                    "workbook": workbook(inv, rs, roster),
                }
            else:  # a firm used by two subset invoices: one roster, the union of both
                wb = workbook(inv, rs, roster)
                have = {r["Name"] for r in firms[firm]["workbook"]["Timekeepers"]}
                firms[firm]["workbook"]["Timekeepers"] += [r for r in wb["Timekeepers"] if r["Name"] not in have]
                have_rates = {(r["Timekeeper"], r["Classification"]) for r in firms[firm]["workbook"]["Rates"]}
                firms[firm]["workbook"]["Rates"] += [r for r in wb["Rates"] if (r["Timekeeper"], r["Classification"]) not in have_rates]
            matters.append({
                "test_id": inv["id"],
                "name": f"{matter_subject(inv).capitalize()} ({inv['id']})",
                "firm_matter_reference": h["firm_matter_reference"],
                "assigned_law_firm_1": firm,
                "assigned_law_firm_2": None,
                "matter_type": "Litigation",
                "practice_area": None,
                # CoreRecordWindow's after_close applies only to Final (CoreRecordWindowHandler; ebilling 2026-11-03).
                "billing_status": "Final" if cwin else "Open",
                "open_date": ctx["matter_open"],
                "close_date": ctx["matter_close"],
                "budget": ctx["matter_budget"],
                "po_number": h.get("po_number"),
                "invoice_received_date": ctx["invoice_received_date"],
            })
        doc = {
            "schema": "ebilling-eval/1",
            "kind": "reference-data",
            "id": f"{rs_id}-REF",
            "rule_set_id": rs_id,
            "meta": {"generator_id": f"build_guideline_data.py seed={SEED}", "licence_status": "synthetic",
                     "pii_note": "Synthetic names; e-mail addresses use the reserved .example domain."},
            "firms": sorted(firms.values(), key=lambda f: f["name"]),
            "matters": matters,
        }
        (OUT / "reference-data" / f"{rs_id}.json").write_text(json.dumps(doc, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    (OUT / "subset.json").write_text(json.dumps(subset, indent=1) + "\n", encoding="utf-8")
    for rs_id, s in subset.items():
        print(rs_id, "seeded", len(s["seeded"]), "clean", len(s["clean"]))


if __name__ == "__main__":
    build()
