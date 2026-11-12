"""Offline analyzer-confidence baseline for task 161 (no Dataverse, no Azure call, no cost).

Re-scores 005's recorded Content Understanding responses (arm c = GA 2025-11-01, arm cp = preview 2026-06-01) per FIELD and bins the
ANALYZER signal only. This is the D-47 analyzer signal measured on the 41-item spike set; the arithmetic, reference and self-report
signals and the combined score need a dev pipeline run (score_run.py). GA and preview are reported separately, never pooled (D-81).

usage:  python -I analyzer_baseline.py --local <path to evaluation-set/local>  [--out results]
        (the raw responses are git-ignored under evaluation-set/local/spike-005/<arm>/; ADR-015: they stay there)

Output: results/analyzer-baseline-<arm>.json  (ids, field names, counts, rates, confidences; no document text).
Method matches 005's score.py: header values the arm returned, matched-line fields as returned ("printed" view), spurious rows on
items whose ground truth is complete. Extra here: field names are kept, and fee task code / activity code as billed are scored.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
HERE = Path(__file__).resolve().parent
SPIKE = HERE.parent / "spikes" / "extraction"
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(SPIKE))

import common  # noqa: E402  (005's helpers)
import score as spike  # noqa: E402  (005's scoring harness)
import calib_lib as cl  # noqa: E402

ARMS = {"c": {"source": "sprk_invoice_v1", "version": "2025-11-01", "label": "GA"},
        "cp": {"source": "sprk_invoice_v1p", "version": "2026-06-01-preview", "label": "preview"}}

CODE_RE = re.compile(r"^[A-Z]\d{3}$")


def split_codes(codes):
    task = [c for c in codes or [] if CODE_RE.match(c) and c[0] not in "AE"]
    act = [c for c in codes or [] if CODE_RE.match(c) and c[0] == "A"]
    return (task[0] if task else None), (act[0] if act else None)


def norm_code(v):
    return re.sub(r"[^A-Z0-9]", "", str(v).upper()) if v else ""


def observations(arm: str) -> tuple[list[dict], dict]:
    meta = ARMS[arm]
    obs: list[dict] = []
    items = spike.spike_items()
    n_extracted = 0
    for gt in items:
        raw = common.load_raw(arm, gt["id"])
        ext = spike.ADAPTER[arm](raw) if raw else None
        if ext is None:
            continue
        n_extracted += 1
        full = common.load_full_gt(gt["id"])

        def add(field, conf, ok, line=None):
            obs.append({"run": f"005-{arm}", "source": meta["source"], "version": meta["version"], "item": gt["id"],
                        "field": field, "line": line, "correct": bool(ok), "analyzer": float(conf), "combined": None,
                        "selfReport": None, "arithmetic": "NotApplicable", "reference": "NotApplicable"})

        for f in spike.HEADER:
            val, conf = ext["header"].get(f) or (None, None)
            if val in (None, "") or conf is None:
                continue
            ok, _ = spike.header_correct(f, val, gt)
            add(f"header.{f}", conf, ok)

        gtl = [ln for ln in (full.get("lines") or []) if ln.get("line_type") in ("fee", "expense")]
        if not gtl:
            continue
        m = spike.match_lines(ext["lines"], gtl)
        for j, g in enumerate(gtl):
            if j not in m:
                continue
            e = ext["lines"][m[j]]
            t = g["line_type"]
            pr = spike.printed(gt, t)
            for f in (spike.FEE_F if t == "fee" else spike.EXP_F):
                if g.get(f) is None or (f == "timekeeper" and not spike.tk_names(g)) or f not in pr:
                    continue
                if e.get(f) in (None, "") or e["conf"].get(f) is None:
                    continue
                add(f"{t}.{f}", e["conf"][f], spike.line_field_ok(f, e, g), line=g.get("n"))
            if t == "fee":
                g_task, g_act = split_codes(g.get("codes"))
                for fld, key, truth in (("task_code", "task_billed", g_task), ("activity_code", "activity_billed", g_act)):
                    v, c = e.get(key), e["conf"].get(key)
                    if v and c is not None and truth:
                        add(f"fee.{fld}", c, norm_code(v) == norm_code(truth), line=g.get("n"))
        if gt["id"] not in spike.PARTIAL_GT:
            used = set(m.values())
            for i, e in enumerate(ext["lines"]):
                if i in used:
                    continue
                t = "fee" if e["type"] != "expense" else "expense"
                for f in (spike.FEE_F if t == "fee" else spike.EXP_F):
                    if e.get(f) not in (None, "") and e["conf"].get(f) is not None:
                        add(f"{t}.{f}", e["conf"][f], False)        # spurious row: every value on it is wrong
    return obs, {"items": len(items), "extracted": n_extracted}


def oracle_arithmetic(obs: list[dict]) -> dict:
    """Upper bound for the arithmetic signal on hours / rate / amount (046 ArithmeticChecker checks hours x rate = amount on a fee line):
    arithmetic = Passed when the value is right, Failed when wrong. No real check does better. Seeded weights (cl.Settings())."""
    s = cl.Settings()
    rows = []
    for field in ("fee.hours", "fee.rate", "fee.amount"):
        sub = [o for o in obs if o["field"] == field]
        base = [o for o in sub if o["analyzer"] >= s.threshold]
        with_a = [o for o in sub if cl.combine({**o, "arithmetic": "Passed" if o["correct"] else "Failed"}, s) >= s.threshold]
        rows.append({"field": field, "values": len(sub), "reach_analyzer_only": cl.pct(len(base), len(sub)),
                     "reach_with_oracle_arithmetic": cl.pct(len(with_a), len(sub)),
                     "wrong_trusted_analyzer_only": sum(1 for o in base if not o["correct"]),
                     "wrong_trusted_with_oracle_arithmetic": sum(1 for o in with_a if not o["correct"])})
    return {"basis": "oracle arithmetic, seeded weights", "rows": rows}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--local", required=True, help="evaluation-set/local folder holding spike-005/ and ground-truth-full/")
    ap.add_argument("--out", default=str(HERE / "results"))
    ap.add_argument("--arms", nargs="*", default=list(ARMS))
    a = ap.parse_args()
    local = Path(a.local).resolve()
    common.LOCAL = local                       # 005's helpers read these module globals at call time
    common.LOCAL_RAW = local / "spike-005"
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    for arm in a.arms:
        obs, counts = observations(arm)
        grp = cl.by_group(obs)
        assert len(grp) <= 1, "one analyzer version per file; never pooled"
        rep = {
            "schema": "ebilling-calibration/1", "kind": "analyzer-baseline", "arm": arm, **ARMS[arm], **counts,
            "signal": "analyzer", "values": len(obs), "threshold": cl.CURRENT_THRESHOLD,
            "overall": {"bins": cl.bin_table(obs), "at_or_above_threshold": cl.wrong_at_or_above(obs, cl.CURRENT_THRESHOLD),
                        "reliability": cl.reliability_curve(obs)},
            # 005 extraction-spike.md section 3 counts header + date/timekeeper/hours/rate/amount only; this row reproduces it.
            "overall_excluding_codes": {
                "values": len([o for o in obs if not o["field"].endswith("_code")]),
                "at_or_above_threshold": cl.wrong_at_or_above([o for o in obs if not o["field"].endswith("_code")], cl.CURRENT_THRESHOLD)},
            # Every wrong value the gate would have trusted (item id, field, line ref, confidence): ids only, no values.
            "trusted_wrong": [{"item": o["item"], "field": o["field"], "line": o["line"], "analyzer": round(o["analyzer"], 3)}
                              for o in obs if o["analyzer"] >= cl.CURRENT_THRESHOLD and not o["correct"]],
            "fields": cl.field_report(obs),
            "sweep_analyzer_only": cl.field_sweep(obs, signal="analyzer"),
        }
        (out / f"analyzer-baseline-{arm}.json").write_text(json.dumps(rep, indent=1), encoding="utf-8")
        (out / f"oracle-{arm}.json").write_text(json.dumps(oracle_arithmetic(obs), indent=1), encoding="utf-8")
        w = rep["overall"]["at_or_above_threshold"]
        print(f"{arm} ({ARMS[arm]['label']}): items {counts['items']} extracted {counts['extracted']} values {len(obs)}; "
              f">=0.90: {w['n']} values, {w['wrong']} wrong ({w['wrong_rate']}%); ECE {rep['overall']['reliability']['ece']}")


if __name__ == "__main__":
    main()
