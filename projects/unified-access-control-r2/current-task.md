# Current Task State — `unified-access-control-r2`

> **Last Updated**: 2026-08-23 (by `context-handoff`, after task 007 + a CI repair)
> **Recovery**: read "Quick Recovery" first. History lives in
> [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) and the per-task `.poml` files.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **none active** — task 007 complete; CI red from task 008 found and repaired |
| **Step** | n/a (between tasks) |
| **Status** | clean — working tree has no uncommitted changes |
| **Phase** | Phase 0 — enforcement remediation · **11 of 19 complete** (001 ✅ 002 ✅ 003 ✅ 004 ✅ 005 ✅ 006 ✅ 007 ✅ 008 ✅ 010 ✅ 014 ✅ 019 ✅) |
| **PR** | **[#812](https://github.com/spaarke-dev/spaarke/pull/812)** — draft, all work pushed |
| **Next Action** | Run **task 016** (`016-close-project-cascade-fix.poml` — **verify the filename**) via the `task-execute` skill. See "Why 016 next" below |

### 🔔 Client-visible contract change from task 008 (surfaced by the CI repair)

On `/grant`, `/revoke` and `/close-project`, a body carrying an **empty identifier** now returns
**403** (`sdap.access.deny.delegation_target_unresolved`) where it previously returned **400**. The
delegation rule runs before the handler and must first work out WHICH record — an empty id resolves
nothing, and task 008's ADR-003 constraint says deny rather than fall through. Still RFC 7807, and the
reason code distinguishes "your request named no record" from "you lack permission".

Low practical impact — `AccessGrantModal` and the external SPA send well-formed bodies — but it is a
real change to the documented contract, not just a test update. Four `Spe.Integration.Tests` cases were
flipped to match, with the rationale in their doc comments.

### Why 016 next

Eight Phase 0 fixes remain, all independent — no hard ordering left. **016** (close-project cascade,
A-12) is the strongest candidate because it is the *third* defect found in the same revocation family:
010 fixed grant/revoke drift, 007 just established that the cascade sweep must deliberately NOT filter
expiry, and 016 fixes the cascade missing organization grants entirely. Doing it while that reasoning is
fresh is worth more than the ordering of the ids.

Reasonable alternatives: **017** (SPE revoke matcher — same file family, carries binding constraints
from 010), **013** (workforce email `oid` hijack — the last unaddressed identity-confusion finding),
**012** (anonymous share links, unblocked by 002), **009** / **011** / **015** / **018**.

### Last verified state

**ALL SEVEN test projects — 11,338 passed / 0 failed**: `Sprk.Bff.Api.Tests` 10,726 ·
`Spe.Integration.Tests` 377 · `Sprk.Bff.Api.IntegrationTests` 96 · `Spaarke.Scheduling.Tests` 46 ·
`Spaarke.Core.Tests` 45 · `Spaarke.ArchTests` 36 · `RecordSyncJob.IsolatedTests` 12.
Publish **43.69 MB** compressed incl. PDBs (baseline 44.96, ceiling 60) · `--vulnerable` clean.

### 🚨 Process failure found 2026-08-23 — READ THIS BEFORE CLAIMING A GREEN SUITE

**There are SEVEN test projects. Tasks 002–008 were verified against THREE.** CI went red on task
008's commit (`SDAP CI` → genuine `failure`, not a supersession cancel) with 9 failures in
`Spe.Integration.Tests`, a project no local run had touched. Repaired in `3e5b9d373`.

**The gate is `dotnet test` at the repo root, plus the three projects it does NOT pick up:**

```
dotnet test -c Debug                                              # 4 projects
dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj
dotnet test tests/unit/Spaarke.Core.Tests/Spaarke.Core.Tests.csproj
dotnet test tests/unit/RecordSyncJob.IsolatedTests/RecordSyncJob.IsolatedTests.csproj
```

Running one project and reporting "full suite green" is how this was missed for six tasks.

---

## 🔔 Owner decisions waiting (read before the next task)

| # | Decision | Where |
|---|---|---|
| ~~D1~~ | ✅ **RESOLVED 2026-08-23** — ADR-028 A4 path A accepted; to be handled in the broader MI migration. Recorded in [`design.md` §9](design.md) | `notes/task-008-delegation-rule.md` §7 |
| ~~D2~~ | ✅ **RESOLVED 2026-08-23** — Write stays (Dataverse `CreateAccess` is an entity-level privilege, not a right on an existing record, so requiring it would deny everyone; Write is also exactly what the endpoint's own `UpdateAsync` needs). The underlying risk was **idempotency**, now closed by a 409 guard. No admin role introduced, per the owner's constraint | `notes/task-008-delegation-rule.md` §10.2 |
| **D3** | **Download enforcement vs `CanDownload`** (from 002/006): enforcement requires **Read**, the capability requires **Write**. Benign in effect but it IS the divergence FR-05 criterion 5 exists to prevent | `notes/task-002-download-authorization.md` §4 |

---

## Session summary — what was accomplished

Eleven Phase 0 tasks, all on PR #812. **FR-01 → FR-09 and FR-13 are closed**, plus part of FR-17 and
NFR-07.

| Task | What it closed |
|---|---|
| 001 | 62-test characterization suite; **first ever backfill** of the `tests/integration/auth/**` KEEP path |
| 002 | **R1's January-2026 attack scenario** — `/download` had no per-document filter; also closed `/content` |
| 003 | 4 missing `OperationAccessPolicy` keys + a source-scanning completeness gate |
| 004 | `AuthorizationService` evaluates **as the caller** |
| 005 | The `AccessRights.Read` ceiling — `RetrievePrincipalAccess` replaces a "can I read it → therefore Read" probe |
| 006 | `PermissionsEndpoints` caller-scoped; **FR-02's criterion closed** |
| **007** | **A-5 — grant expiry.** `sprk_expiresdate` was written and read NOWHERE; expired grants conferred access forever while the UI showed expiry as working |
| **008** | **A-6 — the delegation rule.** Six external-access mutations were behind bare `RequireAuthorization()`. **Unblocks task 065** |
| 010 | **A-11, ranked #1 of 13** — `/grant` upserts, `/revoke` sweeps every row on the logical key |
| 014 | Auth-mode segment in the cache key (`sp`/`obo`) |
| 019 | `LookupUserMembership` no longer sends `["*"]` |

### Method that keeps paying off — apply it to every remaining task

**Verify tests discriminate by breaking the fix and watching them fail.** Done on every task; it has
caught real gaps every time.

| Perturbation | Failures |
|---|---|
| Revert the single-doc token (006) → then the batch token | 2 → 3 |
| Transpose `AppendToAccess → Append` (005) | 4 of 15 |
| Remove the `/content` filter (002) | 2 of 17 |
| Drop `_sprk_contact_value eq null` (010) | 3 of 22 |
| Reduce revoke to the named row (010) | 2 of 22 |
| **Detach the delegation filter (008)** | **17 of 36** |
| **Weaken it to "any rights at all" (008)** | **8 of 36** |
| **Resolve revoke's target from the request body (008)** | **1 of 19** — the one test that isolates it |
| **Point the entity check back at `sprk_documents` (008 follow-up)** | **6 of 9** |
| **Disable the provisioning idempotency guard (008 follow-up)** | **4 of 5** |
| **Drop the expiry predicate (007)** | **2 of 11** |
| **Drop the `eq null` branch (007)** | **1 of 11** |
| **`ge` → `gt` on a Date Only column (007)** | **1 of 11** — the boundary-day test |
| **Ungroup the org disjunction (007)** | **1 of 11** |

**Capture failing-test identity with TRX**, not `-v q`:
`dotnet test … --logger "trx;LogFileName=t.trx"`, then parse `outcome="Failed"`.

---

## Full State (Detailed)

### Decisions made in task 007 (most recent)

| Decision | Rationale |
|---|---|
| **`ge`, not the POML's prescribed `gt`** | `sprk_expiresdate` is **DATE ONLY** (verified live). `gt` kills a grant at 00:00 ON its expiry date, silently shortening every dated grant by a day. "Access until 30 June" means 30 June works. FR-06's acceptance is an expiry **in the past**, which `ge` satisfies |
| **Bare `yyyy-MM-dd`, never a timestamp** | A datetime literal against a Date Only column risks a 400 — and a 400 here returns an EMPTY grant set, i.e. a silent total access outage, not a visible error |
| **`eq null` branch is mandatory** | OData `ge` excludes nulls; most grants have no expiry. Without it the predicate revokes every open-ended grant — an outage, not an expiry bug |
| **Revocation paths deliberately do NOT filter expiry** | `ExternalGrantLifecycle` (upsert + revoke sweep) and `ProjectClosureEndpoint`'s cascade must SEE expired rows — filtering there makes expired grants **unrevokable**. "Add it everywhere" was the obvious reading and would have introduced a new defect |
| **The display path got the predicate too** | `GetProjectContactIdsAsync` feeds a list whose contract says "active access". A participant list that disagrees with enforcement tells an operator someone still has access when they do not — that is how a revocation gets skipped |

### Decisions made in task 008

| Decision | Rationale |
|---|---|
| **Group-level filter, target resolved by bound request TYPE, default DENIES** | A seventh route added to `/api/v1/external-access` later is gated from its first request rather than inheriting A-6. Failure is loud and immediate — the right direction for an authorization default. Path strings would drift from five other files |
| **New `CallerRecordAccessProbe` instead of `AuthorizationService`** | `DataverseAccessDataSource` hard-codes `sprk_documents({id})` in BOTH its RPA target and its fallback probe → answers `None` for a project for EVERY caller. The filter would have denied universally |
| **Not `IDataverseUserClient`** (which is the right shape) | Twice-gated: compound AI gate + `ToolFramework:Enabled`. Six unconditional routes depending on it = §10 F.1 asymmetric registration, plus a CRUD→AI dependency |
| **OBO `WhoAmI()` for the principal** | RPA takes the principal as an ARGUMENT; an app-only version would carry the caller's identity as *data*, and a wrong id silently answers about the wrong person — the A-2 shape. Under OBO the identity is the *credential* |
| **No read-probe fallback** | A read proves Read; Read is not licence to grant. Consequence accepted: an RPA outage denies all six mutations rather than widening them |
| **`/revoke` follows the ROW's root, not the body's `projectId`** | Otherwise a caller with Write on any project of their choosing could revoke grants on a matter they cannot touch |
| **`/invite` now requires a resolvable root** | It provisions a CIAM identity. Contract narrowing; the only first-party caller already sends `projectId` as required |
| Mapper `internal` → `public` | Second production consumer in another assembly. The alternative — a second copy of the name→flag table — is exactly how an `AppendAccess`/`AppendToAccess` transposition gets introduced |

### Carried forward — read before ANY remaining task

| Item | Detail |
|---|---|
| **SEVEN test projects, not one** | See the process-failure box above. `dotnet test` at root covers 4; ArchTests / Core.Tests / RecordSyncJob.IsolatedTests need explicit invocation |
| **POML paths are unreliable** | Tasks 002/005/006/008/**007** all named test paths that do not exist — five of eleven. **Verify every path before acting on it** |
| **Some POMLs are not valid XML** | `007` (and `017`) carry a raw `<` inside a constraint (`Mock<HttpMessageHandler>`), so `ET.parse` fails on them. Pre-existing; `scripts/Validate-TaskPoml.ps1` reports PASS because it is not a strict parse. Do not "fix" a POML on the strength of a parse error alone — check whether it predates you |
| **KEEP paths** | Access-control → `tests/integration/auth/**`; pure domain logic → `tests/unit/domain/**`. Both globbed into `Sprk.Bff.Api.Tests.csproj` |
| **Vacuity trap** | Offline, real auth dependencies fail closed, so "all denied" is true before AND after a fix. Substitute a double that CAN answer yes, then break the fix to prove the tests bite |
| **Shared-fixture write logs bleed across tests** | `IClassFixture` gives ONE fixture per class; a `ConcurrentBag` recording writes accumulates across every test in it. A "created nothing" assertion then fails on another test's residue — or, worse, passes on it. Reset from the test-class constructor (`ProvisionProjectTestFixture.Reset()`) |
| **Moq + generic methods** | `QueryAsync<T>` returning `Task<List<T>>` cannot be stubbed with a plain lambda when `T` is the handler's own private DTO. Use `new InvocationFunc(...)` + reflection over `invocation.Method.GetGenericArguments()`, returning the JSON wire shape so the handler's own `[JsonPropertyName]` bindings stay under test (this is what keeps `_sprk_securitybuid_value` honest) |
| **NEW (008): DI resolves BEFORE endpoint filters** | Minimal API binds a handler's DI arguments before the filter pipeline. `CiamUserProvisioningService` throws without `Ciam:Domain`, so `/invite*` answered 500 *before* the filter ran. Not a hole, but a 403-free assertion on such a route proves nothing. Test fixtures for this group need the CIAM keys |
| **Doc comments in this area lie** | Five cases now: `CachedAccessDataSource`; `DataverseAccessDataSource`'s "Dataverse enforces Write/Delete separately"; a task-001 test claiming 005 would flip it; `RetrievePrincipalAccess` documented as used with zero call sites; and the POML's claim that `provision-project` has no target record (it does) |
| **`/api/v1/external` fixture trap** | `AuthPolicies.ExternalCollaboration` pins `Ciam` + `Bearer`, bypassing `FakeAuthHandler` → 500. Use `ExternalCollaborationTestFixture` |
| **Bash cwd drift** | A bare `cd` persists across calls. Prefix with `cd /c/code_files/spaarke-wt-unified-access-control-r2` |
| **CI bot pushes** | A `dotnet format` bot auto-commits to the branch. **Pull/rebase before pushing** |
| **Own-coverage obligation** | Tasks **007, 012, 013, 015, 016, 017, 018** have no pinned baseline — each supplies its own tests |
| ~~`data-mutation` KEEP path~~ | ✅ **BACKFILLED 2026-08-23** — it was the last of the seven with no csproj glob. **All seven ADR-038 KEEP paths now compile** |

### Open items requiring owner attention

| # | Item |
|---|---|
| 1 | **PR #812 workflow runs are `action_required`** — someone must approve them in the GitHub UI. `SDAP CI` passed and the CI Router's **Tier 1 blocking jobs all passed**; the red rows were runs cancelled by supersession |
| 2 | **D1 above** — ADR-028 A4 ruling (8th `WithClientSecret` site) |
| 3 | **D2 above** — `provision-project`: Write-on-project vs a privileged role for creating a BU |
| 4 | **D3 above** — download enforcement (Read) vs `CanDownload` (Write) |
| ~~5~~ | ✅ **CONFIRMED AND FIXED 2026-08-23** — `EntityAccessFilter` WAS inert: `POST /api/office/save` with a `targetEntity` returned 403 for every caller. Now resolves the target's own collection via `CallerRecordAccessProbe`. **Should fold back into `AuthorizationService` when task 032 generalizes the seam** (constraint filed) |
| 6 | **Needs its own task (002)**: `preview-url`, `view-url`, `office`, `preview` on `/api/documents` still have no per-document filter. They mint **URLs**, which outlive the request |
| 7a | **Expiry enforcement is query-level only** (007) — the tests assert the emitted `$filter`, not Dataverse's evaluation of it (transport mocking is ban B1). Live confirmation of all three cases — past expiry gone, today's expiry still works, null expiry unaffected — filed on **task 034** |
| 7 | **RPA is now load-bearing for six mutation endpoints AND the Office save gate** (008 + follow-up), as well as the document read path (005) — still unverified against a live tenant → **task 034** (constraint filed). Also verify the new not-found retry actually absorbs the wizard's replication lag |
| 8 | **Duplicates remain invisible (010)** to the participation surface until Phase 1 replaces the read-side `GroupBy` collapse |
| 9 | **019's product-semantics question**: `includeRelated: true` is a logged-warning no-op; visible in the Playbook Builder canvas, does nothing |
| 10 | **A-23**: `AddOfficeDocumentAccessFilter` is a second orphaned filter → **task 018** |
| 11 | **I-4**: `sdap:auth:*` keys carry no tenant segment → **task 035** |
| 12 | Stale "task 054 implements" comments in `MembershipEndpoints.cs` + `IMembershipResolverService.cs` → **task 015** |
| 13 | `TypedResults.Unauthorized()` returns a bare 401, not ProblemDetails (ADR-019). Pre-existing; wrap-up candidate |
| 14 | **Suite-health caveat**: one full run during task 005 reported 1 failure that never reproduced; identity not captured. Not attributed, not exonerated. Use TRX if it recurs |

### Constraints filed on future tasks (do not lose these)

| Task | Constraint from |
|---|---|
| **005** ✅ done | 003 (`AppendToAccess`), 006 (verify capabilities light up) |
| **017** | **010** — MUST NOT reduce the revoke sweep to a single row or weaken org/person isolation; also assess whether SPE removal should follow the logical key rather than `request.ContactId` |
| **032** | 006 (one-access-path invariant), 005 (per-principal derivation + `AppendTo`), **008** (collapse `CallerRecordAccessProbe` into the generalized rights map; **and the `IAccessDataSource` must stay SCOPED** — a singleton would turn `DataverseAccessDataSource`'s `DefaultRequestHeaders` mutation into a cross-user OBO-token bleed) |
| **034** | 005 (verify RPA live; grep `RPA-FALLBACK`), **007** (verify the Date Only expiry predicate live — check the null-expiry case FIRST, because if it is broken external access is down for nearly everyone), **008** (RPA now gates six MUTATIONS against `sprk_projects`/`sprk_matters`/`sprk_workassignments` — a different target from 005's `sprk_documents`, so 005 passing does not imply 008 passes; also grep `DELEGATION-RPA-UNAVAILABLE`) |
| **065** | **008** — unblocked; MUST surface `sdap.access.deny.delegation_write_required` as a real message, MUST send `recordType`+`recordId` (not legacy `projectId`), MUST NOT add a client-side pre-check that skips the server call |
| **012/013/015/016/017/018** | 001 (own-coverage obligation) — 007 ✅ discharged its own |

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
- **OBO to Dataverse must work in every deployed environment** — task 008's delegation gate has no
  fallback, so if the BFF cannot perform the OBO exchange, all six external-access mutations return 403
- BFF app user stays **Org-scoped** (impersonated privileges = app user ∩ impersonated user)
- A **non-admin test user** in the Operations subtree with no Global-read role
- BU restructure + user migration + record re-homing (UAT)

### Hard gates

| Gate | Rule |
|---|---|
| **NFR-04** negative canary | Impersonated low-privilege read MUST return a strict subset AND **strictly fewer** rows than app-only. Equality = impersonation inert → build fails. Task 034 also owns RPA live verification |
| **NFR-05** role-depth assertion | No security role may reach the `Secure Projects` BU |
| **NFR-07** | ⚠️ Partial — 9 of 20 findings pinned, 1 partial, 10 owned by their fix tasks per the accepted escalation |
| **FR-07** delegation | ✅ **SHIPPED (task 008)** — the PCF "+ User" button (task 065) is unblocked |

### Coordination

`/conflict-check` before **every** BFF PR. Shares the external-access surface with
`spaarke-SPA-external-access-platform-r1/r2` and `teams-app-r1` (shipped) and `SPA-r3` (draft).
All `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, `Spaarke.Core/Auth/**` and
`DataverseWebApiService.cs` tasks are `parallel-safe:false`. Tasks 030/031/040 edit `.claude/**` →
**main-session-only**. **Phase 0 has no remaining co-schedulable pair** — run serially.
Last master check (2026-08-22): 1 docs-only commit ahead, **zero overlap**.
