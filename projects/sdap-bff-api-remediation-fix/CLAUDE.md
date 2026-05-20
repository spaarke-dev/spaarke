# sdap-bff-api-remediation-fix — AI Context

> **Purpose**: This file provides context for Claude Code when working on the BFF API remediation project.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Phase 0 (Pre-flight resolution) — pending
- **Last Updated**: 2026-05-20
- **Current Task**: Not started
- **Next Action**: Operator runs `/task-execute projects/sdap-bff-api-remediation-fix/tasks/001-owner-signoff-resolved-decisions.poml`

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (308 lines; permanent reference)
- [`design.md`](design.md) — Full design document (594 lines; rationale + decisions)
- [`approach.md`](approach.md) — Upstream record (2026-05-19; preserved)
- [`README.md`](README.md) — Project overview + graduation criteria
- [`plan.md`](plan.md) — Implementation plan + WBS + **Discovered Resources** (§2)
- [`current-task.md`](current-task.md) — Active task state (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Per-phase task tracker

### Project Metadata
- **Project Name**: sdap-bff-api-remediation-fix
- **Type**: Backend remediation (BFF API) + documentation codification
- **Complexity**: High — 5 outcomes × 7 phases × ~55 tasks; 4–6 week calendar
- **Module touched**: `src/server/api/Sprk.Bff.Api/`
- **Sub-agent write boundary**: All `.claude/` updates (Phase 6) are main-session-only

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md and design.md** for design decisions, requirements, and acceptance criteria — `design.md` has the "why", `spec.md` has the "what"
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** listed below (loaded automatically via `adr-aware` + tag mapping)
6. **Load `.claude/constraints/bff-extensions.md`** — binding for every BFF-touching task per root CLAUDE.md §10

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke task-execute skill:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in current-task.md
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5 for FULL-rigor tasks
- ✅ Progress is recoverable after compaction

**Bypassing this skill leads to**:
- ❌ Missing ADR constraints (especially refined ADR-013 facade requirement)
- ❌ No checkpointing — lost progress after compaction
- ❌ Skipped quality gates on Phase 4 code changes

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute. Send one message with multiple Skill tool invocations. For this project, **parallel candidates exist in Phase 0 (UQ coordination), Phase 1 (inventory commands), and Phase 4 Outcome E migrations (Group F)** — see TASK-INDEX.md "Parallel Execution Groups" table.

**Phase 6 exception**: All Phase 6 tasks touch `.claude/` paths and are `parallel-safe: false`. They MUST run sequentially in the main session per the sub-agent write boundary (root CLAUDE.md §3).

### 🚨 MUST: Multi-File Work Decomposition

For tasks modifying 4+ files (notably tasks 047–050 for facade migration), Claude Code MUST:

1. **Decompose into dependency graph**:
   - Group files by consumer module (Finance, Workspace, Jobs, Dataverse, Filters)
   - Identify shared dependencies (facade interfaces must exist before migrations)
   - Separate parallel-safe work from sequential

2. **Parallelize when**:
   - Files are in different consumer modules → CAN parallelize (Finance vs Workspace vs Dataverse)
   - Files have no shared interfaces beyond the facade → CAN parallelize after task 046 completes

3. **Serialize when**:
   - Task 046 (facade interfaces) MUST complete before tasks 047–050 can compile
   - Task 051 (handler relocation) MUST run after 047–050 are verified stable

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

**Binding ADR rules** (see plan.md §2 for full list):
- ✅ MUST route external CRUD→AI consumers through `Services/Ai/PublicContracts/` facade (refined **ADR-013**, binding for FR-E1/FR-E2)
- ✅ MUST publish to `deploy/api-publish/` (NOT `/tmp` — anti-pattern #16)
- ✅ MUST keep Kiota packages version-matched at `1.21.2`
- ✅ MUST use `Deploy-BffApi.ps1` for all BFF deploys
- ✅ MUST load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) for any BFF-touching task
- ❌ MUST NOT set `<PublishTrimmed>` or `<PublishAot>` (reflection-hostile)
- ❌ MUST NOT add new direct CRUD→AI dependencies (use facade)
- ❌ MUST NOT bump Kiota individually, Graph SDK, .NET TFM, or pre-release AI packages

**Code-State Deltas from Spec** (verified during pipeline pre-flight):

1. **CRUD→AI count is ~59 files / 148 occurrences**, not the spec's "20". Per-folder reality:
   - Finance: 3 files ✓ matches spec
   - Workspace: 2 files (spec said 4)
   - Jobs: 2 files (spec said 6)
   - Dataverse: 0 files (spec said 2)
   - Api/Filters: 1 file (spec said 5+)
   - Api/Endpoints: 6 additional files
   - Services/Ai/: 29 internal files (do NOT count — internal coupling is fine)
   - Infrastructure/DI: 4 modules

   **Resolution (PF-3)**: Outcome E task scope defers to Phase 1 inventory output. Tasks 047–050 reference "inventory-derived list" not spec's "20". `spec.md` left intact; Phase 1 is source of truth.

2. **`Services/Ai/Handlers/` already exists** with `GenericAnalysisHandler`. The new `Services/Ai/Jobs/` (FR-E3 target) is a **distinct sibling directory** — task 051 explicitly clarifies this distinction.

3. **4 `.map` files** in `wwwroot/playbook-builder/assets/`:
   - `flow-vendor-BHHmI87s.js.map`
   - `fluent-vendor-CmJVTK5h.js.map`
   - `index-BWeOj5bW.js.map`
   - `react-vendor-BWFb42Va.js.map`

4. **csproj currently has NO `<RuntimeIdentifier>`**, no `<PublishTrimmed>`, no `<PublishAot>` — FR-A1 cleanly applies as a NEW publish flag, not a modification.

5. **Pre-release pins exact match**: `Azure.AI.Projects 1.0.0-beta.8`, `Microsoft.Agents.AI 1.0.0-rc1`, `Azure.AI.OpenAI 2.8.0-beta.1`. Inline csproj rationale comments are current.

6. **Confirmed active vulnerability** (build-warning observed at pipeline pre-flight): `NU1903 — Microsoft.Kiota.Abstractions 1.21.2 has a known HIGH severity vulnerability` (GHSA-7j59-v9qr-6fq9). This is Outcome B target #1 — task 011 will enumerate the full scan; task 043 will be the patch task.

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

- **2026-05-20**: Keep AI in BFF (no extraction). Rationale: latency budgets <50ms/<100ms/<500ms; transactional Cosmos coupling; 100% streaming AI per extraction assessment. — Refined ADR-013 + assessment.
- **2026-05-20**: Outcome E uses small focused facade interfaces (`IBriefingAi`, `IInvoiceAi`, `IRecordMatchingAi`, `IWorkspacePrefillAi`) per UQ-07 default. Owner confirms in Phase 0 task 007.
- **2026-05-20**: Master pulled into work branch at pipeline pre-flight (PF-1 resolution). Build verified passing (0 errors, 17 warnings).

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

- **Sub-agent write boundary** (root CLAUDE.md §3): Sub-agents cannot edit `.claude/` paths. Phase 6 tasks (070–081) — except 070 (scripts/) and 071–075 (.github/) — are all main-session-only.
- **Distinct directories**: `Services/Ai/Handlers/` (existing, with `GenericAnalysisHandler`) ≠ `Services/Ai/Jobs/` (NEW per FR-E3). Task 051 documentation must make this clear in the commit message and the BFF CLAUDE.md update (task 081).
- **Phase 4 bake windows**: 24h dev / 48h demo / 7d prod. Operator-paced; not autonomous. Each Phase 4 candidate task includes the bake window in its acceptance criteria.
- **Active vulnerability discovered at pre-flight**: `Microsoft.Kiota.Abstractions 1.21.2` has HIGH NU1903. Pinning rationale (CLAUDE.md line on Kiota chain-lock) means task 043 must coordinate the upgrade with `Microsoft.Graph` SDK chain.
- **Dependabot PR coordination**: 15+ open Dependabot PRs touch `Sprk.Bff.Api/` (PR #289 Microsoft.Agents.AI, #266 DocumentFormat.OpenXml, #248 Azure.Security.KeyVault.Secrets, etc.). Phase 1 inventory should reconcile against these (task 011 — outdated/vuln scan) before patching to avoid PR conflicts.

---

## Resources

### Applicable ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) | Minimal API + Workers | Single deployable; preserved |
| [ADR-004](../../.claude/adr/ADR-004-job-contract.md) | Job Contract | FR-E3 handler relocation preserves `IJobHandler<T>` contract |
| [ADR-007](../../.claude/adr/ADR-007-spefilestore.md) | SpeFileStore Facade | **Canonical model for Outcome E facade** (task 046) |
| [ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md) | Endpoint Filters | Authorization preserved |
| [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) | DI Minimalism | Known violation (99+ vs ≤15); **no-worsen rule applies** |
| [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) | AI Architecture (refined 2026-05-20) | **BINDING for FR-E1/FR-E2 facade requirement** |
| [ADR-027](../../.claude/adr/ADR-027-subscription-isolation-managed-solutions.md) | Subscription Isolation | Phase 5 promotion process |
| [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) | Spaarke Auth | Auth flows preserved |
| ADR-029 (forthcoming) | BFF Publish Hygiene | **Created in Phase 6 task 076/077** |

### Applicable Constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — **binding pre-merge checklist for all BFF additions**
- [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) — publish location, baseline size, stdout logging; FR-D2 adds Publish Hygiene
- [`.claude/constraints/api.md`](../../.claude/constraints/api.md), [`ai.md`](../../.claude/constraints/ai.md), [`jobs.md`](../../.claude/constraints/jobs.md) — tag-mapped per task

### Applicable Skills

- [`bff-deploy`](../../.claude/skills/bff-deploy/SKILL.md) — canonical BFF deploy (Phase 4/5 tasks); FR-D3 updates it
- [`task-execute`](../../.claude/skills/task-execute/SKILL.md) — invokes adr-aware + code-review + adr-check at Step 9.5
- [`adr-aware`](../../.claude/skills/adr-aware/SKILL.md), [`adr-check`](../../.claude/skills/adr-check/SKILL.md), [`code-review`](../../.claude/skills/code-review/SKILL.md)
- [`repo-cleanup`](../../.claude/skills/repo-cleanup/SKILL.md) — wrap-up task 090

### Knowledge / Patterns

- [`src/server/api/Sprk.Bff.Api/CLAUDE.md`](../../src/server/api/Sprk.Bff.Api/CLAUDE.md) — module guidance (FR-D7 updates it)
- [`src/server/api/Sprk.Bff.Api/Services/SpeFileStore.cs`](../../src/server/api/Sprk.Bff.Api/Services/SpeFileStore.cs) — **canonical facade-over-Graph-SDK pattern** (model for task 046)
- [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) — evidence base
- [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §22.2 — extraction-trigger context
- [`docs/standards/ANTI-PATTERNS.md`](../../docs/standards/ANTI-PATTERNS.md) #16 — `/tmp` publish anti-pattern
- [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md) §9 — endpoint smoke-test source list
- [`.claude/FAILURE-MODES.md`](../../.claude/FAILURE-MODES.md) — AP-1 / G-2 / G-3 (existing); FR-D4 adds new entry

### Related Projects

- `sdap-bff-api-and-performance-enhancement-r1` — active; coordinate to avoid in-flight BFF deploy during Phase 4 (UQ-03)
- `ai-spaarke-insights-engine-r1` — pre-implementation; coordinate baseline window (UQ-04)
- `spaarke-ai-platform-unification-r2` — active; potential file overlap on AI internals (informational)

### External Documentation

- Azure App Service Linux deployment: framework-dependent vs self-contained — see `docs/guides/auth-deployment-setup.md`
- `dotnet list package --vulnerable --include-transitive` — primary Outcome B tool
- `actionlint` — optional for FR-D6 verification
- GitHub Advisory database — referenced for CVE evidence in CANDIDATES.md

---

*This file should be kept updated throughout project lifecycle. Decisions and Implementation Notes sections grow during execution.*
