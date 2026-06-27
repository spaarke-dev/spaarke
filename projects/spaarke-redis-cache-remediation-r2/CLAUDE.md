# Spaarke Redis Cache Remediation (R2) - AI Context

> **Purpose**: This file provides context for Claude Code when working on `spaarke-redis-cache-remediation-r2`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning complete (Phase 1 ready to start)
- **Last Updated**: 2026-06-26
- **Current Task**: Task 001 (auto-start per fully-autonomous mode)
- **Next Action**: Run `task-execute` for task 001 (`cache.failures` Counter)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (268 lines, 14 FR, 8 NFR) — **permanent reference**
- [`design.md`](design.md) — human design document with locked decisions + `<hot-path-declaration>` block
- [`README.md`](README.md) — project overview + 15 graduation criteria
- [`plan.md`](plan.md) — 4-phase WBS + discovered resources + placement justification (CLAUDE.md §10 / §11)
- [`current-task.md`](current-task.md) — **active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker with parallel groups

### Project Metadata
- **Project Name**: spaarke-redis-cache-remediation-r2
- **Type**: BFF observability hardening + IaC automation + IaC gap closure (mixed code + Bicep + script + workflow + docs)
- **Complexity**: **Low-Medium** — closure work, not new architecture; 17 tasks, ~30 hours estimated, 3-5 days walltime
- **Branch**: `work/spaarke-redis-cache-remediation-r2`
- **Predecessor**: `spaarke-redis-cache-remediation-r1` (R7-S7 closure shipped via PR #458 + #460)
- **Hot-path declaration** (per CLAUDE.md §10 / R1 task CICD-062): BFF=Y, SpaarkeAi=N, ci-workflows=Y, skill-directives=N, root-CLAUDE.md=N

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements (14 FRs), and acceptance criteria
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via `adr-aware` — see Resources section below for the 5 in-scope ADRs)
6. **For any BFF-touching task** (Phase 1 — Theme A, tasks 001-006): load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) BEFORE designing the change. Sections F.1 (Asymmetric-Registration), F.2 (Fixture-Config-FIRST), F.3 (Empirical-Reproduction-FIRST) are binding.
7. **For any Azure deploy task** (task 030 — deploy alerts): load [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md)
8. **For any test-modifying task** (task 005 integration test): ADR-038 + TEST-MODIFYING override row applies — FULL rigor unconditionally per CLAUDE.md §8.

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
- Example: Theme A tasks 002 + 004 + 006 (independent file targets) → three `task-execute` calls in one message

**Exception**: Task 002 (Meter consolidation) blocks tasks 003 + 005; must complete first.

See [`task-execute SKILL.md`](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

### Rigor Levels for R2 Tasks (per CLAUDE.md §8)

| Tasks | Rigor | Reason |
|---|---|---|
| 001, 002, 003, 006 | **FULL** | `bff-api` tag + `.cs` modification |
| 005 | **FULL** | TEST-MODIFYING override (ADR-038 + modifies `tests/**`) |
| 004 | **STANDARD** | Bicep file creation (no `.cs` change); FR-04 critical for alerts |
| 010, 011 | **STANDARD** | New script + workflow file |
| 012, 013, 022 | **MINIMAL** | Docs-only |
| 014, 020, 021 | **STANDARD** | Bicep / bicepparam edits |
| 030 | **FULL** | Deploy + verify + publish-size measurement (NFR-04 enforcement) |
| 031 | **MINIMAL** | Issue close + R1 defer-issues update |
| 032 | **FULL** | Wrap-up gates (code-review + adr-check + repo-cleanup) |

---

## Key Technical Constraints

**Binding rules** extracted from `spec.md` + applicable ADRs:

- **Single canonical Meter** — exactly one `Meter("Sprk.Bff.Api.Cache")` instance at runtime (FR-02). Verified by `MetricsDistributedCacheRegistrationTests` (FR-05) via `MeterListener` enumeration.
- **try/finally on every cache op** — `MetricsDistributedCache` MUST emit `cache.failures` from `try/finally` (FR-01); `outcome` ∈ {`timeout`, `canceled`, `connection`, `serialization`, `other`} via `ClassifyException` helper.
- **`resource` dimension at TenantCache layer only** — new `cache.hits.by_resource` + `cache.misses.by_resource` Counters carry the dimension; primary `cache.hits` / `cache.misses` unchanged (FR-03). Different metric names → no double-counting.
- **No cardinality cap** — `resource` dimension cardinality is code-driven (~10-20 expected; NFR-06). No soft limit. Re-evaluate only if observed >50.
- **Bicep-deployed alerts** — 3 minimum: hit-rate <80% / 15min, P95 >100ms / 5min, memory >80% / 15min (FR-04). Plus missed-rotation alert (FR-11). NOT markdown.
- **Safe-window key rotation** — rotate Secondary → update KV → restart BFF → verify `/healthz` → ONLY THEN rotate Primary (FR-07). On healthz failure: rollback via KV version history.
- **Per-env SP isolation** — compromised prod SP MUST NOT be able to rotate dev (FR-09). 3 distinct `AZURE_CLIENT_ID_*` secrets scoped to respective KV + Redis + App Service.
- **Quarterly staggered cron** — dev 1st, staging 8th, prod 15th of every 3rd month (Q1/Q2/Q3/Q4 of calendar year). Dev catches breakage before prod (FR-08).
- **`UseAzureMonitor()` fails-open guard** — throw in non-Development envs when `APPLICATIONINSIGHTS_CONNECTION_STRING` is missing (FR-06). Mirror `CacheModule` 4-branch fail-fast pattern (R1 FR-03).
- **`customer.bicep` no per-customer Redis** — delete module call + params + var + outputs (FR-12). `what-if` verifies NO Redis in plan for fresh customer.
- **One combined PR** — Theme A + B + C ship together (NFR-01). Per-theme split only on escalation.
- **Publish-size ≤+0.5 MB delta** (NFR-04) — measure at task 030 via `Deploy-BffApi.ps1` + verify against `dotnet publish -c Release`. Cumulative ceiling per CLAUDE.md §10: ≤60 MB compressed.
- **Test baseline maintained** — ≥7885 pass / 2 pre-existing fail / 135 skip (NFR-02). No new test failures from R2 changes.
- **MUST NOT modify ADR-009** (NFR-08) — R2 operationalizes R1's amendment only.

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

- **2026-06-26** — Hot-path declaration block appended to design.md (BFF=Y, SpaarkeAi=N, ci-workflows=Y, skill-directives=N, root-CLAUDE.md=N) per CLAUDE.md §10 (added 2026-06-26 by CICD-062). Cleared the HARD WARNING. — Claude Code (project-pipeline run)
- **2026-06-26** — Phase 4 (tasks 030-032) is sequential after Phases 1-3 complete. Deploy → verify → close-issues → wrap-up. No parallel safety in Phase 4. — Claude Code
- **2026-06-26** — Pipeline auto-starts task 001 per user "fully autonomous" decision. Subsequent tasks dispatched per parallel groups in `TASK-INDEX.md`. — Claude Code
- **2026-06-26** — Theme A tasks 002 (Meter consolidation) is a hard prerequisite for tasks 003 + 005. Cannot parallelize until 002 lands. — Claude Code
- **2026-06-26** — Theme B is fully parallel-safe with Theme C (Phase 2 ‖ Phase 3) — different file domains (scripts/workflows/runbook vs bicep/bicepparam/guide). — Claude Code

---

## Implementation Notes

<!-- Gotchas, workarounds, or important learnings during implementation -->

- **R1 R7-S7 closure context**: `MetricsDistributedCache` decorator + OTel exporter wiring are R1 deliverables; R2 extends, not replaces. Read `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/MetricsDistributedCache.cs` and `CacheModule.DecorateDistributedCacheWithMetrics` (R1 task 041 deliverable) before task 001.
- **CacheMetrics consumer audit (FR-02)**: Spec assumes ~3-5 consumers (`EmbeddingCache`, `GraphTokenCache`, + 1-3 others). Grep-audit `grep -r "CacheMetrics" src/server/api/` during task 002 catches all of them — DO NOT leave the instance class half-removed.
- **Bicep alerts.bicep template**: Mirror the existing `redis.bicep` modularization pattern (separate `.bicep` + `.bicepparam` per env if env-specific; alert thresholds are likely env-uniform → single `alerts.bicep` without param file).
- **OIDC SP provisioning out of scope** (FR-09): R2 documents the steps but does not provision SPs (Azure-side IAM work, operator responsibility per spec §Dependencies §External Dependencies).
- **Customer.bicep Redis removal verification**: Task 020 MUST run `az deployment group what-if` BEFORE the delete commit. If a live customer is still on the Redis module path, escalate before deleting (spec FR-12 acceptance + §Dependencies bullet 3).
- **Staging + prod resource names TBD** (spec Assumption §1): operator confirms before task 030 deploy. For dev: `spaarke-bff-redis-dev`, `rg-spaarke-dev`, KV `spaarke-spekvcert` (per R1 task 030 baseline).

---

## Resources

### Applicable ADRs

- [ADR-009 Redis-First Caching (concise)](../../.claude/adr/ADR-009-redis-caching.md) — R1 amended; R2 operationalizes (MUST NOT modify)
- [ADR-009 Caching: Redis First (full)](../../docs/adr/ADR-009-caching-redis-first.md) — same: MUST NOT modify
- [ADR-010 DI Minimalism](../../.claude/adr/ADR-010-di-minimalism.md) — Theme A.2 Meter consolidation: concretes over interfaces
- [ADR-028 Spaarke Auth v2](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Theme B preserves KV reference pattern
- [ADR-029 BFF Publish Hygiene](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — Theme A publish-size delta ≤+0.5 MB (NFR-04)
- [ADR-032 BFF Null-Object Kill-Switch](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) — Theme A.2 preserves symmetric `IConnectionMultiplexer` registration
- [ADR-038 Testing Strategy](../../docs/adr/ADR-038-testing-strategy.md) — task 005 integration test: 6 KEEP path categories; integration-heavy pyramid

### Applicable Constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — §F.1, F.2, F.3 + test-update obligation + §G hot-path declaration
- [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) — KV references, idempotent deploy, publish-size per-task
- [`.claude/constraints/testing.md`](../../.claude/constraints/testing.md) — for task 005

### Related Projects

- **`spaarke-redis-cache-remediation-r1`** (PREDECESSOR) — R7-S7 closure shipped. R2 is the closure-of-closure for senior-review items DEF-001 + DEF-007/008/009. See R1 `notes/r7-backlog.md` §S5/S6/S7 for source of R2 work.
- **`spaarke-ai-azure-setup-dev-r1`** (PARALLEL) — sister project; was unblocked by R1's Phase 3. R2 does not touch sister project's surface.
- **`ci-cd-unit-test-remediation-r1`** (HOT-PATH NEIGHBOR) — owns CI workflow changes (`.github/workflows/**`). R2's `redis-key-rotation.yml` is NEW (no conflict on existing workflows).

### External Documentation

- [Azure Cache for Redis key rotation docs](https://learn.microsoft.com/azure/azure-cache-for-redis/cache-configure#access-keys) — `az redis regenerate-key` semantics
- [Azure Monitor metric alerts](https://learn.microsoft.com/azure/azure-monitor/alerts/alerts-metric) — Bicep alert resource shape (`Microsoft.Insights/metricAlerts`)
- [GitHub Actions OIDC for Azure](https://docs.github.com/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-azure) — federated identity credential setup
- [OpenTelemetry .NET Metrics](https://opentelemetry.io/docs/languages/net/instrumentation/#metrics) — `Meter`, `Counter`, `MeterListener` reference

### Reuse References (cite, don't recreate)

- `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/MetricsDistributedCache.cs` — R1 R7-S7 decorator; task 001 extends, task 005 verifies
- `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/TenantCache.cs` — R1 task 006 deliverable; task 002 removes static Meter fields, task 003 adds resource Counters
- `src/server/api/Sprk.Bff.Api/Telemetry/CacheMetrics.cs` — R1 task 041 deliverable; task 002 promotes to canonical static class
- `scripts/Deploy-RedisCache.ps1` — R1 task 025 deliverable; pattern for task 010 + extended by task 004 (`-DeployAlerts` flag)
- `infrastructure/bicep/redis.bicep` + `redis-dev.bicepparam` — R1 task 020-024 deliverables; pattern for task 004 (`alerts.bicep`)
- `.github/workflows/sdap-ci.yml` — OIDC auth pattern; task 011 reuses
- `docs/guides/redis-cache-azure-setup.md` — R1 deliverable; task 013 updates §6 in-place
- `tests/manual/RedisValidationTests.ps1` — R1 task 026 deliverable; task 030 invokes for post-deploy verification
- R1 `notes/r7-backlog.md` §S5/S6/S7 — source items R2 closes

---

*This file should be kept updated throughout project lifecycle. Add entries to "Decisions Made" and "Implementation Notes" as the project progresses.*
