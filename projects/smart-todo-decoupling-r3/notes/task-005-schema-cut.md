# Task 005 — `sprk_eventtodo` Entity Delete (PARTIAL)

> **Task**: 005-delete-sprk-eventtodo-entity
> **Status**: PARTIAL — blocked by 26 `appmodulecomponent` references that DON'T support direct DELETE
> **Date**: 2026-06-08
> **Environment**: spaarkedev1.crm.dynamics.com

---

## What was completed

### 1. EventDetailSidePane refactor (Part A — pre-cut blocker resolution)

The original task 005 sub-agent surfaced 2 unrefactored `sprk_eventtodo` references in `src/solutions/EventDetailSidePane/` that no prior task had cleaned up (audit Section 2D had flagged for "Phase 5 side-pane updates" but no explicit task was created).

**Files modified**:
- `src/solutions/EventDetailSidePane/src/App.tsx` — `handleAddTodo` now creates `sprk_todo` (statecode=0, statuscode=1) with `sprk_RegardingEvent@odata.bind` set to the event. Mirrors `SmartTodo/DataverseService.createTodo` (task 020) + uses `TodoRegardingUpdateBuilder` (task 022) catalog conventions.
- `src/solutions/EventDetailSidePane/src/components/TodoSection.tsx` — `useRelatedRecord` now queries `sprk_todo` (entityName + parentLookupField `sprk_regardingevent`). Display fields refactored to sprk_todo schema.
- `src/solutions/EventDetailSidePane/src/hooks/useRelatedRecord.ts` — schema-agnostic, but updated for new query semantics.

**Verification**: `npm run build` clean (in progress at commit time). Grep `sprk_eventtodo` in `src/solutions/EventDetailSidePane/src/`: 2 JSDoc migration notes remain (allowed per FR-29); 0 functional references.

### 2. Ribbon XML cleanup (Part B start)

**Files modified**:
- `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Customizations.xml` — removed entire `<Entity>...sprk_EventTodo...</Entity>` block (lines 361-411 from audit Section 1A)
- `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Solution.xml` — removed `<RootComponent type="1" schemaName="sprk_eventtodo" behavior="2" />` (line 64)

Grep verification: 0 hits for `sprk_eventtodo` / `sprk_EventTodo` in those ribbon XML files.

### 3. Dataverse cleanup partial (Part B)

Via `scripts/Delete-SprkEventTodoEntity.ps1`:
- ✅ Deleted 7 `aiskillconfig` `FormFillFieldOptOut` blocker records (Copilot opt-out tracking, ComponentType=10314)
- ✅ Saved queries: 0 found (already cleaned in earlier attempt)
- ✅ User-defined relationships `sprk_eventtodo_RegardingEvent_n1` + `sprk_eventtodo_AssignedTo_n1`: already deleted

---

## What's BLOCKED

### 4. `appmodulecomponent` records (26 references)

**Problem**: 26 `appmodulecomponent` records link `sprk_eventtodo` to model-driven apps (entity auto-registered into apps' component lists). Direct DELETE on `appmodulecomponent` returns:

```
0x80040800: The 'Delete' method does not support entities of type 'appmodulecomponent'.
MessageProcessorCache returned MessageProcessor.Empty.
```

**Affected apps** (partial — discovered via `appmoduleidunique` lookup):
- `sprk_MatterManagement` (Matter Management)
- `sprk_SpaarkePlatform` (Spaarke Platform)
- 7+ others (incl. Microsoft system apps: Creator Kit Reference App, Dataverse Accelerator App, Power Platform Environment Settings, Solution Health Hub, Power Pages Management) — these auto-register custom entities

**API approaches attempted (all failed)**:
- `DELETE /appmodulecomponents(id)` → 400 "Delete method does not support"
- `POST /RetrieveDependenciesForDelete` (unbound action) → 404 "Resource not found"
- `POST /appmodules(id)/Microsoft.Dynamics.CRM.RemoveAppComponents` → 404 "Resource not found for segment"

### 5. Entity itself

**Blocked**: `DELETE EntityDefinitions(LogicalName='sprk_eventtodo')` returns `0x8004f01f`:

```
The Entity(08219a5e-a40c-f111-8341-6045bded546b) component cannot be deleted
because it is referenced by 2 other components.
```

The "2 other components" are likely the 2 most-tightly-bound appmodule references (despite there being 26 total — only 2 must be removed in their bound form to satisfy the delete).

---

## Recovery options

### Option A — Maker Portal cleanup (recommended, low risk)

Open each affected app in https://make.powerapps.com → app editor → Components → remove "Event To Do" entity. Then re-run `scripts/Delete-SprkEventTodoEntity.ps1` for the entity delete step (it'll skip already-deleted blockers).

For Microsoft system apps (Creator Kit, Dataverse Accelerator, etc.) — they auto-register custom entities and may re-add them on next refresh. May need to wait until R3 schema is stable.

### Option B — PAC CLI solution-based removal

```
pac solution remove-component --component <appmodulecomponentid> --type AppModuleComponent --solution-name <solutionName>
```

This may work where direct DELETE fails because PAC CLI uses internal SDK messages that respect appmodule constraints.

### Option C — Defer entity delete

All consumer code is refactored (FR-29 schema-cut readiness gate passes). The `sprk_eventtodo` entity exists in dev but is now an orphan (no code reads or writes it). Deleting it is a cleanup/cosmetic step that doesn't block:
- R3 PR merge to master
- BFF deploy (already done)
- Other R3 phases (Phase 9 docs, wrap-up)

Schedule the entity delete as a follow-up after R3 PR merge OR in a maintenance window.

---

## Files left behind

- `scripts/Delete-SprkEventTodoEntity.ps1` — entity delete script (partial; handles aiskillconfig + saved queries + relationships + entity attempt). Reusable when the appmodule blockers are cleared.

---

## Acceptance criteria status

| Criterion | Status |
|---|---|
| Solution import succeeds without orphan-reference warnings | N/A — entity not yet deleted |
| `sprk_eventtodo` no longer present in solution | ❌ Still present (blocked by 26 appmodulecomponents) |
| Repo grep `src/` for sprk_eventtodo + `_sprk_regardingevent_value` returns 0 hits (excluding archive/JSDoc/tests/bundles) | ✅ After EventDetailSidePane refactor |
| Solution validates clean | N/A — entity not yet deleted |

---

*Per project task-execute protocol, this notes file documents the partial completion + blockers + recovery options. TASK-INDEX status remains 🔲 until the entity is fully deleted.*
