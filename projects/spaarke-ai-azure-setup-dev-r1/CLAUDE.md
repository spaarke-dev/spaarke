# Spaarke AI Search Azure Setup (Dev Restoration + Canonicalization) - AI Context

> **Purpose**: This file provides context for Claude Code when working on `spaarke-ai-azure-setup-dev-r1`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Ready for Implementation
- **Last Updated**: 2026-06-26
- **Current Task**: Not started
- **Next Action**: Execute task 001 (Pre-Phase-3 operational verification per FR-21) via task-execute skill

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - Authoritative spec (21 FRs, 14 NFRs, 5 phases)
- [`design.md`](design.md) - Design rationale, resource inventory, 5-phase plan
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan with phase breakdown + discovered resources
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task registry + parallel-execution groups
- [`notes/pre-phase-3-verification.md`](notes/pre-phase-3-verification.md) - 10-check pre-flight evidence (FR-21, created in task 001)

### Project Metadata
- **Project Name**: spaarke-ai-azure-setup-dev-r1
- **Type**: Infrastructure restoration + canonicalization (Azure AI Search + BFF refactor + docs)
- **Complexity**: Medium-High (~20 BFF files, 7 schemas, 5 phases, 30+ tasks)
- **Branch**: `work/spaarke-ai-azure-setup-dev-r1`

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for FRs/NFRs/acceptance criteria (always-cite-by-number: FR-XX, NFR-XX)
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** automatically loaded via `adr-aware` skill based on tags

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke the `task-execute` skill:

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
- Knowledge files are loaded (ADRs, constraints, patterns)
- Context is properly tracked in `current-task.md`
- Proactive checkpointing occurs every 3 steps
- Quality gates run (`code-review` + `adr-check`) at Step 9.5 for FULL-rigor tasks
- Progress is recoverable after compaction
- Rigor level declared explicitly at task start

**Bypassing this skill leads to**:
- Missing ADR constraints
- No checkpointing — lost progress after compaction
- Skipped quality gates
- Manual errors (especially the NFR-14 fixture sweep)

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute:
- Send ONE message with MULTIPLE Skill tool invocations
- Each invocation calls task-execute with a different task file
- Example: Tasks 002-008 (Phase 1 docs) in parallel → 7 separate task-execute calls in one message

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

### 🚨 MUST: Multi-File Work Decomposition

**For tasks modifying 4+ files, Claude Code MUST**:

1. **Decompose into dependency graph** — group files by module/component
2. **Delegate to subagents in parallel where safe** — Task tool with `subagent_type="general-purpose"`; ONE message with MULTIPLE Task tool calls
3. **Parallelize when**: files are in different modules, no shared interfaces, no imports between them
4. **Serialize when**: tight coupling, one file must be created before another uses it, sequential logic required

**Project-specific gotcha** — Tasks touching `.claude/` paths (e.g., `.claude/patterns/ai/indexing-pipeline.md`, `.claude/skills/add-reference-to-index/SKILL.md`) MUST be `parallel-safe=false` — sub-agents CANNOT write to `.claude/` per the sub-agent write boundary (root CLAUDE.md §3).

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

### Binding Project Rules (from spec.md MUST-rules)

- **MUST** consolidate ALL index-deployment PowerShell scripts into single `scripts/ai-search/Deploy-AllIndexes.ps1` — no per-index wrappers, no backward-compat shim scripts (FR-07)
- **MUST** use canonical index names per `AI-SEARCH-INDEX-CATALOG.md` — no env suffix on indexes (NFR-03)
- **MUST** apply schema property policy unless Azure-restricted; document overrides with JSON comments (NFR-09)
- **MUST** use 3072-dim vectors (`text-embedding-3-large`); no 1536-dim vectors (NFR-11)
- **MUST** migrate dev BFF hardcoded URLs + API keys to Key Vault references via `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=...)` form (FR-15)
- **MUST** verify publish-size delta ≤ 0 MB on BFF refactor (CLAUDE.md §10 NFR-04)
- **MUST** coordinate renames atomically (one rename = one PR per NFR-07)
- **MUST** sweep test fixtures alongside production DI changes — same PR (NFR-14)
- **MUST NOT** touch prod or demo AI Search services during this project (NFR-05)
- **MUST NOT** introduce new BFF endpoints, services, DI registrations, or packages — this is a refactor (CLAUDE.md §10)
- **MUST NOT** restore retired indexes (`spaarke-knowledge-index-v2`, `discovery-index`, `spaarke-knowledge-shared`, `spaarke-knowledge-index` v1, `knowledge-index`)
- **MUST NOT** skip the post-deploy invariant verifier on any index deployment (NFR-02)

### Applicable ADRs

| ADR | Topic | Project Relevance |
|-----|-------|-------------------|
| **ADR-013** | AI services bounded concurrency | Ingestion scripts MUST honor `SemaphoreSlim` patterns (max 16 indexing, max 5 search) |
| **ADR-017** | Background jobs (Service Bus pattern) | `RagIndexingJobHandler` + `BulkRagIndexingJobHandler` refactors MUST preserve job contract |
| **ADR-028** | Spaarke Auth v2 (canonical) | Dev BFF KV-ref migration uses `@Microsoft.KeyVault(...)` syntax + MI resolution |
| **ADR-014** | *(currently misattributed: caching)* | Inline-cited for tenant isolation. FR-06 resolves drift |
| **ADR-004** | *(currently misattributed: job contract)* | Inline-cited for idempotent re-indexing. FR-06 resolves drift |
| **ADR-032** | BFF Null-Object kill-switch | Applies to any future feature-gated AI-Search service (preventive, not currently used) |
| **ADR-009** | Caching policy | Amendment context (post-Redis project) — reference, not modified by this project |

### Tech Stack
- **PowerShell 7+** for `Deploy-AllIndexes.ps1` (mirrors `Deploy-RedisCache.ps1` structure)
- **Bicep** for resource shape (consistent with Redis project hybrid pattern); PS handles env-routing + KV secret + cross-resource wiring
- **.NET 8 Minimal API** (BFF refactor — string constants + DI updates only; no new endpoints/services)
- **Azure CLI** for service queries + role grants + Service Bus + Cognitive Services verification
- **Azure AI Search REST API** (2024-07-01) for index PUT calls

### Spaarke Code Conventions
- Per root CLAUDE.md §10 (BFF Hygiene): NO new endpoints / services / DI registrations / packages. This project is pure refactor + rename + bug fix
- Per root CLAUDE.md §11 (Component Justification): only applies to tasks adding NEW surface; this project modifies existing surface — most tasks SKIP §11 gate
- Per root CLAUDE.md §F.2 (Fixture-Config-FIRST): when a `WebApplicationFactory` test fails after DI changes, FIRST inspect fixture config before assuming DI bug

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

| Date | Decision | Rationale | Who |
|------|----------|-----------|-----|
| 2026-06-25 | Two-tier naming convention: top-level env-suffixed, sub-resources env-agnostic | Build-once-deploy-anywhere; DNS uniqueness only at top level | Ralph Schroeder |
| 2026-06-26 | `spaarke-rag-references` canonical field = `documentType` (rename `domain`) | Bug confirmed: PS writers use `domain`, C# reader filters `documentType` → PS-indexed docs invisible | Pre-pipeline investigation |
| 2026-06-26 | Single unified deployer; retire 5 per-index PS scripts | One source of truth; mirrors Redis project's validated pattern | Spec FR-07 + design §Placement |
| 2026-06-26 | `text-embedding-3-large` (3072 dim) canonical embedding | Schema uses 3072; appsettings drift to `-small` (1536) is the bug | FR-20; NFR-11 |
| 2026-06-26 | Canonical dev KV = `spaarke-spekvcert` (NOT `sprkspaarkedev-aif-kv`) | Redis project handoff confirmed: BFF MI has no role on AI Foundry KV | Redis handoff §3 |

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

### Pre-Pipeline Audit Findings (2026-06-26)

- Pre-pipeline grep audit found `appsettings.template.json:248` declares `text-embedding-3-small` (1536-dim) — drift from schemas. Caused FR-20 to be added.
- Comprehensive audit found additional knowledge-v2 consumers beyond initial FR-13 scope: `FileIndexingService`, `ReferenceIndexingService`, `ReferenceRetrievalService`, `SessionFilesCleanupJob`, `PlaybookIndexingService`, `PlaybookIndexingBackgroundService`, `PlaybookIndexDriftDetectionJob`. FR-13 scope expanded twice.
- Validation sweep confirmed ALL frontend AI-Search access in `src/solutions/` goes through BFF API contract — ZERO direct `*.search.windows.net` calls from frontends or declarative agents. BFF refactor transparently covers production frontend dependencies.

### Redis Project Lessons (apply here)

1. **Spec assumptions can be wrong** — verify Azure resource state before authoring tasks. Redis spec narrative was wrong ("legacy Redis was deleted before this project") — actually it was running but disabled.
2. **KV name verification**: ALWAYS query `az role assignment list --all --assignee <BFF-MI-objectId>` before assuming a KV name. Spec Assumption #5 (sprkspaarkedev-aif-kv) was wrong.
3. **Bicep+PS hybrid is canonical** — PS calls Bicep; PS handles env-routing, KV secret, App Settings; Bicep is purely resource shape.
4. **`SupportsShouldProcess` template** — wraps every destructive call in `if ($PSCmdlet.ShouldProcess(...))` for native `-WhatIf` support.
5. **§F.2 Fixture-Config-FIRST** — DI tightening at startup (e.g., AI-Search forcing KV refs) WILL cause latent `WebApplicationFactory` test failures. Redis project hit 337. NFR-14 sweep IS the preventive gate.

### Project-Specific Watchpoints

- **`.claude/` write boundary**: 3 tasks in Phase 4 touch `.claude/patterns/ai/indexing-pipeline.md` + `.claude/skills/add-reference-to-index/SKILL.md`. These MUST be `parallel-safe=false` (main session only).
- **Atomic renames per NFR-07**: each of the 3 renames (files-index, playbook-embeddings, invoices-index) = ONE PR touching schema + JSON `name` field + BFF code constants + deploy script reference + BU value config + runbook table.
- **`.claude/FAILURE-MODES.md:148`** is a HISTORICAL AP-2 reference — LEAVE as-is, do not "fix" the `spaarke-knowledge-index-v2` reference there.
- **`.claude/skills/azure-deploy/SKILL.md:225`** references `spaarke-search-dev` as a service name (NOT an index name) — no action unless service name changes (it doesn't).

---

## Resources

### Applicable ADRs
See "Key Technical Constraints" → Applicable ADRs table above.

### Skills Used
- `task-execute` (mandatory)
- `adr-aware`, `adr-check`, `code-review` (quality gates)
- `azure-deploy`, `bff-deploy`, `script-aware`
- `dataverse-mcp-usage` (Phase 5 ingestion validation)
- `push-to-github`, `merge-to-master` (per-rename atomic PRs)

### Related Projects
- **`spaarke-redis-cache-remediation-r1`** — DELIVERED 2026-06-26 (PR #458, master `567b98112`). Prerequisite to Phase 3 (NFR-13). Handoff doc: `projects/spaarke-redis-cache-remediation-r1/notes/handoff-to-ai-search-project.md` (canonical KV name, Bicep+PS pattern, lessons #1-5).
- **`spaarke-environment-factory-r1`** — Future consumer of this project's `Deploy-AllIndexes.ps1` + `AI-SEARCH-INDEX-CATALOG.md` + `SPAARKE-DEPLOYMENT-GUIDE.md` §4.6. This project is its prerequisite, NOT part of it.

### External Documentation
- [Azure AI Search REST API 2024-07-01](https://learn.microsoft.com/en-us/rest/api/searchservice/)
- [Azure OpenAI text-embedding-3-large](https://learn.microsoft.com/en-us/azure/ai-services/openai/concepts/models#embeddings-models)
- [Key Vault references for App Service](https://learn.microsoft.com/en-us/azure/app-service/app-service-key-vault-references)

### Knowledge Repository (for rapidly-evolving topics)
- `knowledge/azure-ai-search/` — researcher subagent consults BEFORE external search
- Invoke `researcher` agent if Azure AI Search behavior questions arise (e.g., new vector-search features, semantic ranker config drift)

---

*This file should be kept updated throughout project lifecycle. Update "Decisions Made" + "Implementation Notes" as work progresses.*
