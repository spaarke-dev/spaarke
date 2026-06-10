# Spaarke To Do — Solution Architecture

> **Version**: 1.0
> **Date**: 2026-06-10
> **Project**: smart-todo-decoupling-r3 (PR #373, squash-merged to master `e328beaf`)
> **Status**: Implementation Complete
> **Supersedes**: [`event-to-do-architecture.md`](event-to-do-architecture.md) — the event-coupled model retired.

---

## Overview

Spaarke To Do provides Kanban-style task management as a first-class entity (`sprk_todo`) decoupled from any single parent entity. To-Do items may stand alone or attach to any of **eleven parent record types** via Spaarke's multi-entity resolution pattern ([ADR-024](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md)). The system spans:

- **SmartTodo Code Page** (Kanban + filter + detail + wizard launch) — `sprk_smarttodo` web resource
- **Parent-form subgrids** — every Matter / Project / Event / Communication / WorkAssignment / Invoice / Budget / Analysis / Organization / Contact / Document record exposes a "To Dos" subgrid
- **Outlook add-in** — "Create To Do" ribbon button + linked-todos indicator banner in the taskpane
- **BFF API** — two Office endpoints (`/by-message-id`, `/{commId}/linked-todos`) plus feature-gated scaffolding for Microsoft To Do bidirectional sync (gated off pending AAD `Tasks.ReadWrite` scope)

R3 was a **pre-release hard cut**: the previous event-coupled model (`sprk_event.sprk_todoflag` + `sprk_eventtodo`) is removed entirely. No backward-compatibility shims.

---

## Entity Model

### `sprk_todo` — first-class entity

44 attributes (excluding standard Dataverse audit/owner fields):

| Group | Attributes |
|---|---|
| **Identity / core** | `sprk_todoid` (PK), `sprk_name` (NOT NULL), `sprk_description`, `sprk_notes`, `sprk_assignedto`, `sprk_duedate`, `sprk_completedon` |
| **Prioritization** | `sprk_priorityscore`, `sprk_effortscore`, `sprk_todocolumn` (Today / Tomorrow / Future), `sprk_todopinned` |
| **11 regarding lookups** (FR-13 mutual-exclusivity) | `sprk_regardinganalysis`, `sprk_regardingbudget`, `sprk_regardingcommunication`, `sprk_regardingcontact`, `sprk_regardingdocument`, `sprk_regardingevent`, `sprk_regardinginvoice`, `sprk_regardingmatter`, `sprk_regardingorganization`, `sprk_regardingproject`, `sprk_regardingworkassignment` |
| **4 resolver fields** (ADR-024 atomic-write) | `sprk_regardingrecordtype` (FK → `sprk_recordtype_ref`), `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl` |
| **Graph sync state** (scaffolded, gated off) | `sprk_graphtodolistid`, `sprk_graphtodotaskid`, `sprk_lastsyncedutc`, `sprk_syncerror`, `sprk_synchash` |
| **Statuscode (FR-24)** | `1` Open / `659490001` In Progress / `2` Completed / `659490002` Dismissed |

### Removed in R3

- `sprk_eventtodo` entity — deprecated; **orphan-but-not-deleted** in dev environment due to 26 appmodulecomponent references (cleanup deferred per user authorization; not present in SpaarkeCore solution exports, so portable)
- `sprk_event.sprk_todoflag`, `sprk_todostatus`, `sprk_todocolumn`, `sprk_todopinned` — 4 fields cut (FR-03)
- Retained on `sprk_event` (D-4): `sprk_priorityscore`, `sprk_effortscore`, `sprk_duedate`

---

## UI Surfaces

### SmartTodo Code Page (`sprk_smarttodo`)

React 18 + Fluent UI v9. Lives at `src/solutions/SmartTodo/`. Mounts in:

- **Standalone** — Spaarke MDA navigation
- **Workspace widget** — embedded in SpaarkeAi workspace
- **Wizard launch** — opened from parent record ribbons with `?action=createTodo&regardingType=...&regardingId=...` URL params (parsed by `useLaunchContext` hook → auto-opens `CreateTodoWizard` with regarding pre-filled)

Composition:
- `KanbanBoard` + `KanbanCard` — hoisted to `@spaarke/ui-components` in R3 task 010 (NFR-02)
- `MyTasksFilter` — Fluent v9 RadioGroup for filter modes (NFR-01)
- `TodoDetailPanel` — inline detail panel (the prior `TodoDetailSidePane` was retired in R3 task 081; replaced with a simpler inline panel hosting `AssociateToStep`)
- `CreateTodoWizard` (from `@spaarke/ui-components`) — multi-step wizard launched as modal

### Parent-form subgrids (11 entities)

Each parent record's main form has a **"To Dos"** subgrid querying `sprk_todo` filtered by the appropriate `sprk_regarding<X>` lookup. Subgrid uses `sprk_AssociatedTodos` view configured on the relationship.

**Ribbon**: only Matter has a live "Create To Do" ribbon button as of R3 merge. Ten other entities have draft ribbon XML at `infrastructure/dataverse/ribbon/<Entity>Ribbons/createtodo-button.xml` (deploy deferred per user; consolidated under task 040).

### Outlook add-in (Spaarke Outlook Add-in)

Same add-in manifest (`5e4d66d0-2603-44ea-acbc-400d3b881c90`) extended with:
- **`CreateTodoButton`** — `MessageReadCommandSurface` ribbon button. Opens the SmartTodo Code Page in a new tab with launch-context query params (`?action=createTodo&regardingType=sprk_communication&regardingId=<commId>&regardingName=<subject>`)
- **`LinkedTodosBanner`** — taskpane indicator banner showing N linked `sprk_todo` records for the current email (queries BFF `/api/office/communications/{commId}/linked-todos`)

Manifest version `1.0.19.0`. APP_VERSION `1.0.18` (source string in taskpane footer).

---

## BFF Surface

### New endpoints (R3 task 070a)

| Endpoint | Purpose |
|---|---|
| `GET /api/office/communications/by-message-id/{internetMessageId}` | Look up `sprk_communication` by RFC 5322 message ID (for Outlook → CreateTodo launch flow) |
| `GET /api/office/communications/{commId}/linked-todos` | List `sprk_todo` records with `sprk_regardingcommunication == {commId}` (for LinkedTodosBanner) |

Both endpoints registered via `MapOfficeCommunicationsEndpoints` in `EndpointMappingExtensions.cs:116`. ProblemDetails-compliant errors. Tests at `tests/unit/Sprk.Bff.Api.Tests/Api/Office/CommunicationsEndpointsTests.cs`.

### Existing endpoint repointing

`ExternalDataService.cs` (BFF ExternalAccess) — collection routes updated `/events` → `/todos`, entity-set name fixed `sprk_todoes` → `sprk_todos` (R3 task 040 verification).

### Feature-gated scaffolding (Phase 7 — pending)

Per ADR-032 Null-Object Kill-Switch Pattern. Scaffolded with `Spaarke:Graph:TodoSync:Enabled = false`:

- `ITodoGraphSyncHandler` — outbound `sprk_todo` → Microsoft To Do
- `ISpaarkeListProvisioner` — creates "Spaarke" list in user's `/me/todo/lists`
- `ITodoSubscriptionManager` — Graph webhook subscription lifecycle
- `ITodoSyncBackfiller` — initial opt-in $batch backfill

Null-Object implementations live at `src/server/api/Sprk.Bff.Api/Services/Todo/` and bind unconditionally in DI (`TodoSyncModule.cs`). When flag flips on, Placeholder implementations swap in. Real implementations pending tasks 016, 061–066 (blocked on AAD `Tasks.ReadWrite` delegated scope add by tenant admin).

---

## Multi-Entity Resolution (ADR-024)

`sprk_todo` follows the same regarding shape as `sprk_communication` (canonical reference): **11 specific lookups + 4 resolver fields**. Mutual exclusivity (FR-13) enforced at the service layer:

```
TodoRegardingUpdateBuilder.applyResolverFields(todo, regardingType, regardingId, regardingName)
  → clears all 11 sprk_regarding* lookups
  → sets the appropriate sprk_regarding<Type>
  → atomically populates sprk_regardingrecordtype, sprk_regardingrecordid,
    sprk_regardingrecordname, sprk_regardingrecordurl
  → single Dataverse Update call
```

Wraps `PolymorphicResolverService.applyResolverFields` (shared with `sprk_communication`). Used by `CreateTodoWizard`, `AssociateToStep` (11-entity preset `TODO_REGARDING_TARGETS`), and `TodoDetailPanel`.

---

## Authentication

Per ADR-028:
- **Browser / Code Page / Outlook → BFF**: delegated user OBO via `@spaarke/auth` + MSAL (no change in R3)
- **BFF → Graph/Dataverse**: Managed Identity via `DefaultAzureCredential` when `Graph__ManagedIdentity__Enabled=true` (R3 contributes one BFF setting fix: `AZURE_CLIENT_ID` env var must be set to UAMI clientId for `DefaultAzureCredential` to resolve correctly when no system-assigned MI is attached)

R3 did not introduce new auth scopes. The Microsoft To Do sync feature (Phase 7) requires `Tasks.ReadWrite` delegated scope — pending external action (tenant admin).

---

## Decisions to remember

| Decision | Rationale |
|---|---|
| `sprk_todo` is a **custom entity**, not a Dataverse Activity | Activity entities carry baggage (activity pointer, partylist) that conflict with multi-entity resolution; activity model can't represent the 11-entity regarding cleanly |
| **No backward-compatibility shims** for `sprk_eventtodo` or `sprk_event.sprk_todoflag` | Pre-release hard cut (OS-1 / NFR-12) |
| Modern Outlook clients only (Outlook for Web + new Outlook + classic) | Classic Outlook add-in sideloading deprecated by Microsoft; org-wide deployment via M365 Admin Center → Integrated Apps is the supported path |
| Kanban + TodoDetail hoisted to `@spaarke/ui-components`, not kept in SmartTodo solution | Shared with future widget consumers (SpaarkeAi workspace) and ensures consistent a11y/keyboard behavior (NFR-10) |

---

## Project ledger

- **R1** (`projects/events-smart-todo-kanban/`) — original event-coupled implementation
- **R2** (`projects/events-smart-todo-kanban-r2/`) — R2 refinements; still event-coupled
- **R3** (`projects/smart-todo-decoupling-r3/`) — decoupling to first-class `sprk_todo` (this work)
- **R4** (`projects/smart-todo-r4/`) — pending UX enhancements (SmartTodo Code Page UI overhaul, modal-with-main-form pattern, regarding-resolver as PCF, vertical Kanban, workspace widget migration). Design draft only as of R3 wrap-up.

---

## Cross-references

- [ADR-024](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md) — multi-entity resolution pattern
- [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Spaarke Auth v2 (OBO + MI)
- [ADR-032](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) — Null-Object pattern for feature-gated services
- [`docs/data-model/sprk_communication.md`](../data-model/sprk_communication.md) — canonical multi-entity resolution reference
- [`event-to-do-architecture.md`](event-to-do-architecture.md) — superseded by this document
- [`projects/smart-todo-decoupling-r3/spec.md`](../../projects/smart-todo-decoupling-r3/spec.md) — full R3 spec (30 FRs, 12 NFRs)

---

*Last updated 2026-06-10 (R3 wrap-up).*
