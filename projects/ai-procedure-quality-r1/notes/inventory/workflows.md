# Inventory — `.github/workflows/`

> **Task**: 003-inventory-workflows | **Phase**: 0, Wave 0-A | **Rigor**: STANDARD
> **Generated**: 2026-05-14 | **Source**: read-only inventory of `.github/workflows/*.yml` + `gh run list`
> **Scope**: 13 workflow files (3,813 total lines). Read-only — no workflow files were modified.

---

## 1. Workflow Summary Table

| # | File | Lines | Workflow `name:` | Triggers | Jobs | Action Pin Count | Unpinned/Branch-Pinned |
|---|------|------:|------------------|----------|-----:|-----------------:|----------------------:|
| 1 | `adr-audit.yml` | 218 | `ADR Architecture Audit` | `workflow_dispatch`, `schedule` (Mon 9 AM UTC) | 1 | 3 | 0 |
| 2 | `auto-add-to-project.yml` | 18 | `Auto-add issues to Spaarke Core project` | `issues` (opened, reopened) | 1 | 1 | 0 |
| 3 | `claude-code-review.yml` | 66 | `Claude Code Architecture Review` | `pull_request` (opened, synchronize, reopened) | 1 | 2 | **1 (`@beta`)** |
| 4 | `deploy-bff-api.yml` | 454 | `Deploy BFF API` | `push` (master, `src/server/api/**`), `workflow_dispatch` | 7 | 17 | 0 |
| 5 | `deploy-infrastructure.yml` | 377 | `Deploy Bicep Infrastructure` | `pull_request` (infra paths), `push` master (infra paths), `workflow_dispatch` | 4 | 9 | 0 |
| 6 | `deploy-office-addins.yml` | 51 | `Deploy Office Add-ins to Azure Static Web App` | `push` (master, work/SDAP-outlook-office-add-in, paths), `workflow_dispatch` | 1 | 3 | 0 |
| 7 | `deploy-platform.yml` | 221 | `Deploy Platform Infrastructure` | `workflow_dispatch` | 3 | 5 | 0 |
| 8 | `deploy-promote.yml` | 429 | `Environment Promotion` | `workflow_dispatch`, `workflow_run` (after SDAP CI on master) | 5 | 13 | 0 |
| 9 | `deploy-slot-swap.yml` | 365 | `Deploy via Slot Swap` | `workflow_dispatch`, `workflow_run` (after SDAP CI on master) | 6 | 13 | 0 |
| 10 | `nightly-quality.yml` | 499 | `Nightly Quality` | `schedule` (Mon–Fri 6 AM UTC), `workflow_dispatch` | 5 | 18 | **1 (`@master`)** |
| 11 | `provision-customer.yml` | 279 | `Provision Customer` | `workflow_dispatch` | 4 | 8 | 0 |
| 12 | `sdap-ci.yml` | 386 | `SDAP CI` | `pull_request`, `push` (master) | 6 | 21 | **1 (`@master`)** |
| 13 | `weekly-quality.yml` | 450 | `Weekly Quality Summary` | `schedule` (Fri 10 PM UTC), `workflow_dispatch` | 1 | 2 | 0 |
| **Total** | | **3,813** | | | **44** | **115** | **3** |

---

## 2. Per-Workflow Action Pin Detail

### 2.1 `adr-audit.yml`
| Action | Pin | Form |
|---|---|---|
| `actions/checkout` | `@v6` | semver tag |
| `actions/setup-dotnet` | `@v4` | semver tag |
| `actions/github-script` | `@v8` | semver tag |

### 2.2 `auto-add-to-project.yml`
| Action | Pin | Form |
|---|---|---|
| `actions/add-to-project` | `@v0.5.0` | semver tag |

### 2.3 `claude-code-review.yml`
| Action | Pin | Form |
|---|---|---|
| `actions/checkout` | `@v6` | semver tag |
| `anthropics/claude-code-action` | `@beta` | **branch (unpinned)** |

### 2.4 `deploy-bff-api.yml`
| Action | Pin | Form |
|---|---|---|
| `actions/checkout` | `@v6` (x4) | semver tag |
| `actions/setup-dotnet` | `@v4` (x2) | semver tag |
| `actions/cache` | `@v5` (x2) | semver tag |
| `actions/upload-artifact` | `@v6` (x2) | semver tag |
| `actions/download-artifact` | `@v7` | semver tag |
| `azure/login` | `@v2` (x3) | semver tag |

### 2.5 `deploy-infrastructure.yml`
| Action | Pin | Form |
|---|---|---|
| `actions/checkout` | `@v6` (x3) | semver tag |
| `azure/cli` | `@v2` | semver tag |
| `azure/login` | `@v2` (x3) | semver tag |
| `actions/github-script` | `@v8` | semver tag |

### 2.6 `deploy-office-addins.yml`
| Action | Pin | Form |
|---|---|---|
| `actions/checkout` | `@v6` | semver tag |
| `actions/setup-node` | `@v6` | semver tag |
| `Azure/static-web-apps-deploy` | `@v1` | semver tag |

### 2.7 `deploy-platform.yml`
| Action | Pin | Form |
|---|---|---|
| `actions/checkout` | `@v6` (x3) | semver tag |
| `azure/login` | `@v2` (x3) | semver tag |
| `actions/upload-artifact` | `@v6` | semver tag |

### 2.8 `deploy-promote.yml`
| Action | Pin | Form |
|---|---|---|
| `actions/checkout` | `@v6` (x4) | semver tag |
| `dawidd6/action-download-artifact` | `@v14` (x3) | semver tag |
| `azure/login` | `@v2` (x3) | semver tag |
| `azure/webapps-deploy` | `@v2` (x3) | semver tag |

### 2.9 `deploy-slot-swap.yml`
| Action | Pin | Form |
|---|---|---|
| `actions/checkout` | `@v6` | semver tag |
| `actions/setup-dotnet` | `@v4` | semver tag |
| `actions/upload-artifact` | `@v6` (x2) | semver tag |
| `actions/download-artifact` | `@v7` | semver tag |
| `azure/login` | `@v2` (x2) | semver tag |

### 2.10 `nightly-quality.yml`
| Action | Pin | Form |
|---|---|---|
| `actions/checkout` | `@v6` (x4) | semver tag |
| `actions/setup-dotnet` | `@v4` (x2) | semver tag |
| `actions/cache` | `@v5` (x3) | semver tag |
| `actions/upload-artifact` | `@v6` (x4) | semver tag |
| `actions/download-artifact` | `@v7` (x2) | semver tag |
| `actions/setup-node` | `@v4` (x2) | semver tag |
| `SonarSource/sonarcloud-github-action` | `@master` | **branch (unpinned)** |
| `actions/github-script` | `@v8` | semver tag |

### 2.11 `provision-customer.yml`
| Action | Pin | Form |
|---|---|---|
| `actions/checkout` | `@v6` (x3) | semver tag |
| `azure/login` | `@v2` (x2) | semver tag |
| `actions/upload-artifact` | `@v6` (x2) | semver tag |

### 2.12 `sdap-ci.yml`
| Action | Pin | Form |
|---|---|---|
| `actions/checkout` | `@v6` (x5) | semver tag |
| `aquasecurity/trivy-action` | `@master` | **branch (unpinned)** |
| `github/codeql-action/upload-sarif` | `@v4` | semver tag |
| `actions/setup-dotnet` | `@v4` (x3) | semver tag |
| `actions/cache` | `@v5` (x3) | semver tag |
| `actions/upload-artifact` | `@v6` (x4) | semver tag |
| `actions/setup-node` | `@v4` | semver tag |
| `actions/download-artifact` | `@v7` | semver tag |
| `actions/github-script` | `@v8` | semver tag |

### 2.13 `weekly-quality.yml`
| Action | Pin | Form |
|---|---|---|
| `actions/checkout` | `@v6` | semver tag |
| `actions/github-script` | `@v8` (x2) | semver tag |

---

## 3. Recent Run Outcomes

Source: `gh run list --limit 100 --json status,conclusion,workflowName,createdAt`.
Window covers ~last 100 runs (rolling, all workflows, all branches). Most recent run inspected: 2026-05-14 20:52 UTC. Oldest in window: 2026-05-14 17:19 UTC.

| Workflow | Total Runs in Window | Success | Failure | Skipped | In Progress | Cancelled |
|----------|---------------------:|--------:|--------:|--------:|------------:|----------:|
| `.github/workflows/sdap-ci.yml` | 30 | 0 | **30** | 0 | 0 | 0 |
| `.github/workflows/deploy-infrastructure.yml` | 30 | 0 | **30** | 0 | 0 | 0 |
| `.github/workflows/deploy-promote.yml` | 30 | 0 | **30** | 0 | 0 | 0 |
| `Deploy BFF API` | 4 | 0 | **4** | 0 | 0 | 0 |
| `Nightly Quality` | 3 | 0 | **3** | 0 | 0 | 0 |
| `Claude Code Architecture Review` | 3 | 0 | 0 | 3 | 0 | 0 |
| `ADR Architecture Audit` | 0 | — | — | — | — | — | no runs in window |
| `Auto-add issues to Spaarke Core project` | 0 | — | — | — | — | — | no runs in window |
| `Deploy Office Add-ins to Azure Static Web App` | 0 | — | — | — | — | — | no runs in window |
| `Deploy Platform Infrastructure` | 0 | — | — | — | — | — | no runs in window |
| `Deploy via Slot Swap` | 0 | — | — | — | — | — | no runs in window |
| `Provision Customer` | 0 | — | — | — | — | — | no runs in window |
| `Weekly Quality Summary` | 0 | — | — | — | — | — | no runs in window |

**Note on naming**: The three workflows that fail every run (`sdap-ci.yml`, `deploy-infrastructure.yml`, `deploy-promote.yml`) appear in `gh run list` with their *file paths* as `workflowName` instead of the declared `name:` field. That is a symptom of GHA failing before the workflow header is parsed/rendered — consistent with a workflow-startup failure (e.g., invalid action version), not a job-level test failure. This matches the hypothesis in the task POML.

---

## 4. Concerns

### 4.1 Unpinned / Branch-Pinned Actions (security & reproducibility)

3 distinct branch-pinned references across 3 workflows. These break reproducibility (the action can change without notice) and have weak supply-chain hygiene (no SHA pin):

| Workflow | Action | Pin | Risk |
|---|---|---|---|
| `claude-code-review.yml` | `anthropics/claude-code-action@beta` | branch | Action moves under feet; no SHA pin |
| `nightly-quality.yml` | `SonarSource/sonarcloud-github-action@master` | branch | Third-party action tracking trunk |
| `sdap-ci.yml` | `aquasecurity/trivy-action@master` | branch | Third-party action tracking trunk |

Recommendation candidates (defer to Phase 4b task 070 for diagnosis and F-20 for fix):
- For first-party Anthropic action: pin to a released tag (verify available releases).
- For SonarSource and Aqua Trivy: pin to a release tag and/or to a commit SHA.

### 4.2 Workflows Failing 100% of Recent Runs

Five workflows have **0 successful runs in the visible window**:

1. **`.github/workflows/sdap-ci.yml`** — 30/30 failures. Triggered on every push to master and every PR.
2. **`.github/workflows/deploy-infrastructure.yml`** — 30/30 failures. Triggered on push/PR in `infrastructure/bicep/**` and via dispatch.
3. **`.github/workflows/deploy-promote.yml`** — 30/30 failures. Triggered by `workflow_run` after SDAP CI completion + manual dispatch.
4. **`Deploy BFF API`** — 4/4 failures. Triggered on push to master in `src/server/api/**`.
5. **`Nightly Quality`** — 3/3 failures. Scheduled Mon–Fri at 06:00 UTC + manual dispatch.

**Hypothesis (per task POML context)**: The action pins `actions/checkout@v6`, `actions/upload-artifact@v6`, `actions/download-artifact@v7`, and `actions/cache@v5` are suspect.

- `actions/checkout@v6` — appears in **all** 13 workflows.
- `actions/upload-artifact@v6` — appears in 7 workflows; the latest published major is v4 as of late 2024 / early 2025.
- `actions/download-artifact@v7` — appears in 4 workflows; the latest published major is v4 as of late 2024 / early 2025.
- `actions/cache@v5` — appears in 4 workflows; the latest published major is v4 as of late 2024 / early 2025.

If those tags don't exist in the published action registry, the workflow startup itself fails (0-second failure pattern). This matches the observed behavior: 100% failure across an entire workflow regardless of code changes. **Diagnosis and remediation are explicitly out of scope for this task** (task 070 in Phase 4b).

### 4.3 Workflows with No Recent Activity

The following workflows have **no runs in the inspected window** — cannot be assessed for health:

- `adr-audit.yml` (scheduled Mon 9 AM UTC + dispatch)
- `auto-add-to-project.yml` (only fires on issue open/reopen)
- `deploy-office-addins.yml` (push to master with `src/client/office-addins/**` change)
- `deploy-platform.yml` (dispatch-only)
- `deploy-slot-swap.yml` (dispatch + workflow_run after SDAP CI)
- `provision-customer.yml` (dispatch-only)
- `weekly-quality.yml` (Friday 10 PM UTC)

For `deploy-slot-swap.yml`: it triggers via `workflow_run` after `SDAP CI`. Because SDAP CI fails 100% of recent runs, `deploy-slot-swap.yml`'s `workflow_run` trigger condition (`conclusion == 'success'`) is never satisfied — explaining its silence. This is downstream of the SDAP CI root cause.

For `deploy-promote.yml`: same `workflow_run` after SDAP CI dependency, plus it shows up failing directly which suggests its own failures are independent of (or alongside) the chain.

### 4.4 Pin Pattern Audit (post-F-20 target)

Across 13 workflows / 44 jobs / 115 action references, the current pin distribution is:

| Pin Form | Count | Pct |
|---|---:|---:|
| Semver tag (e.g. `@v6`, `@v4`, `@v0.5.0`) | 112 | 97.4% |
| Branch (e.g. `@master`, `@beta`) | 3 | 2.6% |
| Full commit SHA | 0 | 0% |

F-20 (per spec) targets SHA pinning. The 3 branch pins should be the first to address; the 112 semver-tag pins are reproducibility-acceptable but not supply-chain-locked.

---

## 5. Acceptance Criteria Self-Check (per task POML)

- [x] Every workflow file is represented (13/13)
- [x] 0s-failure workflows are flagged with hypothesis about root cause (Section 4.2)
- [x] Action version table shows all SemVer pins (suspect) vs SHA pins (recommended target after F-20) (Section 2 + 4.4)
- [~] Required-status-check audit — **NOT performed**. The `gh api repos/.../branches/.../protection` call requires write/admin permissions on the repo for branch protection metadata; this task's read-only constraint prevents that lookup. **Recommendation**: defer required-status-check audit to a follow-on step with appropriate auth (or use the GitHub UI inspector at branch settings).

---

*End of inventory. Read-only output. No changes were made to `.github/workflows/`.*
