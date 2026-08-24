# Current Task State — `unified-access-control-r2`

> **Last Updated**: 2026-08-24 (mid-task-022 — keys + C2/C3/H2/H3 gated)
> **Recovery**: read "Quick Recovery" first. History lives in
> [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) and the per-task `.poml` files.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **022 IN PROGRESS** — document-surface authorization sweep (Critical). Inventory ✅ · H5 ✅ · **keys + C2 + C3 + H2 + H3 gated ✅** (8 routes, 21 tests, 10/10 perturbations bite) |
| **Step** | 022 — **8 of 22 routes now gated (12 total incl. task 002's 4)**. Remaining: C1 (bulk), `analyze`, 5 URL-minting reads, 6 OBO reads, 3 collection-shaped |
| **Status** | clean — all work committed and pushed (`f0747bb33`) |
| **Phase** | Phase 0 — enforcement remediation · **14 of 20 complete** (001 ✅ 002 ✅ 003 ✅ 004 ✅ 005 ✅ 006 ✅ 007 ✅ 008 ✅ **009 ✅** 010 ✅ 014 ✅ 016 ✅ 017 ✅ 019 ✅) · **+ Phase 0b filed: 021–029** — **020 added 2026-08-24 by owner decision** (org-grant SPE member cleanup, was a deferred note in 017 §6); **029 added 2026-08-24 by owner decision** (To Do read/create parity — task 009 left PATCH wider than list) |
| **PR** | **[#812](https://github.com/spaarke-dev/spaarke/pull/812)** — draft, all work pushed |
| **Next Action** | **Task 022 — C1, the bulk-download route.** `BulkDownloadAuthorizationFilter` reads a tenant claim, logs "authorization granted", and calls `next()` — **no per-document decision at any point**. 500 GUIDs per request, streamed app-only. Needs per-item authorization **plus** an explicit partial-failure contract: the `_FAILED.txt` manifest distinguishes "denied" from "missing", which stays an enumeration oracle even after per-item auth exists. Then `analyze` (decide `read` vs `write` — it reads the doc but spends money and enqueues work), then the 5 URL-minting reads. |

### 🔔 Client-visible contract change from task 008 (surfaced by the CI repair)

On `/grant`, `/revoke` and `/close-project`, a body carrying an **empty identifier** now returns
**403** (`sdap.access.deny.delegation_target_unresolved`) where it previously returned **400**. The
delegation rule runs before the handler and must first work out WHICH record — an empty id resolves
nothing, and task 008's ADR-003 constraint says deny rather than fall through. Still RFC 7807, and the
reason code distinguishes "your request named no record" from "you lack permission".

Low practical impact — `AccessGrantModal` and the external SPA send well-formed bodies — but it is a
real change to the documented contract, not just a test update. Four `Spe.Integration.Tests` cases were
flipped to match, with the rationale in their doc comments.

### Where task 022 stands (READ BEFORE RESUMING)

**Inventory complete** — [`notes/task-022-document-surface-inventory.md`](notes/task-022-document-surface-inventory.md).
The class is **22 routes across 4 files**, not the "~15" the review estimated:
4 gated · 1 gated-in-form-only (C1) · 2 ungated destroy (C2, C3) · 8 ungated mutate/disclose
(H2, H3, H4) · 5 ungated URL-minting reads · 3 collection-shaped with no caller-supplied id.

**H5 fixed** (`081477bd3`). `GetProjectsAsync` ordered by `sprk_name`, absent from `sprk_project`
(the `$select` one line above already had `sprk_projectname` right) → Dataverse 400 → caught →
empty list → **the external SPA showed "you have no grants" to every caller who had grants.**
Sixth instance of the stale-column class; it survived review because `$select` and `$orderby` sit
on adjacent lines and disagree, so checking the select gives a false all-clear.

### ✅ The blocker is cleared — keys `write` + `delete` registered (`f0747bb33`)

Both landed **in the same commit as their consumers**, verified reachable first
(`DataverseAccessRightsMapper` maps `WriteAccess`/`DeleteAccess`; RPA returns the full rights
string). `AddDocumentAuthorizationFilter`'s own `<param>` doc had always advertised
`"read", "write", "delete"` while two thirds of that contract could not be honoured.

⚠️ **Both new gates depend on RPA being live.** The fallback probe caps rights at Read *by
construction*, so on an RPA outage every `write`/`delete` gate denies — correct fail-closed
direction, same trade as task 008, but it means those routes are **unavailable, not degraded**.
Task 034 owns live verification (`RPA-FALLBACK` marker).

### Then, in order

1. **C1** — bulk. Needs per-item authorization **plus** an explicit partial-failure contract: the
   `_FAILED.txt` manifest distinguishes "denied" from "missing", which stays an enumeration oracle
   even after per-item auth exists.
2. **`analyze`** — deliberately left ungated pending a decision, not by oversight. It reads the
   document and writes to a *different* entity (so `read` by the `finance.confirm` reasoning), but
   spends money and enqueues background work (so `write`). Decide explicitly.
3. **The 5 URL-minting reads** — they outlive the request; decide whether `read` is right or
   whether minting deserves its own key.
4. **The 6 OBO read routes** — decide whether OBO alone suffices; POML escalation trigger if a
   record check is needed that does not exist.
5. **The 3 collection-shaped routes** — confirm they stay Phase 1 evaluator work rather than
   silently dropping them.

### Phase 0b — the 8 review tasks are FILED (owner-approved 2026-08-24)

`021`–`028` exist as POMLs and are registered in [`TASK-INDEX.md`](tasks/TASK-INDEX.md).
Recommended order **022 → 021 → 025 → 023 → 028 → 024**, with **026 and 027 parallel-safe**
(no deps, no contended code — runnable any time, by anyone).

- **021** ⚠️ the three `@odata.bind` names MUST come from `$metadata`. Escalate rather than guess —
  a wrong nav-prop is silently accepted as an unknown property and the write does not happen.
- **025** is why the rest could hide: the central gate (`CallerRecordAccessProbe.GetCallerRightsAsync`)
  can be replaced with "return all rights" and the whole suite stays green.
- **028** (new, from the task-009 To Do discussion): service request is one of the FOUR core types
  in the project model but has no accessible set — only project/matter/WA exist.

### Owner decisions recorded 2026-08-24

| Decision | Effect |
|---|---|
| **To Do: matter + work assignment get the same functionality as project** | Implemented in `9294f0182`. ⚠️ TWO consequences: matter/WA sets carry NO access level, so membership implies WRITE there (more permissive than project, which requires the `Write` right); and the READ path is still project-only, so PATCH is WIDER than list. ✅ **Read/create parity FILED as task 029** (`290fcbf52`) — grounded on the `documents` module's existing OR'd `ScopeDimension` list and the already-entity-generic `ApplyResolverFieldsAsync`, so it is a third instance of an existing pattern, not new machinery. |
| **CI: rely on `CI / Router`; do not chase `SDAP CI`** | Router is green (twice; tier2 ran 24m and 23m32s against the new 30m timeout). SDAP CI stays red on pre-existing latent flakes. **Do NOT register flakes reactively one per ~30-min cycle** — that is the widen-the-tolerance pattern. |

### Last verified state

**ALL SEVEN test projects — 11,410 passed / 0 failed**: `Sprk.Bff.Api.Tests` 10,798 ·
`Spe.Integration.Tests` 377 · `Sprk.Bff.Api.IntegrationTests` 96 · `Spaarke.Scheduling.Tests` 46 ·
`Spaarke.Core.Tests` 45 · `Spaarke.ArchTests` 36 · `RecordSyncJob.IsolatedTests` 12.
Publish **43.70 MB** compressed incl. PDBs (unchanged by tasks 009 + 022 — no packages added; ceiling 60) · `--vulnerable` clean.

⚠️ **`dotnet build --warnaserror` is clean for the BFF project, NOT for the whole solution.**
`dotnet build -c Debug --warnaserror` at the root fails with **5 pre-existing CA2024** errors
(`reader.EndOfStream` in an async method) in `tests/integration/Spe.Integration.Tests/AnalysisEndpointsIntegrationTests.cs`.
Verified present on a stashed clean tree, so not ours — but earlier checkpoints said
"`--warnaserror` clean" without that qualifier. Scope the claim to the project you built.
Frontend: **26** `AccessGrantModal` tests pass — but `node_modules` is **absent in a fresh worktree**, so
`npm install --legacy-peer-deps --no-audit --no-fund` under
`src/client/shared/Spaarke.UI.Components` is required before any frontend test edit can be verified.

⚠️ **Measure the publish COMPRESSED.** Raw bytes on disk are ~137 MB; the §10 ceiling is on the
compressed artifact (43.69 MB). Zip `deploy/api-publish/` before reporting a number.

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
| **Revert the `$select` to `_sprk_contactid_value` (016)** | **14 of 20** |
| **Restore the null-contact exclusion (016)** | **6 of 20** |
| **Rethrow instead of the typed enumeration response (016)** | **2 of 20** |
| **Ignore `failedCount`, always 200 (016)** | **2 of 20** |
| **Drop the unaddressable-row guard (016)** | **1 of 20** |
| **Match the SPE permission on the contact GUID again (017)** | **2** |
| **Restore false success on SPE no-match (017)** | **3** |
| **Report a Graph error as genuinely-absent (017)** | **2** |
| **Re-swallow SPE listing failures (017)** | **2** — *initially 0; see the lesson below* |
| **Ignore per-member SPE removal failures (017)** | **1** |
| **Restore the broken provisioning `$select` (review fix)** | **7 of 7** — and the same names passed **5 of 5** before the guard was ported |

**Capture failing-test identity with TRX**, not `-v q`:
`dotnet test … --logger "trx;LogFileName=t.trx"`, then parse `outcome="Failed"`.

---

## Full State (Detailed)

### Decisions made during the review (most recent)

| Decision | Rationale |
|---|---|
| **Fix the provisioning `$select` immediately, before synthesis** | It broke a shipped endpoint on the branch. Everything else in the findings is analysis; this was a live break |
| **Do NOT guess the `@odata.bind` nav-property casing** | Deferred to task 021 with a mandatory `$metadata` step. Nav props are case-sensitive and not derivable from the attribute name; a wrong one is accepted as an unknown property and the write silently does not happen — the exact class under review. No secure project exists in dev to read the casing back from |
| **Fix names AND the swallow together in 021** | Names alone leaves the next drift invisible; the swallow alone hard-blocks provisioning on names we know are wrong |
| **Port task 016's `$select`-validating fake to the provisioning fixture** | The guard already existed one directory over and was not carried across — which is precisely why 5 of 5 tests stayed green while the endpoint 500'd |
| **KEEP `GrantMembershipAsync`** (owner ruling) | Verified: one code occurrence repo-wide, no reflection path, unreachable from any endpoint, no other worktree or open PR references it |
| **Defuse task 009's POML now, not as a task** | It is a pending security task whose POML told the executor to flip a nonexistent characterization and named task 011's contended file. Under literal execution it would have WEAKENED the fail-closed gate it exists to strengthen |
| **File the review findings as a doc, propose tasks, do not create 7 POMLs unilaterally** | Seven tasks is a scope decision that belongs to the owner |

### Decisions made in task 017

| Decision | Rationale |
|---|---|
| **Delete the endpoint's forked matcher rather than fix it** | `SpeContainerMembershipService.RevokeMembershipAsync` already matched on email correctly and had **zero callers**. The endpoint had forked a working implementation and broken it — CLAUDE.md §11 says reuse, so the fork goes |
| **Keep the SPE removal path** (escalation did not fire) | Nothing in the codebase ADDS a container permission, so this is a cleanup path for legacy/admin ACLs — exactly the ones nothing else will clean. `NoPermissionFound` is therefore the healthy answer, not a problem |
| **4-state `SpeContainerOutcome`, not a bool** | ADR-003 requires distinguishing "confirmed absent" from "match failed". The old bool answered `true` for both, which is how A-13 hid |
| **"No email" → `Failed`, not `NoPermissionFound`** | Without the key an existing permission is unfindable. That is unknown, not absent — calling it absent would repeat A-13 in a new place |
| **Keep `SpeContainerMembershipRevoked`, made honest** | Existing readers get a correct value instead of a constant. Only the relic (`WebRoleRemoved`) was removed |
| **`GrantMembershipAsync` NOT deleted** | It is dead (zero callers) and H-8b says remove dead branches — but it defines the identity key the matcher must match. Documented with a "no callers by design / broker-only" header and **flagged for the owner** rather than silently deleting a public method |
| **`ListExternalMembersAsync` propagates** | An empty list must mean one thing. Catching everything and returning `[]` is what made "Graph unreachable" indistinguishable from "empty container" |
| **Per-member removal failures counted, loop not aborted** | Aborting leaves strictly MORE access in place. Same reasoning as task 016's deactivation sweep |
| **Org-grant SPE cleanup filed, not fixed** | No single grantee → no email. Needs org→members expansion (declined in 016 for cache too). Bounded: broker-only creates no member ACLs |

### Decisions made in task 016

| Decision | Rationale |
|---|---|
| **`_sprk_contact_value`, confirmed against live metadata** | Three sources agreed (live metadata, `ExternalParticipationService`, `ExternalGrantKey`); the solution's `views-schema.md` says `sprk_contactid` and is **stale**. There is no `sprk_contactid` attribute on the table at all, so the escalation trigger did not fire |
| **Drop the null-contact filter entirely** | A null contact IS the organization-grant discriminator. Requiring a contact was not a safety check — it silently excluded every org grant from closure |
| **An id-less row is a FAILURE, not a skip** | It cannot be PATCHed, so it cannot be deactivated. Skipping it quietly would leave an active grant behind a 200 — the same false-success shape, one layer down |
| **Partial deactivation now returns non-success (in-scope extension)** | Not in A-12; found while fixing it. The loop swallowed per-row errors and returned only the success count, so 2-of-5 revoked answered `200 OK`. Precedent one directory over: `ExternalGrantLifecycle.DeactivateAsync` (task 010) |
| **Continue-on-error is KEPT** | Aborting at the first failure leaves strictly MORE access standing. What changed is that failures are counted and reported, not that the sweep stops |
| **Steps 3–4 run before the failure is returned** | Both only ever REMOVE access, so running them makes a partial state strictly less open. Closure is idempotent, so "retry" is sound |
| **`ExternalAccessRow` `private` → `internal`** | The reason A-12 survived: no test could name `QueryAsync<ExternalAccessRow>`. ADR-038 §4 seam via `InternalsVisibleTo`; ban B8 (reflection) avoided |
| **The fake table validates the `$select`** | Load-bearing. A fake that ignored the projection would have gone green on the exact code that shipped A-12 |
| **SPE guard added but NOT tested** | `ListExternalMembersAsync` swallows everything and returns `[]`, so the guard cannot fire today. Documented as untestable-today rather than covered by a fake exception the service cannot throw — and filed on 017 |
| **Tests at `tests/integration/auth/**`, not the POML `<outputs>` unit path** | The `task-001` constraint is explicit; that path is deletion-protected, the unit path is not; every Phase 0 task so far landed there |

### Decisions made in task 007

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
| **POML paths are unreliable** | Tasks 002/005/006/008/007/**016** all named test paths that do not exist or that a later constraint overrides — six of twelve. **Verify every path before acting on it** |
| **Publish size is COMPRESSED** | Raw bytes are ~137 MB, the ceiling is 60. Zip `deploy/api-publish/` before reporting. Measuring raw once produced a false "3× over ceiling" scare |
| **A fake that ignores the `$select` will go green on a broken projection** | Task 016 built a fake that rejects unknown columns; the provisioning fixture had none, so 5 of 5 tests passed while `/provision-project` 500'd. **When an endpoint reads Dataverse, its fake must validate the projection.** Now ported to both |
| **Verify EVERY column you add, not just the ones you came to fix** | The review found five stale-column instances; the fifth was introduced by the same session that fixed three. Fixing an instance of a class does not inoculate the next line you write |
| **Mocking at a seam proves the CALLER, never the CALLEE** | Task 017: re-swallowing listing failures passed EVERY endpoint test, because the closure tests substitute `RemoveAllExternalMembersAsync` at its seam and never reach `ListExternalMembersAsync`. The fix a binding constraint asked for was untested until a perturbation exposed it. **When a task's deliverable is "make X report failures", test X directly** |
| **A green local suite is NOT CI — read the gate, not the substitute** | This project reported "11,374 passed locally" as verification for six consecutive commits while `CI / Router` had never once rendered a verdict on the branch (17 runs, 0 successes). Local runs never execute Arch Tests, Changed-Surface Integration Smoke, Auth Smoke, Plugin Size or the Last-Reviewed stamp. **And when the gate is red for reasons that look unrelated to the diff, that is a finding to chase — not noise to route around.** It hid a repo-wide CI defect for weeks |
| **A check with only a happy-path test is not tested** | Task 009 hit the zero-failure perturbation TWICE. (a) Two guards denied the same case, so a status-code assertion could not tell them apart — deleting the A-7 fix left every test green; fixed by asserting WHICH guard denied. (b) The new work-assignment membership check had a positive test but no negative one — bypassing it entirely failed zero; fixed by adding the negative. **Pair every positive with a negative, and assert the distinguishing observable.** |
| **Check the `$orderby`, not just the `$select`** | H5's sixth stale-column instance sat one line below a CORRECT `$select`. Reading the select gave a false all-clear for months. Verify EVERY clause that names a column — select, filter, orderby, expand, and `@odata.bind`. |
| **A perturbation harness needs a clean-tree BASELINE and fresh mtimes** | Task 022's first sweep produced FAKE numbers. The harness restored files with `shutil.copy2`, which preserves the *backup's* mtime — older than the built DLL — so MSBuild skipped recompiling and some runs measured a **stale binary still carrying the previous perturbation**. It reported 3 failures where the truth was 1. Two fixes, both mandatory for any future harness here: `os.utime(f, None)` after restore, and a clean-tree baseline run that must be **0 failures** before the sweep. Without the baseline every count is measured against unknown noise. Caught only because an unexplained number was checked instead of accepted. |
| **A doc comment claiming "enforced elsewhere" is a finding, not evidence** | `/checkout`'s comment said "PCF controls button visibility based on Dataverse security profile / actual permissions enforced by Graph API via OBO". Both halves false: client-side button visibility is not enforcement, and the path is app-only so nothing downstream saw the caller. Sixth doc-comment-lies instance in this area. **When a comment explains why no check is needed, verify the mechanism it names actually runs on that route.** |
| **Distinguish "the gate needs X" from "the service lacks X"** | I recorded C2 as "NOT a filter attachment — needs a signature change with call-site fallout" because `DeleteAsync` takes no identity. Wrong: `DocumentAuthorizationFilter` reads identity from `HttpContext`. The missing parameter was a real observation (app-only destroy → no defence in depth) attached to the wrong conclusion (a blocker). It nearly cost a whole extra step. |
| **Do not attach an authorization filter before its operation key exists** | `OperationAccessPolicy.GetRequiredRights` throws on an unknown operation and the filter's catch returns 500 — fail-closed, but that means the route becomes an unconditional 403 for EVERY caller. Already happened once (finance surface + Office save + three document reads); the file's header records it. |
| **Do not push again while a CI run is in flight** | The 13 cancelled Router runs are self-inflicted: push cadence (13:39 → 13:53 → 15:06 → 15:09 → 15:24) outran a ~9-min Router with `cancel-in-progress: true`, so each push killed the previous verdict. **After the last push of a work session, wait for the gate before pushing again** — otherwise the branch accumulates commits that were never adjudicated |
| **Look for an existing correct implementation before fixing a broken one** | Task 017's bug was a FORK of working code that had zero callers. Grepping for the method name first turned a "patch the matcher" task into a deletion |
| **Frontend tests need `npm install` first** | `node_modules` is absent in a fresh worktree; `npm test` fails with "jest is not recognized". Use `npm install --legacy-peer-deps --no-audit --no-fund` (never `npm ci`, per root CLAUDE.md §12) |
| **Don't put backticked markdown in a bash-quoted Python heredoc** | Bash treats backticks as command substitution and silently mangles the text. Write the script to the scratchpad and run it as a file |
| **Schema docs lose to live metadata** | `views-schema.md` says `sprk_contactid`; the table has no such attribute. Two Phase 0 tasks (007 type, 016 name) turned on checking live metadata rather than trusting a doc |
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

### CI posture — DECIDED 2026-08-24 (owner)

**Rely on `CI / Router`. Do NOT chase `SDAP CI`.**

`CI / Router` is the intended single composite gate (spec FR-A01) and is now **green** after the
2026-08-24 repair ([`notes/ci-router-gate-repair-2026-08-24.md`](notes/ci-router-gate-repair-2026-08-24.md),
[issue #813](https://github.com/spaarke-dev/spaarke/issues/813)) — two consecutive greens, tier2
unit tests running 24m / 23m32s against a 30m timeout.

`SDAP CI` remains **red on pre-existing latent flakes**, not on anything this project changed. The
repaired gate exposed a cluster of them: the classifier fails the build on any pass-1 failure not in
`tests/.reliability-registry.json`, and because `SDAP CI` was cancelled by the next push on most
recent commits, these had never surfaced. Two seen so far — `JobsEndpointsTests.Trigger_RunsJobOutOfBand_RecordsRun`
(registered) and `ReAnalysisFlowTests.ReAnalysis_HappyPath_...` (SSE stream, `TaskCanceledException`
after 2m26s on a contended runner; passes locally).

**Do not register flakes reactively one per CI cycle.** That is a ~30-minute loop per entry and it is
the "silently widen the tolerance" pattern. If `SDAP CI` needs to go green, enumerate the flake set in
one local sweep under load and propose a single reasoned batch. Otherwise treat it as known-red.

---

### Open items requiring owner attention

| # | Item |
|---|---|
| ~~1~~ | ❌ **THAT CLAIM WAS WRONG — CORRECTED 2026-08-24.** Nothing on PR #812 ever needed owner approval. The only `action_required` runs are on three `github-actions[bot]` auto-format commits (`7ca8669d5`, `7f36a5ffe`, `e12cc48d3`), each superseded by the next human commit within minutes. The claim was carried across three checkpoints unverified. **The real problem it was masking**: `CI / Router` had **never been green on this branch — 17 runs, 0 successes** — because tier2's unit-test job hit `timeout-minutes: 6` (a timeout reports as `cancelled`, which `alls-green`'s `allowed-failures` does not cover) → the gate hard-failed while Tier 1 was green. Repo-wide: 20 of 20 tier2 unit-test jobs across all branches were cancelled; `work/spaarkeai-compose-r8` failed identically. **Fixed here** (owner-approved, files owned by `ci-cd-unit-test-remediation-r1`): timeout 6→30, tier2 excluded from Router adjudication by construction, standalone `pull_request` trigger removed. **VERIFIED GREEN** at `f695ce38f` (run 32747593600): `CI / Router` = **SUCCESS — the first ever on this branch**; all 5 Tier 1 + all 7 Tier 2 jobs pass; zero `CANCELLED` rows (was 8+). ⚠️ `Full Unit Tests` took **exactly 24 min** — the first duration this job has ever produced — so 6 was 18 min short AND the 20 I first drafted would have been **4 min short**. Sizing a runaway-guard timeout at the edge of your estimate IS the bug. Full write-up + their decision list: [`notes/ci-router-gate-repair-2026-08-24.md`](notes/ci-router-gate-repair-2026-08-24.md) · [issue #813](https://github.com/spaarke-dev/spaarke/issues/813) |
| 2 | **D1 above** — ADR-028 A4 ruling (8th `WithClientSecret` site) |
| 3 | **D2 above** — `provision-project`: Write-on-project vs a privileged role for creating a BU |
| 4 | **D3 above** — download enforcement (Read) vs `CanDownload` (Write) |
| ~~5~~ | ✅ **CONFIRMED AND FIXED 2026-08-23** — `EntityAccessFilter` WAS inert: `POST /api/office/save` with a `targetEntity` returned 403 for every caller. Now resolves the target's own collection via `CallerRecordAccessProbe`. **Should fold back into `AuthorizationService` when task 032 generalizes the seam** (constraint filed) |
| 6 | **Needs its own task (002)**: `preview-url`, `view-url`, `office`, `preview` on `/api/documents` still have no per-document filter. They mint **URLs**, which outlive the request |
| ~~7b~~ ✅ | **FR-15's SPE half — CLOSED by task 017.** `ListExternalMembersAsync` now propagates and `RemoveAllExternalMembersAsync` returns `SpeBulkRemovalResult(Removed, Failed)`, so close-project's `container_not_cleared` guard is reachable and tested (listing failure AND partial clear). FR-15 and FR-16 are both fully closed |
| ~~7c~~ ✅ | **RESOLVED 2026-08-24 — KEEP `GrantMembershipAsync`.** Owner: do not delete unless 100% certain it is unused anywhere; the membership service is integral to access + notifications, so anything touching it must be exactly right. Verification done: **one** code occurrence repo-wide (its own definition), no reflection/dynamic-invocation path, not reachable from any endpoint, and no other worktree or open PR references it. Kept, with the no-callers-by-design header. ⚠️ Note for coordination: `code-quality-and-assurance-r3` task 020 plans to remove *4* dead `catch (ServiceException)` sites in this file — task 017 already removed one (in `ListExternalMembersAsync`), so their count is now **3**. *(superseded item below)* |
| ~~7c-old~~ | **Owner call wanted: delete `SpeContainerMembershipService.GrantMembershipAsync`?** (017) It has **zero callers** — Spaarke is broker-only and adds no container ACLs — so H-8b's "no dead branches implying grants add members" argues for deletion. It was KEPT because it defines the identity key the revoke matcher must match, and deleting a public service method exceeds this task's scope. It now carries an explicit no-callers-by-design header. Low risk either way |
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
| **017** ✅ discharged | **010** — sweep preserved (pinned by `Revoke_WhenSpeFails_StillReportsTheDataverseRowsDeactivated` + the existing isolation tests); the "assess SPE-vs-logical-key" ask was **assessed and FILED** — an org revoke has no single grantee, so no email, so cleanup needs an org→members expansion this path lacks. Reports `NotAttempted`. Bounded: broker-only creates no member ACLs. · **016** — SPE reporting made honest, `container_not_cleared` now reachable + tested |
| **032** | 006 (one-access-path invariant), 005 (per-principal derivation + `AppendTo`), **008** (collapse `CallerRecordAccessProbe` into the generalized rights map; **and the `IAccessDataSource` must stay SCOPED** — a singleton would turn `DataverseAccessDataSource`'s `DefaultRequestHeaders` mutation into a cross-user OBO-token bleed) |
| **034** | 005 (verify RPA live; grep `RPA-FALLBACK`), **007** (verify the Date Only expiry predicate live — check the null-expiry case FIRST, because if it is broken external access is down for nearly everyone), **008** (RPA now gates six MUTATIONS against `sprk_projects`/`sprk_matters`/`sprk_workassignments` — a different target from 005's `sprk_documents`, so 005 passing does not imply 008 passes; also grep `DELEGATION-RPA-UNAVAILABLE`) |
| **065** | **008** — unblocked; MUST surface `sdap.access.deny.delegation_write_required` as a real message, MUST send `recordType`+`recordId` (not legacy `projectId`), MUST NOT add a client-side pre-check that skips the server call |
| **012/013/015/018** | 001 (own-coverage obligation) — 007 ✅, 016 ✅ and 017 ✅ discharged their own |
| **043** | **020** — the `sprk_enddate` read-side asymmetry: `QueryActiveOrgIdsAsync` considers `statecode` only, so a membership ended by date but never deactivated still confers inherited access. 020 does not change read behaviour; FR-24/FR-25 must decide whether an ended membership still inherits |
| **Phase 1 evaluator (032/043)** | **017** — if you build the organization→members expansion that FR-24/FR-25 need for org terms, the org-grant **SPE cleanup gap** becomes cheap to close at the same time (`RemoveSpeContainerPermissionAsync` currently reports `NotAttempted` for org revokes). See `notes/task-017-spe-revoke-matcher.md` §6 |

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
