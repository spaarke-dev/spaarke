# spaarke-redis-cache-remediation-r2

> **Portfolio**: [Issue #481](https://github.com/spaarke-dev/spaarke/issues/481) on [Project #2](https://github.com/users/spaarke-dev/projects/2) (`Type=Project`, `Project Type=Infrastructure`, `Status=Planned`)
> **Parent Epic**: [#425 BFF & Test Hygiene](https://github.com/spaarke-dev/spaarke/issues/425)
> **Predecessor**: [`spaarke-redis-cache-remediation-r1`](../spaarke-redis-cache-remediation-r1/) (closed 2026-06-26)
> **Worktree**: `C:\code_files\spaarke-wt-spaarke-redis-cache-remediation-r2`

---

## Status

| Field | Value |
|---|---|
| **Phase** | ✅ Complete (PR pending) |
| **Owner** | spaarke-dev |
| **Estimated effort** | 3-5 days (one combined PR per spec NFR-01) |
| **Spec lock date** | 2026-06-26 |
| **Local complete date** | 2026-06-26 |

All 17 tasks shipped to `origin/work/spaarke-redis-cache-remediation-r2`; PR pending operator-driven live deploy + KQL verification per [`notes/post-deploy-verification.md`](notes/post-deploy-verification.md). Lessons learned captured in [`notes/lessons-learned.md`](notes/lessons-learned.md).

## Summary

R2 is a closure project for `spaarke-redis-cache-remediation-r1` — finishes the R1 work properly without re-architecting. Three coherent themes:

| Theme | Scope | Effort |
|---|---|---|
| **A. Cache observability hardening** | Six concrete fixes from R1 senior review: `cache.failures` Counter, Meter consolidation (R1 audit found TWO `Meter("Sprk.Bff.Api.Cache")` instances), `resource` tag restoration at wrapper layer, Bicep-deployed alerts (3 minimum), decorator regression test, `UseAzureMonitor` fails-open guard | 2-3 days |
| **B. Redis key rotation automation** | Replaces the historically-slipping manual 90-day procedure with a scheduled GitHub Actions workflow (staggered across dev/staging/prod). Closes [#462 DEF-001](https://github.com/spaarke-dev/spaarke/issues/462) without paying +$485/mo for ACR Premium. | 1-2 days |
| **C. R1 implementation gap closure** | Removes `customer.bicep:181` per-customer Redis call (R1 deprecated in `Provision-Customer.ps1` but the Bicep template was left untouched). | 0.5 day |

## Explicit non-goals (with rationale)

- **Managed Redis migration** — see [`notes/managed-redis-decision.md`](notes/managed-redis-decision.md). DEF-005 [#466](https://github.com/spaarke-dev/spaarke/issues/466) closed Won't Fix.
- **DEF-002 / DEF-003 / DEF-004 / DEF-006** — see [`design.md` §4](design.md) for per-item rationale.

## Quick Links

| Document | Purpose |
|---|---|
| [`spec.md`](spec.md) | AI-optimized specification (14 FRs, 8 NFRs, 5 ADRs, 15 success criteria) |
| [`design.md`](design.md) | Human design document (3 themes, decisions locked) |
| [`notes/managed-redis-decision.md`](notes/managed-redis-decision.md) | Decision record for the Managed Redis evaluation |
| [`notes/managed-redis-ai-research.md`](notes/managed-redis-ai-research.md) | Background research (informational; supports the decision record) |

## Next steps

1. Run `/project-pipeline projects/spaarke-redis-cache-remediation-r2` to decompose spec.md into POML task files + generate `TASK-INDEX.md`
2. Execute Themes A + B + C in one combined PR per NFR-01

## Owner clarifications already locked

All 4 spec-lock questions resolved 2026-06-26 (captured in [`spec.md`](spec.md) Owner Clarifications table):

1. No Managed Redis (DEF-005 closed)
2. No ACR Premium for DEF-001 alone (Theme B automation replaces it)
3. Quarterly cron, all envs from day 1, staggered times (dev 1st, staging 8th, prod 15th)
4. One combined PR for atomicity + speed
