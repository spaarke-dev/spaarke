"""Builds notes/calibration-report.md from report-template.md (prose) and results/*.json (numbers), so every table in the report is
reproduced by re-running the harness: analyzer_baseline.py -> cost_estimate.py -> render_report.py.

usage:  python -I render_report.py            (reads results/, writes ../calibration-report.md)
Tokens in the template: {{BINS:<arm>}} {{FIELDS:<arm>}} {{RELIAB:<arm>}} {{SWEEP:<arm>}} {{ORACLE:<arm>}} {{COST}} {{LIVE_RESULTS}}
{{LIVE_RESULTS}} lists any run-*.json / corrections-*.json / findings-*.json present in results/ (empty section until the owner-approved runs).
ADR-015: ids, field names, counts, rates only.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import calib_lib as cl  # noqa: E402

RES = HERE / "results"
LABEL = {"c": "GA (sprk_invoice_v1, 2025-11-01)", "cp": "preview (sprk_invoice_v1p, 2026-06-01-preview)"}


def load(name):
    return json.loads((RES / name).read_text(encoding="utf-8"))


def md(rows, header):
    out = ["| " + " | ".join(header) + " |", "|" + "|".join("---" if i == 0 else "---:" for i in range(len(header))) + "|"]
    out += ["| " + " | ".join(str(c) for c in r) + " |" for r in rows]
    return "\n".join(out)


def f(x, nd=1):
    return "-" if x is None else (f"{x:.{nd}f}" if isinstance(x, float) else str(x))


def bins_table(arm):
    d = load(f"analyzer-baseline-{arm}.json")
    rows = [[b["bin"], b["n"], b["wrong"], f(b["accuracy"]), f(b["wrong_rate"])] for b in d["overall"]["bins"]]
    t = d["overall"]["at_or_above_threshold"]
    rows.append([">= 0.90", t["n"], t["wrong"], f(100 - t["wrong_rate"]) if t["wrong_rate"] is not None else "-", f(t["wrong_rate"])])
    e = d["overall_excluding_codes"]["at_or_above_threshold"]
    rows.append([">= 0.90, 005 basis (no code fields)", e["n"], e["wrong"], f(100 - e["wrong_rate"]), f(e["wrong_rate"])])
    return md(rows, ["Analyzer confidence", "Values", "Wrong", "Accuracy %", "Wrong %"])


def fields_table(arm):
    d = load(f"analyzer-baseline-{arm}.json")
    hdr = ["Field", "Values", "Acc %"] + [b["bin"] for b in d["overall"]["bins"]] + ["n >= .90", "Wrong >= .90", "Wrong % >= .90"]
    rows = []
    for fld, v in d["fields"].items():
        cells = [f"{b['n']} / {f(b['accuracy'], 0) if b['accuracy'] is not None else '-'}" for b in v["bins"]]
        t = v["at_or_above_threshold"]
        rows.append([fld, v["n"], f(v["accuracy"])] + cells + [t["n"], t["wrong"], f(t["wrong_rate"])])
    return md(rows, hdr) + "\n\nBin cells are `count / accuracy %`."


def reliab_table(arm):
    d = load(f"analyzer-baseline-{arm}.json")
    rows = []
    for fld, v in d["fields"].items():
        r = v["reliability"]
        pts = "; ".join(f"{p['mean_confidence']:.2f}->{p['accuracy']:.3f} (n={p['n']})" for p in r["points"])
        rows.append([fld, f(r["ece"], 3), pts])
    o = d["overall"]["reliability"]
    rows.append(["**all fields**", f(o["ece"], 3), "; ".join(f"{p['mean_confidence']:.2f}->{p['accuracy']:.3f}" for p in o["points"])])
    return md(rows, ["Field", "ECE", "Reliability curve: mean confidence -> observed accuracy, per bin"])


def sweep_table(arm):
    d = load(f"analyzer-baseline-{arm}.json")
    rows = [[r["threshold"], r["trusted"], r["trusted_wrong"], f(r["trusted_wrong_rate"], 2), f(r["flagged_share"])] for r in d["sweep_analyzer_only"]]
    return md(rows, ["Threshold", "Trusted values", "Trusted and wrong", "Wrong % of trusted", "Flagged for review % (below T)"])


def oracle_table(arm):
    """Upper bound for the arithmetic signal (D-47): hours, rate and amount on a fee line are checked against each other (hours x rate =
    amount, 046 ArithmeticChecker). ORACLE: arithmetic is Passed when the value is right and Failed when it is wrong. No real check can do
    better, so this bounds what the arithmetic signal can add at the seeded weights; the real fraction comes from the dev run."""
    d = load(f"analyzer-baseline-{arm}.json")
    del d  # numbers come from re-scoring below, through analyzer_baseline's observations when the raw data is available
    path = RES / f"oracle-{arm}.json"
    if not path.exists():
        return "_not computed (needs the raw 005 responses: python -I analyzer_baseline.py --local ...)_"
    o = json.loads(path.read_text(encoding="utf-8"))
    rows = [[r["field"], r["values"], f(r["reach_analyzer_only"]), f(r["reach_with_oracle_arithmetic"]),
             r["wrong_trusted_analyzer_only"], r["wrong_trusted_with_oracle_arithmetic"]] for r in o["rows"]]
    return md(rows, ["Field", "Values", "Reach >= .90, analyzer only %", "Reach >= .90, with arithmetic %", "Trusted & wrong, analyzer only",
                     "Trusted & wrong, with arithmetic"])


def wrongs_table(arm):
    d = load(f"analyzer-baseline-{arm}.json")
    rows = [[w["item"], w["field"], w["line"] if w["line"] is not None else "-", f(w["analyzer"], 3)] for w in d["trusted_wrong"]]
    return md(rows, ["Item", "Field", "Line", "Analyzer confidence"])


def cost_table():
    c = load("cost-estimate.json")
    u = c["unit_costs_from_005"]
    rows = [[k, v["items"], v["pages"], f"${v['c']:.2f}", f"${v['cp']:.2f}", f"${2 * v['coding_per_analyzer']:.2f}", f"${v['total_both_analyzers']:.2f}",
             "yes" if v["exceeds_accepted_005_spend"] else "no"] for k, v in c["scenarios"].items()]
    t = md(rows, ["Scenario", "Items", "Pages", "GA analyzer", "Preview analyzer", "Coding (both)", "Total", f"Over the ${c['accepted_005_spend_usd']:.0f} accepted for 005"])
    t += (f"\n\nUnit costs measured in 005: GA ${u['c']['usd_per_page_le4']}/page for invoices of 4 pages or fewer (${u['c']['usd_per_page_all']} over all 41 items); "
          f"preview ${u['cp']['usd_per_page_le4']}/page (${u['cp']['usd_per_page_all']}); coding $0.000255 per line. "
          "Scenario descriptions:\n\n" + "\n".join(f"- **{k}**: {v['what']}" for k, v in c["scenarios"].items()))
    return t


def live_results():
    files = sorted(p.name for p in RES.glob("*.json") if re.match(r"(run|corrections|findings)-", p.name))
    if not files:
        return ("_None yet. No dev run, correction query or finding score has been performed: each needs the owner's OK (section 9). "
                "This section is filled by re-running this renderer after those runs; the harness has no other state._")
    return "\n".join(f"- `results/{n}`" for n in files)


TOKENS = {"BINS": bins_table, "FIELDS": fields_table, "RELIAB": reliab_table, "SWEEP": sweep_table, "ORACLE": oracle_table, "WRONGS": wrongs_table}


def main() -> None:
    tpl_path = HERE / "report-template.md"
    if not tpl_path.exists():
        raise SystemExit(f"{tpl_path.name} not found: the report prose is authored by the main session (sub-agents may not write report "
                         "documents); place it next to this script, using the {{TOKENS}} listed in the docstring.")
    tpl = tpl_path.read_text(encoding="utf-8")

    def sub(m):
        kind, _, arg = m.group(1).partition(":")
        if kind == "COST":
            return cost_table()
        if kind == "LIVE_RESULTS":
            return live_results()
        return TOKENS[kind](arg)

    out = re.sub(r"\{\{([A-Z_]+(?::[a-z]+)?)\}\}", sub, tpl)
    dest = HERE.parent / "calibration-report.md"
    dest.write_text(out, encoding="utf-8")
    print(f"wrote {dest} ({len(out)} chars)")


if __name__ == "__main__":
    main()
