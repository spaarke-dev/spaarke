# Task 034 — NFR-04 impersonation negative canary (merge gate for task 036)

> **Status**: implemented · **Date**: 2026-09-03 · **Rigor**: FULL
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

## 5. Live-tenant verification checklist — NOT PERFORMED

Tasks 005, 007 and 008 each appended live-tenant observations to this task on the premise that 034
"is the first task that has a real tenant". **It is not.** This execution had no Dataverse access, no
provisioned canary user, and no ability to create one (a new systemuser + custom security role is a
change to the customer's dev environment and an owner action). The following remain **open and
unverified**, and should be re-homed onto whichever task actually acquires tenant access:

- **From task 005** — (a) a caller with Write/Create gets those flags in the `RetrievePrincipalAccess`
  snapshot, not just Read; (b) grep BFF logs for `RPA-FALLBACK` (if firing, the Read ceiling is silently
  back); (c) the BFF app user holds the privileges RPA needs on the app-only path.
- **From task 008** — (a) `WhoAmI()` returns the CALLER's systemuserid under an OBO Dataverse token;
  (b) RPA answers for a `sprk_project` target, not only `sprk_documents`; (c) grep for
  `DELEGATION-RPA-UNAVAILABLE` as well as `RPA-FALLBACK`.
- **From task 007** — (a) a past-dated grant disappears from `GET /api/v1/external/me`; (b) a grant
  expiring TODAY still confers access (the `ge` not `gt` decision); (c) a grant with no expiry is
  unaffected — **check this one first**, it is an access outage if broken; and confirm no 400 (a 400
  surfaces as an empty grant set, i.e. a silent outage).

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
