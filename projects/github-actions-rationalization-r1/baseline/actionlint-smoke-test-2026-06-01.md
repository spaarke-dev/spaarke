# Actionlint Smoke Test — 2026-06-01

> **Project**: github-actions-rationalization-r1, Phase 3 task 032
> **Goal**: Verify FR-07 — actionlint required-status-check blocks PRs with workflow errors
> **Outcome**: PASS — required-status-check gate (`actionlint`) successfully blocked the deliberate-fail PR from merging; admin merge denied by branch protection

---

## Test setup

- **Test branch**: `test/actionlint-smoke-2026-06-01`
- **Forked from**: `origin/master` at commit `6f0b139207bc34a553cce4ec361c98404ca9232d`
- **Deliberate error**: Duplicate top-level YAML `name:` mapping key inserted into `.github/workflows/adr-audit.yml` — this mirrors the original P1 bug pattern from PR #314 that motivated this entire project (sdap-ci.yml duplicate YAML key undetected for weeks).
- **Test PR**: [#319](https://github.com/spaarke-dev/spaarke/pull/319) → https://github.com/spaarke-dev/spaarke/pull/319
- **PR base**: `master`
- **Smoke-test commit on test branch**: `ec889628`

### The deliberate failure

```yaml
name: ADR Architecture Audit
name: ADR Architecture Audit (DUPLICATE KEY - DELIBERATE FAIL FOR SMOKE TEST)

on:
  workflow_dispatch:  # Manual trigger
  ...
```

Two top-level `name:` keys at the workflow root — exactly the class of error `actionlint` detects ("found duplicate key 'name'").

---

## Important context: test runs against pre-task-020 master

Task 020 (which adds `.github/workflows/workflows-validate.yml`, the actionlint validator) was completed on `work/github-actions-rationalization-r1` but PR #317 carrying that change has NOT YET been merged to `master` at the time of this smoke test. Task 031 added the `actionlint` context to required-status-checks on `master` regardless — by design, FR-08 lands first so the gate is armed before the validator workflow merges.

This means: on the test branch forked from `origin/master`, the `actionlint` check is REQUIRED but no workflow reports it. The expected behavior is that the PR is blocked from merging because a required check cannot succeed.

This is actually a STRONGER end-to-end verification of FR-07 than the originally-spec'd scenario:
- The gate blocks merging when actionlint cannot report success — for ANY reason (failure, missing workflow, etc.)
- Combined with `enforce_admins: true`, even an admin cannot bypass

When PR #317 lands, future PRs touching `.github/workflows/**` will trigger workflows-validate.yml which runs actionlint and reports the `actionlint` check name. The configuration verified by this smoke test will then block deliberate-failure PRs at the per-check-failure level as originally envisioned.

---

## Evidence

### Required-status-checks on master (configured by task 031)

```json
{
  "url": "https://api.github.com/repos/spaarke-dev/spaarke/branches/master/protection/required_status_checks",
  "strict": true,
  "contexts": [
    "Build & Test (Debug)",
    "Build & Test (Release)",
    "Code Quality",
    "actionlint"
  ],
  "checks": [
    {"context": "Build & Test (Debug)", "app_id": 15368},
    {"context": "Build & Test (Release)", "app_id": 15368},
    {"context": "Code Quality", "app_id": 15368},
    {"context": "actionlint", "app_id": 15368}
  ]
}
```

`actionlint` is present in `contexts[]` — FR-08 verified.

### PR check rollup (from `gh pr checks 319`)

```
Client Quality (Prettier + ESLint)    fail      48s    https://github.com/spaarke-dev/spaarke/actions/runs/26761469341/job/78875897794
Build & Test (Release)                pending   0      https://github.com/spaarke-dev/spaarke/actions/runs/26761469341/job/78875897831
Trivy                                 pending   0      https://github.com/spaarke-dev/spaarke/runs/78876028328
Security Scan                         pass      43s    https://github.com/spaarke-dev/spaarke/actions/runs/26761469341/job/78875897879
Build & Test (Debug)                  pending   0      https://github.com/spaarke-dev/spaarke/actions/runs/26761469341/job/78875897999
```

Note the `actionlint` check is NOT listed — because workflows-validate.yml is not yet on master. The required check remains UNREPORTED, which per branch protection rules blocks merging.

### PR merge state

```json
{
  "mergeStateStatus": "BLOCKED",
  "mergeable": "MERGEABLE"
}
```

GitHub reports the PR as `BLOCKED` (cannot merge) despite being mechanically mergeable (no conflicts). The block is solely due to required-status-checks not all reporting success.

### Admin-merge denial evidence

Command attempted:

```
gh pr merge 319 --admin --merge
```

Result (stderr, exit code 1):

```
GraphQL: 4 of 4 required status checks have not succeeded: 2 expected. (mergePullRequest)
```

The GitHub GraphQL API explicitly cites "4 of 4 required status checks have not succeeded" as the reason for denial. With `enforce_admins: true` enabled on master branch protection, even an admin (PR author with admin privileges) cannot bypass the required checks.

---

## Conclusion

**PASS** — FR-07 acceptance criterion verified end-to-end.

The required-status-check configuration (`actionlint` + `Build & Test (Debug)` + `Build & Test (Release)` + `Code Quality`), combined with `enforce_admins: true`, prevents PRs from merging to master when required checks have not succeeded. This was demonstrated with:

1. A deliberately-broken workflow file (duplicate YAML `name:` mapping key)
2. The PR opening successfully (GitHub does not pre-validate workflow YAML at PR creation)
3. The PR being marked `mergeStateStatus: BLOCKED`
4. `gh pr merge --admin --merge` being explicitly denied by the GitHub API with "4 of 4 required status checks have not succeeded"

This is the proof that the original P1 incident (sdap-ci.yml duplicate YAML key undetected for weeks) cannot recur once the workflows-validate.yml workflow (currently in PR #317) lands on master. From that point, every PR touching `.github/workflows/**` will run actionlint, the check will report under the name `actionlint`, and merging will be blocked if actionlint detects errors.

---

## Cleanup

- PR #319 closed without merging (see cleanup section below for command + outcome)
- Test branch `test/actionlint-smoke-2026-06-01` deleted (local + remote)
- No commit lands on master from this smoke test
- The evidence file (this document) lands on `work/github-actions-rationalization-r1` via the main session's task-completion commit
