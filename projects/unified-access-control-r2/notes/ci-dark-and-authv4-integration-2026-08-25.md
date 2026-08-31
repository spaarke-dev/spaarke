# CI has been dark for two pushes — root cause, and the auth-v4 integration it exposed

> **Status**: ✅ **RESOLVED by task 045, 2026-08-25.** `Router = SUCCESS` at `c5edf2448`, PR #812 is
> `MERGEABLE`. See [§ RESOLVED](#-resolved--task-045-2026-08-25) at the end for the outcome — including
> **two causes nobody predicted**: the probe had zero test coverage, and master had shipped 6 stale
> tests invisible to its own gate. The diagnosis below is kept as written, because how the blocker was
> found matters as much as the fix.
> **Date**: 2026-08-25 · found while pushing task 021.

---

## 1. The headline: `CI / Router` never ran on the last two commits

| Commit | Pushed | `github-actions` check suite |
|---|---|---|
| `ffc2cb1de` | 2026-08-25 02:46Z | ✅ created, Router **success** |
| `2035b1d16` (docs, last session) | 2026-08-25 13:39Z | ❌ **none** |
| `99408eee5` (task 021) | 2026-08-25 14:48Z | ❌ **none** |

Not "queued", not "failed" — **no workflow was ever dispatched.** `gh pr checks 812` reports
*"no checks reported on the branch"*, and the commit has exactly one check suite (`claude`, queued)
where a healthy sibling PR has four `github-actions` suites.

⚠️ **In the previous session I reported that `2035b1d16`'s "run is starting now."** That was an
assumption stated as fact, and it was wrong — no run ever started. The same class of error the
project's own carried-forward lesson warns about: *a green local suite is not CI; read the gate, not
the substitute.* Here there was no gate to read at all.

### Root cause

```
PR #812:  mergeable = CONFLICTING     mergeStateStatus = DIRTY
PR #811:  mergeable = MERGEABLE       mergeStateStatus = UNSTABLE   ← runs fine
```

`ci-router.yml` triggers on `pull_request: branches: [master]` (it deliberately carries **no**
`paths:` filter). When a PR is conflicted, GitHub cannot compute `refs/pull/812/merge` and therefore
**dispatches no workflows for the PR at all**. Every other explanation was ruled out first: all
workflows are `active`, Actions is firing repo-wide (a sibling branch ran 20 minutes earlier), and
the branch's own workflow files are byte-identical to master's.

**This is a general trap worth remembering: a conflicted PR does not produce a red gate, it produces
NO gate.** Silence reads like "nothing to report" and is indistinguishable from "not yet started".

### The conflict itself is trivial

One file, `projects/INDEX.md`, one hunk: master inserted the `spaarke-auth-v4-dataverse-MI` row at
the position this branch holds the `unified-access-control-r2` row. Both rows belong; keeping both
resolves it. Everything else auto-merges — including `DataverseAccessDataSource.cs` and
`ExternalAccessContractTests.cs`, the two files most at risk.

---

## 2. What the trial merge exposed — and why it was aborted, not committed

Merging `origin/master` and running the full suite **before** committing produced **22 failures**.
Two independent causes.

### Cause A — 🔔 security path, needs an owner decision

**`Infrastructure/ExternalAccess/CallerRecordAccessProbe.cs:134,137`** — created by **task 008** on
this branch — builds its own `ConfidentialClientApplicationBuilder` and calls
`.WithClientSecret(clientSecret)`. Master's auth-v4 forcing functions both fail on it:

- **FR-F1** `CredentialGuardTests` — *"no secret-bearing confidential credential under `src/server/**`
  outside the allowlist"*
- **FR-F2** `CredentialCensusTests` — *"UNLISTED confidential-client site in CallerRecordAccessProbe.cs"*

**This is item D1, and its premise has expired.** D1 was resolved on 2026-08-23 as an **ADR-028 A4
path-A exception**, explicitly *"to be handled in the broader MI migration"*. That migration is
auth-v4 — which **completed on 2026-08-24 and closed exception E-3**. It could not have handled this
site, because the site lives on an unmerged branch and was invisible to it. So the exception's
stated home no longer exists.

**Allowlisting is the wrong move, and the guard says so in its own failure message:**

> *"A failure here is NOT a prompt to update the number. The origin assessment for this project
> counted five confidential-client sites when there were eight, and the two it missed were found by a
> later audit rather than by anything automatic."*

**The fix the guard prescribes**: `CallerRecordAccessProbe` performs OBO, so it should inject
`IConfidentialClientProvider` and construct no credential at all — *"ONE binding point, so the
credential can be changed in configuration instead of in nine files."*

That is a change to a live authorization seam that currently gates **six external-access mutations
plus the Office-save path**, on a branch where RPA is already load-bearing and unverified against a
live tenant (task 034). It is not a drive-by inside a merge commit. **CLAUDE.md §6 requires human
input for security-sensitive code**, which is why nothing was changed.

### Cause B — mechanical, ~20 failures, one root cause

Master widened the `DataverseWebApiClient` constructor:

```csharp
public DataverseWebApiClient(
    IConfiguration configuration,
    ILogger<DataverseWebApiClient> logger,
    TokenCredential? credential = null,              // NEW
    IConfidentialClientProvider? confidentialClients = null)   // NEW
```

Moq needs an **exact** constructor match for a class proxy, so every
`new Mock<DataverseWebApiClient>(config, logger)` now throws
`Can not instantiate proxy of class`. Optional parameters do not help.

**Five fixtures affected** (one is from today's task 021):

```
tests/integration/auth/UnifiedAccessControl/DelegationRuleTestFixture.cs
tests/integration/auth/UnifiedAccessControl/GrantLifecycleCharacterizationTests.cs
tests/integration/auth/UnifiedAccessControl/ProjectClosureCascadeTests.cs
tests/integration/auth/UnifiedAccessControl/SpeRevokeMatcherTests.cs
tests/integration/data-mutation/ExternalAccess/ProvisionProjectTestFixture.cs
```

Fix is one line per fixture — pass the two new arguments explicitly. Cheap, and independently
verifiable. But fixing it alone leaves the build red on Cause A, so it is not worth doing in
isolation.

---

## 3. What is true right now

- **Task 021 is complete, committed and pushed** at `99408eee5`, and is **unaffected** by any of the
  above: 19/19 provisioning tests green on the branch as it stands, all seven projects 11,443/0
  before the trial merge, publish 43.70 MB (zero delta), `--warnaserror` clean, NetArchTest 36/36 on
  the branch, code-review + adr-check 0 violations.
- **The trial merge was aborted.** The branch is exactly as pushed; nothing from master is mixed in.
- **CI still cannot run on PR #812** until the conflict is resolved — and resolving it pulls master
  in, which surfaces Causes A and B. The two problems are therefore coupled in practice: *there is no
  way to get a CI verdict on this branch without first doing the auth-v4 integration.*

## 4. Recommendation

File this as **one new task: "auth-v4 integration"**, ordered ahead of the remaining Phase 0/0b work,
because nothing on this branch can be CI-verified until it lands. Three parts, in order:

1. **Cause A** — migrate `CallerRecordAccessProbe` to `IConfidentialClientProvider`. Security-path;
   FULL rigor; needs the owner's ruling first on whether to (a) migrate now, (b) re-scope the D1
   path-A exception with a new, still-valid home, or (c) add a census/allowlist entry with a written
   reason — noting the guard explicitly argues against (c).
2. **Cause B** — update the five `Mock<DataverseWebApiClient>` construction sites.
3. **Merge master**, resolve `projects/INDEX.md` by keeping both rows, re-run all seven projects, push
   and **confirm `CI / Router` actually renders a verdict** before claiming anything is green.

Two smaller items to fold in while there:

- The auth-v4 PR comment on #812 (2026-08-24) asks for two corrections in
  `docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md` — it still says the client-secret path is
  retained as a local-dev fallback, which stopped being true on 2026-08-24. They supplied replacement
  wording.
- **Push discipline, restated**: after the last push of a session, *verify a check suite exists* —
  not merely that a run was "starting". Absence of runs is a state to check for explicitly, because
  it looks exactly like success from a distance.

---

# ✅ RESOLVED — task 045, 2026-08-25

## The gate rendered a verdict: `Router = SUCCESS`

**First CI-adjudicated state of this branch since `ffc2cb1de`.** Commit `c5edf2448`, run `32874968847`.

| Check | Result |
|---|---|
| **`Router`** (the gate) | ✅ **SUCCESS** |
| Tier 1 / Compile (Debug) | ✅ |
| Tier 1 / Arch Tests (MUST-NOT subset) | ✅ |
| Tier 1 / Classify Tier 1 Surfaces | ✅ |
| **Tier 1 / Auth Smoke** | ✅ |
| **Tier 1 / Changed-Surface Integration Smoke** | ✅ |
| Tier 2 / Full Unit Tests (Debug) | ✅ — the root `dotnet test`, so CI independently validated the stale-test repairs |
| Tier 2 / ADR Compliance · Format · Lint · Plugin Size · Last Reviewed | ✅ |
| Tier 2 / Markdown Link Validator | ⚠️ cancelled — **advisory, excluded from adjudication by construction** (the 2026-08-24 Router fix) |
| PR #812 overall | **22 SUCCESS · 0 FAILURE · 1 cancelled (advisory) · 1 in-flight (`SDAP CI`)** |
| `mergeable` | **MERGEABLE** (was `CONFLICTING`) |

⚠️ Note on reading the run: the *workflow run's* conclusion is `cancelled` because one advisory job was
cancelled, while the **`Router` job itself is `success`**. The Router job is the required check — that
separation is precisely what the 2026-08-24 repair engineered, and this is the first time it has been
exercised in anger.

## What actually had to be fixed — four things, not two

The trial merge predicted two causes. Executing it found four.

**A. The credential (predicted).** `CallerRecordAccessProbe` ported onto
`OrderedCredentialClientProvider`, faithfully following master's `DataverseUserClient`. Client asked
for **per exchange**, not held, so a transient blip cannot pin the path to a fallback. **No allowlist
or census entry added** — the census guard's own message argues against exactly that, and FR-F1/FR-F2
now pass on the merits.

**B. The Moq ctor sites (predicted) — but the real cause was one layer deeper.** Widening the ctor was
only half of it: master also made `DataverseWebApiClient` and `DataverseWebApiService` select their
credential from `Graph:ManagedIdentity:Enabled` **and throw** when it is off with no provider. Six
fixtures were still setting `API_CLIENT_SECRET` / `Dataverse:ClientSecret` — pointing at a credential
deleted from Key Vault on 2026-08-24. Fixed by enabling the MI flag in the fixture configs (the
branch's `DefaultAzureCredential` is lazy and never authenticates in a fully-stubbed double).

**C. NOT PREDICTED — the probe had ZERO test coverage.** Every fixture substitutes it
(`RemoveAll<CallerRecordAccessProbe>`); `DelegationProbeRetryPolicyTests` covers only a pure static.
The real class had never been constructed by any test, so its precondition logic could be inverted —
opening the entire delegation gate — with the suite green. Added
`tests/integration/auth/UnifiedAccessControl/DelegationProbeFailClosedTests.cs` (7 tests, KEEP path).
**Task 017's lesson, again: mocking at a seam proves the CALLER, never the CALLEE.**

**D. NOT PREDICTED — master shipped 6 stale tests.** Master deleted `GET /api/containers` and
`GET /api/drives/{id}/children` from `DocumentsEndpoints.cs` with a documented zero-caller sweep, and
left 6 tests asserting those routes are *registered*. They failed on the literal assertion "should be
registered" for deliberately retired routes. Retired entries removed from the two data-driven lists (2
tests now **pass**); the 2 Facts + 1 Theory that exclusively target them are **skipped with reasons**
rather than retargeted — pointing an authorization test at a different endpoint silently changes WHICH
policy is under test, and that is the deletion owner's call.

> **Why master's own CI never caught D**: Tier 1 runs a **changed-surface FILTERED subset** of
> `Spe.Integration.Tests`, and Tier 2 — which runs everything — is **advisory**. So a whole-suite
> failure in that project is invisible to the gate on master. Worth knowing independently of this task;
> it is the same "the gate is not what you think it is" shape as the conflicted-PR finding.

## Perturbations — 5 run, 1 bit, and the four zeros are each explained

| # | Perturbation | Failures | Cause of a zero |
|---|---|---|---|
| **P1** | **no-credential path GRANTS instead of denying** | **7** | — |
| P2 | `OboAvailable` ignores the provider | 0 | **(b) absorbed** — proceeding NREs into the generic fail-closed catch, which denies. Security property holds either way |
| P3 | `OboAvailable` ignores tenant + client id | 0 | **(b) absorbed** — same |
| P4 | remove the `InvalidOperationException` catch | 0 | **(b) by design** — the generic catch still denies; that catch buys *diagnosability* (a distinct log), not the outcome. Predicted before running |
| P5 | `MsalException` path GRANTS | 0 | **(a)+(b) GENUINE GAP** — unreachable offline; needs a provider yielding a client whose exchange fails, and `OrderedCredentialClientProvider` is `sealed` so it cannot be stubbed |

P1 is the one that matters, and before file **C** existed it would have failed **zero** tests. That is
the whole value of the addition.

## ⚠️ What is NOT proven, and is not claimed

**OBO correctness.** No test performs a real exchange. Reaching the MSAL failure path needs a live
tenant and a real user assertion — task 034's obligation, which already carries the same duty for task
005's document-path use of `RetrievePrincipalAccess`. What IS proven here is the **deny** direction,
which is the direction a mistake would break, plus that the secret-bearing credential is gone
(FR-F1/FR-F2 executable, not asserted).

## Verification

- **All 7 test projects: 11,538 passed / 0 failed** — `Sprk.Bff.Api.Tests` 10,917 ·
  `Spe.Integration.Tests` 373 · `Sprk.Bff.Api.IntegrationTests` 96 · **`Spaarke.ArchTests` 56 incl.
  FR-F1 + FR-F2** · `Spaarke.Scheduling.Tests` 46 · `Spaarke.Core.Tests` 45 ·
  `RecordSyncJob.IsolatedTests` 12
- Publish **43.71 MB** compressed incl. PDBs (+0.01 vs task 021; ceiling 60). `--vulnerable` clean.
- BFF `--warnaserror`: 0 errors (7 pre-existing `CS0618` in `Registration` code, none in changed files).
- The two `DATAVERSE-ACCESS-LAYER-ROUTING.md` corrections auth-v4 requested are applied, with the
  superseded wording quoted so it cannot quietly return.

## Follow-up worth filing (not done here)

Roughly ten test files still set `API_CLIENT_SECRET` / `Dataverse:ClientSecret` in their config. They
pass — the key is simply ignored now — but they are dead references to a credential deleted from Key
Vault. Harmless today; misleading to the next reader who takes them as evidence the secret path exists.
A sweep belongs with whoever owns test hygiene, not in a merge commit.
