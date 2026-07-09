# Field Manifest — KPI Assessment (`sprk_kpiassessment`)

> Phase 0 validation against **live Dataverse schema** (spaarkedev1), 2026-07-08.
>
> **⚠️ SUPERSEDED for wizard-build purposes (2026-07-08, owner decision post-Phase-D)**: the third Visual Host wizard now targets **`sprk_reportcard`**, not `sprk_kpiassessment` directly — see `reportcard.md`. `sprk_kpiassessment` records are line-items belonging to a parent `sprk_reportcard`; creating KPI line-items is a separate, later capability. The schema facts below (resolver fields, manifest validation) remain accurate for that future work — kept for reference, not currently being built against.

## Enter Info manifest (spec FR-16) — ✅ ALL CONFIRMED

| Field (logical) | Owner manifest | Live schema | Match? |
|---|---|---|---|
| `sprk_kpiname` | Text | `NVARCHAR(850) NOT NULL` | ✅ — **required** |
| `sprk_performancearea` | Choice | Choice: Guideline Compliance / Budget Compliance / Outcomes Achievement | ✅ |
| `sprk_kpigradescore` | Choice | Choice: A+, A, B+, B, C+, C, D+, D, F, No Grade | ✅ |
| `sprk_assessmentcriteria` | Multiline text | `MULTILINE TEXT` | ✅ |
| `sprk_assessmentnotes` | Multiline text | `MULTILINE TEXT` | ✅ |

No additional schema-required fields beyond the manifest.

## Resolver fields (ADR-024, post-#549) — ⚠️ 4 of 5 PRESENT

| Field | Type | Present? |
|---|---|---|
| `sprk_regardingrecordtype` | Lookup → `sprk_recordtype_ref` | ✅ |
| `sprk_regardingrecordid` | Text(100) | ✅ |
| `sprk_regardingrecordname` | Text(1000) | ✅ |
| `sprk_regardingrecordnumber` | Text(100) | ✅ — already present; **no schema delta needed** (resolves spec.md Unresolved Question for KPI) |
| `sprk_regardingrecordurl` | URL | ❌ **MISSING** |

Entity-specific lookups confirmed: `sprk_matter`, `sprk_project` (Matter + Project targets per spec FR-09/design §5.6, owner-created 2026-07-05).

## 🔴 BLOCKING — `sprk_regardingrecordurl` does not exist on `sprk_kpiassessment`

`applyResolverFields()` (`PolymorphicResolverService.ts:499`) **unconditionally** executes:
```ts
entity['sprk_regardingrecordurl'] = buildRecordUrl(parentEntityLogicalName, cleanRecordId);
```
— there is no guard skipping this write when the target attribute doesn't exist (unlike the record-number/display-name fields, which do have graceful-blank guards per NFR-06). If `kpiAssessmentService.createKpiAssessment` calls `applyResolverFields` unmodified against a `sprk_kpiassessment` payload, the subsequent `createRecord('sprk_kpiassessment', entity)` Web API call will include an `sprk_regardingrecordurl` property Dataverse doesn't recognize — this **will fail the create call** (Dataverse Web API rejects unknown attributes on the entity payload).

### Options for owner decision

| Path | Description | Impact |
|---|---|---|
| **A — Add schema** | Add `sprk_regardingrecordurl` (URL) to `sprk_kpiassessment`, matching Event/Invoice and the ADR-024 5-field contract. | Schema addition (additive). **Recommended** — keeps KPI consistent with every other resolver-pattern entity; no special-casing in shared service code. |
| **B — Service-level workaround** | In `kpiAssessmentService.createKpiAssessment`, call `applyResolverFields` normally, then `delete entity['sprk_regardingrecordurl']` before `createRecord`. | No schema change, but is a project-scoped deviation from the "MUST use the shared `PolymorphicResolverService`... without duplicating/patching around it" spirit of ADR-024 — narrow but real; would need a one-line comment citing this manifest note as rationale (CLAUDE.md §6.5 Path A style exception, scoped to this one service). |
| **C — Modify `applyResolverFields`** | Make the URL write conditionally guarded like record-number/display-name (skip if target lacks the field — but the service has no schema-introspection to know that upfront; would need a caller-supplied flag). | Changes shared service behavior used by 5 entities (`sprk_event`, `sprk_document`, `sprk_workassignment`, `sprk_communication`, `sprk_memo`) — out of proportion for a single-entity gap; not recommended. |

**Recommendation**: Path A (same reasoning as the Event finding in `event.md`) — a single additive URL column is the smallest, most consistent fix and avoids special-casing the shared resolver service.

**Blocks**: task 040 (`CreateKPIAssessmentWizard` + `kpiAssessmentService`) — building against `applyResolverFields` unmodified will fail at first create attempt until this is resolved.

## ✅ Owner Decision (2026-07-08): Path A

Owner approved adding `sprk_regardingrecordurl` (URL) to `sprk_kpiassessment`. Task 002 (expanded scope) now creates this column, matching the definition on `sprk_event.sprk_regardingrecordurl`.

## Document dual-bind — N/A (confirmed out of scope)

KPI Assessment has no Add Files step (owner decision, spec FR-05/FR-12 note) — no `sprk_document` lookup to `sprk_kpiassessment` exists, and none is needed.

## Verdict

**Owner sign-off required** on the `sprk_regardingrecordurl` gap (Path A/B/C above) before task 040 build. Enter Info fields themselves are fully schema-clear.
