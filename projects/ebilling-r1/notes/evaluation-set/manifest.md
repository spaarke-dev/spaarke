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

## 3. Counts against the targets (computed)

| What | Set | Target | Actual | Result |
|---|---|---|---|---|
| Fee applications | A | 6 | 8 | pass |
| Synthetic invoices | B | 150 | 150 | pass |
| Real invoices | C | 10 | 12 | pass |
| OCGs | E | 15 | 16 | pass |
| Rate cards | E | 5 | 5 | pass |
| Seeded-violation invoices | F | 300 | 300 | pass |
| Clean invoices | F | 300 | 300 | pass |

Set B: 150 invoices, 24 templates, 48 scan-noise PDFs (32 percent), 14 bundled PDFs, largest B-0150 at 26 pages, 20 non-USD, 3762 lines. Set F: 300 seeded invoices with 875 expected findings across all 11 rule types, 300 clean invoices, 6 rule sets, 8707 lines.

Spike subset for task 005 (42 items): A-02, A-07, B-0001, B-0002, B-0003, B-0004, B-0005, B-0006, B-0007, B-0008, B-0009, B-0010, B-0011, B-0012, B-0013, B-0014, B-0015, B-0016, B-0017, B-0018, B-0019, B-0020, B-0021, B-0022, B-0023, B-0024, B-0026, B-0031, B-0043, B-0055, B-0067, B-0079, B-0091, B-0103, B-0115, B-0127, B-0139, B-0149, B-0150, C-03, C-10, C-11. It holds 37 synthetic items (including B-0150, the 26-page invoice, and 14 bundled PDFs) and 5 real documents.

## 4. Real and authored items (one row per item)

### 4.1 Set A: fee applications

| Id | What | Source URL or generator id | Licence | PII note | Storage | Ground-truth file | Label |
|---|---|---|---|---|---|---|---|
| A-01 | K&L Gates LLP first interim fee application, In re Akoustis Technologies (Bankr. D. Del. 24-12796, Doc 518) | https://cases.stretto.com/public/x368/13098/PLEADINGS/1309804302580000000256.pdf | public-record-no-licence: No licence stated. Court record filed on PACER and hosted by the claims agent; used only as a public record for internal evaluation, not redistributed. | Court record. Counts only: email 19, phone_us 5, street_address 12, case_party_marker 4. Committed label has narrative hashes and timekeeper aliases only; full label is local. (scan 2026-10-15) | local-only local/fee-applications/klgates-akoustis.pdf | ground-truth/fee-applications/A-01.json | parser+reconciliation; summary-level; reconciliation exact |
| A-02 | Foley Hoag LLP second monthly application, In re Pear Therapeutics (Bankr. D. Del. 23-10429, Doc 314) | https://cases.stretto.com/public/X237/12136/PLEADINGS/1213607142380000000188.pdf | public-record-no-licence: No licence stated. Court record filed on PACER and hosted by the claims agent; used only as a public record for internal evaluation, not redistributed. | Court record. Counts only: email 16, phone_us 8, card_number 5, street_address 11, case_party_marker 16. Committed label has narrative hashes and timekeeper aliases only; full label is local. (scan 2026-10-15) | local-only local/fee-applications/foleyhoag-pear.pdf | ground-truth/fee-applications/A-02.json | parser+reconciliation; line-level; reconciliation exact |
| A-03 | Polsinelli PC twelfth monthly fee statement (Bankr. N.D. Ill. 23-07541, Doc 782) | https://cases.stretto.com/public/x243/12204/PLEADINGS/1220407182480000000283.pdf | public-record-no-licence: No licence stated. Court record filed on PACER and hosted by the claims agent; used only as a public record for internal evaluation, not redistributed. | Court record. Counts only: email 11, phone_us 7, street_address 2, case_party_marker 1. Committed label has narrative hashes and timekeeper aliases only; full label is local. (scan 2026-10-15) | local-only local/fee-applications/polsinelli-n23-07541.pdf | ground-truth/fee-applications/A-03.json | parser+reconciliation; line-level; reconciliation exact |
| A-04 | Covington & Burling LLP second interim and final application, Montreal Maine & Atlantic Railway (Bankr. D. Me. 13-10670, Doc 1931) | https://www.richter.ca/wp-content/uploads/Insolvency-Cases/en/M/Montreal-Maine-and-Atlantic-Canada-Co/Chapter-11---Montreal-Maine-and-Atlantic-Railway-Ltd/Motions/429-DE1931-Covington-final-fee-appl-20160120.pdf | public-record-no-licence: No licence stated. Court record filed on PACER and hosted by the claims agent; used only as a public record for internal evaluation, not redistributed. | Court record. Counts only: email 3, phone_us 5, street_address 5. Committed label has narrative hashes and timekeeper aliases only; full label is local. (scan 2026-10-15) | local-only local/fee-applications/covington-mmar.pdf | ground-truth/fee-applications/A-04.json | parser+reconciliation; line-level-partial; reconciliation partial |
| A-05 | WH Burkley LLP monthly fee statement, In re Crown Boiler Co. (Bankr. W.D. Pa. 26-20515, Doc 462) | https://casedocs.omniagentsolutions.com/cmsvol2/pub_47595/c5876153-e1c2-42b0-bb1a-4f28ae263c0d_462.pdf | public-record-no-licence: No licence stated. Court record filed on PACER and hosted by the claims agent; used only as a public record for internal evaluation, not redistributed. | Court record. Counts only: email 6, phone_us 9, card_number 3, street_address 6. Committed label has narrative hashes and timekeeper aliases only; full label is local. (scan 2026-10-15) | local-only local/fee-applications/whburkley-crownboiler.pdf | ground-truth/fee-applications/A-05.json | parser+reconciliation; line-level; reconciliation exact |
| A-06 | Randi S. Ellis second monthly fee statement, In re Red River Talc (Bankr. S.D. Tex. 24-90505, Doc 1042) | https://document.epiq11.com/document/getdocumentsbydocket/?docketId=1140755&projectCode=RRI&docketNumber=1042 | public-record-no-licence: No licence stated. Court record filed on PACER and hosted by the claims agent; used only as a public record for internal evaluation, not redistributed. | Court record. Counts only: email 21, phone_us 2, street_address 7. Committed label has narrative hashes and timekeeper aliases only; full label is local. (scan 2026-10-15) | local-only local/fee-applications/ellis-redriver.pdf | ground-truth/fee-applications/A-06.json | parser+reconciliation; line-level; reconciliation exact |
| A-07 | Berger Singerman LLP third and final application, In re Consolidated Burger Holdings (Bankr. N.D. Fla. 25-40162, Doc 530) | https://casedocs.omniagentsolutions.com/cmsvol2/pub_47550/f21a1370-4747-430a-85f2-961b34c67e86_530.pdf | public-record-no-licence: No licence stated. Court record filed on PACER and hosted by the claims agent; used only as a public record for internal evaluation, not redistributed. | Court record. Counts only: email 6, phone_us 98, street_address 3. Committed label has narrative hashes and timekeeper aliases only; full label is local. (scan 2026-10-15) | local-only local/fee-applications/consolidatedburger-final.pdf | ground-truth/fee-applications/A-07.json | parser+reconciliation; line-level; reconciliation exact |
| A-08 | WongPartnership LLP final fee application, In re Zetta Jet USA (Bankr. C.D. Cal. 2:17-bk-21386) | https://casedocs.omniagentsolutions.com/cmsvol2/pub_47213/be10e655-5202-4772-9cd4-5045e4a8308b_2854.pdf | public-record-no-licence: No licence stated. Court record filed on PACER and hosted by the claims agent; used only as a public record for internal evaluation, not redistributed. | Court record. Counts only: email 180, street_address 6, case_party_marker 3. Committed label has narrative hashes and timekeeper aliases only; full label is local. (scan 2026-10-15) | local-only local/fee-applications/zettajet-final.pdf | ground-truth/fee-applications/A-08.json | parser+reconciliation; summary-level; reconciliation exact |

### 4.2 Set C: real invoices

| Id | What | Source URL or generator id | Licence | PII note | Storage | Ground-truth file | Label |
|---|---|---|---|---|---|---|---|
| C-01 | Goss Samford, PLLC invoice 4115 (electric cooperative, KY PSC 2019-00053) | https://psc.ky.gov/pscecf/2019-00053/kristen@gosssamfordlaw.com/06072019112328/Combined_Response.pdf | public-record-no-licence: No licence stated. Public record: filed by the utility with the Kentucky Public Service Commission in a rate-case docket; used only as a public record for internal evaluation, not redistributed. | Public regulatory filing. Counts only: phone_us 1, street_address 1. The source URL carries the filer's business e-mail address as part of the public PSC path. Committed label has timekeeper aliases and narrative hashes only; names and narratives are in the local full label. (scan 2026-10-15) | local-only local/real-invoices/C-01.pdf | ground-truth/real-invoices/C-01.json | extracted+reviewed; line-level; reconciliation exact |
| C-02 | Goss Samford, PLLC invoice 4141 (electric cooperative, KY PSC 2019-00053) | https://psc.ky.gov/pscecf/2019-00053/kristen@gosssamfordlaw.com/06072019112328/Combined_Response.pdf | public-record-no-licence: No licence stated. Public record: filed by the utility with the Kentucky Public Service Commission in a rate-case docket; used only as a public record for internal evaluation, not redistributed. | Public regulatory filing. Counts only: phone_us 1, street_address 1. The source URL carries the filer's business e-mail address as part of the public PSC path. Committed label has timekeeper aliases and narrative hashes only; names and narratives are in the local full label. (scan 2026-10-15) | local-only local/real-invoices/C-02.pdf | ground-truth/real-invoices/C-02.json | extracted+reviewed; line-level; reconciliation exact |
| C-03 | Goss Samford, PLLC invoice 4240 (electric cooperative, KY PSC 2019-00053) | https://psc.ky.gov/pscecf/2019-00053/kristen@gosssamfordlaw.com/06072019112328/Combined_Response.pdf | public-record-no-licence: No licence stated. Public record: filed by the utility with the Kentucky Public Service Commission in a rate-case docket; used only as a public record for internal evaluation, not redistributed. | Public regulatory filing. Counts only: phone_us 1, street_address 1, case_party_marker 1. The source URL carries the filer's business e-mail address as part of the public PSC path. Committed label has timekeeper aliases and narrative hashes only; names and narratives are in the local full label. (scan 2026-10-15) | local-only local/real-invoices/C-03.pdf | ground-truth/real-invoices/C-03.json | extracted+reviewed; line-level; reconciliation exact |
| C-04 | Goss Samford, PLLC invoice 4246 (electric cooperative, KY PSC 2019-00053) | https://psc.ky.gov/pscecf/2019-00053/kristen@gosssamfordlaw.com/06072019112328/Combined_Response.pdf | public-record-no-licence: No licence stated. Public record: filed by the utility with the Kentucky Public Service Commission in a rate-case docket; used only as a public record for internal evaluation, not redistributed. | Public regulatory filing. Counts only: phone_us 1, street_address 1. The source URL carries the filer's business e-mail address as part of the public PSC path. Committed label has timekeeper aliases and narrative hashes only; names and narratives are in the local full label. (scan 2026-10-15) | local-only local/real-invoices/C-04.pdf | ground-truth/real-invoices/C-04.json | extracted+reviewed; line-level; reconciliation exact |
| C-05 | Goss Samford, PLLC invoice 4294 (electric cooperative, KY PSC 2019-00053) | https://psc.ky.gov/pscecf/2019-00053/kristen@gosssamfordlaw.com/06072019112328/Combined_Response.pdf | public-record-no-licence: No licence stated. Public record: filed by the utility with the Kentucky Public Service Commission in a rate-case docket; used only as a public record for internal evaluation, not redistributed. | Public regulatory filing. Counts only: phone_us 1, street_address 1. The source URL carries the filer's business e-mail address as part of the public PSC path. Committed label has timekeeper aliases and narrative hashes only; names and narratives are in the local full label. (scan 2026-10-15) | local-only local/real-invoices/C-05.pdf | ground-truth/real-invoices/C-05.json | extracted+reviewed; line-level; reconciliation exact |
| C-06 | Stoll Keenon Ogden PLLC invoice 881085 (water utility, KY PSC 2018-00358) | https://psc.ky.gov/pscecf/2018-00358/monica.braun@skofirm.com/06142019042023/KAW_SR_LFUCGPHDR_NUM005_061419.pdf | public-record-no-licence: No licence stated. Public record: filed by the utility with the Kentucky Public Service Commission in a rate-case docket; used only as a public record for internal evaluation, not redistributed. | Public regulatory filing. Counts only: email 2, phone_us 2, street_address 2. The source URL carries the filer's business e-mail address as part of the public PSC path. Committed label has timekeeper aliases and narrative hashes only; names and narratives are in the local full label. (scan 2026-10-15) | local-only local/real-invoices/C-06.pdf | ground-truth/real-invoices/C-06.json | extracted+reviewed; invoice-level; reconciliation exact |
| C-07 | Stoll Keenon Ogden PLLC invoice 883230 (water utility, KY PSC 2018-00358) | https://psc.ky.gov/pscecf/2018-00358/monica.braun@skofirm.com/06142019042023/KAW_SR_LFUCGPHDR_NUM005_061419.pdf | public-record-no-licence: No licence stated. Public record: filed by the utility with the Kentucky Public Service Commission in a rate-case docket; used only as a public record for internal evaluation, not redistributed. | Public regulatory filing. Counts only: email 2, phone_us 2, street_address 2. The source URL carries the filer's business e-mail address as part of the public PSC path. Committed label has timekeeper aliases and narrative hashes only; names and narratives are in the local full label. (scan 2026-10-15) | local-only local/real-invoices/C-07.pdf | ground-truth/real-invoices/C-07.json | extracted+reviewed; invoice-level; reconciliation exact |
| C-08 | Stoll Keenon Ogden PLLC invoice 887263 (water utility, KY PSC 2018-00358) | https://psc.ky.gov/pscecf/2018-00358/monica.braun@skofirm.com/06142019042023/KAW_SR_LFUCGPHDR_NUM005_061419.pdf | public-record-no-licence: No licence stated. Public record: filed by the utility with the Kentucky Public Service Commission in a rate-case docket; used only as a public record for internal evaluation, not redistributed. | Public regulatory filing. Counts only: email 2, phone_us 2, street_address 2. The source URL carries the filer's business e-mail address as part of the public PSC path. Committed label has timekeeper aliases and narrative hashes only; names and narratives are in the local full label. (scan 2026-10-15) | local-only local/real-invoices/C-08.pdf | ground-truth/real-invoices/C-08.json | extracted+reviewed; invoice-level; reconciliation exact |
| C-09 | Stoll Keenon Ogden PLLC invoice 888722 (water utility, KY PSC 2018-00358) | https://psc.ky.gov/pscecf/2018-00358/monica.braun@skofirm.com/06142019042023/KAW_SR_LFUCGPHDR_NUM005_061419.pdf | public-record-no-licence: No licence stated. Public record: filed by the utility with the Kentucky Public Service Commission in a rate-case docket; used only as a public record for internal evaluation, not redistributed. | Public regulatory filing. Counts only: email 2, phone_us 2, street_address 2. The source URL carries the filer's business e-mail address as part of the public PSC path. Committed label has timekeeper aliases and narrative hashes only; names and narratives are in the local full label. (scan 2026-10-15) | local-only local/real-invoices/C-09.pdf | ground-truth/real-invoices/C-09.json | extracted+reviewed; invoice-level; reconciliation exact |
| C-10 | Stoll Keenon Ogden PLLC invoice 890701 (water utility, KY PSC 2018-00358) | https://psc.ky.gov/pscecf/2018-00358/monica.braun@skofirm.com/06142019042023/KAW_SR_LFUCGPHDR_NUM005_061419.pdf | public-record-no-licence: No licence stated. Public record: filed by the utility with the Kentucky Public Service Commission in a rate-case docket; used only as a public record for internal evaluation, not redistributed. | Public regulatory filing. Counts only: email 2, phone_us 2, street_address 2. The source URL carries the filer's business e-mail address as part of the public PSC path. Committed label has timekeeper aliases and narrative hashes only; names and narratives are in the local full label. (scan 2026-10-15) | local-only local/real-invoices/C-10.pdf | ground-truth/real-invoices/C-10.json | extracted+reviewed; invoice-level; reconciliation exact |
| C-11 | Dinsmore & Shohl LLP invoice 6011094 (gas utility, KY PSC 2025-00332) | https://psc.ky.gov/pscecf/2025-00332/evan.buckley%40dinsmore.com/06012026085737/2025-00332_DR2-51_Part_2_supplement.pdf | public-record-no-licence: No licence stated. Public record: filed by the utility with the Kentucky Public Service Commission in a rate-case docket; used only as a public record for internal evaluation, not redistributed. | Public regulatory filing. Counts only: email 3, phone_us 3, street_address 1. The source URL carries the filer's business e-mail address as part of the public PSC path. Committed label has timekeeper aliases and narrative hashes only; names and narratives are in the local full label. (scan 2026-10-15) | local-only local/real-invoices/C-11.pdf | ground-truth/real-invoices/C-11.json | extracted+reviewed; line-level; reconciliation exact |
| C-12 | Dinsmore & Shohl LLP invoice 6019250 (gas utility, KY PSC 2025-00332) | https://psc.ky.gov/pscecf/2025-00332/evan.buckley%40dinsmore.com/06012026085737/2025-00332_DR2-51_Part_2_supplement.pdf | public-record-no-licence: No licence stated. Public record: filed by the utility with the Kentucky Public Service Commission in a rate-case docket; used only as a public record for internal evaluation, not redistributed. | Public regulatory filing. Counts only: email 3, phone_us 3, street_address 1. The source URL carries the filer's business e-mail address as part of the public PSC path. Committed label has timekeeper aliases and narrative hashes only; names and narratives are in the local full label. (scan 2026-10-15) | local-only local/real-invoices/C-12.pdf | ground-truth/real-invoices/C-12.json | extracted+reviewed; line-level; reconciliation exact |

### 4.3 Set E: outside counsel guidelines

| Id | What | Source URL or generator id | Licence | PII note | Storage | Ground-truth file | Label |
|---|---|---|---|---|---|---|---|
| E-OCG-01 | County of Berks (PA) Outside Counsel Guidelines (2016) | https://berkspa.gov/getmedia/5f301be1-b62d-43fe-b439-6a3c869d321b/Outside_Counsel_Guidelines.pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. No pattern hits. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/berks.pdf | ground-truth/ocg/E-OCG-01.json | ai-drafted-pending-review |
| E-OCG-02 | Delaware Department of Justice Outside Counsel Billing Policy (bid JUD-DOJ25001) | https://bidcondocs.delaware.gov/JUD/JUD-DOJ25001-OUTSIDE_COUNSEL-billing.pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. No pattern hits. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/delaware.pdf | ground-truth/ocg/E-OCG-02.json | ai-drafted-pending-review |
| E-OCG-03 | Maricopa County Office of Public Defense Services Billing Guidelines | https://listos.maricopa.gov/DocumentCenter/View/58252/OPDS-Billing-Guidelines | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. Hits: case_party_marker 1. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/maricopa-opds.pdf | ground-truth/ocg/E-OCG-03.json | ai-drafted-pending-review |
| E-OCG-04 | Mississippi DFA Billing Guidelines for Defense Attorneys (amended 2022-10-17) | https://www.dfa.ms.gov/sites/default/files/Tort%20Home/Contact%20-%20Services%20and%20Resources%20-%20Claims/Billing%20Guidelines%20for%20Defense%20Attorneys%20-%20amended%2010172022.pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. No pattern hits. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/ms-dfa.pdf | ground-truth/ocg/E-OCG-04.json | ai-drafted-pending-review |
| E-OCG-05 | New Mexico State University Outside Counsel Guidelines (Exhibit B) | https://legal.nmsu.edu/wp-content/uploads/sites/28/2021/11/NMSU-Outside-Counsel-Guidelines.pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. Hits: email 1, case_party_marker 1. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/nmsu.pdf | ground-truth/ocg/E-OCG-05.json | ai-drafted-pending-review |
| E-OCG-06 | Nevada DIDS Attorney Fee Billing Guidelines (2025-03-04) | https://dids.nv.gov/uploadedFiles/didsnvgov/content/Resources/Forms/DIDS%20Billing%20Guidelines%20-%20Attorney%20Fees%2003042025.pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. Hits: phone_us 1, street_address 1. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/nv-dids.pdf | ground-truth/ocg/E-OCG-06.json | ai-drafted-pending-review |
| E-OCG-07 | Santa Barbara Superior Court appointed-counsel billing guidelines (FC-3150, 2026-04-22) | https://www.santabarbara.courts.ca.gov/system/files/general/fc-3150-mc-billing-guidelines-042226.pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. Hits: email 1, case_party_marker 1. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/santabarbara-courts.pdf | ground-truth/ocg/E-OCG-07.json | ai-drafted-pending-review |
| E-OCG-08 | Solano Superior Court appointed-counsel billing guidelines (rev 2025-11-18) | https://solano.courts.ca.gov/system/files/general/billing-guidelines-cac-fc3150-rev11-18-25.pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. Hits: email 1, street_address 2, case_party_marker 2. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/solano-courts.pdf | ground-truth/ocg/E-OCG-08.json | ai-drafted-pending-review |
| E-OCG-09 | Texas Attorney General rules on outside counsel contracts, 1 TAC ch. 57 (via UT System) | https://www.utsystem.edu/sites/default/files/offices/general-counsel/OutsideCounsel/OAGRulesreOutsideCounsel.pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. Hits: case_party_marker 2. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/texas-oag.pdf | ground-truth/ocg/E-OCG-09.json | ai-drafted-pending-review |
| E-OCG-10 | University of California standard core legal Outside Counsel Guidelines | https://ucop.edu/uc-legal/_files/standard-core-legal-outside-counsel-guidelines.pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. Hits: case_party_marker 1. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/ucop.pdf | ground-truth/ocg/E-OCG-10.json | ai-drafted-pending-review |
| E-OCG-11 | Unified Government of Wyandotte County / Kansas City KS Outside Counsel Guide (bid N33053 Exhibit B) | https://purchasing.wycokck.org/eprocurement/bids/N33053/Exhibit%20B%20-%20Outside%20counsel%20guide.pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. No pattern hits. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/wyandotte.pdf | ground-truth/ocg/E-OCG-11.json | ai-drafted-pending-review |
| E-OCG-12 | Louisiana Office of Risk Management attorney billing guidelines | https://www.doa.la.gov/media/bu1jwq0w/attorney-billing-guidelines.pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. Hits: email 7, case_party_marker 6. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/la-orm-attorney-billing.pdf | ground-truth/ocg/E-OCG-12.json | ai-drafted-pending-review |
| E-OCG-13 | Ohio Attorney General Special Counsel Retention Agreement FY20-FY21 | https://ohioattorneygeneral.gov/Files/Legal/Outside-Counsel/General-Appointments/FY20-FY21-Special-Counsel-Retention-Agreement.aspx | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. Hits: email 1, case_party_marker 5. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/ohio-ag-retention.pdf | ground-truth/ocg/E-OCG-13.json | ai-drafted-pending-review |
| E-OCG-14 | State of New Jersey Division of Law Outside Counsel Guidelines | https://www.nj.gov/oag/law/pdf/rfqs/oag-dol-Outside-Counsel-Guidelines.pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. Hits: email 1, phone_us 1, street_address 1, case_party_marker 13. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/nj-oag.pdf | ground-truth/ocg/E-OCG-14.json | ai-drafted-pending-review |
| E-OCG-15 | Forest Preserve District of Cook County Billing Guidelines for Outside Counsel (2023-11-16) | https://fpdcc.com/downloads/policies/FPCC-Outside-Counsel-Guidelines-111623.pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. Hits: case_party_marker 1. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/cook-forest-preserve.pdf | ground-truth/ocg/E-OCG-15.json | ai-drafted-pending-review |
| E-OCG-16 | Georgia Institute of Technology Outside Counsel Guidelines (Feb 2023) | https://generalcounsel.gatech.edu/sites/default/files/2024-01/GT%20OGC%20Outside%20Counsel%20Guidelines%20(Final%20February%202023).pdf | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. Hits: email 1, case_party_marker 3. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/ocg/gatech.pdf | ground-truth/ocg/E-OCG-16.json | ai-drafted-pending-review |

### 4.4 Set E: rate cards

| Id | What | Source URL or generator id | Licence | PII note | Storage | Ground-truth file | Label |
|---|---|---|---|---|---|---|---|
| E-RC-01 | Attorney General's civil panel counsel hourly rates (UK, from 2025-04-01) | https://www.gov.uk/guidance/attorney-generals-panel-counsel-practical-information | open: Open Government Licence v3.0 | Public document from an agency or court; counts only, no matched text recorded. Hits: email 31. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/rate-cards/uk-ag-panel.html | ground-truth/rate-cards/E-RC-01.json | ai-drafted-pending-review |
| E-RC-02 | Louisiana Maximum Hourly Fee Schedule for professional legal services (memo 2024-02-22) | https://doa.louisiana.gov/media/kzvd34i0/memo-legal-services-contract-changes-effective-02-22-2024-ada.docx | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. No pattern hits. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/rate-cards/la-memo-legal-services-2024.docx | ground-truth/rate-cards/E-RC-02.json | ai-drafted-pending-review |
| E-RC-03 | Criminal Justice Act panel attorney rate chart as of 2026-01-01 (S.D. Ohio) | https://www.ohsd.uscourts.gov/sites/ohsd/files/_2026_CJA%20Rate%20Chart.pdf | open: No licence stated. US federal judiciary publication (US government work, 17 USC 105: no copyright). | Public document from an agency or court; counts only, no matched text recorded. Hits: case_party_marker 1. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/rate-cards/cja-2026-ohsd.pdf | ground-truth/rate-cards/E-RC-03.json | ai-drafted-pending-review |
| E-RC-04 | Florida Administrative Code 2-37.030 standard fee schedule for outside attorneys | https://flrules.org/gateway/notice_Files.asp?ID=5583337 | public-record-no-licence: No licence stated. Government/public-institution publication posted on the open web; used only as a public document for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. No pattern hits. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/rate-cards/fl-rule-2-37-030.doc | ground-truth/rate-cards/E-RC-04.json | ai-drafted-pending-review |
| E-RC-05 | Young Conaway timekeeper rate schedule (UST Appendix B), In re Lugano Diamonds & Jewelry, Doc 681 | https://casedocs.omniagentsolutions.com/cmsvol2/pub_47571/6868038c-8c01-43d4-8650-ef1785e91dd6_681.pdf | public-record-no-licence: No licence stated. Court record filed on PACER and hosted by the claims agent; used only as a public record for internal evaluation, not redistributed. | Public document from an agency or court; counts only, no matched text recorded. Hits: email 7, phone_us 4, street_address 2, case_party_marker 2. Hits are agency contact details, addresses and generic role words, not private individuals. (scan 2026-10-15) | local-only local/rate-cards/youngconaway-lugano-exhibit-b.pdf | ground-truth/rate-cards/E-RC-05.json | ai-drafted-pending-review |

### 4.5 Rule sets (set F context)

| Id | What | Source URL or generator id | Licence | PII note | Storage | Ground-truth file | Label |
|---|---|---|---|---|---|---|---|
| RS-01 | County-style guideline | gen_eval_set.py@1 | synthetic: Synthetic - authored for this evaluation set | No personal data. | committed ground-truth/rule-sets/RS-01.json | ground-truth/rule-sets/RS-01.json | generator |
| RS-02 | University-style guideline | gen_eval_set.py@1 | synthetic: Synthetic - authored for this evaluation set | No personal data. | committed ground-truth/rule-sets/RS-02.json | ground-truth/rule-sets/RS-02.json | generator |
| RS-03 | State-agency style guideline | gen_eval_set.py@1 | synthetic: Synthetic - authored for this evaluation set | No personal data. | committed ground-truth/rule-sets/RS-03.json | ground-truth/rule-sets/RS-03.json | generator |
| RS-04 | Quarter-hour panel guideline | gen_eval_set.py@1 | synthetic: Synthetic - authored for this evaluation set | No personal data. | committed ground-truth/rule-sets/RS-04.json | ground-truth/rule-sets/RS-04.json | generator |
| RS-05 | Insurer-style guideline | gen_eval_set.py@1 | synthetic: Synthetic - authored for this evaluation set | No personal data. | committed ground-truth/rule-sets/RS-05.json | ground-truth/rule-sets/RS-05.json | generator |
| RS-06 | Public-defender style guideline | gen_eval_set.py@1 | synthetic: Synthetic - authored for this evaluation set | No personal data. | committed ground-truth/rule-sets/RS-06.json | ground-truth/rule-sets/RS-06.json | generator |

## 5. Synthetic items (per-item rows are in manifest.csv)

| Template | Count | First id | Last id |
|---|---|---|---|
| T01 | 7 | B-0001 | B-0145 |
| T02 | 7 | B-0002 | B-0146 |
| T03 | 7 | B-0003 | B-0147 |
| T04 | 7 | B-0004 | B-0148 |
| T05 | 7 | B-0005 | B-0149 |
| T06 | 7 | B-0006 | B-0150 |
| T07 | 6 | B-0007 | B-0127 |
| T08 | 6 | B-0008 | B-0128 |
| T09 | 6 | B-0009 | B-0129 |
| T10 | 6 | B-0010 | B-0130 |
| T11 | 6 | B-0011 | B-0131 |
| T12 | 6 | B-0012 | B-0132 |
| T13 | 6 | B-0013 | B-0133 |
| T14 | 6 | B-0014 | B-0134 |
| T15 | 6 | B-0015 | B-0135 |
| T16 | 6 | B-0016 | B-0136 |
| T17 | 6 | B-0017 | B-0137 |
| T18 | 6 | B-0018 | B-0138 |
| T19 | 6 | B-0019 | B-0139 |
| T20 | 6 | B-0020 | B-0140 |
| T21 | 6 | B-0021 | B-0141 |
| T22 | 6 | B-0022 | B-0142 |
| T23 | 6 | B-0023 | B-0143 |
| T24 | 6 | B-0024 | B-0144 |

Every B and F item has: generator id `gen_eval_set.py@1 <id>` (base seed 20261015, rendering is a pure function of the committed ground truth), licence `Synthetic (project generator, no third-party rights)`, PII note `Synthetic; no personal data`, storage `regenerate` at `local/synthetic/<id>.pdf` (render with `gen_eval_set.py render`; check with `gen_eval_set.py check`) and its ground-truth file under `ground-truth/synthetic-invoices/` (B) or `ground-truth/compliance-invoices/` (F).

## 6. Size

| Folder | Size |
|---|---|
| compliance-invoices | 3,937,215 bytes |
| fee-applications | 187,451 bytes |
| ocg | 91,555 bytes |
| rate-cards | 20,697 bytes |
| real-invoices | 83,967 bytes |
| rule-sets | 11,394 bytes |
| summary.json | 931 bytes |
| synthetic-invoices | 1,590,712 bytes |
| generator/ (scripts, registries, notes) | 288,581 bytes |
| **committed ground truth total** | 5,923,922 bytes (5.65 MB) |

Local-only material (not committed, git-ignored): 164 MB under `local/` (real PDFs, downloaded OCG and rate-card documents, rendered synthetic PDFs, full labels). Committed size excludes it.

## 7. Dated notes, deviations and known limits

Entries are dated; add new ones at the end. Dates are 2026-10-15 unless shown.

### 7.1 Label methods and tiers

- **Set A (8 fee applications).** Seven reconcile exactly against the totals the filing states (A-01, A-02, A-03, A-05, A-06, A-07 and A-08; the summary-level A-01 and A-08 reconcile at timekeeper-summary level only). A-04 is `partial`: the filing is a scan with an OCR layer and one 0.5-hour entry prints as 0.00. K&L Gates (A-01) and WongPartnership (A-08) are summary-level because the entry detail is not in the filing text. Fee-line amounts in A-05 and A-07 are derived (hours x the timekeeper's rate) because those filings print hours only; they sum to the stated fees. Each label records a `reconciliation` block so a reader can see how far the parse is trusted.
- **Set C (12 real invoices).** `extracted+reviewed`. Header, totals and timekeeper summaries were read from each invoice's own summary pages and then checked by arithmetic closure (timekeeper amounts equal fees; fees + adjustments + expenses + unpaid prior balance equal the amount due), and the summary page images for C-03, C-04 and C-05 were read by eye. Closure caught two OCR defects (a dropped 2,000.00 discount line on C-03's summary page; missing Current Fees and Billable Hours figures on C-04), fixed from the page image. Line entries: C-01 to C-05 (Goss Samford, scans with OCR, reconciled exactly), C-11 and C-12 (Dinsmore & Shohl, native text, reconciled to total hours; no rates or line amounts are printed). C-06 to C-10 (Stoll Keenon Ogden) are invoice-level only because the column order in their text layer is interleaved.
- **Set E (16 OCGs, 5 rate cards).** `ai-drafted-pending-review`: the expected-rule lists and rate tables are paraphrases drafted from the local documents and **have not been signed off by a legal-operations reviewer**. Treat expected-rule recall figures from them as provisional until that review happens. 166 rules map to the eleven FR-24 types and 36 provisions are recorded as `unmapped_provisions` (judgement, not billing, or needing a new type).
- **Sets B and F.** `generator`: ground truth is generator output; rendering is a pure function of the committed ground truth. `intended_codes` is an independent answer key for B.

### 7.2 Real-invoice sourcing (set C) and its limits

- All twelve are law-firm invoices that utilities filed with the Kentucky Public Service Commission in rate-case dockets (three filings, three firms, 2018-2026). They are public records with no stated licence, included for internal evaluation only, stored local-only and not redistributed. The source URLs carry the filers' business e-mail addresses as part of the PSC path.
- Skew to be aware of: one jurisdiction, regulated-utility rate-case work, three firm layouts. The set exercises scan-with-OCR, native-text and interleaved-column layouts but not litigation invoices, LEDES exports or large-firm templates. Adding invoices from other dockets or from customers (with consent) would widen it.
- Other sources examined and set aside: Texas PUC rate-case-expense exhibits (heavily degraded scans, mixed with courier and consultant invoices), Illinois community-college board packets (payment summaries, not invoices), municipal contract packets (terms, not invoices).
- Real PDFs are cut from the downloaded filings by `generator/cut_real_invoices.py` using the page ranges in `generator/real_sources.py`; re-download the filings from the listed URLs to rebuild `local/`.

### 7.3 Known anomalies in source filings (not label defects)

- A-02: the filing's own timekeeper summary prints 14,256.00 for an associate at 740.00 an hour for 19.4 hours (should be 14,356.00); its total row and the line amounts agree with the corrected value, so that summary-table total is excluded from the checks.
- A-04: the cover sheet states interim fees of 8,766.50 while the attached invoice totals 12,766.50 before a 457.50 reduction; the invoice figures are used.
- C-05: fees include a 50.00 flat charge (1.50 flat-charge hours) that is not part of the 70.70 billable hours; the line is flagged `flat_charge`.
- C-10: the expense total (21,019.40) is mostly duplicating charges; only the total is labelled.

### 7.4 Rendering, freshness and checks

- Set F PDFs are rendered on demand (`gen_eval_set.py render`); only set B PDFs were rendered for the spike. Bounding boxes are not part of the ground truth: scoring is on fields and line references, not coordinates.
- 2026-10-15 (task 004 finding, fixed): B-0001's PDF was stale relative to its ground-truth JSON (task 004 found it; B-0004 agreed). All 150 local B PDFs were re-rendered and `gen_eval_set.py render` now writes `local/synthetic/render-manifest.json` (ground-truth sha256, page count, scanned flag per PDF). `gen_eval_set.py check` fails when a PDF is missing from the manifest, was rendered from different ground truth, or whose text disagrees with the JSON (invoice number, matter, PO, totals, invoice date, every line amount, fee-line hours where the template prints hours, first 28 narrative characters; page count). Scan-noise PDFs are checked on the clean render before the noise is applied. Task 005 should run `check` before scoring.
- Environment: `python -m venv .venv` then `pip install -r generator/requirements.txt` (reportlab for rendering, pypdfium2 for reading PDFs). Run the scripts that read downloaded filings with `-I`.
- Gates: `gen_eval_set.py verify` (counts, licence fields, reference evaluator over every F file), `gen_eval_set.py check` (PDF versus ground truth), `build_manifest.py --check` (FR-02 targets and row requirements).

### 7.5 Spike subset (task 005)

The spike holds the B items listed in section 3 (including B-0150, the longest invoice, and bundled PDFs), two fee applications (A-02 line-level with a discounted rate, A-07 a long final application) and three real invoices (C-03 scan with OCR noise, C-10 interleaved columns, C-11 clean native text).

