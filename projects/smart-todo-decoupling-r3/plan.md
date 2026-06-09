# Project Plan: Smart To Do — Decoupling from Events (R3)

> **Last Updated**: 2026-06-07
> **Status**: Ready for Tasks
> **Spec**: [`spec.md`](spec.md)
> **Source design**: [`design.md`](design.md)

---

## 1. Executive Summary

**Purpose**: Replace the event-coupled `sprk_event + sprk_eventtodo` to-do model with a first-class `sprk_todo` custom entity that (a) stands alone or attaches to any of eleven parent record types via Spaarke's multi-entity resolution pattern, (b) mirrors bidirectionally to Microsoft Graph `/me/todo`, and (c) gets an Outlook ribbon "Create To Do" action + indicator banner.

**Scope**:
- New `sprk_todo` custom entity (kanban + Graph sync state + 11 specific regarding lookups + 4 resolver fields)
- Retirement of `sprk_eventtodo` + removal of four `sprk_todo*` fields from `sprk_event` (pre-release, no compat shims)
- Hoist `KanbanBoard` / `KanbanColumn` / `KanbanCard` to `@spaarke/ui-components`
- SmartTodo Code Page repointed at `sprk_todo`; "My Tasks" filter
- CreateTodo wizard repointed at `sprk_todo`; `AssociateToStep` extended to 11 entity targets
- "To Dos" subgrid on each of 11 parent forms
- Microsoft Graph `/me/todo` bidirectional sync (per-user opt-in, backfill, last-write-wins, Modern UCI deep link)
- New `Tasks.ReadWrite` delegated scope + OBO reuse
- Outlook add-in ribbon "Create To Do" + indicator

**Timeline**: estimated 4-6 weeks of effort assuming sequential phases with Phase 2 + Phase 6 parallelized. | **Estimated Effort**: ~80-110 hours depending on parent-form subgrid count and Graph sync hardening depth.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **[ADR-001](../../.claude/adr/ADR-001-minimal-api.md)** — Minimal API pattern for the Graph webhook endpoint (`/api/graph/webhooks/todo`) and any sync trigger endpoints
- **[ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md)** — Endpoint filters for authorization; webhook MUST validate Graph's `clientState` + validation token
- **[ADR-024](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md)** — Multi-entity resolution; MUST use `PolymorphicResolverService.applyResolverFields` to populate the four resolver fields atomically
- **[ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)** — SSO + token issuance; reuse cached OBO flow for `Tasks.ReadWrite` (do not introduce a new auth path)
- **[ADR-032](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md)** — Graph sync services MUST be feature-gated with Null-Object fallbacks so the BFF starts cleanly when sync is disabled

**Cross-cutting constraints**:
- **CLAUDE.md §10** + **[`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md)** — Binding pre-merge checklist for every BFF-touching task (placement decision, publish-size verification, CVE check, test obligation, asymmetric-registration anti-pattern guard)
- **[`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md)** — Publish-size per-task verification rule (NFR-03 binding: baseline ~45.65 MB, ceiling 60 MB, single-task delta ≥+5 MB requires explicit justification)

**From spec**:
- Pre-release rule: NO backward-compatibility shims, NO data migration (FR-29 + OS-1 / OS-2)
- All new and migrated UI uses Fluent v9 exclusively (`@fluentui/react-components`, semantic tokens, Griffel `makeStyles`) (NFR-01)
- Shared-library mandate: Kanban + To Do UI primitives live in `@spaarke/ui-components` (NFR-02)
- Custom entity for `sprk_todo` (NOT Activity entity)
- Multi-entity resolution (NOT native `regardingobjectid`)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Custom entity for `sprk_todo` (not Activity) | Spaarke doesn't use Activities; kanban + scoring need full customization | Full schema control; can include Graph sync state fields |
| Multi-entity resolution via 11 specific lookups + 4 resolver fields | Spaarke standard (ADR-024); reuses `PolymorphicResolverService` + `AssociateToStep` | Atomic resolver population on every regarding change |
| Hard cut, no compat shims | Pre-release; future maintenance of dual paths outweighs migration value | Cleaner final state; FR-29 enforces removal |
| Per-user opt-in for Graph sync | Privacy + delegated-token cost; respects user choice of task surface | Single user-pref toggle; backfill on first opt-in |
| Last-write-wins per-field (timestamp) | Matches user mental model; cleaner than entity-level wins | Combined with synchash + skip-flag for full loop prevention |
| Modern UCI deep link scheme (`/apps/{appid}/r/sprk_todo/{id}`) | Long-term Microsoft direction; preserves form context | `DeepLinkBuilder` reads org URL + app id from configuration |
| Graph sync feature-gated via `Spaarke:Graph:TodoSync:Enabled` | BFF must boot cleanly without Graph creds in dev / disabled environments | Null-Object services per ADR-032 |

### Discovered Resources

**Applicable Skills** (auto-discovered for this project):
- **dataverse-create-schema** — `.claude/skills/dataverse-create-schema/SKILL.md` — Create `sprk_todo` entity, delete `sprk_eventtodo`, remove `sprk_event` fields
- **dataverse-deploy** — `.claude/skills/dataverse-deploy/SKILL.md` — Deploy solution changes via PAC CLI
- **fluent-v9-component** — `.claude/skills/fluent-v9-component/SKILL.md` — Hoist Kanban primitives; build `MyTasksFilter`; simplify `TodoDetail`
- **bff-deploy** — `.claude/skills/bff-deploy/SKILL.md` — Deploy BFF API after Graph sync engine additions
- **office-addins-deploy** — `.claude/skills/office-addins-deploy/SKILL.md` — Deploy updated Outlook add-in
- **code-page-deploy** — `.claude/skills/code-page-deploy/SKILL.md` — Redeploy SmartTodo Code Page after `sprk_todo` repoint
- **adr-aware** — `.claude/skills/adr-aware/SKILL.md` — Auto-loaded by task-execute; ensures the 5 applicable ADRs are applied
- **code-review** — `.claude/skills/code-review/SKILL.md` — Quality gate at task-execute Step 9.5 for FULL-rigor tasks
- **repo-cleanup** — `.claude/skills/repo-cleanup/SKILL.md` — Final cleanup task

**Knowledge Articles & References**:
- [`docs/architecture/event-to-do-architecture.md`](../../docs/architecture/event-to-do-architecture.md) — Will be marked **superseded** by a new `docs/architecture/spaarke-todo-architecture.md` in Phase 9 (FR-30)
- [`docs/data-model/sprk_communication.md`](../../docs/data-model/sprk_communication.md) — Canonical multi-entity resolution reference; **mirror its regarding shape exactly** for `sprk_todo`
- [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md) — Component-library inventory; Kanban primitives belong in `@spaarke/ui-components`

**Reusable Code (existing patterns to consume / extend)**:
- [`src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts`](../../src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts) — Reuse as-is (no edits expected per D-2)
- [`src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/AssociateToStep.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/AssociateToStep.tsx) — Extend to all eleven regarding-target entity types
- [`src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs) — Add `Tasks.ReadWrite` to delegated scopes
- [`src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphSubscriptionManager.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphSubscriptionManager.cs) — Extend for `/me/todo/lists/{id}/tasks` resource
- [`src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs) — Add `TodoSync` message type + `TodoGraphSyncHandler` registration
- [`src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/`](../../src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/) + `WizardShell` — `CreateTodo` becomes a thin wrapper
- [`src/solutions/SmartTodo/src/components/shared/KanbanBoard.tsx`](../../src/solutions/SmartTodo/src/components/shared/KanbanBoard.tsx) — Currently SmartTodo-local; hoist to `@spaarke/ui-components/Kanban/`

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Dataverse Schema (Week 1)                                [SEQUENTIAL, BLOCKS ALL]
  └─ sprk_todo entity + sprk_eventtodo delete + sprk_event field cleanup

Phase 2: Shared-Lib Hoist (Week 1-2)                              [Parallel with Phase 6]
  └─ Kanban primitives → @spaarke/ui-components; TodoDetail simplified

Phase 6: Graph Foundation (Week 1-2)                              [Parallel with Phase 2]
  └─ Tasks.ReadWrite scope; OBO wiring; user-preference schema

Phase 3: SmartTodo Code Page repoint (Week 2-3)                   [depends on 1 + 2]
  └─ Query sprk_todo; "My Tasks" filter

Phase 4: CreateTodo wizard + AssociateToStep extension (Week 2-3) [depends on 1 + 2]
  └─ Wizard targets sprk_todo; AssociateToStep supports all 11 entity targets

Phase 5: Parent-form subgrids (Week 2-3)                          [depends on 1; 11 parallelizable form-edit tasks]
  └─ "To Dos" subgrid on Matter, Project, Event, Communication, WorkAssignment, Invoice, Budget, Analysis, Organization, Contact, Document

Phase 7: Graph Sync Engine (Week 3-4)                             [depends on 1 + 6]
  └─ Plugin → Service Bus → TodoGraphSyncHandler; DeepLinkBuilder; webhook endpoint; subscription manager extension; nightly renewal job

Phase 8: Outlook Add-in (Week 4-5)                                [depends on Phase 4]
  └─ Ribbon action "Create To Do"; taskpane indicator banner

Phase 9: Cleanup & Documentation (Week 5-6)                       [final, sequential]
  └─ TodoDetailSidePane audit + retire/refactor; architecture doc swap; repo-cleanup
```

### Critical Path

**Blocking Dependencies**:
- Phase 1 blocks Phases 2, 3, 4, 5, 6, 7, 8, 9 (every phase depends on `sprk_todo` existing)
- Phase 2 blocks Phases 3 and 4 (shared-lib hoist must complete before code-page + wizard repoint)
- Phase 6 blocks Phase 7 (Graph auth must be in place before sync engine writes to Graph)
- Phase 4 blocks Phase 8 (CreateTodo wizard is what the Outlook ribbon launches)

**Critical Path Length**: Phase 1 → Phase 2 → Phase 4 → Phase 8 → Phase 9. Phase 7 is a parallel critical-path branch (1 → 6 → 7 → 9).

**High-Risk Items**:
- **Schema cut order** (Phase 1) — `sprk_eventtodo` delete must follow removal of all references (subgrids, views, scripts); reverse order causes solution-import failure. Mitigation: dedicated "audit references" task before delete.
- **Graph 429 under backfill** (Phase 7) — Mitigation: exponential backoff (1→2→4→8→cap 30s) + circuit breaker (5 consecutive 429s → 5-min pause) per NFR-05.
- **Subscription expiry** (Phase 7) — Mitigation: nightly renewal job; failure alarmed (NFR-07).
- **BFF publish-size delta** — Mitigation: per-task verification per CLAUDE.md §10; binding ceiling 60 MB (current baseline ~45.65 MB).
- **Loop prevention completeness** (Phase 7) — Mitigation: combined synchash + skip-flag + per-field LWW; integration test for round-trip echo + concurrent-edit scenarios.

---

## 4. Phase Breakdown

### Phase 1: Dataverse Schema (Week 1)

> **Expanded 2026-06-07 (audit escalation)**: Task 001 surfaced 3 prerequisite refactors before the schema cut is safe: `TodoGenerationService` (task 006), `ExternalAccess` BFF surface (007), and external-spa migration (008). All run after task 002 (entity create) but before tasks 004 + 005 (field removal + entity delete).

**Target environment**: `https://spaarkedev1.crm.dynamics.com/` (dev) for the initial deploy. **Product portability**: schema + code MUST be tenant-agnostic — solution export/import for schema; configuration-driven endpoints for code. No hardcoded org URLs.

**Objectives**:
1. Create `sprk_todo` custom entity with the full attribute set defined in spec FR-01 / design §4.1
2. Refactor `TodoGenerationService` (task 006) + `ExternalAccess` BFF (task 007) + migrate external-spa (task 008) — audit-surfaced prerequisites
3. Delete `sprk_eventtodo` entirely
4. Remove `sprk_todoflag` / `sprk_todostatus` / `sprk_todocolumn` / `sprk_todopinned` from `sprk_event`
5. Register `sprk_todo` in `sprk_recordtype_ref`

**Deliverables**:
- [ ] `sprk_todo` entity present (primary name, description, ownership, `sprk_assignedto`, state/status, kanban behavior, detail, 11 specific regarding lookups, 4 resolver fields, 5 Graph sync state fields)
- [ ] Reference-audit complete: every consumer of `sprk_eventtodo` / `sprk_event.sprk_todo*` is enumerated and slated for removal in later phases
- [ ] `sprk_eventtodo` deleted (entity + attributes + `sprk_eventtodo_RegardingEvent_n1` relationship + saved queries + SVG)
- [ ] Four to-do fields removed from `sprk_event`; three retained (`sprk_priorityscore`, `sprk_effortscore`, `sprk_duedate`)
- [ ] `sprk_recordtype_ref` row with `sprk_recordlogicalname = "sprk_todo"`

**Critical Tasks**:
- Audit `sprk_eventtodo` and `sprk_event.sprk_todo*` references before deletion (BLOCKS schema cut)

**Inputs**: `spec.md` FR-01..FR-04, `design.md` §4.1, `docs/data-model/sprk_communication.md` (regarding-shape reference)

**Outputs**: Updated solution with new entity + deletions; updated `sprk_recordtype_ref` row; reference-audit notes for downstream phases

**Verification**: solution import succeeds; entity visible in Power Apps maker portal; all attributes present with correct types/lengths matching design §4.1; `sprk_eventtodo` no longer in solution; no references remain in `src/`

---

### Phase 2: Shared-Lib Hoist (Week 1-2) — parallel with Phase 6

**Objectives**:
1. Move `KanbanBoard`, `KanbanColumn`, `KanbanCard` from `src/solutions/SmartTodo/src/components/shared/` to `src/client/shared/Spaarke.UI.Components/src/components/Kanban/`
2. Simplify `TodoDetail` shared component to single-entity load/save (delete `loadTodoExtension`, `saveTodoExtensionFields`, `deactivateTodoExtension`)

**Deliverables**:
- [ ] Kanban components compile and unit-test in `Spaarke.UI.Components`
- [ ] Exports added to lib barrel; SmartTodo + LegalWorkspace embedded widget consume the shared version
- [ ] `TodoDetail` reduced to single-entity load/save; two-entity logic deleted
- [ ] No local copies of Kanban primitives remain in `src/solutions/SmartTodo/`

**Inputs**: existing `KanbanBoard.tsx` (already Fluent-v9 compliant + domain-agnostic per spec)

**Outputs**: `@spaarke/ui-components/Kanban/` populated; `TodoDetail` simplified; lib version bump

**Verification**: grep returns zero local Kanban imports in `src/solutions/SmartTodo/`; Fluent v9 lint passes; a11y snapshot matches R2 baseline (NFR-10)

---

### Phase 3: SmartTodo Code Page Repoint (Week 2-3)

**Objectives**:
1. Repoint SmartTodo kanban data source from `sprk_event` (todoflag=true) to `sprk_todo`
2. Add "My Tasks" filter component to `KanbanHeader`
3. Use `AssociateToStep` in the SmartTodo `TodoDetail` panel for regarding edit
4. Update `FeedTodoSyncContext` payload from event-id to todo-id

**Deliverables**:
- [ ] All queries hit `/api/data/v9.x/sprk_todos` (no `sprk_events` from kanban path)
- [ ] "My Tasks" filter with three modes (My Tasks default / Assigned to me / All); persisted in user preferences
- [ ] Regarding edit in TodoDetail panel re-runs `applyResolverFields`
- [ ] LegalWorkspace ActivityFeed and SmartToDo widget remain in sync (FeedTodoSyncContext updated)

**Inputs**: Phase 1 (entity), Phase 2 (shared Kanban + simplified TodoDetail)

**Outputs**: Updated `src/solutions/SmartTodo/` Code Page

---

### Phase 4: CreateTodo Wizard + AssociateToStep Extension (Week 2-3)

**Objectives**:
1. Rewrite `CreateTodo` wizard to target `sprk_todo` (NOT `sprk_event` with `todoflag=true`)
2. Extend `AssociateToStep` to support all eleven regarding-target entity types
3. Implement launch-context pre-fill rules (kanban / parent-form ribbon / Outlook)

**Deliverables**:
- [ ] Wizard's create call writes to `/api/data/v9.x/sprk_todos`
- [ ] `AssociateToStep` renders all eleven entity types in picker; selection returns correct entity type + record id + display name
- [ ] Pre-fill correct from all three entry points (kanban: none; parent-form: launch record; Outlook: `sprk_regardingcommunication`)
- [ ] Skip succeeds and creates standalone todo

**Inputs**: Phase 1 (entity), Phase 2 (shared lib)

**Outputs**: Updated `src/client/shared/Spaarke.UI.Components/src/components/CreateTodo/` and `AssociateToStep/`

---

### Phase 5: Parent-Form Subgrids (Week 2-3)

**Objectives**:
1. Add "To Dos" subgrid to the main form of each of the eleven regarding-target entities

**Deliverables**: (one task per entity; can be parallelized)
- [ ] Matter form — subgrid filtered by `sprk_regardingmatter`
- [ ] Project form — subgrid filtered by `sprk_regardingproject`
- [ ] Event form — subgrid filtered by `sprk_regardingevent`
- [ ] Communication form — subgrid filtered by `sprk_regardingcommunication`
- [ ] WorkAssignment form — subgrid filtered by `sprk_regardingworkassignment`
- [ ] Invoice form — subgrid filtered by `sprk_regardinginvoice`
- [ ] Budget form — subgrid filtered by `sprk_regardingbudget`
- [ ] Analysis form — subgrid filtered by `sprk_regardinganalysis`
- [ ] Organization form — subgrid filtered by `sprk_regardingorganization`
- [ ] Contact form — subgrid filtered by `sprk_regardingcontact`
- [ ] Document form — subgrid filtered by `sprk_regardingdocument`
- [ ] Each subgrid: default view = Active todos; "All" view available; command bar includes "+ Create To Do" launching wizard with regarding pre-filled

**Inputs**: Phase 1 (entity + specific lookups)

**Outputs**: Updated form definitions across eleven entities

---

### Phase 6: Graph Foundation (Week 1-2) — parallel with Phase 2

**Objectives**:
1. Add `Tasks.ReadWrite` delegated scope to Azure AD app registration
2. Wire OBO token flow through `GraphClientFactory` for the new scope
3. Provision per-user "Spaarke" list scaffolding (user-pref schema + provisioner service)
4. Feature-gate Graph sync via `Spaarke:Graph:TodoSync:Enabled` with Null-Object fallbacks (ADR-032)

**Deliverables**:
- [ ] AAD app has `Tasks.ReadWrite` scope; tenant admin consent obtained (D-1)
- [ ] Token issued with `Tasks.ReadWrite` claim; OBO call to `/me/todo/lists` succeeds (smoke test)
- [ ] `sprk_userpreference` row schema for "MicrosoftToDoSync" preference type (stores `listId`, `subscriptionId`, `expiresUtc`, `initialBackfillCompletedUtc`)
- [ ] `SpaarkeListProvisioner` service implemented; auto-provisions on first opt-in; reuses existing list on second opt-in
- [ ] Null-Object implementations for sync handler + subscription manager (no-op when `Spaarke:Graph:TodoSync:Enabled` is false)

**Inputs**: existing `GraphClientFactory.cs`, `ADR-028`, `ADR-032`

**Outputs**: Updated `Sprk.Bff.Api` with scope + user-pref + provisioner; Null-Object services registered

**Verification**: BFF boots cleanly with `Spaarke:Graph:TodoSync:Enabled=false` and no Graph config; flag-on requires Graph config and runs sync

---

### Phase 7: Graph Sync Engine (Week 3-4)

**Objectives**:
1. Outbound sync: Dataverse plugin on `sprk_todo` Create/Update enqueues Service Bus message; `ServiceBusJobProcessor` routes to `TodoGraphSyncHandler`; handler performs Graph PATCH/POST via OBO; `linkedResources[0]` populated with Modern UCI deep link
2. Inbound sync: per-user Graph change-notification subscription on `/me/todo/lists/{spaarkeListId}/tasks`; webhook endpoint validates + fetches changes via delta query + PATCHes matching `sprk_todo`; subscription renewed nightly before expiry
3. Loop prevention: `sprk_synchash` (16-hex truncated SHA-256 of canonical JSON) + thread-local "skip outbound" flag during inbound writes + per-field last-write-wins
4. Field mapping per design §6.3 (title, body, due, status, priority→importance bucketing)
5. Initial backfill on opt-in: Graph `$batch` of 20 per request; exponential backoff on 429; resumable; toast progress
6. `DeepLinkBuilder` service: Modern UCI URL from config

**Deliverables**:
- [ ] `SprkTodoSyncPlugin` (Create/Update on `sprk_todo` → Service Bus enqueue)
- [ ] `ServiceBusJobProcessor` routes `TodoSync` message type
- [ ] `TodoGraphSyncHandler` performs outbound PATCH/POST with retry + backoff
- [ ] `DeepLinkBuilder` service reads `Spaarke:ModelDrivenApps:DefaultAppId` + `Spaarke:Environment:OrgUrl`; unit-tested
- [ ] `/api/graph/webhooks/todo` endpoint validates `clientState`, responds to validation token within 10s, fetches delta, PATCHes `sprk_todo`
- [ ] `GraphSubscriptionManager` extended for `/me/todo/lists/{id}/tasks` resource
- [ ] Nightly subscription renewal job (re-creates within 24h of expiry); failure alarmed
- [ ] Loop prevention verified (round-trip echo dropped; concurrent edit results in newer-side-wins per field)
- [ ] Initial backfill: 20-per-batch via `$batch`; exponential backoff on 429; resumable; progress toast
- [ ] All sync operations audit-logged with correlation id

**Inputs**: Phase 1 (entity), Phase 6 (Graph auth + user pref + Null-Object scaffolding)

**Outputs**: New `src/server/api/Sprk.Bff.Api/Services/Todo/` directory + new endpoint + new plugin + Service Bus message route

**Verification**: edit in Spaarke → MS To Do within 60s (NFR-04); edit in MS To Do → Spaarke within 60s; both sides concurrent → newer-per-field wins; deep link opens Modern UCI form

---

### Phase 8: Outlook Add-in (Week 4-5)

**Objectives**:
1. Ribbon action "Create To Do" (email-read context); pre-fills `sprk_regardingcommunication` after ensuring email is saved as `sprk_communication`
2. Taskpane indicator banner showing count of linked Spaarke to-dos

**Deliverables**:
- [ ] "Create To Do" ribbon action visible in Outlook web + desktop
- [ ] Click sequence: save email if needed → open CreateTodo wizard with `sprk_regardingcommunication` pre-filled
- [ ] Indicator banner: `GET sprk_todos?$filter=_sprk_regardingcommunication_value eq {commId}&$select=...&$top=10`; cached per `communicationid` session
- [ ] Banner absent when zero linked; shows count + link to view list

**Inputs**: Phase 4 (CreateTodo wizard), `sprk_regardingcommunication` lookup

**Outputs**: Updated `src/client/office-addins/outlook/`

**Verification**: manual test in Outlook web + desktop; pre-fill correct; cache hit on re-open within session; NFR-09 (indicator query p95 < 500ms cached after first hit)

---

### Phase 9: Cleanup & Documentation (Week 5-6) — final

**Objectives**:
1. Audit `TodoDetailSidePane` solution (A-2 assumption) — retire if no consumers; thin-shell refactor if any remain
2. Mark `docs/architecture/event-to-do-architecture.md` superseded
3. Author new `docs/architecture/spaarke-todo-architecture.md`
4. Update CLAUDE.md §16 pointer table
5. Fix CLAUDE.md §10 line 183 stale `ADR-030-bff-nullobject-kill-switch.md` link → `ADR-032-bff-nullobject-kill-switch.md`
6. Repo-cleanup pass — verify no orphan files from migration
7. Write lessons-learned.md
8. Project wrap-up (status → Complete in README)

**Deliverables**:
- [ ] `TodoDetailSidePane` decision documented + executed
- [ ] Old architecture doc has "Superseded by …" header
- [ ] New architecture doc published; CLAUDE.md §16 updated
- [ ] CLAUDE.md §10 line 183 ADR link corrected (drift fix)
- [ ] Repo-cleanup reports no orphans
- [ ] `notes/lessons-learned.md` written
- [ ] README status → Complete

**Inputs**: All prior phases complete

**Outputs**: Cleaned repo; updated docs; lessons-learned; project marked Complete

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Microsoft Graph `/me/todo` endpoint (D-6) | GA | Low | Production-stable per Microsoft docs |
| Graph change-notification webhook reachability (D-7) | Satisfied | Low | Existing public BFF endpoint reachable + TLS-terminated |
| Microsoft To Do client apps (D-8) | Available | Low | All major surfaces (desktop / mobile / web) GA |
| Outlook for add-in testing (D-9) | Available | Low | `/office-addins-deploy` procedure existing |
| `Tasks.ReadWrite` delegated scope on AAD app (D-1) | Pending | Med | Owner: deployment; tenant admin consent required |
| Modern UCI app id per environment (D-4, UQ-2) | Pending | Med | `Spaarke:ModelDrivenApps:DefaultAppId` — coordinate with env owner |
| Service Bus `todosync` queue/topic per environment (D-5) | Pending | Low | Provision per environment (dev/test/prod) before Phase 7 |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `PolymorphicResolverService` | `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts` | Production |
| `AssociateToStep` | `src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/AssociateToStep.tsx` | Production (extend) |
| `GraphClientFactory` | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` | Production (extend scopes) |
| `GraphSubscriptionManager` | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphSubscriptionManager.cs` | Production (extend resource type) |
| `ServiceBusJobProcessor` | `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` | Production (add message route) |
| `WizardShell` + `CreateRecordWizard` | `src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/` | Production |
| `KanbanBoard.tsx` (SmartTodo-local) | `src/solutions/SmartTodo/src/components/shared/KanbanBoard.tsx` | To-be-hoisted |
| `sprk_recordtype_ref` table | Dataverse schema | Production |

---

## 6. Testing Strategy

**Unit Tests** (per service / per component):
- `TodoGraphSyncHandler` — field mapping (each direction), status mapping, importance bucketing, synchash compute, skip-flag honor, LWW resolution
- `DeepLinkBuilder` — URL construction with various org / app id inputs
- `SpaarkeListProvisioner` — first-opt-in creates list; second-opt-in reuses existing
- `AssociateToStep` extension — all eleven entity types render in picker; selection returns correct triple
- `PolymorphicResolverService` (regression) — `applyResolverFields` still populates all four fields atomically for `sprk_todo`
- `MyTasksFilter` — three modes, persistence across reload

**Integration Tests**:
- Outbound sync round-trip: Dataverse Create → Service Bus → Handler → Graph; verify `linkedResources[0]` populated
- Inbound sync round-trip: webhook receives notification → delta query → PATCH `sprk_todo`; verify field values match
- Loop prevention: outbound write → inbound notification of own change → dropped by synchash + skip-flag
- Concurrent edit: both sides within 10s → newer side wins per field, losers logged to `sprk_syncerror`
- Initial backfill: N existing todos appear in MS To Do; survives BFF restart; 429 triggers backoff

**E2E Tests**:
- Create standalone todo from kanban → record in `sprk_todo` with all 11 lookups + 4 resolver fields null
- Create todo from Matter ribbon → regarding pre-filled; resolver fields populated correctly
- Create todo from Outlook ribbon → email saved (if not) → wizard with `sprk_regardingcommunication` pre-filled
- Opt-in to MS To Do sync → Spaarke list created → backfill completes → edit in MS To Do → reflects in Spaarke within 60s
- Click linked resource in MS To Do → opens Modern UCI Spaarke form
- Outlook indicator on email with 0 / 1 / N linked todos

**Coverage gate**: every modified BFF service (`Sprk.Bff.Api/Services/`) must have unit tests per `.claude/constraints/bff-extensions.md` §F (NFR-11).

---

## 7. Acceptance Criteria

### Technical Acceptance (per phase)

**Phase 1**:
- [ ] Solution import succeeds
- [ ] `sprk_todo` visible in Power Apps maker portal with all attributes
- [ ] `sprk_eventtodo` deleted; zero `src/` references
- [ ] Four to-do fields removed from `sprk_event`; three retained
- [ ] `sprk_recordtype_ref` row for `sprk_todo` present

**Phase 2**:
- [ ] Kanban primitives compile + unit-test in `Spaarke.UI.Components`
- [ ] Zero local Kanban imports in `src/solutions/SmartTodo/`
- [ ] `TodoDetail` two-entity logic deleted
- [ ] A11y snapshot matches R2 (NFR-10)

**Phase 3**:
- [ ] Network trace: only `/sprk_todos` requests from kanban path
- [ ] "My Tasks" filter persists across reload
- [ ] FeedTodoSyncContext payload uses todo-id

**Phase 4**:
- [ ] Wizard writes to `/sprk_todos`
- [ ] All three entry points pre-fill regarding correctly
- [ ] Skip creates standalone todo

**Phase 5**:
- [ ] All 11 parent forms have subgrid with correct filter
- [ ] Each subgrid "+ Create To Do" launches wizard with regarding pre-filled

**Phase 6**:
- [ ] `Tasks.ReadWrite` token claim verified via smoke test
- [ ] BFF boots cleanly with `Spaarke:Graph:TodoSync:Enabled=false`
- [ ] User-pref schema for `MicrosoftToDoSync` present

**Phase 7**:
- [ ] Edit Dataverse → MS To Do within 60s (NFR-04)
- [ ] Edit MS To Do → Dataverse within 60s
- [ ] Round-trip echo dropped
- [ ] Concurrent edit: per-field LWW
- [ ] Deep link opens Modern UCI form
- [ ] Initial backfill resumes after BFF restart
- [ ] 429 triggers exponential backoff
- [ ] All ops audit-logged with correlation id

**Phase 8**:
- [ ] Ribbon "Create To Do" works in Outlook web + desktop
- [ ] Indicator banner correct for 0 / 1 / N linked todos
- [ ] Indicator query p95 < 500ms (cached after first hit) (NFR-09)

**Phase 9**:
- [ ] Old architecture doc marked superseded
- [ ] New architecture doc published
- [ ] CLAUDE.md §16 + §10 corrected
- [ ] Repo-cleanup reports no orphans
- [ ] Lessons-learned written
- [ ] README status → Complete

### Business Acceptance

- [ ] Users with opt-in see all owned + assigned to-dos in Microsoft To Do (Outlook + Teams Tasks app + To Do app + mobile)
- [ ] To-dos can be created without an event (standalone) or attached to any of 11 parent types
- [ ] No legacy references remain (`grep -r "sprk_eventtodo\|sprk_todoflag\|sprk_event\.sprk_todocolumn\|sprk_event\.sprk_todopinned\|sprk_event\.sprk_todostatus" src/` returns zero hits)
- [ ] Fluent v9 compliance across all changed UI

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Schema cut order causes solution-import failure (`sprk_eventtodo` deleted while still referenced) | Med | High | Phase 1 dedicated reference-audit task before delete; pre-cut grep verifies zero `src/` references |
| R2 | Graph 429 throttling under initial backfill | Med | Med | Exponential backoff (1→2→4→8→cap 30s) + circuit breaker (5 consecutive 429s → 5-min pause) (NFR-05) |
| R3 | Subscription expiry not renewed → silent inbound sync loss | Low | High | Nightly renewal job within 24h of expiry; failure alarmed (NFR-07); subscription-renewal smoke test |
| R4 | BFF publish-size delta breaches 60 MB ceiling | Low | High | Per-task publish-size verification per CLAUDE.md §10 / `bff-extensions.md`; current baseline ~45.65 MB; ≥+5 MB delta requires explicit justification |
| R5 | Webhook `clientState` leak compromises sync auth | Low | High | Store in Key Vault with rotation pipeline (UQ-3 resolution); validate every notification |
| R6 | Bidirectional sync loop (echo of own writes) | Med | Med | Three-layer prevention: synchash + thread-local skip-flag + per-field LWW (FR-23); integration test for both round-trip and concurrent-edit |
| R7 | Initial backfill long-running, interruptible | Med | Low | Resumable with `initialBackfillCompletedUtc` checkpoint; progress toast (FR-20) |
| R8 | Modern UCI app id stale per environment | Med | Med | `DeepLinkBuilder` reads from config; env-owner confirmation (UQ-2) |
| R9 | Asymmetric DI registration in feature-gated Graph services (anti-pattern per `bff-extensions.md` §F.1) | Med | Med | Use ADR-032 Null-Object pattern (P1/P2/P3 per service); static-scan recipe; unconditional registration + Null-Object impls |
| R10 | A11y regression in hoisted Kanban primitives | Low | Med | Snapshot accessibility tests before hoist (NFR-10); compare against R2 baseline |
| R11 | New HIGH-severity CVE from added NuGet/npm packages | Low | High | `dotnet list package --vulnerable --include-transitive` on every BFF-touching task; npm audit on UI tasks |
| R12 | `TodoDetailSidePane` consumer audit reveals unavoidable consumer (A-2 assumption fails) | Low | Low | Phase 9 audit produces explicit retain-or-retire decision before any deletion |

---

## 9. Next Steps

1. **Review this plan.md** against `spec.md` and `design.md` to confirm phase coverage
2. **Run** `/task-create projects/smart-todo-decoupling-r3` to decompose phases into POML task files
3. **Begin** Phase 1 (Dataverse schema) via `/task-execute projects/smart-todo-decoupling-r3/tasks/001-*.poml`

---

**Status**: Ready for Tasks
**Next Action**: Generate POML task files via `/task-create`

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks. Phase 1 is the load-bearing schema cut — get reference-audit right before deleting `sprk_eventtodo`.*
