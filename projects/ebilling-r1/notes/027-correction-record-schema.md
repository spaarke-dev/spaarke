# Task 027 — Correction record schema (notes)

> Run 2026-10-15 against dev (`https://spaarkedev1.crm.dynamics.com`). Script: `scripts/ebilling/Deploy-EbillingCorrectionSchema.ps1`. Rigor STANDARD. Data model: `docs/data-model/sprk_invoicecorrection.md`. Decision: D-82 (proposed) in `decisions-log.md`.
> **Status: DONE. The escalation trigger did not fire** (the analysis chose a child table, so task 010's `sprk_billingevent` columns are untouched).

## 1. Shape decision (D-82, proposed)

Child table `sprk_invoicecorrection`, not columns on `sprk_billingevent`. Against the four criteria: several fields per line each need their own AI value, confidence and outcome (columns on the line hold one set per column, not per field); header fields are corrected too and have no line; a value can be confirmed and later corrected (history is more than one row per field); and rows must be queried per Action code + version + outcome type. Recorded as a proposed row in the decisions log for the owner to confirm.

## 2. Runs

| Run | Result |
|---|---|
| Discovery | `sprk_invoicecorrection` did not exist. Targets `sprk_invoice`, `sprk_billingevent`, `systemuser`, `sprk_matter`, `sprk_project`, `sprk_workassignment`, `sprk_recordtype_ref` all exist. |
| `-WhatIf` | 22 creations planned, 0 writes, exit 0. |
| Apply 1 | Table, primary name length (850 -> 200) and required level, 10 columns, `sprk_fieldname` and `sprk_outcometype` raised to SystemRequired, `sprk_invoice` lookup created and raised to SystemRequired. Then **exit 1**: creating `sprk_invoiceline` with delete = Cascade was refused (`0x80047007`, "sprk_invoicecorrection is parented to ... sprk_invoice. Cannot create another parental relation with Entity sprk_BillingEvent"). |
| Fix | The brief asked for Cascade on both the invoice and the line; Dataverse allows one cascade-delete (parental) relationship per table. The invoice (always set) stays the parent (Cascade); the line lookup is RemoveLink. Script and data-model doc state this. |
| Apply 2 | `created : 10` (line, reviewedby, the four regarding lookups and four resolver columns), `changed : 0`, `skipped : 17`, published, verification passed, exit 0. |
| Apply 3 | `created : 0  changed : 0  skipped : 27`, "nothing changed - no publish", exit 0. |
| Final `-WhatIf` | `created : 0  changed : 0  skipped : 27`, exit 0. |

## 3. Live dev schema changes (all additive)

Table `sprk_invoicecorrection` (user-owned, entity set `sprk_invoicecorrections`, in unmanaged `SpaarkeMaster`). Columns: `sprk_name` (text 200, application-required), `sprk_fieldname` (text 100, SystemRequired), `sprk_aivalue` / `sprk_finalvalue` (multiline 4000), `sprk_aiconfidence` (decimal precision 4, 0 to 1), `sprk_outcometype` (local choice: Corrected 100000000, Confirmed individually 100000001, Accepted in bulk 100000002, Unreviewed 100000003; SystemRequired), `sprk_islabel` (two-option, default No), `sprk_sourcecode` (text 100), `sprk_sourceversion` (text 20), `sprk_reviewerrole` (text 100), `sprk_reviewedon` (date and time), resolver text `sprk_regardingrecordid` (100) / `name` (200) / `number` (100) / `url` (500, Url). Lookups (NoCascade for assign, merge, reparent, share, unshare): `sprk_invoice` -> `sprk_invoice` (SystemRequired, delete Cascade, `sprk_invoicecorrection_invoice_invoice`), `sprk_invoiceline` -> `sprk_billingevent` (RemoveLink, `..._invoiceline_billingevent`), `sprk_reviewedby` -> `systemuser` (RemoveLink, `..._reviewedby_systemuser`), `sprk_regardingmatter` / `sprk_regardingproject` / `sprk_regardingworkassignment` (RemoveLink, `..._regmatter` / `_regproject` / `_regworkassignment`), `sprk_regardingrecordtype` -> `sprk_recordtype_ref`. Published entity-scoped (the table plus the seven lookup targets). No file under `src/dataverse/solutions/SpaarkeMaster/` changed; no existing column or table changed. No alternate key (nothing to add to `schema-additions-alternate-keys.md`; the table is append-only history, not an upsert target).

## 4. Acceptance evidence (live test rows, all deleted afterwards; table holds 0 rows)

Used the existing dev invoice `4c0f4d12-…` and line `5ea5ad2a-…` read-only as link targets.

- Row **without** `sprk_invoice`: refused, `0x80040200 Attribute 'sprk_invoice' cannot be NULL`.
- Row without `sprk_outcometype`: refused, `Attribute 'sprk_outcometype' cannot be NULL`.
- Header-field row (invoice and reviewer set, `sprk_invoiceline` empty, confidence 0.8123, outcome Corrected, label Yes): accepted; line read back null.
- Line row (outcome Confirmed individually, source `invoice-line-code` version `1`) and a bulk row (Accepted in bulk, version `2`): accepted; the bulk row's `sprk_islabel` read back false (the default).
- FetchXML filter `sprk_sourcecode = invoice-line-code` + `sprk_sourceversion = 1` + `sprk_outcometype = 100000001` returned exactly the one matching row (confidence 0.95, label true); the other two rows were excluded.
- Metadata read (script verification): every column and lookup above with the stated type and target; exactly four outcome options, local (not global); decimal precision 4, 0 .. 1; `sprk_islabel` default False; cascade configuration as listed.

## 5. Judgment calls and deviations

1. **Line lookup is RemoveLink, not Cascade** (see run table): Dataverse allows one parental relationship. Consequence: a deleted line leaves its correction rows with an empty line link (they then look like header rows, distinguishable by `sprk_fieldname` and the name label). Invoices and lines are closed by status in R1, not deleted, so this is a theoretical path. Alternative if the owner prefers history to block line deletion: Restrict delete on the line lookup.
2. **ADR-024 `sprk_regarding*` trio + resolver fields added** although the brief's column list did not name them: the table holds values read from secure invoices and the 013 coordinator decision says new invoice-family tables carry the trio (task 013 lists 027 as a dependency "if it carries core lookups"). Same shape and lengths as 021. Dropping them later is harder than leaving them empty; task 013 may register the table in the secure family or decide it needs none.
3. **SystemRequired on `sprk_fieldname` and `sprk_outcometype`** besides `sprk_invoice` (DEF-009): a row with no field cannot be queried or understood, and a null outcome type would silently drop out of both the label count and the per-outcome calibration. `sprk_name` stays ApplicationRequired. `sprk_aiconfidence` is empty when a value had no AI source. `sprk_islabel` defaults to No so a writer that forgets it never creates a false label; the writers (100, 101, 103) set it from the outcome type.
4. **Choice raised to SystemRequired after create**: the shared module only raises String and Lookup columns, so the script carries a small `Set-ChoiceSystemRequired` (same rules: never lowers, refuses while rows hold a null) and `New-DecimalDef` / `New-TwoOptionDef` (copied from 010 / 019). Candidates for the shared module; not moved (other agents' file).
5. **statuscode**: no custom statuscode values (the table is append-only; the defaults carry no meaning). Documented in the data-model doc.
6. Per the session's instructions, `TASK-INDEX.md` and `current-task.md` were not edited; the POML status is set to completed, the index cell is left to the main session. Nothing was committed.

## 6. Quality gates and class check

- STANDARD rigor, configuration-only task (no `tests/**`, no BFF or app code): Step 9.5 code-review and adr-check skipped per the skip rule; checked by hand against ADR-002 (no plugin, rollup, flow, business rule), ADR-015 (data-model note; no prompt content stored), CLAUDE.md section 11 (justification in the POML) and the project constraints (additive only, SpaarkeMaster source untouched, entity-scoped PublishXml).
- Class check (Step 9.5 item 6) for "a metadata create that the platform refuses at the cardinality level" (two parental relationships): the only other cascade-delete lookups in `scripts/ebilling` are single per table; nothing to list.
- No defects found in other tasks' code.

## 7. For 013 and 029

- 013: decide secure-family registration of `sprk_invoicecorrection` (child lineage row, role config, census, creator-person stamp if created app-only; writers 100/101/103 are human-driven so `sprk_createdbyperson` may not be needed). Lookup names for the guard mapping: `sprk_regardingmatter`, `sprk_regardingproject`, `sprk_regardingworkassignment`.
- 029: include `sprk_invoicecorrection` in the SpaarkeMaster export; a view of corrections by invoice is optional (no form needed, writers are server-side).

rework: fix-round-1 task 027
