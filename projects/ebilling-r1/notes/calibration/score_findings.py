"""Score detection (findings) against the ontology-r1 PRODUCTION key (task 161/162; PR #2545, guidelines/production-key/).

usage:  python -I score_findings.py --export run-export.json [--keys ../evaluation-set/guidelines/production-key] [--out results]

BINDING scoring rules (guidelines/README.md "Scoring", agreed with ebilling; 2026-11-03):
  * Score findings against `production_findings`, NOT Set F's expected findings, wherever the two differ (status moved / not_expressible /
    missed / production_only). gap-report.md section 3-4 is the baseline.
  * Match a finding on (invoice, rule_id, line refs); order irrelevant. An invoice-scope finding has line_refs [].
  * Clean invoices: the production key predicts NO finding on any clean invoice (30 of 30 in the 120-invoice subset), so a finding on a
    clean invoice is a false positive. Budget: at most 2% of clean invoices.
  * An unrostered timekeeper gets an ApprovedRate "no approved rate" finding beside ApprovedTimekeeper (16 in the subset). They are in
    `production_findings` and are EXPECTED, not false positives: they are reported on their own line.
  * Consequence must be correct on every finding: compare with `production_consequences[rule_id]` (Reject / SuggestAdjustment / Flag), not
    with the guideline's own word (Disallow / Reduce).
  * AiObservation `minConfidence` is a test setting no guideline states: it is not part of any body match.
  * Targets: recall and precision >= 90% per mechanical rule type, >= 75% for AiObservation.

Export finding shape (see score_run.py): {"item": "F-0007", "ruleId": "RS-01-R01" (the key's id; the seed/publish step records policy code ->
key rule id), "ruleType": "TimeIncrement", "lineRefs": [3], "scope": "line", "consequence": "Flag"}.
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import calib_lib as cl  # noqa: E402

DEFAULT_KEYS = HERE.parent / "evaluation-set" / "guidelines" / "production-key"
TARGET_MECHANICAL, TARGET_AI = 0.90, 0.75
CLEAN_FP_BUDGET = 0.02


def load_keys(folder: Path) -> dict:
    keys = {}
    for p in sorted(folder.glob("RS-*.json")):
        keys[p.stem] = json.loads(p.read_text(encoding="utf-8"))
    if not keys:
        raise FileNotFoundError(f"no RS-*.json under {folder}")
    return keys


def index_expected(keys: dict) -> tuple[dict, dict, set, dict]:
    """-> (expected {(item, rule_id, refs): type}, consequence {rule_id: str}, clean item ids, only {(item, rule_id, refs)} that are production-only)."""
    expected, cons, clean, only = {}, {}, set(), set()
    for rs in keys.values():
        cons.update(rs.get("production_consequences") or {})
        for inv in rs["invoices"]:
            if inv.get("clean"):
                clean.add(inv["id"])
            for f in inv.get("production_findings") or []:
                expected[(inv["id"], f["rule_id"], frozenset(f.get("line_refs") or []))] = f["type"]
            for f in inv.get("production_only") or []:
                only.add((inv["id"], f["rule_id"], frozenset(f.get("line_refs") or [])))
    return expected, cons, clean, only


def score_findings(export: dict, keys: dict) -> dict:
    expected, cons, clean, only = index_expected(keys)
    subset_items = {inv["id"] for rs in keys.values() for inv in rs["invoices"]}
    run_items = {i["id"] for i in export["invoices"]} & subset_items
    got: dict = {}
    for inv in export["invoices"]:
        if inv["id"] not in subset_items:
            continue
        for f in inv.get("findings") or []:
            got[(inv["id"], f["ruleId"], frozenset(f.get("lineRefs") or []))] = f
    exp_in_run = {k: t for k, t in expected.items() if k[0] in run_items}
    tp = {k for k in got if k in exp_in_run}
    fp = {k for k in got if k not in exp_in_run}
    fn = {k for k in exp_in_run if k not in got}
    by_type: dict = defaultdict(lambda: {"expected": 0, "found": 0, "tp": 0, "fp": 0})
    for k, t in exp_in_run.items():
        by_type[t]["expected"] += 1
        by_type[t]["tp"] += k in tp
    for k in got:
        t = got[k].get("ruleType") or exp_in_run.get(k)
        by_type[t]["found"] += 1
        by_type[t]["fp"] += k in fp
    rows = {}
    for t, v in sorted(by_type.items()):
        rec = cl.rate(v["tp"], v["expected"])
        prec = cl.rate(v["tp"], v["found"])
        target = TARGET_AI if t == "AiObservation" else TARGET_MECHANICAL
        rows[t] = {**v, "recall": rec, "precision": prec, "target": target,
                   "meets": (rec is None or rec >= target) and (prec is None or prec >= target)}
    wrong_cons = [{"item": k[0], "ruleId": k[1], "got": got[k].get("consequence"), "expected": cons.get(k[1])}
                  for k in tp if got[k].get("consequence") != cons.get(k[1])]
    clean_in_run = clean & run_items
    clean_fp = sorted({k[0] for k in got if k[0] in clean_in_run})
    return {
        "schema": "ebilling-calibration/1", "kind": "findings-score", "run": export.get("run"),
        "items_scored": len(run_items), "subset_items": len(subset_items),
        "expected": len(exp_in_run), "found": len(got), "tp": len(tp), "fp": len(fp), "fn": len(fn),
        "production_only_expected": sum(1 for k in only if k[0] in run_items), "production_only_found": sum(1 for k in tp if k in only),
        "by_rule_type": rows, "all_types_meet_target": all(r["meets"] for r in rows.values()),
        "consequence_wrong": len(wrong_cons), "consequence_wrong_rows": wrong_cons[:50],
        "clean_invoices": len(clean_in_run), "clean_with_finding": len(clean_fp), "clean_with_finding_ids": clean_fp,
        "clean_within_budget": (len(clean_fp) / len(clean_in_run) <= CLEAN_FP_BUDGET) if clean_in_run else None,
        "false_positive_rule_ids": sorted({k[1] for k in fp}),
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--export", required=True)
    ap.add_argument("--keys", default=str(DEFAULT_KEYS))
    ap.add_argument("--out", default=str(HERE / "results"))
    a = ap.parse_args()
    ex = json.loads(Path(a.export).read_text(encoding="utf-8"))
    rep = score_findings(ex, load_keys(Path(a.keys)))
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    (out / f"findings-{ex.get('run', 'export')}.json").write_text(json.dumps(rep, indent=1), encoding="utf-8")
    print({k: rep[k] for k in ("items_scored", "expected", "found", "tp", "fp", "fn", "consequence_wrong", "clean_with_finding")})


if __name__ == "__main__":
    main()
