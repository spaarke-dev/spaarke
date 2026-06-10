# `sprk_eventtodo` + Legacy Event-Todo Fields Reference Audit

> **Task**: 001-audit-eventtodo-references
> **Status**: Complete
> **Date**: 2026-06-07
> **Author**: smart-todo-decoupling-r3 / task-execute
> **Purpose**: Inventory every reference to `sprk_eventtodo` (entity + relationship), the four `sprk_event` to-do fields (`sprk_todoflag`, `sprk_todostatus`, `sprk_todocolumn`, `sprk_todopinned`), and the legacy two-entity helper functions (`loadTodoExtension`, `saveTodoExtensionFields`, `deactivateTodoExtension`, `_sprk_regardingevent_value`) — to gate Phase 1 schema cuts and downstream consumer phases per spec **FR-29** + **OS-1**.

---

## Scope & Method

- Tool: Grep (ripgrep) tool — full repo, case-insensitive
- Search roots: `src/`, `tests/`, `docs/`, `infrastructure/`
- Excluded: `.claude/archive/`, this project's own `projects/smart-todo-decoupling-r3/` (spec/design/plan/tasks legitimately mention these names), unrelated `projects/*/` planning docs (events-smart-todo-kanban, x-home-corporate-workspace-r1, events-workspace-apps-UX-r1) — those are project artifacts, not consumer code
- Patterns searched:
  1. `sprk_eventtodo` (entity logical name)
  2. `sprk_eventtodos` (plural / OData collection)
  3. `sprk_eventtodoid` (primary key column)
  4. `_sprk_eventtodo_value` (lookup-FK column form)
  5. `sprk_eventtodo_RegardingEvent_n1` (relationship)
  6. `sprk_todoflag`, `sprk_todostatus`, `sprk_todocolumn`, `sprk_todopinned`
  7. `loadTodoExtension`, `saveTodoExtensionFields`, `deactivateTodoExtension`
  8. `_sprk_regardingevent_value`

PCF bundle.js files (`AssociationResolver/bundle.js`, `SpeDocumentViewer/bundle.js`) contain transient compiled matches that will disappear on rebuild — flagged but not actionable removal targets.

---

## Section 1 — Schema (Solution / Ribbon XML / Dataverse Metadata)

These are the **hard removal targets for Phase 1 tasks 002–005**. Solution import fails until these are removed (or the to-be-deleted artifacts are removed *first* in the source environment).

### 1A. Ribbon solution XML referencing the entity (tasks 005)

| File | Line | Snippet |
|---|---|---|
| `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Solution.xml` | 64 | `<RootComponent type="1" schemaName="sprk_eventtodo" behavior="2" />` |
| `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Customizations.xml` | 362 | `<Name LocalizedName="Event Todo" OriginalName="Event Todo">sprk_EventTodo</Name>` |
| `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Customizations.xml` | 364 | `<entity Name="sprk_EventTodo" unmodified="1">` |
| `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Customizations.xml` | 370 | `<CustomAction Id="sprk.ThemeMenu.EventTodo.CustomAction" Location="Mscrm.HomepageGrid.sprk_eventtodo.MainTab.Actions.Controls._children" Sequence="900">` |
| `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Customizations.xml` | 385 | `<CustomAction Id="sprk.ThemeMenu.EventTodo.Form.CustomAction" Location="Mscrm.Form.sprk_eventtodo.MainTab.Actions.Controls._children" Sequence="900">` |
| `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Customizations.xml` | 372–406 | All command/menu/action IDs (`sprk.ThemeMenu.EventTodo.*`) and the entire `<Entity>` block (lines 361–411) for the Event Todo theme menu |

**Action**: Phase 1 task 005 must remove the entire `<Entity>...sprk_EventTodo...</Entity>` block from `Customizations.xml` and the corresponding `RootComponent` line from `Solution.xml`. Re-pack + re-import the ribbon solution before deleting the entity from the target environment (or the export will re-fail with cryptic dependency errors on next round-trip).

**Removal order**: this ribbon customization must be removed **before** entity delete (relationship → entity → ribbon overlay is the safe order, but practically: clean both source XML and target environment dependencies in one batched step).

### 1B. Schema documentation referencing the four removed `sprk_event` fields

| File | Line | Snippet |
|---|---|---|
| `docs/data-model/sprk_event-related-tables.md` | 130 | `\| Event \| sprk_event \| sprk_todoflag \| sprk_ToDoFlag \| To Do Flag \| Two options \| ...` |
| `docs/data-model/sprk_event-related-tables.md` | 131 | `\| Event \| sprk_event \| sprk_todoflagname \| ... \| Virtual \| ...` |
| `docs/data-model/sprk_event-related-tables.md` | 135 | `\| Event \| sprk_event \| sprk_todostatus \| sprk_ToDoStatus \| To Do Status \| Choice \| ...` |
| `docs/data-model/sprk_event-related-tables.md` | 136 | `\| Event \| sprk_event \| sprk_todostatusname \| ... \| Virtual \| ...` |

**Action**: docs update belongs to Phase 9 (Docs) — but row removal of `sprk_todoflag` + `sprk_todostatus` (+ their virtual `*name` sidekicks) + `sprk_todocolumn` + `sprk_todopinned` from this data-model doc is **strongly recommended in the same PR as task 004** to avoid the doc lying about Dataverse reality.

---

## Section 2 — TypeScript / TSX (UI Surfaces + Mocks)

Removal target for **Phase 2 (Kanban hoist)**, **Phase 3 (SmartTodo Code Page repoint)**, **Phase 4 (CreateTodo wizard rewrite)**, **Phase 5 (LegalWorkspace + Outlook add-in updates)**.

### 2A. Shared library (`@spaarke/ui-components`) — touched by tasks 010, 011, 030

| File | Line | Snippet |
|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/types.ts` | 6 | `*   - sprk_eventtodo: to-do extension fields (notes, completed, statuscode)` |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/types.ts` | 22 | `sprk_todostatus?: number;` |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/types.ts` | 23 | `sprk_todocolumn?: number;` |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/types.ts` | 24 | `sprk_todopinned?: boolean;` |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/types.ts` | 47 | `'sprk_todostatus',` |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/types.ts` | 48 | `'sprk_todocolumn',` |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/types.ts` | 49 | `'sprk_todopinned',` |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/types.ts` | 58 | `// sprk_eventtodo fields (related entity — to-do extension)` |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/types.ts` | 62 | `sprk_eventtodoid: string;` |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/types.ts` | 75 | `/** OData $select fields for the sprk_eventtodo query. */` |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/types.ts` | 77 | `'sprk_eventtodoid',` |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/types.ts` | 95 | `sprk_todoflag?: boolean;` |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/TodoDetail.tsx` | 7, 13, 279, 285, 287, 342, 404, 547, 551, 602, 635, 637, 646, 654, 823 | `sprk_eventtodo` references in JSDoc, props (`onSaveTodoExtFields`, `onDeactivateTodoExt`), state (`todoExtension?.sprk_eventtodoid`), and save-handler branches |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/TodoDetail.tsx` | 289, 589 | `sprk_todoflag` Remove-from-To-Do path |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/todoService.ts` | 7, 37, 44 | `sprk_todoflag = true` + `sprk_todostatus = 0` (legacy event-todo creation) |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/TodoWizardDialog.tsx` | 5 | `* A To Do is a sprk_event record with sprk_todoflag=true.` |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/formTypes.ts` | 5 | `* A To Do is a sprk_event record with sprk_todoflag=true.` |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/workAssignmentService.ts` | 566 | `entity['sprk_todoflag'] = true;` |

### 2B. SmartTodo Code Page — touched by tasks 020, 021

| File | Line | Snippet |
|---|---|---|
| `src/solutions/SmartTodo/README.md` | 18 | `Right panel — TodoDetailPanel (collapsible, loads sprk_event + sprk_eventtodo)` |
| `src/solutions/SmartTodo/src/types/entities.ts` | 46 | `sprk_todoflag: boolean;` |
| `src/solutions/SmartTodo/src/types/entities.ts` | 47 | `sprk_todostatus?: number;` |
| `src/solutions/SmartTodo/src/types/entities.ts` | 55 | `sprk_todocolumn?: number;` |
| `src/solutions/SmartTodo/src/types/entities.ts` | 56 | `sprk_todopinned?: boolean;` |
| `src/solutions/SmartTodo/src/services/todoDetailService.ts` | 9, 72, 76, 79, 86, 90, 133, 137, 139, 149, 159, 163, 168, 172 | Full body of `loadTodoExtension`, `saveTodoExtensionFields`, `deactivateTodoExtension` (filter on `_sprk_regardingevent_value`) — delete file or strip these exports |
| `src/solutions/SmartTodo/src/services/todoDetailService.ts` | 215, 219, 222 | `Remove from To Do (set sprk_todoflag = false)` — `saveTodoFields(eventId, { sprk_todoflag: false })` |
| `src/solutions/SmartTodo/src/services/queryHelpers.ts` | 185, 186, 475, 476, 483, 484 | `sprk_todoflag`, `sprk_todostatus`, `sprk_todocolumn`, `sprk_todopinned` in OData select lists |
| `src/solutions/SmartTodo/src/services/queryHelpers.ts` | 500, 512 | `_ownerid_value eq ${userId} and sprk_todoflag eq true and sprk_todostatus eq …` filters |
| `src/solutions/SmartTodo/src/services/DataverseService.ts` | 38, 425, 427, 441, 443, 468, 485, 487, 490, 500, 504, 633, 648, 672, 763 | All write paths setting `sprk_todoflag` / `sprk_todostatus` / `sprk_todocolumn` / `sprk_todopinned` |
| `src/solutions/SmartTodo/src/hooks/useTodoItems.ts` | 4 | `Queries sprk_event records where sprk_todoflag=true AND sprk_todostatus != Dismissed.` |
| `src/solutions/SmartTodo/src/hooks/useKanbanColumns.ts` | 6, 97, 105, 106, 176, 200, 290, 325, 329 | Heavy use of `sprk_todopinned` / `sprk_todocolumn` for column assignment |
| `src/solutions/SmartTodo/src/context/TodoContext.tsx` | 162, 168, 198 | `sprk_todostatus` state transitions |
| `src/solutions/SmartTodo/src/components/SmartToDo.tsx` | 367, 415 | `sprk_todostatus` + `sprk_todoflag: true` (create + status override) |
| `src/solutions/SmartTodo/src/components/KanbanCard.tsx` | 245, 246 | `event.sprk_todostatus === 100000001` + `event.sprk_todopinned === true` |
| `src/solutions/SmartTodo/src/components/TodoItem.tsx` | 28, 385 | Checkbox toggles `sprk_todostatus` Open ↔ Completed |
| `src/solutions/SmartTodo/src/components/TodoDetailPanel.tsx` | 3, 38, 40, 41, 145, 208, 219, 234, 250, 254, 258, 261 | Two-entity TodoDetailPanel — imports all three legacy helpers; sets `sprk_todostatus` + deactivates `sprk_eventtodo` in same handler |

### 2C. LegalWorkspace Code Page (mirror of SmartTodo) — touched by Phase 2 hoist + Phase 3 repoint (tasks 020, 040)

| File | Line | Snippet |
|---|---|---|
| `src/solutions/LegalWorkspace/src/types/entities.ts` | 46, 47, 55, 56 | Same `IEvent` shape — `sprk_todoflag`, `sprk_todostatus`, `sprk_todocolumn`, `sprk_todopinned` |
| `src/solutions/LegalWorkspace/src/services/queryHelpers.ts` | 209, 210, 497, 498, 505, 506, 522, 534 | Same select arrays + `sprk_todoflag eq true and sprk_todostatus …` filters |
| `src/solutions/LegalWorkspace/src/services/DataverseService.ts` | 40, 465–467, 481–483, 508, 525, 527, 530, 540, 544, 673, 688, 712, 803 | All toggle/create/column-update paths |
| `src/solutions/LegalWorkspace/src/hooks/useTodoItems.ts` | 4 | `Queries sprk_event records where sprk_todoflag=true …` |
| `src/solutions/LegalWorkspace/src/hooks/useParallelDataLoad.ts` | 159 | `Fetches sprk_event where sprk_todoflag=true AND sprk_todostatus!=Dismissed.` |
| `src/solutions/LegalWorkspace/src/hooks/useKanbanColumns.ts` | 6, 97, 105, 106, 176, 200, 290, 325, 329 | Pinned/column logic — duplicate of SmartTodo hook |
| `src/solutions/LegalWorkspace/src/contexts/FeedTodoSyncContext.tsx` | 126, 128, 263, 266 | `initFlags`/`flagMap` keyed on `sprk_eventid → sprk_todoflag` (FR-14 changes this contract to todo-id) |
| `src/solutions/LegalWorkspace/src/components/SmartToDo/SmartToDo.tsx` | 365, 412, 413 | Create + status override |
| `src/solutions/LegalWorkspace/src/components/SmartToDo/KanbanCard.tsx` | 131, 132 | `event.sprk_todostatus === 100000001` + `isPinned = event.sprk_todopinned === true` |
| `src/solutions/LegalWorkspace/src/components/QuickSummary/quickSummaryConfig.ts` | 114, 118 | Filter strings: `sprk_todoflag eq true and sprk_todostatus ne 100000002 …` |
| `src/solutions/LegalWorkspace/src/components/CreateTodo/todoService.ts` | 6, 7, 39, 40 | Wizard creates `sprk_event` with `sprk_todoflag = true`, `sprk_todostatus = 0` |
| `src/solutions/LegalWorkspace/src/components/CreateTodo/formTypes.ts` | 5 | `A To Do is a sprk_event record with sprk_todoflag=true.` |
| `src/solutions/LegalWorkspace/src/components/CreateTodo/TodoWizardDialog.tsx` | 5 | `A To Do is a sprk_event record with sprk_todoflag=true.` |
| `src/solutions/LegalWorkspace/src/components/CreateWorkAssignment/workAssignmentService.ts` | 531 | `entity['sprk_todoflag'] = true;` |

### 2D. Other solutions

| File | Line | Snippet | Phase |
|---|---|---|---|
| `src/solutions/TodoDetailSidePane/src/App.tsx` | 3, 6, 22, 24, 25, 121, 178, 181, 193, 196, 236, 239 | Full two-entity load + save + deactivate + Remove-from-To-Do path; imports all three legacy helpers | Phase 2/3 — retire-or-refactor (per spec A-2) |
| `src/solutions/TodoDetailSidePane/src/services/todoService.ts` | 6, 10, 68, 72, 75, 84, 86, 132, 139, 141, 151, 161, 165, 170, 174 | Full definitions of `loadTodoExtension`, `saveTodoExtensionFields`, `deactivateTodoExtension`; filter on `_sprk_regardingevent_value`; deactivate via `setRecordState("sprk_eventtodos", …)` | Phase 2/3 — delete with TodoDetailSidePane retire |
| `src/solutions/EventDetailSidePane/src/components/TodoSection.tsx` | 5, 9, 141, 145 | `Queries sprk_eventtodo by _sprk_regardingevent_value` + `entityName: "sprk_eventtodo"` + `$select=sprk_eventtodoid,sprk_name,sprk_duedate,statecode,statuscode,...` | Phase 5 (LegalWorkspace + side-pane updates) — refactor section to query `sprk_todo?$filter=_sprk_regardingevent_value eq …` (the `sprk_regardingevent` lookup now lives on `sprk_todo`) |
| `src/solutions/EventDetailSidePane/src/App.tsx` | 641, 652 | `+To Do` button creates `sprk_eventtodo` record with `sprk_RegardingEvent@odata.bind` | Phase 5 — replace with `sprk_todo` create through the wizard or direct create |
| `src/solutions/EventDetailSidePane/src/hooks/useRelatedRecord.ts` | 21 | JSDoc example: `Entity logical name (e.g., "sprk_memo", "sprk_eventtodo")` | Phase 5 — comment-only update (no behavior change; replace example with `sprk_todo`) |
| `src/solutions/EventDetailSidePane/src/components/MemoSection.tsx` | 5 | `Queries sprk_memo by _sprk_regardingevent_value.` — **NOT a todo reference**, included only because of the pattern hit on `_sprk_regardingevent_value` | **needs human review** — confirm `_sprk_regardingevent_value` on `sprk_memo` survives Phase 1 (it should; only the four `sprk_event.sprk_todo*` fields are being removed, not the inverse `sprk_memo` lookups) |
| `src/solutions/DailyBriefing/src/hooks/useInlineTodoCreate.ts` | 5, 106, 107 | `// Unlike FeedTodoSyncContext (which toggles existing sprk_event.sprk_todoflag),` + create `sprk_event { sprk_todoflag: true, sprk_todostatus: 100000000 }` | Phase 3 / 4 — repoint at `sprk_todo` |

### 2E. external-spa (React app under `src/client/external-spa/`)

| File | Line | Snippet |
|---|---|---|
| `src/client/external-spa/src/pages/WorkspaceHomePage.tsx` | 736 | `$select: 'sprk_eventid,sprk_name,sprk_duedate,sprk_todoflag,_sprk_projectid_value,createdon'` |
| `src/client/external-spa/src/mocks/mock-service.ts` | 73 | `sprk_todoflag: body.sprk_todoflag ?? false` |
| `src/client/external-spa/src/mocks/mock-data.ts` | 90, 99, 108, 117, 128 | `sprk_todoflag: false / true` mock records |
| `src/client/external-spa/src/components/SmartTodo.tsx` | 4, 559, 592, 593, 667 | `Displays tasks (sprk_event records with sprk_todoflag=true)` + filter `_sprk_projectid_value eq '${projectId}' and sprk_todoflag eq true` |
| `src/client/external-spa/src/components/EventsCalendar.tsx` | 300, 380 | `event.sprk_todoflag` icon + `sprk_todoflag: false` create payload |
| `src/client/external-spa/src/api/web-api-client.ts` | 95, 362, 464, 484 | `sprk_todoflag?: boolean / null` interface fields + `$select` includes `sprk_todoflag` |

**needs human review**: `external-spa` is the external/portal SPA. Decoupling its data model from `sprk_event.sprk_todoflag` is in-scope per FR-29, but the wider change (point at `sprk_todo`) may be out of scope for R3 if external-spa's consumer is currently event-feed-only. **Recommend**: schedule external-spa repoint as a Phase-3/4 follow-up task — add a TODO comment in the existing tasks listing this as a downstream consumer.

### 2F. PCF compiled bundles (transient — rebuild artefacts)

| File | Line | Notes |
|---|---|---|
| `src/client/pcf/AssociationResolver/Solution/Controls/sprk_Spaarke.Controls.AssociationResolver/bundle.js` | 2 | `sprk_eventtodo` / `sprk_todopinned` / `sprk_todoflag` in bundled string (one or more PCFs serialize entity metadata into the bundle) |
| `src/client/pcf/SpeDocumentViewer/solution/src/Controls/Spaarke.SpeDocumentViewer/bundle.js` | 2000–3090 | Same — `sprk_todoflag`, `sprk_todopinned`, `sprk_eventtodo` |

**Action**: not a removal target. These regenerate from source on `npm run build:prod`. After source-level removal, rebuild + re-deploy PCFs (existing PCF deployment flow). **Verify** by re-grepping `bundle.js` post-rebuild during task 085 sweep — if anything remains, it indicates a source-level reference still exists.

---

## Section 3 — C# / BFF / Plugins / Tests

Removal target for **Phase 7 (plugin work)** and the BFF-side workspace TodoGenerationService (which is a separate sprk_event-based "system to-do generation" feature). **Important nuance**: `TodoGenerationService.cs` is the X-Home-Corporate-Workspace project's overdue-event-flagging job — it sets `sprk_todoflag=true` on existing `sprk_event` rows. R3's spec FR-29 explicitly lists this as legacy and to be removed. The BFF service must be either deleted or rewritten to create `sprk_todo` rows instead.

### 3A. BFF API (`src/server/api/Sprk.Bff.Api/`) — Phase 7

| File | Line | Snippet |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Workspace/TodoGenerationService.cs` | 91, 106, 107, 112, 113, 121, 124, 296, 311, 314, 652, 665, 677, 681, 690, 793, 814 | Full service: `private const string FieldTodoFlag = "sprk_todoflag";`, `FieldTodoStatus = "sprk_todostatus"`, query criteria `sprk_todoflag != true`, dismissed-todo filter, etc. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs` | 71, 173, 199, 244 | `[JsonPropertyName("sprk_todoflag")] public bool? SprkTodoflag { get; set; }` + `$select` and write paths |
| `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/Dtos/ExternalProjectDtos.cs` | 93, 115, 131 | `[JsonPropertyName("sprk_todoflag")]` in three DTOs |

**Decision needed**: spec FR-29 explicitly lists `sprk_todoflag` for removal. **`TodoGenerationService` cannot continue to write `sprk_todoflag` after Phase 1 task 004**, or the BFF will return 400s from Dataverse. Two options:

- **Option A**: delete `TodoGenerationService` outright (it's an x-home-corporate-workspace feature, not core to smart-todo); flag as separate cleanup.
- **Option B**: rewrite `TodoGenerationService` to create `sprk_todo` records (with `sprk_regardingevent` set to the source event) — this aligns with the new model.
- **Recommend**: **needs human review** — this BFF service belongs to a different parent project (x-home-corporate-workspace-r1). R3 task plan must either explicitly include its rewrite (option B) or coordinate with whoever owns x-home for option A. **This is a blocker for Phase 1 task 004 if not resolved**, because deleting `sprk_todoflag` from `sprk_event` will break this service's build (`FieldTodoFlag` constant compiles fine but Dataverse REST calls 400).

`ExternalDataService` + `ExternalProjectDtos` — these are external-portal data contracts. **needs human review** — same as external-spa frontend (Section 2E); coordinate scope.

### 3B. Tests — Phase 7 / Phase 8

| File | Line | Snippet |
|---|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Workspace/TodoGenerationServiceTests.cs` | 20, 22, 272, 274, 539, 541 | Tests assert `capturedEntity["sprk_todoflag"].Should().Be(true)` etc. — paired with `TodoGenerationService` decision above |

**Action**: paired with TodoGenerationService decision (option A delete-test, option B rewrite-test against `sprk_todo`).

---

## Section 4 — Docs

Removal/update target for **Phase 9 (Docs)** per FR-30.

| File | Line | Snippet | Action |
|---|---|---|---|
| `docs/architecture/event-to-do-architecture.md` | 50, 64, 67, 79, 122 | Architecture doc describing the two-entity `sprk_event + sprk_eventtodo` model | Mark **superseded** by new `docs/architecture/spaarke-todo-architecture.md` per FR-30 |
| `docs/data-model/sprk_event-related-tables.md` | 130, 131, 135, 136 | Data-model entries for `sprk_todoflag`, `sprk_todoflagname`, `sprk_todostatus`, `sprk_todostatusname` (also implicitly `sprk_todocolumn` + `sprk_todopinned` if present further) | Delete the four field rows (+ verify no `sprk_todocolumn` / `sprk_todopinned` rows exist below — none surfaced in grep) |
| `docs/procedures/production-release.md` | 110 | `\| **Events/Tasks** \| sprk_event, sprk_eventset, sprk_eventtodo, sprk_eventlog, sprk_workassignment \|` | Replace `sprk_eventtodo` with `sprk_todo` |
| `docs/guides/EXTERNAL-ACCESS-SPA-GUIDE.md` | 208 | `ODataEvent         // sprk_eventid, sprk_name, sprk_duedate, sprk_status, sprk_todoflag, ...` | Update once external-spa decision is taken (see Section 2E) |
| `docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md` | 253 | `Webapi/sprk_event/fields = sprk_eventid,sprk_eventname,sprk_duedate,sprk_eventstatus,sprk_todoflag,_sprk_regardingproject_value,createdon` | Same as above |

No `.claude/` (excluding archive) hits — clean.

---

## Removal Order — Mapping to Tasks 002 / 003 / 004 / 005 and Downstream Phases

The schema cut **must not run before consumer code is gone or simultaneously updated** — solution import will fail with cryptic dependency errors. The correct order is:

### Step 1 — Phase 2 + Phase 3 + Phase 4 + Phase 5: remove TypeScript consumers FIRST

Run in parallel where independent. **Must complete (or have green PRs queued) before any Phase 1 task touches the Dataverse schema.**

| Task | Files to clean (per this audit) |
|---|---|
| Phase 2 hoist (Kanban → `@spaarke/ui-components`) | Section 2A (TodoDetail types/TodoDetail.tsx — remove the `sprk_eventtodo` extension props); Section 2B + 2C `useKanbanColumns.ts` (replace `sprk_todocolumn` / `sprk_todopinned` with the same field names which now live on `sprk_todo`, not `sprk_event` — names stay, entity changes) |
| Phase 3 SmartTodo repoint (tasks 020, 021) | Section 2B all of SmartTodo (queryHelpers, DataverseService, hooks, contexts, components) |
| Phase 3 LegalWorkspace repoint (task 040) | Section 2C all of LegalWorkspace SmartToDo block + FeedTodoSyncContext shape change |
| Phase 4 CreateTodo wizard rewrite (tasks 030, 031) | Section 2A CreateTodoWizard + 2B/2C CreateTodo + CreateWorkAssignment |
| Phase 5 add-in / side-pane updates | Section 2D TodoDetailSidePane retire; EventDetailSidePane TodoSection refactor; DailyBriefing inline todo create |

### Step 2 — Phase 7: remove C# consumers (BFF + plugins + tests)

| Task | Files |
|---|---|
| Phase 7 BFF cleanup | Section 3A `TodoGenerationService.cs` (decision required), `ExternalDataService.cs`, `ExternalProjectDtos.cs` |
| Phase 7 tests | Section 3B `TodoGenerationServiceTests.cs` |
| Phase 7 plugin work | (new `SprkTodoSyncPlugin` per spec FR-21; no removal — no existing `sprk_eventtodo` plugin in audit hits) |

### Step 3 — Phase 1: schema cut

**Run after Steps 1 + 2 land (or in same coordinated PR if all green together).** Order within Phase 1:

| Task | What it touches | Pre-condition |
|---|---|---|
| **task 002** — Create `sprk_todo` | New entity + new fields + new relationships | Independent; can land first |
| **task 003** — Register `sprk_todo` in `sprk_recordtype_ref` | One row insert | After task 002 |
| **task 004** — Remove `sprk_todoflag` / `sprk_todostatus` / `sprk_todocolumn` / `sprk_todopinned` from `sprk_event` | Field deletes via Web API DELETE | After Step 1 + Step 2 land (otherwise client + BFF builds break or 400 at runtime) |
| **task 005** — Delete `sprk_eventtodo` entity | Relationship → entity → ribbon overlay | Last. Order within task: (1) re-pack ribbon `ThemeMenuRibbons` solution **without** the `sprk_EventTodo` Entity block (Section 1A) + remove the RootComponent line + re-import → (2) Web API DELETE relationship `sprk_eventtodo_RegardingEvent_n1` → (3) Web API DELETE entity `sprk_eventtodo` |

### Step 4 — Phase 9: documentation

Run last. Section 4 + the `docs/data-model/sprk_event-related-tables.md` row removal (which can also be batched into task 004's PR for atomicity).

### Step 5 — Phase 10 (or repo-cleanup): bundle.js refresh

Section 2F — rebuild PCFs via `npm run build:prod` and verify `bundle.js` no longer contains the legacy names. This is implicit in PCF redeploy after source clean.

---

## Summary Counts

| Category | Distinct Files | Approximate Match Count |
|---|---|---|
| Schema (Solution/Ribbon XML + data-model docs) | 3 | ~12 lines |
| TypeScript / TSX (shared lib + SmartTodo + LegalWorkspace + TodoDetailSidePane + EventDetailSidePane + DailyBriefing + external-spa + PCF bundles) | 38 | ~150 lines (excluding compiled bundles) |
| C# (BFF service + DTOs + tests) | 4 | ~22 lines |
| Docs (architecture + data-model + guides + procedures) | 5 | ~13 lines |
| **Total** | **~50 distinct files** | **~200 lines** (excluding compiled bundle.js) |

---

## Items Needing Human Review

1. **`Sprk.Bff.Api/Services/Workspace/TodoGenerationService.cs`** (Section 3A): owned by x-home-corporate-workspace-r1, but writes `sprk_todoflag` which R3 deletes. R3's task plan does not include a Phase 7 rewrite task for it. **Blocker for task 004** unless deleted, rewritten, or coordinated with x-home owner. **Recommend**: file an issue / escalate to project owner before Phase 1 runs.
2. **`Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs` + `Api/ExternalAccess/Dtos/ExternalProjectDtos.cs`** (Section 3A): external-portal data contracts. R3 scope does not explicitly mention external-portal. **Recommend**: confirm with project owner whether external-portal is in scope; if no, freeze its DTOs and don't remove `sprk_todoflag` from them in this project — but then `sprk_event.sprk_todoflag` cannot be deleted without breaking the external portal. **This may be a Phase 1 blocker**.
3. **`src/client/external-spa/`** (Section 2E): same scope concern as #2; external-spa is the consumer of the DTOs in #2.
4. **`src/solutions/EventDetailSidePane/src/components/MemoSection.tsx:5`** (Section 2D): the `_sprk_regardingevent_value` hit on this file is **not** a todo reference (it's the memo section using the same lookup pattern from `sprk_memo` → `sprk_event`). No action needed in R3 — but include in task 085 final-sweep guidance so the next sweep doesn't false-positive on it.

---

## Safe-to-Proceed Assessment

**The project is NOT yet safe to proceed with Phase 1 schema cuts without resolving the C# `TodoGenerationService` + external-portal questions** (items 1 + 2 above). Once those are resolved:

- All TypeScript references are confined to a well-bounded set of solutions (SmartTodo, LegalWorkspace, TodoDetailSidePane, EventDetailSidePane, DailyBriefing, plus a single shared-lib component) — Phase 2/3/4/5 tasks 010–055 cover them.
- All ribbon-XML references are contained in one solution (`ThemeMenuRibbons`) — Phase 1 task 005 covers it.
- All docs references are 5 files — Phase 9 task 080 + 081 cover them.
- No unexpected non-Spaarke consumers (e.g., admin scripts, external integrations not already enumerated) were found.

**Recommended action**: escalate items 1 + 2 (TodoGenerationService + external-portal scope) to the project owner before scheduling Phase 1. Confirm both will be handled (either by inclusion in R3 or by an explicit "freeze, don't delete" decision on the four `sprk_event` to-do fields if external-portal must keep them).

---

*Audit completed by task-execute (STANDARD rigor) for task 001-audit-eventtodo-references.poml.*
