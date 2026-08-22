# Current Task State — `unified-access-control-r2`

> **Last Updated**: 2026-08-21 (by `task-execute`, after task 005)
> **Recovery**: read "Quick Recovery" first. History lives in
> [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) and the per-task `.poml` files.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **none active** — task 005 completed and committed |
| **Step** | n/a (between tasks) |
| **Status** | clean — working tree has no uncommitted changes |
| **Phase** | Phase 0 — enforcement remediation · **7 of 19 complete** (001 ✅ 003 ✅ 004 ✅ 005 ✅ 006 ✅ 014 ✅ 019 ✅) |
| **Next Action** | Run **task 002** via `task-execute` with `projects/unified-access-control-r2/tasks/002-authorize-document-download.poml` (verify the filename first — the POML paths in this project have been wrong before). It is R1's January-2026 attack scenario, still open, and unblocks 012 |

### The Phase 0 critical path is complete

`001 → {003, 014} → 004 → {005, 006}` is done. **FR-01 through FR-05 and FR-13 are closed.** What
remains in Phase 0 are independent fixes, not chain links.

### Critical Context

Last verified: **BFF 10,657 passed / 0 failed · ArchTests 36/36 · Core 45/45 · publish 43.65 MB
compressed incl. PDBs** (baseline 44.96, ceiling 60 — **0.00 MB delta** across both of the last two
tasks; no packages added).

⚠️ **Suite-health caveat, unresolved.** The first full run after task 005 reported **1 failure**; the
three runs after it were clean, and the five touched suites were 6/6 clean. The identity was **not
captured** because `-v q` suppresses it. Matches the pre-existing unreproducible flake — **not attributed
to task 005, and not exonerated either.** Next time it recurs, capture it:
`dotnet test … --logger "trx;LogFileName=full.trx"`, then parse `outcome="Failed"` out of the TRX.

### Files Modified This Session

**All committed.** 9 commits on `work/unified-access-control-r2`, **none pushed**:

| Commit | Contents |
|---|---|
| `fe88d5339` | task 001 — 62-test characterization suite; first backfill of `tests/integration/auth/**` + csproj glob |
| `3e4523055` | accepted escalation — own-coverage obligations on tasks 007/012/013/015/016/017/018 |
| `9037c3a01` | task 003 — 4 policy keys + 15-test completeness gate; filed A-23 |
| `6393acba8` | wave P0-B — 014 (auth-mode cache key) + 019 (membership `["*"]`) |
| `ac9d78c85` | task 004 — `AuthorizationService` caller-scoped; `TokenHelper.ExtractBearerTokenOrNull` |
| `4a695ce02` | checkpoint — context handoff after task 004 |
| `93b506a66` | task 006 — caller-scoped `PermissionsEndpoints`; `GetCallerAccessAsync`; removed body-supplied `UserId` |
| `ab5ce1d05` | checkpoint — record task 006 sha |
| _(task 005)_ | Read ceiling lifted — `RetrievePrincipalAccess` + `DataverseAccessRightsMapper` |

---

## Full State (Detailed)

### Next waves

| Option | Task | Why |
|---|---|---|
| **Recommended** | **002** — authorize document download (FR-01 / A-1) | The last High-severity open disclosure in Phase 0: `GET /api/documents/{id}/download` has **no per-document filter** at all, so any authenticated caller streams any document by GUID. Unblocks 012. `parallel-safe:false` |
| High value | **010** — idempotent grant + revoke-all (A-11) | `opus` / `xhigh`. Unblocks 017 **and** Phase 4's task 060 (POA seam consolidation), so it is the longest remaining dependency chain |
| Cluster | **007 / 008 / 009** | Grant expiry, delegation (**must precede the PCF "+ User" button**, task 065), external To Do scope-check |

⚠️ **Phase 0 has no remaining co-schedulable pair.** `{014, 019}` was the only file-disjoint pair;
everything left sits in the four contended directories. Run serially.

### Decisions made in task 005

| Decision | Rationale |
|---|---|
| `RetrievePrincipalAccess` first, **old probe retained as fallback** | The deleted comment claimed RPA "may not be available" under OBO. Unverified (zero call sites — nothing ever exercised it) and unverifiable offline. The composition cannot regress: worst case is exactly today's behaviour. Failures log **`RPA-FALLBACK`** so a silent re-cap at Read is visible |
| `null` vs `AccessRights.None` distinguished | `None` = authoritative "Dataverse says no rights"; `null` = "no answer, fall back". Collapsing them would make a permissions outage indistinguishable from a legitimate denial |
| Pure mapping extracted to `internal static DataverseAccessRightsMapper` + `InternalsVisibleTo` | FR-04 criterion 5 asks for the no-over-grant property "asserted by test with a **mocked Dataverse answer**" — impossible while the logic was private (ban B8) behind an HTTP call (ban B1). Not scope creep: the criterion requires the seam |
| Renamed the mis-framed task-001 characterization | It was doc-commented "FLIPPED BY: task 005". **Following that would have allowed upload to a read-only caller.** It hands the RULE a Read-only snapshot; the ceiling lived in the DATA SOURCE |

### Carried forward — read before ANY Phase 0 task

| Item | Detail |
|---|---|
| **POML paths are unreliable** | Task 005's POML named `tests/unit/Spaarke.Core.Tests/Auth/…` (does not exist); task 006's named `tests/unit/Sprk.Bff.Api.Tests/AccessControl/…` (does not exist); the task-006 handoff guessed a wrong filename. **Verify every path before acting on it** |
| **KEEP paths** | Access-control tests → `tests/integration/auth/**`; pure domain logic → `tests/unit/domain/**`. Both are globbed into `Sprk.Bff.Api.Tests.csproj` |
| **Vacuity trap** | With the REAL `IAccessDataSource` the offline host fails closed to `None`, so "all capabilities false" is true before AND after a caller-scoping fix. Substitute a double that CAN answer true, then **verify empirically** by reverting the fix and confirming failures. Done twice now (tasks 006, 005) and it caught real gaps both times |
| **Doc comments in this area lie** | Three separate cases: `CachedAccessDataSource` claimed `AuthorizationService` was app-only (false since 004); `DataverseAccessDataSource` claimed Dataverse "will enforce Write/Delete separately" (false on the SPA surface); a task-001 test claimed task 005 would flip it (would have been a regression). **Verify claims against code before relying on them** |
| **`/api/v1/external` fixture trap** | `AuthPolicies.ExternalCollaboration` pins the `Ciam` + `Bearer` schemes, bypassing `FakeAuthHandler` → 500. Use `ExternalCollaborationTestFixture` |
| **Bash cwd drift** | A bare `cd` in one Bash call persists and breaks later relative paths. Prefix with `cd /c/code_files/spaarke-wt-unified-access-control-r2` |
| **Own-coverage obligation** | Tasks **007, 012, 013, 015, 016, 017, 018** have no pinned baseline — each supplies its own tests |
| **`data-mutation` KEEP path** | Still un-backfilled — zero compiled files. A write-path test placed there will silently not run |

### Open items requiring owner attention

| # | Item |
|---|---|
| 1 | **019's product-semantics question**: `includeRelated: true` on `LookupUserMembership` is a logged-warning no-op; visible in the Playbook Builder canvas, does nothing. No playbook sets it today |
| 2 | **A-23**: `AddOfficeDocumentAccessFilter` is a second orphaned filter → **task 018** deletes it |
| 3 | **I-4**: `sdap:auth:*` keys carry no tenant segment → **task 035** should design its per-user cache tenant-aware from the start |
| 4 | Stale "task 054 implements" comments in `MembershipEndpoints.cs` + `IMembershipResolverService.cs` → **task 015** |
| 5 | **Nothing pushed.** 9 commits are local-only; no PR exists for this branch |
| 6 | `/api/documents/{id}/permissions` + `/permissions/batch` have **zero clients** (two independent greps). Fixed rather than retired because tasks 065–067 need the surface; retirement stays a legitimate wrap-up option |
| 7 | `TypedResults.Unauthorized()` returns a bare 401, not ProblemDetails (ADR-019). **Pre-existing**; not fixed in 006 because it would touch task 001's authentication-floor pins. Wrap-up candidate |
| 8 | **NEW (005)**: `RetrievePrincipalAccess` is **untested against a real tenant** — its URL form and OBO availability can only be settled live. Filed as a constraint on **task 034**, which already needs a tenant. If `RPA-FALLBACK` fires in production the Read ceiling is silently back |
| 9 | **NEW (005)**: the RPA `Target` hard-codes `sprk_documents({id})`. Not a regression (the replaced probe did too), but Phase 1's evaluator answers for ANY entity → filed as a constraint on **task 032** |

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
- **NEW**: the BFF app user also needs whatever `RetrievePrincipalAccess` requires on the app-only path
  (read `systemuser` + read the target) — task 005
- BFF app user stays **Org-scoped** (impersonated privileges = app user ∩ impersonated user)
- A **non-admin test user** in the Operations subtree with no Global-read role
- BU restructure + user migration + record re-homing (UAT)

### Hard gates

| Gate | Rule |
|---|---|
| **NFR-04** negative canary | Impersonated low-privilege read MUST return a strict subset AND **strictly fewer** rows than app-only. Equality = impersonation inert → build fails. Task 034 also now owns the RPA live verification |
| **NFR-05** role-depth assertion | No security role may reach the `Secure Projects` BU |
| **NFR-07** | ⚠️ Partially satisfied — **9 of 20** findings pinned, 1 partial, 10 owned by their fix tasks per the accepted escalation |
| **FR-07** delegation | Must ship BEFORE the PCF "+ User" button (task 065) |

### Coordination

`/conflict-check` before **every** BFF PR. Shares the external-access surface with
`spaarke-SPA-external-access-platform-r1/r2` and `teams-app-r1` (shipped) and `SPA-r3` (draft).
All `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, `Spaarke.Core/Auth/**` and
`DataverseWebApiService.cs` tasks are `parallel-safe:false`. Tasks 030/031/040 edit `.claude/**` →
**main-session-only**. Last check (2026-08-21): master is 1 docs-only commit ahead, **zero overlap**.
