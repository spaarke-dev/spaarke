# Current Task — `unified-access-control-r2`

> **Purpose**: active-task state for context recovery. Tracks ONLY the active task —
> history lives in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) and the per-task `.poml` files.
> **Last updated**: 2026-08-21

---

## Active Task

| Field | Value |
|---|---|
| **Task** | 004 (next) |
| **Status** | not-started |
| **Phase** | Phase 0 — enforcement remediation |
| **Next action** | Run **004** — all three deps (001, 003, 014) are ✅. Step 1 (call-site classification) is **already done**: [`notes/task-004-callsite-classification.md`](notes/task-004-callsite-classification.md) |

**Phase 0 complete: 001 ✅ · 003 ✅ · 014 ✅ · 019 ✅** (4 of 19)

### Task 004 — pre-loaded context

`opus` @ `xhigh`, `parallel-safe:false`. Highest blast radius in Phase 0: makes `AuthorizationService`
evaluate as the **caller** rather than the application (A-2 / FR-02).

Classification already established (read-only, done 2026-08-21):

- **Zero app-only consumers.** All six call-sites run inside an HTTP request →
  **POML Step 3 has no work** (nothing to route to an app-only entry point).
- "Missing token → Deny" is therefore unambiguous; the POML's ambiguous-classification escalation
  does **not** fire on the current call graph.
- `OfficeDocumentAccessFilter:132` is a caller-scoped consumer but **orphaned** (A-23) — leave it;
  task 018 deletes it. Do not plumb a token into dead code.
- `ChatDocumentEndpoints:911` is the only non-filter consumer and injects the **interface**
  `IAuthorizationService`, not the concrete type the filters use. Any signature change must keep
  that injection working.
- ⚠️ Trap: the three AI filters declare a field *named* `_authorizationService` whose type is
  `IAiAuthorizationService`. A grep on the field name makes them look like consumers. They are not —
  they already pass the caller bearer, which is why they were A-19's evidence base.

After 004: **{005, 006}** unblock (006 is `parallel-safe:true`).

## Project State

- ✅ Investigation complete — 10 passes, all claims cited `file:line`
- ✅ `design.md` / `spec.md` / `notes/design-register.md` written and owner-reviewed
- ✅ Documentation drift corrected (5 files) · registered in `projects/INDEX.md`
- ✅ 58 tasks generated · validator PASS (57 clean, 0 errors, 1 benign WARN on 090)
- ✅ **Task 001 complete** — 62 tests green at `tests/integration/auth/UnifiedAccessControl/`
- 🔔 **Escalation open** — 10 of 20 Phase 0 findings have no characterization coverage; owner decision
  needed on approach. See [`notes/task-001-untestable-findings.md`](notes/task-001-untestable-findings.md)

## Carried forward from task 001 (read before any Phase 0 task)

| Item | Detail |
|---|---|
| **KEEP path** | Access-control tests belong at `tests/integration/auth/**` (ADR-038 §2 security-auth), NOT `tests/unit/Sprk.Bff.Api.Tests/`. Task 001 added the csproj glob; later POMLs may still name the wrong path — prefer the KEEP path |
| **`/api/v1/external` fixture trap** | `AuthPolicies.ExternalCollaboration` pins the `Ciam` + `Bearer` schemes, bypassing `FakeAuthHandler`. With the shared fixtures that group returns **500**, which makes any "not 403" assertion pass **vacuously**. Use `ExternalCollaborationTestFixture` |
| **Flipping contract** | Each of the 20 `Characterization_` tests names its finding (A-nn) and its flipping task in a `FLIPPED BY` doc comment. Tasks 002/003/004/005/006/008/010/011/014 must invert their own, not delete it |
| **Own-coverage obligation** | Tasks **007, 012, 013, 015, 016, 017, 018, 019** have NO pinned baseline. Recommended: extract a query-builder seam inside the fix task (forbidden in 001, legitimate there) |
| **`data-mutation` KEEP path** | Still un-backfilled — zero compiled files, globbed by no csproj. A write-path test placed there silently will not run |

## Decisions carried into execution

| Decision | Where |
|---|---|
| Derived access default-on; **Secure is the veto** | design §4.5 |
| Level precedence = **highest wins**; vetoes evaluated AFTER the max | design §4.5 |
| **"No Access" is a veto, never a level** | spec FR-23 |
| Core records need direct grants; child records inherit **1 hop** via denormalized core ancestor | design §4.3 |
| **Matter does NOT inherit from Project** — both are core | design §4.3 |
| Type 1 root sets = Dataverse's real answer via the existing `MSCRMCallerID` seam | spec FR-20 |
| Secure Project = Secure BU + service-account owner + **share-only** | design §5.1 |
| BU restructure is **UAT/environment work, NOT a project task** | spec § UAT & Environment Setup |

## Blocking prerequisites (before Phase 4 live-dev acceptance)

- `prvActOnBehalfOfAnotherUser` on the BFF application user — **no runbook records this grant today**
- BFF app user stays **Org-scoped** (impersonated privileges are the intersection of app user × impersonated user)
- A **non-admin test user** in the Operations subtree with no Global-read role
- BU restructure + user migration + record re-homing (UAT)

## Hard gates

| Gate | Rule |
|---|---|
| **NFR-04** negative canary | Impersonated low-privilege read MUST return a strict subset AND **strictly fewer** rows than app-only. Equality = impersonation inert → build fails. Task 001 pinned the API-level fail-closed guard; the row-count comparison is task 034's and needs a live tenant |
| **NFR-05** role-depth assertion | No security role may reach the `Secure Projects` BU |
| **NFR-07** | ✅ Satisfied for 9 of 20 findings — see the escalation note |
| **FR-07** delegation | Must ship BEFORE the PCF "+ User" button (task 065). Task 001 pinned the current hole: `/grant` reaches handler validation for an arbitrary authenticated caller |

## Coordination

`/conflict-check` before **every** BFF PR. Shares the external-access surface with
`spaarke-SPA-external-access-platform-r1/r2` and `teams-app-r1` (shipped) and `SPA-r3` (draft).
All `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, `Spaarke.Core/Auth/**` and
`DataverseWebApiService.cs` tasks are `parallel-safe:false`. The three ADR-amendment tasks
(030/031/040) edit `.claude/**` → **main-session-only**.

## Note on suite health

Three full runs of `Sprk.Bff.Api.Tests` (10,695 tests): run 1 had **1 failure**, runs 2 and 3 were
clean (10,598 passed). The failure was not reproducible and its identity was lost to log truncation —
i.e. a pre-existing flake, not a regression from task 001. Worth watching if it recurs.
