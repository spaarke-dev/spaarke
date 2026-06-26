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
| **Next action** | Nothing here. Follow-on work scoped as [`spaarke-redis-cache-remediation-r2`](../spaarke-redis-cache-remediation-r2/) (3-5 days: cache observability hardening + key rotation automation + R1 implementation gap closure). |

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

## Follow-on work — `spaarke-redis-cache-remediation-r2`

R7-S7 was fully closed inline (both sub-gaps fixed and KQL-verified). Remaining items surfaced during execution are scoped into the R2 project:

- **R2 Theme A** — Cache observability hardening (6 items from senior review): `cache.failures` Counter, Meter consolidation, `resource` tag restoration, Bicep-deployed alerts, decorator regression test, `UseAzureMonitor` fails-open guard
- **R2 Theme B** — Redis key rotation automation (replaces DEF-001 Entra ID auth without paying +$485/mo for ACR Premium)
- **R2 Theme C** — `customer.bicep` per-customer Redis cleanup (R1 implementation gap)

R2 spec + design landed via PRs #477, #478, #479. R2 worktree at `C:\code_files\spaarke-wt-spaarke-redis-cache-remediation-r2` on branch `work/spaarke-redis-cache-remediation-r2`.

## Defer/issue tracking

All 6 R1-surfaced items are filed as GitHub Issues:

- [#462](https://github.com/spaarke-dev/spaarke/issues/462) DEF-001 Entra ID auth → **Superseded** by R2 Theme B
- [#463](https://github.com/spaarke-dev/spaarke/issues/463) DEF-002 Pub/Sub separation → Out of R2 scope; re-evaluate after 30 days of post-R2 data
- [#464](https://github.com/spaarke-dev/spaarke/issues/464) DEF-003 Multi-region → Open (single-region today)
- [#465](https://github.com/spaarke-dev/spaarke/issues/465) DEF-004 Plain-text secrets (non-Redis) → Open (cross-cutting)
- [#466](https://github.com/spaarke-dev/spaarke/issues/466) DEF-005 Managed Redis → **Closed Won't Fix** (decision: enterprise solution; Spaarke below scale)
- [#467](https://github.com/spaarke-dev/spaarke/issues/467) DEF-006 App Insights rename → Open (fold into sister project)

See [`notes/defer-issues.md`](notes/defer-issues.md) for full context. The legacy [`notes/r7-backlog.md`](notes/r7-backlog.md) is preserved as the historical R7-round-organized backlog (superseded by the canonical defer-issues.md tracker).

---

*Project closed 2026-06-26. Code review sign-off ✅ (R7-S7 closure clean; no blocking issues). R2 follow-on already scoped and worktree-ready.*
