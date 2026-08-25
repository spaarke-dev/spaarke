# CI has been dark for two pushes — root cause, and the auth-v4 integration it exposed

> **Status**: 🔔 **OWNER DECISION REQUIRED.** Diagnosis complete, both causes isolated, fix path
> known for each. Nothing has been changed to address them — the trial merge was **aborted**, not
> committed, because one of the two causes is security-path auth code (CLAUDE.md §6).
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
