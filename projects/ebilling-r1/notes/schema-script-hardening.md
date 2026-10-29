# eBilling schema scripts: shared module and required-level decisions

> **Project**: ebilling-r1. Fixes DEF-006, DEF-007, DEF-008, DEF-009, DEF-010 (GitHub #1931, #1937, #1938, #1939, #1940).
> **Date**: 2026-10-15. **Environment**: dev (spaarkedev1). **Module**: `scripts/ebilling/EbillingSchema.psm1`.

## What changed

Every schema script carried its own copy of the Dataverse helpers and each later task fixed only its own copy, so the same defects came back. The helpers now live in one module; the seven in-scope scripts import it (`using module .\EbillingSchema.psm1`) and keep only what is specific to them (status-value helpers, global choices, the D-33 pre-check, the verification block).

In scope: `Deploy-EbillingInvoiceLineSchema.ps1` (010), `InvoiceHeader` (012), `TimekeeperRateCard` (017), `BillingCode` (018), `PolicyStore` (019), `Submission` (020), `Approval` (021). Not touched: `Seed-EbillingBillingCodes.ps1`, `Deploy-EbillingExternalIdentifierSchema.ps1`, `Deploy-EbillingExportSchema.ps1` (tasks 022, 023).

Public parameters, `-WhatIf` behaviour, output text and the schema each script defines are unchanged. Differences in output, all deliberate:

- the "WOULD CREATE table" / "CREATED table" line now names the ownership (`user-owned` / `organization-owned`) in every script (three scripts printed none or the raw enum);
- scripts that raise a required level print the extra `CHANGED` / `WOULD SET` / `skipped` lines, and the submission script a `4b. Required levels` heading;
- the summary prints a `deferred:` line when a promotion was held back (scripts 017, 018, 020, 021).

## Defects and where they are fixed

| Defect | Fix in the module |
|---|---|
| DEF-006 / DEF-007: the primary name's required level is ignored in the entity-create body | `Set-PrimaryNameRequired` (read, PUT, read back) is called after `Add-Table` for `sprk_billingcodeset.sprk_name` (018) and `sprk_ruleset.sprk_name` (019); 017 / 020 / 021 already called it |
| DEF-008: `0x80072551` not treated as "already exists" | `Test-DuplicateRefusal` is the single definition of "already exists" (`0x80047013`, `0x80048403`, `0x80040216`, `0x8009050d`, `0x80072551`, "already exists"); every create helper uses it and confirms on a settled read, throwing if the column never becomes readable |
| DEF-010: `0x8009050d` not recognised; false "has rows" | same function; `Set-PrimaryNameLength` counts rows with `$count=true`, repeated until a count is returned (`Get-RowCountExact`), wrapped in the MetadataCache retry |
| DEF-009: ApplicationRequired is not enforced on API writes | `Set-LookupRequiredLevel` / `Set-StringRequiredLevel` / `Set-ColumnRequiredLevel` (below); comments and docs corrected |

Also in the module, from the task 021 / 020 versions: re-reads until metadata appears (`Get-DvOrNull -Retries`, `Wait-AttrVisible`), `Invoke-WhilePropagating` (MetadataCache retries), option-list re-reads (`Get-PicklistLabels`), backoff on customization / publish locks (`Invoke-Dv`).

Two defects found in the new code while testing, both fixed:

1. **A stale read made a re-run report a change.** `Set-ColumnRequiredLevel` read `sprk_vendororg` as `None` minutes after it had been raised to SystemRequired (the next read showed SystemRequired; nothing else touches that table). It now re-reads three times before concluding that a level must change.
2. **DEF-012 (#1942), filed by the task 023 session against this module.** A read-modify-PUT of the required level could restore the old primary-name length when the read was stale. `Set-PrimaryNameLength` now records the length it set and `Set-ColumnRequiredLevel` waits for a read that shows it before sending the PUT, and checks it again afterwards. Tasks 022 and 023 carried their own helper copies when this was written; both now import the module (2026-10-15) and DEF-012 is closed.

## Required-level decisions (DEF-009)

Rule: Dataverse enforces ApplicationRequired on forms only, so an API write that omits the value succeeds. SystemRequired is enforced on every write ("Attribute 'x' cannot be NULL"). SystemRequired where a null corrupts meaning (a parental or key column); everything else is ApplicationRequired and documented as "required by the BFF write path, not enforced on API writes". Every promotion is guarded in code: `Set-ColumnRequiredLevel` counts the rows with a null first and changes nothing (it lists the column as deferred) if there are any. A level is never lowered.

Null rows were counted in dev on 2026-10-15 before the first apply: all zero.

| Column | Script | Type | Level before (dev) | Rows / null rows | Decision | Reason |
|---|---|---|---|---|---|---|
| `sprk_ratecardrate.sprk_ratecard` | 017 | lookup, parental | ApplicationRequired | 0 / 0 | **SystemRequired** | Parental: a rate without its card is meaningless and the cascade needs the parent |
| `sprk_ratecard.sprk_name` | 017 | primary name | ApplicationRequired | 0 / 0 | ApplicationRequired (unchanged) | Display name; composed by the rate import |
| `sprk_ratecardrate.sprk_name` | 017 | primary name | ApplicationRequired | 0 / 0 | ApplicationRequired (unchanged) | Display name; composed by the rate import |
| `sprk_billingcode.sprk_codeset` | 018 | lookup | ApplicationRequired | 161 / 0 | **SystemRequired** | Alternate-key column; a code without a set cannot be addressed by the key |
| `sprk_billingcode.sprk_code` | 018 | primary name | **None** (DEF-006) | 161 / 0 | **SystemRequired** | Alternate-key column (and the primary name): the key does not cover a row with no code |
| `sprk_billingcode.sprk_codetype` | 018 | local choice | ApplicationRequired | 161 / 0 | ApplicationRequired (unchanged) | Not a key or parental column; the seed and the BFF set it. Comment and doc now say it is not enforced on API writes |
| `sprk_billingcodeset.sprk_name` | 018 | primary name | **None** (DEF-006) | 8 / 0 | **ApplicationRequired** | Primary name; the seed matches a set on it but it is not an alternate key |
| `sprk_ruleset.sprk_name` | 019 | primary name | **None** (DEF-007) | 0 / 0 | **ApplicationRequired** | Primary name; the BFF write path (task 076) refuses a nameless rule set |
| `sprk_invoicesubmission.sprk_vendororg` | 020 | lookup | None | 0 / 0 | **SystemRequired** | Alternate-key column; D-70 scopes external access by firm, so a row without a firm would be invisible or unscoped. See "Open judgement" below |
| `sprk_invoicesubmission.sprk_trackingnumber` | 020 | text | None | 0 / 0 | **SystemRequired** | Alternate-key column; two rows with an empty number would escape the key |
| `sprk_invoicesubmission.sprk_name` | 020 | primary name | ApplicationRequired | 0 / 0 | ApplicationRequired (unchanged) | Display name, normally the tracking number |
| `sprk_invoiceapproval.sprk_invoice` | 021 | lookup | SystemRequired | 0 / 0 | SystemRequired (unchanged) | Already done by task 021 |
| `sprk_invoiceapproval.sprk_name` | 021 | primary name | ApplicationRequired | 0 / 0 | ApplicationRequired (unchanged) | Display name |
| 010 / 012 columns | 010, 012 | various | None | n/a | none | No column in these scripts is marked required |

Nothing was held back: no promoted column had a null row. The metadata reports `CanBeChanged = True` on every promoted level, so a decision can be reversed by script.

**Open judgement.** `sprk_invoicesubmission` records rejected and failed attempts as well as accepted ones. If a writer (task 044) ever has to record an attempt before the firm is known, `sprk_vendororg` being SystemRequired will refuse it. The firm comes from the SPA login or the internal user's choice and tracking numbers are allocated per firm (task 045), so this should not arise; it is flagged so that task 044 confirms it. Lowering it is one call to `Set-ColumnRequiredLevel`.

## Verification against dev (spaarkedev1)

1. `-WhatIf` of all seven scripts: the only differences from the pre-refactor scripts' `-WhatIf` output were the required-level lines above. Scripts 010, 012 and 021 were byte-identical.
2. Apply: 017 (1 change), 018 (3), 019 (1), 020 (2) as in the table; 010, 012, 021 `created 0, changed 0`. Each publish was entity-scoped and succeeded; no lock retry was needed.
3. Second run of all seven: `created 0, changed 0`, exit 0 (one run reported a stale-read change, fixed as described above and re-run clean twice).
4. Unit checks of the module against dev (dry run): the duplicate test accepts `0x80072551` and `0x8009050d`; a column with null rows (`sprk_billingcode.sprk_phase`, 35 of 161) is refused for SystemRequired and listed as deferred; an already-SystemRequired column is not lowered; the exact row count of an empty table is 0.

The duplicate-refusal paths (`0x80072551`, `0x8009050d`) could not be provoked on demand against dev without creating metadata; they are covered by the unit check of the shared test and by the code path that the 021 and 020 scripts already exercised.
