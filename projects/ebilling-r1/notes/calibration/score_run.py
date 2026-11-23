"""Score a dev pipeline run export against ground truth: per-field bins for the analyzer signal AND the combined confidence,
per-signal contributions, the threshold sweep, the weights/caps what-if and the NFR-06 rejection sweep. (Task 161 step 2 and 4.)

usage:  python -I score_run.py --export run-export.json [--local <evaluation-set/local>] [--out results]
        python -I score_run.py --export a.json b.json ...        (each export is one run; groups never pooled)

The export is produced by live/Export-CalibrationRun.ps1 from sprk_billingevent.sprk_extractionconfidence, the header provenance in
sprk_findingsjson and the stored values (schema below). Nothing here calls Azure or Dataverse.

run-export/1:
{ "schema": "ebilling-run-export/1", "run": "<run id>",
  "source": {"analyzerId": "sprk_invoice_v1", "apiVersion": "2025-11-01"},
  "settings": {"threshold": 0.9, ...},                                  # the 025 values in force for the run
  "invoices": [ { "id": "B-0001",                                       # evaluation-set item id
      "header": { "FirmInvoiceNumber": {"value": "..", "confidence": 0.93, "combined": 0.93}, ... },
      "lines":  [ { "n": 1, "type": "fee"|"expense", "date": "..", "timekeeper": "..", "hours": 1.5, "rate": 300, "amount": 450,
                    "fields": { "Hours": {"confidence": .., "combined": ..}, ... },
                    "signals": { "Hours": {"arithmetic": "Passed", "reference": "NotApplicable", "selfReport": 0.9,
                                           "referenceStrength": 1.0} } } ],
      "findings": [ { "ruleId", "ruleType", "consequence", "scope", "reason", "lineRefs", "readFields": [{"line": 3, "field": "Hours"}] } ] } ] }
ADR-015: the export carries values for scoring only and is written to the git-ignored evaluation-set/local/; only the results/ files are committed.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
HERE = Path(__file__).resolve().parent
SPIKE = HERE.parent / "spikes" / "extraction"
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(SPIKE))

import common  # noqa: E402
import score as spike  # noqa: E402
import calib_lib as cl  # noqa: E402

HEADER_MAP = {"FirmName": "firm", "FirmInvoiceNumber": "firm_invoice_number", "InvoiceDate": "invoice_date",
              "BillingPeriodStart": "billing_period_start", "BillingPeriodEnd": "billing_period_end",
              "FirmMatterReference": "firm_matter_reference", "PurchaseOrderAsQuoted": "po_number",
              "FeesTotal": "fees_total", "ExpensesTotal": "expenses_total", "AdjustmentsTotal": "adjustments_total",
              "TotalDue": "total_due", "Currency": "currency"}
LINE_MAP = {"Date": "date", "TimekeeperAsBilled": "timekeeper", "Hours": "hours", "Rate": "rate", "Amount": "amount"}
LINE_MAP_INV = {v: k for k, v in LINE_MAP.items()}
HEADER_INV = {v: k for k, v in HEADER_MAP.items()}


def _num(x):
    return None if x is None else float(x)


def _sig(sigs: dict, stored: str) -> dict:
    s = (sigs or {}).get(stored) or {}
    return {"arithmetic": s.get("arithmetic") or "NotApplicable", "reference": s.get("reference") or "NotApplicable",
            "selfReport": _num(s.get("selfReport")), "referenceStrength": float(s.get("referenceStrength", 1.0))}


def score_invoice(inv: dict, gt: dict, full: dict, meta: dict) -> tuple[list[dict], dict]:
    """One invoice -> observations (+ counts). `gt` is the committed label, `full` the full local label for real items."""
    obs: list[dict] = []
    counts = {"gt_lines": 0, "matched": 0, "spurious": 0, "unmapped_header_fields": 0}

    def add(field, stored, line, conf, ok, sigs=None):
        analyzer = _num(conf.get("confidence"))
        if analyzer is None and _num(conf.get("combined")) is None:
            return
        o = {**meta, "item": inv["id"], "field": field, "stored": stored, "line": line, "correct": bool(ok),
             "analyzer": analyzer if analyzer is not None else 0.0, "combined": _num(conf.get("combined"))}
        o.update(_sig(sigs, stored))
        obs.append(o)

    for stored, f in HEADER_MAP.items():
        h = (inv.get("header") or {}).get(stored)
        if not h or h.get("value") in (None, ""):
            continue
        ok, _ = spike.header_correct(f, h["value"], gt)
        add(f"header.{f}", stored, None, h, ok)
    counts["unmapped_header_fields"] = len([k for k in (inv.get("header") or {}) if k not in HEADER_MAP])

    ext_lines = []
    for ln in inv.get("lines") or []:
        e = {"type": ln.get("type", "fee"), "conf": {}, "_n": ln.get("n"), "_fields": ln.get("fields") or {},
             "_signals": ln.get("signals") or {}}
        for f in ("date", "timekeeper", "hours", "rate", "amount", "narrative"):
            e[f] = ln.get(f)
        ext_lines.append(e)
    gtl = [x for x in (full.get("lines") or []) if x.get("line_type") in ("fee", "expense")]
    counts["gt_lines"] = len(gtl)
    m = spike.match_lines(ext_lines, gtl) if gtl and ext_lines else {}
    counts["matched"] = len(m)
    for j, g in m.items():
        e = ext_lines[g]
        gl = gtl[j]
        t = gl["line_type"]
        pr = spike.printed(gt, t)
        for f in (spike.FEE_F if t == "fee" else spike.EXP_F):
            if gl.get(f) is None or (f == "timekeeper" and not spike.tk_names(gl)) or f not in pr:
                continue
            stored = LINE_MAP_INV[f]
            if e.get(f) in (None, "") or stored not in e["_fields"]:
                continue
            add(f"{t}.{f}", stored, e["_n"], e["_fields"][stored], spike.line_field_ok(f, e, gl), e["_signals"])
    if gt["id"] not in spike.PARTIAL_GT:
        used = set(m.values())
        for i, e in enumerate(ext_lines):
            if i in used:
                continue
            counts["spurious"] += 1
            t = "fee" if e["type"] != "expense" else "expense"
            for f in (spike.FEE_F if t == "fee" else spike.EXP_F):
                stored = LINE_MAP_INV[f]
                if e.get(f) not in (None, "") and stored in e["_fields"]:
                    add(f"{t}.{f}", stored, e["_n"], e["_fields"][stored], False, e["_signals"])
    return obs, counts


def score_export(export: dict, load_gt, load_full) -> dict:
    meta = {"run": export["run"], "source": export["source"]["analyzerId"], "version": export["source"]["apiVersion"]}
    all_obs: list[dict] = []
    findings: list[dict] = []
    tot = {"invoices": 0, "gt_lines": 0, "matched": 0, "spurious": 0}
    for inv in export["invoices"]:
        gt = load_gt(inv["id"])
        obs, c = score_invoice(inv, gt, load_full(inv["id"]), meta)
        all_obs += obs
        tot["invoices"] += 1
        for k in ("gt_lines", "matched", "spurious"):
            tot[k] += c[k]
        for f in inv.get("findings") or []:
            findings.append({**f, "item": inv["id"]})

    settings = cl.Settings(**{k: v for k, v in (export.get("settings") or {}).items() if k in cl.Settings.__dataclass_fields__})
    th = settings.threshold
    with_combined = [o for o in all_obs if o["combined"] is not None]
    rep = {
        "schema": "ebilling-calibration/1", "kind": "run-score", **meta, "settings": settings.__dict__,
        "counts": {**tot, "values": len(all_obs), "values_with_combined": len(with_combined)},
        "analyzer": {"overall": {"bins": cl.bin_table(all_obs), "at_or_above_threshold": cl.wrong_at_or_above(all_obs, th)},
                     "fields": cl.field_report(all_obs, "analyzer", th)},
        "combined": {"overall": {"bins": cl.bin_table(with_combined, "combined"),
                                 "at_or_above_threshold": cl.wrong_at_or_above(with_combined, th, "combined"),
                                 "reliability": cl.reliability_curve(with_combined, "combined")},
                     "fields": cl.field_report(with_combined, "combined", th),
                     "sweep": cl.field_sweep(with_combined, "combined")},
        "signals": cl.signal_contributions(all_obs),
    }
    if with_combined and all(cl.has_signal_breakdown(o) for o in with_combined):
        rep["settings_grid_top"] = cl.settings_grid(with_combined, settings)[:15]
    else:
        rep["settings_grid_top"] = None
        rep["settings_grid_note"] = "signal breakdown not exported for every value; export 'signals' (see Export-CalibrationRun.ps1)"
    if findings:
        fc = {(o["item"], o["line"], o["stored"]): o["combined"] for o in with_combined}
        truth = {(o["item"], o["line"], o["stored"]): o["correct"] for o in all_obs}
        rep["rejection_sweep"] = cl.rejection_sweep(findings, truth, fc)
        rep["findings"] = len(findings)
    return rep


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--export", nargs="+", required=True)
    ap.add_argument("--local", default=None)
    ap.add_argument("--out", default=str(HERE / "results"))
    a = ap.parse_args()
    if a.local:
        common.LOCAL = Path(a.local).resolve()
        common.LOCAL_RAW = common.LOCAL / "spike-005"
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    for path in a.export:
        ex = json.loads(Path(path).read_text(encoding="utf-8"))
        rep = score_export(ex, common.load_gt, common.load_full_gt)
        name = f"run-{ex['run']}.json"
        (out / name).write_text(json.dumps(rep, indent=1), encoding="utf-8")
        print(name, rep["counts"], "combined>=T:", rep["combined"]["overall"]["at_or_above_threshold"])


if __name__ == "__main__":
    main()
