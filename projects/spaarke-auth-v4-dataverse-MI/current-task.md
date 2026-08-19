# Current Task — spaarke-auth-v4-dataverse-MI

> Updated by `task-execute`. All work state must be recoverable from this file + `tasks/TASK-INDEX.md` + `CLAUDE.md`.

## Active Task

**Task**: none (project initialized 2026-08-19; execution owner-gated, NOT started)
**Status**: not-started
**Next action**: run `task-execute` on `tasks/001-create-dev-deployment-slot.poml`

## Progress

- [x] Design, research, live verification
- [x] ADR-028 A4 + E-3 applied (2026-08-17)
- [x] Dev MI-FIC created (2026-08-19) — `mi-bff-api-dev-assertion`
- [x] spec.md generated + `/adr-check` clean (1 violation folded in)
- [x] Cross-project coordination sent; provisioning replied + applied
- [x] `/project-pipeline` — plan.md, CLAUDE.md, 29 tasks, TASK-INDEX.md
- [ ] Phase 0 spike

## Files Modified This Task

(none — no execution started)

## Decisions

| Date | Decision | Rationale |
|---|---|---|
| 2026-08-19 | Rollout is **dev only** | `spaarke-bff-prod` is Stopped; prod/demo decommissioned |
| 2026-08-19 | Power BI → **UAMI-as-principal** | Microsoft's documented model; leaves the shared provider seam |
| 2026-08-19 | Group 2 keys = **parallel workstream** | Independent of the OBO migration |
| 2026-08-19 | Provider seam = **injected named interface** | `Spaarke.Core` placement is circular + fails FR-14 |
| 2026-08-19 | FR-C4 (FIC automation) **in scope** | Provisioning task 130 is soft-blocked on it |

## Blockers

- ⚠️ Task 040 gates Phase 4: Power BI service-principal **profiles** under a managed identity are unverified.
- ⚠️ Raised to provisioning: Model 2 FIC issuer may be cross-tenant (`PROVISIONING-CHANGE-REQUEST.md` §9.2).
