# Current Task State — `unified-access-control-r2`

> **Last Updated**: 2026-08-21 (by `context-handoff`)
> **Recovery**: read "Quick Recovery" first. History lives in
> [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) and the per-task `.poml` files.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **none active** — task 004 completed and committed |
| **Step** | n/a (between tasks) |
| **Status** | clean — working tree has no uncommitted changes |
| **Phase** | Phase 0 — enforcement remediation · **5 of 19 complete** (001 ✅ 003 ✅ 004 ✅ 014 ✅ 019 ✅) |
| **Next Action** | Run **task 006** via the `task-execute` skill with argument `projects/unified-access-control-r2/tasks/006-caller-scoped-permissions-endpoint.poml` (path verified 2026-08-21). It finishes what 004 started — see ⚠️ below. |

### Files Modified This Session

**All committed.** 5 commits on `work/unified-access-control-r2`, **none pushed**:

| Commit | Contents |
|---|---|
| `fe88d5339` | task 001 — 62-test characterization suite; first backfill of `tests/integration/auth/**` + csproj glob |
| `3e4523055` | accepted escalation — own-coverage obligations on tasks 007/012/013/015/016/017/018 |
| `9037c3a01` | task 003 — 4 policy keys + 15-test completeness gate; filed A-23 |
| `6393acba8` | wave P0-B — 014 (auth-mode cache key) + 019 (membership `["*"]`) |
| `ac9d78c85` | task 004 — `AuthorizationService` caller-scoped; `TokenHelper.ExtractBearerTokenOrNull` |

### Critical Context

Phase 0 is repairing enforcement on the access path. Task 004 made `AuthorizationService` evaluate as
the **caller** (token now on `AuthorizationContext.UserAccessToken`, `required string?` so app-only must
be declared explicitly; missing token → deny with `sdap.access.deny.no_caller_token`, data source never
consulted). Last verified state: **BFF 10,625 passed / 0 failed · ArchTests 36/36 · Core 45/45 · publish
43.65 MB compressed** (baseline 44.96, ceiling 60).

### ⚠️ Read before starting task 006

**FR-02's acceptance criterion is still OPEN and 006 owns the rest of it.**
`PermissionsEndpoints.cs:76` and `:159` still pass `userAccessToken: null` — they call
`IAccessDataSource` **directly**, bypassing `AuthorizationService`, so task 004 could not reach them
(finding A-4). `required` does not protect that path either, because it never constructs an
`AuthorizationContext`. **Route those calls THROUGH `AuthorizationService`** rather than re-plumbing the
token at the endpoint, or the same defect reappears at the next direct caller. Full reasoning:
[`notes/task-004-caller-scoped-design.md`](notes/task-004-caller-scoped-design.md) §6.

---

## Full State (Detailed)

### Alternative next waves

| Option | Tasks | Why |
|---|---|---|
| **Recommended** | **006** (`parallel-safe:true`) | Finishes FR-02 (above). Small, well-scoped |
| High value | **005** (lift the Read ceiling) | Carries a **binding obligation from task 003**: MUST map Dataverse `AppendToAccess` → `AccessRights.AppendTo`, or `POST /api/office/save` is permanently 403 *while looking fixed* (the denial reads as legitimate `insufficient_rights`) |
| Independent | **002** (authorize document download) | A-1 — R1's January-2026 attack scenario, still open. Unblocks 012 |

⚠️ 005 and 006 are file-disjoint but **not ideal to pair**: 006's correct fix routes through
`AuthorizationService`, which 005's rights-mapping also affects. Run 005 first if pairing.

### Decisions made this session

| Decision | Rationale | Where |
|---|---|---|
| Access-control tests live at `tests/integration/auth/**`, not the paths the POMLs name | Only that path is an ADR-038 §2 KEEP category; it had **zero compiled files** and was globbed by no csproj, so a test placed there would silently not run. CLAUDE.md §6.5 path C | `notes/task-001-untestable-findings.md` §4a |
| Escalation resolved as **path B** for 6 wire-format findings, **path C** for A-15/A-16 | Owner-accepted 2026-08-21. Recorded as `<constraint source="task-001">` on tasks 007/012/013/015/016/017/018 | same file, §3 |
| `entity.associate_document` → `AppendTo`, not `Write` | The filter authorizes the **target entity** and the operation attaches a document TO it. Creates a binding obligation on task 005 | `notes/task-003-operation-rights-decisions.md` §1–2 |
| Token carried on `AuthorizationContext`, **not** via `IHttpContextAccessor` | `Spaarke.Core` has no ASP.NET Core dependency and `LayerDependencyTests` guards that boundary; a second auth service would violate ADR-010 | `notes/task-004-caller-scoped-design.md` §1 |
| `required` on a **nullable** property | Forces every construction site to state intent, so app-only is a visible `= null` rather than the default you get by not thinking — which is how A-2 survived. Produced 7 compile errors across 11 sites | same file, §2 |

### Carried forward — read before ANY Phase 0 task

| Item | Detail |
|---|---|
| **KEEP path** | Access-control tests → `tests/integration/auth/**`. POMLs still name `tests/unit/Sprk.Bff.Api.Tests/AccessControl/`, which does not exist and is not a KEEP path |
| **`/api/v1/external` fixture trap** | `AuthPolicies.ExternalCollaboration` pins the `Ciam` + `Bearer` schemes, bypassing `FakeAuthHandler`. With the shared fixtures that group returns **500**, making any "not 403" assertion pass **vacuously**. Use `ExternalCollaborationTestFixture` |
| **Anti-vacuity rule** | A test passing because the path was never reached is worse than no test. All "not 403" assertions carry a sub-500 guard; cache-miss assertions also assert the inner source was reached |
| **Parallel-wave gotcha** | Most POMLs instruct the executor to update `TASK-INDEX.md`. When running >1 task concurrently, **suppress that instruction** and have the orchestrator own the file, or the agents collide |
| **Own-coverage obligation** | Tasks **007, 012, 013, 015, 016, 017, 018** have no pinned baseline — each extracts a query-builder seam and supplies its own tests |
| **`data-mutation` KEEP path** | Still un-backfilled — zero compiled files, globbed by no csproj. A write-path test placed there will silently not run |

### Open items requiring owner attention

| # | Item |
|---|---|
| 1 | **019's product-semantics question**: `includeRelated: true` on the `LookupUserMembership` node is now a **logged-warning no-op**. The flag is visible in the Playbook Builder canvas and does nothing. No playbook sets it today (verified), so it is latent. Real fix = a `relatedEntities: string[]` config field, or remove the flag |
| 2 | **A-23** (new, filed by 003): `AddOfficeDocumentAccessFilter` is a second orphaned filter alongside A-15 — zero call-sites, doc examples `"share"`/`"attach"` unregistered → **task 018** deletes it |
| 3 | **I-4** (new, filed by 014): `sdap:auth:*` keys carry no tenant segment, unlike the `ITenantCache` pattern used elsewhere. Moot while single-tenant; **task 035** should design its per-user cache tenant-aware from the start |
| 4 | Stale "task 054 implements" comments remain in `MembershipEndpoints.cs` + `IMembershipResolverService.cs` → **task 015** owns that directory |
| 5 | **Nothing pushed.** 5 commits are local-only; no PR exists for this branch |

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
(10,598 → 10,617 → 10,622 → 10,625 passed, 0 failed). The failure was not reproducible and its identity
was lost to log truncation — a pre-existing flake, not a regression. Watch for recurrence.
