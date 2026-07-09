# Field Manifest — Event (`sprk_event`)

> Phase 0 validation against **live Dataverse schema** (spaarkedev1), 2026-07-08.
> Event has no new Enter Info manifest (spec FR-16: "uses the fields already collected by the existing `CreateEventStep`"). This note covers the **resolver + document dual-bind validation** only.

## Resolver fields (ADR-024, post-#549) — ✅ ALL PRESENT

| Field | Type | Present? |
|---|---|---|
| `sprk_regardingrecordtype` | Lookup → `sprk_recordtype_ref` | ✅ |
| `sprk_regardingrecordid` | Text(100) | ✅ |
| `sprk_regardingrecordname` | Text(1000) | ✅ |
| `sprk_regardingrecordurl` | URL(2000) | ✅ |
| `sprk_regardingrecordnumber` | Text(100) | ✅ — **already present; no schema delta needed** (resolves spec.md Unresolved Question) |

Entity-specific lookups confirmed (8, matches ADR-024 Examples table): `sprk_regardingmatter`, `sprk_regardingproject`, `sprk_regardinginvoice`, `sprk_regardingaccount`, `sprk_regardingcontact`, `sprk_regardingworkassignment`, `sprk_regardingbudget`, `sprk_regardinganalysis`. Extra denormalized field `sprk_regardingrecordtypelogicalname` (text) exists but is outside the ADR-024 5-field contract — not written by `applyResolverFields`, harmless (no NOT NULL constraint).

**Verdict**: task 014 (`eventService` → `applyResolverFields` migration) is schema-clear. Matter/Project catalog rows in `sprk_recordtype_ref` are fully populated (`sprk_regardingrecordnumberfield` + `sprk_recorddisplaynamefield` both set) — the 5-field write will succeed for both host types.

## 🔴 BLOCKING — Document dual-bind (FR-12 / Success Criterion #5) — NOT SUPPORTED BY SCHEMA

**Design assumption (design.md §5.8, §11 R3; spec.md Assumptions; task 013 background) is FALSE**: *"Both child lookups already exist on `sprk_document`: `sprk_Event` (used today by `CreateEventWizard`) and `sprk_invoice`."*

**Live schema fact**: `sprk_document` has **no lookup field referencing `sprk_event`** (confirmed via `describe('tables/sprk_document')` — full field list has `sprk_invoice`, `sprk_workassignment`, `sprk_matter`, `sprk_project`, `sprk_relatedmatter`, `sprk_relatedproject`, `sprk_relatedvendororg`, `sprk_parentdocument`, but **nothing for Event**). This matches ADR-024's own polymorphic-association table, which lists `sprk_document`'s targets as **Matter, Project, Invoice, Work Assignment only** — Event was never in that set.

**This is also a pre-existing production bug, not something this project introduces.** `CreateEventWizard.tsx:254-257` (today's shipped code) already calls:
```ts
entityService.createDocumentRecords('sprk_events', eventId, 'sprk_Event', uploadResult.uploadedFiles, {...})
```
passing the literal nav-prop name `'sprk_Event'` — which does not exist on `sprk_document`. This call is inside a `try/catch` that pushes failures to a non-fatal `warnings` array (the Event record itself still gets created), so the failure is silent today: uploading a file while creating an Event currently does **not** actually bind the document to the Event (only the SPE upload + `sprk_document` creation succeed; the Event-specific `@odata.bind` write fails and is swallowed as a warning).

### Options for owner decision

| Path | Description | Impact |
|---|---|---|
| **A — Add schema** | Add a `sprk_event` lookup column to `sprk_document` (via `dataverse-create-schema` skill), matching the `sprk_invoice`/`sprk_workassignment` pattern. Fixes both this project's FR-12 for Event *and* the pre-existing production bug. | Schema addition (additive, non-breaking). Recommended — restores the design's original intent and fixes a latent bug for free. |
| **B — Scope-cut Event dual-bind** | Drop Event from FR-12 / Success Criterion #5. Event's Add Files step uploads + creates `sprk_document` bound to the **host only** (single-bind), same as it silently does today (minus the failed Event bind attempt — remove the doomed call instead of leaving it to fail). Invoice keeps full dual-bind (its lookup exists). | No schema change. Narrows scope; the pre-existing bug is fixed by *removing* the broken call rather than making it work. |
| **C — Defer** | Leave Event's file step exactly as-is (including the latent bug) for this project; track as a separate defer/issue. | Not recommended — ships known-broken behavior knowingly, contradicts the Phase-0 gate's purpose. |

**Recommendation**: Path A. It's a single additive lookup column (same shape as the existing `sprk_invoice`/`sprk_workassignment` columns), fixes a real production bug, and preserves the spec's stated FR-12/Success-Criterion-#5 scope for Event without a design rewrite.

**Blocks**: task 013 (`EntityCreationService` multi-bind — background text corrected), task 015 (Event dual-bind smoke test).

## ✅ Owner Decision (2026-07-08): Path A

Owner approved adding the `sprk_event` lookup column to `sprk_document`. Task 002 (expanded scope) now creates this column. Task 015 additionally fixes the hardcoded `'sprk_Event'` nav-prop string in `CreateEventWizard.tsx:254-257` (previously pointing at a nonexistent column) once the real column name is known.
