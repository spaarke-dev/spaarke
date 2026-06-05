# D-01 — Master CI Failure Disposition (Run 26755019759)

> **Date**: 2026-06-01 (project-pipeline initialization)
> **Project**: github-actions-rationalization-r1
> **Phase**: 0 (Inventory + Baseline)
> **Related**: spec FR-02, Risk R1, NFR-01

---

## Context

On 2026-06-01 12:31 UTC, the first master `SDAP CI` run after PR #314 merged (commit `1e735025`) failed. PR #314 was the predecessor project's (`sdap-bff.api-test-suite-repair`) terminal commit; its workflow-level scope was limited to a 1-line YAML duplicate-key removal in `.github/workflows/sdap-ci.yml` (commit `c9863276`). That fix made the workflow actually load and execute — and on its first real execution against the current `src/` tree, it surfaced two pre-existing problems that had been masked while the workflow was silently failing in 0 seconds.

This is a Phase-0-gating concern per Risk R1: if `sdap-ci.yml` remains red on master, the project must decide whether to (a) fix the underlying causes (forbidden in this project — NFR-01 prohibits `src/` changes), (b) defer and document, or (c) confirm it self-resolves on a subsequent run.

---

## Evidence

### Failure details

- **Run ID**: 26755019759
- **Workflow**: `sdap-ci.yml` ("SDAP CI")
- **Trigger**: `push` to `master` (PR #314 merge commit `1e7350256c10da58d3a3b63b6cb46250b56b63fa`)
- **Created**: 2026-06-01 12:31:01 UTC
- **Runner**: windows-latest (image `windows-2025` 20260525.149.1) for Build, ubuntu-latest for Prettier
- **Conclusion**: `failure`
- **URL**: https://github.com/spaarke-dev/spaarke/actions/runs/26755019759

**Failing jobs (2)**:

| Job | Failing step | Exit code | Cause |
|---|---|---|---|
| `Build & Test (Release)` (job 78852517833) | Step 7: `Build` (`dotnet build -c Release --no-restore -warnaserror`) | 1 | 17 `error`s from `-warnaserror` upgrading pre-existing warnings: 1 × `NU1903` (Kiota CVE), 4 × `CS0618` obsolete API uses (DemoProvisioningOptions), 6 × `CS1998` async-without-await, 3 × `CS8601`/`CS8604` nullability, 1 × `CS0109` `new` keyword in test, 1 × `CS0618` in DemoExpirationService |
| `Client Quality (Prettier + ESLint)` (job 78852517980) | Step 8: `Prettier format check` (`npx prettier --check "src/client/**/*.{ts,tsx}" --no-error-on-unmatched-pattern`) | 1 | `[warn] Code style issues found in 330 files. Run Prettier with --write to fix.` |

**Note**: `Build & Test (Debug)` (matrix sibling) was cancelled mid-build by `concurrency: cancel-in-progress` after the Release sibling failed. The Debug log shows the same `NU1903` error before cancellation — i.e., **both Debug and Release fail** under `-warnaserror`, not Release-only.

### Key log excerpts

**Build (Release) — final summary** (`b2qtqxecg.txt` line 106 / 126):

```
Build FAILED.
...
D:\a\spaarke\spaarke\src\server\api\Sprk.Bff.Api\Sprk.Bff.Api.csproj : error NU1903:
  Package 'Microsoft.Kiota.Abstractions' 1.21.2 has a known high severity vulnerability,
  https://github.com/advisories/GHSA-7j59-v9qr-6fq9
##[error] src/server/api/Sprk.Bff.Api/Endpoints/RegistrationEndpoints.cs(458,22):
  error CS0618: 'DemoProvisioningOptions.Environments' is obsolete
##[error] src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookInvocationService.cs(88,61):
  error CS1998: This async method lacks 'await' operators
##[error] tests/integration/Spe.Integration.Tests/Api/Ai/UploadIntegrationTests.cs(559,27):
  error CS0109: The member 'UploadTestFixture.CreateAuthenticatedClient(...)' does not hide
##[error] ...
    17 Error(s)
##[error]Process completed with exit code 1.
```

All 17 errors originate in `src/server/api/Sprk.Bff.Api/**/*.cs` or `tests/integration/Spe.Integration.Tests/**/*.cs`.

**Prettier — failing step** (job 78852517980 step 8 tail):

```
[warn] src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts
[warn] Code style issues found in 330 files. Run Prettier with --write to fix.
##[error]Process completed with exit code 1.
```

330 files (across `src/client/**/*.{ts,tsx}`) are not formatted to Prettier's specification.

### Subsequent master sdap-ci.yml runs

Verifies the failure is **persistent**, not transient:

| Run ID | Created | Conclusion | Failing jobs | Head SHA |
|---|---|---|---|---|
| 26701913034 | 2026-05-31 03:15 | failure | (pre-fix duplicate-key — workflow completed in ~0s, no jobs ran) | `7a99c8ae` (pre-PR-#314) |
| 26719956303 | 2026-05-31 17:50 | failure | (pre-fix duplicate-key) | `f5768d87` |
| **26755019759** | **2026-06-01 12:31** | **failure** | **Build & Test (Release), Client Quality** | **`1e7350256` (PR-#314 merge)** |
| 26757481055 | 2026-06-01 13:20 | failure | Build & Test (Release), Client Quality | `6f0b1392` |

The very next master run (26757481055, 49 minutes later, no source-code intervening commit) had **the same two failing jobs** — confirming a real persistent regression in `src/`, not a flake.

### PR #314 scope analysis

`git show 1e7350256c10da58d3a3b63b6cb46250b56b63fa --stat` shows PR #314 touched:

- `.github/workflows/sdap-ci.yml` (−1 line, the duplicate-key fix from `c9863276`)
- `.github/workflows/deploy-bff-api.yml` (−6 lines, `skip-tests` input removal per D-02)
- `.github/pull_request_template.md`, `.claude/constraints/bff-extensions.md`, `docs/procedures/*`, root `CLAUDE.md` (governance)
- `projects/sdap-bff.api-test-suite-repair/**` (project artifacts)
- `tests/**/*.cs` (extensive — the test-repair body of work)
- `power-platform/plugins/Spaarke.Plugins/**` (3 files; test-related)

**Critical**: PR #314 did NOT touch any of the `src/server/api/Sprk.Bff.Api/**/*.cs` files that are now failing the build. `git log --oneline master -- src/server/api/Sprk.Bff.Api/Endpoints/RegistrationEndpoints.cs` shows the most-recent edit to that file was `34676e20 feat(environment-provisioning): deprecate Environments config, cleanup DTOs` from **2026-04-06**, two months before PR #314.

This confirms: the build failure is **exposed by** PR #314 (the workflow now actually runs) but **caused by** pre-existing `src/` drift — primarily the `DemoExpirationService` migration left `DemoProvisioningOptions.Environments` marked `[Obsolete]` with active callers still pointing at it, and accumulated `CS1998` / nullability warnings that are caught only under `-warnaserror`.

Similarly, the 330 Prettier formatting failures span the entire `src/client/shared/Spaarke.UI.Components/**` tree — pre-existing client drift never previously checked because the workflow was silently dead.

### Predecessor project's own measurement

The predecessor project's CLAUDE.md (`projects/sdap-bff.api-test-suite-repair/CLAUDE.md`) explicitly recorded on 2026-05-31:

> "0 build errors / 17 warnings unchanged from Wave 1 baseline"

Those 17 warnings (Kiota CVE + obsolete API + CS1998) are EXACTLY the 17 errors the CI now reports under `-warnaserror`. The predecessor measured locally with `dotnet build` (no `-warnaserror`); CI measures with `-warnaserror`. The 17-warning vs 17-error delta is the entire root cause on the Build side.

### Local reproduction

Not performed in this task — the failure signature is already conclusively reproduced by the CI itself across two consecutive master runs, and the predecessor project independently documented the 17 warnings on a local Windows machine 12 hours before the CI failure. Local reproduction would add no new information and would burn agent budget. The follow-up project (see below) should reproduce locally as part of remediation.

---

## Disposition

**Decision**: **DEFERRED** (routed to product backlog as a separate, src/-scoped follow-up project).

### Rationale (3 sentences)

The failures are entirely caused by pre-existing `src/` drift — 17 `-warnaserror` upgrades of long-standing C# warnings, and 330 unformatted client files. NFR-01 explicitly forbids this project from modifying `src/`, `power-platform/`, `infra/`, or `scripts/`, so attempting a fix here would violate the project's binding constraint. The Phase-0 exit gate (per the task POML `<notes>`) requires only that a disposition exist — not that the failure be fixed — and Phase 1 (workflow rationalization, actionlint validator, `deploy-promote` cascade fix) is independent of `src/` health and can proceed.

### Why not FIXED

- 17 source-code errors and 330 formatting violations are NOT a workflow-config issue; they require `src/` edits. NFR-01 forbids this.
- Lowering `-warnaserror` or excluding files via `.prettierignore` would be a workflow-config workaround that masks real drift — exactly the failure mode this project's parent (`code-quality-and-assurance-r1`) and predecessor (`sdap-bff.api-test-suite-repair` D-02 strict CI gate) exist to prevent. Any such workaround would be a regression of D-02.

### Why not RESOLVED-TRANSIENT

- Run 26757481055 (49 minutes later, no intervening fix commit) failed with the identical two jobs. Persistent, not flaky.

### Phase-0 exit-gate impact

**Does NOT block Phase 1.** Phase 1 tasks 010 (`deploy-promote.yml` cascade), 011 (`deploy-infrastructure.yml` trigger filter), and 012 (`nightly-quality.yml` schedule fix) are all workflow-config edits that do not require a green master CI signal as a precondition. The new `workflows-validate.yml` actionlint check (FR-07) is also independent. The deliberate-fail verification (FR-13, Phase 5) DOES need `Build & Test (Release)` to be a meaningful gate — by then the follow-up `src/`-fix project should have landed, or this project will run the verification against the actionlint check alone with a documented carve-out.

---

## Follow-up

### Immediate (this project)

1. ✅ This decision record (D-01) written. No source code modified.
2. **Cross-link from Phase 0 task 001's `baseline/workflow-inventory-2026-06-01.md`** (when produced by task 001): the `sdap-ci.yml` row should reference D-01 in its "recent failures" column.
3. **Cross-link from spec.md FR-02** acceptance: "decision record `decisions/D-01-master-ci-failure-disposition.md`" satisfies "documented as out-of-scope with rationale."
4. **Phase 5 deliberate-fail PR (FR-13)**: when authored, must account for `Build & Test (Release)` possibly being red for reasons OTHER than the test PR's deliberate fault. Acceptable mitigation: verify the actionlint check blocks the PR, and accept Build red as a known pre-existing condition referencing D-01.

### Product-backlog (separate project — recommended title + sketch)

**Suggested issue title**: `Repair master CI: 17 src/ -warnaserror errors + 330 Prettier violations (post-PR-#314)`

**Suggested project name**: `sdap-bff-warnaserror-cleanup-r1` (or roll into an existing src/-touching project)

**Scope sketch**:
- **Part A (.NET build)**: Resolve the 17 build errors revealed by `dotnet build -c Release -warnaserror`:
  - 1 × `NU1903` — bump `Microsoft.Kiota.Abstractions` past 1.21.2 (CVE GHSA-7j59-v9qr-6fq9) — likely a transitive that needs an explicit `Directory.Packages.props` pin
  - 5 × `CS0618` — complete the `DemoExpirationService` → `DataverseEnvironmentService` migration the obsolete attribute warned about (callers in `RegistrationEndpoints.cs` lines 458/460/461 and `DemoExpirationService.cs` lines 347/348)
  - 6 × `CS1998` — add `await` or change signatures in `PlaybookInvocationService.cs` (3), `AgentConfigurationService.cs` (1), `ChatEndpoints.cs` (1), and 1 more
  - 3 × `CS8601`/`CS8604` — nullability fixes in `AgentEndpoints.cs` and `ChatEndpoints.cs`
  - 1 × `CS0109` — remove `new` keyword from `UploadTestFixture.CreateAuthenticatedClient` in `tests/integration/Spe.Integration.Tests/Api/Ai/UploadIntegrationTests.cs`
- **Part B (client formatting)**: `npx prettier --write "src/client/**/*.{ts,tsx}"` to format the 330 files, then commit. Optional sub-task: investigate whether prior projects bypassed Prettier (unlikely) or whether the rule set silently changed (more likely — `prettier` version bump in root `package.json`).
- **Verification**: After Parts A + B, run `gh workflow run sdap-ci.yml -r master` (or push a no-op commit) and confirm green.

**Estimated effort**: 4–8 hours (mostly mechanical; the `Environments` migration is the only non-trivial piece).

**Owner**: BFF-API team / project owner; recommend opening before this project's Phase 1 starts to parallelize, but not blocking.

**Reassessment trigger for THIS project**: if the follow-up project doesn't land before Phase 5's FR-13 deliberate-fail verification, Phase 5 carves out `Build & Test (Release)` and verifies via `actionlint` alone, documenting the carve-out as `decisions/D-NN-fr13-carve-out.md`.

---

*Decision record format per predecessor pattern (`projects/sdap-bff.api-test-suite-repair/decisions/D-NN-{topic}.md`).*
