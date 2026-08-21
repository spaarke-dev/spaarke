# Current Task State — `unified-access-control-r2`

> **Last Updated**: 2026-08-21 (by `task-execute`, after task 006)
> **Recovery**: read "Quick Recovery" first. History lives in
> [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) and the per-task `.poml` files.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **none active** — task 006 completed and committed |
| **Step** | n/a (between tasks) |
| **Status** | clean — working tree has no uncommitted changes |
| **Phase** | Phase 0 — enforcement remediation · **6 of 19 complete** (001 ✅ 003 ✅ 004 ✅ 006 ✅ 014 ✅ 019 ✅) |
| **Next Action** | Run **task 005** via `task-execute` with `projects/unified-access-control-r2/tasks/005-lift-read-ceiling.poml` (path verified). It is the last critical-path item in Phase 0 and now carries **two** binding obligations — see ⚠️ below |

### 🎉 FR-02 is now closed

Task 004 closed the `AuthorizationService` path; **task 006 closed the direct-call path**. A repo grep
for `userAccessToken: null` returns **zero production call-sites**. Both FR-02 and FR-05 are satisfied.

### ⚠️ Read before starting task 005

Task 005 carries **two** binding constraints, both filed as `<constraint>` elements in its POML:

1. **From task 003** — you MUST map Dataverse `AppendToAccess` → `AccessRights.AppendTo`. Miss it and
   `POST /api/office/save` is permanently 403 *while looking fixed*: the operation resolves, so the
   denial reads as legitimate `insufficient_rights` rather than the loud `unknown_operation` it replaced.
2. **From task 006** — **eleven of fourteen** capabilities on `GET /api/documents/{id}/permissions` are
   false for every caller until the ceiling lifts (only `CanPreview`, `CanReadMetadata`,
   `CanViewVersions` can be true). After 005 lands, verify those flags actually light up; a ceiling fix
   that does not surface there means the snapshot widened somewhere the endpoint does not read.

### Files Modified This Session

**All committed.** 7 commits on `work/unified-access-control-r2`, **none pushed**:

| Commit | Contents |
|---|---|
| `fe88d5339` | task 001 — 62-test characterization suite; first backfill of `tests/integration/auth/**` + csproj glob |
| `3e4523055` | accepted escalation — own-coverage obligations on tasks 007/012/013/015/016/017/018 |
| `9037c3a01` | task 003 — 4 policy keys + 15-test completeness gate; filed A-23 |
| `6393acba8` | wave P0-B — 014 (auth-mode cache key) + 019 (membership `["*"]`) |
| `ac9d78c85` | task 004 — `AuthorizationService` caller-scoped; `TokenHelper.ExtractBearerTokenOrNull` |
| `4a695ce02` | checkpoint — context handoff after task 004 |
| _(this task)_ | task 006 — caller-scoped `PermissionsEndpoints`; `GetCallerAccessAsync`; removed body-supplied `UserId` |

### Critical Context

Last verified state: **BFF 10,637 passed / 0 failed · ArchTests 36/36 · Core 45/45 · publish 43.65 MB
compressed incl. PDBs** (baseline 44.96, ceiling 60 — **0.00 MB delta**, no packages added). No
HIGH/CRITICAL CVE.

---

## Full State (Detailed)

### Alternative next waves

| Option | Tasks | Why |
|---|---|---|
| **Recommended** | **005** (lift the Read ceiling) | Last critical-path item in Phase 0; unblocks eleven capability flags AND the Office save route. Two binding obligations (above) |
| Independent | **002** (authorize document download) | A-1 — R1's January-2026 attack scenario, still open. Unblocks 012. `parallel-safe:false` |
| Independent | **010** (idempotent grant + revoke-all) | A-11 · `opus`/`xhigh`. Unblocks 017 and Phase 4's task 060 |

⚠️ **Phase 0 has no remaining co-schedulable pair.** `{014, 019}` was the only file-disjoint pair, and
006 was the only other `safe:true` task; it had no partner in its wave. Everything left clusters in the
four contended directories. Run these serially.

### Decisions made in task 006

| Decision | Rationale | Where |
|---|---|---|
| `AuthorizationService.GetCallerAccessAsync` — **no default** on the token param | A-4's root cause was the `= null` **default** on `IAccessDataSource.GetUserAccessAsync`, not a missing null check. A mandatory positional param can't be called without stating intent — task 004's `required` forcing function, in the shape a method signature allows | `notes/task-006-capability-rights-mapping.md` §3 |
| `AuthorizeAsync` routes through it → **one** member touches `_accessDataSource` | Makes "capabilities derive from the same snapshot as enforcement" grep-checkable, not asserted. Pinned by a test comparing both paths' argument tuples | same, §3 |
| Snapshot accessor, not 14 × `AuthorizeAsync` | The batch route would otherwise be 1,400 rule-chain evaluations per 100-doc request — and that route exists specifically to avoid N+1 | same, §3 |
| No-access = **200 + all-false**, not 403 | FR-05's wording presupposes a body; the batch route can't express per-document denial as a status code; all-false doesn't distinguish inaccessible from nonexistent | same, §4 |
| **Removed `BatchPermissionsRequest.UserId`** | `DataverseAccessDataSource.cs:184-199` treats `userId` and `userAccessToken` as INDEPENDENT — a body-supplied id would query a different principal under the caller's OBO token and write task 014's cache key under the **victim's** oid | same, §5 |
| Method NOT added to `IAuthorizationService` | It's a testing seam for `AuthorizeAsync`; widening it forces every mock to change for no benefit (ADR-010). The endpoint injects the concrete type, as `DocumentAuthorizationFilter.cs:26` already does | same, §3 |

### Carried forward — read before ANY Phase 0 task

| Item | Detail |
|---|---|
| **KEEP path** | Access-control tests → `tests/integration/auth/**`. POMLs still name `tests/unit/Sprk.Bff.Api.Tests/AccessControl/`, which does not exist and is not a KEEP path |
| **Vacuity trap (bit task 006)** | With the REAL `IAccessDataSource` the offline host fails closed to `None`, so "all capabilities false" is true both before AND after a caller-scoping fix. Endpoint tests asserting only all-false pass **vacuously**. Fix: substitute a caller-scoped double that CAN answer true — see `CallerScopedAccessTestFixture`. Then **empirically verify** by reverting the fix and confirming the tests fail |
| **`/api/v1/external` fixture trap** | `AuthPolicies.ExternalCollaboration` pins the `Ciam` + `Bearer` schemes, bypassing `FakeAuthHandler` → 500. Use `ExternalCollaborationTestFixture` |
| **Bash cwd drift** | A bare `cd` in one Bash call persists and breaks later relative paths. Prefix with `cd /c/code_files/spaarke-wt-unified-access-control-r2` |
| **Own-coverage obligation** | Tasks **007, 012, 013, 015, 016, 017, 018** have no pinned baseline — each supplies its own tests |
| **`data-mutation` KEEP path** | Still un-backfilled — zero compiled files, globbed by no csproj. A write-path test placed there will silently not run |

### Open items requiring owner attention

| # | Item |
|---|---|
| 1 | **019's product-semantics question**: `includeRelated: true` on `LookupUserMembership` is a logged-warning no-op; the flag is visible in the Playbook Builder canvas and does nothing. No playbook sets it today (verified), so it is latent. Real fix = a `relatedEntities: string[]` config field, or remove the flag |
| 2 | **A-23**: `AddOfficeDocumentAccessFilter` is a second orphaned filter alongside A-15 → **task 018** deletes it |
| 3 | **I-4**: `sdap:auth:*` keys carry no tenant segment. Moot while single-tenant; **task 035** should design its per-user cache tenant-aware from the start |
| 4 | Stale "task 054 implements" comments in `MembershipEndpoints.cs` + `IMembershipResolverService.cs` → **task 015** |
| 5 | **Nothing pushed.** 7 commits are local-only; no PR exists for this branch |
| 6 | **NEW (006)**: `/api/documents/{id}/permissions` and `/permissions/batch` have **zero clients** — verified by two independent greps. The endpoint has been shipping a disclosure nothing consumed. Fixing was chosen over retiring because tasks 065–067 need this surface; if that changes, retirement is a legitimate option for the wrap-up |
| 7 | **NEW (006)**: `TypedResults.Unauthorized()` returns a bare 401, not ProblemDetails (ADR-019). **Pre-existing**, not introduced by 006, and deliberately not fixed there — changing the 401 shape touches the authentication-floor characterizations task 001 pinned. Candidate for the wrap-up |

### Decisions carried in from design (unchanged)

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

### Blocking prerequisites (before Phase 4 live-dev acceptance)

- `prvActOnBehalfOfAnotherUser` on the BFF application user — **no runbook records this grant today**
- BFF app user stays **Org-scoped** (impersonated privileges = app user ∩ impersonated user)
- A **non-admin test user** in the Operations subtree with no Global-read role
- BU restructure + user migration + record re-homing (UAT)

### Hard gates

| Gate | Rule |
|---|---|
| **NFR-04** negative canary | Impersonated low-privilege read MUST return a strict subset AND **strictly fewer** rows than app-only. Equality = impersonation inert → build fails. Task 001 pinned the API-level fail-closed guard; the row-count comparison is task 034's and needs a live tenant |
| **NFR-05** role-depth assertion | No security role may reach the `Secure Projects` BU |
| **NFR-07** | ⚠️ Partially satisfied — **9 of 20** findings pinned, 1 partial, 10 owned by their fix tasks per the accepted escalation |
| **FR-07** delegation | Must ship BEFORE the PCF "+ User" button (task 065). Task 001 pinned the hole: `/grant` reaches handler validation for an arbitrary authenticated caller |

### Coordination

`/conflict-check` before **every** BFF PR. Shares the external-access surface with
`spaarke-SPA-external-access-platform-r1/r2` and `teams-app-r1` (shipped) and `SPA-r3` (draft).
All `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, `Spaarke.Core/Auth/**` and
`DataverseWebApiService.cs` tasks are `parallel-safe:false`. Tasks 030/031/040 edit `.claude/**` →
**main-session-only**.

### Note on suite health

An early full run of `Sprk.Bff.Api.Tests` had **1 failure**; every run since has been clean
(10,598 → 10,617 → 10,622 → 10,625 → 10,637 passed, 0 failed). The failure was not reproducible and its
identity was lost to log truncation — a pre-existing flake, not a regression. Watch for recurrence.
