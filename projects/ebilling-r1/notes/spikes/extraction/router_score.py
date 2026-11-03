"""Task 005 bundled-PDF experiment: GA router (rga) vs preview router (rpv) on the 14 bundled spike items.

Per item: whether exactly one invoice segment was produced with the GT invoice page range, the segment
confidence (preview only), and whether field `source` page numbers refer to the ORIGINAL file pages
(research note 9.8 item 2: FirmName source page compared with the GT invoice first page).
Extraction accuracy of the routed results is scored with score.summarise on the same 14 items, beside the
un-routed c and c-prime arms. Output: results/router.json (counts and rates only).
"""
import json
import re

from common import RESULTS, load_raw, spike_items
from score import summarise


def main():
    items = [g for g in spike_items() if g["document"].get("bundled")]
    ids = {g["id"] for g in items}
    out = {"items": sorted(ids)}
    for arm in ("rga", "rpv"):
        rows = []
        for g in items:
            raw = load_raw(arm, g["id"])
            contents = ((raw or {}).get("result") or {}).get("contents") or []
            segs = [s for c in contents for s in (c.get("segments") or [])]
            inv = [c for c in contents if c.get("category") == "invoice" and c.get("fields")]
            first, n = g["document"]["invoice_first_page"], g["document"]["invoice_pages"]
            want = (first, first + n - 1)
            got = [(c.get("startPageNumber"), c.get("endPageNumber")) for c in inv]
            src = ((inv[0]["fields"].get("FirmName") or {}).get("source") or "") if inv else ""
            m = re.match(r"D\((\d+),", src)
            rows.append({"id": g["id"], "segments": len(segs), "invoice_contents": len(inv),
                         "invoice_range_ok": got == [want],
                         "segment_confidences": [s.get("confidence") for s in segs],
                         "firmname_source_page": int(m.group(1)) if m else None, "gt_invoice_first_page": first})
        ok = sum(r["invoice_range_ok"] for r in rows)
        # informative only where the invoice does not start on page 1 (segment-relative would then read 1)
        orig = [r for r in rows if r["firmname_source_page"] is not None and r["gt_invoice_first_page"] > 1]
        confs = [c for r in rows for c in r["segment_confidences"] if c is not None]
        out[arm] = {"split_correct": ok, "of": len(rows),
                    "source_pages_original_file": sum(1 for r in orig if r["firmname_source_page"] == r["gt_invoice_first_page"]),
                    "source_pages_segment_relative": sum(1 for r in orig if r["firmname_source_page"] == 1),
                    "source_pages_checked": len(orig),
                    "segment_confidence_present": len(confs), "segment_confidence_min": min(confs) if confs else None,
                    "rows": rows}
    for arm in ("c", "cp", "rga", "rpv"):
        s = summarise(arm, ids)
        out[f"accuracy_{arm}"] = {k: s[k] for k in ("header_accuracy", "fee_line_accuracy", "line_accuracy_all",
                                                    "line_recall", "conf_ge_090", "latency_p90_s", "cost_mean_usd",
                                                    "succeeded")}
    RESULTS.mkdir(exist_ok=True)
    (RESULTS / "router.json").write_text(json.dumps(out, indent=1), encoding="utf-8")
    print(json.dumps({k: (v if k.startswith("accuracy") else {kk: vv for kk, vv in v.items() if kk != "rows"} if isinstance(v, dict) else v)
                      for k, v in out.items()}, indent=1))


if __name__ == "__main__":
    main()
