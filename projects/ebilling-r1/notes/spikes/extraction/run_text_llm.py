"""Arms a and a2 of the task 005 spike: document text + one LLM structured-output call.

a  = the legacy Finance path as it stands in code: Document Intelligence prebuilt-read text
     (TextExtractorService: result.content, 100k-token truncation) -> ExtractInvoiceFactsAsync user prompt
     and FinanceJsonSchemas.ExtractionResultSchema (strict, temperature 0). The FinanceExtraction system
     prompt was never seeded in dev or the repo (#1918), so a one-paragraph stand-in is used (SYSTEM_A).
     Legacy default deployment gpt-4o does not exist in dev; the closest available deployment is used:
     spaarke-openai-dev 'gpt-4o-mini' (model gpt-4.1-mini 2025-04-14, non-reasoning, same API surface).
a2 = supplementary, the D-49 no-go fallback shape: DI prebuilt-layout markdown -> an FR-21 schema with
     per-field self-reported confidence on gpt-5.2 (reasoning effort low). Not an FR-03 arm; it shows what
     the fallback would score.

usage: python run_text_llm.py a|a2 [--ids ...] [--only-failed] [--workers 3]
"""
from __future__ import annotations

import argparse
import concurrent.futures as cf
import json
import time

from common import (AOAI_API, CU_ENDPOINT, DI_API, OPENAI_ENDPOINT, append_run, http, pdf_path, read_runs,
                    save_raw, spike_items, utcnow)

MAX_CHARS = 100_000 * 4   # DocumentIntelligenceOptions.MaxInputTokens * 4 (TextExtractorService truncation)

SYSTEM_A = ("You extract structured data from law-firm invoices. Read the document text and return the invoice "
            "header and every line item. Use costType 'Fee' for professional time entries and 'Expense' for "
            "costs or disbursements. Dates as yyyy-MM-dd. Amounts as numbers without currency symbols. "
            "Set extractionConfidence between 0 and 1 for the extraction as a whole.")

# Verbatim from InvoiceAnalysisService.ExtractInvoiceFactsAsync (no reviewer hints on first extraction).
USER_A = """Extract all invoice facts from the following document text.
Return the invoice header, line items, and your confidence in the extraction.

<document>
{text}
</document>"""

# Verbatim copy of FinanceJsonSchemas.ExtractionResultSchema (src/server/api/Sprk.Bff.Api/Services/Finance/Models).
SCHEMA_A = json.loads("""{
  "type": "object",
  "properties": {
    "header": {"type": "object", "properties": {
        "invoiceNumber": {"type": "string"}, "invoiceDate": {"type": "string"}, "totalAmount": {"type": "number"},
        "currency": {"type": "string"}, "vendorName": {"type": "string"}, "vendorAddress": {"type": ["string", "null"]},
        "paymentTerms": {"type": ["string", "null"]}},
      "required": ["invoiceNumber", "invoiceDate", "totalAmount", "currency", "vendorName", "vendorAddress", "paymentTerms"],
      "additionalProperties": false},
    "lineItems": {"type": "array", "items": {"type": "object", "properties": {
        "lineNumber": {"type": "integer"}, "description": {"type": "string"}, "costType": {"type": "string"},
        "amount": {"type": "number"}, "currency": {"type": "string"}, "eventDate": {"type": ["string", "null"]},
        "roleClass": {"type": ["string", "null"]}, "hours": {"type": ["number", "null"]}, "rate": {"type": ["number", "null"]}},
      "required": ["lineNumber", "description", "costType", "amount", "currency", "eventDate", "roleClass", "hours", "rate"],
      "additionalProperties": false}},
    "extractionConfidence": {"type": "number"},
    "notes": {"type": ["string", "null"]}
  },
  "required": ["header", "lineItems", "extractionConfidence", "notes"],
  "additionalProperties": false
}""")


def _v(t):
    return {"type": "object", "properties": {"value": {"type": [t, "null"]}, "confidence": {"type": "number"}},
            "required": ["value", "confidence"], "additionalProperties": False}


HDR = ["firm", "firm_invoice_number", "invoice_date", "billing_period_start", "billing_period_end",
       "firm_matter_reference", "po_number", "fees_total", "expenses_total", "adjustments_total", "total_due", "currency"]
NUM = {"fees_total", "expenses_total", "adjustments_total", "total_due"}


def _obj(props: dict) -> dict:
    return {"type": "object", "properties": props, "required": list(props), "additionalProperties": False}


SCHEMA_A2 = _obj({
    "header": _obj({k: _v("number" if k in NUM else "string") for k in HDR}),
    "fee_lines": {"type": "array", "items": _obj({
        "date": {"type": ["string", "null"]}, "timekeeper": {"type": ["string", "null"]}, "title": {"type": ["string", "null"]},
        "hours": {"type": ["number", "null"]}, "rate": {"type": ["number", "null"]}, "amount": {"type": ["number", "null"]},
        "narrative": {"type": ["string", "null"]}, "task_code": {"type": ["string", "null"]},
        "activity_code": {"type": ["string", "null"]}, "confidence": {"type": "number"}})},
    "expense_lines": {"type": "array", "items": _obj({
        "date": {"type": ["string", "null"]}, "description": {"type": ["string", "null"]},
        "quantity": {"type": ["number", "null"]}, "unit_cost": {"type": ["number", "null"]},
        "amount": {"type": ["number", "null"]}, "expense_code": {"type": ["string", "null"]},
        "confidence": {"type": "number"}})},
})

SYSTEM_A2 = (
    "You extract one outside-counsel (law firm) invoice from its layout markdown. Return the header and every row of "
    "the fee time-entry table and of the expense table. Header: firm = the law firm that issued the invoice; "
    "firm_invoice_number exactly as printed; invoice_date; billing period start/end only if stated; "
    "firm_matter_reference = the firm's matter or client-matter number as printed; po_number as quoted or null; "
    "fees_total, expenses_total, adjustments_total (negative for discounts or reductions), total_due = the current "
    "charges payable (not a prior balance); currency as an ISO 4217 code. Fee rows: date, timekeeper as printed "
    "(name or initials), title as printed, hours, rate, amount, full narrative, task and activity codes as printed. "
    "Expense rows: date, description, quantity, unit cost, amount, expense code as printed. Do not include "
    "timekeeper-summary rows, subtotals or adjustments as lines. Dates yyyy-MM-dd; numbers without symbols; null "
    "when absent. Give each header value and each row a confidence between 0 and 1 that it is exactly right.")

ARMS = {
    "a": {"di_model": "prebuilt-read", "endpoint": OPENAI_ENDPOINT, "deployment": "gpt-4o-mini",
          "schema": SCHEMA_A, "schema_name": "ExtractionResult", "system": SYSTEM_A, "temperature": 0,
          "max_tokens": 32768},
    "a2": {"di_model": "prebuilt-layout", "endpoint": CU_ENDPOINT, "deployment": "gpt-5.2",
           "schema": SCHEMA_A2, "schema_name": "InvoiceExtraction", "system": SYSTEM_A2, "reasoning_effort": "low",
           "max_tokens": 120000},
}


def di_text(model: str, data: bytes) -> tuple[str, dict]:
    q = "&outputContentFormat=markdown" if model == "prebuilt-layout" else ""
    url = f"{CU_ENDPOINT}/documentintelligence/documentModels/{model}:analyze?api-version={DI_API}{q}"
    st, hdrs, body = http("POST", url, data, "application/pdf")
    if st != 202:
        raise RuntimeError(f"di-post-{st}")
    op = hdrs.get("Operation-Location") or hdrs.get("operation-location")
    assert op.startswith(CU_ENDPOINT)
    while True:
        time.sleep(1.5)
        _, _, r = http("GET", op)
        if r.get("status") in ("succeeded", "failed"):
            break
    if r["status"] != "succeeded":
        raise RuntimeError("di-failed")
    ar = r["analyzeResult"]
    text = (ar.get("content") or "").strip()
    if not text:
        text = "\n".join(ln["content"] for p in ar.get("pages", []) for ln in p.get("lines", []))
    pages = len(ar.get("pages", []))
    return text, {"pages": pages, "chars": len(text)}


def run(arm: str, gt: dict) -> dict:
    cfg = ARMS[arm]
    data = pdf_path(gt).read_bytes()
    rec = {"id": gt["id"], "arm": arm, "deployment": cfg["deployment"], "started": utcnow()}
    t0 = time.time()
    text, info = di_text(cfg["di_model"], data)
    rec["di_s"] = round(time.time() - t0, 2)
    rec.update(info)
    if arm == "a" and len(text) > MAX_CHARS:
        text = text[:MAX_CHARS] + "\n\n[Content truncated due to size limits]"
        rec["truncated"] = True
    user = USER_A.format(text=text) if arm == "a" else f"<document>\n{text}\n</document>"
    body = {"messages": [{"role": "system", "content": cfg["system"]}, {"role": "user", "content": user}],
            "response_format": {"type": "json_schema", "json_schema": {
                "name": cfg["schema_name"], "schema": cfg["schema"], "strict": True}},
            "max_completion_tokens": cfg["max_tokens"]}
    if "temperature" in cfg:
        body["temperature"] = cfg["temperature"]
    if "reasoning_effort" in cfg:
        body["reasoning_effort"] = cfg["reasoning_effort"]
    url = f"{cfg['endpoint']}/openai/deployments/{cfg['deployment']}/chat/completions?api-version={AOAI_API}"
    t1 = time.time()
    st, _, res = http("POST", url, body, timeout=900)
    rec["llm_s"] = round(time.time() - t1, 2)
    rec["latency_s"] = round(time.time() - t0, 2)
    rec["http"] = st
    if st != 200:
        rec["status"] = "llm-failed"
        rec["error"] = (res or {}).get("error", {}).get("code") if isinstance(res, dict) else str(st)
        rec["error_message"] = ((res or {}).get("error", {}).get("message") or "")[:300] if isinstance(res, dict) else ""
        return rec
    ch = res["choices"][0]
    rec["finish_reason"] = ch.get("finish_reason")
    rec["model"] = res.get("model")
    rec["usage"] = {k: res["usage"].get(k) for k in ("prompt_tokens", "completion_tokens")}
    rec["usage"]["reasoning_tokens"] = (res["usage"].get("completion_tokens_details") or {}).get("reasoning_tokens")
    rec["usage"]["cached_tokens"] = (res["usage"].get("prompt_tokens_details") or {}).get("cached_tokens")
    try:
        parsed = json.loads(ch["message"]["content"])
        rec["status"] = "Succeeded"
    except (ValueError, TypeError):
        parsed = None
        rec["status"] = "unparseable-output"
    save_raw(arm, gt, {"parsed": parsed, "usage": rec["usage"], "finish_reason": rec["finish_reason"]},
             {"parsed": parsed, "usage": rec["usage"]})
    return rec


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("arm", choices=list(ARMS))
    ap.add_argument("--ids")
    ap.add_argument("--only-failed", action="store_true")
    ap.add_argument("--workers", type=int, default=3)
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
        futs = {ex.submit(run, a.arm, g): g["id"] for g in items}
        for f in cf.as_completed(futs):
            try:
                rec = f.result()
            except Exception as e:
                rec = {"id": futs[f], "arm": a.arm, "status": "harness-error", "error": type(e).__name__,
                       "error_message": str(e)[:200]}
            append_run(a.arm, rec)
            print(rec["id"], rec.get("status"), rec.get("latency_s"), rec.get("finish_reason"),
                  rec.get("error") or "", flush=True)
    print(f"{utcnow()} done", flush=True)


if __name__ == "__main__":
    main()
