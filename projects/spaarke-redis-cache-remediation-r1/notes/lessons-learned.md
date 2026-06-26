# Spaarke Redis Cache Remediation (R1) — Lessons Learned

> **Project**: `spaarke-redis-cache-remediation-r1`
> **Last Updated**: 2026-06-26
> **Author**: Task 056 (per FR-22 acceptance criteria)
> **Audience**: Future BFF caching maintainers, sister-project authors, ADR-009 / ADR-032 stewards
> **Status**: Authoritative project retrospective — companion to the operational summary in [`docs/guides/redis-cache-azure-setup.md`](../../../docs/guides/redis-cache-azure-setup.md) §10

This document captures the full retrospective for the Redis cache remediation project. The canonical operational guide ([`docs/guides/redis-cache-azure-setup.md`](../../../docs/guides/redis-cache-azure-setup.md)) §10 carries a condensed summary of the drift origin and guardrails; the execution lessons below are project-specific and live here, not in the canonical guide.

---

## 1. Drift Origin — How It Happened

The state at the start of this project — `spaarke-bff-dev` silently running on in-memory cache for an unknown duration — was the cumulative result of five compounding failures, none of which alone would have been catastrophic:

1. **Some prior project deleted the dev Redis instance** (`spe-redis-dev-67e2xz`). The deletion was operational, not coordinated with BFF owners; no follow-up issue was filed to re-provision a replacement.
2. **BFF App Setting `Redis__Enabled` was left at `false`.** This may have been an emergency mitigation during the deletion or a stale config from earlier; either way, no record of the rationale exists.
3. **`CacheModule` had `AbortOnConnectFail = false`**, so even when a connection was attempted, transient failures were silent — the BFF would start, log a single warning, and then run on `MemoryDistributedCache` indefinitely.
4. **`CacheModule` had no environment guard.** Redis-off + in-memory fallback was treated as a universal default. There was no distinction between "local developer laptop" (where in-memory is acceptable) and "deployed App Service environment named Development" (where it is not — multi-instance Pub/Sub never delivers).
5. **The in-memory warning log line was ignored or lost.** No App Insights alert fired on it; no startup-health check enforced the invariant; nobody was paged. The warning sat in the log stream, technically observable, operationally invisible.

The combination produced a deployed environment running on in-memory cache, with no key tenant prefix enforcement, no Pub/Sub invalidation, and no visibility of the degradation. The state could have persisted indefinitely.

---

## 2. Guardrails Now In Place (would have prevented the drift)

The following controls now exist in the BFF + IaC + observability stack as a result of this project. Each one independently breaks the failure chain above.

1. **Fail-fast in deployed environments.** `CacheModule` now throws `InvalidOperationException` at startup when Redis is configured-but-unreachable (`AbortOnConnectFail = true` + 4-branch environment guard). A deployed BFF cannot silently run on in-memory cache — it either runs on Redis or it does not start. Reference: `src/server/api/Sprk.Bff.Api/Modules/CacheModule.cs`, ADR-009 (amended), task 003.
2. **Explicit opt-in for fallback.** `Redis:AllowInMemoryFallback` defaults `false`. Even the Development environment requires it `true` to use in-memory cache. The deployed dev App Service ships with `false`; in-memory mode is now a local-developer-laptop-only state. Reference: `RedisOptions.cs`, `appsettings.template.json`.
3. **Null-Object `IConnectionMultiplexer`.** Symmetric DI registration (per ADR-032) means consumers in dev never see `service not registered` — they see no-op Pub/Sub (Subscribe registers but never delivers) and an explicit `NotSupportedException` on any direct database call ("use `IDistributedCache`"). This eliminates an entire class of `IConnectionMultiplexer?` nullable-defensive code that previously masked the underlying state. Reference: `NullConnectionMultiplexer.cs`, task 004.
4. **Canonical naming.** All new instances follow `spaarke-bff-redis-{env}` (top-level env-suffix). Off-pattern legacy instances (`spe-redis-dev-67e2xz`) stand out at a glance in resource lists and lifecycle scripts. Reference: NFR-03, `redis-{env}.bicepparam`.
5. **Tenant-prefix mandatory in keys.** The canonical key format is `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}`. The `ITenantCache` wrapper enforces the prefix at the call site; even system-level exceptions (feature flags, system config) are explicitly allow-listed with JSON-comment justification (NFR-08). The 11 in-tree exception candidates are documented in `notes/system-cache-exceptions.md` — well under the NFR-08 escalation threshold of 20. Reference: `Spaarke.Core/Cache/ITenantCache.cs`, task 002.
6. **App Insights observability.** Redis dependency telemetry (auto), `cache.hits` / `cache.misses` / `cache.redis_call_duration_ms` custom metrics (wrapper-emitted), and three alert rules (hit rate < 80%, P95 latency > 100 ms, memory > 80%) make any future degradation visible within minutes. Reference: FR-16 / FR-17, `notes/alert-definitions-draft.md`, `docs/guides/redis-cache-azure-setup.md` §8.
7. **Deployment checklist.** `Deploy-RedisCache.ps1` (idempotent, multi-env, `-WhatIf` / `-VerifyOnly` / `-CutoverBffSettings` / `-Force` per NFR-01/05/06) and the runbook in [`docs/guides/redis-cache-azure-setup.md`](../../../docs/guides/redis-cache-azure-setup.md) let any future operator provision a new env Redis end-to-end in under 30 minutes (FR-19, Success Criterion #6). Reference: `scripts/Deploy-RedisCache.ps1`, task 025.

---

## 3. Execution Lessons (project-specific — stay in notes, not the canonical guide)

The lessons in this section are about how this project was run, not about Redis caching as a system. They are recorded here because they are useful to future task-execute / project-pipeline maintainers and to authors of follow-on remediations.

1. **§F.2 Fixture-Config-FIRST inspection saved the project.** Task 003's stricter `CacheModule` (4-branch fail-fast) introduced **337 latent test failures** across 15 `WebApplicationFactory<Program>`-based fixture files because those fixtures used `["Redis:Enabled"] = "false"` + `builder.UseEnvironment("Testing")`, which now hits the new throw branch. Sweeping the fixtures BEFORE running the full test suite would have caught this; running tests first would have wasted a wave debugging the resulting noise. The fix (3-part change applied uniformly: `AllowInMemoryFallback=true`, `UseEnvironment("Development")` + `ValidateScopes/ValidateOnBuild = false`, `ThrowOnBadRequest = false`) was uniform once the root cause was understood. **Future rule**: any task tightening DI/startup invariants in `CacheModule` / `Module.cs` MUST sweep all `WebApplicationFactory`-based fixtures as Step 0 — this is §F.2 of [`.claude/constraints/bff-extensions.md`](../../../.claude/constraints/bff-extensions.md) and it earned its place this project. Outcome after fix: **7826 passed, 0 failed, 135 skipped** on full BFF tests.
2. **Authoritative inventory beats estimates.** The spec said 117 cache call sites; the plan said ~199; reality was **153 sites across 56 files** in `Sprk.Bff.Api/` + 4 in `Spaarke.Core/Cache/`. Task 001's inventory was the load-bearing first step — without it, the parallel-migration ordering in tasks 010–015 would have been miscalibrated, and the NFR-07 atomicity (single-PR commit) would have been at risk. **Future rule**: any project that touches "N call sites" of an abstraction must produce an authoritative inventory as task 001 before plan WBS is finalized.
3. **PR #253 (NuGet bump) coexisted gracefully.** `Microsoft.Extensions.Caching.StackExchangeRedis` package bump (PR #253) was open during this project but did not merge first, so task 063's conflict-check pattern was never exercised in practice. **Future rule**: open Dependabot PRs that have been stale for >30 days are unlikely to merge mid-project; budget a low conflict probability and don't over-engineer rebases.
4. **SKU shape decision.** The spec said "SKU object" but the existing `redis.bicep` module + 3 in-tree callers (`customer.bicep`, `stacks/model1-shared.bicep`, `stacks/model2-full.bicep`) were `string + int` form. Migrating to object would have been a breaking change with zero capability gain (the module already constructs `{ name, family, capacity }` internally via `skuFamilies` map). The right call was to document the decision in `plan.md` + `notes/redis-bicep-audit.md` and keep `string + int`. **Future rule**: spec wording about parameter shapes is non-binding when the existing in-tree shape satisfies the same FR; the FR is the contract, the shape is the implementation detail.
5. **`appsettings.json` does NOT exist for the BFF.** The spec's FR-07 grep gate targeted `appsettings.json`, but `Sprk.Bff.Api` uses `appsettings.template.json` (with `#{TOKEN}#` placeholders) + `appsettings.tokens.md` (the operator-facing token doc). The dev-environment InstanceName value lives in `appsettings.tokens.md` (changed `sdap-dev:` → `spaarke:`). FR-07 is satisfied by `RedisOptions.cs` default + the token doc. **Future rule**: always verify file layout assumptions against reality (`Grep` / `Glob` first) before authoring grep-based gates in spec acceptance criteria.
6. **Parallel migration was viable despite NFR-07 "sequential" guidance.** Tasks 010–015 ran in parallel because they touched file-disjoint sets of cache call sites. The NFR-07 atomicity requirement is about the **single-PR boundary** (the read-old/write-new bug class), not about in-flight task ordering. Approximately **6x speedup** vs. the originally planned linear ordering. **Future rule**: NFR-07-style "atomic" constraints describe the merge boundary, not the in-flight execution graph. Parallel task execution is valid as long as the final commit is a single coordinated PR.

---

## 4. Cross-References

- [`docs/guides/redis-cache-azure-setup.md`](../../../docs/guides/redis-cache-azure-setup.md) §10 — condensed summary (drift + guardrails) in the canonical operational guide.
- [`spec.md`](../spec.md) §FR-22 — acceptance criteria this document satisfies.
- [`r7-backlog.md`](r7-backlog.md) — deferred items (S1 Entra ID auth, S2 Pub/Sub separation, S3 multi-region, S4 other secrets migration) per FR-22.
- [`cache-call-site-inventory.md`](cache-call-site-inventory.md) — task 001 authoritative call-site inventory (153 sites across 56 files).
- [`system-cache-exceptions.md`](system-cache-exceptions.md) — NFR-08 allow-listed non-tenant-scoped keys (11 candidates).
- [`symmetric-di-verification.md`](symmetric-di-verification.md) — task 005 verification of symmetric `IConnectionMultiplexer` registration across BFF.
- [`alert-definitions-draft.md`](alert-definitions-draft.md) — FR-17 alert specifications (hit rate, P95 latency, memory).
- [`.claude/constraints/bff-extensions.md`](../../../.claude/constraints/bff-extensions.md) §F.2 — Fixture-Config-FIRST Inspection Protocol (the rule that saved this project).
- [`.claude/adr/ADR-009-redis-caching.md`](../../../.claude/adr/ADR-009-redis-caching.md) (concise) and [`docs/adr/ADR-009-caching-redis-first.md`](../../../docs/adr/ADR-009-caching-redis-first.md) (full) — both amended in lockstep by FR-20 / task 052+053.
- [`.claude/adr/ADR-032-bff-nullobject-kill-switch.md`](../../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) — Null-Object pattern used for `IConnectionMultiplexer`.

---

*This document is the canonical retrospective for `spaarke-redis-cache-remediation-r1`. Updates require justification — most readers should be steered to [`docs/guides/redis-cache-azure-setup.md`](../../../docs/guides/redis-cache-azure-setup.md) §10 for the operational summary instead.*
