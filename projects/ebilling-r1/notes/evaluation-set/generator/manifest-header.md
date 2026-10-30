# eBilling evaluation set — manifest

> Task 002 (spec FR-02). Consumers: extraction spike (005), D-47 calibration report (161), accuracy evaluation (162).
> **This file is generated** by `generator/build_manifest.py` from `generator/manifest-header.md` (sections 1-2, hand-written) and every `ground-truth/**/*.json` `meta` block. Edit the header or the generator, not the tables.

## 1. Ground-truth format (contract — change only with a dated note in section 7)

All ground truth is UTF-8 JSON, one file per item, minified or indented, schema tag `ebilling-eval/1`. Every file has the same envelope:

```
{ "schema": "ebilling-eval/1",
  "kind":   "invoice" | "fee-application" | "real-invoice" | "ocg" | "rate-card" | "rule-set",
  "id":     "B-0001",                       // set letter + number; unique
  "set":    "A" | "B" | "C" | "E" | "F",
  "meta": {
    "title": "...",
    "source_url": "https://..."            // real items; synthetic items use "generator_id" instead
    "generator_id": "gen_eval_set.py@1 seed=20261015 template=T07",
    "licence": "...", "licence_status": "open" | "public-record-no-licence" | "synthetic",
    "pii_note": "...", "pii_scan": { "tool", "scanned", "counts": {..}, "disposition": "..." } | null,
    "storage": { "location": "committed" | "local-only" | "regenerate", "path": "..." },
    "spike": true | false,                 // member of the 005 spike subset
    "label_method": "generator" | "parser+reconciliation" | "extracted+reviewed" | "ai-drafted-pending-review"
  },
  ... kind-specific body ... }
```

### 1.1 `invoice` (sets B and F) and `real-invoice` / `fee-application` (sets C and A)

Field names follow spec FR-21 exactly.

```
"header": {
  "firm", "firm_invoice_number", "invoice_date",            // ISO yyyy-mm-dd
  "billing_period_start", "billing_period_end",
  "firm_matter_reference", "po_number",                      // PO as quoted; null if none
  "fees_total", "expenses_total", "adjustments_total", "total_due",   // decimal numbers, USD unless currency says otherwise
  "currency"                                                  // ISO 4217
},
// real invoices (set C) add to header: "current_charges" (fees + adjustments + expenses), "prior_balance_due", "client_type";
// their reconciliation block adds "header_arithmetic" (fees + adjustments + expenses + prior balance == total_due)
"timekeepers": [ {"name", "title", "rate"} ],               // synthetic: the Timekeeper-summary table as billed
                                                              // real (A, C): {"alias":"TK01","title","rates":[..],"hours","total"}; names/initials as billed are local only
"lines": [ {
  "n": 1,                                                     // 1-based line reference used by findings
  "line_type": "fee" | "expense" | "adjustment",
  "date", "timekeeper", "title",                               // as billed; expense lines: timekeeper null
  "hours", "rate", "amount",                                   // expense lines: hours = quantity, rate = unit cost
  "narrative",                                                 // omitted from COMMITTED real-document labels (see 1.3)
  "codes": ["L110","A102"],                                    // codes as billed; [] when absent
  "page": 1                                                    // page of the rendered/filed document (1-based)
} ],
"intended_codes": { "<n>": ["L110","A102"] },                 // synthetic only: the code the narrative was GENERATED for (independent answer key; may differ from the billed code on seeded miscoding)
"document": { "pages", "scan_noise", "bundled", "bundle_parts": [ {"part","pages"} ], "template" },
```

Set F additionally carries:

```
"rule_set_id": "RS-03",                                       // ground-truth/rule-sets/RS-03.json
"context": { "matter_open", "matter_close", "matter_budget", "approved_timekeepers": [..], "invoice_received_date" },
"expected_findings": [ { "rule_id": "RS-03-R04", "rule_type": "TimeIncrement",
                         "line_refs": [3, 7],                  // [] with "scope":"invoice" for invoice-level findings
                         "scope": "line" | "invoice",
                         "consequence": "Reject" | "Reduce" | "Disallow" | "Flag" } ]    // [] for clean invoices
"seeded_violations": <count>                                   // == len(expected_findings); 0 for clean
```

A `Duplicate` finding also carries `"related_line_refs": [n]` (the earlier line the flagged line repeats); `line_refs` holds the repeat only, so matching on `rule_id` and `line_refs` is unaffected.

```
```

Rule types are the eleven FR-24 types: `ApprovedTimekeeper, ApprovedRate, TimeIncrement, DailyHoursCap, NonBillableActivity, ExpenseRule, InvoiceAge, CoreRecordWindow, Duplicate, RequiredData, AiObservation`. A finding matches if `rule_id` and every `line_refs` entry match (order irrelevant). Scoring detail is for 162; the format is the contract.

### 1.2 `ocg`, `rate-card`, `rule-set` (set E)

```
"rules": [ { "rule_id": "<id>-R01", "rule_type": "<one of the 11>", "params": {..}, "consequence": "Reject"|"Reduce"|"Flag"|"Disallow"|null,
             "clause_ref": {"section": "VI.B", "page": 4}, "paraphrase": "...", "expressible": true } ],
"unmapped_provisions": [ { "clause_ref": {..}, "paraphrase": "...", "reason": "judgement|not-billing|needs-new-type" } ]
```

`params` vocabulary (evaluation-time; the production schemas are task 070's): `ApprovedTimekeeper {approved_names|approved_titles|disallowed_titles|max_attorneys_per_event}`, `ApprovedRate {rates:{title:amount}|per_timekeeper:{name:amount}|max_annual_increase_pct}`, `TimeIncrement {increment_hours}`, `DailyHoursCap {max_hours_per_timekeeper_per_day}`, `NonBillableActivity {keywords|activity_codes|task_codes|travel_rate_pct}`, `ExpenseRule {non_reimbursable|caps:{item:amount}|preapproval_over|no_markup}`, `InvoiceAge {max_days_after_period_end|max_days_work_age}`, `CoreRecordWindow {require_within_matter_dates|budget_cap}`, `Duplicate {match_on:[..]}`, `RequiredData {fields|codes_required}`, `AiObservation {observations:["block_billing","vague_description",...]}`. `rate-card` bodies add `"rates": [ {"key","amount","currency","unit":"hour","effective_from","effective_to","scope"} ]`. A `rule-set` (sets F) is a hand-composed rule list modelled on named OCGs (`"modelled_on": ["E-OCG-03", ...]`).

### 1.3 Real documents (sets A, C, E) — what is committed

Real filings, real invoices, OCGs and rate-card documents are **never committed**. They live under `local/` (git-ignored by `local/.gitignore`). The committed label for a real document carries header, timekeepers (aliased `TK01`...), line amounts/dates/codes and, instead of narrative text, `narrative_sha256` and `narrative_len`; the full label (narratives, names and initials as billed) is written to `local/ground-truth-full/<id>.json` by `generator/label_fee_applications.py` (set A) and `generator/label_real_invoices.py` (set C), which re-derive it from the local PDFs. Set E labels are AI-drafted paraphrases (`ai-drafted-pending-review`) written by `generator/build_ground_truth_e.py` from the `ocg_rules_*.py` and `ratecards.py` data. Committed OCG/rate-card rule files carry paraphrases and clause references only, no quotation beyond a few words.

### 1.4 Licence rule applied

No item with a non-commercial or research-only licence is included (ConfBench CC BY-NC 4.0, DocILE research-only access and Onit proprietary samples were excluded up front). An item with no stated licence is included only as a **public record used for internal evaluation**; such rows say `public-record-no-licence`, are stored local-only and are not redistributed. UTBMS and LEDES standards are freely usable (decisions-log D-32).

## 2. Sets and targets (spec FR-02)

| Set | What | FR-02 target | Ground truth |
|---|---|---|---|
| A | Chapter 11 fee applications / monthly fee statements | >= 6 | header + sampled lines, parser + reconciliation |
| B | Synthetic invoices (generator) | >= 150, >= 20 templates, 30% scan noise, bundled PDFs, one > 20 pages | generator output |
| C | Real invoices | >= 10 | extracted + reviewed |
| E | OCGs | >= 15 | expected-rule list per document |
| E | Rate cards | >= 5 | structured rates |
| F | Seeded-violation invoices | >= 300 | generator findings |
| F | Clean invoices | >= 300 | generator (zero findings) |
