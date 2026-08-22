# Current Task State — `unified-access-control-r2`

> **Last Updated**: 2026-08-22 (by `context-handoff`, after task 010)
> **Recovery**: read "Quick Recovery" first. History lives in
> [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) and the per-task `.poml` files.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **none active** — task 010 completed, committed and pushed |
| **Step** | n/a (between tasks) |
| **Status** | clean — working tree has no uncommitted changes |
| **Phase** | Phase 0 — enforcement remediation · **9 of 19 complete** (001 ✅ 002 ✅ 003 ✅ 004 ✅ 005 ✅ 006 ✅ 010 ✅ 014 ✅ 019 ✅) |
| **PR** | **[#812](https://github.com/spaarke-dev/spaarke/pull/812)** — draft, all work pushed |
| **Next Action** | Run **task 008** (`008-*.poml` — **verify the filename**, POML paths in this project have been wrong repeatedly) via the `task-execute` skill. See "Why 008 next" below |

### Why 008 next (not the first 🔲)

The first pending id is **007** (grant expiry, A-5), but **008 is the gate**: FR-07's delegation rule
("you may grant if you have Write on the record") **must ship before the PCF "+ User" button** (task 065),
or that button is one-click privilege escalation on a confidential matter. Task 001 pinned the hole —
`/grant` currently reaches handler validation for an *arbitrary authenticated caller*.

Reasonable alternatives: **007** (expiry is written but never read — a promise-shaped no-op in the UI),
**017** (newly unblocked by 010, same file as 010 — see its new binding constraint), **012** (unblocked
by 002).

### Last verified state

**BFF 10,676 passed / 0 failed** (TRX confirms none) · **ArchTests 36/36** · **Core 45/45** ·
publish **43.66 MB** compressed incl. PDBs (baseline 44.96, ceiling 60 — +0.01 across the whole session,
no packages added).

---

## Session summary — what was accomplished

Nine Phase 0 tasks, all committed to PR #812. **FR-01 → FR-05, FR-09 and FR-13 are closed**, plus part
of FR-17 and NFR-07.

| Task | What it closed |
|---|---|
| 001 | 62-test characterization suite; **first ever backfill** of the `tests/integration/auth/**` KEEP path (it had zero compiled files and was globbed by no csproj) |
| 002 | **R1's January-2026 attack scenario** — `/download` had no per-document filter. Also closed `/content`, the same hole by another URL |
| 003 | 4 missing `OperationAccessPolicy` keys + a source-scanning completeness gate |
| 004 | `AuthorizationService` evaluates **as the caller** |
| 005 | The `AccessRights.Read` ceiling — `RetrievePrincipalAccess` replaces a "can I read it → therefore Read" probe |
| 006 | `PermissionsEndpoints` caller-scoped; **FR-02's criterion closed** (zero `userAccessToken: null` in production) |
| 010 | **A-11, ranked #1 of 13** — `/grant` upserts, `/revoke` sweeps every row on the logical key |
| 014 | Auth-mode segment in the cache key (`sp`/`obo`) |
| 019 | `LookupUserMembership` no longer sends `["*"]` |

### Method that kept paying off — apply it to every remaining task

**Verify tests discriminate by breaking the fix and watching them fail.** Done on every task this
session, and it caught real gaps each time:

| Perturbation | Failures |
|---|---|
| Revert the single-doc token (006) → then the batch token | 2 → 3 |
| Transpose `AppendToAccess → Append` (005) | 4 of 15 |
| Remove the `/content` filter (002) | 2 of 17 |
| Drop `_sprk_contact_value eq null` (010) | 3 of 22 |
| Reduce revoke to the named row (010) | 2 of 22 |

**Capture failing-test identity with TRX**, not `-v q`:
`dotnet test … --logger "trx;LogFileName=t.trx"`, then parse `outcome="Failed"`. This named a real
regression in task 010 that `-v q` would have rendered indistinguishable from the known flake.

---

## Full State (Detailed)

### Decisions made in task 010 (most recent)

| Decision | Rationale |
|---|---|
| Logical key = `(root) × (Contact XOR Organization)` | A row may carry BOTH — the org is the contact's **firm**, association metadata, NOT identity. Contact wins, or a person grant and an org grant on the same root collide and could revoke each other |
| `_sprk_contact_value eq null` in the org filter | Without it, revoking one firm's grant sweeps every member's **personal** grant. Mirrors the read side term for term — write/read disagreement about "the same grant" **is** A-11 |
| Survivor election = `OrderBy(id).First()` | Concurrent racers MUST elect the same survivor or they deactivate each other and the grant vanishes — worse than the bug. Stable and clock-independent (`createdon` can tie) |
| Underivable key → **fail loudly, deactivate nothing** | The POML flags it as an escalation, but the task's own ADR-003 constraint answers it: siblings that cannot be queried cannot be guaranteed absent, so success is forbidden |
| Discard rows with `Id == Guid.Empty` | **A real defect caught by the full suite** — the upsert adopted an unaddressable row as "the existing grant" and returned an empty id: a silent no-op reported as success. Fixed in production, not by adjusting the test stub |

### Carried forward — read before ANY remaining task

| Item | Detail |
|---|---|
| **POML paths are unreliable** | Tasks 002/005/006 all named test paths that do not exist, and one handoff guessed a wrong filename. **Verify every path before acting on it** |
| **KEEP paths** | Access-control → `tests/integration/auth/**`; pure domain logic → `tests/unit/domain/**`. Both globbed into `Sprk.Bff.Api.Tests.csproj` |
| **Vacuity trap** | With the REAL `IAccessDataSource` the offline host fails closed to `None`, so "all denied" is true before AND after a fix. Substitute a double that CAN answer yes, then break the fix to prove the tests bite |
| **Doc comments in this area lie** | Four cases found: `CachedAccessDataSource` claimed `AuthorizationService` was app-only; `DataverseAccessDataSource` claimed Dataverse "enforces Write/Delete separately"; a task-001 test claimed 005 would flip it (**following that would have allowed upload to a read-only caller**); `RetrievePrincipalAccess` was documented as used but had zero call sites |
| **`/api/v1/external` fixture trap** | `AuthPolicies.ExternalCollaboration` pins `Ciam` + `Bearer`, bypassing `FakeAuthHandler` → 500. Use `ExternalCollaborationTestFixture` |
| **Bash cwd drift** | A bare `cd` persists across calls. Prefix with `cd /c/code_files/spaarke-wt-unified-access-control-r2` |
| **CI bot pushes** | A `dotnet format` bot auto-commits to the branch. **Pull/rebase before pushing** — one push was rejected non-fast-forward |
| **Own-coverage obligation** | Tasks **007, 012, 013, 015, 016, 017, 018** have no pinned baseline — each supplies its own tests |
| **`data-mutation` KEEP path** | Still un-backfilled — zero compiled files. A write-path test placed there silently will not run |

### Open items requiring owner attention

| # | Item |
|---|---|
| 1 | **PR #812 workflow runs are `action_required`** — someone must approve them in the GitHub UI. `SDAP CI` passed and the CI Router's **Tier 1 blocking jobs all passed**; the red rows were runs cancelled by supersession, not code failures |
| 2 | **Decision needed (002)**: download enforcement requires **Read**, but task 006's `CanDownload` capability requires **Write**. Benign in effect but it IS the divergence FR-05 criterion 5 exists to prevent. Option A: re-point `CanDownload`. Option B: move enforcement to Write on all three routes. Product call — `notes/task-002-download-authorization.md` §4 |
| 3 | **Needs its own task (002)**: `preview-url`, `view-url`, `office`, `preview` on `/api/documents` still have no per-document filter. They mint **URLs** rather than stream bytes — arguably worse, since a URL outlives the request |
| 4 | **Untested against a real tenant (005)**: `RetrievePrincipalAccess`'s URL form and OBO availability. Filed on **task 034**. If `RPA-FALLBACK` fires in production, the Read ceiling is silently back |
| 5 | **Duplicates remain invisible (010)** to the participation surface until Phase 1 replaces the read-side `GroupBy` collapse |
| 6 | **019's product-semantics question**: `includeRelated: true` is a logged-warning no-op; visible in the Playbook Builder canvas, does nothing |
| 7 | **A-23**: `AddOfficeDocumentAccessFilter` is a second orphaned filter → **task 018** |
| 8 | **I-4**: `sdap:auth:*` keys carry no tenant segment → **task 035** |
| 9 | Stale "task 054 implements" comments in `MembershipEndpoints.cs` + `IMembershipResolverService.cs` → **task 015** |
| 10 | `TypedResults.Unauthorized()` returns a bare 401, not ProblemDetails (ADR-019). Pre-existing; wrap-up candidate |
| 11 | **Suite-health caveat**: one full run during task 005 reported 1 failure that never reproduced; identity not captured. Not attributed to any change, not exonerated. Use the TRX technique if it recurs |

### Constraints filed on future tasks (do not lose these)

| Task | Constraint from |
|---|---|
| **005** ✅ done | 003 (`AppendToAccess`), 006 (verify capabilities light up) |
| **017** | **010** — MUST NOT reduce the revoke sweep to a single row or weaken org/person isolation; also assess whether SPE removal should follow the logical key rather than `request.ContactId` (for an **org** revoke those are different sets) |
| **032** | 006 (one-access-path invariant), 005 (per-principal derivation + `AppendTo`; the RPA `Target` hard-codes `sprk_documents`) |
| **034** | 005 (verify `RetrievePrincipalAccess` live; grep logs for `RPA-FALLBACK`) |
| **007/012/013/015/016/017/018** | 001 (own-coverage obligation) |

### Decisions carried in from design (unchanged)

| Decision | Where |
|---|---|
| Derived access default-on; **Secure is the veto** | design §4.5 |
| Level precedence = **highest wins**; vetoes AFTER the max | design §4.5 |
| **"No Access" is a veto, never a level** | spec FR-23 |
| Core records need direct grants; child records inherit **1 hop** via denormalized core ancestor | design §4.3 |
| **Matter does NOT inherit from Project** — both are core | design §4.3 |
| Type 1 root sets = Dataverse's real answer via the existing `MSCRMCallerID` seam | spec FR-20 |
| Secure Project = Secure BU + service-account owner + **share-only** | design §5.1 |
| BU restructure is **UAT/environment work, NOT a project task** | spec § UAT & Environment Setup |

### Blocking prerequisites (before Phase 4 live-dev acceptance)

- `prvActOnBehalfOfAnotherUser` on the BFF application user — **no runbook records this grant today**
- Whatever `RetrievePrincipalAccess` requires on the app-only path (read `systemuser` + the target) — task 005
- BFF app user stays **Org-scoped** (impersonated privileges = app user ∩ impersonated user)
- A **non-admin test user** in the Operations subtree with no Global-read role
- BU restructure + user migration + record re-homing (UAT)

### Hard gates

| Gate | Rule |
|---|---|
| **NFR-04** negative canary | Impersonated low-privilege read MUST return a strict subset AND **strictly fewer** rows than app-only. Equality = impersonation inert → build fails. Task 034 also owns the RPA live verification |
| **NFR-05** role-depth assertion | No security role may reach the `Secure Projects` BU |
| **NFR-07** | ⚠️ Partial — 9 of 20 findings pinned, 1 partial, 10 owned by their fix tasks per the accepted escalation |
| **FR-07** delegation | Must ship BEFORE the PCF "+ User" button (task 065) — **this is why 008 is the recommended next task** |

### Coordination

`/conflict-check` before **every** BFF PR. Shares the external-access surface with
`spaarke-SPA-external-access-platform-r1/r2` and `teams-app-r1` (shipped) and `SPA-r3` (draft).
All `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, `Spaarke.Core/Auth/**` and
`DataverseWebApiService.cs` tasks are `parallel-safe:false`. Tasks 030/031/040 edit `.claude/**` →
**main-session-only**. **Phase 0 has no remaining co-schedulable pair** — run serially.
Last master check: 1 docs-only commit ahead, **zero overlap**.
