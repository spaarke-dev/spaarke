# Smart To Do — Decoupling from Events (R3) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-07
> **Source**: `projects/smart-todo-decoupling-r3/design.md`
> **Predecessors**: events-smart-todo-kanban-r1, events-smart-todo-kanban-r2

---

## Executive Summary

Replace the event-coupled `sprk_event + sprk_eventtodo` to-do model with a first-class `sprk_todo` custom entity that can stand alone or attach to any of eleven parent record types via Spaarke's multi-entity resolution pattern. Mirror `sprk_todo` records bidirectionally to Microsoft Graph `/me/todo`, surfacing them in Outlook, Teams Tasks, and the Microsoft To Do app for users who opt in. Update the Outlook add-in with a "Create To Do" ribbon action and an indicator for emails with existing Spaarke to-dos. Retire `sprk_eventtodo` entirely and remove the related event-side fields without backward-compatibility shims (pre-release, no migration).

---

## Scope

### In Scope

- **S-1** New custom entity `sprk_todo` with full attribute set (kanban behavior, detail, regarding fields, Graph sync state).
- **S-2** Eleven specific `sprk_regarding*` lookups + four `sprk_regardingrecord*` resolver fields, populated atomically via the shared `PolymorphicResolverService`.
- **S-3** Single User-only `sprk_assignedto` lookup; Owner can be a Team via standard `ownerid`.
- **S-4** Retirement of `sprk_eventtodo` entity and removal of `sprk_todoflag` / `sprk_todostatus` / `sprk_todocolumn` / `sprk_todopinned` from `sprk_event`.
- **S-5** Hoist `KanbanBoard`, `KanbanColumn`, `KanbanCard` from SmartTodo-local to `@spaarke/ui-components/Kanban/`.
- **S-6** Simplify `TodoDetail` shared component to single-entity load/save.
- **S-7** SmartTodo Code Page repointed at `sprk_todo`; "My Tasks" filter added.
- **S-8** CreateTodo wizard repointed at `sprk_todo`; `AssociateToStep` integrated for regarding.
- **S-9** "To Dos" subgrid added to each of the eleven parent entity main forms (Matter, Project, Event, Communication, WorkAssignment, Invoice, Budget, Analysis, Organization, Contact, Document).
- **S-10** Microsoft Graph `/me/todo` bidirectional sync with one "Spaarke" list per opted-in user, `linkedResources` carrying a Modern UCI deep link back to Dataverse, last-write-wins per-field timestamp conflict resolution, and initial backfill on opt-in.
- **S-11** New `Tasks.ReadWrite` delegated scope added to Azure AD app registration and reused via the existing OBO infrastructure.
- **S-12** Outlook add-in ribbon action "Create To Do" (email-read context) with regarding pre-filled to `sprk_regardingcommunication`.
- **S-13** Outlook add-in indicator (taskpane banner) showing count of Spaarke to-dos already linked to the current email.
- **S-14** All new and migrated UI uses Fluent v9 (`@fluentui/react-components`, semantic tokens, Griffel `makeStyles`).

### Out of Scope

- **OS-1** Backward-compatibility shims for `sprk_eventtodo` or `sprk_event.sprk_todo*`. Hard cut, pre-release.
- **OS-2** Data migration of existing dev-environment `sprk_event` (`todoflag=true`) or `sprk_eventtodo` rows.
- **OS-3** Microsoft Planner / group-owned task integration.
- **OS-4** Legacy `/me/outlook/tasks` API (deprecated by Microsoft Aug 2022).
- **OS-5** Custom Teams app, personal tab, or bot extension. Teams Tasks app already surfaces `/me/todo` lists "for free."
- **OS-6** Changes to the scoring formula, threshold defaults, drag/pin behavior, or general kanban UX.
- **OS-7** Editing existing to-dos from the Outlook add-in (v1 is create-only + indicator).
- **OS-8** Mobile-native Spaarke surface (mobile UX is delivered indirectly via MS To Do mobile app).

### Affected Areas

| Path | Description |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/Kanban/` | **NEW** — hoisted Kanban shared library |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/` | Simplified to single-entity load/save |
| `src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/` | Extended to support all eleven regarding targets |
| `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts` | Reuse — no changes expected |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateTodo/` | Rewritten to target `sprk_todo` |
| `src/solutions/SmartTodo/` | Entire Code Page repointed at `sprk_todo`; MyTasksFilter added |
| `src/solutions/TodoDetailSidePane/` | Audit + decision (retire or refactor) |
| `src/solutions/LegalWorkspace/src/components/SmartToDo/` | Updated to consume new shared `KanbanBoard` and `sprk_todo` data |
| `src/server/api/Sprk.Bff.Api/Services/Todo/` | **NEW** — `TodoGraphSyncHandler`, `DeepLinkBuilder`, `SpaarkeListProvisioner` |
| `src/server/api/Sprk.Bff.Api/Endpoints/GraphWebhookEndpoints.cs` | **NEW or extended** — `/api/graph/webhooks/todo` |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphSubscriptionManager.cs` | Extended for `/me/todo/lists/{id}/tasks` subscriptions |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` | `Tasks.ReadWrite` added to delegated scopes |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` | Route new `TodoSync` message type |
| `src/dataverse/plugins/...` | **NEW** — `SprkTodoSyncPlugin` (Create/Update on `sprk_todo` → Service Bus) |
| `src/client/office-addins/outlook/` | Ribbon action + indicator |
| Dataverse schema | `sprk_todo` created; `sprk_eventtodo` deleted; four fields removed from `sprk_event` |
| Parent forms (Matter, Project, Event, Communication, WorkAssignment, Invoice, Budget, Analysis, Organization, Contact, Document) | "To Dos" subgrid added |

---

## Requirements

### Functional Requirements

#### Schema & Data Model

1. **FR-01** — Create `sprk_todo` custom entity with the attribute set defined in design §4.1, including primary name (`sprk_name`), description, ownership (`ownerid`, `owningbusinessunit`), `sprk_assignedto` (User-only lookup → systemuser), state/status, kanban-behavior fields (`sprk_todocolumn`, `sprk_todopinned`, `sprk_priorityscore`, `sprk_effortscore`, `sprk_duedate`, `sprk_completedon`), detail (`sprk_notes` rich text), eleven `sprk_regarding*` specific lookups, four `sprk_regardingrecord*` resolver fields, and five Graph sync state fields (`sprk_graphtodolistid`, `sprk_graphtodotaskid`, `sprk_lastsyncedutc`, `sprk_synchash`, `sprk_syncerror`).
   - **Acceptance**: Solution import succeeds; entity visible in Power Apps maker portal; all attributes present with correct types/lengths matching design §4.1 table.

2. **FR-02** — Delete `sprk_eventtodo` entity entirely, including all attributes, the `sprk_eventtodo_RegardingEvent_n1` relationship, all saved queries, and the entity SVG.
   - **Acceptance**: `sprk_eventtodo` no longer present in solution; no references remain in `src/`; solution validates clean.

3. **FR-03** — Remove `sprk_todoflag`, `sprk_todostatus`, `sprk_todocolumn`, `sprk_todopinned` from `sprk_event`. Keep `sprk_priorityscore`, `sprk_effortscore`, `sprk_duedate` on `sprk_event` (per design D-4).
   - **Acceptance**: Four removed attributes absent from `sprk_event`; three retained attributes still present; no code references the removed attributes anywhere.

4. **FR-04** — Register `sprk_todo` in the `sprk_recordtype_ref` table per the polymorphic-resolver convention so existing entities can reference it (future-proofing).
   - **Acceptance**: `sprk_recordtype_ref` row with `sprk_recordlogicalname = "sprk_todo"` exists.

#### Multi-Entity Resolution (Regarding)

5. **FR-05** — When any one of the eleven `sprk_regarding*` lookups is set on a `sprk_todo`, the four resolver fields (`sprk_regardingrecordtype`, `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl`) MUST be populated atomically by [`PolymorphicResolverService.applyResolverFields`](src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts).
   - **Acceptance**: Unit tests verify all four fields populated on create + on regarding-change; only one specific lookup is set at a time; switching regarding clears the previous lookup.

6. **FR-06** — Standalone `sprk_todo` (no regarding) is a valid persisted state: all eleven specific lookups null AND all four resolver fields null.
   - **Acceptance**: Create-without-regarding flow succeeds; resolver fields remain null; record visible in kanban; no validation error.

7. **FR-07** — `AssociateToStep` shared component accepts a configurable list of eleven entity targets covering Matter, Project, Event, Communication, WorkAssignment, Invoice, Budget, Analysis, Organization, Contact (`sprk_contact` OOB), Document.
   - **Acceptance**: Component renders all eleven types in the picker; record-lookup dialog launches for each; selection returns correct entity type + record id + display name.

#### UI: Shared Library Migration

8. **FR-08** — Hoist `KanbanBoard`, `KanbanColumn`, `KanbanCard` from `src/solutions/SmartTodo/src/components/shared/` to `src/client/shared/Spaarke.UI.Components/src/components/Kanban/`. Exports added to the lib barrel.
   - **Acceptance**: Components compile and unit-test in `Spaarke.UI.Components`; SmartTodo Code Page imports them from `@spaarke/ui-components/Kanban` (no local copies remain); LegalWorkspace embedded widget consumes the shared version.

9. **FR-09** — `TodoDetail` shared component reduced to single-entity load/save; removes the parallel two-entity (`sprk_event` + `sprk_eventtodo`) load logic.
   - **Acceptance**: `loadTodoExtension`, `saveTodoExtensionFields`, `deactivateTodoExtension` are deleted; `loadTodoRecord` operates against `sprk_todo`; save is a single `updateRecord("sprk_todo", id, fields)`.

10. **FR-10** — All new and migrated UI components use Fluent v9 exclusively: `@fluentui/react-components`, semantic tokens (`tokens.*`), Griffel `makeStyles`. No Fluent v8, no inline `style={{}}`, no CSS modules.
    - **Acceptance**: Grep for `@fluentui/react@` (v8) returns no matches in changed files; lint rule (TBD by `fluent-v9-component` skill) passes.

#### UI: SmartTodo Code Page

11. **FR-11** — SmartTodo Code Page queries `sprk_todo` (NOT `sprk_event`) as the kanban data source. The `todoflag=true` filter is gone; all queries target the new entity.
    - **Acceptance**: Network trace shows requests to `/api/data/v9.x/sprk_todos`; no requests to `sprk_events` from the kanban path.

12. **FR-12** — "My Tasks" filter component added to `KanbanHeader` with three modes: **My Tasks** (default — owner OR assignee = current user), **Assigned to me** (assignee = current user), **All** (no filter). State persisted in user preferences alongside thresholds.
    - **Acceptance**: Toggle changes the OData filter; preference survives page reload; default is My Tasks.

13. **FR-13** — TodoDetail panel within SmartTodo uses `AssociateToStep` for regarding edit, allowing users to attach or change the parent record after creation.
    - **Acceptance**: Editing regarding from the panel re-runs `applyResolverFields` and saves; all four resolver fields update; UI shows the chosen parent's name + clickable URL.

14. **FR-14** — `FeedTodoSyncContext` cross-block sync events update payload shape from event-id to todo-id, keeping LegalWorkspace ActivityFeed and SmartToDo widget in sync in real time.
    - **Acceptance**: Mutating a `sprk_todo` in one block updates the other within one render cycle; no stale state across blocks.

#### UI: CreateTodo Wizard

15. **FR-15** — CreateTodo wizard creates `sprk_todo` (NOT `sprk_event` with `todoflag=true`).
    - **Acceptance**: Wizard's create call writes to `/api/data/v9.x/sprk_todos`; resulting record has correct entity logical name; no `sprk_event` row created.

16. **FR-16** — CreateTodo wizard includes a skippable `AssociateToStep` step. Launch context determines pre-fill:
    - From kanban "Add To Do" — no regarding pre-filled.
    - From a Matter / Project / Event / Communication / Contact / etc. ribbon button — regarding pre-filled to the launch record.
    - From Outlook add-in "Create To Do" — regarding pre-filled to `sprk_regardingcommunication`.
    - **Acceptance**: All three entry points exercised; pre-fill correct for each; skip succeeds and creates standalone todo.

#### Parent-Form Subgrids

17. **FR-17** — A "To Dos" subgrid is added to the main form of each of the eleven regarding-target entities (Matter, Project, Event, Communication, WorkAssignment, Invoice, Budget, Analysis, Organization, Contact, Document), filtered by the corresponding `sprk_regarding*` lookup. Default view shows only Active todos; "All" view available.
    - **Acceptance**: Each parent form renders subgrid; correct filter applied; row count matches direct OData query; subgrid view command bar includes "+ Create To Do" launching the wizard with regarding pre-filled.

#### Microsoft Graph Integration

18. **FR-18** — Add delegated scope `Tasks.ReadWrite` to the Azure AD app registration. Tenant admin consent obtained.
    - **Acceptance**: Token issued with `Tasks.ReadWrite` claim; OBO call to `/me/todo/lists` succeeds in a smoke test.

19. **FR-19** — Auto-provision a "Spaarke" list under `/me/todo/lists/` on first opt-in per user. Store the resulting `listId` and subscription metadata on a new `sprk_userpreference` row (preference type "MicrosoftToDoSync").
    - **Acceptance**: Opt-in toggle from off→on triggers list creation; user pref row created/updated with list id + subscription id + expires-utc; second opt-in same user reuses existing list (no duplicate).

20. **FR-20** — Initial backfill on opt-in: enumerate all Active `sprk_todo` records where the user is `ownerid` OR `sprk_assignedto`, create corresponding `todoTask` entries in the user's Spaarke list via Graph `$batch` (20 per request), with exponential backoff on 429. Track progress; persist `initialBackfillCompletedUtc` on completion. Resumable on interruption.
    - **Acceptance**: User with N existing todos sees all N appear in Microsoft To Do after opt-in; backfill survives BFF restart and resumes; 429 responses trigger backoff (verified via Graph throttle test); UI toast shows progress.

21. **FR-21** — Outbound sync: Dataverse plugin on `sprk_todo` Create/Update enqueues a Service Bus message `{ todoId, userId, op }`. `ServiceBusJobProcessor` dispatches to `TodoGraphSyncHandler` which performs the Graph PATCH/POST via OBO. `linkedResources[0]` is populated with `{ webUrl: ModernUCIDeepLink, externalId: todoId, applicationName: "Spaarke" }`.
    - **Acceptance**: Updating a `sprk_todo` field results in the mirrored `todoTask` changing within sync SLA (NFR-04); `linkedResources` populated correctly; deep link opens the Dataverse record in the host model-driven app.

22. **FR-22** — Inbound sync: per-user Graph change-notification subscription on `/me/todo/lists/{spaarkeListId}/tasks` posts to `/api/graph/webhooks/todo`. The endpoint validates the notification, fetches changes via delta query, locates the matching `sprk_todo` by `sprk_graphtodotaskid`, and PATCHes with changed fields. Subscription renewed nightly by a scheduled job before expiry.
    - **Acceptance**: Editing a task in Microsoft To Do app updates the corresponding `sprk_todo` within sync SLA; subscription renewal logged; expired subscription auto-replaced.

23. **FR-23** — Loop prevention combines (a) `sprk_synchash` (16-hex truncated SHA-256 of canonical JSON of synced fields), (b) thread-local "skip outbound" flag during inbound writes, (c) per-field last-write-wins by timestamp when hashes differ AND modification windows overlap.
    - **Acceptance**: A round-trip echo (outbound write → inbound notification of our own change) is dropped; simulated concurrent edit (both sides within 10s) results in newer side winning per field; conflict losers logged to `sprk_syncerror`.

24. **FR-24** — Field mapping per design §6.3: `sprk_name`↔`title`, `sprk_notes`↔`body.content`, `sprk_duedate`↔`dueDateTime`, state↔`status` (`notStarted`/`inProgress`/`completed`/`deferred`), `sprk_priorityscore` bucketed to `importance` (≥70 high / 30–69 normal / <30 low).
    - **Acceptance**: Each mapping verified bidirectionally; status mapping handles Dismissed → `deferred`; importance buckets enforced.

25. **FR-25** — `linkedResources[0].webUrl` uses the Modern UCI scheme: `https://{org}.crm.dynamics.com/apps/{appid}/r/sprk_todo/{id}`. A `DeepLinkBuilder` service in the BFF reads org URL + app id from configuration.
    - **Acceptance**: Clicking the linked resource in Microsoft To Do opens the `sprk_todo` form in the configured model-driven app; URL builder unit-tested.

26. **FR-26** — Graph sync is feature-gated via a configuration flag (`Spaarke:Graph:TodoSync:Enabled`). When disabled, registrations resolve to Null-Object implementations per [ADR-032](.claude/adr/ADR-032-bff-nullobject-kill-switch.md) so the BFF starts cleanly without Graph credentials.
    - **Acceptance**: Flag-off boot succeeds without Graph config; flag-on requires Graph config and runs sync; Null-Object handlers no-op silently when flag is off.

#### Outlook Add-in

27. **FR-27** — Outlook add-in (email-read context) exposes a ribbon action "Create To Do". On click, if the email is not already saved to Spaarke as `sprk_communication`, the existing save flow runs first. Then the CreateTodo wizard taskpane opens with `sprk_regardingcommunication` pre-filled to the saved/found communication id.
    - **Acceptance**: Manual test in Outlook web + desktop both succeed; pre-fill verified; wizard completes and creates `sprk_todo` with correct regarding.

28. **FR-28** — Outlook add-in shows a taskpane banner indicator on emails that already have one or more linked `sprk_todo` records (via `sprk_regardingcommunication`). Banner shows count and a link to view the list. Query: `GET sprk_todos?$filter=_sprk_regardingcommunication_value eq {commId}&$select=sprk_todoid,sprk_name,statecode&$top=10`. Results cached per `communicationid` for the add-in session.
    - **Acceptance**: Opening an email with 2 linked todos shows "This email has 2 Spaarke to-dos" banner; cache hit on re-open within session; banner absent when zero linked.

#### Tech Debt Removal

29. **FR-29** — All code paths that reference `sprk_eventtodo`, `sprk_event.sprk_todoflag`, `sprk_event.sprk_todostatus`, `sprk_event.sprk_todocolumn`, `sprk_event.sprk_todopinned`, `loadTodoExtension`, `saveTodoExtensionFields`, `deactivateTodoExtension`, or `_sprk_regardingevent_value` are deleted (NOT deprecated, NOT commented out).
    - **Acceptance**: Repo-wide grep returns no occurrences of any of the listed names outside this spec's own references and the `.claude/archive/` history; `dotnet build` and frontend build both clean.

30. **FR-30** — Documentation updates: [docs/architecture/event-to-do-architecture.md](docs/architecture/event-to-do-architecture.md) is marked **superseded** and a new `docs/architecture/spaarke-todo-architecture.md` replaces it. CLAUDE.md §16 pointer table updated. `repo-cleanup` skill is run as the final task.
    - **Acceptance**: Old architecture doc carries a "Superseded by …" header; new doc is the canonical reference; CLAUDE.md pointer points to the new file; `repo-cleanup` reports no orphans.

### Non-Functional Requirements

- **NFR-01** — Fluent v9 mandate (per CLAUDE.md and Spaarke standards). All new and modified components use `@fluentui/react-components`, semantic tokens, Griffel `makeStyles`. Verified by lint/build.
- **NFR-02** — Shared-library mandate. All Kanban + To Do UI primitives live in `@spaarke/ui-components`; SmartTodo Code Page is a thin domain consumer.
- **NFR-03** — BFF publish-size delta is justified and within bounds per CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`. Current baseline ~45.65 MB; ceiling 60 MB; any single-task delta ≥+5 MB requires explicit justification. Measure on every BFF-touching task.
- **NFR-04** — Sync SLA: Dataverse → Graph and Graph → Dataverse updates propagate within 60 seconds at p95 under normal load. Measured by instrumentation on the sync handler + webhook handler.
- **NFR-05** — Graph throttling resilience: any 429 response triggers exponential backoff (1s, 2s, 4s, 8s, capped at 30s). Sustained throttling triggers circuit-breaker (5 consecutive 429s → 5-minute pause).
- **NFR-06** — All Graph sync operations are audit-logged (correlation id, user id, todoId, op, result, latency). Logs land in the existing BFF logging pipeline.
- **NFR-07** — Subscription renewal: nightly scheduled job re-creates subscriptions whose `expiresUtc` is within the next 24 hours. Failure to renew is alarmed.
- **NFR-08** — Dataverse security: `sprk_todo` honors standard ownership + BU + role-based security. Users see only records their roles allow. Sync handler operates under OBO so it can never escalate privilege.
- **NFR-09** — Outlook add-in indicator: Dataverse query on email open completes within 500ms p95 (cached after first hit).
- **NFR-10** — Accessibility: keyboard navigation, screen-reader labels, high-contrast theme all preserved for kanban (no regression vs. R2); new "My Tasks" filter + Outlook banner are keyboard- and SR-accessible.
- **NFR-11** — Test coverage: every new service in BFF has unit tests (per CLAUDE.md §10 bullet 6 / `bff-extensions.md` §F.1–F.3). Coverage gate enforced by CI on touched files.
- **NFR-12** — Pre-release no-compat rule: no feature flags or shims for `sprk_eventtodo` / legacy event fields. Code is removed cleanly per FR-29.

---

## Technical Constraints

### Applicable ADRs

- **[ADR-001](.claude/adr/ADR-001-minimal-api.md)** — Minimal API pattern for new BFF endpoints (Graph webhook, sync trigger).
- **[ADR-008](.claude/adr/ADR-008-endpoint-filter-authorization.md)** — Endpoint filters for authorization; webhook MUST validate Graph's clientState + validation token.
- **[ADR-009](.claude/adr/ADR-009-graph-obo-token-caching.md)** (or current OBO ADR) — Reuse cached OBO token flow for `Tasks.ReadWrite`; do not introduce a new auth path.
- **[ADR-024](.claude/adr/ADR-024-polymorphic-resolver-pattern.md)** — Multi-entity resolution; MUST use `PolymorphicResolverService.applyResolverFields` to populate the four resolver fields atomically.
- **[ADR-028](.claude/adr/ADR-028-spaarke-auth-architecture.md)** — SSO + token issuance consistency.
- **[ADR-032](.claude/adr/ADR-032-bff-nullobject-kill-switch.md)** — Graph sync services MUST be feature-gated with Null-Object fallbacks so the BFF starts cleanly when sync is disabled.
- **CLAUDE.md §10** + **[`.claude/constraints/bff-extensions.md`](.claude/constraints/bff-extensions.md)** — BFF binding governance; mandatory pre-merge checklist for every BFF-touching task (placement decision, publish-size verification, CVE check, test obligation, asymmetric-registration anti-pattern guard).

### MUST Rules

- ✅ MUST use `PolymorphicResolverService.applyResolverFields` whenever any `sprk_regarding*` lookup is set or changed on `sprk_todo`. Never set the four resolver fields directly.
- ✅ MUST ensure at most one of the eleven `sprk_regarding*` lookups is populated at a time. Switching regarding clears the previous lookup.
- ✅ MUST use `@fluentui/react-components` v9.x with semantic tokens and Griffel `makeStyles`. No Fluent v8, no inline styles, no CSS modules, no external CSS files.
- ✅ MUST place all Kanban primitives in `@spaarke/ui-components`. SmartTodo Code Page imports from the shared lib only.
- ✅ MUST feature-gate Graph sync via `Spaarke:Graph:TodoSync:Enabled` with Null-Object fallbacks (ADR-032).
- ✅ MUST validate Graph change notifications: check `clientState`, respond to validation token within 10 seconds, verify `subscriptionId` matches an active subscription.
- ✅ MUST audit-log every sync operation with correlation id (NFR-06).
- ✅ MUST measure BFF publish size on every BFF-touching task and report delta vs. ~45.65 MB baseline (per CLAUDE.md §10).
- ✅ MUST add/update tests for every modified BFF service (`bff-extensions.md` §F test obligation).
- ❌ MUST NOT introduce backward-compatibility shims for `sprk_eventtodo` or legacy event todo fields.
- ❌ MUST NOT migrate dev-environment data. Fresh-start only.
- ❌ MUST NOT use the legacy Outlook Tasks API (`/me/outlook/tasks`) — deprecated.
- ❌ MUST NOT integrate with Microsoft Planner in this project.
- ❌ MUST NOT use Dataverse native polymorphic `regardingobjectid` lookup. Use the Spaarke multi-entity resolution pattern instead.
- ❌ MUST NOT use a Dataverse Activity entity for `sprk_todo`. Custom entity only.

### Existing Patterns to Follow

- **Multi-entity resolution example**: [docs/data-model/sprk_communication.md](docs/data-model/sprk_communication.md) is the canonical reference. Mirror its regarding shape.
- **Resolver service**: [`src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts`](src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts) — reuse as-is.
- **Regarding picker UI**: [`src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/AssociateToStep.tsx`](src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/AssociateToStep.tsx) — extend entity targets to all eleven.
- **Graph OBO + token caching**: [`src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`](src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs) — add `Tasks.ReadWrite` to the scope list.
- **Graph change-notification subscriptions**: [`src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphSubscriptionManager.cs`](src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphSubscriptionManager.cs) — extend to support `/me/todo/lists/{id}/tasks` resource.
- **Service Bus job processing**: [`src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs`](src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs) — add `TodoSync` message type and `TodoGraphSyncHandler` registration.
- **Wizard pattern**: [`src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/`](src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/) and `WizardShell` — CreateTodo becomes a thin wrapper.
- **Kanban (currently SmartTodo-local, ready to hoist)**: [`src/solutions/SmartTodo/src/components/shared/KanbanBoard.tsx`](src/solutions/SmartTodo/src/components/shared/KanbanBoard.tsx) — already Fluent-v9-compliant, domain-agnostic.

---

## Success Criteria

1. [ ] **`sprk_todo` entity present**; `sprk_eventtodo` deleted; four to-do fields removed from `sprk_event` — verify by solution diff + Power Apps maker portal.
2. [ ] **Multi-entity resolution working** for all eleven regarding targets — verify by creating one `sprk_todo` against each target type and inspecting all four resolver fields.
3. [ ] **Standalone `sprk_todo` creation works** — verify by creating from kanban "Add To Do" with no regarding; all eleven lookups null, all four resolver fields null, record visible in My Tasks.
4. [ ] **Kanban runs entirely from `@spaarke/ui-components`** — verify by grepping `src/solutions/SmartTodo/` for local `KanbanBoard` imports (should be zero).
5. [ ] **Fluent v9 compliance** — verify by Fluent v9 lint pass on every changed file.
6. [ ] **"My Tasks" filter** works and persists — verify manually + by unit test.
7. [ ] **All eleven parent forms** show "To Dos" subgrid with correct filter — verify by opening each form.
8. [ ] **CreateTodo wizard** creates `sprk_todo` from kanban, parent-form ribbon, and Outlook add-in — verify all three entry points.
9. [ ] **Microsoft To Do bidirectional sync works end-to-end** — verify by:
   - Opt-in toggle creates Spaarke list ✓
   - All existing Active todos backfilled ✓
   - Edit in Spaarke kanban → reflects in Microsoft To Do within 60s ✓
   - Edit in Microsoft To Do app → reflects in Spaarke within 60s ✓
   - Edit on both sides simultaneously → newer side wins per field ✓
   - Click linked resource in MS To Do → opens Modern UCI Spaarke form ✓
10. [ ] **Outlook ribbon "Create To Do"** creates a `sprk_todo` with `sprk_regardingcommunication` pre-filled — verify in Outlook web + desktop.
11. [ ] **Outlook indicator** appears on emails with existing Spaarke to-dos — verify with 0, 1, and N linked todos.
12. [ ] **No legacy references** — repo-wide grep for `sprk_eventtodo`, `sprk_todoflag`, etc. returns zero hits in `src/`.
13. [ ] **Graph sync feature-gates correctly** — verify BFF boots cleanly with sync flag off; ADR-032 Null-Object pattern verified.
14. [ ] **BFF publish size** within 60 MB ceiling; delta justified — measured + reported per task per CLAUDE.md §10.
15. [ ] **Test coverage** — every modified BFF service has unit tests (CI gate).
16. [ ] **No HIGH-severity CVEs** introduced — `dotnet list package --vulnerable --include-transitive` clean.
17. [ ] **Architecture doc replaced** — `event-to-do-architecture.md` marked superseded; new `spaarke-todo-architecture.md` published; CLAUDE.md §16 updated.
18. [ ] **`repo-cleanup`** reports clean — no orphan files from the migration.

---

## Dependencies

### Prerequisites

- **D-1** Azure AD app registration update: `Tasks.ReadWrite` delegated scope added; tenant admin consent obtained. Owner: deployment.
- **D-2** Existing `PolymorphicResolverService`, `AssociateToStep`, `GraphClientFactory`, `GraphSubscriptionManager`, `ServiceBusJobProcessor` are stable (no in-flight refactors). Verified at project kickoff.
- **D-3** Existing Fluent v9 + semantic-token conventions in `@spaarke/ui-components` are stable.
- **D-4** Modern UCI app id available in BFF configuration (`Spaarke:ModelDrivenApps:DefaultAppId`) — coordinate with environment owner.
- **D-5** Service Bus `todosync` queue or topic provisioned per environment (dev/test/prod).

### External Dependencies

- **D-6** Microsoft Graph `/me/todo` endpoint availability (production stable).
- **D-7** Microsoft Graph change-notification webhook reachability — BFF webhook URL must be publicly resolvable + TLS-terminated (use existing public BFF endpoint).
- **D-8** Microsoft To Do client apps (desktop / mobile / web) for end-to-end verification of the visible-to-user surfaces.
- **D-9** Outlook for testing the add-in (web + desktop) per the existing add-in deploy procedure (`/office-addins-deploy`).

---

## Owner Clarifications

Captured during design + design-to-spec discussion (2026-06-07):

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Entity choice | Activity vs. custom entity for `sprk_todo`? | Custom entity (Spaarke does not use activities) | `sprk_todo` is a fully customizable entity; kanban + scoring fit naturally |
| Association pattern | Polymorphic lookup vs. multi-entity resolution? | Multi-entity resolution (Spaarke standard) | Reuses ADR-024 / `PolymorphicResolverService` / `AssociateToStep`; 11 specific lookups + 4 resolver fields |
| Backward compatibility | Shims or hard cut? | Hard cut, pre-release, no compat | `sprk_eventtodo` deleted; legacy event fields removed; FR-29 enforces removal |
| Data migration | Migrate existing dev data? | No | Fresh-start; testing uses newly created records |
| Regarding targets | Which entities? | Matter, Project, Event, Communication, WorkAssignment, Invoice, Budget, Analysis, Organization, Contact (`sprk_contact` OOB), Document | 11 specific lookups defined in FR-01 |
| AssignedTo | User or Team? | Single User-only lookup (`sprk_assignedto` → systemuser). Teams own via standard `ownerid`. | Simpler schema; explicit team-assignment out of scope for R3 |
| Event-side scores | Move `sprk_priorityscore` / `sprk_effortscore` / `sprk_duedate` to `sprk_todo`? | Keep on `sprk_event`; `sprk_todo` gets its OWN copies | Events retain prioritization for their own use cases; FR-03 |
| MS To Do sync opt-in | Always-on / per-user / per-record? | Per-user toggle (option B) | Single user-preference flag; FR-19 / FR-20 |
| Initial sync on opt-in | Backfill or forward-only? | Backfill all Active owned-by-or-assigned-to todos with batched throttling | FR-20 |
| Conflict resolution | Dataverse-wins / Graph-wins / LWW? | Last-write-wins per-field by timestamp | FR-23 / design §6.4 |
| Deep link scheme | Maker / model-driven / modern UCI? | Modern UCI (`/apps/{appid}/r/{etn}/{id}`) | FR-25 / `DeepLinkBuilder` |
| Outlook add-in v1 scope | Ribbon + indicator? | Ribbon action "Create To Do" + taskpane banner indicator | FR-27 / FR-28 |
| UI library | Local vs. shared? | Shared mandatory; migrate Kanban if not already shared | FR-08 / NFR-02 |
| UI framework | Fluent v9 mandatory? | Yes, mandatory | FR-10 / NFR-01 |

---

## Assumptions

Proceeding with these (owner did not explicitly specify; reasonable defaults applied):

- **A-1** **Outlook indicator UX option** — using **option (A)** from design §7.2 (pinned message banner inside the add-in taskpane) as the default. Reassess in implementation if Office.js `notificationMessages` proves more discoverable.
- **A-2** **`TodoDetailSidePane` solution** — assuming retirement at Phase 9 unless an audit of ribbon / app module references reveals an unavoidable consumer. If retained, it becomes a thin shell around the shared `TodoDetail` component (no two-entity logic).
- **A-3** **Subscription model** — one subscription per user on `/me/todo/lists/{spaarkeListId}/tasks`. Assuming current Microsoft Graph limits accommodate the expected user base (~< 5000 subscriptions per app per tenant for `todoTask`). If limit becomes a constraint, fall back to delta-query polling.
- **A-4** **Importance bucketing** — assuming priorityscore thresholds 70 (high) and 30 (low) match user expectations. If field feedback requests different cutoffs, the constants are central in the field-mapping module.
- **A-5** **Initial backfill cap** — assuming backfill batch size of 20 per `$batch` request with up to 5 concurrent batches. Tunable.
- **A-6** **"My Tasks" filter team semantics** — for `My Tasks` mode (per design R-8): `ownerid eq @currentuser OR sprk_assignedto eq @currentuser OR ownerid eq team-where-currentuser-is-member`. Last clause uses standard Dataverse "owned by my teams" semantics.
- **A-7** **Phase parallelism** — Phases 2 (shared-lib migration) and 6 (Graph foundation: scope + auth + user-pref) can proceed in parallel. Phases 1 (schema) blocks everything that touches `sprk_todo`. Phases 7–8 (sync engine + add-in updates) depend on Phase 6.
- **A-8** **Webhook URL** — using the existing public BFF endpoint hostname (no new public ingress required); webhook path is `/api/graph/webhooks/todo`. Public reachability is a prerequisite already satisfied for email change notifications.
- **A-9** **DeepLinkBuilder configuration** — read app id and org URL from `Spaarke:ModelDrivenApps:DefaultAppId` and `Spaarke:Environment:OrgUrl` per existing config conventions. If multiple model-driven apps host `sprk_todo`, a single canonical app id is chosen per environment.

---

## Unresolved Questions

These do NOT block design-to-spec but should be answered during plan / task phase:

- [ ] **UQ-1** Should the `Sprk*` Dataverse plugin for outbound sync live in the existing `Sprk.Dataverse.Plugins` assembly, or a new assembly dedicated to To Do? Decision affects deploy bundle composition. *Blocks*: Phase 7 plugin task.
- [ ] **UQ-2** What is the canonical Modern UCI **app id** for the `sprk_todo` form per environment (dev, test, prod)? Requires environment-owner confirmation. *Blocks*: FR-25 / `DeepLinkBuilder` configuration in deployment.
- [ ] **UQ-3** Where should the Graph webhook authentication secret (clientState seed) be stored — Key Vault, BFF config, environment variable? Default proposal: Key Vault with rotation via the existing secret-rotation pipeline. *Blocks*: FR-22 hardening.
- [ ] **UQ-4** Whether `AssociateToStep` needs an enhancement to support **default-view per entity type** (so e.g. the Matter picker opens on "My Open Matters" instead of "Active Matters"). Default proposal: use OOB default views; revisit only on field feedback. *Blocks*: Phase 4 polish.
- [ ] **UQ-5** Form / model-driven-app placement of `sprk_todo` — is there a single canonical app where `sprk_todo` records should be reachable from the sitemap, or is it kanban-only with no main-grid entry? Default proposal: add to the LegalWorkspace app sitemap and to one administrative app. *Blocks*: Phase 1 polish.

---

*AI-optimized specification. Original design: `projects/smart-todo-decoupling-r3/design.md` (preserved verbatim alongside this spec).*
