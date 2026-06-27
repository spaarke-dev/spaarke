# Router Signal Model — Decision Record

> **Task**: CICD-012 (spike, resolves spec Unresolved Question #1)
> **Date**: 2026-06-26
> **Decision authority**: Solo developer (autonomous per project CLAUDE.md)
> **Blocks**: CICD-040 (`ci-router.yml` final implementation)
> **Spec constraints**: FR-A01 (single required check, never stuck pending), FR-A05 (path-aware dispatch only; no commit-marker skip), FR-C01 (merge queue batch=1, no speculative)

---

## 1. Decision

**Adopt Model A: Single composite required check.**

`ci-router.yml` exposes exactly ONE branch-protection-required status check named **`CI / Router`**. The router itself is a single workflow that:

1. Runs unconditionally on every `pull_request` and `merge_group` event (no `paths:` filter on the workflow itself).
2. Performs path classification in a first job (`classify`) using a script (e.g., `dorny/paths-filter` or inline `git diff`) that emits booleans: `bff_changed`, `spaarke_ai_changed`, `docs_only`.
3. Dispatches Tier 1 jobs (`tier1-compile`, `tier1-arch`, `tier1-smoke`, `tier1-auth-smoke`) conditionally via `if:` expressions referencing the `classify` outputs.
4. Terminates with a final **`router-result`** job that uses `if: always()` + `needs: [classify, tier1-*]` and an explicit gate (e.g., `re-actors/alls-green` or inline bash) to convert the aggregate of upstream `success` / `skipped` / `failure` results into a single pass/fail.

The branch-protection rule requires only `router-result` (displayed in PRs as `CI / Router`).

**Model B rejected**: relying on GitHub's "skipped-as-success" semantics directly (i.e., each tier as its own required check, gated by `if:`) is technically valid for `pull_request`, but introduces three operational hazards documented in §3 and is brittle under `merge_group` because each required check is independently evaluated by branch protection AND by the merge queue's own check list.

---

## 2. Evidence (GitHub doc citations)

### 2.1 The "stuck pending forever" footgun is real and documented

GitHub Docs, *Troubleshooting required status checks*:

> "If a workflow is skipped due to **path filtering, branch filtering or a commit message**, then checks associated with that workflow will remain in a 'Pending' state. A pull request that requires those checks to be successful will be blocked from merging."
> ([docs.github.com](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/collaborating-on-repositories-with-code-quality-features/troubleshooting-required-status-checks))

**Implication**: Spec FR-A05 ("decision lives in router, not `paths:` filters") is correct. We MUST NOT add `paths:` to the workflows themselves. Path classification must happen INSIDE the workflow (router job) so the check always reports.

### 2.2 But conditionally-skipped JOBS report success

Same GitHub Docs page:

> "If a job within a workflow is skipped due to a conditional, it will report its status as 'Success'."
> ([docs.github.com](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/collaborating-on-repositories-with-code-quality-features/troubleshooting-required-status-checks))

GitHub Actions *Using conditions to control job execution* docs confirm:

> "A job that is skipped will report its status as 'Success'. It will not prevent a pull request from merging, even if it is a required check."
> ([docs.github.com](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-jobs-with-conditions))

**Implication**: Model B is technically possible. But "skipped == success" is silent — there is no visible UX cue that the tier ran. For solo + 100% Claude operating model where the developer reads the PR check list as a status board, every check displayed should map 1:1 to "this ran and passed."

### 2.3 Merge queue requires `merge_group` event AND has its OWN check list

GitHub Docs, *Managing a merge queue*:

> "If your repository uses GitHub Actions to perform required checks on pull requests in your repository, you need to **update the workflows to include the `merge_group` event as an additional trigger**. Otherwise, status checks will not be triggered when you add a pull request to a merge queue."
> ([docs.github.com](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/managing-a-merge-queue))

And:

> "Choose how long the queue should wait for a response from CI before assuming that checks have failed."
> (Status check timeout setting — default 60 minutes per community references; spec FR-C01 sets ours to 30 min)

**Implication**: Whatever signal model we pick MUST emit on `merge_group` events. With Model A, we add `merge_group` to a single workflow's triggers and add a single check (`router-result`) to the merge queue's required-checks list. With Model B, each tier workflow needs the `merge_group` trigger separately AND each check needs to be added to BOTH branch-protection and merge-queue required-check lists. Operational surface area is multiplied.

### 2.4 The summary-job pattern is the documented community workaround

The canonical pattern uses `if: always()` to defeat the "skipped == pending" trap at the workflow level by ensuring the gating job NEVER gets skipped:

```yaml
router-result:
  if: always()
  needs: [classify, tier1-compile, tier1-arch, tier1-smoke, tier1-auth-smoke]
  runs-on: ubuntu-latest
  steps:
    - uses: re-actors/alls-green@release/v1
      with:
        jobs: ${{ toJSON(needs) }}
```

Per [emmer.dev](https://emmer.dev/blog/skippable-github-status-checks-aren-t-really-required/): "a job with `if: always()` cannot be skipped — it will evaluate the dependencies through the `alls-green` action and report an explicit pass/fail result. This prevents the loophole where dependent jobs could fail or skip while the required check still passes."

**Implication**: Model A's `router-result` job is the directly-recommended pattern from independent community evidence. The `alls-green` action treats `skipped` and `success` as pass (configurable), `failure` and `cancelled` as fail.

---

## 3. Decision matrix (3 scenarios × 2 signal models)

| Scenario | Model A: Single composite `router-result` | Model B: Each tier as own required check (skipped-as-success) |
|---|---|---|
| **BFF-only diff** | Router classifies `bff_changed=true, spaarke_ai_changed=false`. `tier1-compile` + `tier1-arch` + `tier1-smoke` run; SpaarkeAi-specific jobs skipped via `if:`. `router-result` runs (always()), `alls-green` sees `success`+`skipped` mix → passes. **One green check displayed.** | BFF-specific checks run → success. SpaarkeAi-specific checks skipped → reported as success (silent). Both required checks show "green" but UX cannot distinguish ran-and-passed from skipped. |
| **SpaarkeAi-only diff** | Mirror of above. `tier1-compile` runs (always required for any code change); SpaarkeAi-touching jobs run; BFF-specific (e.g., auth-smoke) skipped. `router-result` passes. **One green check displayed.** | Mirror; same UX silent-skip issue. |
| **Docs-only diff** | Router classifies `docs_only=true`. All Tier 1 jobs skipped via `if:`. `router-result` runs (always()), `alls-green` sees all-skipped → passes (configurable behavior; we explicitly allow). **One green check displayed.** Total wall-clock ~30s (classify + result jobs only). | All tier checks skipped → all report success. Branch protection passes. BUT: any reviewer/automation tool inspecting the check list cannot tell from check names alone whether the codepath was actually exercised. |

### Cross-cutting comparison

| Dimension | Model A | Model B |
|---|---|---|
| **Branch protection required-check count** | 1 (`CI / Router`) | N (one per tier; grows with Tier 2 if ever required) |
| **Merge queue required-check count** | 1 (mirrors branch protection) | N (must mirror; double-maintenance) |
| **`merge_group` trigger surface** | 1 workflow file | N workflow files |
| **Stuck-pending risk** | Eliminated (router always runs, `router-result` uses `if: always()`) | Eliminated PROVIDED jobs are gated by `if:` not workflow-level `paths:` filter. One slip into `paths:` reintroduces FR-A01 violation. |
| **UX signal clarity** | Explicit aggregate result | Silent skip indistinguishable from success |
| **Failure attribution** | Drill into `router-result` → see which `needs:` job failed | Direct (failing check name is the failing tier) |
| **Adding a new tier later** | Add job + add to `needs:`; required-check list unchanged | Add workflow + add to branch-protection list + add to merge-queue list + ensure `merge_group` trigger |
| **Spec FR-A01 compliance** | Native fit | Compliant only if every workflow stays `paths:`-filter-free forever (operational discipline burden) |
| **Spec FR-C01 (batch=1, no speculative)** | One check to wait on → simpler queue-timeout reasoning | Multiple checks to wait on; queue timeout (30 min per FR-C01) applies independently to each |

---

## 4. Why Model A wins for this project specifically

1. **Solo + 100% Claude operating model.** Operational discipline burden (Model B requires "never add `paths:` to a tier workflow" forever) is the wrong tradeoff when the same dev/AI is also the reviewer. Model A enforces FR-A01 mechanically via the `router-result` job structure.
2. **Spec FR-A05 explicitly forbids commit-marker skip.** Model A keeps the only legitimate skip mechanism (path-aware dispatch) co-located with the router decision, making the rule self-evident in one file.
3. **Merge queue surface minimization.** FR-C01 (batch=1, no speculative) means a stalled check stalls the whole queue. One required check = one timeout failure mode to debug. Model B multiplies this.
4. **Future Tier 2 / Tier 3 evolution.** Spec FR-A03/A04 mark Tier 2 as advisory and Tier 3 as nightly. If we ever promote a Tier 2 job to blocking, Model A is a one-line `needs:` edit; Model B requires branch-protection + merge-queue config changes (two settings UIs, no diff trail).
5. **Aligns with documented community pattern.** `re-actors/alls-green` is widely adopted; risk of pattern abandonment is low.

---

## 5. Fallback plan if Model A misbehaves in shadow phase

The shadow phase (Phase 2 per design.md §150-176) runs `ci-router.yml` in parallel with `sdap-ci.yml` for ≥14 days before cutover. Concrete misbehavior signals + fallbacks:

| Misbehavior signal (observed in shadow) | Diagnosis | Fallback action |
|---|---|---|
| `router-result` reports `pending` for >5 min on >2 PRs | `if: always()` not firing; likely workflow YAML parse error or `needs:` reference to non-existent job | Add a no-op `safety-net` job with no `needs:` that always reports success; restructure `router-result` to depend on it explicitly. If still pending, file GitHub support ticket and rollback to per-tier required checks (Model B) as documented in spec §152-160 rollback. |
| `re-actors/alls-green` action deprecated or breaking change | External dep risk | Replace with inline bash gate: `if: always()` + `run: jq -e 'all(.[]; .result == "success" or .result == "skipped")' <<< '${{ toJSON(needs) }}'` — same semantics, zero deps. |
| Merge queue times out at 30 min waiting for `router-result` | One of the downstream tier jobs is hung; `router-result` correctly waits | Triage the hung tier job. NOT a signal-model failure — Model B would also time out. |
| `router-result` shows `success` but a tier job actually failed | `alls-green` misconfigured to ignore `failure` | Audit `alls-green` `allowed-failures` / `allowed-skips` inputs; default config treats `failure` as failure. Add a smoke test PR that deliberately fails Tier 1 compile to verify before cutover. |
| Path classification misfires (e.g., BFF diff classified as docs-only) | Bug in `dorny/paths-filter` config or `git diff` script | Fix classifier; add unit test in `tests/integration/contract/` for the classifier script. NOT a signal-model failure. |
| Shadow phase reveals `merge_group`-triggered runs behave differently | `merge_group` event payload differs from `pull_request` | Audit `if:` conditions for event-name dependencies; explicitly handle `github.event_name == 'merge_group'` where needed. Per merge queue docs, this is expected — design for both events from day 1. |

**Hard rollback trigger** (per spec §152-160): Tier 1 flake rate >2% sustained over 24h post-cutover OR master green rate <90% over 24h → flip branch protection back to pre-cutover state (saved to `notes/branch-protection-pre-cutover.json` per FR-A06), disable merge queue, re-enable `sdap-ci.yml`. Shadow workflows stay running for diagnosis.

**Fallback to Model B if Model A is fundamentally broken**: estimated 4-hour rework to (a) remove `router-result` job, (b) split tier1 jobs into separate workflow files each with `merge_group` trigger, (c) update branch protection + merge queue required-check lists. Acceptable cost; not expected to be needed.

---

## 6. Implementation notes for task CICD-040

### 6.1 Required workflow triggers

```yaml
on:
  pull_request:
    branches: [master]
  merge_group:
    branches: [master]
  push:
    branches: [master]
```

**No `paths:` filter at workflow level** (FR-A01 binding). Path-aware dispatch happens in the `classify` job's outputs.

### 6.2 Required job structure (skeleton)

```yaml
jobs:
  classify:
    runs-on: ubuntu-latest
    outputs:
      bff_changed: ${{ steps.filter.outputs.bff }}
      spaarke_ai_changed: ${{ steps.filter.outputs.spaarke_ai }}
      docs_only: ${{ steps.filter.outputs.docs_only }}
    steps:
      - uses: actions/checkout@v4
      - uses: dorny/paths-filter@v3
        id: filter
        with:
          filters: |
            bff:
              - 'src/server/api/Sprk.Bff.Api/**'
              - 'src/server/shared/**'
              - 'tests/integration/auth/**'
              - 'tests/integration/regression/**'
            spaarke_ai:
              - 'src/solutions/SpaarkeAi/**'
            docs_only:
              - 'docs/**'
              - '*.md'
              - '!src/**'
              - '!tests/**'
              - '!.github/workflows/**'

  tier1-compile:
    needs: classify
    if: needs.classify.outputs.docs_only != 'true'
    # ... compile job
    
  tier1-arch:
    needs: classify
    if: needs.classify.outputs.docs_only != 'true'
    # ... NetArchTest MUST-NOT subset
    
  tier1-smoke:
    needs: classify
    if: needs.classify.outputs.docs_only != 'true'
    # ... changed-surface integration smoke

  tier1-auth-smoke:
    needs: classify
    if: needs.classify.outputs.bff_changed == 'true'
    # ... auth smoke when BFF touched

  router-result:
    if: always()
    needs: [classify, tier1-compile, tier1-arch, tier1-smoke, tier1-auth-smoke]
    runs-on: ubuntu-latest
    steps:
      - name: Aggregate tier results
        uses: re-actors/alls-green@release/v1
        with:
          jobs: ${{ toJSON(needs) }}
          # Default: success+skipped count as pass; failure+cancelled count as fail
```

### 6.3 Branch protection config (task 070)

- Required status checks on `master`: **`CI / Router / router-result`** (the only one).
- All other tier checks: NOT required.
- Tier 2 (`ci-tier2-advisory.yml`): NOT required (advisory per FR-A03).
- Pre-cutover state exported to `notes/branch-protection-pre-cutover.json` per FR-A06.

### 6.4 Merge queue config (task 071, per FR-C01)

- Required checks on merge queue: **`CI / Router / router-result`** (same single check).
- Batch size: 1 (no speculative).
- Status check timeout: 30 minutes.
- `merge_group` event added to `ci-router.yml` triggers per §6.1.

### 6.5 Validation in shadow phase

Before cutover, file 3 deliberate test PRs:
- **PR-S1**: BFF-only diff (modify one file in `src/server/api/Sprk.Bff.Api/`). Expect: `tier1-compile`, `tier1-arch`, `tier1-smoke`, `tier1-auth-smoke` run; `router-result` green.
- **PR-S2**: SpaarkeAi-only diff (modify one file in `src/solutions/SpaarkeAi/`). Expect: `tier1-compile`, `tier1-arch`, `tier1-smoke` run; `tier1-auth-smoke` skipped (BFF not touched); `router-result` green.
- **PR-S3**: Docs-only diff (modify a single `.md` file). Expect: all tier1 jobs skipped; `router-result` green; wall-clock ≤90s.

Plus **PR-S4 (negative test)**: deliberately break Tier 1 compile (introduce a syntax error). Expect: `tier1-compile` fails; `router-result` fails; `CI / Router` blocks merge.

### 6.6 Naming convention

GitHub displays the required check as `<workflow-name> / <job-name>`. To get the spec-required `CI / Router` display name:

```yaml
name: CI
# ...
jobs:
  # ...
  router-result:
    name: Router
    # ...
```

So branch protection required-check string: `CI / Router`.

---

## 7. Acceptance criteria self-check

| Criterion (from POML) | Met? | Evidence |
|---|---|---|
| Decision documented with explicit choice + GitHub doc citations | ✅ | §1 (decision), §2 (4 GitHub Docs citations + 1 community pattern citation) |
| 3 scenarios × 2 signal models matrix analyzed | ✅ | §3 (BFF-only, SpaarkeAi-only, docs-only × Model A vs Model B) |
| Fallback documented if neutral-conclusion path fails in shadow | ✅ | §5 (6 misbehavior signals + actions; hard rollback trigger; Model B fallback cost estimate) |

---

## 8. References

- GitHub Docs — [Troubleshooting required status checks](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/collaborating-on-repositories-with-code-quality-features/troubleshooting-required-status-checks)
- GitHub Docs — [Managing a merge queue](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/managing-a-merge-queue)
- GitHub Docs — [Using conditions to control job execution](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-jobs-with-conditions)
- Community pattern — [Skippable GitHub Status Checks Aren't Really Required (emmer.dev)](https://emmer.dev/blog/skippable-github-status-checks-aren-t-really-required/)
- Community pattern — [GitHub Actions Required Checks for Conditional Jobs (devopsdirective.com)](https://devopsdirective.com/posts/2025/08/github-actions-required-checks-for-conditional-jobs/)
- GitHub Community — [Branch protections when actions use `paths-ignore` (Discussion #54877)](https://github.com/orgs/community/discussions/54877)
- Action — [`re-actors/alls-green`](https://github.com/re-actors/alls-green) (canonical aggregator)
- Spec — `projects/ci-cd-unit-test-remediation-r1/spec.md` FR-A01, FR-A05, FR-C01
- Design — `projects/ci-cd-unit-test-remediation-r1/design.md` §5 Stream A, §11 deferred-to-spec item #12
