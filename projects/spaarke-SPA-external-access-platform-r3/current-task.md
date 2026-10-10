# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-10 (by project-pipeline)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none active — project initialized; execution not started |
| **Step** | `/project-pipeline` complete (README, plan, CLAUDE.md, 34 task POMLs, TASK-INDEX) |
| **Status** | ready for Wave 1 |
| **Next Action** | Owner go to start Wave 1: task 001 in the main session (needs owner approval of the ADR-028 amendment text), plus 003, 010, 020, 050 as parallel task-execute agents. 002 starts when the owner sends the per-tab column list |

### Critical Context
- Scope is in `spec.md`; the binding rules and owner decisions are in `CLAUDE.md`; waves and file chains are in `tasks/TASK-INDEX.md`.
- **Planning found and fixed three things.** All are recorded in `notes/decisions.md`; `spec.md` is updated.
  - `sprk_servicerequest` has no request-type or intake column. Task 020 adds them.
  - `WizardModal`/`WizardRegistry` were deleted. Use `WizardShell` + `InAppWizardHost`.
  - Matter and Work Assignment have no event reads. New task 039 adds them.
- PR #1604 (R3 design + spec) merged to master on 2026-10-10.

---

## Active Task

None.

## Next Actions

1. Wave 1 on the owner's go:
   - main session: 001 (owner approval);
   - agents: 003, 010, 020 (schema import is owner-gated), 050.
2. Ask the owner for the per-tab column list (task 002).
3. Ask the owner the 060 host question early, since it decides whether FR-14 stays in R3 or moves to R4.
4. Task 036 cannot start until UAC-r2 PR #1583 merges.

## Open Owner Questions / Blockers

- **Registration service host (FR-14, task 060).** Who builds and hosts the T240c directory and the registration service? R3 now or R4?
- **Grid column list** (task 002).
- **New single-tenant workforce client registration** (task 040). The owner creates it or approves its creation.
- **Teams broker pre-authorization:** answered by the FR-21 live check (task 071).
- **Portfolio:** R3 is not registered on the GitHub board (no Portfolio block in `README.md`).

## Dependencies tracked elsewhere

- **Provisioning:**
  - T240c directory;
  - T240d — spike S1 (i)–(ii) passed; (iv) pending, and it gives the FR-22 token shapes (`notes/coordination/2026-10-10-from-provisioning-t240d-s1-partial.md`);
  - H3 pre-authorization of the new workforce client;
  - per-customer shared mailbox (#1562).
- **UAC-r2:**
  - PR #1583 (needed by 036);
  - PR #1586;
  - A5 (#1567).
- **Issues R3 carries:** #1563 (task 012), #1566 (task 042), #1568 (task 011).
