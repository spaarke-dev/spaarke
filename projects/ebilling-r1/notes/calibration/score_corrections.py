"""Calibration from structured correction records (task 161 step 3; FR-31, D-64).

usage:  python -I score_corrections.py --export corrections-export.json [--out results]

corrections-export/1 (written by live/Export-CalibrationCorrections.ps1 from sprk_invoicecorrection; ids and numbers only):
{ "schema": "ebilling-corrections-export/1", "run": "<export id>",
  "rows": [ {"fieldName": "Hours", "aiConfidence": 0.93, "outcomeType": 100000000|100000001|100000002|100000003,
             "isLabel": true|false, "sourceCode": "sprk_invoice_v1", "sourceVersion": "2025-11-01"} ],
  "unreviewed": [ {"sourceCode": "...", "sourceVersion": "...", "count": 12} ] }       # values with no review outcome

Rules (POML constraints):
  * ONLY sprk_islabel = true rows are ground truth. Corrected (100000000) = the AI value was wrong; Confirmed individually (100000001) =
    it was right. Accepted in bulk (100000002) and Unreviewed (100000003) are never labels: reported as counts only.
  * Results are split by (sourceCode, sourceVersion) and never pooled.
  * Under MIN_LABELS (30) label rows in a group the group is marked insufficient and no recommendation may rest on it (POML escalation 2).
  * sprk_aiconfidence is the combined confidence for extracted fields (InvoiceCorrectionService: combined, else analyzer) and the coding
    confidence for code fields, so bins here are on the confidence the reviewer saw.
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import calib_lib as cl  # noqa: E402

MIN_LABELS = 30
CORRECTED, CONFIRMED, ACCEPTED_BULK, UNREVIEWED = 100000000, 100000001, 100000002, 100000003
OUTCOME_NAME = {CORRECTED: "Corrected", CONFIRMED: "ConfirmedIndividually", ACCEPTED_BULK: "AcceptedInBulk", UNREVIEWED: "Unreviewed"}


def score_corrections(export: dict) -> dict:
    rows = export.get("rows") or []
    groups: dict[str, dict] = defaultdict(lambda: {"labels": [], "outcomes": Counter(), "non_label_rows": 0})
    for r in rows:
        key = f"{r.get('sourceCode') or '?'}@{r.get('sourceVersion') or '?'}"
        g = groups[key]
        g["outcomes"][OUTCOME_NAME.get(r.get("outcomeType"), str(r.get("outcomeType")))] += 1
        is_label = bool(r.get("isLabel")) and r.get("outcomeType") in (CORRECTED, CONFIRMED)
        if bool(r.get("isLabel")) != is_label:
            g.setdefault("inconsistent_label_flag", 0)
            g["inconsistent_label_flag"] += 1          # isLabel disagrees with the outcome: excluded, counted
        if not is_label:
            g["non_label_rows"] += 1
            continue
        if r.get("aiConfidence") is None:
            g.setdefault("labels_without_confidence", 0)
            g["labels_without_confidence"] += 1
            continue
        g["labels"].append({"source": r.get("sourceCode"), "version": r.get("sourceVersion"), "field": r.get("fieldName"),
                            "correct": r["outcomeType"] == CONFIRMED, "analyzer": None, "combined": float(r["aiConfidence"]),
                            "item": None, "line": None})
    unrev = {f"{u.get('sourceCode') or '?'}@{u.get('sourceVersion') or '?'}": u.get("count", 0) for u in export.get("unreviewed") or []}
    out = {"schema": "ebilling-calibration/1", "kind": "corrections-score", "run": export.get("run"), "min_labels": MIN_LABELS,
           "groups": {}}
    for key in sorted(set(groups) | set(unrev)):
        g = groups.get(key) or {"labels": [], "outcomes": Counter(), "non_label_rows": 0}
        labels = g["labels"]
        entry = {
            "labels": len(labels), "sufficient": len(labels) >= MIN_LABELS,
            "outcomes": dict(g["outcomes"]), "bulk_accepted_rows": g["outcomes"].get("AcceptedInBulk", 0),
            "unreviewed_values": unrev.get(key, 0) + g["outcomes"].get("Unreviewed", 0),
            "excluded_non_label_rows": g["non_label_rows"],
            "inconsistent_label_flag": g.get("inconsistent_label_flag", 0),
            "labels_without_confidence": g.get("labels_without_confidence", 0),
        }
        if labels:
            entry["wrong_rate_pct"] = cl.pct(sum(1 for o in labels if not o["correct"]), len(labels))
            entry["bins"] = cl.bin_table(labels, "combined")
            entry["at_or_above_threshold"] = cl.wrong_at_or_above(labels, cl.CURRENT_THRESHOLD, "combined")
            entry["fields"] = cl.field_report(labels, "combined")
            entry["sweep"] = cl.field_sweep(labels, "combined")
        if not entry["sufficient"]:
            entry["note"] = (f"fewer than {MIN_LABELS} label rows ({len(labels)}): report the counts, base recommendations on the "
                             "evaluation set only, do not generate review outcomes (POML escalation 2)")
        out["groups"][key] = entry
    out["totals"] = {"labels": sum(e["labels"] for e in out["groups"].values()),
                     "any_group_sufficient": any(e["sufficient"] for e in out["groups"].values())}
    return out


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--export", required=True)
    ap.add_argument("--out", default=str(HERE / "results"))
    a = ap.parse_args()
    ex = json.loads(Path(a.export).read_text(encoding="utf-8"))
    rep = score_corrections(ex)
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    (out / f"corrections-{ex.get('run', 'export')}.json").write_text(json.dumps(rep, indent=1), encoding="utf-8")
    for k, e in rep["groups"].items():
        print(k, "labels", e["labels"], "sufficient", e["sufficient"], "bulk", e["bulk_accepted_rows"], "unreviewed", e["unreviewed_values"])


if __name__ == "__main__":
    main()
