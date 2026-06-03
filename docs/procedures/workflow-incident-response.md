# Workflow Incident Response

> **Last updated**: 2026-06-01 (github-actions-rationalization-r1)
> **Purpose**: Runbook for responding to a failed GitHub Actions workflow run
> **Audience**: On-call operator / Platform engineer
> **Companion**: [`.github/WORKFLOWS.md`](../../.github/WORKFLOWS.md) (per-workflow purpose, owner, SLA, common failure modes)
> **Related**: [`docs/procedures/ci-cd-workflow.md`](ci-cd-workflow.md) (broader CI/CD pipeline architecture), [`docs/guides/INCIDENT-RESPONSE.md`](../guides/INCIDENT-RESPONSE.md) (production-incident template; broader scope)

---

## 1. Overview

Use this runbook when:

- The **"CI Health Report" issue** posts a snapshot showing a workflow with declining success rate (created/updated weekly by `report-workflow-health.yml` — FR-11)
- A **GitHub Actions notification** (email/in-app) reports a workflow failure
- `gh run list --workflow={name} --status=failure` shows recurring failures
- A **PR is blocked from merging** because a required status check (`Build & Test (Debug)`, `Build & Test (Release)`, `Code Quality`, or `actionlint`) failed

This runbook is **specific to GitHub Actions workflow failures**. For broader production incidents (Azure outage, data corruption, BFF API down), see [`docs/guides/INCIDENT-RESPONSE.md`](../guides/INCIDENT-RESPONSE.md).

### Current workflow set (post-rationalization)

The repo runs 8 workflows. Know which one alerted you before triaging:

| Workflow | Purpose | Trigger |
|---|---|---|
| `sdap-ci.yml` | Build + test + quality scan (PR gate) | push/PR |
| `adr-audit.yml` | ADR architecture audit | weekly + manual |
| `deploy-bff-api.yml` | BFF API staging-slot deploy | push to `master` (api paths) + manual |
| `deploy-infrastructure.yml` | Bicep validate + what-if + deploy | PR/push to `master` (infra paths) + manual |
| `deploy-office-addins.yml` | Office Add-ins to Azure Static Web App | push to `master` (add-in paths) + manual |
| `deploy-promote.yml` | dev → staging → prod promotion | after-sdap-ci success + manual |
| `workflows-validate.yml` | actionlint on `.github/workflows/**` | PR/push touching workflows |
| `report-workflow-health.yml` | Weekly CI health snapshot issue | schedule (Mon 09:00 UTC) + manual |

For per-workflow detail (owner, SLA, common failure modes), see [`.github/WORKFLOWS.md`](../../.github/WORKFLOWS.md).

---

## 2. Detection sources

- **CI Health Report issue**: `report-workflow-health.yml` runs weekly (Mondays 09:00 UTC) and updates [the CI Health Report issue](https://github.com/spaarke-dev/spaarke/issues?q=is%3Aissue+%22CI+Health+Report%22) with a per-workflow snapshot. **Rolling 7-day target: ≥90% success rate** (FR-14). A workflow trending below 90% is the primary signal that something needs triage.
- **GitHub Actions notifications**: per D-05, `spaarke-dev` org notifications route to `dev@spaarke.com`. See [`.github/WORKFLOWS.md`](../../.github/WORKFLOWS.md) "Notification routing" section for the setup that produced this routing.
- **Manual inspection**:

  ```powershell
  # Last 30 runs of a workflow
  gh run list --workflow=sdap-ci.yml --limit 30

  # Failures only, with conclusion + timestamps
  gh run list --workflow=sdap-ci.yml --status=failure --limit 30 --json conclusion,createdAt,event,headBranch,databaseId

  # Drill into a specific run
  gh run view {run-id} --log-failed | head -200

  # Inspect job structure (useful for detecting loader-failure: jobs: [])
  gh run view {run-id} --json jobs
  ```

- **PR block**: GitHub's PR UI shows "Required checks failing" when a required status check fails. The 4 required contexts on `master` are `Build & Test (Debug)`, `Build & Test (Release)`, `Code Quality`, and `actionlint` (FR-08). A PR cannot merge until all 4 are green.

---

## 3. Triage decision tree (SCAN THIS FIRST)

```
┌────────────────────────────────────────────────────────────────────┐
│ Did the run complete in 0s with `jobs: []`?                        │
│   YES → LOADER-FAILURE   (workflow YAML is malformed)              │
│         Go to §4.A                                                 │
│   NO  → continue ↓                                                 │
└────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────┐
│ Did the failing step name contain "actionlint" or "shellcheck"?    │
│   YES → STYLE/LINT FAILURE                                         │
│         Go to §4.E                                                 │
│   NO  → continue ↓                                                 │
└────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────┐
│ Did the run fail under a `workflow_run` trigger when the upstream  │
│ (e.g., `SDAP CI`) succeeded — or did the consumer fail spuriously  │
│ because the upstream failed?                                       │
│   YES → CASCADE BUG (P2-class — workflow_run fires regardless of   │
│         upstream conclusion; needs workflow-level `if:` filter)    │
│         Go to §4.C                                                 │
│   NO  → continue ↓                                                 │
└────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────┐
│ Did jobs execute and a specific build/test step fail with non-zero │
│ exit code?                                                         │
│   YES → CODE REGRESSION                                            │
│         Go to §4.B                                                 │
│   NO  → ENVIRONMENT FAILURE (secret missing, runner outage,        │
│         action yanked, network)                                    │
│         Go to §4.D                                                 │
└────────────────────────────────────────────────────────────────────┘
```

### Diagnostic signatures (quick visual cues)

| Signature | What it looks like in `gh run view` | Probable class |
|---|---|---|
| **LOADER-FAILURE** | Run duration < 5 seconds, `gh run view {id} --json jobs` returns `"jobs": []`, no checks posted to PR | Workflow YAML is malformed (duplicate key, invalid runner, job-level `${{ runner.X }}` in `env:`). This was the original `sdap-ci.yml` duplicate-key bug (P1) and the `deploy-promote.yml` / `deploy-infrastructure.yml` patterns (P2/P3). |
| **CASCADE** | A `workflow_run`-triggered workflow reports failure even when its real work is skipped (all jobs `skipped` except summary) — OR fires spuriously after upstream failure | Missing workflow-level `if: github.event.workflow_run.conclusion == 'success'`. Fixed in `deploy-promote.yml` per D-02. |
| **GHOST TRIGGER** | Workflow runs on branches/paths it should be filtered out of | Path filters silently dropped because YAML parsing rejected the trigger block — usually a sibling of LOADER-FAILURE. |
| **REAL CODE FAILURE** | Jobs execute; a specific step (e.g., `Run unit tests`, `dotnet build -c Release`, `npx prettier --check`) fails with exit code != 0; log shows the actual error | A regression in `src/` code or in a test asset. Route the fix through the normal change process — NOT through `.github/workflows/`. |
| **LINT/STYLE** | `actionlint` job in `workflows-validate.yml` reports specific YAML/expression errors | `.github/workflows/{name}.yml` violates GitHub Actions schema. Fix per actionlint output before merging the offending PR. |
| **ENVIRONMENT** | Job fails before its real work runs — `azure/login@v2` failure, `gh` API 401, "secret X not found" | Missing/expired secret, deprecated/yanked action version, GitHub Actions service incident, or transient network. |

---

## 4. Remediation

### 4.A — Loader-failure (P1/P3 pattern)

**Symptom**: Run completes in 0s; `jobs: []`; no PR check posted.

1. Open `.github/workflows/{name}.yml`. Look for:
   - **Duplicate YAML mapping keys** (the original `sdap-ci.yml` bug — fixed by PR #314)
   - **Invalid `runs-on`** (e.g., typo'd label, or label that has been retired by GitHub)
   - **`${{ runner.X }}` at job-level `env:`** (only valid at step-level)
   - **Path filter without proper indentation** (silently makes the filter a no-op)
2. Run `actionlint` locally:

   ```powershell
   # Install once if needed
   bash <(curl -sSL https://raw.githubusercontent.com/rhysd/actionlint/main/scripts/download-actionlint.bash)

   # Lint the offending workflow
   ./actionlint .github/workflows/{name}.yml
   ```

3. Verify the YAML at least parses:

   ```powershell
   python -c "import yaml; yaml.safe_load(open('.github/workflows/{name}.yml'))"
   ```

4. Push the fix on a feature branch. Verify the workflow now actually queues jobs:

   ```powershell
   gh run list --workflow={name}.yml --branch fix/loader-failure --limit 5
   gh run view {newest-run-id} --json jobs    # expect non-empty array
   ```

5. PR + merge per [`docs/procedures/ci-cd-workflow.md`](ci-cd-workflow.md).

**Note**: `workflows-validate.yml` (FR-07) was created specifically to prevent this class of failure from reaching `master` again. If you are fixing a loader-failure, also confirm `workflows-validate.yml` would have caught it — if not, file a follow-up to tighten actionlint rules.

### 4.B — Code regression (real test/build failure)

**Symptom**: Jobs execute; a step fails with a clear error message in the log.

1. View the failing job log:

   ```powershell
   gh run view {run-id} --log-failed | Out-String -Stream | Select-Object -First 200
   ```

2. Identify the failing test/file. Reproduce locally:

   ```powershell
   # If a test failed
   dotnet test {test-project} --filter "FullyQualifiedName~{TestName}" --logger "console;verbosity=detailed"

   # If a build failed
   dotnet build {project-path} -c Release --warnaserror

   # If Prettier failed
   npx prettier --check "src/client/**/*.{ts,tsx}"

   # If ESLint failed
   cd src/client/pcf; npx eslint . --max-warnings 0
   ```

3. Decide: **fix the regression** OR **mark as known issue** (with TODO + product-backlog ticket).
4. If the regression is in `src/`: PR the fix through the normal change process. Do NOT modify the workflow file to make the failure "go away".
5. If the workflow itself is the bug (e.g., the workflow assumes a path that no longer exists), treat it as 4.A or 4.C as appropriate.

**Boundary**: Per project NFR-01, fixes that touch `src/`, `power-platform/`, `infra/`, or `scripts/` are out of scope for the `github-actions-rationalization-r1` project but ARE in scope for normal change PRs.

### 4.C — Cascade bug (P2 pattern)

**Symptom**: A `workflow_run`-triggered workflow reports failure even when its real work was skipped — OR fires spuriously after upstream failure.

1. Check the workflow's `on:` block. Does it have a `workflow_run` trigger? Does it have a workflow-level (or top-of-each-job) `if:` filter that gates on upstream success?
2. Add the standard guard (or its job-level equivalent):

   ```yaml
   jobs:
     {job-name}:
       if: >-
         github.event_name == 'workflow_dispatch' ||
         (github.event_name == 'workflow_run' &&
          github.event.workflow_run.conclusion == 'success')
       runs-on: ubuntu-latest
       steps:
         ...
   ```

3. Verify on a test branch by deliberately failing the upstream workflow and confirming the consumer either does not produce a run OR produces a run whose jobs are all explicitly skipped (and the run reports `success` or `skipped`, not `failure`).
4. Reference decision: `projects/github-actions-rationalization-r1/decisions/D-02-deploy-promote-artifact-contract-verified.md` records the artifact contract + cascade fix for `deploy-promote.yml`.

### 4.D — Environment failure

**Symptom**: Job fails before its real work begins. Common signatures: `azure/login@v2` 401, `gh api` 403, "secret X not found", "unable to resolve action {owner}/{repo}@{ref}".

1. Check secrets:

   ```powershell
   gh secret list                              # repo-level secrets
   gh secret list --env production             # environment-scoped secrets
   ```

   Confirm the secret expected by the failing step exists and is not expired. Known sensitive secrets: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `POWER_PLATFORM_CLIENT_SECRET`.
2. Check runner availability: [GitHub Actions status page](https://www.githubstatus.com/). If GitHub is degraded, wait + retry.
3. Check pinned action versions: is the referenced action deprecated, yanked, or moved? (Example: the original PR attempted to use `rhysd/actionlint@v1` but the correct reference is `rhysd/actionlint@v1.7.7` per pinned-version policy.)
4. **Transient**: re-run the failed jobs only: `gh run rerun {run-id} --failed`. If it succeeds, move on but log the flake.
5. **Persistent**: fix the root cause AND write a decision record under `projects/{project}/decisions/` if the fix changes the secret surface or action-version surface.

### 4.E — Lint failure (actionlint / shellcheck)

**Symptom**: `workflows-validate.yml` job fails on PR. Output names a specific file + line + rule.

1. Read the actionlint output carefully — it includes the file, line, column, and the rule that was violated.
2. Reproduce locally:

   ```powershell
   # Download actionlint
   bash <(curl -sSL https://raw.githubusercontent.com/rhysd/actionlint/main/scripts/download-actionlint.bash)

   # Lint a specific workflow
   ./actionlint .github/workflows/{name}.yml

   # Lint everything
   ./actionlint
   ```

3. Apply the suggested fix. Common patterns:
   - **Undefined matrix value**: matrix axis is referenced but not declared. Add the axis to `strategy.matrix.{axis}`.
   - **Invalid expression**: `${{ }}` in a position that doesn't allow expressions. Move to a `run:` block or pre-compute into an output.
   - **Unknown runner label**: replace with a current GitHub-hosted runner (`ubuntu-latest`, `windows-latest`, `macos-latest`).
4. **Note**: per project decision (see `projects/github-actions-rationalization-r1/decisions/D-02-...`), `workflows-validate.yml` runs `actionlint` BUT does NOT run `shellcheck` inside `run:` blocks. Shellcheck warnings would be informational only and create too much noise. If you see a shellcheck-style issue, it is not enforced — but feel free to fix it as a hygiene improvement.

---

## 5. Escalation

### When to call the owner

| Trigger | Action |
|---|---|
| The failure blocks `master` and you cannot reproduce locally within 30 minutes | Notify owner |
| The failure requires disabling `enforce_admins` to merge | Notify owner BEFORE acting (per NFR-03, each disable must be logged in `decisions/`) |
| You are unsure whether a workflow should be deleted | Notify owner; the delete-by-default rule (D-03) places burden of proof on retention, but consult before pulling the trigger |
| A required-status-check (`Build & Test (Debug)`, `Build & Test (Release)`, `Code Quality`, `actionlint`) needs to be removed or renamed | Notify owner FIRST; required-status-check changes touch branch protection |

### Contacts

- **Owner**: ralph.schroeder@hotmail.com (project owner, github-actions-rationalization-r1)
- **Notification routing**: `dev@spaarke.com` receives all spaarke-dev notifications (per D-05; see `.github/WORKFLOWS.md` for the setup procedure)

### When to file a tracking issue

File a GitHub issue (label: `ci-failure`) when:

- A failure cannot be fixed within the current session AND is not blocking `master`
- A failure recurs (≥3 occurrences in 7 days) without an obvious root cause
- A flaky test is identified — the issue tracks the flake until either fixed or quarantined

### When to delete-with-rationale

Per design D-03 (delete-by-default for never-used workflows), and the precedent set by `projects/github-actions-rationalization-r1/decisions/D-04..D-10`, a workflow may be deleted when:

- It has 0 successful runs in the last 30 days AND no clear forthcoming demand for it
- Its function is fully covered by another retained workflow or by a local script

Procedure:

1. Write a one-paragraph rationale at `projects/{active-project}/decisions/D-NN-disposition-{name}.md`
2. `git rm .github/workflows/{name}.yml`
3. Commit with subject `chore(ci): remove {name}.yml — D-NN`
4. PR + merge

**Never** comment-out-and-leave (NFR-04).

---

## 6. Postmortem template (HIGH-impact failures only)

Use for failures that:

- Blocked deploys for > 1 day
- Required an emergency `enforce_admins: false` bypass
- Surfaced a class of bug (loader-failure, cascade, etc.) the team should systemically prevent

```markdown
# Workflow Incident Postmortem — {YYYY-MM-DD} — {workflow-name}

## Summary
{1-2 sentences: what failed, who noticed, total impact duration}

## Timeline
- {YYYY-MM-DD HH:MM UTC}: {event}
- {YYYY-MM-DD HH:MM UTC}: {event}
- ...

## Detection
{How was the failure noticed? CI Health Report? Direct notification? PR block? Manual `gh run list`?}

## Triage path
{Which §3 branch matched? Did the diagnostic signatures lead you to the right §4 remediation?}

## Root cause
{1-2 paragraphs. Be specific. Cite the offending YAML line, the exact secret name, the exact action version, etc.}

## Resolution
{What was done to fix it. Include PR link and commit SHA.}

## Prevention
{What changes — workflow files, branch protection, docs, monitoring — would have caught this earlier or prevented it entirely? Did `workflows-validate.yml` catch it? Should it?}

## Action items
- [ ] {prevent recurrence — e.g., tighten actionlint rules, add path-filter test}
- [ ] {improve detection — e.g., new metric in `report-workflow-health.yml`}
- [ ] {update runbook — add new pattern recognition to §3 / §4}

## Decision record
{If a delete-with-rationale or an `enforce_admins` bypass was used, link to the corresponding `decisions/D-NN-*.md`.}
```

Save postmortems under `projects/{active-project}/postmortems/POST-NN-{slug}.md` (parallel structure to `decisions/`). For repository-wide postmortems unrelated to a specific project, save under `docs/postmortems/` (create the directory if needed).

---

## 7. References

### Internal — current project
- [`.github/WORKFLOWS.md`](../../.github/WORKFLOWS.md) — per-workflow purpose, owner, SLA, notification routing
- [`docs/procedures/ci-cd-workflow.md`](ci-cd-workflow.md) — broader CI/CD pipeline architecture, PR workflow, deployment pipeline
- [`docs/guides/INCIDENT-RESPONSE.md`](../guides/INCIDENT-RESPONSE.md) — production-incident template (broader scope: Azure outages, data corruption, BFF API down)
- [`docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md`](../guides/GITHUB-ENVIRONMENT-PROTECTION.md) — branch protection + required-status-check configuration
- [`projects/github-actions-rationalization-r1/`](../../projects/github-actions-rationalization-r1/) — origin of the current workflow rationalization
- [`projects/github-actions-rationalization-r1/decisions/`](../../projects/github-actions-rationalization-r1/decisions/) — per-workflow disposition rationales (D-01..D-10)
- [`projects/github-actions-rationalization-r1/baseline/workflow-inventory-2026-06-01.md`](../../projects/github-actions-rationalization-r1/baseline/workflow-inventory-2026-06-01.md) — original inventory and failure-pattern evidence

### External
- GitHub Actions documentation: https://docs.github.com/en/actions
- `actionlint` reference: https://github.com/rhysd/actionlint
- GitHub Actions status: https://www.githubstatus.com/
- `gh` CLI: https://cli.github.com/

---

*This runbook is owned by the Platform/DevOps team. Update it whenever a new failure class is discovered. Add new pattern recognition to §3 (triage decision tree) and §4 (remediation) as classes accumulate.*
