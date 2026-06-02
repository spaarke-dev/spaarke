# Lessons Learned — github-actions-rationalization-r1

> **Project Closed**: 2026-06-01
> **Branch**: `work/github-actions-rationalization-r1`
> **PR**: #317
> **Scope**: 17 tasks; 13 → 8 workflows; actionlint gate added; weekly health report added; runbook + per-workflow doc authored.

This document captures **non-obvious** insights from the project — patterns and pitfalls that would not have been predictable from the spec alone. The intent is to make the next DevOps-class project on this repo cheaper to execute. **It is deliberately NOT a recap of the work**; for that, see [`tasks/TASK-INDEX.md`](../tasks/TASK-INDEX.md) and the `decisions/` records.

---

## 1. Non-obvious findings

Things we discovered the hard way:

### 1.1 GitHub Actions records `workflow_run` as `failure` even when ALL jobs are conditionally skipped via job-level `if:`

This was the root cause of P2 (`deploy-promote.yml` cascade). The intuition is "if every job evaluates `if:` to false, the workflow records as `skipped`" — that intuition is **wrong**. Job-level `if:` filters cause individual jobs to be skipped, but at least one job in a typical `workflow_run`-triggered workflow uses `if: always()` (the summary/status job). That `always()` job still runs, evaluates `success`/`failure`, and the workflow's `conclusion` is the rollup.

**The fix that works**: put the `if:` filter at **workflow level on the summary job too** (or on every job, including the always-running one). Then the workflow itself is recorded as `skipped`, not `failure`.

The Wave B fix was: `+9/-1 lines` total. Trivial code. Hard-to-discover cause.

### 1.2 The "0s + jobs:[]" loader-failure signature is the diagnostic for malformed workflow YAML

If a workflow shows `conclusion: failure`, `duration: 0s`, and `jobs: []` in `gh run view`, GitHub's workflow loader rejected the YAML before any runner was even allocated. This is **distinct from** "job ran and failed." The fix is structural (fix the YAML), not retry-or-reauth.

Two pre-existing instances of this pattern were repaired in Wave B:
- **P1** (`sdap-ci.yml`, fixed by predecessor PR #314): duplicate top-level `name:` key.
- **P3** (`deploy-infrastructure.yml`, fixed in this project): `${{ runner.temp }}` referenced in **job-level** `env:` — but the `runner` context is only available at **step level**. The loader silently rejected the workflow without surfacing the diagnostic. actionlint v1.7.7 emits the correct diagnostic (`context 'runner' is not allowed here`); this is one of the highest-value catches the actionlint gate provides.

### 1.3 "75% success rate" can be a no-op trick

`deploy-slot-swap.yml`'s headline success rate of 75% turned out to be the rate at which its `Pipeline Summary` job (always-running) reported success. The **substantive** jobs were skipped on most runs because the upstream `sdap-ci.yml` had failed. The workflow was effectively dormant, and the dormancy was masked by the headline metric.

**Lesson**: when auditing workflow value, look at substantive-job conclusions, not workflow-level conclusions. Inspect the job list per run, not just `conclusion`.

### 1.4 `rhysd/actionlint@v1` does NOT exist as a published GitHub Action

The original design document (D-01) listed `rhysd/actionlint@v1` as the canonical Action invocation. This was wrong: the `actionlint` repository publishes specific-version tags only (e.g., `v1.6.x`, `v1.7.x`); there is no `v1` major-version float tag. Trying to use it produces a download error.

The canonical pattern recommended by actionlint's own README is **bash-install via `download-actionlint.bash`**, which fetches the latest minor version of v1 to the runner workspace:

```yaml
- name: Install actionlint
  run: |
    bash <(curl https://raw.githubusercontent.com/rhysd/actionlint/main/scripts/download-actionlint.bash)
```

We corrected this approach in task 030. The lesson: **verify the Action exists before encoding it in a design decision** — a quick `gh api repos/rhysd/actionlint/releases` would have caught this.

### 1.5 `secrets` context is NOT allowed in `environment.url:` fields

GitHub's context-availability rules restrict which contexts can appear in which workflow fields. The `environment.url:` field (which sets the deployment-environment URL displayed in the PR / Actions UI) allows only: `env`, `github`, `inputs`, `job`, `matrix`, `needs`, `runner`, `steps`, `strategy`, `vars`. Notably **NOT** `secrets`.

`deploy-promote.yml` had three pre-existing references to `${{ secrets.* }}` in `environment.url:`. These had been silently broken (rendering an empty URL or causing weird behavior) but had never been reported as a failure. actionlint caught all three in task 030's first run.

This is exactly the kind of catch FR-07 was built for. **Lesson**: if your design includes "we already have linting" or "we already validate," verify it actually catches these. actionlint catches it; YAML-syntax validation does not.

### 1.6 `workflow_dispatch` (and direct API dispatch) require the workflow file to be on the **default branch** first

A new workflow can't be triggered manually (`gh workflow run`) or via API until its file is present on the default branch (`master`/`main`), even if you specify `--ref` for a non-default branch. The `--ref` flag tells GitHub which ref to **check out** for the run, but the workflow definition itself must be discoverable from the default branch's `.github/workflows/` directory at API-list time.

This created an FR-11 / task-040 chicken-and-egg constraint: the new `report-workflow-health.yml` can't be triggered for its FR-11 first-run acceptance verification until PR #317 merges. We documented this constraint in `baseline/branch-protection-verification-2026-06-01.md` § "Note on FR-14."

**Lesson**: any new workflow that has a "first-run verification" acceptance criterion must either (a) have that verification deferred to post-merge, or (b) use the GitHub Actions API's `workflow file content from any ref` semantic (which is messier and rarely worth the complexity).

### 1.7 actionlint's shellcheck integration produces noise that distracts from workflow validation

actionlint runs shellcheck on every `run:` block by default. For a long-running set of CI scripts inside workflows (especially Bash blocks doing `gh api ... | jq ...` or `for f in ...`), shellcheck flags many SC2086 (word-splitting) and SC2129 nits that are not actually bugs. These are pre-existing in the codebase and out of scope per NFR-01.

We disabled shellcheck via `actionlint -shellcheck=` in the workflow-validate task. The actionlint validation of **workflow structure** (the FR-07 mission) is unaffected.

**Lesson**: actionlint's defaults are too noisy for workflows that contain long shell scripts. Disable shellcheck unless your project's scope explicitly includes script-level cleanup.

---

## 2. Patterns to reuse

What worked, in declining order of leverage:

### 2.1 Decision records + ledger + baseline triad (inherited from predecessor)

The pattern `decisions/D-NN-{topic}.md` + `ledgers/{topic}-ledger.md` + `baseline/{state}-{date}.md` worked very well for this DevOps-style project:
- **Decision records** are atomic, auditable, and survive context loss — each one stands alone.
- **The ledger** drives execution (a checklist of dispositions to apply in Phase 2).
- **Baselines** are reference points for "what did we change" and "what was the pre-state."

For 10 disposition decisions (D-01 through D-10) + 7 actual file deletions, this pattern produced perfectly traceable changes with zero ambiguity at PR-review time.

### 2.2 Subagents produce artifacts; main session integrates

Subagents launched via `task-execute` were instructed to produce files but **NOT** commit. The main session committed each wave's output in one integrated commit. Benefits:
- Avoided `git index.lock` races during parallel execution.
- Allowed the main session to add cross-wave context to commit messages (e.g., "Wave B fixes P2/P3; routes nightly-quality fix to D-03 deletion").
- Clean separation of concerns: subagents do work; main session is responsible for git state.

### 2.3 "Wave" framing > rigid Phase numbering

The dependency graph in `TASK-INDEX.md` (Wave A → B → C → D → final) matched actual execution better than Phase numbering. Phases are documentation; Waves are execution. They overlap but don't have to align.

For future projects: the source of truth for execution order should be the **dependency graph**. Phase numbers can describe what work belongs to which phase without dictating execution order.

### 2.4 Naming-collision recovery: rename + preamble note

Wave B's task 010 had its work tagged D-02 in working notes; task 012's parallel work also tagged D-02. When merging, we renamed the later one to D-03 and added a "Renumbered note" preamble explaining why. Cheap, traceable, doesn't require regenerating either record.

**Lesson**: don't try to prevent naming collisions in parallel work — recover from them deterministically. The cost of prevention (synchronization between parallel agents) is higher than the cost of recovery.

---

## 3. Pitfalls avoided

What we deliberately did NOT do, and why:

### 3.1 Did NOT permanently disable `enforce_admins`

NFR-03 forbids extended `enforce_admins: false` windows. Task 031 (add actionlint to required checks) was the only candidate for a bypass — but no bypass was needed because the `actionlint` workflow had already run and passed on PR #317's HEAD before the API change. The required-check change took effect cleanly.

**Lesson**: the predecessor's "brief enforce_admins bypass" pattern is real and sometimes necessary, but it can often be avoided by re-running the new check on the existing HEAD before flipping the gate.

### 3.2 Did NOT comment-out broken workflows

NFR-04 forbids soft-deletes. Every workflow removed in this project (7 total) was removed via `git rm`, not `// commented out`. Each has a `decisions/D-NN-disposition-{name}.md` record with rationale. Recovery via `git log -- .github/workflows/{name}.yml` is trivial.

**Lesson**: soft-deletes accumulate. A `decisions/` record + `git rm` is strictly better than "leave the file commented out as a future-reminder."

### 3.3 Did NOT fix `src/` regressions while auditing workflows

NFR-01 binding: this project does not modify `src/`. The 17 `-warnaserror` errors and 330 Prettier-unformatted files surfaced by D-01 are routed to a **separate** follow-on project (`sdap-bff-warnaserror-cleanup-r1`). The temptation to fix-as-you-go is real; the discipline of NFR-01 is what keeps the project scoped and shipable.

**Lesson**: when a project has a binding "do not touch X" constraint and X-things surface in the work, route them to a separate follow-on project IMMEDIATELY. Don't park them in a `TODO:` comment in this project's own files.

### 3.4 Did NOT chase noise from actionlint's shellcheck integration

Per § 1.7 above. The mission was workflow-YAML validation, not bash-script cleanup. SC2086 / SC2129 nits in long-running scripts inside workflows weren't in scope.

### 3.5 Did NOT introduce a wrapper-action dependency for actionlint

`reviewdog/action-actionlint` is a popular wrapper that posts review comments on PRs. We considered it but rejected it: it adds another dependency (and another supply-chain attack surface), and actionlint's canonical bash-install is what actionlint's own docs recommend. The Action ecosystem rewards minimalism.

---

## 4. Recommendations for follow-on work

In declining order of value:

### 4.1 `sdap-bff-warnaserror-cleanup-r1` (recommended new project, ~4–8h)

Per D-01's "Follow-up" section. Repair the 17 `-warnaserror` errors and 330 Prettier-unformatted files in `src/`. This is the **direct dependency for FR-14** (rolling 7-day ≥90% success rate). Without it, the rate will stay below 90% because every master CI run will fail `Build & Test (Release)` and `Client Quality`.

**Estimated effort**: 4–8 hours (mostly mechanical; the `DemoExpirationService` → `DataverseEnvironmentService` migration is the only non-trivial piece).

### 4.2 Consider adding `yamllint` strict mode alongside actionlint

actionlint catches Actions-specific issues. `yamllint` catches general YAML quality issues that actionlint doesn't (some duplicate-key patterns, indentation edge cases). They're complementary; together they're stronger.

Cost: low (`pip install yamllint` + 1 extra step in workflows-validate.yml). Value: catches edge cases earlier.

### 4.3 Extend `report-workflow-health.yml` to historical windows

Phase 4's report currently aggregates the **last 7 days**. Once the workflow has been running for a quarter, extending it to also report the last 30 / 90 / quarter windows will be cheap and high-value for trend detection. Add this as a Phase 4.1 enhancement after the first ~12 weekly runs.

### 4.4 Doc-drift sweep (small)

Wave C surfaced stale references to deleted workflows in:
- `docs/procedures/ci-cd-workflow.md`
- `docs/architecture/ci-cd-architecture.md`
- `docs/procedures/DEPENDENCY-MANAGEMENT.md:199-200`
- `docs/procedures/testing-and-code-quality.md` (Nightly/Weekly sections)
- `docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md`
- `docs/guides/PRODUCTION-DEPLOYMENT-GUIDE.md`
- `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md`
- `.claude/skills/ci-cd/SKILL.md:171`
- `.claude/skills/azure-deploy/SKILL.md`
- 2× `src/` code comments (out of scope per NFR-01; route to product backlog)

These are NOT critical (the workflows they reference are deleted, so the docs are stale but not actively misleading anyone) but should be cleaned up in a focused doc-drift sweep. The existing `doc-drift-audit` skill can drive this. Estimated effort: 1–2 hours.

### 4.5 Document the "workflow-on-default-branch" chicken-and-egg in `docs/procedures/ci-cd-workflow.md`

Per § 1.6 above. Future projects that add new workflows with "first-run verification" acceptance criteria will rediscover this constraint and burn agent-budget on it. A single paragraph in `docs/procedures/ci-cd-workflow.md` would prevent that.

### 4.6 Test the weekly health-report semantics post-merge

The `report-workflow-health.yml` workflow has never actually executed end-to-end (per § 1.6 chicken-and-egg). After PR #317 merges:
1. Manually trigger: `gh workflow run report-workflow-health.yml`
2. Verify it creates/updates the "CI Health Report" issue.
3. Confirm the metrics it reports look reasonable.

If the first run reveals defects, route them as `report-workflow-health-bugs` follow-on items.

---

## 5. By-the-numbers

The wrap-up audit:

| Metric | Value |
|---|---|
| Tasks total | 17 |
| Tasks complete (in-branch) | 17 ✅ |
| Acceptance criteria total (FRs + NFRs) | ~17 (per spec.md) |
| Acceptance criteria satisfied in-branch | 15–16 (see § 5.1) |
| Acceptance criteria deferred to post-merge | 2 (FR-11 first-run verification + FR-14 ≥90% rate) |
| Workflows pre-project | 13 |
| Workflows post-project | 8 (6 retained + 2 new) |
| Workflows deleted | 7 (via `git rm`, NOT commented-out) |
| Workflows added | 2 (`workflows-validate.yml`, `report-workflow-health.yml`) |
| Workflows fixed (in-place) | 2 (`deploy-promote.yml` cascade, `deploy-infrastructure.yml` loader) |
| Decision records authored | 10 (D-01 through D-10) |
| Subagent dispatches | ~16 (across 5 waves) |
| Files written | ~20 project artifacts + 4 workflows (2 new + 2 modified) + 7 deletions + 2 new docs (`docs/procedures/workflow-incident-response.md`, `.github/WORKFLOWS.md`) |
| Commits on branch | ~12 (planning through wrap-up) |
| Net lines | **negative** — 7 workflow deletions removed ~1980 lines; project additions total ~3000 lines (mostly docs + decision records). The codebase is smaller and better-documented. |

### 5.1 Honest accounting of deferred acceptance criteria

Two FRs are NOT verified in-branch:

- **FR-11 first-run verification**: `report-workflow-health.yml` cannot be triggered until PR #317 merges (per § 1.6). Verification path: post-merge, run `gh workflow run report-workflow-health.yml` and confirm the issue is created. Tracked in `baseline/branch-protection-verification-2026-06-01.md` "Note on FR-14."

- **FR-14 ≥90% rolling 7-day rate**: depends on FR-11 running AND on the `src/` regression (D-01) being resolved by a follow-on project (`sdap-bff-warnaserror-cleanup-r1`). The gate machinery is in place (Phase 3 actionlint + Phase 4 weekly report); the rate cannot improve until the underlying `src/` failures stop.

Both deferrals are honest. The project's success is **the gate machinery + observability + rationalization** — not the immediate health metric. The rate will improve as soon as the follow-on project lands.

### 5.2 Rigor calibration retrospective

STANDARD was the right rigor level for this DevOps-class project. We did NOT touch `.cs`/`.ts`/`.tsx` — so `adr-check` and `code-review` skills don't apply. The judgment-based reviewing of workflow YAML and decision records was best done by the main session integrating each wave, not by a skill-based gate. The decision-record + ledger + baseline triad provided sufficient quality signal without invoking FULL rigor.

**Lesson**: for DevOps-only projects, STANDARD is the right default. Don't reflexively apply FULL just because the project is "important" — apply FULL when the **code surface** (`.cs`/`.ts`) warrants it.

---

## 6. Open questions for the next DevOps-class project

Things we didn't get to and that the next DevOps project might benefit from answering first:

1. **Does the `report-workflow-health.yml` weekly cadence work for the team's review tempo?** We'll know after ~4 weeks of runs.
2. **Is the `dev@spaarke.com` notification routing applied?** Owner action per D-05, tracked in `.github/WORKFLOWS.md` Notification Routing section. Status: pending owner.
3. **Should `actionlint` block on workflows in `.github/actions/` (composite actions) too?** This project's scope was `.github/workflows/**` only. If we add composite actions in the future, the `workflows-validate.yml` glob should expand.
4. **Is there a similar dormant-workflow problem in the `power-platform/` solution-deploy pipelines?** Not in scope here, but a similar audit there might surface similar findings.

---

## Addendum — Closeout PRs (2026-06-02)

After task 090 declared "project complete", the owner pushed back on the implicit-deferral pattern (some items were "tracked in lessons-learned" rather than explicitly deferred with owner-action steps). This drove a closeout series that genuinely closed the open items. Two new lessons emerged here that don't fit above:

### 10. "Documented in lessons-learned" is NOT a deferral

A project's closure isn't legitimate if it relies on hand-waves like "tracked in the assessment doc" or "to be addressed by a follow-on project" without a concrete owner-action plan. Per the owner's principle (2026-06-02): **every open item at project close must be in exactly one of three states — done, explicitly deferred with owner-action steps + signoff checkboxes + ideally a timeline, OR handed off to a new project with its own scope/plan/lifecycle.**

Applied here: FR-12 routing → done (PR #322); MULTI-ENVIRONMENT-PROVISIONING-GUIDE.md → drafted (PR #322); actionlint path-filter deadlock → fixed (PR #322); prod/production naming mismatch → aligned (PR #323); D-11 OIDC federated credentials → added + verified working (PR #324, PR #325); D-12 deploy mechanics → verified end-to-end (PR #326); Service Bus placeholder + queue-naming gap → handed off to `production-environment-setup-r3` (PR #326's D-12 doc).

This principle is now saved to project memory as `project-closure-discipline.md` for application in future projects.

### 11. Do Azure CLI / Portal discovery yourself

Pre-closeout pattern: I asked the owner for every Azure resource detail (PROD_APP_NAME, App Service name, Key Vault names, role assignments, etc.). The owner correction (2026-06-02): "by the way you can look all of this up on azure portal yourself."

Right pattern: query `az ad app list`, `az webapp list`, `az role assignment list`, `az keyvault secret show`, etc. directly. Reserve questions for genuine judgment calls (which env to deploy to, whether to defer or fix now), not data lookups. Saved as memory note `use-azure-cli-autonomously.md`.

### Closeout findings worth recording

These don't fit the "lesson" frame but should be in the project's record:

a. **AZURE_CLIENT_ID repo secret was misconfigured pre-closeout.** It pointed to an Entra app with no role assignments (`spe-github-actions` `e3d1bd6a-...`). Updated to point to the actual deploy app `github-actions-spe-infrastructure` (`8c85a481-...`) which has Contributor on the subscription. Likely net-positive for other workflows (deploy-bff-api.yml etc.) that were failing on OIDC before.

b. **The deploy-promote.yml originally had a sequential `dev → staging → production` chain.** That model fights against per-environment App Service isolation (dev = `spaarke-bff-dev`, prod = `spaarke-bff-prod` in different RGs). Refactored to direct-target so any single environment can be triggered without forcing a re-deploy of others. The GitHub Environment protection rules (reviewer approval on production) provide the actual gates; workflow-internal chaining was unnecessary.

c. **`production-environment-setup-r2` left "values are placeholders" as an inherited gap.** R2 made config parameterizable (KV refs, no hardcodes) but explicitly Out-of-Scope'd verifying that real production values were set. The follow-on `production-environment-setup-r3` is the proper home for that audit + replacement work.

d. **Container exit code 134 (SIGABRT) within ~7 seconds is the diagnostic signature of unhandled startup exception in .NET hosted services.** Container starts, .NET runtime initializes, background workers throw on first iteration, host aborts. Useful for triaging future App Service deploy failures.

---

*This document is the closing artifact of the github-actions-rationalization-r1 project. Updated 2026-06-02 with closeout PR findings. For execution detail, see `tasks/TASK-INDEX.md` and the per-task POML files. For decisions, see `decisions/` (D-01 through D-12). For pre/post state, see `baseline/`.*
