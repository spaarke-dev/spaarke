# Task 023 — sprk_billingexport schema and invoice batch lookup (notes)

> Run 2026-10-15 against dev (`https://spaarkedev1.crm.dynamics.com`). Script: `scripts/ebilling/Deploy-EbillingExportSchema.ps1`. Rigor STANDARD. Data model: `docs/data-model/sprk_billingexport.md`.
> **Status: DONE. No escalation fired.**

## 1. Discovery (before any write)

- `sprk_billingexport` did not exist (404). `sprk_document` exists (primary name `sprk_documentname`).
- Escalation trigger: `sprk_invoice` had no attribute `sprk_billingexport` and no many-to-one relationship to any table whose name contains `export` or `batch`. Did **not** fire. The script repeats this read-only check on every run (exit 2 before any write if it ever fails; on a re-run its own lookup is recognised).
- No core-record lookups on this table, so task 013's naming scheme does not apply.

## 2. Runs

| Run | Result |
|---|---|
| `-WhatIf` | 10 creations planned, 0 writes, exit 0. |
| Apply 1 | Table, primary-name length and required level, 6 columns, 3 lookups created (several `0x80071151` customization-lock retries handled by backoff), published. Verification failed (exit 1): `sprk_name` max length read back 850. Cause: `Set-PrimaryNameRequired` PUT the whole attribute from a stale read still carrying 850 and undid the length change (DEF-012). |
| Fix | `Set-PrimaryNameRequired` now takes the wanted length and waits (up to 60 s) for a read that shows it before the PUT. |
| Apply 2 | `changed : 1` (`sprk_name` length 850 -> 100), skipped 11, published, verification passed, exit 0. |
| Apply 3 and final `-WhatIf` | `created : 0  changed : 0  skipped : 12`, "nothing changed - no publish", exit 0. |

The fixed wait was not re-exercised on a fresh table (no table is deleted); apply 2 shows the PUT sequence is otherwise correct (length changed, required level kept).

## 3. Live dev schema changes (all additive)

Table `sprk_billingexport` (user-owned, entity set `sprk_billingexports`, in unmanaged `SpaarkeMaster`). Columns: `sprk_name` (text 100, application-required), `sprk_invoicecount` (whole number 0 .. 1,000,000), `sprk_totalamount` (money precision 2, min 0, plus `_base`), `sprk_formats` (local multi-select: CSV 100000000, JSON 100000001, XML 100000002), `sprk_exportstatus` (local choice: Generating 100000000, Completed 100000001, Failed 100000002, no default), `sprk_mappingsnapshot` (multiline 1,048,576), `sprk_failurereason` (text 200). Lookups (delete RemoveLink, other cascades none, not required): `sprk_csvdocument` -> `sprk_document` (`sprk_billingexport_csvdocument_document`), `sprk_jsondocument` -> `sprk_document` (`sprk_billingexport_jsondocument_document`), and on `sprk_invoice`: `sprk_billingexport` -> `sprk_billingexport` (display "Export Batch", `sprk_invoice_billingexport_billingexport`). Published entity-scoped (sprk_billingexport, sprk_invoice, sprk_document). No file under `src/dataverse/solutions/SpaarkeMaster/` changed; no existing `sprk_invoice` column changed.

## 4. Acceptance evidence

- Metadata read (script verification and an independent raw Web API read): every column and lookup above with the stated types, targets and choice values. `sprk_invoice.sprk_billingexport` read back as `Lookup`, `Targets: ["sprk_billingexport"]`, so an invoice references at most one batch.
- Live test: a test batch created through the Web API with `sprk_formats = "100000000,100000001"`, `sprk_exportstatus = 100000001`, count 2, total 123.45, mapping snapshot; read back correctly (`_base` 123.45, exchange rate 1). Deleted afterwards; the table holds 0 rows. No invoice was linked or modified.
- Second run: `created : 0  changed : 0`; `-WhatIf` makes no write.
- Docs: `docs/data-model/sprk_billingexport.md`, one row in `docs/data-model/sprk_invoice.md`, one row in `docs/data-model/INDEX.md`.

## 5. Deviations and judgment calls

1. **No SystemRequired anywhere.** A null `sprk_billingexport` on an invoice (not yet exported) is the normal state; none of the batch columns corrupts meaning when empty (ApplicationRequired is not enforced on API writes, DEF-009), so only the primary name keeps ApplicationRequired (forms).
2. **`sprk_exportstatus` has no default.** The POML does not state one and the writer (task 150) sets the status explicitly; unlike the approval status there is no safe "initial" value to assume.
3. **"An invoice cannot be in two batches"** is structural only to the extent the lookup is single-valued. Nothing in metadata stops a writer overwriting it; task 150 must select only invoices with an empty `sprk_billingexport` in Approved status (documented in the data model).
4. **Primary-name helper fix** (DEF-012, #1942): the required-level step waits for the length change to settle. The shared module `EbillingSchema.psm1` and the 022 script carry the unfixed shape.
5. Per the session's instructions, `TASK-INDEX.md` and `current-task.md` were not edited; the POML status is set to completed, the index cell is left to the main session.

## 6. Quality gates and class check

- STANDARD rigor, configuration-only task (no `tests/**`, no BFF or app code): Step 9.5 code-review and adr-check skipped per the skip rule; checked by hand against ADR-002 (no plugin, rollup, flow, business rule), CLAUDE.md section 11 (justification in the POML) and the project constraints (additive only, SpaarkeMaster source untouched).
- Class check (Step 9.5 item 6) for the one repeatable defect: "read-modify-PUT of a whole attribute after a settled-read assumption" — grep of `scripts/ebilling` for `Set-PrimaryNameRequired` lists the module and the 022 script; listed in DEF-012, not changed (other agents' files).
- Defects filed: DEF-012 (#1942).

rework: fix-round-1 task 023
