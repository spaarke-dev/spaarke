# Task 022 - sprk_externalidentifier schema

> Project: ebilling-r1 (FR-12, FR-05, D-12, D-68). Executed 2026-10-15 against dev (`https://spaarkedev1.crm.dynamics.com`, solution `SpaarkeMaster`).
> Script: `scripts/ebilling/Deploy-EbillingExternalIdentifierSchema.ps1`. Data model: `docs/data-model/sprk_externalidentifier.md`.

## Result

New table created in dev and verified; the second run created and changed nothing (`created : 0, changed : 0, skipped : 20`, "nothing changed - no publish", exit 0). `-WhatIf` after the real run also made no change. Nothing existing was touched or deleted (the only writes outside the new table are the six lookup relationships, which add a lookup column to the new table and a relationship entry on the referenced tables). No file under `src/dataverse/solutions/SpaarkeMaster/**` and no C# file changed. The table is a member of `SpaarkeMaster` (root component, `rootcomponentbehavior` 0, so all subcomponents) - task 029's export picks it up.

## Key decision (escalation trigger 1)

The owner's D-68 key (issuing organization, identifier type, normalized value, core record id) **was buildable**: Dataverse accepted a key over a lookup, a choice and two text columns, and the index went Pending -> InProgress -> Active in about 90 s. So the "STOP and report" trigger did not fire and no other key was considered. The owner's decision is already in `notes/decisions-log.md` as D-68 (not re-added). The key is addressed in the data-model doc and the alternate-key catalog.

Escalation trigger 2 (task 012's lookup names) did not fire: the main session settled it as the planned mixed scheme (task 013); this table uses `sprk_regardingmatter` / `sprk_regardingproject` / `sprk_regardingworkassignment`.

## Live schema (metadata read, run 1 verify and run 2)

User-owned, entity set `sprk_externalidentifiers`, primary name `sprk_name` (text 200, application-required).

- Choice (local) `sprk_identifiertype`, SystemRequired: Firm invoice number 100000000, Firm client-matter number 100000001, Law-department matter number 100000002, PO as quoted 100000003, AP document number 100000004, VAT invoice number 100000005, E-invoice / clearance ID 100000006, Peppol message ID 100000007.
- Text: `sprk_identifiervalue` 200, `sprk_normalizedvalue` 200 (SystemRequired), `sprk_issuingsystem` 200, `sprk_regardingrecordid` 100, `sprk_regardingrecordname` 200, `sprk_regardingrecordnumber` 100, `sprk_regardingrecordurl` 500 (Url).
- Lookups (all Delete = RemoveLink, Assign / Share NoCascade): `sprk_issuingorganization` -> sprk_organization (`sprk_externalidentifier_issuingorg_organization`); `sprk_regardinginvoice` -> sprk_invoice (`..._reginvoice`); `sprk_regardingmatter` -> sprk_matter (`..._regmatter`); `sprk_regardingproject` -> sprk_project (`..._regproject`); `sprk_regardingworkassignment` -> sprk_workassignment (`..._regworkassignment`); `sprk_regardingrecordtype` -> sprk_recordtype_ref (`..._regrecordtype_recordtype_ref`).
- Alternate key `sprk_externalidentifier_firm_type_value_record`; Dataverse lists the attributes alphabetically (`sprk_identifiertype`, `sprk_issuingorganization`, `sprk_normalizedvalue`, `sprk_regardingrecordid`); index status **Active**.

## Run history

| Run | Result |
|---|---|
| `-WhatIf` (before) | 16 items "would create" (table, 14 columns / lookups, key), no write, exit 0. |
| Apply 1 | Created everything, shortened `sprk_name` 850 -> 200, set it application-required, raised type and normalized value to SystemRequired, key created and Active in about 90 s, published, verify passed, exit 0. Two 429 customization locks (task 021 publishing) were retried; one MetadataCache retry after the table create. |
| Apply 2 | `created : 0, changed : 0, skipped : 20`, "nothing changed - no publish", verify passed, exit 0. |
| `-WhatIf` (after) | `created : 0, changed : 0, skipped : 20`, exit 0. |

## Key behaviour tests (acceptance criterion 3)

Raw Web API, dev, test rows named `ZZTEST022-*`, all 9 + 1 deleted afterwards (0 `sprk_externalidentifier` rows remain, read back with `$count`):

| Case | Result |
|---|---|
| A: firm 1, Firm client-matter number, `ABC123`, record `ZZR1` | created |
| A': identical to A | **refused**, HTTP 412, `0x80060892` "Entity Key Firm, Type, Value and Record violated ... A duplicate record cannot be created." |
| C: firm 2, same type / value / record | accepted (firm-scoped) |
| D: firm 1, same type / value, record `ZZR2` | accepted (core-record-scoped, as D-68 intends) |
| E: firm 1, Firm invoice number type, same value / record | accepted |
| F: firm 1, same type, other value | accepted |
| filtered query (firm 1, type, `ABC123`) | returns the `ZZR1` and `ZZR2` rows |
| create without `sprk_identifiertype` / without `sprk_normalizedvalue` | refused, HTTP 400, `0x80040200 Attribute ... cannot be NULL` |
| GET by key `(_sprk_issuingorganization_value=...,sprk_identifiertype=100000001,sprk_normalizedvalue='ADDR1',sprk_regardingrecordid='ZZR1')` | works; other spellings (`sprk_issuingorganization=...`) refused HTTP 400. One earlier attempt, minutes after the key went Active, was refused and the same form worked later (cause not established). |

### Finding: a row with an empty key column is not covered by the key

Two rows with the same firm / type / value and **no** `sprk_regardingrecordid` were both accepted (G1, G2), and so were two rows with the same type / value / record and **no** issuing organization (H1, H2). This is Dataverse's alternate-key behaviour (also noted for task 020), not a script defect, and it is a property of the owner's key, so the key was not changed. Consequence for the writer: task 044 must always set the issuing organization and the resolver record id on a firm-quoted identifier, otherwise the duplicate check silently does not apply. For a row whose only regarding lookup is `sprk_regardinginvoice`, what `sprk_regardingrecordid` holds (the invoice id is the natural value) is for task 013 to define. Both are written into the data-model doc ("Notes for the writers"). I deliberately did not make the issuing organization or the record id required: a law-department matter number or a PO is not necessarily issued by a firm, and the resolver id is filled by the server in 013, so a schema-level requirement could break legitimate rows.

## Deviations and judgment calls

- **Built on the task 021 helpers, not 020's**: 021 already carries the `0x8009050d` (table-create duplicate) and `0x80072551` (lookup-create duplicate) handling, the `$count` row test in `Set-PrimaryNameLength` and the SystemRequired helper (DEF-008, DEF-010 fixes), so none of those defects is repeated here. `Set-LookupRequiredLevel` was generalised to `Set-RequiredLevel` with the attribute metadata type so it also serves a choice and a text column.
- **SystemRequired on `sprk_identifiertype` and `sprk_normalizedvalue`** (not in the POML): an identifier with no type or no matching value cannot be looked up and would break the key's meaning; ApplicationRequired is not enforced on API writes (DEF-009), SystemRequired is (checked live above). Additive; the table was empty.
- The key create treats an HTTP 400 refusal as an escalation (exit 2, "do not choose another key"); "already exists", MetadataCache and lock conditions are retried or skipped.
- The shared helper module `scripts/ebilling/EbillingSchema.psm1` appeared in the worktree while this task ran (another session's hardening pass for DEF-006 to DEF-010). This script is self-contained and does not use it; porting it to the module is part of that pass.

## Class check (task-execute Step 9.5 item 6)

Patterns considered: "primary name RequiredLevel / MaxLength ignored in the entity-create body" (handled in this script; the other scripts are DEF-006 / DEF-007) and "ApplicationRequired not enforced on API writes" (DEF-009; applied here). Nothing new to file. Catalog gap: `docs/data-model/schema-additions-alternate-keys.md` lists the task 018 key (`sprk_billingcode`) but not the task 020 key (`sprk_invoicesubmission_firm_trackingnumber`). Not added by this task (own-table rows only, per the POML constraint); reported to the main session.

## Quality gates

STANDARD rigor, no C#/TS code and no `tests/**` change: code-review and adr-check not required by the protocol. ADR-002 (no plugin / business rule / calculated column / flow) and ADR-024 (three entity-specific lookups, five resolver fields) were checked against the live metadata above; no ADR conflict.

rework: none task 022
