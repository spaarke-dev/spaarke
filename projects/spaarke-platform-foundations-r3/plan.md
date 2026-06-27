# Project Plan: Spaarke Platform Foundations (R3)

> **Last Updated**: 2026-06-20
> **Status**: Ready for Implementation
> **Spec**: [spec.md](spec.md)
> **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Ship three cross-cutting platform foundations together — user-record membership resolution (Part 1 with Phase 2 firm in-scope), background-job framework (Part 2 / `Spaarke.Scheduling`), and playbook engine hardening (Part 3). All three surfaced as platform gaps during R2 UAT.

**Scope**:
- Part 1: `MembershipResolverService` (Phases 1A+1B+1C+1D) + Phase 2 junction-table + event-driven sync via Service Bus topic
- Part 2: `Spaarke.Scheduling` library + `sprk_backgroundjob*` entities + admin endpoints + 2 reference consumers
- Part 3: Workstreams H1 (template engine) + H2 (builder UI) + H3 (schema/DI hardening) + G4 doc-only
- 2 new ADRs (ADR-034, ADR-036) + ADR-035 reserved for R4 rollout-mode
- 1 new pattern doc + docs sweep

**Timeline**: ~50–70 tasks across 11 phases. Parallel execution where dependencies allow.

**Estimated Effort**: 8–12 implementation days (with parallel execution); 14–20 days if fully serialized.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: BFF stays Minimal API + in-process BackgroundService. No Azure Functions, no external schedulers. `Spaarke.Scheduling` runs in-process.
- **ADR-007**: `SpeFileStore` facade pattern (N/A here — no SPE file ops in R3)
- **ADR-008**: Use endpoint filters for resource-level authorization. Membership + admin endpoints follow filter convention; global middleware forbidden.
- **ADR-009**: Redis caching for identity normalization, membership cache, metadata cache, pub/sub invalidation
- **ADR-010**: DI minimalism — inject concretes; `IScheduledJob` allowed as testing seam; `IMembershipResolverService` allowed (consumed by node executor)
- **ADR-012**: `Spaarke.Scheduling` is a new shared .NET library under `src/server/shared/`
- **ADR-013**: `LookupUserMembership` node executor extends existing AI framework per [`INodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs)
- **ADR-016**: Membership endpoint cache + retry pattern
- **ADR-024**: Polymorphic resolver pattern informs identity normalization
- **ADR-028**: Endpoint uses OBO; identity resolution uses standard `@spaarke/auth` contract
- **ADR-029**: BFF publish hygiene (NFR-01 enforcement — ≤+1 MB delta, ≤60 MB cumulative)
- **ADR-032**: Null-Object Kill-Switch Pattern applied if any new service is feature-gated

**From Spec**:
- Single Service Bus **topic** `sprk-membership-changes` with subscription-per-consumer (NOT queue, NOT reuse `ServiceBusJobProcessor` queue) — D3 resolved
- Event-publishing semantics: **fire-and-forget** (nightly recon backstop) — Q2 resolved
- `includeRelated` max **1 hop** — Q3 resolved
- `sprk_assignedlawfirm1/2` targets `sprk_organization` → `identityType: "Organization"` — Q4 resolved
- Use existing **`SystemAdmin`** policy at [`AuthorizationModule.cs:241`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L241) — Q6 resolved
- **Per child playbook fresh `correlationId`** in scheduler fan-out — Q1 resolved
- All H2 builder UI work **extends existing PlaybookBuilder componentry** — Q5 resolved

### Key Technical Decisions

| Decision | Rationale | Impact |
|---|---|---|
| Phase 2 in-scope | Owner promoted from "design-only" to firm in-scope | +8-12 tasks; ships `sprk_userentityassociation` + topic + handlers + real recon |
| Service Bus topic over queue | Defense-in-depth + future consumers (cache warmers, VIP invalidators) | New infra; ~5-10% per-message cost premium (pennies/month) |
| Discovery-based metadata resolution over explicit enumeration | Drift-prone explicit config was the D5/A1 root cause | Auto-discovery + per-entity overrides; metadata cache (1h TTL) |
| Cronos NuGet for cron parsing | Mature, ~50KB, MIT, avoids reinventing | +~50KB to BFF publish; well within NFR-01 budget |
| Single-row migrated `PlaybookSchedulerService` | Preserves 1:1 cadence behavior; per-playbook "Run Now" deferred | Operators get whole-scheduler trigger now; finer granularity later |
| Existing `SystemAdmin` policy (NOT new `PlatformAdmin`) | Already exists + used by `RagEndpoints.cs:157`; design.md was wrong | spec.md globally corrected |

### Discovered Resources

**Applicable ADRs** (12 existing + 2 new):
- Existing: ADR-001, ADR-007, ADR-008, ADR-009, ADR-010, ADR-012, ADR-013, ADR-016, ADR-024, ADR-028, ADR-029, ADR-032
- New (R3 deliverable): **ADR-034** (Membership), **ADR-036** (Spaarke.Scheduling)
- Reserved for R4: ADR-035 (Playbook rollout-mode)

**Applicable Skills**:
- `task-execute` — every task (MANDATORY per CLAUDE.md §4)
- `adr-aware` — auto-invoked
- `code-review` + `adr-check` — Step 9.5 quality gates
- `bff-deploy` — BFF deployment tasks
- `dataverse-create-schema` — `sprk_backgroundjob*`, `sprk_userentityassociation` entity creation
- `dataverse-deploy` — solution deployment
- `code-page-deploy` — PlaybookBuilder code-page redeploy (if H2 ships)
- `ui-test` — H2 builder UI tests + playbook integration tests
- `worktree-sync` — periodic sync

**Knowledge Articles** (`.claude/patterns/`):
- **AI patterns**: `analysis-scopes.md`, `endpoint-di-symmetry.md`, `streaming-endpoints.md`, `text-extraction.md`, `public-contracts-facade.md`, `indexing-pipeline.md`
- **API patterns**: `background-workers.md`, `endpoint-definition.md`, `endpoint-filters.md`, `error-handling.md`, `provisioning-pipeline.md`, `resilience.md`, `service-registration.md`
- **Dataverse patterns**: `entity-operations.md`, `web-api-client.md`, `relationship-navigation.md`, `polymorphic-resolver.md`

**Binding Constraints**:
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) §§A, F, F.1, F.2, F.3 — binding for every BFF-touching task
- [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) — BFF publish-size verification per task

**Reusable Code**:
- [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/QueryDataverseNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/QueryDataverseNodeExecutor.cs) — closest analog for `LookupUserMembershipNodeExecutor`
- [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AgentServiceNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AgentServiceNodeExecutor.cs) — Singleton-executor-depends-on-Scoped pattern (cited by `node-executor-authoring.md` doc)
- [`src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerService.cs) — current state being migrated
- [`src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs) — existing Service Bus consumer pattern (Phase 2 uses NEW topic, not this queue)
- [`src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs) — register `default` + `joinIds` helpers
- [`src/client/code-pages/PlaybookBuilder/src/components/properties/CreateNotificationForm.tsx`](../../src/client/code-pages/PlaybookBuilder/src/components/properties/CreateNotificationForm.tsx) — per-ActionType form pattern (new `LookupUserMembershipForm.tsx` follows this)
- [`src/client/code-pages/PlaybookBuilder/src/services/canvasValidation.ts`](../../src/client/code-pages/PlaybookBuilder/src/services/canvasValidation.ts) — H2 rename guard + edge perf hint validations extend this

**Reference Scripts**:
- `Capture-BffBaseline.ps1` — capture baseline publish-size at project init
- Various entity-creation scripts in `scripts/` — pattern for `Create-BackgroundJobEntities.ps1` + `Create-UserEntityAssociation.ps1`

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1 (P1):  Template engine helpers + 2 broken playbook migration (H1 — unblocks 1C)
                  Tasks: 001–005

Phase 2 (P2):  Spaarke.Scheduling library + ADR-036 + entities (foundational for P3, P7.5, P8)
                  Tasks: 010–017

Phase 3 (P3):  Admin endpoints + PlaybookSchedulerService migration
                  Tasks: 020–025

Phase 4 (P4):  MembershipResolverService Phase 1A + ADR-034
                  Tasks: 030–037

Phase 5 (P5):  LookupUserMembership node executor + canvas mapping
                  Tasks: 040–043

Phase 6 (P6):  Playbook 1C migration + integration tests
                  Tasks: 050–053

Phase 6.5:     Phase 1D — Transitive memberships
                  Tasks: 054–056

Phase 7.0:     H3 discovery — sprk_searchindexed consumer inventory
                  Tasks: 060

Phase 7.1 (P7.1): H3 schema migration + DI hardening
                  Tasks: 061–066

Phase 7.5:     Phase 2 — Junction entity + Service Bus topic
                  Tasks: 070–073

Phase 8 (P8):  Phase 2 event-source inventory + publishing + recon job + Redis invalidation
                  Tasks: 080–087

Phase 9 (P9):  H2 builder UI affordances
                  Tasks: 090–095

Phase 10 (P10): Docs sweep + Phase 3 design + data-model refresh + G4 note
                  Tasks: 100–104

Phase 11 (P11): Project wrap-up (lessons-learned + code-review + adr-check + repo-cleanup)
                  Task: 110
```

### Critical Path

**Blocking Dependencies**:
- Phase 2 (Spaarke.Scheduling library) BLOCKS Phase 3, Phase 7.5 (recon job needs framework), Phase 8 (recon-job real logic)
- Phase 4 (MembershipResolverService) BLOCKS Phase 5 (node executor consumes it), Phase 6 (migration uses node), Phase 6.5 (transitive uses endpoint)
- Phase 5 BLOCKS Phase 6
- Phase 7.0 (discovery) BLOCKS Phase 7.1 (migration)
- Phase 7.5 BLOCKS Phase 8 (Phase 2 work depends on entity + topic existence)
- P-event-1 inside Phase 8 BLOCKS event-publishing hook tasks within Phase 8

**Critical Path Length** (longest dependency chain):
`P1 (001) → P2 (010) → P3 (020) → P4 (030) → P5 (040) → P6 (050) → P6.5 (054) → P7.5 (070) → P8 (080) → P9 (090) → P10 (100) → P11 (110)`

But many phases run in PARALLEL once dependencies are satisfied (see §4 Parallel Execution Groups below).

**High-Risk Items**:
- **Phase 2 scope** (junction + Service Bus topic + real recon) — substantial new infra; mitigation: phase-gate (Phase 1A ships first, Phase 2 is additive)
- **`sprk_organization` user mapping** — mechanism not yet defined; mitigation: Phase 4 first task defines it
- **PlaybookBuilder UI regression** — H2 changes risk breaking existing builder; mitigation: align with existing patterns, comprehensive snapshot tests

---

## 4. Parallel Execution Strategy

The user explicitly requested **parallel-optimized task structuring**. Tasks are grouped by:
1. **File-region disjointness** — tasks touching different files can run in parallel
2. **Dependency satisfaction** — all upstream tasks must be ✅
3. **Sub-agent boundary** — tasks touching `.claude/` paths MUST be sequential (main-session-only per CLAUDE.md §3)
4. **Max concurrency**: 6 agents per wave (hard limit per pipeline §5)

### Parallel Execution Groups

| Group | Tasks | Prerequisite | Files Touched | Parallel-Safe | Concurrency |
|---|---|---|---|---|---|
| **A** | 001, 002 | none | `TemplateEngine.cs` helpers (002 depends on 001 — serial) | ❌ Serial | 1 |
| **B** | 003, 004 | 002 ✅ | Two `notification-*.json` migrations (disjoint files) | ✅ Yes | 2 |
| **C** | 010, 011, 012 | none | `Spaarke.Scheduling/` new lib (010 scaffold first), then `IScheduledJob.cs` + `JobRunContext.cs` + `JobRunResult.cs` (parallel) | ⚠️ 010 first, then 011+012 parallel | 1 → 2 |
| **D** | 013, 014 | 010 ✅ | `ScheduledJobHost.cs` + `Cronos`-binding code (disjoint files) | ✅ Yes | 2 |
| **E** | 015, 016 | 010 ✅ | Two new entities `sprk_backgroundjob` + `sprk_backgroundjobrun` (disjoint Dataverse) | ✅ Yes | 2 |
| **F** | 020, 021, 022 | 014, 015, 016 ✅ | `JobsEndpoints.cs` GET handlers (020), POST trigger (021), enable/disable (022) — disjoint endpoint registrations | ✅ Yes | 3 |
| **G** | 030, 031, 032 | 005 ✅ (broken playbooks fixed) | `MembershipFieldDiscoveryService.cs`, `IdentityNormalizationService.cs`, `MembershipOptions.cs` — disjoint files | ✅ Yes | 3 |
| **H** | 033, 034 | 030, 031, 032 ✅ | `MembershipResolverService.cs` orchestration + `MembershipResponse.cs` DTO | ✅ Yes | 2 |
| **I** | 035, 036, 037 | 033 ✅ | Three endpoint handlers in `MembershipEndpoints.cs` + `MembershipAdminEndpoints.cs` (disjoint endpoint blocks) | ✅ Yes | 3 |
| **J** | 040, 041 | 035 ✅ | `LookupUserMembershipNodeExecutor.cs` + ActionType enum update (disjoint) | ✅ Yes | 2 |
| **K** | 042, 043 | 040, 041 ✅ | `playbookNodeSync.ts` (client) + `LookupUserMembershipForm.tsx` (new) | ✅ Yes | 2 |
| **L** | 050, 051, 052 | 042 ✅ | Three playbook JSON migrations (disjoint) | ✅ Yes | 3 |
| **M** | 060 | 053 ✅ | Discovery task — main-session sequential (produces inventory) | ❌ Sequential | 1 |
| **N** | 061, 062 | 060 ✅ | `sprk_searchindex*` schema (Dataverse) + `DeliverToIndexNodeExecutor.cs` update | ✅ Yes | 2 |
| **O** | 063, 064, 065 | 061, 062 ✅ | Consumer migrations per P7.0 inventory — parallelizable per consumer cluster | ✅ Yes | up to 3 |
| **P** | 070, 071 | 010 ✅ | `sprk_userentityassociation` entity (Dataverse) + Service Bus topic Bicep | ✅ Yes | 2 |
| **Q** | 080 | 070, 071 ✅ | P-event-1 discovery task — main-session sequential | ❌ Sequential | 1 |
| **R** | 081, 082, 083 | 080 ✅ | Event-publishing hookup per inventoried endpoint cluster (per matter/document/event) | ✅ Yes | up to 3 |
| **S** | 084, 085 | 081-083 ✅ | `MembershipJunctionUpdater.cs` handler + `MembershipReconciliationJob.cs` real logic | ✅ Yes | 2 |
| **T** | 090, 091, 092 | 042, 043 ✅ | H2 builder UI: `OutputVariable` rename guard + branch wiring + edge perf hint — disjoint component edits | ✅ Yes | 3 |
| **U** | 100, 101, 102 | All implementation phases ✅ | Three doc updates (ADR-034, ADR-036, playbook-architecture.md) — disjoint `.claude/` + `docs/` files | ⚠️ Main-session only (`.claude/` write boundary) | 1 |

### Sub-Agent Write Boundary

**Tasks touching `.claude/` paths MUST be sequential (main-session-only)** per CLAUDE.md §3. Affected tasks:
- 100 (ADR-034 in `.claude/adr/`)
- 101 (ADR-036 in `.claude/adr/`)
- 104 (pattern doc in `.claude/patterns/ai/`)

These are tagged `parallel-safe: false` in their `.poml` files. The main session picks them up sequentially after parallel docs work.

### Build Verification Between Waves

After each wave completes, main session runs:
- `dotnet build src/server/api/Sprk.Bff.Api/` (if any `.cs` file modified)
- `npm run build` in `src/client/code-pages/PlaybookBuilder/` (if any `.ts/.tsx` modified)
- If build fails → STOP, do not dispatch next wave, report breakage

### Failure Isolation

- One agent failing in a wave does NOT abort the wave
- All agent outcomes collected; failures marked `🔄 needs retry` in TASK-INDEX (not `❌ abandoned`)
- Main session decides retry-sequential vs report-and-stop

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|---|---|---|---|
| **Cronos NuGet** | Stable v0.7.x, MIT | Low | Verify no HIGH CVE; ~50KB well within publish-size budget |
| **Microsoft Dataverse Web API** | GA | Low | Standard metadata endpoint; auth via existing patterns |
| **Azure Service Bus namespace** | Already provisioned | Low | New topic + subscription added via Bicep |
| **Azure App Insights** | Already integrated | Low | NFR-04 perf measurement via existing AI |
| **Redis** | Already deployed | Low | Per ADR-009; new pub/sub channels added |
| **spaarkedev1 environment** | Available | Low | Need seeded user fixture with `sprk_matter` memberships |

### Internal Dependencies

| Dependency | Location | Status |
|---|---|---|
| `Spaarke.Core` library | `src/server/shared/Spaarke.Core/` | Production (sole dep of new `Spaarke.Scheduling`) |
| `SystemAdmin` policy | [`AuthorizationModule.cs:241`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L241) | Production (already exists; used by `RagEndpoints.cs:157`) |
| `ActionType` enum | [`INodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs) | Production (add value 52 for `LookupUserMembership`) |
| `TemplateEngine.cs` | [`Services/Ai/TemplateEngine.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs) | Production (extend with `default` + `joinIds` helpers) |
| `PlaybookOrchestrationService` | [`Services/Ai/PlaybookOrchestrationService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs) | Production (add unrendered-template runtime warning) |
| `DeliverToIndexNodeExecutor` | [`Services/Ai/Nodes/DeliverToIndexNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/DeliverToIndexNodeExecutor.cs) | Production (rewrite to use `sprk_searchindexqueuedon`/`completedon`) |
| `PlaybookBuilder` code page | [`src/client/code-pages/PlaybookBuilder/`](../../src/client/code-pages/PlaybookBuilder/) | Production (extend per-ActionType form pattern + validation) |
| `bff-extensions.md` | [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) | Binding constraint (read before every BFF-touching task) |

---

## 6. Testing Strategy

### Unit Tests (per NFR-03)

Every new BFF service has unit tests. Key targets:
- `MembershipFieldDiscoveryService` (metadata filter + role-name strategy)
- `IdentityNormalizationService` (each identity-type path)
- `MembershipResolverService` (orchestration + caching)
- `LookupUserMembershipNodeExecutor` (validation + execution + output binding)
- `ScheduledJobHost` (cron parsing via Cronos + job dispatch + run-record write)
- `MembershipJunctionUpdater` (idempotency + add/remove/update)
- `MembershipReconciliationJob` (drift detection + upsert/delete)
- Handlebars helpers (`default`, `joinIds`)

### Integration Tests

- Membership endpoint end-to-end against `spaarkedev1` seeded fixture
- Each migrated playbook (`notification-new-documents.json`, etc.) produces `count > 0`
- Scheduled-job framework dispatch via `/api/admin/jobs/{jobId}/trigger`
- Canvas-server mapping drift test (CI fails on intentional drift)
- Phase 2 event-publishing → handler → junction-row roundtrip
- Phase 2 cache invalidation via Redis pub/sub

### E2E / UAT

- Operator dispatches `MembershipReconciliationJob` via admin endpoint; observes successful run record
- Operator dispatches migrated `notification-playbook-scheduler`; all 7 playbooks fan out + complete
- Membership endpoint p95 ≤300ms (AC-1A.5) verified via App Insights over 24h soak
- Discovery report endpoint reviewed by operator for `sprk_matter` + `sprk_event` + `sprk_document`

### Test Coverage

Target: 80% line coverage on new BFF services; integration tests cover all happy paths + key failure modes.

---

## 7. Acceptance Criteria

See [`spec.md`](spec.md) for 40 detailed acceptance criteria with verification methods. Grouped by:

**Part 1** (12 ACs): AC-1A.1, AC-1A.2, AC-1A.3, AC-1A.4, AC-1A.5, AC-1A.6, AC-1A.7, AC-1B.1, AC-1B.2, AC-1C.1, AC-1C.2, AC-1D.1, AC-1D.2, AC-1.ADR, AC-1.Docs
**Part 1 Phase 2** (8 ACs): AC-1P2.1 through AC-1P2.8
**Part 2** (8 ACs): AC-2.1 through AC-2.ADR
**Part 3** (8 ACs): AC-H1.1, AC-H1.2, AC-H2.1, AC-H2.2, AC-H2.3, AC-H3.1, AC-H3.2, AC-H3.3, AC-Docs
**Cross-cutting** (4 ACs): AC-X.1, AC-X.2, AC-X.3, AC-X.4

### Phase Acceptance Gates

| Phase | Must satisfy |
|---|---|
| P1 | AC-H1.1 + AC-H1.2 (template engine helpers + runtime warning) |
| P2-P3 | AC-2.1 + AC-2.2 + AC-2.5 + AC-2.6 + AC-2.7 + AC-2.ADR (framework + entities + admin endpoints) |
| P4 | AC-1A.1 through AC-1A.7 + AC-1.ADR (membership resolver + endpoint + admin) |
| P5-P6 | AC-1B.1, AC-1B.2, AC-1C.1, AC-1C.2 (node executor + playbook migration) |
| P6.5 | AC-1D.1, AC-1D.2 (transitive memberships) |
| P7.0-P7.1 | AC-H3.1 + AC-H3.2 (canvas-server drift test + sprk_searchindex* migration) |
| P7.5-P8 | AC-1P2.1 through AC-1P2.8 (Phase 2 full delivery) + AC-2.3 + AC-2.4 (reference consumers) |
| P9 | AC-H2.1 + AC-H2.2 + AC-H2.3 (builder UI affordances) |
| P10 | AC-1.Docs + AC-H3.3 + AC-Docs + AC-X.4 (docs sweep) |
| P11 | All ACs satisfied; AC-X.1 + AC-X.2 + AC-X.3 final verification |

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Phase 2 scope creep — junction + topic + handlers + recon is meaty | High | High | Phase-gate (Phase 1A ships independently); strict timeboxing per task; failures isolated per wave |
| R2 | `sprk_organization` user-mapping mechanism not yet defined | Medium | Medium | First Phase 4 task scopes the mapping; spec.md Assumption flagged |
| R3 | PlaybookBuilder UI regression risk (H2) | Medium | Medium | Align with existing componentry (Q5 owner directive); snapshot tests per existing pattern |
| R4 | BFF publish-size near ceiling | Low | High | Per-task NFR-01 measurement; Cronos ~50KB is well within budget |
| R5 | `sprk_searchindexed` consumer migration blast radius unknown | Medium | Medium | P7.0 discovery task produces full inventory before migration (owner directive) |
| R6 | Service Bus topic provisioning slowdown | Low | Low | Standard Bicep pattern; existing SB namespace |
| R7 | Identity normalization edge cases (users w/o contact, etc.) | Medium | Low | Each identity type resolved independently per ADR-034 contract |
| R8 | Concurrent R2.2 hotfix work (PR #405) | Low | Low | No file overlap; R2.2 is daily-briefing UX only |
| R9 | Parallel-agent file conflicts | Medium | Medium | Per-task `relevant-files` declared in POML; `parallel-safe` flag; main-session orchestrates waves; build verify between waves |
| R10 | Sub-agent `.claude/` write boundary forces serialization of ADR/pattern writes | Low | Low | EXPECTED behavior; tasks pre-marked `parallel-safe: false`; main session picks them up |

---

## 9. Next Steps

1. **Review this plan.md** for any architecture concerns
2. **Run `/project-pipeline projects/spaarke-platform-foundations-r3`** (already in progress) to generate task POML files
3. **Begin Phase 1 (P1)** with task 001 once tasks land — first parallel wave is small (Group A is serial 001→002, then Group B has 2-task parallelism for playbook migrations)
4. **Watch for Phase 2 scope** at Phase 7.5/8 — this is the highest-risk part; consider checkpointing more aggressively here

---

**Status**: Plan ready for task decomposition
**Next Action**: Generate `tasks/*.poml` files + `TASK-INDEX.md` (in progress via `/project-pipeline`)

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks via `task-execute`.*
