# Spaarke External Access Platform (R1) - AI Context

> **Purpose**: This file provides context for Claude Code when working on spaarke-SPA-external-access-platform-r1.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning → ready for task decomposition
- **Last Updated**: 2026-07-19
- **Current Task**: Not started
- **Next Action**: Run task-create to decompose plan into task files

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized specification (BFF-audit-reconciled)
- [`design.md`](design.md) - Owner-reviewed design (decision record)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (created by task-create)
- [`adr-028-amendment-draft.md`](adr-028-amendment-draft.md) - Amendment A1 (APPLIED to `.claude/adr/ADR-028`)

### Project Metadata
- **Project Name**: spaarke-SPA-external-access-platform-r1
- **Type**: Hosting + Identity migration (BFF auth + external SPA + Dataverse schema + Azure infra)
- **Complexity**: Medium-High (cross-tenant identity, live Azure/CIAM dependencies)

---

## Context Loading Rules

1. **Always load this file first** when starting work on any task.
2. **Check current-task.md** for active work state (especially after compaction/new session).
3. **Reference spec.md** for requirements/acceptance criteria; **design.md** for the "why".
4. **Load the relevant task file** from `tasks/`.
5. **Apply ADRs** — ADR-028 (+Amendment A1) is central; also ADR-007/008/009/001/010/019.

**Context Recovery**: [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" / "keep going" / "next task" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" / "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Bypassing task-execute leads to**: missing ADR constraints, no checkpointing, skipped quality gates (code-review + adr-check at Step 9.5).

### Parallel Task Execution
When tasks can run in parallel (no dependencies), each MUST still use task-execute — one message with multiple Skill invocations. See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md).

### 🚨 MUST: Multi-File Work Decomposition
For tasks modifying 4+ files: decompose into a dependency graph, parallelize independent modules via subagents, serialize tightly-coupled work. `.claude/` paths are main-session-only (sub-agents cannot write there).

---

## 🔴 Project-Specific Binding Rules

1. **§10 BFF Hygiene applies to every BFF task.** State Placement Justification in the PR (cite `.claude/constraints/bff-extensions.md`); run `dotnet publish -c Release` and report compressed size + diff (ceiling **≤60 MB**, baseline ~49.63 MB); verify no new HIGH CVE; add/update tests in `tests/unit/Sprk.Bff.Api.Tests/`.
2. **Broker-only invariant (ADR-028 A1).** The external user's token is used ONLY to authenticate to the BFF; **never** exchange it downstream (no OBO on the external path). All external SPE/Dataverse access is app-only. Do NOT reuse the OBO `DownloadFileAsUserAsync` path on the external surface.
3. **Reuse, don't rebuild** (from the BFF audit). Reuse `SpeFileStore.DownloadFileAsync`, `SpeAdminTokenProvider` (cross-tenant client template), `GraphUserService`/`PasswordGenerator`, `RegistrationEmailService`, `ExternalCallerAuthorizationFilter`/`ExternalParticipationService` (extend, don't fork), `AuthorizationModule` (add scheme), `TrackingIdGenerator`/`RegistrationDataverseService`.
4. **Authz-before-stream.** The download endpoint MUST enforce `sprk_externalrecordaccess` + document→project scoping BEFORE resolving Graph pointers / streaming. Requires a negative/unauthorized test.
5. **Preserve external-SPA `sessionStorage`** per-tab isolation (documented ADR-028 exception). Do NOT switch to `@spaarke/auth` or `localStorage`.
6. **Do NOT expose Graph pointers** (`driveId`/`driveItemId`) to the browser — endpoint keyed on `documentId`, pointers resolved server-side.

---

## Key Technical Constraints

- **ADR-028 (+Amendment A1)**: CIAM authority via a second `"Ciam"` JwtBearer scheme (pinned on the `/api/v1/external` group only); resolve Contact by stable `oid` (`sprk_externalobjectid`); broker-only; E-3 direct-Office boundary out of scope.
- **ADR-008**: per-endpoint `ExternalCallerAuthorizationFilter` (extend for oid).
- **ADR-009**: Redis participation cache invalidation on grant/revoke.
- **ADR-007**: `SpeFileStore` facade for SPE ops.
- **ADR-001/010/019**: Minimal API, DI minimalism (register concretes), ProblemDetails.
- **ADR-021/022**: Fluent v9 + React 18 for the SPA.
- Cross-tenant Graph client: `GraphClientFactory` is single-tenant — model the CIAM client on `SpeAdminTokenProvider.GetOrCreateMsalApp` (per-authority + KV secret / MI-FIC).

---

## Decisions Made

- 2026-07-19: Type-2 CIAM only; Type-1 demo-registration out of scope. — Owner review.
- 2026-07-19: Admin-initiated onboarding; self-service sign-up + Legal Front Door deferred (`isSignUpAllowed=false`, onboarding-agnostic hook). — Owner review.
- 2026-07-19: Resolve Contact by `oid`, not email; add `Contact.sprk_externalobjectid`. — Owner + researcher spike.
- 2026-07-19: Reuse existing `DownloadFileAsync`; drop DTO pointer exposure; endpoint keyed on `documentId`. — BFF audit.
- 2026-07-19: ADR-028 Amendment A1 applied (CIAM sanctioned for external surface). — Owner accepted.

---

## Implementation Notes

- The demo-registration system (`spaarke-self-service-registration-app`) is a *separate* Type-1 subsystem; its form is on the marketing site + approval in the MDA — NOT Power Pages, so it is out of the migration blast radius. It is the north-star pattern for the future Legal Front Door router.
- Phase-2 verification spikes (OTP-only feasibility, MI-FIC GA, `email` claim presence, live E2E) are **non-blocking** — the architecture is already GREEN.

---

## Deferrals & Issues — tracking obligation

Track deferred work + newly-discovered issues in BOTH `notes/defer-issues.md` (source of truth) AND GitHub Issues (visibility) via `/project-defer-issue-tracking` (`/defer`). §11 rule applies — every entry must name a concrete failing behavior/contract. Use `gh issue list --label spaarke-SPA-external-access-platform-r1` for the team view.

---

## Resources

### Applicable ADRs
- **ADR-028 (+Amendment A1)** [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — external identity/auth
- **ADR-008** — endpoint authorization filters
- **ADR-009** — Redis-first caching
- **ADR-007** — SpeFileStore facade
- **ADR-001 / ADR-010 / ADR-019** — Minimal API / DI minimalism / ProblemDetails
- **ADR-021 / ADR-022** — Fluent v9 / React 18 SPA

### Related Projects
- `projects/sdap-secure-project-module` (R1) / `-r2` — the platform this layer sits on
- `projects/spaarke-self-service-registration-app` — Type-1 demo registration (out of scope; future-router pattern)

### External Documentation
- `docs/architecture/external-access-spa-architecture.md` (to be rewritten)
- `docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md`, `EXTERNAL-ACCESS-SPA-GUIDE.md`, `auth-deployment-setup.md`
- `.claude/constraints/bff-extensions.md` (BFF §10 governance)
- `.claude/agent-memory/researcher/ciam-user-provisioning-graph-2026-07-19.md` (CIAM provisioning mechanics)

---

*This file should be kept updated throughout project lifecycle.*
