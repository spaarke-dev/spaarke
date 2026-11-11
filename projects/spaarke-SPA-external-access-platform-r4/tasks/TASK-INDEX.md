# Task Index — Spaarke External Access Platform R4

> **Created**: 2026-11-10 by `/project-pipeline` (task-create)
> **Execution**: NOT started. Owner directive 2026-11-10: no task runs until the owner has reviewed this index and `plan.md`.
> Status tokens: `[open]` 🔲 · `[wip]` 🔄 · `[done]` ✅ · `[escalated]` ⚠️ · `[blocked]` 🟡 · `[cancelled]` 🚫 · `[deferred]` ⏸️. The bracketed token is mandatory (FAILURE-MODES G-16). After any status change, run `pwsh scripts/check-task-status-drift.ps1`.

## Tasks

| ID | Title | Phase | Status | Dependencies | Parallel | Tier / effort |
|-----|-------|-------|--------|--------------|----------|---------------|
| 001 | Shared RecordCommandBar + icon map (FR-04, FR-05) | 1 | 🔲 [open] | none | Group A | opus / high |
| 002 | BFF matter/WA event routes + delete-409 ProblemDetails (FR-08, FR-09) | 1 | 🔲 [open] | none | Group A | opus / high |
| 003 | RecordTemplateConfig + template shell (FR-01, FR-02) | 1 | 🔲 [open] | 001 | — | opus / high |
| 005 | Section renderers for six types; retire R3 modal (FR-01) | 1 | 🔲 [open] | 003 | — | opus / high |
| 004 | Child dialogs inside the parent SprkModal (FR-03) | 1 | 🔲 [open] | 005 | — | sonnet / high |
| 006 | Per-record access, hide-don't-disable, "Your access" (FR-06, FR-07) | 1 | 🔲 [open] | 002, 004 | — | opus / high |
| 007 | Submit invoice record action (FR-10) | 1 | 🔲 [open] | 006 | — | sonnet / high |
| 018 | P1 deploy (BFF via uac-r2, SPA dev) + E2E + first-E2E checks (FR-33) | 1 | 🔲 [open] | 002, 004, 005, 006, 007 | — (live) | sonnet / high |
| 020 | Review detail (082c) + held-Accepted badge (FR-11, FR-12) | 2 | 🔲 [open] | 018 | Group B | sonnet / high |
| 022 | Inline PDF/image preview (FR-14) | 2 | 🔲 [open] | 018 | Group B | sonnet / high |
| 023 | Documents with no file + Documents on shared DataGrid (FR-15, FR-16) | 2 | 🔲 [open] | 018, 022 | — | sonnet / xhigh |
| 021 | Invoice Files section; remove invoice upload/versions (FR-13) — gated on ebilling routes | 2 | 🔲 [open] | 018, 020, 023 | — | sonnet / high |
| 024 | Shared EmptyState everywhere (FR-17) | 2 | 🔲 [open] | 018, 023 | — | sonnet / high |
| 028 | P2 deploy (SPA dev) + E2E | 2 | 🔲 [open] | 018, 020, 021, 022, 023, 024 | — (live) | sonnet / high |
| 030 | Live-metadata check of Information columns (owner-approved read) (FR-18a) | 3 | 🔲 [open] | 028 | — (live) | sonnet / high |
| 031 | ReadableColumns additions (UAC review) + Information section (FR-18) | 3 | 🔲 [open] | 028, 030 | Group C | opus / high |
| 032 | DataGrid persisted widths/sort + Reset columns (FR-19) | 3 | 🔲 [open] | 028 | Group C | sonnet / high |
| 034 | Messages refresh + new-message indicator (FR-21) | 3 | 🔲 [open] | 028 | Group C | sonnet / high |
| 033 | DataGrid Money sort + formatted export (FR-20) | 3 | 🔲 [open] | 028, 032 | Group D | sonnet / high |
| 035 | Message threads (FR-22) | 3 | 🔲 [open] | 028, 034 | Group D | sonnet / high |
| 036 | Matter events: task type + assignee (FR-23) — gated on ontology D-199 | 3 | 🔲 [open] | 028 | — | sonnet / high |
| 038 | P3 deploy (BFF allow-list via uac-r2, SPA dev) + E2E | 3 | 🔲 [open] | 028, 031, 032, 033, 034, 035, 036 | — (live) | sonnet / high |
| 040 | Workforce-contact tabs scoped by grants (FR-24) | 4 | 🔲 [open] | 038 | Group E | opus / high |
| 041 | Licensed user's first sign-in skips the profile form (FR-25) | 4 | 🔲 [open] | 038 | — (may edit `CallerPrincipalResolver.cs` with 043) | opus / high |
| 043 | Binder faults → 503 + SPA retry; ProjectPage 404 fold (FR-28) | 4 | 🔲 [open] | 038 | Group E | opus / high |
| 044 | no-mock-identity test warm-up (FR-29) | 4 | 🔲 [open] | 038 | Group E | sonnet / medium |
| 045 | Contact store log labels + test double + gate fix (FR-30) | 4 | 🔲 [open] | 038 | Group E | sonnet / high |
| 047 | Foreign-environment 403 → Warning / cooldown (FR-31c) | 4 | 🔲 [open] | 038 | Group E | sonnet / xhigh |
| 042 | Email-missing text + email rule alignment (FR-26, FR-27) | 4 | 🔲 [open] | 041 | Group F | sonnet / high |
| 048 | Teams mobile layout (FR-32) | 4 | 🔲 [open] | 040 | Group F | sonnet / high |
| 046 | acme fault: owner-approved checks + data fix (FR-31a/b) | 4 | 🔲 [open] | none (owner-gated) | — (live) | sonnet / high |
| 058 | P4 deploy (BFF via uac-r2, SPA dev, Teams package) + E2E | 4 | 🔲 [open] | 040, 041, 042, 043, 044, 045, 046, 047, 048 | — (live) | sonnet / high |
| 080 | Standards proposals (FR-34) — main session | Closing | 🔲 [open] | 018 | — (`.claude/`) | sonnet / high |
| 090 | Project wrap-up (`090-wrapup-project.poml`) | Closing | 🔲 [open] | all | — | sonnet / high |

**Total:** 34 tasks — P1: 8 · P2: 6 · P3: 8 · P4: 10 · Closing: 2.

## Parallel Execution Plan

```
Phase 1:
  Wave 1 (parallel, 2 agents): 001, 002 — goal-eligible: NO (2-task wave; 002 is authorization-sensitive)
  Wave 2 (sequential): 003 → 005 → 004 → 006 → 007 — same template files
  Wave 3 (sequential, live): 018 — owner go + uac-r2 BFF deploy
Phase 2:
  Wave 4 (parallel, 2 agents): 020, 022 — goal-eligible: NO (2-task wave)
  Wave 5 (sequential): 023 → 021 → 024 — shared DocumentLibrary.tsx; 021 waits on ebilling's files routes
  Wave 6 (sequential, live): 028
Phase 3:
  Wave 7 (sequential, live read): 030 — owner sees the commands first
  Wave 8 (parallel, 3 agents): 031, 032, 034 — goal-eligible: NO (031 changes the allow-list security boundary)
  Wave 9 (parallel, 2 agents): 033, 035 — goal-eligible: NO (2-task wave)
  Wave 10 (sequential): 036 — gated on ontology D-199
  Wave 11 (sequential, live): 038
Phase 4:
  Any time (owner-gated, live): 046
  Wave 12 (parallel, 5 agents): 040, 043, 044, 045, 047 — goal-eligible: NO (auth/access tasks)
  Wave 12b (sequential): 041 — may change CallerPrincipalResolver.cs, which 043 also changes
  Wave 13 (parallel, 2 agents): 042, 048 — goal-eligible: NO (2-task wave)
  Wave 14 (sequential, live): 058
Closing:
  080 (main session; may run any time after 018) → 090 last
```

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| A | 001, 002 | none | Shared lib `RecordCommandBar/` vs BFF `ExternalProjectDataEndpoints.cs` + `DocumentOperationsEndpoints.cs` | ✅ Yes |
| B | 020, 022 | 018 ✅ | Invoice widget/sections vs Document preview section | ✅ Yes |
| C | 031, 032, 034 | 030 ✅ (031), 028 ✅ | BFF `ExternalAccessModule.cs` + Information section vs shared `DataGrid` vs `RecordMessages.tsx` | ✅ Yes |
| D | 033, 035 | 032 ✅, 034 ✅ | Shared `DataGrid` vs `RecordMessages.tsx` | ✅ Yes |
| E | 040, 043, 044, 045, 047 | 038 ✅ | Widget registry + `ModuleEntitlementResolver` vs resolver + `me-client` + `ProjectPage` vs `no-mock-identity.test.tsx` (044 owns it) vs `ContactIdentityStore` + gate vs reconciliation job | ✅ Yes (separate BFF files; build-check after the wave). 041 runs after it (shares `CallerPrincipalResolver.cs` with 043) |
| F | 042, 048 | 041 ✅, 040 ✅ | `RegistrationPage` + profile form vs shell/Teams adapter/template CSS | ✅ Yes |

**How to execute a group:** confirm every prerequisite is complete, then send one message with one task-execute sub-agent per task (model = the task's `<model-tier>`). Wait for all of them, then verify the build before starting the next group (project-pipeline Step 5).

## Critical Path

001 → 003 → 005 → 004 → 006 → 007 → 018 → (P2) 020/022 → 023 → 021 → 024 → 028 → 030 → 031 → 038 → (P4) wave 12 → 058 → 090.

## High-Risk Items

- **Shared external-access BFF surface** (002, 031, 043, 045, 047), shared with uac-r2 and ebilling-r1: run `/conflict-check` before each PR, and deploy only via uac-r2.
- **ebilling timing** (020 live check, 021): P2 ships without them if ebilling slips.
- **Ontology D-199** (036) and the ontology agreement on `AssignTaskRecipientGate.cs` (045): both have escalation triggers.
- **Owner-gated live work** (030, 046, every deploy task): the owner sees the commands first, then approves with a rollback.
