# Task 034 — NFR-04 impersonation negative canary (merge gate for task 036)

> **Status**: COMPLETED — manual live gate PASSED 2026-10-03 (§8) · **Date**: 2026-09-03 (built), 2026-10-03 (live run) · **Rigor**: FULL
> **Deliverables**: `tests/integration/auth/UnifiedAccessControl/ImpersonationNegativeCanary.cs`,
> `…/ImpersonationCanaryEnvironment.cs`, `…/ImpersonationNegativeCanaryTests.cs`,
> `tests/integration/auth/README.md` § "NFR-04 impersonation negative canary"

---

## 1. What was built, and why it is shaped this way

Three layers, because the single-layer design the POML implies could not be both truthful and runnable.

| Layer | Tenant? | Blocking in CI today? | What it establishes |
|---|---|---|---|
| **Invariant + perturbation** — `ImpersonationNegativeCanary.Evaluate` / `EvaluateExactness` as a pure verdict function, exercised by 8 tests | No | **Yes** | That the canary's assertion actually FAILS for the inert case. An assertion nobody has watched fail is an assertion nobody has verified. |
| **Live tenant** — Tests 1–3 from investigation 08 §3d | Yes | No (see §4) | The real row-set comparison against the provisioned canary user. |
| **Config tripwire** — `Fr20ImpersonatedRootSetFlag_…RequiresAProvisionedCanary` | No | **Yes** | That `ExternalAccess:ImpersonatedRootSets:Enabled` cannot be turned on in checked-in config while the canary is unprovisioned. |

**The design decision that matters**: hoisting the comparison out of the live test into a pure function.
Investigation 08 §3d specifies the assertions inline in a live test. Written that way, the entire NFR-04
gate would consist of code that has never executed in this repo — the tenant does not exist in CI, and
no canary user is provisioned anywhere. The gate would have been a claim. Hoisting the verdict lets the
suite feed it the exact fail-OPEN state on every run, with no tenant, and assert it reports failure.

## 2. Perturbation evidence (the inversion check, POML step 4)

The POML asks for a one-time manual inversion against an admin-privileged user. No non-admin canary user
exists and none could be provisioned from here, so the inversion was performed **on the mechanism**
instead — which is strictly more repeatable, and is now permanent regression coverage rather than a
note in a file.

Baseline: `dotnet test --filter FullyQualifiedName~ImpersonationNegativeCanaryTests` → **13 passed, 0 failed**.

| # | Perturbation applied | Expected | Observed |
|---|---|---|---|
| **P1** | Weaken invariant B from `impersonated.Count == appOnly.Count` to `> appOnly.Count` — i.e. accept equality as a pass, which IS the inert-impersonation state | Perturbation tests go red | **2 failed** — `Evaluate_WhenImpersonatedSetEqualsAppOnlySet_ReportsInertRatherThanPassing`, `Evaluate_WhenRowsAreDuplicated_ComparesDistinctIdsAndStillReportsInert` |
| **P2** | Add `"ExternalAccess": { "ImpersonatedRootSets": { "Enabled": true } }` to `appsettings.Testing.json` | Tripwire + all 3 live tests go red | **4 failed**, tripwire message naming `appsettings.Testing.json`; live tests threw the full provisioning contract |
| **P3** | Same key with an indeterminate value `#{IMPERSONATED_ROOT_SETS}#` | Treated as enabled → red | **4 failed** |
| **P4** | Same key with literal `false` | Green (the gate is not a blanket wall) | **13 passed** |
| **P5** | `SPAARKE_CANARY_REQUIRED=true`, canary env absent | 3 live tests FAIL with the provisioning contract, not a silent pass | **3 failed**, message named the missing variable, the required role shape, and `prvActOnBehalfOfAnotherUser` |

All perturbations reverted; `appsettings.Testing.json` and the invariant restored to their committed
state (verified clean via `git status`).

**P1 is the one that answers "does the canary fail when impersonation is inert?"** — yes, and the test
that proves it is itself proven load-bearing, because weakening the invariant is what turns it red.

## 3. What the task POML got wrong

1. **`<pattern name="live-Dataverse seam test" location="tests/integration/seam/ExternalAccess/StandingGrantRuntimeUnionSeamTests.cs">` — "config-driven live environment, seeded records, cleanup" does not exist.**
   That file is fully mock-based (`Mock<IDataverseService>`, `Mock<IMembershipResolverService>`, a fake
   participation service). There is **no live-Dataverse test harness in this repo** to copy. The nearest
   precedent is `Phase2EndToEndTests.LiveMode_*`, whose convention is *skip-via-return on a missing env
   var* — i.e. a silent green, exactly what NFR-01 forbids. Both had to be departed from.

2. **`<file role="new">tests/integration/auth/README.md</file>` — the file already exists**, and its
   headline warning ("This directory is EMPTY and NOT COMPILED") had been false since 2026-08-25. It was
   corrected in place rather than overwritten; the stale text is retained under a `<details>` for
   provenance.

3. **Output path.** The POML names `tests/integration/auth/ImpersonationNegativeCanaryTests.cs`; the
   files landed in `tests/integration/auth/UnifiedAccessControl/` per the convention the csproj comment
   and the README both state ("new auth tests add files under `tests/integration/auth/{Module}/`"), and
   alongside the 36 sibling UAC auth tests. Task 036 should cite the `UnifiedAccessControl/` path.

4. **Test 3 was already written.** Task 001's `ImpersonationFailClosedTests` already asserts
   `RetrieveMultipleImpersonatedAsync(…, Guid.Empty)` throws. Rather than duplicate it (CLAUDE.md §11),
   Test 3 re-pins the refusal **on the live-configured instance**, so the guard cannot become conditional
   on configuration only the live path supplies. The distinction is documented in both files.

5. **Constraint NFR-01 ("must FAIL, never skip") is unsatisfiable as literally written.** The class
   compiles into `Sprk.Bff.Api.Tests`, which runs on every CI build and every developer machine, none of
   which have a tenant. A test that always fails there is deleted within a week, and xUnit 2.9.0 has no
   dynamic skip (no `Assert.Skip`; `Xunit.SkippableFact` is not referenced, and adding a package for
   this was not justified). The rule was therefore applied to its actual subject — *the canary may not
   be absent while the gate is open* — with "open" defined as `SPAARKE_CANARY_REQUIRED=true` **or** the
   FR-20 flag being enabled in checked-in config. In both states an unprovisioned canary is a hard
   failure (P2, P5). This is a **CLAUDE.md §6.5 path A** narrowing, documented at the point of decision.

6. **`appsettings.template.json` is not valid JSON.** It contains bare deploy tokens (e.g.
   `"RecordMatchingEnabled": #{RECORD_MATCHING_ENABLED}#`), so `ConfigurationBuilder.AddJsonFile` throws
   on it — the first tripwire implementation crashed on exactly this. Skipping unparseable files would
   have blinded the gate to the one file a deployment renders, so the tripwire text-scans instead.

7. **Steps 3 and 5 of the POML could not be completed as written** — see §4 and §5.

## 4. 🔔 ESCALATION — the live canary is not a CI-blocking check (POML escalation trigger 2)

**Fired as designed, not worked around.** No workflow in this repo can reach Dataverse:
`ci-tier1-blocking.yml`, `ci-tier2-advisory.yml` and `nightly-health.yml` hold no environment credential,
no canary identity and no seeded org. Task 034's step 3 says "wire the tests into CI as a blocking check
… if integration tests are not run in CI today, wire this class into whatever gate DOES block merges" —
which was done for the two tenant-free layers. The **live** layer cannot be wired without a decision:

- **(A) Scheduled canary + required manual gate** — a `nightly-health.yml` job with federated credentials
  to dev; the FR-20 rollout checklist requires a green canary run cited in the PR. Cost: one federated
  credential and one canary identity per environment.
- **(B) Dataverse secrets in CI** — full blocking check on every PR. Cost: a standing Dataverse
  credential in GitHub Actions, a materially larger blast radius, on a repo whose auth-v4 work has been
  systematically *removing* standing secrets.

**Recommendation: (A).** It buys the automated signal that matters (config drift, revoked privilege,
proxy stripping the header) at nightly cadence without a standing PR-scoped credential, and the tripwire
already prevents the flag being enabled by default in the meantime.

Escalation trigger 1 (no non-admin canary user can be provisioned) is **also live**: none exists in dev
today. It does not block *this* task — the mechanism is complete and proven — but **task 036 must not
proceed until the canary can run truthfully against a provisioned user.** The provisioning procedure is
in `tests/integration/auth/README.md`.

## 5. Live-tenant verification checklist — RECORDED 2026-10-03 (dev, read-only)

The checklist tasks 005, 007 and 008 parked here, executed in the same manual session as §8. Caller for
every read: `ralph.schroeder@spaarke.com` (systemuser `1d02f31c-1872-f011-b4cb-7c1e52671ad0`, System
Administrator) through `az`; canary = `uac.child.user@demo.spaarke.com` (`d6f8f439-40bf-f111-a05b-3833c5e9614d`).

| Item | Observation (2026-10-03T20:55Z) | Verdict |
|---|---|---|
| **005(a)** RPA flags beyond Read | `RetrievePrincipalAccess` for the canary on BU1 matter `491b1efe-e562-f111-ab0c-000d3a4d8152` = `ReadAccess, WriteAccess, AppendAccess, AppendToAccess, CreateAccess, DeleteAccess, ShareAccess, AssignAccess` | PASS — the snapshot carries every flag, not only Read |
| **005(b) / 008(c)** `RPA-FALLBACK`, `DELEGATION-RPA-UNAVAILABLE` in BFF logs | Application Insights `spe-insights-dev-67e2xz` (role `spaarke-bff-dev`): **0** of each, but the store only held traces since **2026-10-03T19:56Z** (3,915 traces) | PASS within a ~1-hour window only. **Re-check after the batch-4 deploy** over a full day |
| **005(c)** the BFF app user's privileges | `# mi-bff-api-dev`, `# spaarke-bff-api-prod`, `SDAP-BFF-SPE-API` (+ Delegate, Spaarke Ontology Service) hold **System Administrator**, which holds `prvActOnBehalfOfAnotherUser` at depth **8 (Global)**. ⚠️ `# Spaarke DMS-SPE Dev 1` (named in amendment 4) is **disabled** | PASS. Correction to amendment 4: the DMS-SPE Dev 1 user is disabled, so it is not a holder in practice |
| **008(a)** `WhoAmI()` under a delegated token | `WhoAmI` with the caller's delegated user token returned `1d02f31c-1872-f011-b4cb-7c1e52671ad0` = the caller | PASS for a delegated token (the token class an OBO exchange yields); the BFF's own OBO path is exercised by the post-deploy gates |
| **008(b)** RPA on a `sprk_project` target | canary on BU1 project `e070fa52-e662-f111-ab0c-000d3a4d8152` = the full flag set above; on SECURE project `65a3fab2-77a5-f111-aaad-70a8a590c51c` = **None** | PASS |
| **007(a,b,c)** grant expiry | The BFF's `ExpiryPredicate` filter (`statecode eq 0 and sprk_expiresdate ge 2026-10-03`) runs live with **HTTP 200** (no 400, so no silent empty-set outage). Dev holds 31 active grants: all 31 future-dated; **0** past, **0** expiring today, **0** with no expiry, so (a) and (b) cannot be exercised on live data, and (c) is **superseded by owner D-1 A** (a null expiry confers NOTHING). The rule is pinned by the in-memory mirror's unit tests | Query shape PASS; (a)/(b) through `GET /api/v1/external/me` need a signed-in CIAM user → the owner's CIAM manual gates (with 136/037/039) |

## 6. Residual (filed, not done)

The **runtime canary** variant recommended by investigation 08 §3d — the same strict-fewer check as a
startup/scheduled probe emitting a metric + alarm, guarding *production* config drift that no CI test
can see. Not mandated by NFR-04, so out of scope here; it is the natural companion to option (A) above.

## 7. Notes on parallel safety

`projects/unified-access-control-r2/current-task.md` was deliberately **not** updated: a concurrent
agent holds it for task 076, and overwriting it would destroy that agent's recovery state. Nothing under
`.claude/`, `Api/OBOEndpoints.cs`, `EntityCreationService.ts`, the DocumentUploadWizard, or
`Services/Communication/**` was touched. No file under `src/server/**` was modified (POML constraint) —
verified by `git status`.

## 8. Manual live gate — RUN 2026-10-03 (amendments 1-5)

**Result: all four live tests PASS.** Run from the batch-4 integration worktree (`C:\wt4i`, branch
`integ/uac-r2-batch4`, the canary code unchanged since 2026-09-03) at 2026-10-03T20:47:35Z:

| Test | Result |
|---|---|
| `ImpersonatedMatterRead_AgainstTheCanaryUser_ReturnsAStrictSubsetAndStrictlyFewerRows` | PASS — impersonated **59** ⊂ app-only **61** |
| `ImpersonatedMatterRead_AgainstTheCanaryUser_ReturnsExactlyTheSeededMatters` | PASS — impersonated = the independently derived 59 |
| `RetrieveMultipleImpersonatedAsync_OnTheLiveConfiguredService_RefusesAnEmptyCaller` | PASS |
| `HelperStampedMatterRead_AgainstTheCanaryUser_ReturnsAStrictSubsetAndStrictlyFewerRows` | PASS — 59 ⊂ 61 |

All 10 tenant-free tests in the class also pass, including the config tripwire. None halted as NOT RUN
(`SPAARKE_CANARY_REQUIRED=true`).

**The canary (amendment 2: an existing user; none created, relocated or re-roled for this run).**
`uac.child.user@demo.spaarke.com`, systemuser `d6f8f439-40bf-f111-a05b-3833c5e9614d`, business unit
**Spaarke Business Unit 1**. It was created on 2026-10-03 under owner round 11 as the batch-4 child-BU test
user, before and independently of this run.
- Direct roles: **Spaarke Basic User**, **Spaarke Core User** (BU1 copies).
- Team: **Spaarke Business Unit 1** (default team) → Spaarke Reporting Access Viewer, Spaarke Basic User,
  Spaarke AI Analysis User, Spaarke Office Add In User.
- `prvReadsprk_matter` depth: Basic User **4**, Core User **4**, Office Add In User **4** (Deep); the other
  two roles hold none. **No Organization depth** anywhere (amendment 3 satisfied).
- Hierarchy security: `ishierarchicalsecuritymodelenabled = false`, `usepositionhierarchy = false`.

**Expected set derivation (amendment 2 / the exactness criterion), app-only, never from the impersonated
read.** Deep read = matters whose `owningbusinessunit` is BU1 or a descendant (BU1 has none) → **59**;
plus `principalobjectaccess` rows with the Read bit for the user or its team → **0**. Total **59** of the
org's **61**. The two outside it are owned in the ROOT business unit "Spaarke":
`0ee64da4-9dbe-f111-aaaf-0022482913fc` (owner Ralph Schroeder) and `ced2c3d9-47bf-f111-aaaf-0022482913fc`
(owner the Spaarke team). Margin: 2 rows.

**Inversion check (POML step 4), 2026-10-03T20:48:08Z.** With `SPAARKE_CANARY_SYSTEMUSERID` pointed at the
admin caller (`1d02f31c…`), Tests 1 and 4 both **FAILED** with "IMPERSONATION IS INERT: the impersonated
read returned the SAME 61 row(s) as the app-only read". The gate sees the failure it exists to catch.

**Credential note — the README was incomplete.** The unit test assembly's `TestOutboundNetworkGuard`
restricts `DefaultAzureCredential` to `EnvironmentCredential` and blocks outbound HTTP, so a run that
follows the old README fails every live test with `CredentialUnavailableException`. The run needs
`AZURE_TOKEN_CREDENTIALS=AzureCliCredential` and `SPAARKE_TESTS_ALLOW_OUTBOUND=1`; the README now says so.
The ambient credential was the operator's `az` login (System Administrator), not the BFF application
user, so the app user's own `prvActOnBehalfOfAnotherUser` is evidenced by role (§5, 005(c)) and by the
deployed BFF's impersonated reads, not by this run.

**Consequence:** task 036 is unblocked. The live layer must be re-run (same procedure) before 036 merges
if the canary code or the impersonation primitive changes, and before every FR-20 rollout.
