# Spaarke Platform Foundations (R3) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-20
> **Source**: `design.md` (v2 — architectural sharpening 2026-06-20)
> **Predecessor**: `projects/spaarke-daily-update-service-r2/` (R2 — surfaced the gaps this project resolves)

---

## Executive Summary

R3 addresses three cross-cutting platform gaps surfaced during R2 UAT that affect every entity-aware UI surface, every Notification playbook, and every scheduled background process in Spaarke:

1. **User-record membership resolution** — replaces ad-hoc per-playbook FetchXML with a canonical, discovery-based `MembershipResolverService` + endpoint, junction-table materialization (Phase 2), and event-driven sync via Service Bus.
2. **Background-job infrastructure** — introduces a shared `Spaarke.Scheduling` library + `sprk_backgroundjob*` Dataverse entities + admin endpoints; ships two reference consumers (junction-recon + migrated `PlaybookSchedulerService`).
3. **Playbook engine hardening** — fixes eleven known pitfalls (Handlebars `??` silent breakage, OutputVariable rename, canvas/server mapping drift, `sprk_searchindexed` misleading status, etc.).

All three ship together as R3 — same audience (BFF + playbook engine + builder), same review surface, single ADR/doc update round.

---

## Scope

### In Scope (R3)

- **Part 1 / Phase 1A**: Discovery-based `MembershipResolverService` (metadata-driven Lookup field discovery + per-entity overrides + identity normalization)
- **Part 1 / Phase 1B**: `LookupUserMembership` playbook node executor (`ActionType = 52`) + `joinIds` Handlebars helper
- **Part 1 / Phase 1C**: Migrate `notification-new-documents.json` + audit `notification-new-emails.json` + `notification-new-events.json`
- **Part 1 / Phase 1D**: Transitive memberships (`includeRelated=documents,events`) — **firm in-scope per owner decision (2026-06-20)**
- **Part 1 / Phase 2**: Junction table `sprk_userentityassociation` + event-driven Service Bus sync (topic `sprk-membership-changes` with subscription-per-consumer) + real `MembershipReconciliationJob` (nightly) + Redis pub/sub cache invalidation — **firm in-scope per owner decision (2026-06-20)**
- **Part 2**: `Spaarke.Scheduling` library + `sprk_backgroundjob` / `sprk_backgroundjobrun` entities + `/api/admin/jobs/*` endpoints + cron parsing via `Cronos` NuGet
- **Part 2 reference consumers**: `MembershipReconciliationJob` (real logic, not no-op) + `PlaybookSchedulerService` migration (single `sprk_backgroundjob` row, preserves current 1:1 behavior)
- **Part 3 / Workstream H1**: `default` + `joinIds` Handlebars helpers + migrate 2 broken playbooks + unrendered-template runtime warning
- **Part 3 / Workstream H2**: Builder UI affordances (OutputVariable rename guard, branch wiring auto-gen, edge perf hint)
- **Part 3 / Workstream H3**: Canvas-server mapping drift test (CI) + `sprk_searchindexed` → dual-field migration + node-executor authoring pattern doc
- **ADRs**: ADR-034 (Membership) + ADR-036 (Background-job infrastructure)
- **Docs**: `playbook-architecture.md` Known Pitfalls refresh + new pattern doc + data-model doc refresh for `sprk_matter`

### Out of Scope (deferred)

- **R2.2 UX hotfix** (TL;DR bullets, due dates, AddToTodo improvements) — ships independently on `work/spaarke-daily-update-service-r2-hotfix` branch
- **Phase 3 — AI Search integration** (`associatedPersons[]` field in indexed documents) — design-only in R3; build when consumer entity gets a search index
- **Identity matching for free-text display-name fields** — explicitly NOT supported
- **Migration of consumer UI surfaces** (Dashboard tiles, list filters, etc.) to the new endpoint — separate adoption project
- **Migration of the other 26 `BackgroundService` implementations** to `Spaarke.Scheduling` — opportunistic over time
- **Migration of queue-consumer services** (`ServiceBusJobProcessor` family) — different shape, out of scope
- **G11 / H4 rollout-mode** (Disabled / PilotUsers / AllUsers for notification playbooks) — new R4 project `spaarke-playbook-rollout-mode-r4` + ADR-035
- **Producer-layer playbook additions** (new notification categories, new entity sources)
- **Dataverse-stored membership config** (`sprk_membershipconfig` entity) — `appsettings.json` sufficient at R3 scale; revisit in R4 if makers ask for runtime control

### Explicitly NOT Changing

- Daily Briefing widget / standalone code page UX (R2 / R2.2 territory)
- BFF `/narrate` endpoint contract
- `appnotification` schema
- `sprk_processingjob` schema (kept Office-scoped)
- Pattern D dual-use conventions (per ADR-012)
- Existing `POST /api/ai/rag/index*` per-document manual triggers (retained alongside new `/api/admin/jobs/*` framework)
- Existing FieldMapping framework (`sprk_fieldmappingprofile` / `sprk_fieldmappingrule` + `AssociationResolver` PCF) — different concept, name disambiguated via "Membership" terminology

### Affected Areas

| Path | Purpose |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/` (NEW) | `MembershipResolverService`, discovery, identity normalization |
| `src/server/api/Sprk.Bff.Api/Api/Membership/` (NEW) | Membership endpoint + admin endpoints |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` (NEW) | Phase 1B node executor |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` | Add `LookupUserMembership = 52` to `ActionType` enum |
| `src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs` | Register `default` + `joinIds` helpers |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` | Unrendered-template runtime warning |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/DeliverToIndexNodeExecutor.cs` | Write `sprk_searchindexqueuedon` + `sprk_searchindexcompletedon` |
| `src/server/shared/Spaarke.Scheduling/` (NEW) | New shared library |
| `src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs` (NEW) | `/api/admin/jobs/*` |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerService.cs` (REFACTOR) | Migrate to `Spaarke.Scheduling` |
| `src/server/api/Sprk.Bff.Api/Services/Ai/MembershipReconciliationJob.cs` (NEW) | Phase 2 real recon |
| `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts` | Add `LookupUserMembership = 52` to canvas/server mapping |
| `src/client/code-pages/PlaybookBuilder/src/components/` (TBD) | Builder UI affordances (H2) |
| `projects/spaarke-daily-update-service/notes/playbooks/notification-new-documents.json` | Migrate to `LookupUserMembership` node |
| `tests/integration/PlaybookBuilder.Tests/` (NEW or extend) | Canvas-server mapping drift test |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/` (NEW) | Membership unit tests |
| `tests/unit/Spaarke.Scheduling.Tests/` (NEW) | `ScheduledJobHost` + cron parsing tests |
| `.claude/adr/ADR-034-user-record-membership.md` (NEW) | Membership ADR |
| `.claude/adr/ADR-036-background-job-infrastructure.md` (NEW) | Scheduling ADR |
| `.claude/patterns/ai/node-executor-authoring.md` (NEW) | Singleton/Scoped DI pattern doc |
| `docs/architecture/playbook-architecture.md` | Known Pitfalls refresh |
| `docs/data-model/sprk_matter-related-tables.md` | Refresh with actual `sprk_matter` columns |

---

## Requirements

### Functional Requirements — Part 1 (Membership)

**Phase 1A — Discovery-based resolver**

1. **FR-1A.1** — `MembershipFieldDiscoveryService` queries Dataverse `EntityDefinitions(LogicalName=…)?$expand=Attributes(...)` filtered to Lookup attributes whose `Targets[]` includes a configured identity table. Acceptance: discovery returns expected fields per AC-1A.1.
2. **FR-1A.2** — Global field exclusions (`createdby`, `modifiedby`, `createdonbehalfby`, `modifiedonbehalfby`) applied before per-entity overrides. Acceptance: system fields never appear in discovered descriptors.
3. **FR-1A.3** — Per-entity overrides supported in `appsettings.json` (`ExcludedFields`, `IncludedFields`, `FieldRoleOverrides`). Acceptance: `sprk_assignedlawfirm1` and `sprk_assignedlawfirm2` both resolve to role `"assignedLawFirm"` per design example.
4. **FR-1A.4** — Role-name strategy `CamelCase` strips `sprk_` prefix + trailing numeric digits + camelCases (e.g., `sprk_AssignedAttorney1` → `"assignedAttorney"`). Acceptance: unit test covers `sprk_*N` → `roleN` patterns.
5. **FR-1A.5** — `IdentityNormalizationService` resolves `systemuserid` → `{contactId, primaryEmail, teamIds[], businessUnitId}`. Contact↔SystemUser cross-reference via `azureactivedirectoryobjectid` per ADR-028. Cached in Redis with 10-min TTL.
6. **FR-1A.6** — Identity-type resolution per design table (Lookup→systemuser direct, Lookup→contact cross-ref, Lookup→team expand `teammembership`, Lookup→businessunit + descendants per config, Lookup→account via primary `parentcustomerid`, Lookup→sprk_organization via configured mapping). **Correction from design (per owner 2026-06-20)**: `sprk_assignedlawfirm1` / `sprk_assignedlawfirm2` Lookup target is `sprk_organization` (NOT `contact` as shown in the design's Discovery Report example) → `identityType: "Organization"`. Role-name override remains `"assignedLawFirm"` for both fields. Acceptance: AC-1A.4.
7. **FR-1A.7** — Metadata cache: per-entity-type runtime descriptor list, 1-hour TTL by default (configurable via `MetadataCacheTtlMinutes`).
8. **FR-1A.8** — Per-user membership cache: Redis, 5-min TTL, no write invalidation in Phase 1A (Phase 2 invalidates via pub/sub — FR-2P2.6).
9. **FR-1A.9** — `GET /api/users/me/memberships/{entityType}` endpoint per design contract (query params: `roles`, `identityTypes`, `includeRelated`, `limit`, `continuationToken`). Auth: standard Spaarke Auth v2 OBO (ADR-028). Response shape per design §Endpoint contract.
10. **FR-1A.10** — `GET /api/admin/membership/discovered/{entityType}` — operator audit endpoint. `RequireAuthorization("SystemAdmin")`. Response: discovered fields (with `source: "auto" | "override"`), excluded fields (with reason), ignored fields (with target table).
11. **FR-1A.11** — `POST /api/admin/membership/refresh-metadata` — invalidates metadata cache. `RequireAuthorization("SystemAdmin")`.

**Phase 1B — Playbook node executor**

12. **FR-1B.1** — `LookupUserMembershipNodeExecutor` (new) handles `ActionType.LookupUserMembership = 52` (slots into existing Dataverse-data-ops group; 51=QueryDataverse, 60=AgentService). Calls the membership endpoint internally (in-process — does NOT round-trip HTTP), binds resolved IDs to node's `OutputVariable`.
13. **FR-1B.2** — `joinIds` Handlebars helper registered in `TemplateEngine.cs`: `{{joinIds arr}}` → comma-separated list suitable for FetchXML `operator='in'` clauses.
14. **FR-1B.3** — Canvas-server mapping added to `playbookNodeSync.ts` (client) AND `NodeService.cs` (server) AND `ActionType` enum in `INodeExecutor.cs`.
14b. **FR-1B.4** — New properties form `LookupUserMembershipForm.tsx` in `src/client/code-pages/PlaybookBuilder/src/components/properties/` (follows the existing per-ActionType form pattern — e.g., `CreateNotificationForm.tsx`, `QueryDataverseForm.tsx`). Registered in `properties/index.ts` and wired into `NodePropertiesDialog.tsx`. Validation added to `canvasValidation.ts`.

**Phase 1C — Playbook migration**

15. **FR-1C.1** — `notification-new-documents.json` migrated to use `LookupUserMembership` node + `joinIds` helper (replaces broken `sprk_matterteammember` join). Acceptance: AC-1C.1.
16. **FR-1C.2** — `notification-new-emails.json` audited; if it uses similar broken pattern, migrate identically.
17. **FR-1C.3** — `notification-new-events.json` audited; if it uses similar broken pattern, migrate identically.
18. **FR-1C.4** — Integration tests: run each notification playbook end-to-end against seeded `spaarkedev1` user fixture; assert `count > 0`.

**Phase 1D — Transitive memberships** *(promoted to firm in-scope by owner 2026-06-20)*

19. **FR-1D.1** — `includeRelated=documents,events` query parameter on `GET /api/users/me/memberships/{entityType}` supports chained discovery (e.g., documents on matters I'm on, events on matters I'm on).
20. **FR-1D.2** — Chained-discovery algorithm: resolve primary entity memberships → for each related-entity type in `includeRelated`, discover its Lookup fields → join via FetchXML / Dataverse expansion. **Max chain depth: 1 hop** (per owner decision 2026-06-20). Requests like `matter → document → comment → reply` are rejected with `400 BadRequest`. Performance budget: AC-1A.5 still holds (≤300ms p95) — if chained query degrades, document the limit and reject `includeRelated` requests beyond it.
21. **FR-1D.3** — Response shape extends `byRole` map with related-entity sub-keys per design (TBD final shape — settled during implementation if not in design; assume `byRole.documents.{role}` nested structure).

### Functional Requirements — Part 1 Phase 2 *(promoted to firm in-scope by owner 2026-06-20)*

22. **FR-2P2.1** — `sprk_userentityassociation` Dataverse entity created with 7 columns per design (`sprk_personid`, `sprk_personidtype`, `sprk_entitylogicalname`, `sprk_entityrecordid`, `sprk_role`, `sprk_sourcefield`, `sprk_lastsyncedon`). Indexes on `{sprk_personid, sprk_entitylogicalname}` + `{sprk_entitylogicalname, sprk_entityrecordid}` for both query directions.
23. **FR-2P2.2** — `MembershipChangedEvent` payload (`{personId, personIdType, entityLogicalName, entityRecordId, sourceField, role, mutationType: "added"|"removed"|"updated", correlationId}`).
24. **FR-2P2.3** — Service Bus **topic** `sprk-membership-changes` (NOT queue, NOT reuse `ServiceBusJobProcessor` queue — D3 resolved 2026-06-20). One subscription `recon-junction-updater` ships in R3; future consumers add subscriptions without infra migration.
25. **FR-2P2.4** — `MembershipJunctionUpdater` handler (consumes subscription) upserts/deletes junction rows on event receipt. Idempotency: handler is keyed on `{personId, entityRecordId, sourceField}` so duplicate event delivery is safe.
26. **FR-2P2.5** — **Discovery task P-event-1** *(per owner decision 2026-06-20)*: inventory all BFF mutation endpoints touching person/team/BU lookups on matter / document / event / task / opportunity entities. Produces a checklist; subsequent FRs hook each endpoint.
27. **FR-2P2.6** — Event-publishing wired into discovered endpoints (per FR-2P2.5 inventory). Each mutation endpoint publishes `MembershipChangedEvent` to the topic when a lookup field is added / removed / changed. **Semantics: fire-and-forget** (per owner decision 2026-06-20) — publish is best-effort; the mutation succeeds even if the publish fails. Nightly `MembershipReconciliationJob` (FR-2P2.7) is the defense-in-depth backstop for missed events. Log publish failures as structured warnings (correlationId-tagged) for diagnostic visibility.
28. **FR-2P2.7** — `MembershipReconciliationJob` (Part 2 reference consumer — real logic, NOT no-op): scheduled nightly via `Spaarke.Scheduling`. Scans source-of-truth lookups for configured entities → compares to junction rows → upserts missing rows, removes orphans. Reports `ProcessedItems` (rows touched) per run. Catches drift from mutations that bypass BFF (e.g., maker portal direct edits).
29. **FR-2P2.8** — Redis pub/sub cache invalidation: junction-row writes (by either event handler OR recon job) publish to Redis channel `membership-cache-invalidate` carrying `{userId, entityType}`. Subscribers (BFF instances) evict matching cache entries.
30. **FR-2P2.9** — Endpoint contract unchanged from Phase 1A — internal switch from per-request FetchXML to junction-table query is opaque to consumers. Strangler-fig pattern per design.

### Functional Requirements — Part 2 (Background-Job Infrastructure)

31. **FR-2.1** — `Spaarke.Scheduling` library created at `src/server/shared/Spaarke.Scheduling/`. Depends only on `Spaarke.Core`. Contracts: `IScheduledJob`, `JobRunContext` (record), `JobRunResult` (record), `JobRunTrigger` (enum: Scheduled, ManualAdmin, OnStartup), `ScheduledJobHost : BackgroundService`.
32. **FR-2.2** — Cron parsing via `Cronos` NuGet (D4 — confirmed mature, ~50KB, MIT-licensed). Supports full cron expression syntax for `sprk_cronschedule` field.
33. **FR-2.3** — `ScheduledJobHost` reads `sprk_backgroundjob` rows on startup + refreshes hourly. For each enabled job: parses cron → manages per-job `PeriodicTimer` → invokes `IScheduledJob.ExecuteAsync` on schedule → records `sprk_backgroundjobrun`. Handles failure, retry (with backoff), idempotency.
34. **FR-2.4** — `sprk_backgroundjob` Dataverse entity created with columns per design (`sprk_jobid` unique key, `sprk_displayname`, `sprk_description`, `sprk_handlertype` FQ class name, `sprk_enabled`, `sprk_cronschedule`, `sprk_configjson`, `sprk_lastrunstartedon`, `sprk_lastruncompletedon`, `sprk_lastrunstatus` OptionSet, `sprk_lastrunerror`).
35. **FR-2.5** — `sprk_backgroundjobrun` Dataverse entity created with columns per design (`sprk_backgroundjob` lookup, `sprk_runid`, `sprk_trigger` OptionSet, `sprk_correlationid`, `sprk_startedon`, `sprk_completedon`, `sprk_status` OptionSet, `sprk_errormessage`, `sprk_processeditems`, `sprk_resultjson`).
36. **FR-2.6** — Admin endpoints under `/api/admin/jobs/*` — all `RequireAuthorization("SystemAdmin")`:
    - `GET /api/admin/jobs` — list registered jobs + status (last run, next scheduled)
    - `GET /api/admin/jobs/{jobId}/status` — detailed status + last 10 runs
    - `GET /api/admin/jobs/{jobId}/history?limit=50` — run history
    - `POST /api/admin/jobs/{jobId}/trigger` — run NOW; returns `{runId, status, startedAt}`
    - `POST /api/admin/jobs/{jobId}/enable` — enable scheduled execution
    - `POST /api/admin/jobs/{jobId}/disable` — disable without removing
37. **FR-2.7** — `MembershipReconciliationJob` registered as `IScheduledJob` (real logic per FR-2P2.7).
38. **FR-2.8** — `PlaybookSchedulerService` migrated to `Spaarke.Scheduling` (REFACTOR — D2). Becomes a **single** `sprk_backgroundjob` row (`sprk_jobid = "notification-playbook-scheduler"`) that internally fans out across the 7 active playbooks (preserves current 1:1 behavior). **Correlation behavior: each child playbook run gets a fresh `correlationId`** (per owner decision 2026-06-20). The parent scheduler run's `correlationId` is recorded on the `sprk_backgroundjobrun` row; each fanned-out playbook's correlationId is generated at fan-out time and propagated through the playbook execution chain. Operators can join parent ↔ children via the parent run's `sprk_resultjson` (which records the child correlationIds). Per-playbook "Run Now" deferred to a follow-up. Existing `sprk_analysisplaybook.sprk_configjson` schedule fields are migrated; scheduler becomes a thin adapter on `Spaarke.Scheduling`.

### Functional Requirements — Part 3 (Playbook Engine Hardening)

**Workstream H1 — Template engine + runtime detection (G1 + G10)**

39. **FR-3H1.1** — `default` Handlebars helper registered in `TemplateEngine.cs`: `{{default X 'Y'}}` returns `X` if non-empty, else `'Y'`. Tested via unit tests.
40. **FR-3H1.2** — `joinIds` Handlebars helper registered in `TemplateEngine.cs` (overlaps with FR-1B.2 — single implementation).
41. **FR-3H1.3** — 2 known-broken playbooks migrated from `{{X ?? 'Y'}}` to `{{default X 'Y'}}`: `notification-tasks-due-soon.json` + one other (TBD — task surfaces during implementation via grep `\?\?` in playbook JSON).
42. **FR-3H1.4** — Runtime unrendered-template warning in `PlaybookOrchestrationService.cs`: after each node executes, if any string field in `NodeOutput` contains literal `{{`, log structured warning AND emit `PlaybookStreamEvent` with type `unrendered-template-detected`.

**Workstream H2 — Builder validation + UI affordances (G2 + G3 + G9)**

43. **FR-3H2.1** — `OutputVariable` rename guard in Builder UI (`src/client/code-pages/PlaybookBuilder/`): when user edits a node's `OutputVariable` (via `NodePropertiesForm.tsx` / `NodePropertiesDialog.tsx`), scan all other nodes for `{{<oldName>.output*}}` references. Reuse existing `VariableReferencePanel.tsx` infrastructure for the find-references query. If references found, present dialog: "(a) Auto-rename — find/replace all references; (b) Keep old name; (c) Continue and break (advanced)." Default to (a). New validation rule added to `services/canvasValidation.ts`.
44. **FR-3H2.2** — Branch wiring auto-generation in Builder UI: when an edge connects a Condition node (handled via existing `ConditionEditor.tsx` properties form) to a downstream node, prompt for branch (`true` / `false` / `both`); persist in `DependsOn` branch metadata; visualize edges differently per branch. Edge rendering changes in `components/edges/`; persistence via existing `services/playbookNodeSync.ts`.
45. **FR-3H2.3** — Edge perf hint in Builder UI: when an edge connects two nodes whose configs don't reference each other's `OutputVariable`, show non-blocking warning via `NodeValidationBadge.tsx` or equivalent: "This edge forces sequential execution. Confirm or remove?" Advisory only (does not block save). Validation rule added to `services/canvasValidation.ts`. **Align all three affordances with existing PlaybookBuilder componentry patterns — do not invent new patterns; extend the existing per-ActionType form / validation / dialog architecture.**

**Workstream H3 — Schema + DI hardening (G6 + G7 + G8)**

46. **FR-3H3.1** — Canvas-server mapping drift integration test in `tests/integration/PlaybookBuilder.Tests/`: asserts every canvas type in `playbookNodeSync.ts` has a corresponding entry in `NodeService.cs`. Fails CI build on drift.
47. **FR-3H3.2** — `sprk_searchindexed` rename + dual flag (schema migration):
    - Rename `sprk_searchindexed` (bool) → `sprk_searchindexqueuedon` (datetime, set when job enqueued)
    - Add `sprk_searchindexcompletedon` (datetime, set when indexing completes per AI Search confirmation)
    - Update `DeliverToIndexNodeExecutor.cs` to write new fields
48. **FR-3H3.3** — **Discovery task P7.0** *(per owner decision 2026-06-20)*: inventory all consumers of `sprk_searchindexed` (UI tiles, queries, OData filters, FetchXML, code-paths). Produces checklist of every reference.
49. **FR-3H3.4** — Migrate all consumers identified in FR-3H3.3 to read `sprk_searchindexqueuedon` / `sprk_searchindexcompletedon`. Maintain `sprk_searchindexed` as dual-write during transition; remove after consumer migration confirmed in dev/test.
50. **FR-3H3.5** — Node-executor authoring pattern doc at `.claude/patterns/ai/node-executor-authoring.md`: documents the Singleton-executor-depends-on-Scoped-service pattern (`IServiceProvider.CreateScope()` per execution). `AiAnalysisNodeExecutor` cited as worked example.

**Doc-only**

51. **FR-3G4** — `docs/architecture/playbook-architecture.md` Known Pitfalls section refreshed: G4 (CreateNotification idempotency-on-unread) added explicitly; G1-G11 status updated (Fixed / Documented / Deferred-to-R4 per R3 outcomes).

### Non-Functional Requirements

- **NFR-01** — **BFF publish-size**: ≤+1 MB delta per BFF-touching task; cumulative ≤60 MB compressed (binding ceiling per `.claude/constraints/azure-deployment.md`). Current baseline ~45.65 MB (Phase 5 Outcome A, 2026-05-26). Measure on EVERY BFF-touching task via `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`. Cronos NuGet (~50KB) + new code estimated +1-2 MB total.
- **NFR-02** — **CVE hygiene**: No new HIGH-severity CVE from `dotnet list package --vulnerable --include-transitive`.
- **NFR-03** — **Test coverage**: Every new BFF service has unit tests. Integration tests exist for: membership endpoint, `LookupUserMembership` node executor, each migrated playbook, scheduled-job framework, `MembershipReconciliationJob`.
- **NFR-04** — **Membership endpoint perf**: p95 ≤ 300ms for a user with ≤500 memberships on a 50K-row entity. **Measured via Application Insights server-side request telemetry** (decision 2026-06-20) — query AI for the endpoint route's p95 over a 24h window. No new instrumentation code required.
- **NFR-05** — **Membership cache hit ratio**: ≥90% under steady-state load (Phase 1A: 5-min TTL; Phase 2: longer TTL + pub/sub invalidation).
- **NFR-06** — **Data-model docs**: `docs/data-model/sprk_matter-related-tables.md` updated to reflect actual `sprk_matter` columns (`sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedlawfirm1/2`, `sprk_assignedtointernal`, `sprk_assignedtoexternal`). Doc-drift finding from this design surfaced and resolved (AC-X.4).
- **NFR-07** — **Async safety**: All new `IScheduledJob` implementations honor `CancellationToken`. `ScheduledJobHost` cancellation propagates to running jobs within 30s.
- **NFR-08** — **Distributed tracing**: Every `MembershipChangedEvent` carries `correlationId`. Every `sprk_backgroundjobrun` records `correlationId`. Trace via App Insights.

---

## Technical Constraints

### Applicable ADRs (existing)

| ADR | Relevance |
|---|---|
| **ADR-001** — Minimal API + BackgroundService | `Spaarke.Scheduling` stays in-process; no Azure Functions, no external scheduler |
| **ADR-007** — `SpeFileStore` facade | N/A for this project (no SPE file ops) |
| **ADR-008** — Endpoint-filter authorization | Membership + admin endpoints follow filter convention (NOT global middleware) |
| **ADR-009** — Redis caching | Identity normalization cache, membership cache, metadata cache, pub/sub invalidation all use Redis |
| **ADR-010** — DI minimalism | New services injected as concretes; `IScheduledJob` interface allowed as testing seam; `IMembershipResolverService` allowed (consumed by `LookupUserMembershipNodeExecutor`) |
| **ADR-012** — Shared component library | `Spaarke.Scheduling` is a new shared .NET library under `src/server/shared/` |
| **ADR-013** — AI architecture | `LookupUserMembership` node executor extends existing framework (per `INodeExecutor.cs`) |
| **ADR-016** — Rate-limit handling | Membership endpoint cache + retry pattern |
| **ADR-024** — Polymorphic resolver pattern | Informs identity normalization patterns |
| **ADR-028** — Spaarke Auth v2 | Endpoint uses OBO; identity resolution uses standard `@spaarke/auth` contract |
| **ADR-029** — BFF publish hygiene | NFR-01 enforcement |
| **ADR-030** — PaneEventBus | N/A (no widget event coordination in R3) |
| **ADR-032** — Null-Object Kill-Switch | Apply if any new service is feature-gated (per § F.1 anti-pattern check) |

### New ADRs (R3 deliverable)

- **ADR-034** — "User-record membership resolution pattern" (Part 1 — contract, identity normalization, discovery model, Phase 2 junction architecture)
- **ADR-036** — "Background-job infrastructure (Spaarke.Scheduling)" (Part 2)
- *(ADR-035 reserved for R4 playbook rollout-mode — out of scope for R3)*

### MUST Rules

- ✅ **MUST** use `RequireAuthorization("SystemAdmin")` on all `/api/admin/*` endpoints — policy already defined at [`AuthorizationModule.cs:241`](src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L241) (checks `Admin`/`SystemAdmin` role/claim or `scope` claim containing "admin"). Existing precedent: [`RagEndpoints.cs:157`](src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs#L157). Do NOT create a new "PlatformAdmin" policy — design.md's reference to that name is superseded by this spec
- ✅ **MUST** use endpoint filters for resource-level authorization (per ADR-008) — global middleware forbidden
- ✅ **MUST** use `SpeFileStore` facade if any SPE op is added (none currently planned)
- ✅ **MUST** inject services as concretes; interface only if there's an actual testing/swap need (per ADR-010)
- ✅ **MUST** use `DefaultAzureCredential` (managed identity) for Graph + Dataverse outbound when `Graph__ManagedIdentity__Enabled=true` (per ADR-028)
- ✅ **MUST** publish to topic `sprk-membership-changes` with subscription-per-consumer (NOT queue, NOT reuse `ServiceBusJobProcessor` queue) — D3 resolved
- ✅ **MUST** measure publish-size delta on EVERY BFF-touching task (per `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule (NFR-01)")
- ✅ **MUST** follow `.claude/constraints/bff-extensions.md` § A (pre-merge checklist) + § F (asymmetric-registration anti-pattern) + § F.2 (fixture-config-FIRST inspection) + § F.3 (empirical-reproduction-FIRST protocol)
- ✅ **MUST** state Placement Justification in PR description for each new BFF service/endpoint (per CLAUDE.md §10 imperative)
- ❌ **MUST NOT** inject `IOpenAiClient`, `IPlaybookService`, or other AI-internal types directly into CRUD code — use `Services/Ai/PublicContracts/` facade
- ❌ **MUST NOT** skip `--no-verify` or bypass pre-commit hooks
- ❌ **MUST NOT** support identity matching for free-text display-name fields (explicitly out of scope)

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/QueryDataverseNodeExecutor.cs` for the FetchXML+node-executor pattern (closest analog to `LookupUserMembershipNodeExecutor`)
- See `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AgentServiceNodeExecutor.cs` for Singleton-executor-depends-on-Scoped pattern (NodeExecutor authoring doc cites this)
- See `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerService.cs` (current state) for the `PeriodicTimer + IOptions<XOptions>` pattern being migrated
- See `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` for existing Service Bus consumer pattern (note: Phase 2 uses a topic, NOT this queue)
- See `.claude/patterns/api/endpoint-definition.md` for endpoint definition convention
- See `.claude/patterns/dataverse/web-api-client.md` for metadata query patterns (FR-1A.1)
- See `docs/architecture/playbook-architecture.md` for orchestration architecture

---

## Success Criteria

### Part 1 — User-Record Membership

- [ ] **AC-1A.1** — Discovery finds all expected lookup fields on `sprk_matter` (owner, owningteam, owningbusinessunit, `sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedlawfirm1/2`, `sprk_assignedtointernal`, `sprk_assignedtoexternal`). System fields (`createdby` etc.) excluded. Custom-entity lookups (`sprk_chartdefinition`) excluded. *Verify*: integration test against `spaarkedev1` seeded fixture.
- [ ] **AC-1A.2** — `GET /api/admin/membership/discovered/sprk_matter` returns expected descriptor list with `source: "auto"` or `"override"`. *Verify*: integration test asserts shape.
- [ ] **AC-1A.3** — `GET /api/users/me/memberships/sprk_matter` returns expected IDs for a seeded test user. *Verify*: integration test with fixture asserting `count > 0` and presence of expected matter GUIDs.
- [ ] **AC-1A.4** — Identity normalization resolves a test user whose `systemuserid` + `contactid` are separate records linked by `azureactivedirectoryobjectid`. Both Lookup-field types produce a match. *Verify*: integration test with seeded contact + systemuser pair.
- [ ] **AC-1A.5** — Endpoint p95 ≤300ms for user with ≤500 memberships on 50K-row entity. *Verify*: App Insights query for endpoint route's p95 over 24h soak.
- [ ] **AC-1A.6** — Membership cache hit ratio ≥90% steady-state. *Verify*: App Insights custom metric or Redis stats.
- [ ] **AC-1A.7** — Metadata cache invalidates correctly on `POST /api/admin/membership/refresh-metadata`. *Verify*: integration test asserts pre/post-refresh descriptor freshness.
- [ ] **AC-1B.1** — `LookupUserMembership` playbook node (ActionType 52) executes end-to-end and writes resolved IDs to `OutputVariable`. *Verify*: playbook integration test.
- [ ] **AC-1B.2** — `joinIds` Handlebars helper produces correct comma-separated lists for FetchXML `IN` clauses. *Verify*: unit test.
- [ ] **AC-1C.1** — `notification-new-documents.json` migrated; produces non-zero notifications for seeded test fixture. *Verify*: playbook integration test.
- [ ] **AC-1C.2** — Any other playbook using broken `sprk_matterteammember` join similarly migrated. *Verify*: grep `sprk_matterteammember` in playbook JSON returns zero matches post-R3.
- [ ] **AC-1D.1** — `includeRelated=documents,events` returns transitive memberships per design contract. *Verify*: integration test with fixture asserting transitive matches.
- [ ] **AC-1D.2** — Transitive query maintains p95 ≤300ms or documents the limit explicitly. *Verify*: App Insights perf measurement.
- [ ] **AC-1.ADR** — ADR-034 merged into `.claude/adr/` and `docs/adr/`. *Verify*: file exists + INDEX.md updated.
- [ ] **AC-1.Docs** — `docs/architecture/` has new page describing the pattern with code-entry pointers + naming-collision disambiguation from existing `AssociationResolver` PCF.

### Part 1 Phase 2 (junction table + event-driven sync)

- [ ] **AC-1P2.1** — `sprk_userentityassociation` entity created with 7 columns + 2 indexes. *Verify*: schema deploy in dev.
- [ ] **AC-1P2.2** — Service Bus topic `sprk-membership-changes` + subscription `recon-junction-updater` provisioned. *Verify*: Azure CLI / Bicep diff.
- [ ] **AC-1P2.3** — Event-source inventory (P-event-1) produces complete checklist of mutation endpoints. *Verify*: checklist exists in `projects/spaarke-platform-foundations-r3/notes/` or task notes.
- [ ] **AC-1P2.4** — Each inventoried mutation endpoint publishes `MembershipChangedEvent`. *Verify*: integration test for each endpoint asserts message published.
- [ ] **AC-1P2.5** — `MembershipJunctionUpdater` handler upserts/deletes junction rows correctly + idempotently. *Verify*: integration test with duplicate-event-delivery scenario.
- [ ] **AC-1P2.6** — `MembershipReconciliationJob` real logic reconciles source vs junction; reports `ProcessedItems`. *Verify*: integration test with intentional drift fixture.
- [ ] **AC-1P2.7** — Redis pub/sub invalidates membership cache on junction write. *Verify*: integration test asserts cache miss after junction-row mutation.
- [ ] **AC-1P2.8** — Endpoint contract unchanged from Phase 1A. *Verify*: existing Phase 1A integration tests still pass after Phase 2 swap-in.

### Part 2 — Background-Job Infrastructure

- [ ] **AC-2.1** — `Spaarke.Scheduling` library compiles + has unit tests for `ScheduledJobHost`, cron schedule parsing (via Cronos), run-history recording.
- [ ] **AC-2.2** — `sprk_backgroundjob` + `sprk_backgroundjobrun` entities deployed; schema migrations work in dev/test.
- [ ] **AC-2.3** — `MembershipReconciliationJob` registered + visible in `GET /api/admin/jobs` + triggerable via `POST /api/admin/jobs/membership-reconciliation/trigger`. *Verify*: integration test.
- [ ] **AC-2.4** — `PlaybookSchedulerService` migrated to `Spaarke.Scheduling`. All 7 notification playbooks fan out from the single `notification-playbook-scheduler` row. Existing scheduled behavior preserved (operators see same notification cadence). *Verify*: regression test against current playbook output cadence.
- [ ] **AC-2.5** — Admin endpoints behind `RequireAuthorization("SystemAdmin")` — non-admin users receive 403. *Verify*: integration test with non-admin token.
- [ ] **AC-2.6** — Job runs recorded in `sprk_backgroundjobrun` with `correlationId`, trigger source, status, duration. *Verify*: query after triggered run.
- [ ] **AC-2.7** — Failed jobs surface in `GET /api/admin/jobs/{jobId}/status` with last error. *Verify*: integration test with intentionally-failing handler.
- [ ] **AC-2.ADR** — ADR-036 merged.

### Part 3 — Playbook Engine Hardening

- [ ] **AC-H1.1** — `{{default X 'Y'}}` + `{{joinIds arr}}` helpers registered + 2 broken playbooks migrated + unit tests. *Verify*: unit tests pass; grep `\?\?` in playbook JSON returns zero.
- [ ] **AC-H1.2** — Unrendered `{{` runtime warning logs structured event + appears in playbook stream telemetry. *Verify*: integration test with intentionally-broken playbook asserts warning emitted.
- [ ] **AC-H2.1** — Builder UI prevents silent `OutputVariable` rename breakage. *Verify*: Playwright/component test.
- [ ] **AC-H2.2** — Builder UI guides authors to specify branch on Condition→downstream edges.
- [ ] **AC-H2.3** — Builder UI flags non-referencing edges as perf-impacting (advisory only).
- [ ] **AC-H3.1** — Canvas-server mapping drift test passes in CI; fails build on intentional drift. *Verify*: insert temporary mismatch + assert CI fails.
- [ ] **AC-H3.2** — `sprk_searchindexqueuedon` + `sprk_searchindexcompletedon` schema migration deployed; consumer migration complete (per P7.0 inventory).
- [ ] **AC-H3.3** — `.claude/patterns/ai/node-executor-authoring.md` published.
- [ ] **AC-Docs** — `docs/architecture/playbook-architecture.md` Known Pitfalls section refreshed.

### Cross-cutting

- [ ] **AC-X.1** — No new HIGH-severity CVE from `dotnet list package --vulnerable --include-transitive`.
- [ ] **AC-X.2** — BFF publish-size delta ≤+1 MB per BFF-touching task; cumulative ≤60 MB compressed.
- [ ] **AC-X.3** — Test coverage: every new BFF service has unit tests; integration tests exist for endpoint + node + reference scheduled job + recon job + event-driven sync.
- [ ] **AC-X.4** — Data-model docs (`docs/data-model/sprk_matter-related-tables.md`) updated to reflect actual `sprk_matter` columns.

---

## Dependencies

### Prerequisites

- `Spaarke.Core` library exists and is consumable (used as `Spaarke.Scheduling`'s sole upstream dep)
- Redis available and configured in BFF (per ADR-009)
- Service Bus namespace provisioned (used by existing `ServiceBusJobProcessor`; topic + subscription added in R3)
- `RequireAuthorization("SystemAdmin")` policy already exists at [`AuthorizationModule.cs:241`](src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L241) — no work required
- `spaarkedev1` environment has at least one seeded user with `sprk_matter` memberships across all field types (Owner, Team, Attorney, Paralegal, LawFirm) for integration tests

### External Dependencies

- **Cronos NuGet package** — `Cronos v0.7.x` (mature, ~50KB, MIT). Adds to BFF publish-size; verify NFR-01.
- **Microsoft Dataverse Web API** — metadata endpoint `/api/data/v9.2/EntityDefinitions(...)`. Standard auth.
- **Azure AI Search** — for `sprk_searchindexed` migration completion confirmation (Workstream H3 #2). Existing integration.

---

## Owner Clarifications

*Captured during design-to-spec interview, 2026-06-20 (initial round + follow-up round):*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| **Phase 1D scope** | "Design-only, conditional, or always-build in R3?" | **In-scope: always build in R3** | Lifts Phase 1D into firm scope; added FR-1D.1, FR-1D.2, FR-1D.3, AC-1D.1, AC-1D.2 |
| **AC-1A.5 perf measurement** | "Where measured?" | **Application Insights server-side request telemetry** | No new instrumentation code; NFR-04 specifies AI query method |
| **H3 `sprk_searchindexed` consumers** | "Inventory exists, or discovery needed?" | **Start with discovery — spec adds P7.0 inventory task** | Added FR-3H3.3 (discovery task) before FR-3H3.4 (consumer migration) |
| **Phase 2 scope** | "Defer per design, or build in R3?" | **Build Phase 2 in R3 with real recon logic** | Lifts Phase 2 from "design-only" to firm scope; added FR-2P2.1 through FR-2P2.9 + AC-1P2.1 through AC-1P2.8; task count est. 35-51 → 50-70 |
| **D3 transport shape** | "Single queue, topic+subscriptions, or reuse existing `ServiceBusJobProcessor` queue?" | **Topic `sprk-membership-changes` with subscription-per-consumer** | FR-2P2.3 locked. Allows future consumers (cache warmers, downstream indexers, Teams notify, VIP cache invalidator) without infra migration. ~5-10% Service Bus cost premium per message; pennies/month at expected volume |
| **Event-source inventory** | "Inventory now, or discovery task?" | **Discovery task P-event-1** | Added FR-2P2.5 (discovery) before FR-2P2.6 (event-publishing hookup); preserves completeness without locking endpoint list prematurely |
| **Q1: Playbook scheduler correlationId** | "Preserve parent correlationId for child playbooks, or fresh per playbook?" | **Fresh `correlationId` per playbook** | FR-2.8 updated: parent run records child correlationIds in `sprk_resultjson` so operators can join parent ↔ children |
| **Q2: Phase 2 publish semantics** | "Fire-and-forget or transactional outbox?" | **Fire-and-forget** | FR-2P2.6 updated: publish best-effort; mutation succeeds even on publish failure; nightly recon (FR-2P2.7) is the backstop |
| **Q3: Phase 1D max chain depth** | "1 hop only or arbitrary depth?" | **1 hop max** | FR-1D.2 updated: requests like `matter → document → comment` rejected with 400 |
| **Q4: `sprk_assignedlawfirm*` Lookup target** | "Contact or account?" | **`sprk_organization` (neither Contact nor Account)** | FR-1A.6 corrected: design.md's Discovery Report example showed `Contact` for these fields — this is wrong; spec.md identity-type for `sprk_assignedlawfirm*` is `Organization`. Role-name override (`"assignedLawFirm"`) unchanged |
| **Q5: Builder UI componentry** | "Existing component library or new UX work?" | **Align with existing PlaybookBuilder code page** | FR-3H2.1-FR-3H2.3 updated to reference existing `properties/` per-ActionType form pattern, `VariableReferencePanel.tsx`, `canvasValidation.ts`, `NodePropertiesDialog.tsx`, `NodeValidationBadge.tsx`. Added FR-1B.4 for new `LookupUserMembershipForm.tsx`. **Research existing patterns first — do not invent new patterns.** |
| **Q6: Admin policy name** | "PlatformAdmin uses AAD groups, roles, or Dataverse check?" | **Use existing `SystemAdmin` policy** (NOT new `PlatformAdmin`) | spec.md globally replaced `PlatformAdmin` → `SystemAdmin`. Policy defined at [`AuthorizationModule.cs:241`](src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L241) — checks `Admin`/`SystemAdmin` role/claim OR `scope` claim containing "admin". Existing precedent: [`RagEndpoints.cs:157`](src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs#L157) |

---

## Assumptions

*Proceeding with the following assumptions (owner did not specify; spec.md surfaces these for confirmation during implementation):*

- **MembershipReconciliationJob nightly cadence** — Assuming `0 2 * * *` (2 AM UTC daily). Adjustable via `sprk_cronschedule` post-deploy.
- **`MembershipChangedEvent` payload shape** — Assuming `{personId, personIdType, entityLogicalName, entityRecordId, sourceField, role, mutationType, correlationId}`. Final shape settled during FR-2P2.2 implementation.
- **Junction table indexes** — Assuming composite indexes on `{sprk_personid, sprk_entitylogicalname}` and `{sprk_entitylogicalname, sprk_entityrecordid}`. Adjust based on query patterns observed.
- **Phase 1D response shape** — Assuming `byRole.documents.{role}` nested structure for transitive memberships. Settled during FR-1D.3.
- **`notification-tasks-due-soon.json` is the only known second broken playbook** — FR-3H1.3 will grep all playbook JSON for `??` and migrate any additional hits found.
- **Per-user membership cache TTL in Phase 2** — Assuming 30 min (longer than Phase 1A's 5 min, since pub/sub invalidation handles correctness). Tune based on hit-ratio observation.
- **VIP-cache-invalidator subscription** mentioned in design-discussion is **NOT** shipped in R3 — it was an illustrative future consumer to justify topic transport. Phase 2 ships only the `recon-junction-updater` subscription.
- **`sprk_searchindexed` dual-write transition window** — Assuming dual-write for the duration of R3 + one sprint after; remove old field after consumer migration confirmed in prod.
- **Cronos NuGet version** — Assuming `0.7.x` (latest stable at time of implementation). Verify no HIGH-severity CVE.
- **`sprk_organization` identity mapping** — Assuming the configured user-organization mapping (per design's identity normalization contract row 6) is via a Dataverse N:N relationship between `systemuser` and `sprk_organization`, OR via a configurable lookup field. Final mechanism settled during FR-1A.6 implementation; if no mapping exists yet, this is a configuration prerequisite that needs operator setup.

---

## Unresolved Questions

*All 6 questions from the first draft were resolved 2026-06-20 (see Owner Clarifications above). No blocking questions remain.*

*New items may surface during task execution; capture them here when they do:*

- *(none currently)*

---

## R4 Backlog Seeds

*Items explicitly deferred from R3 to R4 (or later) — captured here so they're not lost:*

- **G11 / H4 rollout-mode** for notification playbooks (Disabled / PilotUsers / AllUsers) — new R4 project `spaarke-playbook-rollout-mode-r4` + ADR-035
- **Migration of remaining 26 `BackgroundService` implementations** to `Spaarke.Scheduling` — opportunistic per touch
- **Phase 3 — AI Search integration** (`associatedPersons[]` field in indexed documents) — when first consumer entity gets a search index
- **Dataverse-stored membership config** (`sprk_membershipconfig` entity) — if makers ask for runtime control
- **Per-playbook "Run Now"** for the migrated `PlaybookSchedulerService` (today's R3 is whole-scheduler granularity)
- **VIP-cache-invalidator subscription** on the `sprk_membership-changes` topic — when VIP-list requirement emerges
- **Migration of queue-consumer services** (`ServiceBusJobProcessor` family) to `Spaarke.Scheduling` — if/when there's a fit

---

## Implementation Phases

*Suggested ordering for `task-create`. Updated to reflect Phase 1D + Phase 2 promotion to firm scope.*

| Phase | Workstream | Sequencing | Est. tasks |
|---|---|---|---|
| **P1** | Part 3 H1 — Template engine + runtime detection (G1 + G10) | Earliest; small; unblocks playbook migration | 3–5 |
| **P2** | Part 2 — `Spaarke.Scheduling` library + entities + ADR-036 | Early; needed for recon job + scheduler migration | 6–8 |
| **P3** | Part 2 — Admin endpoints + `PlaybookSchedulerService` migration | After P2 | 4–6 |
| **P4** | Part 1A — `MembershipResolverService` discovery + identity normalization + endpoint + ADR-034 | After P1; independent of P2/P3 | 6–8 |
| **P5** | Part 1B — `LookupUserMembership` node executor + canvas/server mapping update | After P4 | 2–3 |
| **P6** | Part 1C — Playbook migration + integration tests | After P5 | 2–3 |
| **P6.5** | Part 1D — Transitive memberships (`includeRelated`) | After P5; parallel to P6 | 2–3 |
| **P7.0** | H3 discovery — `sprk_searchindexed` consumer inventory | Before P7.1 | 1 |
| **P7.1** | Part 3 H3 — Schema migration + DI hardening (G6 + G7 + G8) + consumer migration | After P7.0; parallel to P4-P6 | 5–7 |
| **P7.5** | Part 1 Phase 2 — Junction table entity + Service Bus topic + subscription | After P2; before P-event-1 | 3–5 |
| **P-event-1** | Phase 2 discovery — BFF mutation-endpoint inventory | After P7.5; before P8 | 1 |
| **P8** | Phase 2 — Event-publishing hookup + handler + recon-job real logic + Redis invalidation | After P-event-1 | 6–9 |
| **P9** | Part 3 H2 — Builder validation + UI affordances (G2 + G3 + G9) | After P7.1 (uses mapping-drift test infra) | 4–6 |
| **P10** | Docs sweep + data-model doc refresh + Phase 3 design write-up + G4 doc note | Last; depends on all above | 3–5 |
| **P11** | Wrap-up: lessons-learned, code-review, adr-check | Last | 1 |

**Estimated total: ~50–70 tasks** *(was 35-51 before Phase 1D + Phase 2 promotion)*. Larger R3 — verify capacity before kickoff.

---

## Naming Collision Register

*Reproduced from design.md for spec-time reference:*

| Existing concept | New R3 concept |
|---|---|
| `src/client/pcf/AssociationResolver/` — PCF for record-to-record FieldMapping | `Services/Ai/Membership/MembershipResolverService.cs` — BFF service for user-record membership |
| `sprk_fieldmappingprofile` / `sprk_fieldmappingrule` — field-copy rule entities | `Membership` appsettings section (+ potentially R4 `sprk_membershipconfig`) |
| `sprk_processingjob` — Office-scoped job-instance tracker | `sprk_backgroundjob` + `sprk_backgroundjobrun` — scheduled-job framework, distinct from Office |
| `PlaybookSchedulerService` — AI playbook scheduler | `ScheduledJobHost` (Spaarke.Scheduling) — generic; `PlaybookSchedulerService` becomes a consumer |
| `POST /api/ai/rag/index*` — per-document manual indexing | `POST /api/admin/jobs/{jobId}/trigger` — bulk job manual triggers (additive) |
| Existing `ServiceBusJobProcessor` queue | NEW topic `sprk-membership-changes` (D3 — separate transport, do not reuse) |

---

*AI-optimized specification. Original design: `design.md` (v2). After review, run `/project-pipeline projects/spaarke-platform-foundations-r3` to scaffold README + plan + CLAUDE.md + tasks.*
