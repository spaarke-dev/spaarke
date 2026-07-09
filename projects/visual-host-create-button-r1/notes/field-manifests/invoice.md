# Field Manifest — Invoice (`sprk_invoice`)

> Phase 0 validation against **live Dataverse schema** (spaarkedev1), 2026-07-08.

## Enter Info manifest (spec FR-16) — ✅ ALL CONFIRMED

| Field (logical) | Owner manifest | Live schema | Match? |
|---|---|---|---|
| `sprk_invoicenumber` | Text | `NVARCHAR(100)` | ✅ |
| `sprk_name` | Text | `NVARCHAR(850) NOT NULL` | ✅ — **required**; service must supply a value at create (e.g., derive from invoice number, or collect explicitly in Enter Info as the manifest already lists it) |
| `sprk_description` | Text | `MULTILINE TEXT` | ✅ (control should be multiline, not single-line, to match schema) |
| `sprk_vendororg` | Lookup → `sprk_organization` | Lookup → `sprk_organization` | ✅ target table confirmed. Relationship schema name `sprk_sprk_organization_sprk_invoice_sprk_vendororg` (owner-provided) was **not independently re-verified** via relationship metadata — `describe()` confirms the target table only. Not blocking: task 030's nav-prop discovery (`findNavProp` against `sprk_organization`) resolves the actual nav-prop programmatically, same pattern as `workAssignmentService.ts` — no hardcoded relationship name needed. |
| `sprk_invoicedate` | Date, default today | `DATE ONLY` | ✅ |

No additional schema-required fields beyond the manifest (only other `NOT NULL` field on the entity is `sprk_extractionstatus`, which has a system default and is unrelated to wizard input).

## Resolver fields (ADR-024, post-#549) — ✅ ALL PRESENT

| Field | Type | Present? |
|---|---|---|
| `sprk_regardingrecordtype` | Lookup → `sprk_recordtype_ref`, **NOT NULL** | ✅ — note the NOT NULL constraint: `applyResolverFields` must successfully resolve the record type (confirmed working — `sprk_recordtype_ref` rows for Matter/Project exist and are fully populated) |
| `sprk_regardingrecordid` | Text(100) | ✅ |
| `sprk_regardingrecordname` | Text(100) | ✅ |
| `sprk_regardingrecordurl` | URL(250) | ✅ |
| `sprk_regardingrecordnumber` | Text(1000) | ✅ — already present, confirms spec.md's "Invoice already has it" |

Entity-specific lookups confirmed: `sprk_matter`, `sprk_project` (Matter + Project targets per spec FR-09/design §5.6).

## Document dual-bind (FR-12) — ✅ SUPPORTED

`sprk_document.sprk_invoice` lookup confirmed present (targets `sprk_invoice`). Invoice dual-bind (host + Invoice) is schema-clear — unlike Event (see `event.md`), Invoice already has its child lookup on `sprk_document`.

## Verdict

**No blocking issues for Invoice.** Task 030 (`CreateInvoiceWizard` + `invoiceService`) is schema-clear and can proceed once owner signs off this manifest.
