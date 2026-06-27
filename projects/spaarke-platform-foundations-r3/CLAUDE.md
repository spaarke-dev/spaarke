# Spaarke Platform Foundations (R3) — AI Context

> **Purpose**: This file provides context for Claude Code when working on `spaarke-platform-foundations-r3`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning → Implementation
- **Last Updated**: 2026-06-20
- **Current Task**: Not started (project just initialized)
- **Next Action**: Run task-execute on task 001 (`001-register-default-handlebars-helper.poml`)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (51 FRs + 8 NFRs + 40 ACs; all owner clarifications resolved 2026-06-20)
- [`design.md`](design.md) — Human design doc (697 lines, v2 architectural sharpening)
- [`README.md`](README.md) — Project overview + graduation criteria
- [`plan.md`](plan.md) — Implementation plan with phase breakdown + parallel execution groups
- [`current-task.md`](current-task.md) — **Active task state** (for context recovery after compaction)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Task registry + dependencies + parallel groups

### Project Metadata
- **Project Name**: `spaarke-platform-foundations-r3`
- **Branch**: `work/spaarke-platform-foundations-r3` (already created)
- **Predecessor**: [`projects/spaarke-daily-update-service-r2/`](../spaarke-daily-update-service-r2/) (R2 UAT surfaced the gaps R3 resolves)
- **Type**: Multi-part platform infrastructure (BFF + Dataverse + Service Bus + PlaybookBuilder UI + AI playbook engine)
- **Complexity**: High — 3 cross-cutting workstreams, ~50–70 tasks, touches 9+ distinct file regions
- **Rigor Level**: FULL (all FR/AC tasks — mandatory `code-review` + `adr-check` at Step 9.5)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting any task
2. **Check current-task.md** for active work state (especially after compaction)
3. **Reference spec.md** for design decisions, requirements, and acceptance criteria
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** via `adr-aware` (automatic)
6. **Reference [bff-extensions.md](../../.claude/constraints/bff-extensions.md)** before ANY modification in `src/server/api/Sprk.Bff.Api/` (binding constraint per CLAUDE.md §10)

**Context Recovery**: If resuming work, see [`docs/procedures/context-recovery.md`](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

| User Says | Required Action |
|---|---|
| "work on task X" | Invoke `task-execute` with task X POML |
| "continue" / "keep going" / "next task" | Read `TASK-INDEX.md`, find first 🔲, invoke `task-execute` |
| "continue with task X" / "resume task X" | Invoke `task-execute` with task X POML |
| "pick up where we left off" | Load `current-task.md`, invoke `task-execute` |

### Why This Matters

The `task-execute` skill ensures:
- ✅ Knowledge files loaded (ADRs, constraints, patterns)
- ✅ Context tracked in `current-task.md`
- ✅ Proactive checkpointing every 3 steps
- ✅ Quality gates run (`code-review` + `adr-check` at Step 9.5)
- ✅ Progress recoverable after compaction

### Parallel Task Execution

When tasks are in the same Parallel Group in TASK-INDEX.md, ALL tasks STILL use `task-execute`. Pattern:
- **One message** containing **multiple Skill tool invocations** (one per task)
- Each `task-execute` call runs in its own subagent with full context loading
- **Max concurrency: 6 agents per wave** (hard limit per pipeline §5)
- **Sub-agent boundary**: Tasks touching `.claude/` paths MUST be sequential (main-session-only) — pre-marked `parallel-safe: false`

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) and [`plan.md` §4 Parallel Execution Strategy](plan.md) for details.

### 🚨 MUST: Multi-File Work Decomposition

For tasks modifying 4+ files, Claude Code MUST:

1. **Decompose into dependency graph** (which changes depend on others)
2. **Delegate to subagents in parallel where safe** (one Task tool call per module in one message)
3. **Parallelize when**: files in different modules, no shared interfaces, no imports between
4. **Serialize when**: tight coupling, sequential creation order needed

Example for R3:
- P4 (MembershipResolverService): 3 parallel subagents — discovery service, identity normalization, options class
- P5 (Node executor): 2 parallel — executor + ActionType enum addition
- P8 (event-publishing): up to 3 parallel — per entity cluster (matter, document, event)

---

## Key Technical Constraints

### From spec.md MUST Rules

- ✅ MUST use `RequireAuthorization("SystemAdmin")` on all `/api/admin/*` endpoints (policy at [`AuthorizationModule.cs:241`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L241))
- ✅ MUST use endpoint filters for resource-level authorization (per ADR-008) — global middleware FORBIDDEN
- ✅ MUST inject services as concretes; interface only if there's an actual testing/swap need (per ADR-010)
- ✅ MUST use `DefaultAzureCredential` (managed identity) for Graph + Dataverse outbound when `Graph__ManagedIdentity__Enabled=true` (per ADR-028)
- ✅ MUST publish to topic `sprk-membership-changes` with subscription-per-consumer (NOT queue, NOT reuse `ServiceBusJobProcessor` queue) — D3 resolved
- ✅ MUST measure publish-size delta on EVERY BFF-touching task (per `.claude/constraints/azure-deployment.md`)
- ✅ MUST follow [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) §§A, F, F.1, F.2, F.3
- ✅ MUST state Placement Justification in PR description for each new BFF service/endpoint (per CLAUDE.md §10 imperative)
- ✅ MUST extend existing PlaybookBuilder componentry for H2 work (per Q5 owner directive — do NOT invent new patterns)
- ❌ MUST NOT inject AI-internal types (`IOpenAiClient`, `IPlaybookService`) into CRUD code — use `Services/Ai/PublicContracts/` facade
- ❌ MUST NOT support identity matching for free-text display-name fields (explicitly out of scope)
- ❌ MUST NOT create a new "PlatformAdmin" policy — use existing `SystemAdmin`

### Cross-cutting (from CLAUDE.md §10 — BFF Hygiene)

- BFF publish-size: ≤+1 MB delta per task (spec NFR-01); baseline ~45.65 MB; hard ceiling 60 MB
- Every BFF-touching PR MUST verify size via `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`
- Every BFF-touching PR MUST verify no new HIGH-severity CVE via `dotnet list package --vulnerable --include-transitive`
- Test update obligation (BFF §10 bullet 6): PRs modifying `Services/` or `Api/` MUST add/update tests in `tests/unit/Sprk.Bff.Api.Tests/`
- Asymmetric-registration rule (BFF §10 §§F.1–F.3): inspect fixture config before assuming DI issue; empirically reproduce before applying ledger fixes; apply ADR-032 Null-Object Kill-Switch Pattern for new conditional services

### Owner Clarifications (resolved 2026-06-20)

These 12 decisions are baked into spec.md and tasks. Re-read spec.md "Owner Clarifications" before any related work:

| Topic | Decision |
|---|---|
| Phase 1D scope | **In-scope** for R3 (transitive memberships always built) |
| AC-1A.5 perf measurement | App Insights server-side request telemetry |
| H3 consumer inventory | Discovery task P7.0 first |
| Phase 2 scope | **In-scope** for R3 (full junction + topic + real recon) |
| D3 transport | Service Bus **topic** `sprk-membership-changes` with subscription-per-consumer |
| Event-source inventory | Discovery task P-event-1 |
| Q1 scheduler correlationId | **Fresh per child playbook** |
| Q2 publish semantics | **Fire-and-forget** (nightly recon backstop) |
| Q3 includeRelated depth | **1 hop max** |
| Q4 lawfirm Lookup target | `sprk_organization` → `identityType: "Organization"` (NOT Contact as design.md showed) |
| Q5 Builder UI | Align with existing PlaybookBuilder componentry — extend per-ActionType form pattern |
| Q6 admin policy | Use existing `SystemAdmin` policy (NOT new `PlatformAdmin`) |

---

## Decisions Made

- **2026-06-20**: Use existing `work/spaarke-platform-foundations-r3` branch (not new `feature/` branch). Reason: matches Spaarke `work/` convention; branch already set up per worktree.
- **2026-06-20**: All 6 unresolved questions from spec.md round 1 answered (Q1-Q6) — see Owner Clarifications above.
- **2026-06-20**: Phase 1D + Phase 2 lifted from "design-only" to firm in-scope. Task count grew from 35-51 → 50-70.
- **2026-06-20**: Pipeline run with explicit parallel-optimized structuring per user directive.

---

## Implementation Notes

- **R2 has `lessons-learned.md`** (predecessor) — review at project start to absorb prior project learnings
- **Existing `ActionType` values** (from grep): 0=AiAnalysis, 51=QueryDataverse, 60=AgentService, 70=GroundingVerify, 80=LiveFact, 90=IndexRetrieve, 100=EvidenceSufficiency, 110=DeclineToFind, 120=ReturnInsightArtifact, 130=Sanitization, 140=ObservationEmit. **R3 adds 52 = LookupUserMembership** (slots into Dataverse-data-ops group with QueryDataverse=51).
- **PlaybookBuilder structure** (verified 2026-06-20): per-ActionType forms in `src/client/code-pages/PlaybookBuilder/src/components/properties/` (e.g., `CreateNotificationForm.tsx`, `ConditionEditor.tsx`). New `LookupUserMembershipForm.tsx` follows same pattern. Validation lives in `services/canvasValidation.ts`. Variable reference logic in `VariableReferencePanel.tsx`.
- **SystemAdmin policy** (verified 2026-06-20 at [`AuthorizationModule.cs:241`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L241)): checks `Admin`/`SystemAdmin` role/claim OR `scope` claim containing "admin". Already used by [`RagEndpoints.cs:157`](../../src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs#L157).
- **BFF baseline**: Capture via `scripts/Capture-BffBaseline.ps1` at project init; note in `notes/bff-baseline.md`. NFR-01 measurements compare against this baseline.

---

## Resources

### Applicable ADRs

| ADR | Title | Relevance |
|---|---|---|
| **ADR-001** | BFF Minimal API + BackgroundService | `Spaarke.Scheduling` stays in-process; no Azure Functions |
| **ADR-007** | SpeFileStore facade | N/A (no SPE file ops in R3) |
| **ADR-008** | Endpoint-filter auth | Membership + admin endpoints follow filter convention |
| **ADR-009** | Redis caching | Identity normalization + membership cache + metadata cache + pub/sub invalidation |
| **ADR-010** | DI minimalism | New services as concretes; `IScheduledJob` + `IMembershipResolverService` allowed |
| **ADR-012** | Shared component library | `Spaarke.Scheduling` is new shared .NET library |
| **ADR-013** | AI architecture | `LookupUserMembership` node executor extends existing AI framework |
| **ADR-016** | Rate-limit handling | Membership endpoint cache + retry |
| **ADR-024** | Polymorphic resolver pattern | Informs identity normalization |
| **ADR-028** | Spaarke Auth v2 | Endpoint uses OBO; identity resolution uses `@spaarke/auth` contract |
| **ADR-029** | BFF publish hygiene | NFR-01 enforcement |
| **ADR-032** | Null-Object Kill-Switch | Apply if any new service is feature-gated |
| **ADR-034 (NEW)** | User-record membership resolution pattern | R3 deliverable — Part 1 |
| **ADR-036 (NEW)** | Background-job infrastructure (Spaarke.Scheduling) | R3 deliverable — Part 2 |

(ADR-035 reserved for R4 playbook rollout-mode; out of R3 scope.)

### Binding Constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — §§A, F, F.1, F.2, F.3 (binding for every BFF-touching task)
- [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) — BFF publish-size per-task verification rule

### Patterns

- [`.claude/patterns/api/endpoint-definition.md`](../../.claude/patterns/api/endpoint-definition.md)
- [`.claude/patterns/api/endpoint-filters.md`](../../.claude/patterns/api/endpoint-filters.md)
- [`.claude/patterns/api/service-registration.md`](../../.claude/patterns/api/service-registration.md)
- [`.claude/patterns/api/background-workers.md`](../../.claude/patterns/api/background-workers.md)
- [`.claude/patterns/api/error-handling.md`](../../.claude/patterns/api/error-handling.md)
- [`.claude/patterns/api/resilience.md`](../../.claude/patterns/api/resilience.md)
- [`.claude/patterns/dataverse/web-api-client.md`](../../.claude/patterns/dataverse/web-api-client.md)
- [`.claude/patterns/dataverse/entity-operations.md`](../../.claude/patterns/dataverse/entity-operations.md)
- [`.claude/patterns/dataverse/relationship-navigation.md`](../../.claude/patterns/dataverse/relationship-navigation.md)
- [`.claude/patterns/dataverse/polymorphic-resolver.md`](../../.claude/patterns/dataverse/polymorphic-resolver.md)
- [`.claude/patterns/ai/endpoint-di-symmetry.md`](../../.claude/patterns/ai/endpoint-di-symmetry.md)
- [`.claude/patterns/ai/public-contracts-facade.md`](../../.claude/patterns/ai/public-contracts-facade.md)

### Reusable Code (canonical implementations)

- [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/QueryDataverseNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/QueryDataverseNodeExecutor.cs) — closest analog for new `LookupUserMembershipNodeExecutor`
- [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AgentServiceNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AgentServiceNodeExecutor.cs) — Singleton-with-Scoped-dep pattern
- [`src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerService.cs) — current state being migrated to Spaarke.Scheduling
- [`src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs) — existing SB consumer (Phase 2 uses NEW topic, not this queue)
- [`src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs) — register `default` + `joinIds` helpers
- [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/DeliverToIndexNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/DeliverToIndexNodeExecutor.cs) — rewrite for `sprk_searchindex*` schema migration
- [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs) — add `LookupUserMembership = 52` to ActionType enum
- [`src/client/code-pages/PlaybookBuilder/src/components/properties/CreateNotificationForm.tsx`](../../src/client/code-pages/PlaybookBuilder/src/components/properties/CreateNotificationForm.tsx) — pattern for new `LookupUserMembershipForm.tsx`
- [`src/client/code-pages/PlaybookBuilder/src/services/canvasValidation.ts`](../../src/client/code-pages/PlaybookBuilder/src/services/canvasValidation.ts) — H2 validation extensions
- [`src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs) — `SystemAdmin` policy reference

### Related Projects

- [`projects/spaarke-daily-update-service-r2/`](../spaarke-daily-update-service-r2/) — R2 predecessor (UAT surfaced the gaps)
- [`projects/spaarke-daily-update-service-r2.2-hotfix/`](../spaarke-daily-update-service-r2.2-hotfix/) — Parallel R2.2 hotfix (different branch, no overlap)
- Future: `spaarke-playbook-rollout-mode-r4` — picks up G11/H4 (ADR-035 reserved)
- Future: opportunistic migration of 26 remaining BackgroundService implementations to `Spaarke.Scheduling`

### External Documentation

- Cronos NuGet (cron parsing): https://github.com/HangfireIO/Cronos
- Microsoft Dataverse Web API EntityDefinitions: standard Microsoft Learn docs
- Azure Service Bus topics + subscriptions: standard Microsoft Learn docs

---

*This file should be kept updated throughout project lifecycle. Last refreshed 2026-06-20 at project initialization.*
