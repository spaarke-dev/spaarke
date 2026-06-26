# Spaarke Redis Cache Remediation (R1) - AI Context

> **Purpose**: This file provides context for Claude Code when working on `spaarke-redis-cache-remediation-r1`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning (Phase 1 ready to start)
- **Last Updated**: 2026-06-25
- **Current Task**: Not started
- **Next Action**: Run `/task-create` to decompose plan into POML task files, then `/task-execute` for task 001 (inventory)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (379 lines, 22 FR, 13 NFR) — **permanent reference**
- [`design.md`](design.md) — human design document (v0.3)
- [`README.md`](README.md) — project overview and graduation criteria (16 criteria)
- [`plan.md`](plan.md) — implementation plan + 5-phase WBS + discovered resources + Placement Justification (CLAUDE.md §10 / §11)
- [`current-task.md`](current-task.md) — **active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker with parallel groups (created by task-create)

### Project Metadata
- **Project Name**: spaarke-redis-cache-remediation-r1
- **Type**: BFF infrastructure / caching / IaC / docs (mixed code + infra + ADR amendment)
- **Complexity**: **High** — atomic migration of ~199 cache call sites, dev environment cutover, ADR-009 lockstep amendment, sister-project prerequisite gating
- **Branch**: `work/spaarke-redis-cache-remediation-r1`
- **Sister project (DOWNSTREAM)**: `spaarke-ai-azure-setup-dev-r1` — its Phase 3 is GATED on this project's Phase 3 cutover (NFR-11 / sister NFR-13)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements (22 FRs), and acceptance criteria
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via `adr-aware` — see Resources section below for the 6 in-scope ADRs)
6. **For any BFF-touching task** (Phase 1, Phase 3 cutover, Phase 4 metrics): load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) BEFORE designing the change. Sections F.1 (Asymmetric-Registration), F.2 (Fixture-Config-FIRST), F.3 (Empirical-Reproduction-FIRST) are binding.
7. **For any Azure deploy task** (Phase 2, Phase 3): load [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke `task-execute`:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via `task-execute` |
| "continue" | Execute next pending task (check `TASK-INDEX.md` for next 🔲) |
| "continue with task X" | Execute task X via `task-execute` |
| "next task" | Execute next pending task via `task-execute` |
| "keep going" | Execute next pending task via `task-execute` |
| "resume task X" | Execute task X via `task-execute` |
| "pick up where we left off" | Load `current-task.md`, invoke `task-execute` |

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Why This Matters

`task-execute` ensures: ADRs loaded, `current-task.md` tracked, proactive checkpointing every 3 steps, quality gates (`code-review` + `adr-check`) at Step 9.5, recovery after compaction. Bypassing it = missing ADR constraints, lost progress, skipped gates.

### Parallel Task Execution

For independent tasks in the same group (per `TASK-INDEX.md`):
- Send ONE message with MULTIPLE `Skill` tool invocations
- Each invocation calls `task-execute` with a different task file
- Example: Tasks 022, 023, 024 (the three new `.bicepparam` files) → three `task-execute` calls in one message
- **Exception**: Tasks 010–017 (atomic call-site migration) are sequential despite touching distinct files — atomicity (NFR-07) requires linear ordering for single-PR commit
- **Exception**: Task 052 touches `.claude/adr/ADR-009-redis-caching.md` — main-session-only per CLAUDE.md §3 sub-agent write boundary

See [`task-execute SKILL.md`](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

### 🚨 MUST: Multi-File Work Decomposition

For tasks modifying 4+ files: decompose into dependency graph, delegate to parallel subagents where safe (distinct modules, no shared interfaces), serialize when files have tight coupling. For this project specifically:

- **Phase 1 task 001** (inventory) — sequential, foundational
- **Phase 1 tasks 010–017** (call-site migration) — **sequential** despite distinct files (NFR-07 single-PR atomicity)
- **Phase 2 tasks 022–024** (three `.bicepparam` files) — parallel-safe
- **Phase 5 tasks 050, 051** (two distinct doc files) — parallel-safe
- **Phase 5 tasks 052, 053** (ADR concise + full) — sequential despite distinct files (lockstep PR requirement)

---

## Key Technical Constraints

**Binding rules** extracted from `spec.md` + applicable ADRs:

- **Production-like dev** — `AbortOnConnectFail = true`; fail-fast at startup; no silent in-memory fallback in deployed envs (FR-01..03, ADR-009 amendment)
- **Symmetric DI registration of `IConnectionMultiplexer`** — real or Null-Object both register the interface; selection by config (FR-04, ADR-032)
- **Tenant-scoped cache keys MANDATORY** — every cache call carries `tenant:{tenantId}:` prefix; key format `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}` (FR-05)
- **Atomic call-site migration** — ALL direct `IDistributedCache.GetAsync/SetAsync/RemoveAsync` calls in `Sprk.Bff.Api/` migrated to `ITenantCache` in a single PR; partial migration prohibited (FR-06, NFR-07)
- **`InstanceName` MUST be `spaarke:`** — drops deprecated `sdap` brand; verified via grep returning zero `sdap:` references (FR-07, Success Criterion #10)
- **Key Vault references MANDATORY** — Redis connection string MUST be `@Microsoft.KeyVault(VaultName=...;SecretName=...)`; no plain text in App Settings (FR-14, ADR-028)
- **Canonical resource naming** — `spaarke-bff-redis-{env}` (top-level env-suffix); sub-resources env-agnostic (cache keys, KV secret names) (NFR-03, NFR-10)
- **MUST NOT touch prod or demo environments** — `Deploy-RedisCache.ps1` rejects `prod`/`demo` without explicit `-Force` (NFR-05)
- **MUST NOT introduce new BFF endpoints, services beyond the cache wrapper, DI registrations beyond `CacheModule` changes, or new packages** (spec MUST NOT list)
- **MUST NOT cache authorization decisions** (ADR-009 preserved)
- **MUST NOT add L1 in-process cache layer on top of Redis** without ADR amendment + profiling proof (ADR-009 preserved)
- **MUST NOT recreate per-customer Redis in `Provision-Customer.ps1`** (deprecated per Q-E Architecture 1)
- **Publish-size delta ≤+1 MB compressed** per BFF-touching task; measure absolute + diff (NFR-04, ADR-029, CLAUDE.md §10)
- **ADR-009 lockstep amendment** — BOTH `.claude/adr/ADR-009-redis-caching.md` AND `docs/adr/ADR-009-caching-redis-first.md` updated in same PR with matching Last Updated dates (FR-20)
- **Extend, don't duplicate** — `infrastructure/bicep/modules/redis.bicep` extended (not duplicated); `tests/manual/RedisValidationTests.ps1` extended (not rewritten) (Spec MUST rules)

**Reality vs. spec — material findings** (already captured in `plan.md` §2 Discovered Resources):
- Cache call sites: spec says 117; reality is ~199 across 62 files
- Dev `appsettings.json` has `sdap-dev:` (not `sdap:` as spec assumes)
- `CacheModuleTests.cs` does NOT exist (creating new)
- `redis.bicep` default SKU is Premium; dev override to Basic C0 via `redis-dev.bicepparam`
- `Provision-Customer.ps1` Redis block is at lines 422–492 (one line longer than spec's 422–487)

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

- **2026-06-25** — `ITenantCache` wrapper justified per CLAUDE.md §11 three-question template (overlap: none; extension: not viable on static helpers; cost-of-doing-nothing: read-old/write-new bug class + no central metrics seam). Recorded in `plan.md` §2 "Placement Justification". — Claude Code (project-pipeline run)
- **2026-06-25** — Tasks 010–017 (call-site migration) are sequential despite touching distinct files, because NFR-07 atomicity requires single-PR commit. — Claude Code
- **2026-06-25** — Pipeline pauses at Step 5 (no auto-start of task 001) because Phase 3 cutover is irreversible and task 001's inventory output should inform downstream task sizing before user proceeds. — Claude Code
- **2026-06-25** — PR #253 (Redis NuGet bump) flagged as overlapping; mitigation = pin Phase 1 to current version range; rebase if #253 lands first. — Claude Code

---

## Implementation Notes

<!-- Gotchas, workarounds, or important learnings during implementation -->

- **PR overlap (PR #253)**: `Microsoft.Extensions.Caching.StackExchangeRedis` NuGet bump pending. If it merges before Phase 1 PR, rebase before final commit; if Phase 1 lands first, #253 will need a trivial rebase.
- **Dev KV name uncertainty**: Spec Assumption §1 names `spaarke-spekvcert` but flags alternate (`sprkspaarkedev-aif-kv`) possibility. Task 030 captures the actual KV in `notes/dev-cutover-baseline.md` BEFORE task 032 attempts secret upsert.
- **System-level cache exceptions**: Some keys legitimately need to be non-tenant-scoped (feature flags, system config). NFR-08 requires JSON-comment justification per call site; task 001 inventory flags candidates; if count >20 at FR-06 migration time, escalate for architecture review.
- **Pub/Sub no-op in dev**: Null-Object `IConnectionMultiplexer` Subscribe never delivers — documented as known multi-instance limitation in operational guide; dev single-instance only (Q-B).
- **Sub-agent write boundary**: Task 052 modifies `.claude/adr/ADR-009-redis-caching.md` — must run in main session, not via parallel sub-agent. `task-create` should flag this as `parallel-safe: false`.

---

## Resources

### Applicable ADRs

- [ADR-009 Redis-First Caching (concise)](../../.claude/adr/ADR-009-redis-caching.md) — **being amended** by FR-20
- [ADR-009 Caching: Redis First (full)](../../docs/adr/ADR-009-caching-redis-first.md) — **being amended** by FR-20 in lockstep
- [ADR-010 DI Minimalism](../../.claude/adr/ADR-010-di-minimalism.md) — `ITenantCache` new-interface justification
- [ADR-013 AI services bounded concurrency](../../.claude/adr/) — preserve `SemaphoreSlim` patterns in `GraphTokenCache` / `EmbeddingCache` migration
- [ADR-028 Spaarke Auth v2](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — KV reference syntax, Managed Identity
- [ADR-029 BFF Publish Hygiene](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — publish-size delta ≤+1 MB
- [ADR-032 BFF Null-Object Kill-Switch](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) — symmetric `IConnectionMultiplexer` registration

### Applicable Constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — §F.1, F.2, F.3 + test-update obligation
- [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) — KV references, idempotent deploy, publish-size per-task

### Related Projects

- **`spaarke-ai-azure-setup-dev-r1`** (DOWNSTREAM) — Phase 3 GATED on this project's Phase 3 cutover (NFR-11). Sister project's NFR-13 codifies the dependency. Gate signal = Success Criterion #1.

### External Documentation

- [Azure Cache for Redis SKU comparison](https://learn.microsoft.com/azure/azure-cache-for-redis/cache-overview) — Basic vs Standard vs Premium feature matrix (referenced in FR-20 SKU table addition)
- [StackExchange.Redis docs](https://stackexchange.github.io/StackExchange.Redis/) — `IConnectionMultiplexer` and `AbortOnConnectFail` semantics
- [Azure App Service Key Vault references syntax](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references) — `@Microsoft.KeyVault(...)` syntax

### Reuse References (cite, don't recreate)

- `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/NullMembershipCacheInvalidator.cs` — Null-Object template
- `infrastructure/ai-search/deploy-session-files-index.ps1` — post-deploy invariant verification pattern (for `Deploy-RedisCache.ps1`)
- `spaarke-bff-prod` App Settings — KV reference pattern to mirror in dev
- `scripts/Provision-Customer.ps1:422-492` — source for `Deploy-RedisCache.ps1` extraction
- `tests/manual/RedisValidationTests.ps1` — extend with tenant-isolation + fail-fast checks

---

*This file should be kept updated throughout project lifecycle. Add entries to "Decisions Made" and "Implementation Notes" as the project progresses.*
