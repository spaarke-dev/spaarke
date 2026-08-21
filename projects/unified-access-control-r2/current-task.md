# Current Task — `unified-access-control-r2`

> **Purpose**: active-task state for context recovery. Tracks ONLY the active task —
> history lives in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) and the per-task `.poml` files.
> **Last updated**: 2026-08-21

---

## Active Task

| Field | Value |
|---|---|
| **Task** | none |
| **Status** | not-started |
| **Phase** | Phase 0 — enforcement remediation |
| **Next action** | Start the first Phase 0 task per `tasks/TASK-INDEX.md` (characterization suite first — spec NFR-07) |

## Project State

- ✅ Investigation complete — 10 passes, all claims cited `file:line` ([`notes/investigation/`](notes/investigation/))
- ✅ `design.md` written and owner-reviewed
- ✅ `spec.md` written — 32 FRs / 7 NFRs / 6 phases, all open questions closed 2026-08-21
- ✅ `notes/design-register.md` — consolidated register (§A–I)
- ✅ Documentation drift corrected (5 files)
- ✅ Registered in `projects/INDEX.md` (BFF=Y, Skill-directives=Y)
- ✅ Committed + pushed — `424b8e0bd` on `work/unified-access-control-r2`
- ⬜ Task files generated → see `tasks/TASK-INDEX.md`
- ⬜ Execution not started

## Decisions carried into execution

| Decision | Where |
|---|---|
| Derived access default-on; **Secure is the veto** | design §4.5 |
| Level precedence = **highest wins**; vetoes evaluated AFTER the max | design §4.5 |
| **"No Access" is a veto, never a level** — under highest-wins `max()` would ignore it and the ethical wall would fail silently | spec FR-23 |
| Core records need direct grants; child records inherit **1 hop** via denormalized core ancestor | design §4.3 |
| **Matter does NOT inherit from Project** — both are core | design §4.3 |
| Type 1 root sets = Dataverse's real answer via the existing `MSCRMCallerID` seam | spec FR-20 |
| Secure Project = Secure BU + service-account owner + **share-only** | design §5.1 |
| BU restructure is **UAT/environment work, NOT a project task** | spec § UAT & Environment Setup |

## Blocking prerequisites (before Phase 4 live-dev acceptance)

- `prvActOnBehalfOfAnotherUser` on the BFF application user — **no runbook records this grant today**
- BFF app user stays **Org-scoped** (impersonated privileges are the intersection of app user × impersonated user)
- A **non-admin test user** in the Operations subtree with no Global-read role — isolation is not verifiable from an admin account
- BU restructure + user migration + record re-homing (UAT)

## Hard gates

| Gate | Rule |
|---|---|
| **NFR-04** negative canary | Impersonated low-privilege read MUST return a strict subset AND strictly fewer rows than app-only. **Equality = impersonation inert → build fails** |
| **NFR-05** role-depth assertion | No security role may reach the `Secure Projects` BU. A role edit that re-opens secure projects must fail the build, not ship |
| **NFR-07** | Characterization suite built BEFORE Phase 1 changes behaviour — the current baseline is near-zero |
| **FR-07** delegation | Must ship BEFORE the PCF "+ User" button, or a read-only user gains a one-click path to Full Access on a confidential matter |

## Coordination

`/conflict-check` before **every** BFF PR. Shares the external-access surface with `spaarke-SPA-external-access-platform-r1/r2` and `teams-app-r1` (both shipped) and `SPA-r3` (draft — assumes the dual-plane model, must be notified). All `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, `Spaarke.Core/Auth/**` and `DataverseWebApiService.cs` tasks are `parallel-safe:false`. The three ADR-amendment tasks edit `.claude/**` → **main-session-only**.
