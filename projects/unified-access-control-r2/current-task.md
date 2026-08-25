# Current Task State — `unified-access-control-r2`

> **Last Updated**: 2026-08-25 (task 046 complete) — **021 + 045 merged to master; 046 configured live**
> **Recovery**: read "Quick Recovery" first. History is in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md),
> the per-task `.poml` files, and `notes/`. "Full State (Detailed)" below is retained history.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | ✅ **046 COMPLETE** (live Dataverse config + docs; **uncommitted**). 021 + 045 merged to master (`290d9ab79`) |
| **Step** | Between tasks. Nothing in flight. **Working tree has uncommitted 046 doc changes — commit before anything else** |
| **Status** | **PR #812 is MERGED** — continued work needs a NEW PR |
| **Phase** | **Phase 0 — 14 of 20** (001–010, 014, 016, 017, 019 ✅ · remaining **011 012 013 015 018 020**) · **Phase 0b — 4 of 12** (**021 ✅ 022 ✅ 045 ✅ 046 ✅** · remaining **047** 023 024 025 026 027 028 029) |
| **Next Action** | **Commit + push 046**, then **🔔 owner decides the §5.1a-2 depth fix** (below). Task **047** is runnable after the operator deploys the BFF to dev — but see the scope caveat |

### 🔔 OWNER DECISION REQUIRED — task 046 found that secure projects are NOT isolated

**Proven empirically, not inferred.** `Test User 1` — an ordinary non-admin user — **read a real
`sprk_issecure=true` project** owned by the `Secure Project` owner team, sitting in the `Secure Project`
BU. Cause: **`Spaarke Basic User` holds `prvReadsprk_Project` at `Deep` depth**, and `Deep` held at the
**root** BU reaches every descendant BU.

This is **design §5.2's blocking prerequisite, still unremediated** — not a new defect. §5.2 inferred it
from a depth census on 2026-08-20; task 046 exercised the whole mechanism against a real record. The
**negative control passed** (a `Basic`-depth principal WAS denied on the same record), which is what
establishes that BU containment works correctly *once no ordinary role holds `Deep` or `Global`*.

| Fix | Blast radius (measured live 2026-08-25) | Note |
|---|---|---|
| **A — BU restructure** (§5.2's already-decided direction): users out of root into an Operations BU; secure BU becomes a **sibling** | Larger — every user's BU changes; secure BU re-parented; BU-cascade container re-seeded | Durable; survives future role edits |
| **B — narrow the depth**: `Spaarke Basic User` `prvReadsprk_Project` `Deep`(4) → `Local`(2) | **ZERO today** — all 18 real projects and all 5 human users are in the root BU, so `Local` preserves current visibility exactly | One reversible edit, but a *role* guarantee, so a later role edit can silently undo it |

**Not applied by 046 on purpose** — editing an ordinary end-user role changes every user's effective
access. B closes the exposure now at near-zero risk while A is scheduled; they are not exclusive.
Detail: design §5.1a-2. **Do NOT "fix" it by removing `sprk_project` Read from ordinary roles** — a
share confers nothing without the entity privilege, so that would silently disable all sharing.

### ⚠️ What this does to task 047's claim

047 can validly conclude **"provisioning runs end-to-end"** — worth doing, since provisioning has never
succeeded in any environment. It **cannot** conclude "isolation works" until the decision above lands.
Keep those claims separate in the report.

### The one thing that needs the OPERATOR, not the agent

**Task 047 (live provisioning validation) needs the BFF deployed to dev.** The `Deploy BFF API`
workflow is **`disabled_manually`**, so that deploy is operator-driven. Sequence:
**~~046 (agent)~~ ✅ → deploy (operator) → 047 (agent).**

### What 046 configured in live dev (`spaarkedev1`) — already done, do not redo

| | |
|---|---|
| `Secure Project Owner` | `roleid e4ebabd9-b4a0-f111-aaac-000d3a99d1d7`, in the `Secure Project` BU |
| Privileges | **exactly 1** — `prvReadsprk_Project` @ **User (`Basic`)** depth (hypothesis said 7 @ BU depth — wrong in both dimensions) |
| Held by | that one owner team; **0 users, 0 other teams** |
| `System Administrator` | **REMOVED** from the team; assignment re-proven *after* removal |
| Team members | **0** |
| Test artifacts | probe project deleted — 0 secure projects, 0 projects in the secure BU |

Runbook: [`docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md`](../../docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md) ·
write-up: [`notes/task-046-secure-project-owner-role.md`](notes/task-046-secure-project-owner-role.md)

### Still open from 046

- **Child-entity ownership** — **18 Spaarke entities via 19 lookups** carry a project lookup (the POML
  said 3); `sprk_document` carries **two** (`sprk_project` *and* `sprk_relatedproject`, so a one-lookup
  check misses half the cases). **Nothing assigns children to the secure team**, so they are unisolated
  independently of the depth defect and would stay so after it is fixed. **Needs its own task** —
  extending task 021's assign is the wrong shape (children are created continuously, long after
  provisioning returns; this needs a create-time rule). Sequence with `spaarke-secure-project-r1`.
- **FR-28's share→read assertion is untestable** until the depth fix lands — every human with
  `sprk_project` Read holds `Deep`/`Global`, so no record exists that they cannot already read.

### Live Dataverse facts task 046 needs (verified 2026-08-25 — do NOT re-derive from docs)

| Fact | Value |
|---|---|
| Secure BU | **`Secure Project`** — SINGULAR — `d9ec0b6f-80a0-f111-aaac-000d3a99d1d7`, parent = root `Spaarke`, created 2026-08-25 08:28 |
| Its default owner team | `Secure Project` — `daec0b6f-80a0-f111-aaac-000d3a99d1d7`, `teamtype=0` (Owner), `isdefault=Yes` |
| Team members | **ZERO** ✅ (design §5.1a requires this) |
| Team roles | **ONLY `System Administrator`** (`3980a53d-b0cf-3ded-37c8-4d4f9b94acef`) — 🔴 task 046 removes this |
| Roles matching `Secure%` | **NONE EXIST** — `Secure Project Owner` has never been created |
| Secure projects in dev | **ZERO — none has ever been provisioned** |
| `SP-*` per-project BUs | **NONE** — the retired mechanism never succeeded, so there is no legacy debris |
| Root BU `Spaarke`.`sprk_containerid` | `b!vzGDfDpd7km_-_H38Q6ZfbotQXLPXF9Ci71VoQmIOHUKlvxOqBsHQLrROZ5KySLh` |
| `Secure Project` BU.`sprk_containerid` | **`null`** ✅ correct by design |
| Dev BFF app service | **`spaarke-bff-dev`** in `rg-spaarke-dev` (the e2e spec's `spe-api-dev-67e2xz` default is STALE) |
| `SharePointEmbedded__ContainerTypeId` | `8a6ce34c-6055-4681-8f87-2f4f9f921c06` ✅ configured |
| `SecureProject__BusinessUnitName` | **NOT SET** → the endpoint uses the code default, which is why the singular/plural fix was load-bearing |

⚠️ **Three projects share that root-BU container id** (`Intellectual Asset Management System Patent`,
`Clarivate Plc Q3 2025 Earnings Disclosure`, `Test New Matter via Workspace`). That is the wizard's BU
cascade stamping SHARED storage onto projects — the mechanism behind both the 409 regression and design
§5.1c's isolation gap. **For task 047: assert INEQUALITY against every BU container, never presence of
a value** — a populated field is exactly the false positive.

### 🔴 Task 046's headline finding, restated so it is not lost

The owner team holds **`System Administrator`**. It is memberless so nothing is exposed *today*, but it
is one membership row from full admin rights on the BU that NFR-05 exists to guard, and review §D says
of this exact question *"None — and definitely NOT System Administrator."*

**Consequence**: task 021's escalation trigger for "the team lacks entity privileges" **cannot fire in
dev** — assignment succeeds because the team is omnipotent, not because it is correctly scoped. **A
green provisioning run in dev is NOT evidence the role is configured.**

⚠️ **046 treats design §5.1a's privilege list as a HYPOTHESIS, not a spec.** For a team that owns the
records, **User depth may suffice** and is tighter than the Business-Unit depth currently written down —
which would *narrow* NFR-05's exemption. Determine empirically; record the error that forced each
privilege you add.

---

## What 021 and 045 shipped (both on master)

**021 — provisioning matches design §5.1.** Resolves the ONE canonical BU **by name** from
`SecureProject:BusinessUnitName` (`$top=2`, fails closed on absent AND ambiguous, never falls back) →
assigns the project to that BU's **default owner team** and **reads the owner back to verify** →
creates the project's own SPE container → records it on `sprk_containerid`, **failing loudly with the
container id** if that write cannot land (ADR-003). Deleted: BU creation, account creation, both
rollbacks, three resolvers, the umbrella branch, and three response members. `sprk_externalaccount` —
the project's **CLIENT** lookup — is never written, pinned by a test.

**The live 409 regression is CLOSED.** The marker is now **ownership**, which only provisioning writes;
`sprk_containerid` was shared state, which was the whole bug.

**045 — auth-v4 integration.** `CallerRecordAccessProbe` ported off its own client secret onto
`OrderedCredentialClientProvider` (ADR-028 A4; FR-F1/FR-F2 pass with **no** allowlist or census entry).
Plus 5 Moq ctor sites, 6 fixtures needing `Graph:ManagedIdentity:Enabled`, and master's own 6 stale
tests. Full write-ups: [`notes/task-021-provisioning-stamping.md`](notes/task-021-provisioning-stamping.md)
and [`notes/ci-dark-and-authv4-integration-2026-08-25.md`](notes/ci-dark-and-authv4-integration-2026-08-25.md).

### ⚠️ What is NOT achieved yet — do not overstate this on master

- **No document isolation.** Nothing READS the project's `sprk_containerid` yet; that needs the three
  container-resolution strategies special-cased → project **`spaarke-secure-project-r1`** (design.md
  drafted, 4 open questions awaiting the owner).
- **No human can reach a secure project.** FR-28's explicit share (access teams, design §5.1b) is
  outstanding. The record is isolated but **unshared**. Still needs its own task.
- **OBO correctness is unproven.** No test performs a real exchange (P5 unreachable offline —
  `OrderedCredentialClientProvider` is `sealed`). Task **034** owns live verification.
- **Provisioning has never run successfully in ANY environment.** Task **047**.

---

## Four lessons that keep paying off — apply to every remaining task

**1. A misleading "it passed" now has FOUR causes, not two.** (a) test at the wrong level,
(b) perturbed code unreachable, (c) — task 021 — **a FAKE that ignores part of the contract**
(its fixture ignored `$top` and the discriminating `$filter` predicates, so two perturbations looked
"covered" by accident; *a fake is evidence only to the extent it refuses what Dataverse would refuse*),
and (d) — task 046 — **the platform answered from a STALE CACHE.** Dataverse's principal-privilege
cache lags role edits by ~one operation; an early 046 pass reported *"assignment allowed with zero
privileges"*, which taken at face value would have justified shipping a role that grants nothing.
**Defences**: re-probe until stable across ≥3 polls, and cross-check the `privilegeCount` reported in
any denial against the role's real privilege count. Run a zero-privilege control — if a role with no
privileges still allows the operation, every reading in that session is void.
*All four share one shape: the observation was real, but it was not an observation of the thing you
thought it was.*

**1b. Configuration-shaped assertions miss depth-shaped holes.** Task 046's headline finding —
ordinary users can read secure projects — is invisible to any check that enumerates roles "scoped to
the secure BU". `Spaarke Basic User` names that BU nowhere and reaches it anyway, via `Deep` at an
ancestor. **Reach is a property of depth held at an ancestor, not of the target.** Prefer the
empirical form: provision the record, attempt an impersonated read as a known non-admin, require
denial. Same shape as NFR-04's negative canary — **success where you expect denial is the signal.**

**2. Read the GATE, not a substitute — and check the gate EXISTS.** A conflicted PR produces **NO
gate, not a red one**: GitHub cannot compute `refs/pull/N/merge` and dispatches zero workflows. Two
pushes went unadjudicated while a local suite was green. **Verify a `github-actions` check suite exists
for the SHA** (`gh api repos/{owner}/{repo}/commits/{sha}/check-suites`) before claiming anything.
Related: master's Router can be green while a whole test project fails, because tier1 runs a
**changed-surface filtered subset** and tier2 (which runs everything) is **advisory**.

**3. A merge conflict is not the only way two branches collide.** Task 045 hit the same invisibility
pattern three times — a duplicated credential site, a duplicated stale-test repair, and a duplicated
`.csproj` glob that merged **textually clean and semantically broken** (`NETSDK1022`, whole test
project fails to build, no conflict to warn you). When merging a long-lived branch, check for
*semantic* duplicates, not just textual ones.

**4. Mocking at a seam proves the CALLER, never the CALLEE.** 045 found `CallerRecordAccessProbe` had
**zero** test coverage because every fixture substituted it — its precondition logic could be inverted,
opening the whole delegation gate, with the suite green.

---

## Verified baselines (as of `290d9ab79`, on master)

- **All 7 test projects: 11,715 passed / 0 failed** — `Sprk.Bff.Api.Tests` 11,075 ·
  `Spe.Integration.Tests` 372 · `Sprk.Bff.Api.IntegrationTests` 96 · `Spaarke.ArchTests` **69** ·
  `Spaarke.Scheduling.Tests` 46 · `Spaarke.Core.Tests` 45 · `RecordSyncJob.IsolatedTests` 12
- **Publish 43.75 MB** compressed incl. PDBs (ceiling 60). `--vulnerable` clean. BFF build 0 errors.
- **`Router = SUCCESS`**; main repo local master synced and rebuilt clean from that checkout.

**The suite gate is `dotnet test` at the root PLUS three projects it does not pick up:**

```
dotnet test -c Debug                                              # 4 projects
dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj
dotnet test tests/unit/Spaarke.Core.Tests/Spaarke.Core.Tests.csproj
dotnet test tests/unit/RecordSyncJob.IsolatedTests/RecordSyncJob.IsolatedTests.csproj
```

Running one project and reporting "full suite green" is how six tasks' worth of breakage was missed.

⚠️ `Sprk.Bff.Api.Tests` **silently vanishes from a root `dotnet test`** when it fails to BUILD (exit 1,
no `Failed!` line). If it is absent from the output, build it explicitly before believing anything.

---

## Recommended order

**046 → [operator deploy] → 047 → 025 → 023 → 029 → 028 → 024**, with **026 and 027 runnable any
time**. **026 is higher value than its position suggests** — it repairs
`secure-project-fields-schema.md`, the stale doc that CAUSED Critical findings C4/C5.

Also open: **Phase 0's 011, 012, 013, 015, 018, 020**, and a task still needed for **FR-28's access
teams** (design §5.1b).

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
| **A zero-failure perturbation has TWO causes — distinguish them** | Either the test is at the wrong level, **or the perturbed code is unreachable**. Task 022's `BulkDownloadAuthorizationFilter` catch inverted to fail-open broke 0 of 30 — not a coverage gap: `AuthorizeAsync` absorbs its own exceptions, so nothing reaches that catch. Proved with a two-factor experiment (force `AuthorizeAsync` to throw outside its try → 14 failures; do that AND invert the catch → 17; **the 3-test delta IS the guard's coverage**). Rewriting tests would have added coverage for a path that cannot execute. **Check reachability before "fix the test".** |
| **A doc comment asserting "enforcement happens elsewhere" is a claim to verify, not evidence** | Task 022 found four. `BulkDownloadAuthorizationFilter` said twice that per-document access was "enforced at Dataverse lookup time via the user's identity (same model as `preview-url`)" — the lookup is app-only, and `preview-url` had no authorization either, so the claim cited a route making the same empty claim. `/checkout` claimed OBO+PCF enforcement on an app-only path. But `share-link`'s identical-sounding claim was **TRUE** (`CreateSharingLinkAsUserAsync` really does call `ForUserAsync`). **Check the named mechanism — the pattern is valid, the instances vary.** |
| **State the blast radius you verified, not the one that sounds worse** | I nearly shipped "any authenticated caller could mint a url for any document by GUID" for the five URL-minting reads. They use OBO, so Graph already enforced SPE access; the gate is a second, narrowing boundary. Overstating a finding in a comment is the same defect as understating one — both mislead the next reader. |
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
