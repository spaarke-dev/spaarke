# AI Spaarke Action Engine R1 — AI Context

> **Purpose**: This file provides context for Claude Code when working on the ai-spaarke-action-engine-r1 project.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Phase 0 — Architecture Spike
- **Last Updated**: 2026-05-29
- **Current Task**: Not started — task 001 is next
- **Next Action**: Run `execute task 001` to launch the architecture spike

---

## Quick Reference

### Key Files

- [`spec.md`](spec.md) — Functional + non-functional spec (~6,500 words; source of truth)
- [`design.md`](design.md) — Multi-surface Assistant model + conceptual model (~63KB)
- [`README.md`](README.md) — Project overview + graduation criteria (12 gates)
- [`plan.md`](plan.md) — 7-phase WBS + discovered resources + risk register
- [`current-task.md`](current-task.md) — **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Task tracker + parallel groups + dependency graph
- [`coordination-assessment-with-insights-engine.md`](coordination-assessment-with-insights-engine.md) — Joint ownership decisions (signal envelope, gate primitive, Tool Registry stewardship)
- [`lavern-pattern-assessment.md`](lavern-pattern-assessment.md) — External reference (LAVERN patterns adopted inline)

### Project Metadata

- **Project Name**: ai-spaarke-action-engine-r1
- **Type**: BFF feature (Services/Ai/ActionEngine/) + Dataverse schema + Client surfaces (SpaarkeAi, GateApprovalCard)
- **Complexity**: High (multi-surface, 35 tasks, gates publish-size + p95 NFRs, cross-project coordination)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check `current-task.md`** for active work state (especially after compaction/new session)
3. **Reference `spec.md`** for design decisions, requirements, and acceptance criteria
4. **Reference `design.md`** for multi-surface Assistant model + system prompt §7.4.3
5. **Load the relevant task file** from `tasks/` based on current work
6. **Apply ADRs** relevant to the technologies used (loaded automatically via `adr-aware` based on task tags)
7. **Honor binding constraints** — `.claude/constraints/bff-extensions.md` is MANDATORY for any BFF-touching task (root CLAUDE.md §10)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md).

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke the `task-execute` skill:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via `task-execute` |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via `task-execute` |
| "next task" | Execute next pending task via `task-execute` |
| "keep going" | Execute next pending task via `task-execute` |
| "resume task X" | Execute task X via `task-execute` |
| "pick up where we left off" | Load `current-task.md`, invoke `task-execute` |

**Implementation**: When the user triggers task work, invoke the Skill tool with `skill="task-execute"` and the task file path.

### Why This Matters

The `task-execute` skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in `current-task.md`
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (`code-review` + `adr-check`) at Step 9.5
- ✅ Progress is recoverable after compaction

**Bypassing this skill leads to**:
- ❌ Missing ADR constraints (esp. bff-extensions.md governance)
- ❌ No checkpointing — lost progress after compaction
- ❌ Skipped quality gates

### Parallel Task Execution

When tasks can run in parallel (per `TASK-INDEX.md` parallel groups), each task MUST still use `task-execute`:
- Send ONE message with MULTIPLE `Skill` tool invocations
- Each invocation calls `task-execute` with a different task file
- Example: tasks 010, 011, 012, 013 in Phase 1 Group A → four separate `task-execute` calls in one message
- **MAX CONCURRENCY: 6 agents per wave** (root CLAUDE.md guidance)

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for the complete protocol.

### 🚨 MUST: Multi-File Work Decomposition

**For tasks modifying 4+ files, Claude Code MUST:**

1. **Decompose into dependency graph**: group files by module/component; identify shared interfaces.
2. **Delegate to subagents in parallel where safe** (Task tool, `subagent_type="general-purpose"`):
   - Files in different modules → CAN parallelize
   - Files with no shared imports → CAN parallelize
3. **Serialize when** files share state, or one creates a contract another consumes (e.g., facade interface before consumers).
4. **Permission boundary**: tasks touching `.claude/` paths MUST be sequential (main-session-only — root CLAUDE.md §3).

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for the protocol.

---

## Key Technical Constraints

These are the binding rules every task in this project must respect. Extracted from `spec.md`, applicable ADRs, and `.claude/constraints/bff-extensions.md`.

### Architecture & Placement

- **ADR-001** — Use .NET 8 Minimal API + BackgroundService. NO Azure Functions for in-proc Action execution. Scheduler MAY be Azure-native (task 001 decides between Logic Apps timer / Service Bus scheduled / Functions timer / Container Apps Jobs).
- **ADR-010** — Register Action Engine via `AddActionEngineModule()` extension in `Infrastructure/DI/`. NO flat `Program.cs` registration. ≤15 non-framework DI registrations.
- **ADR-013 (refined 2026-05-20)** — Action Engine lives in BFF per decision criteria. CRUD-side consumers MUST reach Action Engine via `Services/Ai/PublicContracts/IActionEngineFacade.cs` — never inject `IActionOrchestrationService` or other internals directly.

### Authorization

- **ADR-008** — Every Action Engine endpoint MUST apply endpoint-filter authorization via `.AddEndpointFilter<>()`. NO global middleware.
- **ADR-028** — Spaarke Auth v2: clients use `useAuth()` / `authenticatedFetch` for BFF calls; server uses `DefaultAzureCredential` for outbound (MI). Audit middleware applies.
- **ADR-003** — Multi-surface OBO chain for user-attributable Actions.

### Background Work

- **ADR-004** — `ScheduledActionDispatchJobHandler` implements `IJobHandler<T>` and is consumed by existing `ServiceBusJobProcessor`. Honors idempotency via `IIdempotencyService`.
- **ADR-002** — Action triggers via Dataverse webhooks (R2 scope), NOT plugins. Plugins stay thin (no HTTP/Graph calls).

### Audit, Errors, Flags, Limits

- **ADR-015** — Audit per Tool dispatch: Tier 2 hash-only (compliance) + Tier 3 Cosmos work history (tenant-partitioned, GDPR erasure path). Extend existing `AuditEnrichmentMiddleware`.
- **ADR-019** — All endpoint errors use ProblemDetails (RFC 7807).
- **ADR-018** — Action governance block references feature flag + kill-switch.
- **ADR-016** — Rate limiting on `/run` endpoints: soft enforcement MVP; hard caps deferred to R2.

### UI

- **ADR-021** — Fluent UI v9 only. `GateApprovalCard` uses semantic tokens. Dark-mode parity MUST be tested.

### Publish Hygiene (Binding for NFR-10)

- **ADR-029** — Framework-dependent linux-x64 publish. Sourcemap exclusion. **Publish-size delta MUST be ≤ 5 MB compressed** (NFR-10). Task 001 spike measures empirically; task 065 validates pre-deploy.

### BFF Extensions Governance (Binding — root CLAUDE.md §10)

Every BFF-touching task MUST:
1. Load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) before design
2. State the placement decision explicitly (even if "in BFF") in PR description; cite decision criteria
3. Use `Services/Ai/PublicContracts/` facade for any CRUD code needing AE capability
4. Verify publish-size impact (baseline ~60 MB compressed)
5. Verify no new HIGH-severity CVE via `dotnet list package --vulnerable --include-transitive`

### Performance NFRs

- **NFR-01**: `FindResources` p95 < 200ms — verified by load test + App Insights metric
- **NFR-10**: Publish-size delta ≤ 5 MB — verified before deploy

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

- **2026-05-29 — Project initialized**: Stay on existing `work/ai-spaarke-action-engine-r1` worktree branch (no separate `feature/` branch). Task 001 = architecture spike that blocks all Phase 1+ tasks. Pipeline stops after task generation (no auto-execute). — `/project-pipeline` user decisions.

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

- **Master staleness at init time**: 5 work branches unmerged (insights-engine-r1 with 35, sdap-bff-api-remediation-fix with 50, matter-ui-r1 with 47, ai-platform-unification-r4 with 18, -r3 with 3). Insights Engine in particular shares signal envelope + gate primitive + Tool Registry concerns — coordinate before Phase 5.
- **Pre-existing HIGH CVE on `Microsoft.Kiota.Abstractions 1.21.2`** — NOT introduced by this project. Task 066 (CVE scan) verifies no NEW HIGH-sev CVEs.
- **PR #306 (assistant new resources)** may overlap SpaarkeAi launcher work in Phase 5. Coordinate timing or rebase.

---

## Resources

### Applicable ADRs

15 ADRs apply to this project. Concise versions in `.claude/adr/`; full versions in `docs/adr/`.

| ADR | Title | Why applicable |
|-----|-------|----------------|
| ADR-001 | Minimal API + BackgroundService | BFF placement; scheduler choice constrained |
| ADR-002 | Thin Dataverse plugins | Action triggers via webhooks, not plugins |
| ADR-003 | Authorization seams | Multi-surface OBO chain |
| ADR-004 | Job Contract pattern | `ScheduledActionDispatchJobHandler` |
| ADR-007 | SpeFileStore facade | Any Tool touching SPE |
| ADR-008 | Endpoint-filter auth | Every Action Engine endpoint |
| ADR-010 | DI minimalism | `AddActionEngineModule()` feature-module |
| ADR-013 | AI Architecture (refined 2026-05-20) | BFF placement + `PublicContracts/` facade |
| ADR-015 | Audit middleware | Tier 2 hash + Tier 3 Cosmos per Tool dispatch |
| ADR-016 | Rate limiting | Soft MVP; hard caps R2 |
| ADR-018 | Feature flags + kill switches | Action governance block |
| ADR-019 | ProblemDetails | Endpoint errors |
| ADR-021 | Fluent UI v9 + dark mode | `GateApprovalCard` |
| ADR-028 | Spaarke Auth v2 | `useAuth()` / MI / audit middleware |
| ADR-029 | BFF Publish Hygiene | ≤5MB delta (NFR-10) |

### Binding Constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — Pre-merge checklist + decision criteria
- [`.claude/constraints/auth.md`](../../.claude/constraints/auth.md) — OAuth/OBO rules
- [`.claude/constraints/ai.md`](../../.claude/constraints/ai.md) — AI feature constraints
- [`.claude/constraints/api.md`](../../.claude/constraints/api.md) — BFF endpoint rules
- [`.claude/constraints/jobs.md`](../../.claude/constraints/jobs.md) — Background worker rules

### Knowledge Articles

- [`docs/architecture/AI-ARCHITECTURE.md`](../../docs/architecture/AI-ARCHITECTURE.md)
- [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md)
- [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md)
- [`docs/architecture/jobs-architecture.md`](../../docs/architecture/jobs-architecture.md)
- [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) — Governance evidence base
- [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md)
- [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md)

### Related Projects

- **`work/ai-spaarke-insights-engine-r1`** — Parallel project. Shared concerns: `InsightArtifact` signal envelope contract, `IGateResolver` consumption, Tool Registry classification taxonomy. See [`coordination-assessment-with-insights-engine.md`](coordination-assessment-with-insights-engine.md).

### External Documentation

- LAVERN pattern reference (external). Patterns adopted inline; no separate ADR ratification. See [`lavern-pattern-assessment.md`](lavern-pattern-assessment.md).

---

*This file should be kept updated throughout the project lifecycle. After Phase 0 (task 001) completes, update the runtime topology section with the scheduler choice and Hybrid D confirmation.*
