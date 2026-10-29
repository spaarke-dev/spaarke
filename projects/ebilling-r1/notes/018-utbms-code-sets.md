# Task 018 — UTBMS code sets: schema, mapping lookups, seed files, seed script (notes)

> Run 2026-10-15 against dev (`https://spaarkedev1.crm.dynamics.com`). Scripts: `scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1`, `scripts/ebilling/Seed-EbillingBillingCodes.ps1`. Seed files: `scripts/seed-data/ebilling/utbms-*.csv`. Rigor STANDARD. Data model: `docs/data-model/sprk_billingcode.md`.

## 1. Step 1 — dev baseline (before any write)

Read-only metadata GETs: `sprk_billingcodeset` and `sprk_billingcode` returned 404; `sprk_mattertype_ref` and `sprk_practicearea_ref` exist (organization-owned, primary names `sprk_mattertypename` / `sprk_practiceareaname`) and had no `sprk_defaultbillingcodeset`. No same-name object of another type existed: escalation triggers did not fire at the schema stage.

## 2. Schema runs (dev)

| Run | Result |
|---|---|
| `-WhatIf` | 13 items "would create" (2 tables, 10 columns incl. 2 mapping lookups, 1 key), no write, exit 0. |
| Apply 1 | Both tables and the 3 code-set columns created; creating `sprk_billingcode.sprk_codeset` failed once with `0x80041102: The entity ... was not found in the MetadataCache` (the just-created table was still propagating). Nothing else was written. |
| Script fix 1 | A create issued while this run's new table propagates (`0x80041102`, `MetadataCache`, 404) is retried (12 x 8 s). |
| Apply 2 | Tables skipped, 5 `sprk_billingcode` columns created, then the alternate key was refused: `0x80060895: Index size exceeded 1700 bytes`. The inline primary-name `MaxLength` in the entity-create body is **ignored** (both primary names came back as 850). |
| Script fix 2 | `Set-PrimaryNameLength` shortens `sprk_code` to 100 and `sprk_name` to 200 right after table creation, only while the table has no rows. |
| Apply 3 | Lengths fixed, key created and Active, but the script waited 400 s for a key it could not find: Dataverse returns `KeyAttributes` alphabetically (`sprk_code, sprk_codeset`) and the comparison was order-sensitive. |
| Script fix 3 | Key attribute lists compared sorted. |
| Apply 4 | Key found Active; created the two `sprk_defaultbillingcodeset` lookups; published; verification read-back OK; exit 0. |
| Re-run | `created : 0`, `changed : 0`, `skipped : 15`, "nothing changed - no publish", exit 0; `-WhatIf` identical. |

Primary-name shortening of my own just-created, empty columns is a modification of this task's objects, not a delete or rename by logical name.

Independent read-back (separate script, raw Web API): `sprk_billingcodeset` = `sprk_name`, `sprk_body`, `sprk_version`, `sprk_sourceurl`; `sprk_billingcode` = `sprk_code`, `sprk_codeset` (Lookup), `sprk_codetype` (Picklist: Task 100000000, Activity 100000001, Expense 100000002), `sprk_phase`, `sprk_description`, `sprk_guidancetext` (Memo 10000); key `sprk_billingcode_codeset_code_key` on (`sprk_code`, `sprk_codeset`) = **Active**; `sprk_defaultbillingcodeset` -> `sprk_billingcodeset` on both `_ref` tables; statuscode 1 = Active (state 0), 2 = Inactive (state 1) on both new tables. Both tables are in the unmanaged `SpaarkeMaster` solution with all subcomponents (`rootcomponentbehavior = 0`), so task 029's export picks them up. No file under `src/dataverse/solutions/SpaarkeMaster/` and not `scripts/seed-data/manifest.yaml` changed.

## 3. Task 010's deferred lookups

Re-running `scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1` (unchanged) after the table existed: `created : 3  sprk_taskcode, sprk_activitycode, sprk_expensecode`, `skipped : 24`, `deferred: 0`, published, exit 0. Metadata read: all three are `Lookup` -> `sprk_billingcode` on `sprk_billingevent`. Next run: `created : 0`, `skipped : 27`, `deferred: 0`. Recorded in `notes/010-invoice-line-columns.md` section 7; `docs/data-model/sprk_billingevent.md` no longer marks them deferred. This proves 010's deferred path.

## 4. Source files (downloaded 2026-10-15 from utbms.com)

Terms of use (https://utbms.com/terms-of-use/): "You may download materials displayed on this website"; D-32 (owner) records UTBMS / LEDES code sets as freely usable by systems. All eight sets were available from utbms.com; **no escalation trigger fired** (none was unavailable). Files were fetched with curl into their own directory, read only with `python -I` and `pdftotext`, never executed. SHA-256 of the files used:

| File | SHA-256 |
|---|---|
| ABA-UTBMS-Litigation-Codes-1997.xlsx | 06b6d5c46cc6505d8ea968aad21d0ca4bf485eb1651a3259386192bb149c65fb |
| ABA-UTBMS-Counseling-Codes-1997.xlsx | c3d440c24eb560d44142b61ae984fd4ec7183a38a3dfbeff649959d802cf0334 |
| ABA-UTBMS-Project-Codes-1997.xlsx | 8e8ec0105127a6638bb6775fa49ba01a22b493120c69f22d98285fa0df52173f |
| ABA-UTBMS-Bankruptcy-Codes-1997.xlsx | 8df6fad5bdaa6d01aede8f5bb5c76232481fbd5f4e448c63d748cfff18de3f7e |
| LOC-IP-Patent-Prosecution-Codes-4-2023.xlsx | a6825fa74bea0ae9e5947c5388bca025d8d88ad45bdab029d9f3645ed47c5d35 |
| UTBMS-Trademark-Billing-Codes.pdf | da89ba1c417ab5dcc16527e6f083833416e497b97d849be6d42c89077d6fe4cc |
| ABA-UTBMS-Activity-Codes-1997.xlsx | e0937e6f492ff9aad70cc35fd9261abea41ff3f7877d92a8b983c133a714f84c |
| ABA-UTBMS-Expense-Codes-1997.xlsx | 4e7eecb67fcb55d36f7f8a4e5e77a0624bc68f209497c3eab2912f3abc51894b |

URLs are in each CSV's `source_url` column and in `docs/data-model/sprk_billingcode.md`. The CSVs were generated by a throw-away converter (not committed; the cleaned CSVs are the committed artefact). Duplicate check: no duplicate code within any set (escalation trigger 2 did not fire).

## 5. Decisions (spec leaves them open)

- **Which "IP patent".** utbms.com lists two patent sets: the LOC 2009 Patent set (PA codes, PDF) and the **LOC IP Patent Prosecution Codes, April 2023** (numeric 110xxx codes, spreadsheet) that supersedes it. Seeded the 2023 set (name matches FR-08's "IP patent"). Trademark = LOC 2009 Trademark (TR codes; the only trademark set, PDF).
- **Activity and expense = the original ABA lists** (A101-A111, E101-E124, spreadsheets). The LOC 2013 revised lists and the IP expense codes E125-E131 are PDF-only with misaligned tables and were not seeded: **DEF-005**, GitHub #1929. This is a gap, not a silent substitution; it needs an owner choice of standard.
- **A row is a billable code.** A phase with task rows is not a row (it is the task's `sprk_phase`); a phase without tasks is itself a row (all counseling codes; project P100, P300-P800).
- **`sprk_phase` holds the phase code only** (`L100`, `TR200`, `110400`), empty for activity and expense; phase names are not stored (spec FR-08 lists a single phase column).
- **Guidance text** = the published definition; IP patent = the LOC's USPTO validation documents / PAIR transactions; activity and expense have none in the source (empty). One source typo fixed (`IACHII` -> `IA; CHII`, a missing separator in the IP patent workbook).
- **Seed idempotency: create-only.** Existing rows are not updated in any column (the spec offered "update only if equal to the previous seed value, or not at all"). Reason: the script cannot tell an admin edit from a stale seed. Differences are reported, not written.
- Mapping rows (`sprk_defaultbillingcodeset` values) are not seeded: the admin and task 048 own them.

## 6. Seed runs (dev)

| Run | Result |
|---|---|
| `-WhatIf` | 8 sets and 161 codes would be created, 0 writes, exit 0. |
| Apply | 8 sets + 161 codes created (169 writes), exit 0. |
| Re-run | `sets created: 0`, `codes created: 0`, `updated: 0`, `unchanged: 161`, `writes this run: 0`, exit 0. |
| Guidance-edit test | Edited `L110` guidance via PATCH on the alternate key; re-ran the seed: 0 writes, report showed "1 guidance" left alone; the edit was unchanged; restored the original text and re-read it. |
| Negative control | A copy of the counseling CSV with a repeated `C100 ` row: `STOPPED (exit 2) ... code 'C100' is not unique within set 'UTBMS Counseling' (lines 2, 6)`, nothing written. |

Code counts in dev equal the CSV row counts (read back per set):

| Set (dev id) | Body | Version | Codes = CSV |
|---|---|---|---|
| UTBMS Activity (789605b8-abc8-f111-a05c-3833c5e9614d) | ABA | 1997 | 11 |
| UTBMS Bankruptcy (7ff27db8-abc8-f111-a05a-7c1e520a989f) | ABA | 1997 | 21 |
| UTBMS Counseling (01e102be-abc8-f111-a05c-3833c5e9614d) | ABA | 1997 | 4 |
| UTBMS Expense (1ee102be-abc8-f111-a05c-3833c5e9614d) | ABA | 1997 | 24 |
| UTBMS IP Patent Prosecution (567ec4c0-abc8-f111-a05c-0022482913fc) | LOC | 2023-04 | 23 |
| **UTBMS Litigation (827ec4c0-abc8-f111-a05c-0022482913fc)** | ABA | 1997 | 29 |
| UTBMS Project (c3e973c4-abc8-f111-a05a-7c1e520a989f) | ABA | 1997 | 15 |
| UTBMS Trademark (caaac1c6-abc8-f111-a05c-0022482913fc) | LOC | 2009 | 34 |

### For task 025 (settings default = Litigation)

Litigation code set: **name `UTBMS Litigation`, id `827ec4c0-abc8-f111-a05c-0022482913fc`** (dev; ids are environment-specific, so resolve by name). This task does not create the settings row.

## 7. Gotchas worth keeping

- Alternate key with a lookup is addressed by the **lookup value name**: `sprk_billingcodes(_sprk_codeset_value=<id>,sprk_code='L110')`. `sprk_codeset=` / `sprk_CodeSet=` give 400.
- Entity-create body: the primary-name `MaxLength` is ignored (850); an alternate key on (lookup, text) needs the text kept short (index limit 1,700 bytes).
- `KeyAttributes` come back alphabetically ordered.
- A column / relationship create right after a table create can fail with `0x80041102 ... MetadataCache`; retry.

## 8. Quality gates and class check

- STANDARD rigor, configuration and seed-data task, no `tests/**`, no BFF code: Step 9.5 code-review / adr-check skipped per the skip rule; checked by hand against ADR-002 (no plugin, business rule, calculated column or flow), CLAUDE.md §11 (justification is in the POML) and the project constraints (additive only, no delete or rename by logical name, SpaarkeMaster source and `manifest.yaml` untouched).
- Class check (Step 9.5 item 6) for the two script defects fixed here: (a) order-sensitive comparison of `KeyAttributes` - a grep of `scripts/` finds no other script that compares a multi-attribute key (the others compare a single column); (b) trusting the primary-name `MaxLength` in an entity-create body - 17 scripts use `IsPrimaryName` inline (`grep -l IsPrimaryName scripts`), not opened or verified, out of scope; a shortening step like `Set-PrimaryNameLength` is the model if any of them needs a key on that column.
- Pre-existing observation, not changed: `Deploy-EbillingInvoiceLineSchema.ps1` reads `sprk_billingcode` once without a retry, so a lagging read right after the table is created could report the lookups deferred; harmless (exit 0, re-run creates them). It did not happen here.

rework: fix-round-3 task 018
