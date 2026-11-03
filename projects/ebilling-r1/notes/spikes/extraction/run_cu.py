"""Run a Content Understanding analyzer over the spike items (task 005 arms b, c, c-prime, cls, routers).

usage: python run_cu.py ARM [--ids B-0001,B-0004] [--only-failed] [--workers 4]
ARM -> (analyzerId, api-version) in ARMS below. Records per item: wall latency (POST to terminal
status), status, warnings count, usage block, page count, row counts. Raw results go to the git-ignored
local folder; a stripped copy (fields + usage, no markdown/pages) is committed for synthetic items only.
"""
from __future__ import annotations

import argparse
import concurrent.futures as cf
import time

from common import (CU_ENDPOINT, GA, PREVIEW, append_run, http, pdf_path, read_runs, save_raw,
                    spike_items, utcnow)

ARMS = {
    "b": ("prebuilt-invoice", GA),
    "c": ("sprk_invoice_v1", GA),
    "cp": ("sprk_invoice_v1p", PREVIEW),          # same schema, created on the preview API (spike only)
    "cls": ("sprk_invoice_v1_cls", GA),            # copy of v1 + ActivityCodeClassified (classify)
    "rga": ("sprk_invoice_router", GA),            # bundled-PDF router (contentCategories + enableSegment)
    "rpv": ("sprk_invoice_router_p", PREVIEW),     # same router on preview (segment confidence)
    "crep": ("sprk_invoice_v1", GA),               # repeat of arm c on the items where c and c-prime differ
    "cprep": ("sprk_invoice_v1p", PREVIEW),        # repeat of arm c-prime on the same items
    "probe": ("sprk_invoice_v1p", GA),             # preview-built analyzer called with the GA api-version
}


def strip(result: dict) -> dict:
    """Keep fields, usage, warnings and segment metadata; drop markdown, pages, tables, spans of text."""
    out = {k: result.get(k) for k in ("status", "usage")}
    r = result.get("result") or {}
    out["result"] = {"analyzerId": r.get("analyzerId"), "apiVersion": r.get("apiVersion"),
                     "warnings": r.get("warnings"), "contents": []}
    for c in r.get("contents") or []:
        out["result"]["contents"].append({k: c.get(k) for k in (
            "kind", "category", "path", "analyzerId", "startPageNumber", "endPageNumber", "fields", "segments")
            if k in c})
    return out


def analyze(arm: str, gt: dict) -> dict:
    analyzer, api = ARMS[arm]
    data = pdf_path(gt).read_bytes()
    url = f"{CU_ENDPOINT}/contentunderstanding/analyzers/{analyzer}:analyzeBinary?api-version={api}"
    t0 = time.time()
    st, hdrs, body = http("POST", url, data, "application/pdf", headers={"x-ms-diagnostics": "true"})
    rec = {"id": gt["id"], "arm": arm, "analyzer": analyzer, "api": api, "bytes": len(data),
           "started": utcnow(), "post_status": st}
    if st != 202:
        rec.update(status="post-failed", error=(body or {}).get("error", {}).get("code") if isinstance(body, dict) else str(st),
                   message=((body or {}).get("error", {}).get("message", "")[:300] if isinstance(body, dict) else ""),
                   latency_s=round(time.time() - t0, 2))
        return rec
    op = hdrs.get("Operation-Location") or hdrs.get("operation-location")
    assert op and op.startswith(CU_ENDPOINT), "unexpected operation host"
    while True:
        time.sleep(2)
        st2, _, res = http("GET", op)
        status = (res or {}).get("status") if isinstance(res, dict) else None
        if status in ("Succeeded", "Failed", "Canceled") or st2 >= 400 or time.time() - t0 > 1800:
            break
    rec["latency_s"] = round(time.time() - t0, 2)
    rec["status"] = status or f"http-{st2}"
    if isinstance(res, dict):
        r = res.get("result") or {}
        rec["usage"] = res.get("usage") or r.get("usage")
        rec["warnings"] = [(w.get("code"), (w.get("message") or "")[:200]) for w in (r.get("warnings") or [])]
        rec["error"] = (res.get("error") or {}).get("code")
        rec["error_message"] = ((res.get("error") or {}).get("message") or "")[:300]
        contents = r.get("contents") or []
        rec["contents"] = len(contents)
        rec["pages"] = max([c.get("endPageNumber") or 0 for c in contents] or [0])
        rec["categories"] = [c.get("category") for c in contents if c.get("category")]
        save_raw(arm, gt, res, strip(res))
    return rec


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("arm", choices=list(ARMS))
    ap.add_argument("--ids")
    ap.add_argument("--only-failed", action="store_true")
    ap.add_argument("--workers", type=int, default=4)
    a = ap.parse_args()
    items = spike_items()
    if a.ids:
        want = set(a.ids.split(","))
        items = [g for g in items if g["id"] in want]
    if a.only_failed:
        ok = {r["id"] for r in read_runs(a.arm) if r.get("status") == "Succeeded"}
        items = [g for g in items if g["id"] not in ok]
    print(f"{utcnow()} arm={a.arm} items={len(items)}", flush=True)
    with cf.ThreadPoolExecutor(a.workers) as ex:
        futs = {ex.submit(analyze, a.arm, g): g["id"] for g in items}
        for f in cf.as_completed(futs):
            try:
                rec = f.result()
            except Exception as e:  # record, never print document content
                rec = {"id": futs[f], "arm": a.arm, "status": "harness-error", "error": type(e).__name__,
                       "error_message": str(e)[:200]}
            append_run(a.arm, rec)
            print(rec["id"], rec.get("status"), rec.get("latency_s"), rec.get("error") or "", flush=True)
    print(f"{utcnow()} done", flush=True)


if __name__ == "__main__":
    main()
