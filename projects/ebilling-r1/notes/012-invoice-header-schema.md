# Task 012 — Invoice header schema on sprk_invoice (notes)

> Run 2026-10-15 against dev (`https://spaarkedev1.crm.dynamics.com`). Script: `scripts/ebilling/Deploy-EbillingInvoiceHeaderSchema.ps1`. Rigor STANDARD. Numbers in section 4 are an interface for tasks 014, 015, 016, 150.

## 1. Step 1 — live state before any write

- 105 attributes on `sprk_invoice`. None of the nine target logical names existed (no same-name column of another type): no escalation.
- Existing and reused: typed `sprk_matter` / `sprk_project`, `sprk_regardingagreement`, the five ADR-024 resolver fields, `sprk_totalamount` (Money, precision 2, 0 .. 1e9, the billed total), `sprk_invoicestatus` (Picklist, left alone).
- `statuscode`: `1 Active` (statecode 0), `2 Inactive` (statecode 1). `statecode` default statuses: 0 -> 1, 1 -> 2.
- **Invoices: 10 at statecode 0 (all statuscode 1), 0 at statecode 1.** The escalation trigger for relabelling value 2 did not fire. The script repeats this count before any write and exits 2 if it is non-zero.
- Targets present: `sprk_workassignment`, `sprk_invoice`. Publisher `Spaarke` option-value prefix 65949. Solution `SpaarkeMaster` 1.2.3.0 (unmanaged).

## 2. Resolver check (escalation trigger 3) — creating the column alone changes nothing

Read: `CoreAncestorResolver.cs` (`CoreAncestorLookups`, `ChildRecordEntities`, `IntermediateRootColumns`, `StampSourceColumns`, both `ApplyStamps` overloads), `CoreAncestorRestamper.cs` (`ChildColumns`, orphan classification), `CoreAncestorStampReconciliationJob.cs`, `OwnedChildWrite.cs` (column map), and every non-resolver caller of `StampAsync` / `DeriveForHostAsync`.

- `ApplyStamps` writes `sprk_regarding{core}` only for a host the caller passes to `StampAsync` / `DeriveForHostAsync`. No caller passes `sprk_invoice` as host (hosts are To Do, Event, Communication, Memo, Signal, Decision Record).
- `sprk_invoice` is **not** a key of `StampSourceColumns`. The restamper, the 5-minute reconcile job and the `OwnedChildWrite` column map all start from that dictionary, so none of them reads or writes a column on `sprk_invoice`; the new column is invisible to them.
- `IntermediateRootColumns["sprk_invoice"]` (line 182) lists only typed `sprk_project` / `sprk_matter`; nothing probes the live column set of an intermediate. Derivation through an invoice is unchanged. The new lookup is empty on all 10 existing rows.
- Conclusion: no behaviour changes today. The planned split holds: task 013 adds `("sprk_regardingworkassignment", "sprk_workassignment")` to `IntermediateRootColumns["sprk_invoice"]` and `INTERMEDIATE_ROOT_COLUMNS` (pinned by `IntermediateRootColumns_MatchTheTypeScriptSide`) in one change, as 010 section 3 says. Until then a record filed under a work-assignment invoice would derive no stamp, which is unreachable because nothing writes the column yet.

## 3. Runs (dev)

| Run | Result |
|---|---|
| `-WhatIf` | 14 "would create", 2 "would relabel", exit 0, no write. |
| Apply | Created 9 columns/relationships + 5 status values, relabelled values 1 and 2, publish OK, exit 0. Its closing statuscode list printed only values 1 and 2: a metadata read straight after `InsertStatusValue` returned the old list (eventual consistency), while the same read seconds later returned all seven. |
| Script fix | The closing report now waits (up to 60 s) until all seven labels are visible and fails if they are not. Create paths already counted an "already exists" refusal as skipped (copied from 010). |
| Re-run (final script) | `created : 0`, `changed : 0`, `skipped : 16`, "nothing changed - no publish", exit 0. Pre-check read showed all seven statuscode values. |

## 4. Verification (metadata read, 2026-10-15)

| Column | Type / target |
|---|---|
| `sprk_regardingworkassignment` | Lookup -> `sprk_workassignment`; relationship `sprk_sprk_workassignment_sprk_invoice_sprk_regardingworkassignment`, delete RemoveLink, assign NoCascade |
| `sprk_feestotal`, `sprk_expensestotal`, `sprk_lineadjustmentstotal`, `sprk_invoiceadjustmentstotal`, `sprk_approvedtotal` | Money, precision 2 (PrecisionSource 0), 0 .. 1,000,000,000, optional. Dataverse also created the system `_base` companions. |
| `sprk_supersedesinvoice` | Lookup -> `sprk_invoice` (self); relationship `sprk_sprk_invoice_sprk_invoice_sprk_supersedesinvoice`, delete RemoveLink |
| `sprk_billingperiodstart`, `sprk_billingperiodend` | DateTime, Format DateOnly, behavior DateOnly (same as `sprk_invoicedate`) |

### statuscode values (needed by tasks 014, 015, 016, 150)

| statuscode | Label | statecode |
|---|---|---|
| **1** | In Review (was "Active"; default of statecode 0) | 0 Active |
| **659490000** | Approved | 0 Active |
| **659490001** | Sent to AP | 0 Active |
| **659490002** | Paid | 1 Inactive |
| **659490003** | Rejected | 1 Inactive |
| **659490004** | Returned to Firm | 1 Inactive |
| **2** | Cancelled (was "Inactive"; default of statecode 1) | 1 Inactive |

No other value exists. The 10 existing invoices still read statecode 0 / statuscode 1 (now labelled In Review). `sprk_invoicestatus` is unchanged. statecode labels remain "Active" / "Inactive".

## 5. Deviations and decisions

- Billing period columns are included as the POML says (FR-21 header has no other home). Brief did not list them.
- Explicit statuscode numbers (659490000+) were chosen over auto-assignment so every environment gets the same numbers, matching the `sprk_todo` precedent. The numbers are listed above for 014 / 015 / 016 / 150; code should reference constants, not literals.
- Default status of statecode 1 stays value 2, now Cancelled: a plain deactivate in the UI lands on Cancelled. The lifecycle service sets the exact value (task 014); consider noting it there.
- Totals have minimum 0 (copying `sprk_totalamount`). An adjustment total is stored as a positive amount; a negative value in any of them is refused by the platform.
- No column, option or relationship was deleted or renamed by logical name (two statuscode display labels changed, as the POML allows). No file under `src/dataverse/solutions/SpaarkeMaster/` changed; `CoreAncestorResolver` and all C# / TS untouched (task 013).
- Finding class check (Step 9.5 item 6): the "read straight after write returns the old list" hazard now has two instances in this repo's schema scripts (010 column reads, 012 statuscode options). Both are handled in their own script. No mechanical guard is possible for live-API timing; 010 section 6 already lists the 27 scripts that share the check-then-create shape.
- Quality gates: STANDARD rigor, configuration-only (script + doc + live metadata), no `tests/**`: code-review / adr-check skipped per Step 9.5 skip rule. ADR-002 checked by hand: no plugin, calculated / rollup / formula column, flow or business rule. ADR-024: the resolver fields already exist on `sprk_invoice`; the work-assignment lookup follows the `CoreAncestorLookups` name. Solution is unmanaged.

rework: fix-round-1 task 012
