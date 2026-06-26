# spaarke-redis-cache-remediation-r2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-26
> **Source**: [`design.md`](design.md) (spec-locked 2026-06-26 — all 4 owner decisions resolved)
> **Predecessor**: [`spaarke-redis-cache-remediation-r1`](../spaarke-redis-cache-remediation-r1/) (R7-S7 closure shipped)
> **Background research** (informational): [`notes/managed-redis-ai-research.md`](notes/managed-redis-ai-research.md)
> **Companion decision record**: [`notes/managed-redis-decision.md`](notes/managed-redis-decision.md)

---

## Executive Summary

R2 is a **closure project** — finishes the R1 work properly without re-architecting. Three coherent themes, shipped in one combined PR over 3-5 days: cache observability hardening (6 concrete fixes from R1 senior review), Redis key rotation automation (replaces DEF-001 Entra ID without paying +$485/mo for ACR Premium), and R1 implementation gap closure (`customer.bicep` still provisions per-customer Redis even though R1 deprecated it). Closes GitHub Issues [#462](https://github.com/spaarke-dev/spaarke/issues/462) (DEF-001 Entra ID auth) and DEF-007/DEF-008/DEF-009 (filed by this project).

---

## Scope

### In Scope

**Theme A — Cache observability hardening (1 PR section)**:
- `cache.failures` Counter with `outcome` dimension; `try/finally` on every `MetricsDistributedCache` op
- Meter consolidation — collapse the two `Meter("Sprk.Bff.Api.Cache")` instances (`TenantCache` static fields + `Telemetry/CacheMetrics.cs`) into one canonical static class
- `resource` dimension restoration at `TenantCache` wrapper layer via secondary `cache.hits.by_resource` / `cache.misses.by_resource` Counters
- Bicep-deployed alerts (3 minimum): hit-rate <80% / 15min, P95 >100ms / 5min, memory >80% of SKU / 15min
- Decorator regression integration test (asserts `IDistributedCache` resolves to `MetricsDistributedCache` after full DI graph builds)
- `UseAzureMonitor()` fails-open guard — throw in non-Development envs when `APPLICATIONINSIGHTS_CONNECTION_STRING` is missing

**Theme B — Redis key rotation automation**:
- `scripts/Rotate-RedisKey.ps1` — idempotent PowerShell script with safe-window semantics (rotate secondary → update KV → restart BFF → verify `/healthz` → only then rotate primary)
- `.github/workflows/redis-key-rotation.yml` — 3 staggered scheduled cron jobs (dev 1st, staging 8th, prod 15th of every 3rd month) + `workflow_dispatch` for manual testing
- Per-environment service principal isolation (compromised prod SP cannot rotate dev)
- `docs/guides/redis-cache-azure-setup.md` §6 — runbook update; manual fallback procedure retained for emergency rotation
- Alert if `RedisKeyRotation` custom event missing for >100 days for any environment

**Theme C — R1 implementation gap closure**:
- Remove `customer.bicep:181` `module redis 'modules/redis.bicep'` call + associated `redisSku` / `redisCapacity` params + `redisName` variable + any `redis.outputs.*` references
- Update `infrastructure/bicep/parameters/customer-template.bicepparam` — drop `redisSku` / `redisCapacity` params
- Clean up `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` §4.6 (the strikethrough row + footnote can be removed once the gap is closed)

### Out of Scope (with rationale)

- **Managed Redis migration** — decision: NO (see [`notes/managed-redis-decision.md`](notes/managed-redis-decision.md)). Spaarke is below the scale where its differentiating features pay off.
- **DEF-002 Pub/Sub separation in prod** — no measured contention. Theme A's `cache.failures` + `resource`-tagged hits give us the data to know if/when it becomes real. Re-evaluate after 30 days of prod data.
- **DEF-003 Multi-region Redis** — Spaarke BFF is single-region. No DR commitment requires geo-rep.
- **DEF-004 Plain-text secret remediation (non-Redis)** — cross-cutting App Settings hygiene; not cache.
- **DEF-006 Rename App Insights `spe-insights-dev-67e2xz`** — not Redis. Fold into sister project `spaarke-ai-azure-setup-dev-r1` continuation.
- **N-2 Hot-path performance baseline** — theoretical concern; no observed regression.
- **N-3 ConnectionMultiplexerFactory hot-rotation lifecycle** — theoretical; no observed failure.
- **M-1 dead `Microsoft.ApplicationInsights.AspNetCore` package** — non-cache cleanup.
- **M-4 `CacheModule` bootstrap logger leak** — cosmetic.

### Affected Areas

| Path | Description |
|---|---|
| `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/MetricsDistributedCache.cs` | Theme A.1 try/finally + `cache.failures` Counter |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/TenantCache.cs` | Theme A.2 remove static Meter/Counter fields; A.3 emit secondary `*_by_resource` Counters |
| `src/server/api/Sprk.Bff.Api/Telemetry/CacheMetrics.cs` | Theme A.2 promote to canonical static class; drop instance class |
| `src/server/api/Sprk.Bff.Api/Services/Ai/EmbeddingCache.cs` | Theme A.2 remove `CacheMetrics?` injection (now static) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/GraphTokenCache.cs` | Theme A.2 remove `CacheMetrics?` injection (now static) |
| Other `CacheMetrics` consumers | Theme A.2 — grep + remove (~3-5 files) |
| `src/server/api/Sprk.Bff.Api/Program.cs` | Theme A.6 throw in non-Development when AI conn string missing |
| `tests/integration/Sprk.Bff.Api.Tests.Integration/Cache/MetricsDistributedCacheRegistrationTests.cs` | Theme A.5 NEW integration test (decorator regression net) |
| `infrastructure/bicep/alerts.bicep` | Theme A.4 NEW Bicep file with 3 alert rules + Theme B.4 missed-rotation alert |
| `infrastructure/bicep/customer.bicep` | Theme C remove lines ~181-195 (Redis module call) + ~62-67 (Redis params) + ~99 (redisName var) + any outputs |
| `infrastructure/bicep/parameters/customer-template.bicepparam` | Theme C drop redisSku / redisCapacity params |
| `scripts/Rotate-RedisKey.ps1` | Theme B NEW key rotation script |
| `scripts/Deploy-RedisCache.ps1` | Theme A.4 add `-DeployAlerts` flag invoking new Bicep |
| `.github/workflows/redis-key-rotation.yml` | Theme B NEW scheduled workflow |
| `docs/guides/redis-cache-azure-setup.md` | Theme B §6 runbook update — replace manual procedure with automated workflow + retain manual emergency fallback |
| `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` | Theme C §4.6 strikethrough + footnote cleanup |

---

## Requirements

### Functional Requirements

#### Theme A — Cache observability hardening

1. **FR-01**: `MetricsDistributedCache` MUST track failures. Every op method wraps the inner-cache call in `try/finally`; on exception sets `outcome` to one of `timeout` / `canceled` / `connection` / `serialization` / `other` via a `ClassifyException` helper; records the new `cache.failures` Counter with `outcome` + `op` dimensions before rethrowing.
   - **Acceptance**: KQL `customMetrics | where name == 'cache.failures' | summarize sum(value) by tostring(customDimensions.outcome)` returns ≥1 row after `az redis force-reboot` against dev.

2. **FR-02**: Only ONE `Meter("Sprk.Bff.Api.Cache")` instance MUST exist at runtime. Today `TenantCache.cs:28-32` and `Telemetry/CacheMetrics.cs:25-40` independently create instances claiming the same name. Collapse to a single canonical `Sprk.Bff.Api.Telemetry.CacheMetrics` static class owning the Meter + instruments. `MetricsDistributedCache` and any other consumer emits via the static class.
   - **Acceptance**: integration test asserts the count of `Meter` instances with name `"Sprk.Bff.Api.Cache"` is exactly 1 at runtime via `MeterListener` enumeration. Production `dotnet test` baseline maintained.

3. **FR-03**: `TenantCache` MUST emit secondary metrics with `resource` dimension at the wrapper layer. New Counters `cache.hits.by_resource` + `cache.misses.by_resource` carry the `resource` dimension (bounded, code-driven natural range ~10-20 values). Primary `cache.hits` / `cache.misses` from the decorator layer remain unchanged. Different metric names → no double-counting risk.
   - **Acceptance**: KQL `customMetrics | where name == 'cache.hits.by_resource' | summarize sum(value) by tostring(customDimensions.resource)` returns rows with bounded resource values (e.g., `session`, `document-analysis`, `embedding`, `graph-token`, `membership`).

4. **FR-04**: Three Azure Monitor metric alerts MUST be Bicep-deployed (not markdown-only). New `infrastructure/bicep/alerts.bicep` with:
   - Hit-rate <80% / 15min → on-call email action group
   - P95 latency >100ms / 5min → on-call email action group
   - Memory >80% of SKU / 15min → on-call email action group
   - **Acceptance**: `az monitor metrics alert list -g rg-spaarke-dev` shows 3 rules referencing the cache metrics. Manually trigger one (e.g., scale dev Redis to evict all keys) → corresponding alert fires within window.

5. **FR-05**: A new integration test MUST verify that after the full DI graph builds, `IDistributedCache` resolves to `MetricsDistributedCache` wrapping the expected inner type (e.g., the StackExchangeRedis `RedisCache` in deployed envs, `MemoryDistributedCache` in dev-fallback mode). Test runs in CI as part of the existing test suite.
   - **Acceptance**: new test class `MetricsDistributedCacheRegistrationTests` passes both branches; runs as part of `dotnet test`.

6. **FR-06**: `Program.cs:21-30` `UseAzureMonitor()` guard MUST throw in non-Development environments when `APPLICATIONINSIGHTS_CONNECTION_STRING` is missing or empty. Today silently skips. Throw mirrors the existing `CacheModule` 4-branch pattern (FR-03 from R1).
   - **Acceptance**: BFF startup with `ASPNETCORE_ENVIRONMENT=Production` + missing `APPLICATIONINSIGHTS_CONNECTION_STRING` → throws `InvalidOperationException` with actionable message. Development env continues to start with `null` conn string.

#### Theme B — Redis key rotation automation

7. **FR-07**: A new `scripts/Rotate-RedisKey.ps1` MUST be idempotent and parameterized by environment (`dev`, `staging`, `prod`). Algorithm (designed for safe partial failure):
   - Verify Redis + KV exist; verify operator has required permissions
   - Read current connection string from KV (CONN_OLD)
   - Call `az redis regenerate-key --key-type Secondary` (primary still works during transition — safe window)
   - Construct new connection string using the SECONDARY key (CONN_NEW)
   - Update KV secret `Redis-ConnectionString` with CONN_NEW (new secret version)
   - Restart BFF App Service (`az webapp restart`)
   - Poll `/healthz` for HTTP 200 with 120s timeout
   - On healthz success: call `az redis regenerate-key --key-type Primary` (eliminates the now-unused old primary)
   - On healthz failure: rollback — update KV secret back to CONN_OLD via KV version history, restart BFF, exit non-zero
   - Log every step to App Insights as a `RedisKeyRotation` custom event with `outcome`, `environment`, `duration_ms`
   - **Acceptance**: `-WhatIf` plans for all 3 envs (`dev`, `staging`, `prod`) without making changes; dry-run on dev produces 2 KV secret versions + BFF restart + `/healthz` 200 + `customEvent.RedisKeyRotation` with `outcome=success`.

8. **FR-08**: A new GitHub Actions workflow `.github/workflows/redis-key-rotation.yml` MUST trigger key rotation on quarterly staggered cron schedules with `workflow_dispatch` for manual testing:
   - Dev: `0 6 1 */3 *` (06:00 UTC, 1st of Jan/Apr/Jul/Oct)
   - Staging: `0 6 8 */3 *` (06:00 UTC, 8th of Jan/Apr/Jul/Oct)
   - Prod: `0 6 15 */3 *` (06:00 UTC, 15th of Jan/Apr/Jul/Oct)
   - `workflow_dispatch` accepts `environment` choice input
   - **Acceptance**: `gh workflow run redis-key-rotation.yml -f environment=dev` from CLI triggers a successful run; cron schedules visible in `gh workflow view redis-key-rotation.yml`.

9. **FR-09**: Per-environment OIDC service-principal isolation MUST be enforced. Three distinct GitHub Environment secrets (`AZURE_CLIENT_ID_DEV`, `AZURE_CLIENT_ID_STAGING`, `AZURE_CLIENT_ID_PROD`) each scoped to the corresponding environment's KV + Redis + App Service only. A compromised prod SP must not be able to rotate dev (and vice versa).
   - **Acceptance**: each SP has Key Vault Secrets Officer role on ONLY its own KV; `redis-cache-contributor` (or equivalent) role on ONLY its own Redis. Verified via `az role assignment list --assignee {sp-id} --all`.

10. **FR-10**: `docs/guides/redis-cache-azure-setup.md` §6 (Secret Rotation Procedure) MUST be updated to describe the automated workflow as the primary path. The existing 5-step manual procedure is retained as the **emergency fallback** (suspected key compromise scenario). Include the KQL verification query for the last successful rotation per environment.

11. **FR-11**: A new "rotation hasn't fired in 100 days" alert MUST be in `infrastructure/bicep/alerts.bicep` (alongside the Theme A.4 alerts). Queries `customEvents | where name == 'RedisKeyRotation' AND outcome == 'success'` over 100 days; if count = 0 for any env, alert on-call.
   - **Acceptance**: alert visible via `az monitor scheduled-query list -g rg-spaarke-dev`; fake an empty result via App Insights query simulator → alert fires.

#### Theme C — R1 implementation gap closure

12. **FR-12**: `infrastructure/bicep/customer.bicep` MUST NOT provision per-customer Redis. Delete the `redis` module call (lines ~181-195), the `redisSku` / `redisCapacity` parameters (lines ~62-67), the `redisName` variable (line ~99), and any `output redis*` lines. `Provision-Customer.ps1` already does NOT call the Redis module path post-R1 — this is closing the IaC gap, not changing runtime behavior.
   - **Acceptance**: `az deployment group what-if` on `customer.bicep` for a fresh customer ID shows NO Redis resource in the plan. Existing customer environments are unaffected (Bicep template governs new deployments only).

13. **FR-13**: `infrastructure/bicep/parameters/customer-template.bicepparam` MUST drop the `redisSku` and `redisCapacity` params consistent with FR-12. Any test fixture using `customer.bicep` is grep-audited and updated accordingly.

14. **FR-14**: `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` §4.6 strikethrough row + footnote referencing `customer.bicep:181` MUST be cleaned up (replaced with a clean table excluding Redis from per-customer resources).

### Non-Functional Requirements

- **NFR-01**: PR atomicity — all three themes ship in ONE combined PR. Per-theme split is acceptable only if PR review time becomes a blocker (escalation only).
- **NFR-02**: Test baseline — `dotnet test` must produce ≥7885 pass / 2 pre-existing flaky-fail / 135 skip (matches R1 final baseline). No new test failures introduced.
- **NFR-03**: Build clean — `dotnet build src/server/api/Sprk.Bff.Api/` produces 0 errors. New warnings limited to ≤2 (Roslyn analyzer suggestions on new code).
- **NFR-04**: Publish-size delta — Theme A net delta MUST be ≤+0.5 MB (compressed). Verify via `Deploy-BffApi.ps1` package size. Theme B + C produce 0 BFF deploy delta (Bicep + script only).
- **NFR-05**: Env safety on rotation script — `Rotate-RedisKey.ps1` MUST reject `prod` or `staging` env without explicit `-Force` parameter (mirrors R1's `Deploy-RedisCache.ps1` pattern).
- **NFR-06**: Cardinality bound — `resource` dimension cardinality must remain ≤30 distinct values in production. Code-driven natural bounding (no soft cap added). Re-evaluate only if observed > 50.
- **NFR-07**: Deploy verification — after deploy, KQL `customMetrics | where name == 'cache.failures' | count` AND `customMetrics | where name == 'cache.hits.by_resource' | count` must both be non-zero within 10 min of post-deploy traffic.
- **NFR-08**: ADR-009 lockstep — R2 does NOT amend ADR-009 (only operationalizes what R1 amended). No `.claude/adr/` or `docs/adr/` edits required; if a real architectural change emerges during execution, escalate before proceeding.

---

## Technical Constraints

### Applicable ADRs

- **ADR-009** (concise: `.claude/adr/ADR-009-redis-caching.md`, full: `docs/adr/ADR-009-caching-redis-first.md`) — R1 amended. R2 operationalizes the amendment (Bicep alerts replace markdown skeletons). MUST NOT modify ADR-009 in this project.
- **ADR-010** (DI minimalism: `.claude/adr/ADR-010-di-minimalism.md`) — Theme A.2 Meter consolidation eliminates an interface (`CacheMetrics` instance class) in favor of a static singleton. Aligns with ADR-010 "concretes over interfaces".
- **ADR-028** (auth: `.claude/adr/ADR-028-spaarke-auth-architecture.md`) — Theme B's rotation script preserves KV reference pattern (`@Microsoft.KeyVault(VaultName=...;SecretName=Redis-ConnectionString)`). No deviation.
- **ADR-029** (BFF publish hygiene: `.claude/adr/ADR-029-bff-publish-hygiene.md`) — Theme A publish-size delta ≤+0.5 MB per NFR-04. Verify per-task per ADR-029 protocol.
- **ADR-032** (Null-Object kill-switch: `.claude/adr/ADR-032-bff-nullobject-kill-switch.md`) — Theme A.2 Meter consolidation preserves symmetric DI for `IConnectionMultiplexer`. No changes to the Null-Object path.

### MUST Rules

- ✅ MUST emit `cache.failures` from `try/finally` so Redis outages are observable
- ✅ MUST have exactly one `Meter("Sprk.Bff.Api.Cache")` instance at runtime
- ✅ MUST deploy alerts via Bicep, not markdown
- ✅ MUST use safe-window key rotation (rotate secondary first, then primary)
- ✅ MUST isolate per-environment service principals (compromised env SP cannot rotate other envs)
- ✅ MUST log every rotation step to App Insights `RedisKeyRotation` custom event
- ✅ MUST preserve the manual rotation runbook as emergency fallback
- ✅ MUST keep all metric names emission-stable so post-deploy KQL queries still work
- ❌ MUST NOT cap `resource` dimension cardinality pre-emptively (NFR-06)
- ❌ MUST NOT silently skip OTel exporter in non-Development envs (FR-06)
- ❌ MUST NOT remove `customer.bicep` Redis provisioning without verifying via `what-if` that no live deployment relies on it
- ❌ MUST NOT modify ADR-009 (NFR-08)

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/MetricsDistributedCache.cs` for the existing decorator pattern (R7-S7) — Theme A.1 extends it
- See `scripts/Deploy-RedisCache.ps1` for the env-safety + idempotency + post-deploy verification pattern — Theme B's `Rotate-RedisKey.ps1` follows the same shape
- See `infrastructure/bicep/redis.bicep` and `redis-dev.bicepparam` for the Bicep modularization pattern — Theme A.4 `alerts.bicep` follows
- See `.github/workflows/sdap-ci.yml` (root CI) for the OIDC auth pattern — Theme B's `redis-key-rotation.yml` reuses
- See `docs/guides/redis-cache-azure-setup.md` (R1 deliverable) — Theme B §6 update is in-place; runbook structure already exists

---

## Success Criteria

1. [ ] Theme A: all 6 hardening items merged in one PR — Verify: `git log --grep 'spaarke-redis-cache-remediation-r2' | wc -l` ≥ 1 merge commit
2. [ ] KQL `customMetrics | where name == 'cache.failures'` returns ≥1 row within 10 min of `az redis force-reboot` against dev — Verify: documented in `notes/post-deploy-verification.md`
3. [ ] KQL `customMetrics | where name == 'cache.hits.by_resource'` returns rows with bounded resource values — Verify: KQL output captured in notes
4. [ ] Exactly one `Meter("Sprk.Bff.Api.Cache")` instance at runtime — Verify: integration test `MetricsDistributedCacheRegistrationTests` passes; `MeterListener` enumeration confirms count = 1
5. [ ] `az monitor metrics alert list -g rg-spaarke-dev` shows ≥3 alerts referencing cache metrics — Verify: command output captured in notes
6. [ ] BFF startup in Production env with missing `APPLICATIONINSIGHTS_CONNECTION_STRING` → throws — Verify: unit test for FR-06; live verification skipped (would require breaking prod)
7. [ ] `Rotate-RedisKey.ps1 -Environment dev` dry-run + manual `workflow_dispatch` produces successful rotation — Verify: `customEvents | where name == 'RedisKeyRotation' AND environment == 'dev' AND outcome == 'success'` non-empty
8. [ ] 3 cron schedules + workflow_dispatch input visible in workflow file — Verify: `gh workflow view redis-key-rotation.yml`
9. [ ] Per-env SP isolation verified — Verify: `az role assignment list --assignee {sp-id} --all` confirms scoped permissions for each env
10. [ ] `RedisKeyRotation`-missing alert fires in test condition — Verify: alert configuration captured in `alerts.bicep`; smoke-test via fake-timestamp method
11. [ ] `customer.bicep` `what-if` plan for fresh customer shows no Redis resource — Verify: `az deployment group what-if` output captured in notes
12. [ ] `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` §4.6 clean of strikethrough/footnote — Verify: diff review
13. [ ] DEF-007 + DEF-008 + DEF-009 GitHub Issues created and closed by R2 PR
14. [ ] GitHub Issue [#462 DEF-001](https://github.com/spaarke-dev/spaarke/issues/462) closed with link to Theme B automation
15. [ ] R1 close-out — DEF-001 in `notes/defer-issues.md` flipped from "Superseded" to "Closed (Done by R2 PR #N)"

---

## Dependencies

### Prerequisites

- **R1 R7-S7 closure shipped** (merged via PR #468) — R2 builds on the decorator pattern + OTel exporter. Confirmed merged to master `561fcf5d3`+.
- **R1 doc lockstep updates shipped** (merged via PR #477) — ADR-009 + caching-architecture.md + runbook + deployment guide all reflect R7-S7 reality.
- **R2 design spec-locked** (merged via PR #478) — design.md decisions resolved 2026-06-26.
- **GitHub Issues #462–#467 filed + #466 closed** — defer/issue tracking landed via PR #468 + #478.

### External Dependencies

- Operator must provision 3 GitHub Environment service principals (`AZURE_CLIENT_ID_DEV`, `AZURE_CLIENT_ID_STAGING`, `AZURE_CLIENT_ID_PROD`) before enabling cron schedules in Theme B. The R2 PR will document the setup steps in `docs/guides/redis-cache-azure-setup.md` §6 and gate the cron enablement on this one-time setup.
- Azure environments `dev` (rg `rg-spaarke-dev`), `staging` (rg name TBD — operator provides), `prod` (rg name TBD — operator provides) must exist with Redis + KV + App Service deployed.
- For Theme C: NO live customers may be relying on `customer.bicep`'s Redis module call. Verified via `az deployment group what-if` (FR-12 acceptance criterion). If a live customer is detected, escalate before deletion.

---

## Owner Clarifications

*Captured during design phase 2026-06-26 + spec-lock Q&A:*

| Topic | Question | Owner Answer | Implementation Impact |
|---|---|---|---|
| Managed Redis migration | Should R2 evaluate or migrate to Azure Managed Redis (`Microsoft.Cache/redisEnterprise`)? | NO. It's a high-throughput enterprise solution; we're below the scale where its differentiating features pay off. | DEF-005 closed Won't Fix. Research preserved at `notes/managed-redis-ai-research.md` for future revisit. |
| Path C (ACR Premium for Entra ID alone) | Worth +$485/mo to upgrade ACR to Premium just to get Entra ID auth? | NO. But build automation for the manual key rotation procedure instead. | Theme B replaces DEF-001 with rotation automation; closes #462 without infra cost. |
| Cron cadence (Theme B) | Quarterly, monthly, or other? | Quarterly (every 3rd month). | `0 6 N */3 *` cron expressions in workflow. |
| Theme B environment coverage | Dev only or all envs? | All envs (dev + staging + prod) from day 1. | 3 staggered cron schedules (dev 1st, staging 8th, prod 15th — operator-safety refinement so dev catches breakage before prod). |
| `resource` cardinality cap | Soft limit (e.g., 20) or unbounded? | Unbounded; trust code-driven natural bounding. | NFR-06: no cap. Re-evaluate only if observed >50 distinct values. |
| PR sequencing | One PR per theme or one combined? | One combined PR. Ship quickly. | NFR-01: atomic PR. |

---

## Assumptions

*Proceeding with these assumptions where owner did not specify:*

- **Staging + prod resource names**: Assuming naming follows R1 canonical pattern (`spaarke-bff-redis-{env}`, `rg-spaarke-{env}`, KV `spaarke-spekvcert` for dev — TBD for staging/prod). Operator confirms exact names at execution start.
- **OIDC service principal provisioning**: Assuming the operator already has tenant + subscription IDs and can create 3 federated identity credentials. If not, R2 documents the steps but does not provision SPs (out of scope — that's Azure-side IAM work).
- **No live customers on `customer.bicep` Redis**: Assuming `Provision-Customer.ps1` is the only path that creates customer envs in production, and that path was R1-deprecated. Confirmed by FR-12 `what-if` verification before any Bicep delete.
- **`Sprk.Bff.Api.Telemetry.CacheMetrics` consumers**: Assuming ~3-5 existing consumers (`EmbeddingCache`, `GraphTokenCache`, plus 1-3 others). Grep audit during execution catches all of them.
- **Production cardinality**: Assuming current production prompt/resource distribution has ≤20 distinct `resource` values (consistent with NFR-06 trust). If post-deploy KQL shows >30, file as observation but don't block on it.
- **`Deploy-BffApi.ps1` is the deploy path for production**: assumed valid post-R1 — operator confirms before R2 deploy if anything changed.

---

## Unresolved Questions

*None blocking. All 4 spec-lock questions resolved 2026-06-26.*

The following may surface during execution and require in-flight decisions (NOT blocking spec lock):

- [ ] Exact production resource names (staging + prod RG / Redis / KV / App Service) — operator confirms at execution start
- [ ] Whether to remove `Microsoft.ApplicationInsights.AspNetCore 2.23.0` package (M-1 from senior review) as a Theme A bonus cleanup — current spec says NO (cross-cutting, not cache); revisit at execution if convenient

---

*AI-optimized specification. Original design: `design.md` (spec-locked 2026-06-26). Decisions captured in `design.md` "Decisions locked" section.*
