# spaarke-redis-cache-remediation-r2 — Implementation Plan

> **Last Updated**: 2026-06-26 (created by `/project-pipeline`)
> **Spec authority**: [`spec.md`](spec.md) (spec-locked 2026-06-26)
> **Design rationale**: [`design.md`](design.md)
> **Total estimated effort**: ~3-5 days (single combined PR per NFR-01)

---

## 1. Phase Breakdown

Three themes execute mostly in parallel; deploy + verify + wrap-up are serial gates.

### Phase 1 — Theme A: Cache observability hardening (tasks 001-006, ~2 days)

Six concrete fixes from R1 senior review. All BFF code changes (`bff-api` tag → **FULL rigor**).

| ID | Title | FR | Files | Hours |
|---|---|---|---|---|
| 001 | `cache.failures` Counter + `ClassifyException` + try/finally | FR-01 | `Infrastructure/Cache/MetricsDistributedCache.cs` | 2 |
| 002 | Meter consolidation — single canonical `CacheMetrics` static class | FR-02 | `Telemetry/CacheMetrics.cs` + grep-purge `TenantCache.cs:28-32` static fields + remove `CacheMetrics?` ctor injections | 3 |
| 003 | `resource` dimension via `cache.hits.by_resource` + `cache.misses.by_resource` Counters at TenantCache layer | FR-03 | `Infrastructure/Cache/TenantCache.cs` | 2 |
| 004 | Bicep alerts — new `infrastructure/bicep/alerts.bicep` (3 cache alerts) | FR-04 | `infrastructure/bicep/alerts.bicep` (NEW) + `scripts/Deploy-RedisCache.ps1` `-DeployAlerts` flag | 3 |
| 005 | Decorator regression integration test | FR-05 | `tests/integration/Sprk.Bff.Api.Tests.Integration/Cache/MetricsDistributedCacheRegistrationTests.cs` (NEW) | 2 |
| 006 | `UseAzureMonitor()` fails-open guard — throw in non-Development env | FR-06 | `src/server/api/Sprk.Bff.Api/Program.cs:21-30` | 1 |

### Phase 2 — Theme B: Redis key rotation automation (tasks 010-014, ~1.5 days)

Replaces DEF-001 manual rotation procedure with scheduled automation. No BFF code change.

| ID | Title | FR | Files | Hours |
|---|---|---|---|---|
| 010 | NEW `scripts/Rotate-RedisKey.ps1` — safe-window rotation algorithm | FR-07 | `scripts/Rotate-RedisKey.ps1` (NEW) | 4 |
| 011 | NEW `.github/workflows/redis-key-rotation.yml` — 3 staggered quarterly crons + `workflow_dispatch` | FR-08 | `.github/workflows/redis-key-rotation.yml` (NEW) | 2 |
| 012 | Document per-env OIDC SP isolation (operator provisions SPs) | FR-09 | `docs/guides/redis-cache-azure-setup.md` §6 SP section | 1 |
| 013 | Runbook §6 update — automated as primary; manual as emergency fallback | FR-10 | `docs/guides/redis-cache-azure-setup.md` §6 | 1 |
| 014 | Missed-rotation alert (>100 days) added to `alerts.bicep` (depends on task 004) | FR-11 | `infrastructure/bicep/alerts.bicep` (extends task 004) | 1 |

### Phase 3 — Theme C: R1 implementation gap closure (tasks 020-022, ~0.5 day)

Removes per-customer Redis from IaC. R1 deprecated this in `Provision-Customer.ps1` runtime but left Bicep template untouched.

| ID | Title | FR | Files | Hours |
|---|---|---|---|---|
| 020 | Remove Redis module call + params + var + outputs from `customer.bicep` | FR-12 | `infrastructure/bicep/customer.bicep` (lines ~62-67, ~99, ~181-195 + outputs) | 2 |
| 021 | Drop `redisSku` / `redisCapacity` from `customer-template.bicepparam` | FR-13 | `infrastructure/bicep/parameters/customer-template.bicepparam` | 1 |
| 022 | Clean up `SPAARKE-DEPLOYMENT-GUIDE.md` §4.6 strikethrough + footnote | FR-14 | `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` | 1 |

### Phase 4 — Deploy + verify + close-out (tasks 030-032, ~0.5 day)

Serial: deploy alerts → verify KQL + measure publish-size delta → close issues → wrap-up.

| ID | Title | Source | Hours |
|---|---|---|---|
| 030 | Deploy `alerts.bicep` to dev + KQL verification (NFR-07) + BFF publish-size delta measurement (NFR-04) | Spec NFR-07 + NFR-04 | 2 |
| 031 | Close GitHub Issues #462 / DEF-007 / DEF-008 / DEF-009 + flip R1 `defer-issues.md` entry | Spec G-13, G-14 | 1 |
| 032 | Project wrap-up — README → Complete, `notes/lessons-learned.md`, `/repo-cleanup` | task-create Step 3.7 mandatory | 2 |

**Total**: 17 tasks · ~30 hours estimated · ~3-5 days walltime including review.

---

## 2. Discovered Resources

### Applicable ADRs (5 from spec §Technical Constraints)

- **ADR-009** ([concise](../../.claude/adr/ADR-009-redis-caching.md), [full](../../docs/adr/ADR-009-caching-redis-first.md)) — R1 amended; R2 operationalizes the amendment (Bicep alerts replace markdown skeletons). **MUST NOT modify ADR-009** (NFR-08).
- **ADR-010** ([DI minimalism](../../.claude/adr/ADR-010-di-minimalism.md)) — Theme A.2 Meter consolidation eliminates an interface (`CacheMetrics` instance class) in favor of a static singleton. Aligns with "concretes over interfaces".
- **ADR-028** ([Spaarke Auth v2](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)) — Theme B's rotation script preserves KV reference pattern `@Microsoft.KeyVault(VaultName=...;SecretName=Redis-ConnectionString)`. No deviation.
- **ADR-029** ([BFF publish hygiene](../../.claude/adr/ADR-029-bff-publish-hygiene.md)) — Theme A publish-size delta ≤+0.5 MB per NFR-04. Verify per-task per ADR-029 protocol.
- **ADR-032** ([Null-Object kill-switch](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md)) — Theme A.2 Meter consolidation preserves symmetric DI for `IConnectionMultiplexer`. No changes to Null-Object path.

### Applicable Constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — §F.1 (Asymmetric-Registration), §F.2 (Fixture-Config-FIRST), §F.3 (Empirical-Reproduction-FIRST), §G (Hot-Path Declaration). All Phase 1 BFF tasks load this first.
- [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) — KV references, idempotent deploy, NFR-01 publish-size per-task verification.
- [`.claude/constraints/testing.md`](../../.claude/constraints/testing.md) — for task 005 integration test.

### Applicable Skills

- `task-execute` — every task uses this entry-point (CLAUDE.md §4 mandatory protocol)
- `code-review` + `adr-check` — FULL-rigor Step 9.5 gates for Phase 1
- `context-handoff` — proactive checkpointing
- `script-aware` — for tasks 010 (PowerShell) + 004 (Bicep deploy script extension)
- `azure-deploy` — for task 030 (deploy alerts.bicep)
- `bff-deploy` — for task 030 publish-size delta measurement
- `code-review` + `adr-check` — final gates in task 032
- `worktree-sync` + `merge-to-master` — wrap-up phase

### Existing Code Patterns to Follow (cite, don't recreate)

| Pattern | Source file | Used by R2 task |
|---|---|---|
| Decorator pattern with try/finally | `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/MetricsDistributedCache.cs` (R1 R7-S7 deliverable) | 001 (extends), 005 (verifies) |
| Env-safety + idempotency + post-deploy verification | `scripts/Deploy-RedisCache.ps1` (R1 task 025) | 010 (mirrors), 030 (uses) |
| Bicep modularization | `infrastructure/bicep/redis.bicep` + `redis-dev.bicepparam` | 004 (`alerts.bicep` follows shape) |
| OIDC auth in GitHub Actions | `.github/workflows/sdap-ci.yml` | 011 (reuses pattern) |
| Runbook structure | `docs/guides/redis-cache-azure-setup.md` (R1 deliverable; Theme B §6 update is in-place) | 012, 013 |
| KV secret upsert + version semantics | `Provision-Customer.ps1` (KV ops block) | 010 |

### Existing Scripts to Reuse

- `scripts/Deploy-RedisCache.ps1` — Theme A.4 extends with `-DeployAlerts` flag
- `scripts/Deploy-BffApi.ps1` — Theme A NFR-04 publish-size measurement
- `tests/manual/RedisValidationTests.ps1` — task 030 verification harness

---

## 3. Placement Justification (CLAUDE.md §10 + §11 binding)

Per CLAUDE.md §10 BFF Hygiene + §11 Component Justification, this project's NEW components must answer the three-question template:

### `MetricsDistributedCache` `cache.failures` Counter (FR-01, task 001)

1. **Existing**: `MetricsDistributedCache` decorator already exists (R1 R7-S7). Currently emits `cache.hits` / `cache.misses` / `cache.redis_call_duration_ms`. No failure counter exists.
2. **Extension**: YES — extending the existing decorator (not a new class).
3. **Cost-of-doing-nothing**: Redis outages are invisible. P95 latency alerts catch slowness but not exception storms. KQL `customMetrics | where name == 'cache.failures'` returns empty rows during a real `az redis force-reboot` (spec acceptance test).

### `cache.hits.by_resource` + `cache.misses.by_resource` Counters (FR-03, task 003)

1. **Existing**: Primary `cache.hits` / `cache.misses` at decorator layer; no `resource` dimension. R1 amendment to ADR-009 noted resource cardinality concern.
2. **Extension**: NO — different name → distinct Counter required to avoid double-counting. Same Meter instance via the canonical static class.
3. **Cost-of-doing-nothing**: Cannot answer "is the `session:` cache pulling its weight versus `embedding:` cache?" without resource attribution. Pub/Sub separation decision (deferred DEF-002) requires this data.

### `Rotate-RedisKey.ps1` (FR-07, task 010)

1. **Existing**: `Deploy-RedisCache.ps1` provisions Redis but does not rotate keys. No existing rotation script.
2. **Extension**: NO — distinct lifecycle (deploy = provision; rotate = ops). Adding rotation to `Deploy-RedisCache.ps1` would conflate concerns + violate single-responsibility.
3. **Cost-of-doing-nothing**: 90-day rotation policy is unenforced; manual procedure has slipped historically (root of DEF-001 / Issue #462). Without automation, the next compliance audit re-files DEF-001.

### `infrastructure/bicep/alerts.bicep` (FR-04 + FR-11, tasks 004, 014)

1. **Existing**: Alert rules documented in `redis-cache-azure-setup.md` §8 as markdown skeletons (R1 task 043 deliverable). No Bicep file.
2. **Extension**: NO — markdown is not deployable. Bicep is. Replace markdown skeletons with Bicep resources.
3. **Cost-of-doing-nothing**: Alerts not enforceable; only documented. Hit-rate <80% goes unnoticed until pulled into a dashboard manually.

### `.github/workflows/redis-key-rotation.yml` (FR-08, task 011)

1. **Existing**: `sdap-ci.yml` (root CI) + `Bff-CI.yml` exist. No rotation workflow.
2. **Extension**: NO — different trigger semantics (cron, not push/PR). Embedding in `sdap-ci.yml` would couple unrelated concerns.
3. **Cost-of-doing-nothing**: Script without workflow = back to manual invocation. Defeats DEF-001 closure.

---

## 4. PR Strategy (NFR-01)

ONE combined PR for Theme A + B + C. Per-theme split is acceptable only if PR review time becomes a blocker. PR opens after task 030 (deploy + verify) succeeds.

PR description includes:
- BFF publish-size baseline + R2 delta (≤+0.5 MB per NFR-04)
- KQL queries + screenshots/output for FR-01 + FR-03 acceptance
- Bicep `what-if` output for FR-12 acceptance
- Issue closures: closes #462, DEF-007, DEF-008, DEF-009
- Hot-path declaration cited (BFF=Y, ci-workflows=Y) per CLAUDE.md §10

---

## 5. References

- [`spec.md`](spec.md) — authoritative requirements
- [`design.md`](design.md) — design rationale + locked decisions + hot-path-declaration block
- [`CLAUDE.md`](CLAUDE.md) — AI context for task execution
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker with parallel groups
- [`notes/managed-redis-decision.md`](notes/managed-redis-decision.md) — DEF-005 Won't Fix rationale
- [`notes/managed-redis-ai-research.md`](notes/managed-redis-ai-research.md) — background research (informational)
- R1 [`projects/spaarke-redis-cache-remediation-r1/`](../spaarke-redis-cache-remediation-r1/) — predecessor reference; R7-S7 closure context
- R1 [`notes/r7-backlog.md`](../spaarke-redis-cache-remediation-r1/notes/r7-backlog.md) — source of items R2 closes
