"""Task 005 step 5: does a preview-built analyzer run under the GA api-version? (research note 9.8 item 1)

Probes, each recorded to runs/probe.jsonl:
  1. GET sprk_invoice_v1p (created on 2026-06-01-preview) with api-version=2025-11-01
  2. analyzeBinary sprk_invoice_v1p with api-version=2025-11-01 on B-0004 (synthetic)
  3. GET + analyzeBinary sprk_invoice_router_p (preview router, enableSegment) with the GA api-version on B-0019
  4. reverse: GET sprk_invoice_v1 (GA-built) with the preview api-version (config.workflow reported)
  5. controls: each analyzer under its own api-version (must be 200)
Only plain-schema preview analyzers exist here (no labeled data / distillation), see the note.
"""
import json
import time

from common import CU_ENDPOINT, GA, PREVIEW, append_run, http, load_gt, pdf_path, utcnow


def get(aid, api):
    st, _, b = http("GET", f"{CU_ENDPOINT}/contentunderstanding/analyzers/{aid}?api-version={api}")
    return {"probe": f"GET {aid} @ {api}", "http": st,
            "workflow": ((b or {}).get("config") or {}).get("workflow") if isinstance(b, dict) else None,
            "error": (b or {}).get("error", {}).get("code") if isinstance(b, dict) and st >= 400 else None,
            "inner": ((b or {}).get("error", {}).get("innererror") or {}).get("code") if isinstance(b, dict) and st >= 400 else None}


def analyze(aid, api, item):
    gt = load_gt(item)
    t0 = time.time()
    st, h, b = http("POST", f"{CU_ENDPOINT}/contentunderstanding/analyzers/{aid}:analyzeBinary?api-version={api}",
                    pdf_path(gt).read_bytes(), "application/pdf")
    rec = {"probe": f"analyze {aid} @ {api} on {item}", "post_http": st}
    if st != 202:
        rec["error"] = (b or {}).get("error", {}).get("code") if isinstance(b, dict) else None
        rec["message"] = ((b or {}).get("error", {}).get("message") or "")[:300] if isinstance(b, dict) else None
        rec["inner"] = ((b or {}).get("error", {}).get("innererror") or {}).get("code") if isinstance(b, dict) else None
        return rec
    op = h.get("Operation-Location") or h.get("operation-location")
    while True:
        time.sleep(2)
        _, _, r = http("GET", op)
        if r.get("status") in ("Succeeded", "Failed", "Canceled"):
            break
    res = r.get("result") or {}
    rec.update(status=r.get("status"), latency_s=round(time.time() - t0, 1), apiVersion=res.get("apiVersion"),
               contents=len(res.get("contents") or []),
               fields=sorted({k for c in res.get("contents") or [] for k in (c.get("fields") or {})})[:20],
               segments=[{k: s.get(k) for k in ("category", "startPageNumber", "endPageNumber", "confidence")}
                         for c in res.get("contents") or [] for s in (c.get("segments") or [])],
               error=(r.get("error") or {}).get("code"))
    return rec


for rec in (get("sprk_invoice_v1p", GA), analyze("sprk_invoice_v1p", GA, "B-0004"),
            get("sprk_invoice_router_p", GA), analyze("sprk_invoice_router_p", GA, "B-0019"),
            get("sprk_invoice_v1", PREVIEW), get("sprk_invoice_v1", GA), get("sprk_invoice_v1p", PREVIEW)):
    rec["at"] = utcnow()
    rec["id"] = rec["probe"]
    append_run("probe", rec)
    print(json.dumps(rec))
