"""Step 1 of task 161: cost estimate for running the evaluation set through the dev pipeline (analyzer + coding calls).

usage:  python -I cost_estimate.py [--out results/cost-estimate.json]

Unit prices are NOT re-quoted here: they come from 005's measured per-item cost (usage x prices.json, Azure Retail Prices read
2026-10-15) for the GA analyzer run (arm c) and the preview run (arm cp), turned into a cost per page (CU pages + contextualization + model
tokens scale with pages) and a coding cost per line (gpt-5.4-mini, 005 section 5). Page and line counts come from the ground-truth labels.
Read-only; no Azure or Dataverse call.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "spikes" / "extraction"))
import common  # noqa: E402
import score as spike  # noqa: E402

EVAL = HERE.parent / "evaluation-set"
CODING_PER_LINE = {"B": 0.000255, "A": 0.000185}      # 005 section 5, gpt-5.4-mini (corpus B / corpus A)
ACCEPTED_SPIKE_USD = 32.0                              # 005 note: "Approximate spend for the whole spike, at list prices: about $32"


def gt_files():
    for sub in ("synthetic-invoices", "fee-applications", "real-invoices", "compliance-invoices"):
        for p in sorted((EVAL / "ground-truth" / sub).glob("*.json")):
            yield sub, json.loads(p.read_text(encoding="utf-8"))


def pages_lines(gt):
    pages = (gt.get("document") or {}).get("pages")
    lines = len(gt.get("lines") or [])
    return pages, lines


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=str(HERE / "results" / "cost-estimate.json"))
    a = ap.parse_args()

    # 1. measured cost per page from 005's runs (arm c = GA, cp = preview)
    unit = {}
    spike_items = {g["id"]: g for g in spike.spike_items()}
    for arm in ("c", "cp"):
        runs = {r["id"]: r for r in common.read_runs(arm)}
        tot_c = tot_p = 0.0
        small_c = small_p = 0.0
        for iid, r in runs.items():
            if r.get("status") != "Succeeded" or iid not in spike_items:
                continue
            c = spike.cost_of(arm, r)
            pg = (spike_items[iid].get("document") or {}).get("pages") or r.get("pages")
            if c is None or not pg:
                continue
            tot_c += c
            tot_p += pg
            if pg <= 4:
                small_c += c
                small_p += pg
        unit[arm] = {"usd_per_page_all": round(tot_c / tot_p, 5), "usd_per_page_le4": round(small_c / small_p, 5),
                     "spike_total_usd": round(tot_c, 2), "spike_pages": tot_p}

    # 2. pages / lines per set (committed labels)
    sets = {}
    for sub, gt in gt_files():
        s = gt["set"]
        pg, ln = pages_lines(gt)
        d = sets.setdefault(s, {"items": 0, "pages": 0, "lines": 0, "missing_pages": 0})
        d["items"] += 1
        d["lines"] += ln
        if pg:
            d["pages"] += pg
        else:
            d["missing_pages"] += 1
    sub120 = json.loads((EVAL / "guidelines" / "subset.json").read_text(encoding="utf-8"))
    f_ids = [i for rs in sub120.values() for grp in rs.values() for i in grp]
    f_pages = f_lines = 0
    for sub, gt in gt_files():
        if gt["id"] in f_ids:
            pg, ln = pages_lines(gt)
            f_pages += pg or 2
            f_lines += ln
    sets["F120"] = {"items": len(f_ids), "pages": f_pages, "lines": f_lines}

    # 3. scenarios. Both analyzers are always run (D-81), so the CU part is priced for arm c AND arm cp; coding runs once per analyzer output.
    def price(pages_small, pages_big, lines, coding_rate):
        out = {}
        for arm in ("c", "cp"):
            out[arm] = round(pages_small * unit[arm]["usd_per_page_le4"] + pages_big * unit[arm]["usd_per_page_all"], 2)
        out["coding_per_analyzer"] = round(lines * coding_rate, 2)
        out["total_both_analyzers"] = round(out["c"] + out["cp"] + 2 * out["coding_per_analyzer"], 2)
        return out

    spike_b = [g for g in spike_items.values() if g["set"] == "B"]
    spike_b_pages = sum((g["document"].get("pages") or 0) for g in spike_b)
    spike_b_lines = sum(len(g.get("lines") or []) for g in spike_b)
    scen = {
        "S0_full_set_both_analyzers": {
            "what": "A 8 + B 150 + C 12 + F 600 (all labelled items) through c and cp",
            "items": sum(sets[k]["items"] for k in ("A", "B", "C", "F")),
            "pages": sum(sets[k]["pages"] for k in ("A", "B", "C", "F")),
            **price(sum(sets[k]["pages"] for k in ("B", "C", "F")), sets["A"]["pages"],
                    sum(sets[k]["lines"] for k in ("B", "C", "F")), CODING_PER_LINE["B"]),
        },
        "S1_proposed_subset_both_analyzers": {
            "what": ("the 37 set B spike items (14 bundled, B-0150 at 26 pages) + C-03, C-10, C-11 + A-07 (108 pages, the largest) "
                     "+ the 120-invoice guideline subset (15 seeded + 5 clean per rule set); both analyzers"),
            "items": len(spike_b) + 3 + 1 + len(f_ids),
            "pages": spike_b_pages + 3 + 108 + f_pages,
            **price(spike_b_pages + 3 + f_pages, 108, spike_b_lines + 200 + f_lines, CODING_PER_LINE["B"]),
        },
        "S2_minimum_subset_both_analyzers": {
            "what": "the 120-invoice guideline subset + the 14 bundled spike items + B-0150 + A-07; both analyzers",
            "items": len(f_ids) + 14 + 1 + 1,
            "pages": f_pages + 14 * 3 + 26 + 108,
            **price(f_pages + 14 * 3, 26 + 108, f_lines + 14 * 15 + 650, CODING_PER_LINE["B"]),
        },
    }
    for s in scen.values():
        s["exceeds_accepted_005_spend"] = s["total_both_analyzers"] > ACCEPTED_SPIKE_USD
    res = {"schema": "ebilling-calibration/1", "kind": "cost-estimate", "accepted_005_spend_usd": ACCEPTED_SPIKE_USD,
           "unit_costs_from_005": unit, "coding_usd_per_line": CODING_PER_LINE, "sets": sets, "scenarios": scen,
           "note": ("Estimate only. Pages of F invoices missing from labels are priced at 2 pages. Dataverse and BFF compute are not "
                    "priced (no marginal Azure meter). Re-run reads prices.json via 005's cost_of.")}
    Path(a.out).parent.mkdir(parents=True, exist_ok=True)
    Path(a.out).write_text(json.dumps(res, indent=1), encoding="utf-8")
    print(json.dumps({"unit": unit, "sets": sets, "scenarios": {k: {x: v[x] for x in ("items", "pages", "c", "cp", "total_both_analyzers", "exceeds_accepted_005_spend")} for k, v in scen.items()}}, indent=1))


if __name__ == "__main__":
    main()
