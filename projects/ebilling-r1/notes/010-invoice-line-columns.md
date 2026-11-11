# Task 010 — Invoice Line columns on sprk_billingevent (notes)

> Run 2026-10-15 against dev (`https://spaarkedev1.crm.dynamics.com`). Script: `scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1`. Rigor STANDARD. Task 012 (`sprk_invoice`) reuses section 3.

## 1. Step 1 — live metadata vs the POML background

Read-only metadata GETs before any write. Differences from the background list:

- `sprk_billingevent` also carries `sprk_assignedtointernal` / `sprk_assignedtoexternal` (contact lookups), `sprk_createdbyperson` (lookup), `sprk_currency`, `sprk_containerid`, `sprk_correlationid`. None collides with a name this task adds.
- None of the 22 target logical names existed (no same-name column of another type): escalation trigger 1 did not fire.
- `sprk_costtype` global choice: Fee 100000000, Expense 100000001, unmanaged, as stated. `sprk_timekeeper` text column: 100 chars, display name "Timekeeper".
- Target tables present: `sprk_timekeeper`, `sprk_policyversion`, `sprk_workassignment`, `sprk_recordtype_ref`, `systemuser`. `sprk_billingcode` is absent (404): the three code lookups are deferred.
- `sprk_invoice` resolver fields (live): `sprk_regardingrecordid` Text 100, `sprk_regardingrecordname` Text 100, `sprk_regardingrecordnumber` Text 1000, `sprk_regardingrecordurl` URL 250, `sprk_regardingrecordtype` lookup -> `sprk_recordtype_ref` (**ApplicationRequired** there). These differ from the ADR-024 text (50 / 200); the task says copy live, so the script reads them from `sprk_invoice` at run time. `sprk_invoice` carries **no** `sprk_regardingmatter` / `sprk_regardingproject` / `sprk_regardingworkassignment` (only typed `sprk_matter` / `sprk_project` and `sprk_regardingagreement`) — task 012 adds the work-assignment lookup.
- `SpaarkeMaster` (unmanaged, v1.2.3.0) already contains `sprk_billingevent` with `rootcomponentbehavior = 0` (all subcomponents), so the new columns are picked up by task 029's export without separate solution edits. The script also sends `MSCRM.SolutionUniqueName: SpaarkeMaster`.

## 2. Runs (dev)

| Run | Result |
|---|---|
| `-WhatIf` | 22 items "would create", 2 "would relabel", 3 deferred, no write, exit 0. |
| Apply | Created 22 (21 columns + option `sprk_costtype.Adjustment`), relabelled 2 (entity display names, `sprk_timekeeper`), 3 deferred, publish OK, exit 0. |
| Re-run 1 | Failed at the last column: a metadata read returned 404 for `sprk_regardingrecordnumber` (created minutes earlier), the script tried to create it and Dataverse refused with `0x80047013 already exists`. **Metadata reads are eventually consistent across nodes** (the same GET gave 404 then 200 within seconds). Nothing was written. |
| Script fix | A create refused with "already exists" is now counted as skipped after a type check on a settled read; must-exist reads (entity, option set, `sprk_timekeeper`) retry on 404. |
| Re-run 2 (final script) | `created : 0`, `changed : 0`, `skipped : 24`, `deferred: 3`, "nothing changed - no publish", exit 0. Every item reported skipped. |

Deferred (exit 0): `sprk_taskcode`, `sprk_activitycode`, `sprk_expensecode` — "deferred: sprk_billingcode missing". The same `Add-Lookup` function created the other seven lookups, so the creation path after task 018 is the proven one; task 029 re-runs it.

## 3. ApplyStamps / mixed-scheme analysis (task 012 reuses this)

Read: `CoreAncestorResolver.cs` `ApplyStamps` (both overloads, ~718-790), `StampAsync` / `DeriveForHostAsync` / `DeriveAndProbeAsync`, `CoreAncestorLookups` (116-122), `ChildRecordEntities`, `IntermediateRootColumns`, `StampSourceColumns`; `SecureChildLineage.cs`; `RecordOwnershipResolver.OwnershipParentEntities`; `ChildRecordEndpoints.CreateTables`.

**What `ApplyStamps` does.** It runs only when a writer calls `StampAsync` / `DeriveForHostAsync` for a host entity. It takes the core ancestors derived from the **filing target**, skips the target itself, and writes `sprk_regarding{core}` onto the host for each ancestor whose column the host carries (live column probe); an ancestor the host has no column for is returned as `unstampable` and logged. It never writes anything for a host nobody passes it.

**Conclusion for `sprk_billingevent`: the mixed scheme maps cleanly; nothing was stopped.**

1. `sprk_billingevent` is in none of the resolver's tables: not in `ChildRecordEntities`, not an intermediate (`IntermediateRootColumns`), not a stamped child (`StampSourceColumns`, so the restamper and the 5-minute reconcile job never touch it), and no code calls `StampAsync` with a billing-event host (`OfficeService`, `ChildRecordEndpoints` and the Finance code do not). Adding `sprk_regardingworkassignment` therefore does **not** make the resolver stamp `sprk_billingevent`, and changes no derivation: the target of a line write is its invoice or its own core record, the invoice's root columns are the typed `sprk_matter` / `sprk_project`, and a core target's stamp is the target itself (skipped).
2. The column is a **filing lookup** (FR-05 "entity-specific lookup"), not an access stamp. D-75 holds: a line's core is read from its own `sprk_matter` / `sprk_project` / `sprk_regardingworkassignment`, never via `sprk_invoice`. Only the work-assignment lookup was added; the typed Matter / Project lookups are reused (no duplicate regarding columns, no migration), as the POML specifies. uac-r2's own notes (task 146 section 4) list billing event / budget / invoice as typed-lookup tables and document is a precedent for a typed work-assignment lookup, so the mixed scheme is already in the platform.
3. Nothing indicates uac-r2 wants all three `sprk_regarding*` columns on the line (no mention of billing event in its stamp tables).

**Obligations this creates for later tasks (not done here, BFF code):**

- **Task 013 / 026:** add `("sprk_regardingworkassignment", WorkAssignment)` to the `sprk_billingevent` entry of `SecureChildLineage.Children` (`SecureChildLineage.cs:74`). Today the entry lists only `sprk_invoice`, `sprk_matter`, `sprk_project`; a line filed under a secure work assignment would not be walked to its root (the mirror is the intersection over every lookup). Also confirm with uac-r2 (FR-05 review) that `RecordOwnershipResolver` re-filing handles a work-assignment-filed line.
- **Task 012 (`sprk_invoice`):** when it adds `sprk_regardingworkassignment` to the invoice, `IntermediateRootColumns["sprk_invoice"]` (today only typed `sprk_project` / `sprk_matter`) and its TypeScript mirror `INTERMEDIATE_ROOT_COLUMNS` (pinned by `IntermediateRootColumns_MatchTheTypeScriptSide`) must gain the work-assignment column in the **same change**; otherwise a child filed under a work-assignment invoice derives no stamp (silent FR-26 under-grant). Same for `sprk_billingevent` if task 026 makes the line an intermediate (`sprk_regardinginvoiceline`): its root columns would be typed `sprk_matter`, `sprk_project` plus `sprk_regardingworkassignment`.
- **Task 013:** the one-lookup guard and the five resolver fields are server-side; resolver fields on `sprk_billingevent` are not business-required (the invoice's record-type lookup is).
- A new relationship from `sprk_workassignment` to `sprk_billingevent` now exists (NoCascade, RemoveLink): uac-r2's live-sweep gates that enumerate root->child relationships will see it.

## 4. Verification (metadata read, 2026-10-15)

- Entity display names: "Invoice Line" / "Invoice Lines".
- All 21 added or repurposed columns present with the stated type and target (22 attributes checked incl. `sprk_timekeeper`):

| Column | Type / target |
|---|---|
| sprk_resolvedtimekeeper | Lookup -> sprk_timekeeper |
| sprk_approvedrate, sprk_approvedamount | Money, precision 2, 0 .. 1e9 |
| sprk_adjustmentreason | Memo 2000 |
| sprk_reviewedby | Lookup -> systemuser |
| sprk_reviewedon | DateTime |
| sprk_linestatus | Picklist (local) |
| sprk_taskcodeasbilled, sprk_activitycodeasbilled, sprk_expensecodeasbilled | String 100 |
| sprk_codingconfidence | Decimal precision 4, 0 .. 1 |
| sprk_extractionconfidence | Memo 100000 |
| sprk_adjustmentmethod | Picklist (local) |
| sprk_adjustmentvalue | Decimal precision 4, 0 .. 1e9 |
| sprk_adjustmentrule | Lookup -> sprk_policyversion |
| sprk_regardingworkassignment | Lookup -> sprk_workassignment |
| sprk_regardingrecordtype | Lookup -> sprk_recordtype_ref |
| sprk_regardingrecordid / name | String 100 |
| sprk_regardingrecordurl | String (Url) 250 |
| sprk_regardingrecordnumber | String 1000 |
| sprk_timekeeper | String 100, label "Timekeeper (as billed)" |

- New relationships: delete RemoveLink, assign / share / reparent NoCascade.

### Option values (needed by tasks 014, 016, 100)

- `sprk_linestatus` (local): Pending **100000000**, Clean **100000001**, Flagged **100000002**, Accepted **100000003**, Reduced **100000004**, Disallowed **100000005**.
- `sprk_adjustmentmethod` (local): Percent **100000000**, Fixed **100000001**.
- `sprk_costtype` (global): Fee **100000000**, Expense **100000001**, Adjustment **100000002**.

## 5. SpendSnapshotService and the new Adjustment value

`SpendSnapshotService` is a reader and was not changed. It reads `sprk_costtype` into `BillingEventData.CostType` but never uses it: spend is `Sum(e.Amount)` over lines with `sprk_visibilitystate = Invoiced` (`:185`, `:402`, monthly `:455-459`). An Adjustment line stored as Invoiced would therefore be **added** to spend (the amount column is never negative) and disallowed / reduced lines count at the billed amount. Not reachable until line writers exist. Filed: **DEF-004**, GitHub #1923.

## 6. Deviations and decisions

- Entity and column relabels use PUT with `MSCRM.MergeLabels`; both worked first time. No column, option or relationship was deleted or renamed by logical name. No file under `src/dataverse/solutions/SpaarkeMaster/` changed.
- `sprk_regardingrecordtype` on the line is optional (invoice has it ApplicationRequired).
- Money columns copy `sprk_amount` (0 .. 1e9, precision 2). If a later task needs a negative approved amount it widens the range.
- `sprk_linestatus` has no default: legacy rows stay null; the writers set it (ADR-002, no default business rule).
- Finding class check (Step 9.5 item 6): the eventually-consistent-metadata-read hazard is a repeatable pattern in schema scripts that check-then-create from a read. A grep for `Test-AttributeExists` or `Test-EntityExists` over `scripts/*.ps1` and `scripts/*/*.ps1` matches 27 scripts; they were not opened or changed (out of scope). No guard can be mechanised for a live-API timing behaviour; this script's create-refusal handling is the model.
- Quality gates: STANDARD rigor, configuration-only (script + doc + live metadata), no `tests/**`: code-review / adr-check skipped per Step 9.5 skip rule (configuration-only). ADR-002 / ADR-024 / ADR-022 checked by hand: no plugin / calculated column / flow; resolver fields complete; unmanaged solution.

## 7. Deferred lookups created (task 018, 2026-10-15)

Task 018 created `sprk_billingcode` in dev (`scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1`), then `Deploy-EbillingInvoiceLineSchema.ps1` was re-run unchanged against dev. Result: `created : 3  sprk_taskcode, sprk_activitycode, sprk_expensecode`, `skipped : 24`, `deferred: 0`, customizations published, exit 0. So the deferred path works: the script detected the table on the first read (no false deferral from a lagging metadata read) and `Add-Lookup` created all three.

Confirmed by an independent metadata read (`LookupAttributeMetadata`): `sprk_billingevent.sprk_taskcode`, `sprk_activitycode` and `sprk_expensecode` are `Lookup` -> `sprk_billingcode`. A further run: `created : 0`, `skipped : 27`, `deferred: 0`, "nothing changed - no publish". `docs/data-model/sprk_billingevent.md` no longer marks them deferred.

rework: fix-round-1 task 010
