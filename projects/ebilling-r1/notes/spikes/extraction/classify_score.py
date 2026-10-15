"""Task 005 step 4: activity code as an analyzer classify field (sprk_invoice_v1_cls) vs the separate coding step.

Items: the 21 spike set-B invoices whose fee lines print no activity code (CLS_ITEMS); answer key = intended_codes.
The cls analyzer's TimeEntries rows are matched to ground-truth lines with score.match_lines; the coding-step
predictions (coding.py, corpus B) are looked up for the same lines. Accuracy is over all GT lines in scope
(an unmatched line counts as wrong for the analyzer). Output: results/classify.json (counts and rates only).
"""
import json

from common import LOCAL_RAW, RESULTS, load_gt, load_raw, read_runs
from score import adapt_custom, cost_of, match_lines, summarise

CLS_ITEMS = ("B-0001,B-0004,B-0005,B-0006,B-0007,B-0009,B-0011,B-0012,B-0013,B-0014,B-0015,B-0016,B-0017,"
             "B-0018,B-0020,B-0021,B-0024,B-0026,B-0067,B-0091,B-0127").split(",")


def main():
    coding = {}
    for model in ("gpt-5.4-mini", "gpt-5.6-luna"):
        p = LOCAL_RAW / "coding" / f"{model}-B.json"
        coding[model] = json.loads(p.read_text(encoding="utf-8")) if p.exists() else {}
    n = cls_ok = cls_matched = 0
    conf_hi = []
    model_ok = {m: 0 for m in coding}
    billed_echo = 0
    for iid in CLS_ITEMS:
        gt = load_gt(iid)
        raw = load_raw("cls", iid)
        ext = adapt_custom(raw) if raw else None
        gtl = [ln for ln in gt["lines"] if ln["line_type"] in ("fee", "expense")]
        m = match_lines(ext["lines"], gtl) if ext else {}
        for j, g in enumerate(gtl):
            if g["line_type"] != "fee":
                continue
            ic = gt["intended_codes"].get(str(g["n"])) or []
            act = next((c for c in ic if c.startswith("A")), None)
            if not act or any(c.startswith("A") for c in g.get("codes") or []):
                continue
            n += 1
            e = ext["lines"][m[j]] if ext and j in m else None
            if e is not None:
                cls_matched += 1
                ok = e.get("activity_cls") == act
                cls_ok += ok
                billed_echo += bool(e.get("activity_billed"))
                if (e["conf"].get("activity_cls") or 0) >= 0.9:
                    conf_hi.append(ok)
            for model, preds in coding.items():
                model_ok[model] += (preds.get(f"{iid}:{g['n']}") or {}).get("activity_code") == act
    runs = {r["id"]: r for r in read_runs("cls")}
    lat = sorted(r["latency_s"] for r in runs.values() if r.get("status") == "Succeeded")
    out = {"items": len(CLS_ITEMS), "lines": n, "cls_matched_lines": cls_matched,
           "cls_activity_top1": round(100 * cls_ok / n, 1) if n else None,
           "cls_activity_top1_on_matched": round(100 * cls_ok / cls_matched, 1) if cls_matched else None,
           "cls_conf_ge_090": {"n": len(conf_hi), "wrong": conf_hi.count(False)},
           "cls_rows_with_billed_activity_value": billed_echo,
           "coding_step_activity_top1": {m: round(100 * v / n, 1) for m, v in model_ok.items()} if n else None,
           "cls_latency_p90_s": round(lat[int(0.9 * (len(lat) - 1))], 1) if lat else None,
           "cls_succeeded": len(lat)}
    # side effects of adding the classify field: extraction accuracy, cost and latency vs arm c on the same items
    confs = []
    for iid in CLS_ITEMS:
        raw = load_raw("cls", iid)
        if raw:
            confs += [ln["conf"].get("activity_cls") for ln in adapt_custom(raw)["lines"]
                      if ln["conf"].get("activity_cls") is not None]
    out["cls_confidence_p50_max"] = ([sorted(confs)[len(confs) // 2], max(confs)] if confs else None)
    ids = set(CLS_ITEMS)
    for arm in ("c", "cls"):
        sm = summarise(arm, ids)
        out[f"same_items_{arm}"] = {"fee_line_accuracy_repaired": sm["repaired_fee_line_accuracy"],
                                    "header_accuracy": sm["header_accuracy"], "latency_p90_s": sm["latency_p90_s"],
                                    "cost_mean_usd": sm["cost_mean_usd"]}
    RESULTS.mkdir(exist_ok=True)
    (RESULTS / "classify.json").write_text(json.dumps(out, indent=1), encoding="utf-8")
    print(json.dumps(out, indent=1))


if __name__ == "__main__":
    main()
