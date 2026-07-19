# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-19
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none — full pipeline complete; 25 tasks generated; **all committed + pushed** to `origin/work/spaarke-SPA-external-access-platform-r1` |
| **Step** | Pipeline paused BEFORE execution (Step 5) — deliberate |
| **Status** | none (awaiting execution go-ahead) |
| **Next Action** | Two paths (owner to choose): **(A)** provision Phase 0 live resources externally (CIAM tenant+app, SWA resource, `Contact.sprk_externalobjectid`) then run tasks 010+ ; **(B)** start the two code-only leaf tasks that need NO live resources first — **024** (CIAM onboarding email template) and **026** (drop synthetic SPE grant) — via `task-execute`. Recommended: B for momentum while A is provisioned. |

### Where things stand (fresh-session summary)
- **Design → Spec → Pipeline all DONE and committed + pushed.** No PR opened yet (branch is planning-only; open one when implementation code lands, or a draft for visibility).
- **ADR-028 Amendment A1 APPLIED** to `.claude/adr/ADR-028-spaarke-auth-architecture.md` (CIAM sanctioned for external surface, broker-only invariant, E-3 boundary).
- **BFF audit done** (3-track) — reuse map is baked into `spec.md`/`plan.md`/`CLAUDE.md`. Key reuse: `SpeFileStore.DownloadFileAsync` (no new download method), `SpeAdminTokenProvider` (cross-tenant client template), `GraphUserService`/`PasswordGenerator`, `RegistrationEmailService`, extend `ExternalCallerAuthorizationFilter` (don't fork).
- **Registered** in `projects/INDEX.md` (BFF=Y narrow, CI=Y).
- **25 POMLs validated** (Validate-TaskPoml.ps1: 0 errors/0 warnings); TASK-INDEX has the DAG + 16 waves; **no `/goal`-eligible waves** (auth/deploy/irreversible).

### Critical Context
Hosting + identity migration (Power Pages + B2B → Azure SWA + Entra External ID/CIAM), broker-only. Type-2 (CIAM/MAU) only; Type-1 demo-registration out of scope. Two `xhigh` correctness-critical tasks: **025** (provisioner) + **027** (download authz-before-stream, negative test is the key property). Phase 0 = live Azure/CIAM ops provisioning (why execution paused). See `CLAUDE.md` for binding project rules + decisions.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — |
| **Title** | — |
| **Phase** | — |
| **Status** | none |
| **Started** | — |

---

## Progress

### Completed Steps
*No steps completed yet*

### Files Modified (All Task)
*No task files modified yet*

### Decisions Made
*See [`CLAUDE.md`](CLAUDE.md) "Decisions Made" for project-level decisions*

---

## Next Action

**Next Step**: Run `/task-create` to generate POML task files from `plan.md`, then execute Phase 0.

**Pre-conditions**: spec.md + plan.md finalized (done); ADR-028 Amendment A1 applied (done); baseline builds (verified).

**Key Context**: Phase 0 (foundations: CIAM tenant/app + SWA resource + `sprk_externalobjectid`) gates Phases 1–2 and depends on live Azure/CIAM provisioning.

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-07-19
- Focus: Project initialization (design → spec → BFF audit → artifacts). Pipeline paused before task execution per owner request.

### Key Learnings
- BFF audit found significant reuse (download, provisioning, email, auth) — scope is smaller than the raw spec implied.

---

## Quick Reference

### Project Context
- **Project**: spaarke-SPA-external-access-platform-r1
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md) (pending task-create)

### Applicable ADRs
- ADR-028 (+Amendment A1): CIAM external identity/auth
- ADR-008: endpoint authorization filters
- ADR-009: Redis-first caching
- ADR-007: SpeFileStore facade

---

*This file is the primary source of truth for active work state. Keep it updated.*
