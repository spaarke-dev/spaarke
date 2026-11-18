"""Shared calibration maths for task 161 (D-47 calibration report). Pure functions, stdlib only.

An OBSERVATION is one scored extracted value:

    {"run": "<run id>", "source": "<analyzer id or Action code>", "version": "<api / action version>",
     "item": "<evaluation item id>", "field": "fee.hours" | "header.total_due" | ...,
     "line": <1-based line ref or null>, "correct": true|false,
     "analyzer": 0..1,                       # the analyzer's confidence (sprk_extractionconfidence .confidence)
     "combined": 0..1 | null,                # 046's combined confidence (.combined); null on the offline baseline
     "selfReport": 0..1 | null,
     "arithmetic": "Passed"|"Failed"|"NotApplicable",
     "reference": "Matched"|"Missing"|"NotApplicable", "referenceStrength": 0..1}

Results are ALWAYS computed per (source, version) group, never pooled across them (POML constraint, FR-31, D-81).
ADR-015: observations carry ids, field names, flags and confidences only. No document text, names or amounts.
"""
from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, replace

# Same bins as 005's score.py so the baseline is comparable with extraction-spike.md section 3.
BINS = [(0.0, 0.5), (0.5, 0.7), (0.7, 0.8), (0.8, 0.9), (0.9, 0.95), (0.95, 1.0001)]
SWEEP_THRESHOLDS = [round(0.70 + 0.01 * i, 2) for i in range(30)]   # 0.70 .. 0.99 (POML step 4)
CURRENT_THRESHOLD = 0.90                                              # sprk_reliabilitythreshold, seeded (025)


def pct(c: int, n: int):
    return round(100.0 * c / n, 1) if n else None


def rate(c: int, n: int):
    return round(c / n, 4) if n else None


def group_key(o: dict) -> str:
    return f"{o.get('source') or '?'}@{o.get('version') or '?'}"


def by_group(obs: list[dict]) -> dict[str, list[dict]]:
    g: dict[str, list[dict]] = defaultdict(list)
    for o in obs:
        g[group_key(o)].append(o)
    return dict(g)


def by_field(obs: list[dict]) -> dict[str, list[dict]]:
    g: dict[str, list[dict]] = defaultdict(list)
    for o in obs:
        g[o["field"]].append(o)
    return dict(sorted(g.items()))


def conf_of(o: dict, signal: str) -> float | None:
    """signal: 'analyzer' | 'combined' | 'selfReport'."""
    v = o.get(signal)
    return None if v is None else float(v)


# --------------------------------------------------------------------------- bins and curves

def bin_table(obs: list[dict], signal: str = "analyzer") -> list[dict]:
    """Confidence band, count, wrong, accuracy (percent). Observations with no value for the signal are skipped."""
    scored = [(conf_of(o, signal), bool(o["correct"])) for o in obs if conf_of(o, signal) is not None]
    rows = []
    for lo, hi in BINS:
        b = [ok for c, ok in scored if lo <= c < hi]
        rows.append({"bin": f"{lo:.2f}-{min(hi, 1.0):.2f}", "n": len(b), "wrong": b.count(False),
                     "accuracy": pct(b.count(True), len(b)), "wrong_rate": pct(b.count(False), len(b))})
    return rows


def wrong_at_or_above(obs: list[dict], threshold: float, signal: str = "analyzer") -> dict:
    hi = [bool(o["correct"]) for o in obs if (c := conf_of(o, signal)) is not None and c >= threshold]
    return {"threshold": threshold, "n": len(hi), "wrong": hi.count(False), "wrong_rate": pct(hi.count(False), len(hi))}


def reliability_curve(obs: list[dict], signal: str = "analyzer") -> dict:
    """Per bin: mean confidence vs observed accuracy, and the expected calibration error (count-weighted |gap|)."""
    pts = []
    total = 0
    ece = 0.0
    scored = [(conf_of(o, signal), bool(o["correct"])) for o in obs if conf_of(o, signal) is not None]
    for lo, hi in BINS:
        b = [(c, ok) for c, ok in scored if lo <= c < hi]
        if not b:
            continue
        mean_c = sum(c for c, _ in b) / len(b)
        acc = sum(ok for _, ok in b) / len(b)
        pts.append({"bin": f"{lo:.2f}-{min(hi, 1.0):.2f}", "n": len(b), "mean_confidence": round(mean_c, 4),
                    "accuracy": round(acc, 4), "gap": round(acc - mean_c, 4)})
        total += len(b)
        ece += len(b) * abs(acc - mean_c)
    return {"points": pts, "ece": round(ece / total, 4) if total else None, "n": total}


def field_report(obs: list[dict], signal: str = "analyzer", threshold: float = CURRENT_THRESHOLD) -> dict:
    """POML acceptance: per field a bin table with counts and accuracy, the wrong rate at or above the current threshold,
    and a reliability curve."""
    out = {}
    for field, rows in by_field(obs).items():
        out[field] = {
            "n": len(rows),
            "accuracy": pct(sum(bool(o["correct"]) for o in rows), len(rows)),
            "bins": bin_table(rows, signal),
            "at_or_above_threshold": wrong_at_or_above(rows, threshold, signal),
            "reliability": reliability_curve(rows, signal),
        }
    return out


# --------------------------------------------------------------------------- field-level threshold sweep

def field_sweep(obs: list[dict], signal: str = "combined", thresholds=SWEEP_THRESHOLDS) -> list[dict]:
    """For each threshold T: values at or above T that are wrong (a misread the gate would trust), and the share of values
    BELOW T (flagged for review). One row per T."""
    scored = [(conf_of(o, signal), bool(o["correct"])) for o in obs if conf_of(o, signal) is not None]
    rows = []
    for t in thresholds:
        above = [ok for c, ok in scored if c >= t]
        below = [ok for c, ok in scored if c < t]
        rows.append({
            "threshold": t, "values": len(scored),
            "trusted": len(above), "trusted_wrong": above.count(False),
            "trusted_wrong_rate": pct(above.count(False), len(above)),
            "flagged_for_review": len(below), "flagged_share": pct(len(below), len(scored)),
            "flagged_but_correct": below.count(True),
        })
    return rows


# --------------------------------------------------------------------------- 046 replica

@dataclass(frozen=True)
class Settings:
    """sprk_ebillingsetting values (025), seeded provisionally (D-69). All within 0..1."""
    threshold: float = 0.90
    weight_analyzer: float = 0.50
    weight_arithmetic: float = 0.25
    weight_reference: float = 0.15
    weight_self_report: float = 0.10
    arithmetic_fail_cap: float = 0.50
    reference_missing_cap: float = 0.80

    def validate(self) -> None:
        for name, v in self.__dict__.items():
            if not 0.0 <= v <= 1.0:
                raise ValueError(f"{name}={v} outside 0..1 (025 valid range)")


def _clamp(x: float) -> float:
    return min(1.0, max(0.0, x))


def combine(o: dict, s: Settings) -> float:
    """Mirror of CombinedConfidenceCalculator.Calculate (046): weighted mean over APPLICABLE signals, then ceiling caps.
    Self-report signal = min(self-report, analyzer): it can lower a score, never raise it above the analyzer."""
    analyzer = _clamp(float(o.get("analyzer") or 0.0))
    self_report = None
    if o.get("selfReport") is not None:
        self_report = min(_clamp(float(o["selfReport"])), analyzer)
    num = den = 0.0

    def add(w: float, sig: float) -> None:
        nonlocal num, den
        w = max(0.0, w)
        num += w * sig
        den += w

    add(s.weight_analyzer, analyzer)
    arith = o.get("arithmetic") or "NotApplicable"
    if arith != "NotApplicable":
        add(s.weight_arithmetic, 1.0 if arith == "Passed" else 0.0)
    ref = o.get("reference") or "NotApplicable"
    if ref != "NotApplicable":
        add(s.weight_reference, _clamp(float(o.get("referenceStrength", 1.0))) if ref == "Matched" else 0.0)
    if self_report is not None:
        add(s.weight_self_report, self_report)
    combined = num / den if den > 0 else 0.0
    if arith == "Failed":
        combined = min(combined, _clamp(s.arithmetic_fail_cap))
    if ref == "Missing":
        combined = min(combined, _clamp(s.reference_missing_cap))
    return _clamp(combined)


def recombine(obs: list[dict], s: Settings) -> list[dict]:
    """Observations with 'combined' recomputed under other settings (needs the signal breakdown)."""
    return [{**o, "combined": combine(o, s)} for o in obs]


def has_signal_breakdown(o: dict) -> bool:
    return o.get("analyzer") is not None and (o.get("arithmetic") is not None or o.get("reference") is not None)


def signal_contributions(obs: list[dict]) -> dict:
    """Per signal: how well it separates correct from wrong values on its own (D-47: report each signal, not only the
    combined score). For each signal the mean value on correct vs wrong observations and the wrong rate when it 'fails'.
    arithmetic: Failed vs Passed; reference: Missing vs Matched; analyzer / selfReport: mean and wrong rate below 0.90."""
    def stats(sub, label):
        n = len(sub)
        w = sum(1 for o in sub if not o["correct"])
        return {"label": label, "n": n, "wrong": w, "wrong_rate": pct(w, n)}

    out: dict = {}
    a = [o for o in obs if o.get("analyzer") is not None]
    out["analyzer"] = {
        "mean_when_correct": _mean([o["analyzer"] for o in a if o["correct"]]),
        "mean_when_wrong": _mean([o["analyzer"] for o in a if not o["correct"]]),
        "bands": [stats([o for o in a if o["analyzer"] >= 0.9], ">=0.90"),
                  stats([o for o in a if o["analyzer"] < 0.9], "<0.90")],
    }
    arith = [o for o in obs if o.get("arithmetic") in ("Passed", "Failed")]
    out["arithmetic"] = {"bands": [stats([o for o in arith if o["arithmetic"] == "Passed"], "Passed"),
                                   stats([o for o in arith if o["arithmetic"] == "Failed"], "Failed")]}
    ref = [o for o in obs if o.get("reference") in ("Matched", "Missing")]
    out["reference"] = {"bands": [stats([o for o in ref if o["reference"] == "Matched"], "Matched"),
                                  stats([o for o in ref if o["reference"] == "Missing"], "Missing")]}
    sr = [o for o in obs if o.get("selfReport") is not None]
    out["selfReport"] = {
        "n": len(sr),
        "mean_when_correct": _mean([o["selfReport"] for o in sr if o["correct"]]),
        "mean_when_wrong": _mean([o["selfReport"] for o in sr if not o["correct"]]),
        "bands": [stats([o for o in sr if o["selfReport"] >= 0.9], ">=0.90"), stats([o for o in sr if o["selfReport"] < 0.9], "<0.90")],
    }
    return out


def _mean(xs):
    return round(sum(xs) / len(xs), 4) if xs else None


def settings_grid(obs: list[dict], base: Settings, *, thresholds=SWEEP_THRESHOLDS,
                  weights=None, caps=None) -> list[dict]:
    """What-if search over weights and caps (needs the signal breakdown on every observation). Each candidate is scored by
    (trusted_wrong at its threshold, flagged share). Candidates stay inside 0..1; the self-report weight is kept small by
    construction (D-47: minor input). Returns rows sorted by (trusted_wrong, flagged_share)."""
    weights = weights or [(0.50, 0.25, 0.15, 0.10), (0.60, 0.20, 0.15, 0.05), (0.40, 0.30, 0.20, 0.10),
                          (0.30, 0.35, 0.25, 0.10), (0.50, 0.30, 0.20, 0.00), (0.35, 0.30, 0.30, 0.05)]
    caps = caps or [(0.50, 0.80), (0.50, 0.70), (0.40, 0.70), (0.30, 0.60)]
    rows = []
    for w in weights:
        for c in caps:
            cand = replace(base, weight_analyzer=w[0], weight_arithmetic=w[1], weight_reference=w[2], weight_self_report=w[3],
                           arithmetic_fail_cap=c[0], reference_missing_cap=c[1])
            cand.validate()
            re_obs = recombine(obs, cand)
            for t in thresholds:
                above = [o for o in re_obs if o["combined"] >= t]
                wrong = sum(1 for o in above if not o["correct"])
                rows.append({"weights": list(w), "caps": list(c), "threshold": t, "trusted": len(above), "trusted_wrong": wrong,
                             "flagged_share": pct(len(re_obs) - len(above), len(re_obs))})
    rows.sort(key=lambda r: (r["trusted_wrong"], r["flagged_share"], -r["threshold"]))
    return rows


# --------------------------------------------------------------------------- NFR-06: rejections caused by misreads

# FR-26 / BillingRuleCatalogue.IsRejectEligible: catalogue positions 1, 2, 6-10. ApprovedTimekeeper is hold-only (D-89).
REJECT_ELIGIBLE = {"ApprovedTimekeeper", "ApprovedRate", "ExpenseRule", "InvoiceAge", "CoreRecordWindow", "Duplicate", "RequiredData"}
HOLD_ONLY = {"ApprovedTimekeeper"}
AI_ASSIGNED_FIELDS = {"BilledTaskCode", "BilledActivityCode", "BilledExpenseCode", "TaskCode", "ActivityCode", "ExpenseCode"}


def would_reject(f: dict, field_conf: dict, threshold: float) -> tuple[bool, str]:
    """Replay of SubmissionRejectionRule.DecideFinding for one finding at one threshold.
    f: {"item", "ruleType", "consequence", "scope": "line"|"invoice", "reason": str|None,
        "readFields": [{"line": n|None, "field": "Hours"}]}
    field_conf: {(item, line, field): combined confidence}. Returns (rejects, held_because)."""
    if f.get("consequence") != "Reject":
        return False, "consequence_not_reject"
    t = f.get("ruleType")
    if t not in REJECT_ELIGIBLE:
        return False, "type_not_reject_eligible"
    if t in HOLD_ONLY:
        return False, "type_holds_only"
    if t == "ApprovedRate" and f.get("reason") != "RateAboveApproved":
        return False, "reference_missing_holds"
    if t == "Duplicate" and f.get("scope") != "invoice":
        return False, "duplicate_at_line_level"
    reads = f.get("readFields") or []
    if not reads:
        return False, "no_read_fields"
    for r in reads:
        if r["field"] in AI_ASSIGNED_FIELDS:
            return False, "ai_assigned_input"
    for r in reads:
        c = field_conf.get((f.get("item"), r.get("line"), r["field"]))
        if c is None or c < threshold:
            return False, "field_below_threshold"
    return True, ""


def rejection_sweep(findings: list[dict], truth: dict, field_conf: dict, thresholds=SWEEP_THRESHOLDS) -> list[dict]:
    """NFR-06 per threshold. truth: {(item, line, field): bool correct}. A rejection is MISREAD-CAUSED when it fires and at least one
    field it read was wrong against ground truth. A rejection whose read fields are all correct is not a misread (it is a true
    or rule-level outcome and is reported separately as 'rejects_all_fields_correct')."""
    rows = []
    for t in thresholds:
        rej = misread = clean = 0
        for f in findings:
            ok, _ = would_reject(f, field_conf, t)
            if not ok:
                continue
            rej += 1
            wrong = [r for r in f["readFields"] if truth.get((f.get("item"), r.get("line"), r["field"])) is False]
            if wrong:
                misread += 1
            else:
                clean += 1
        rows.append({"threshold": t, "rejects": rej, "misread_caused": misread, "rejects_all_fields_correct": clean})
    return rows
