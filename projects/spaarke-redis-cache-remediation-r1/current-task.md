# Current Task State — spaarke-redis-cache-remediation-r1

> **Last Updated**: 2026-06-26 (project close-out)
> **Status**: ✅ **PROJECT COMPLETE** — no active task

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | spaarke-redis-cache-remediation-r1 |
| **Active task** | none — project closed |
| **Status** | ✅ Complete (all 5 phases shipped via PR #458 + PR #460) |
| **Next action** | Nothing here. R8 backlog has follow-ups documented in [`notes/r7-backlog.md`](notes/r7-backlog.md) §S5/S6/S7 — pick up in a future R8 project. |

---

## Project deliverables shipped

- **PR #458** (merged) — Phase 1 (CacheModule hardening + ITenantCache + atomic 153-site migration) + Phase 2 (Bicep + Deploy-RedisCache.ps1) + Phase 3 (dev cutover: new Redis + KV secret + App Settings + BFF restart + verification + legacy tag + sister handoff) + Phase 5 (canonical docs + ADR-009 lockstep amendment + R7 backlog)
- **PR #460** (merged) — AI-Search sister-project handoff doc + R7-S7 partial closure (OTel → Azure Monitor exporter wiring)

## Verified at close-out

- ✅ Build clean: `dotnet build src/server/api/Sprk.Bff.Api/` 0 errors
- ✅ Tests at baseline: 7885 pass / 2 pre-existing flaky-fail / 135 skip
- ✅ Deploy clean: `Deploy-BffApi.ps1` 4/4 critical DLLs hash-verified; package 46.67 MB; `/healthz` 200
- ✅ Dev BFF runs new code: traces confirm `Cached 2 communication accounts (comm:accounts:receive-enabled)` + Redis health check passing every minute + ~800-1000 cmds/min on `spaarke-bff-redis-dev`
- ✅ App Insights OTel pipeline live: HTTP server/client histograms + custom `circuit_breaker.open_count` counter both visible in `customMetrics` (proves Sprk.Bff.Api.* Meter pipeline works)
- ✅ Sister project unblocked: `projects/spaarke-ai-azure-setup-dev-r1/notes/handoffs/redis-cache-remediation-r1-phase3-cleared.md` written + verified

## R8 follow-up backlog (documented, deliberately deferred)

See [`notes/r7-backlog.md`](notes/r7-backlog.md) for full detail. Three items surfaced during this project:

- **S5** — Evaluate Azure Managed Redis (`Microsoft.Cache/redisEnterprise`) for prod
- **S6** — Rename App Insights `spe-insights-dev-67e2xz` → `spaarke-bff-insights-dev` (canonical naming)
- **S7** — Two specific Redis observability sub-gaps remaining post-OTel-exporter-wiring:
  - (1) `AddRedisInstrumentation()` not surfacing Redis dep spans — fix: wire `RedisCacheOptions.ConnectionMultiplexerFactory` so cache + telemetry share the same multiplexer
  - (2) `cache.hits/misses/redis_call_duration_ms` Meter measurements not appearing — needs static-init-vs-MeterProvider ordering audit + call-site coverage audit

None of these block the project's success criteria or the sister project's unblock signal. All are documented for future planning rounds.

---

*Project closed 2026-06-26. To resume work on any of S5/S6/S7, create a new project per `/design-to-spec` and reference `notes/r7-backlog.md` for the entry-points.*
