# Branch Protection Configuration — master

> **Applied**: 2026-03-13
> **Repository**: spaarke-dev/spaarke
> **Branch**: master

---

## Required Status Checks (Blocking)

These checks MUST pass before a PR can be merged to master:

| Check Name | Source Workflow | What It Validates |
|-----------|----------------|-------------------|
| `Build & Test (Debug)` | `sdap-ci.yml` | .NET build + full test suite (Debug config) |
| `Build & Test (Release)` | `sdap-ci.yml` | .NET build + full test suite (Release config) |
| `Client Quality (Prettier + ESLint)` | `sdap-ci.yml` | TypeScript/React formatting (Prettier) and linting (ESLint --max-warnings 0) |
| `Code Quality` | `sdap-ci.yml` | dotnet format verification, ADR architecture tests (NetArchTest), plugin size validation, dependency vulnerability audit |

### Configuration Details

- **Require branches to be up to date before merging**: YES (`strict: true`)
  - PRs must be rebased/merged with the latest master before the merge button is enabled
  - Prevents stale PRs from bypassing checks that would fail against current master
- **Dismiss stale reviews when new commits are pushed**: YES
  - Any new push invalidates previous approvals, forcing re-review
- **Enforce admins**: NO
  - Repository admins can bypass in emergencies (e.g., hotfix)
- **Allow force pushes**: NO
- **Allow deletions**: NO

---

## Advisory Checks (Non-Blocking)

These checks run on PRs but do NOT block merging:

| Check | Source | Why Advisory |
|-------|--------|-------------|
| CodeRabbit review | GitHub App | AI review produces subjective findings (spec FR-13) |
| Claude Code Action review | GitHub Action | AI review produces subjective findings (spec FR-13) |
| SonarCloud quality gate | Nightly workflow | Not yet consistently passing for 2+ weeks (see Promotion Timeline below) |
| Security Scan (Trivy) | `sdap-ci.yml` | Informational — vulnerability reports go to GitHub Security tab |
| ADR Violations Report | `sdap-ci.yml` | PR comment with ADR test results — informational only |
| Nightly Quality findings | `nightly-quality.yml` | Post-merge analysis, not PR-time |

---

## SonarCloud Promotion Timeline

**Current status**: Advisory (not in required checks list)

**Promotion criteria** (from spec Domain H):
- SonarCloud quality gate must pass consistently for 2+ weeks
- New code coverage threshold >= 70% must be configured in SonarCloud quality gate conditions
- After consistent passing confirmed, add `SonarCloud Code Analysis` to required status checks

**How to check readiness**:
```bash
# Check recent nightly runs for SonarCloud results
gh run list --workflow=nightly-quality.yml --limit=14

# View a specific run's SonarCloud job
gh run view {run_id} --json jobs --jq '.jobs[] | select(.name | contains("SonarCloud"))'
```

**How to promote to blocking**:
```bash
# Add SonarCloud to required checks
gh api repos/spaarke-dev/spaarke/branches/master/protection/required_status_checks \
  -X PATCH \
  --input - <<'EOF'
{
  "strict": true,
  "contexts": [
    "Build & Test (Debug)",
    "Build & Test (Release)",
    "Client Quality (Prettier + ESLint)",
    "Code Quality",
    "SonarCloud Code Analysis"
  ]
}
EOF
```

**Coverage threshold configuration**:
- The >= 70% new code coverage threshold should be configured in SonarCloud quality gate conditions (SonarCloud UI > Quality Gates > Conditions)
- This is the preferred location per the spec (SonarCloud quality gate conditions over CI workflow)
- When SonarCloud is promoted to blocking, this threshold becomes enforced automatically

---

## Reproduction Commands

If branch protection needs to be recreated (repository migration, settings reset):

```bash
# Full branch protection setup
gh api repos/spaarke-dev/spaarke/branches/master/protection \
  -X PUT \
  -H "Accept: application/vnd.github+json" \
  --input - <<'EOF'
{
  "required_status_checks": {
    "strict": true,
    "contexts": [
      "Build & Test (Debug)",
      "Build & Test (Release)",
      "Client Quality (Prettier + ESLint)",
      "Code Quality"
    ]
  },
  "enforce_admins": false,
  "required_pull_request_reviews": {
    "dismiss_stale_reviews": true,
    "require_code_owner_reviews": false,
    "required_approving_review_count": 0
  },
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false
}
EOF
```

```bash
# Verify configuration
gh api repos/spaarke-dev/spaarke/branches/master/protection \
  --jq '{
    required_checks: .required_status_checks.contexts,
    strict: .required_status_checks.strict,
    dismiss_stale_reviews: .required_pull_request_reviews.dismiss_stale_reviews,
    enforce_admins: .enforce_admins.enabled
  }'
```

---

## Rationale

| Decision | Rationale |
|----------|-----------|
| Test jobs are blocking | Tests are the primary quality signal; failures indicate real regressions |
| Client Quality is blocking | Prettier + ESLint enforce code consistency; violations are objective |
| Code Quality is blocking | ADR architecture tests and format checks enforce architectural decisions |
| SonarCloud is advisory (for now) | Nightly workflow not yet merged to master; need 2+ weeks of data |
| AI reviews are advisory | Per spec FR-13: AI findings are subjective and should inform, not block |
| `strict: true` (up-to-date) | Prevents merging stale PRs that might pass on old master but fail on current |
| Admins not enforced | Allows emergency hotfixes without waiting for CI |

---

*Configuration applied by task 040 (code-quality-and-assurance-r1). Last verified: 2026-03-13.*
