# CI/CD GitHub Actions Enhancement

> **Status**: Design
> **Created**: 2026-04-05
> **Purpose**: Rehabilitate GitHub Actions to provide quality gates without blocking development speed

---

## Problem Statement

The team is actively ignoring their own CI/CD protections. The complaint: *"GitHub Actions create significant overhead for every PR AND seem to always fail, so we ignore our own protections."*

Concretely, the current system (13 workflows documented in `docs/architecture/ci-cd-architecture.md`) has the following symptoms:

- **Slow feedback loop**: `sdap-ci.yml` runs a Debug+Release matrix build with `-warnaserror`, full unit tests, Prettier, ESLint `--max-warnings 0`, `dotnet format` verification, plugin size validation, dependency audit, Trivy scan, and ADR NetArchTest suite — all as blocking checks on every PR.
- **Unreliable signals**: A mix of legitimate failures, flaky tests, pre-existing tech debt surfacing through linters, and warnings-as-errors creates noise that makes genuine issues hard to spot.
- **All-or-nothing blocking**: Nearly every check in `sdap-ci.yml` is blocking. There is no tier separation between "build is broken" (must block) and "formatting drifted" (should inform).
- **No escape hatch**: Docs-only PRs run the full CI suite, wasting minutes on changes that cannot break code.
- **Cultural cost**: When CI is ignored, every real failure becomes invisible. The protections designed to maintain quality now actively undermine it.

## Goals

- **Fast feedback loop**: Blocking tier completes in <3 min p95; advisory tier in <8 min p95
- **Trustworthy signals**: Zero flaky tests in blocking tier; advisory failures have clear, actionable messages
- **Clear pass/fail**: Developers see a small number of status checks, not 15+ buried in logs
- **Easy overrides**: Legitimate cases (docs-only PRs, emergencies) have documented, auditable escape hatches
- **Maintain code quality**: No net loss of coverage — every existing check is preserved, just moved to the tier that matches its reliability and cost

## Non-Goals

- **Replacing Azure deployment pipelines** — `deploy-bff-api.yml`, `deploy-slot-swap.yml`, `deploy-promote.yml`, `deploy-infrastructure.yml`, `provision-customer.yml` all work and are out of scope
- **Adding new test frameworks** — this is about organizing existing checks, not introducing new ones
- **Branch protection rule changes** — the new tiers will feed into branch protection, but the rules themselves are a separate discussion with the team
- **Replacing nightly/weekly quality workflows** — `nightly-quality.yml` and `weekly-quality.yml` already follow the right pattern (deep analysis, non-blocking, rolling issues)

## Current State Assessment

*This section will be filled in during Phase 1 (Diagnostic & Triage).*

- [ ] Run `gh run list --workflow=sdap-ci.yml --status=failure --limit=20` to catalog recent failures
- [ ] Run `gh run list --workflow=sdap-ci.yml --limit=50 --json databaseId,conclusion,createdAt,updatedAt` and compute p50/p95 duration
- [ ] Identify top 3 failure patterns and their root causes
- [ ] For each pattern, decide: **fix** (resolve root cause), **silence** (move to advisory tier), or **remove** (check has negative ROI)
- [ ] Catalog which checks in `sdap-ci.yml` are genuinely required-to-merge vs aspirational
- [ ] Measure current CI time distribution (which jobs dominate the critical path?)

## Proposed Tiered Model

### Tier 1 — Fast Blocking (<3 min, must pass)

Runs on every PR. Required status check for merge. Failure blocks the PR.

| Check | Implementation | Why Tier 1 |
|-------|----------------|------------|
| Build (Debug only, no `-warnaserror`) | `dotnet build -c Debug` | Must compile to merge. Warnings-as-errors promoted to Tier 2 to avoid blocking on pre-existing debt. |
| Critical arch tests | Subset of `Spaarke.ArchTests` — the "MUST NOT" rules from ADRs | These encode non-negotiable architectural rules. Fast (<30s) and deterministic. |
| Markdown file-path validator | New script: scans touched `.md` files for internal links; fails if any reference non-existent files | R2 found ~40 broken doc links; this would have caught most. |
| Last Reviewed stamp check | New script: warns (doesn't block) when a touched doc in `docs/architecture/` lacks an updated `Last Reviewed` date | Catches stale docs as they're touched. |

**Budget**: 3 minutes p95. If this tier exceeds 3 min, reduce scope.

### Tier 2 — Advisory (<8 min, PR comment only, non-blocking)

Runs on every PR. Posts a single PR comment summarizing results. **Not a status check** — developers see it, but it does not block merge.

| Check | Implementation | Why Tier 2 |
|-------|----------------|------------|
| `dotnet format --verify-no-changes` | Existing step from `sdap-ci.yml` | Formatting drift is advisory; blocking on it frustrates drive-by fixes. |
| Unit tests (`Category=Unit` only) | `dotnet test --filter Category=Unit` | Unit tests should be stable, but history shows flakes. Advisory until proven reliable. |
| ADR compliance check (full) | Existing NetArchTest + logic from `adr-check` skill | Already advisory in current CI; formalize here. |
| Prettier on `.ts/.tsx/.md` | Existing step from `sdap-ci.yml` | Style preference, not correctness. |
| ESLint (warnings allowed) | `eslint . --max-warnings 50` (relaxed from 0) | Lint pre-existing debt shouldn't block new work. |
| Plugin size validation | Existing step | Already well-behaved; keeping in advisory until proven it catches regressions. |

**Budget**: 8 minutes p95.

### Tier 3 — Info (<20 min, runs on merge to master)

Runs post-merge. Results go to rolling GitHub issues (same pattern as existing `nightly-quality.yml`).

| Check | Implementation | Why Tier 3 |
|-------|----------------|------------|
| Full integration tests | Existing `Category=Integration` suite | Slow and environment-dependent; not suitable for PR gate. |
| Coverage report | Coverlet + existing runsettings | Trend signal, not per-PR gate. |
| Trivy security scan | Existing `sdap-ci.yml` step | Already uploads SARIF; runs post-merge is sufficient for most findings. |
| Dependency vulnerability audit | Existing `dotnet list --vulnerable` + `npm audit` | Already scheduled nightly; reinforce there. |
| SonarCloud analysis | Existing nightly step | Already post-merge. |

**Budget**: 20 minutes.

### Escape Hatches

| Mechanism | Effect | Audit |
|-----------|--------|-------|
| `[skip-ci]` in commit message | Skips Tier 1 entirely. For docs-only or emergency commits only. | Logged in run summary; monthly audit of usage. |
| `[docs-only]` PR label | Skips Tier 2 entirely (Tier 1 still runs, but doc-link validator is the main check). | Label application is logged by GitHub. |
| Admin override | GitHub branch protection allows admins to merge over a failed Tier 1 after manual review. | GitHub logs all override merges. |

---

## Implementation Phases

### Phase 1: Diagnostic & Triage (1-2 days)

- Catalog the last 20-50 CI failures on `sdap-ci.yml`
- Classify each: **legitimate** (code broke) vs **flaky** (retry passes) vs **false-positive** (pre-existing debt surfaced)
- Measure current p50/p95 run times for `sdap-ci.yml`
- Document findings in `projects/ci-cd-github-enhancement/notes/diagnostic-findings.md`

### Phase 2: Tier 1 Implementation (2-3 days)

- Create `.github/workflows/ci-tier1-blocking.yml`
- Write markdown link validator script (`scripts/validate-markdown-links.ps1` or `.sh`)
- Write Last Reviewed stamp check script
- Identify the subset of `Spaarke.ArchTests` that belong in Tier 1 (the MUST NOT rules only)
- Keep `sdap-ci.yml` running in parallel during transition for comparison

### Phase 3: Tier 2 Implementation (2-3 days)

- Create `.github/workflows/ci-tier2-advisory.yml`
- Configure as PR comment poster (reuse the ADR-comment deduplication pattern already in `sdap-ci.yml`)
- Migrate format, lint, unit test, ADR compliance checks into this workflow
- Confirm it does **not** register as a required status check

### Phase 4: Tier 3 Implementation (1-2 days)

- Move slow integration tests out of `sdap-ci.yml` into a post-merge workflow (or fold into existing `nightly-quality.yml`)
- Ensure coverage reporting still runs (trend signal preserved)
- Confirm security scans run on the expected cadence

### Phase 5: Escape Hatches (1 day)

- Implement `[skip-ci]` detection in Tier 1 workflow (`if: !contains(github.event.head_commit.message, '[skip-ci]')`)
- Implement `[docs-only]` label detection in Tier 2 workflow
- Document override procedures in `docs/procedures/ci-cd-workflow.md`
- Add monthly audit script/checklist for escape hatch abuse

### Phase 6: Branch Protection & Rollout (1 day)

- Update branch protection rules to require only Tier 1 checks
- Retire the old `sdap-ci.yml` (or trim it to a redirect/no-op for transition)
- Announce changes to the team
- Monitor adoption for 2 weeks; collect feedback

## Success Criteria

- Tier 1 runs complete in **<3 min p95**
- Tier 2 runs complete in **<8 min p95**
- Zero Tier 1 failures on docs-only PRs (after `[docs-only]` label applied)
- Team survey (2 weeks post-rollout): majority report CI signals are trustworthy
- **PR-to-merge time decreases by >30%** (measured from PR open to merge)
- No regression in code quality metrics (coverage %, ADR violations, vulnerable deps) over the 2 weeks post-rollout

## Risks

| Risk | Mitigation |
|------|------------|
| Advisory tier being ignored | Post PR comments with bot mention; summarize failures prominently; include actionable fix commands |
| Escape hatches being abused | Monthly audit of `[skip-ci]` commits and `[docs-only]` labels; include in quarterly retrospective |
| Breaking existing deployment workflows | Azure deploy workflows (`deploy-bff-api.yml`, `deploy-promote.yml`, etc.) remain completely untouched; Tier 1/2/3 are new files, not modifications |
| Removing genuinely-useful blocking checks | Phase 1 diagnostic must explicitly justify each check's movement; keep `sdap-ci.yml` in parallel for one sprint before retiring |
| Tier 1 still flaky after migration | If Tier 1 shows flake > 1%, pause rollout and return to Phase 1 |

## References

- `docs/architecture/ci-cd-architecture.md` — Current 13 workflows and their responsibilities
- `.github/workflows/sdap-ci.yml` — The current monolithic CI workflow that this effort splits
- `tests/Spaarke.ArchTests/` — Architecture tests; subset goes to Tier 1, full suite to Tier 2
- `projects/ai-procedure-refactoring-r2/notes/lessons-learned.md` — R2 found ~40 bugs; a markdown link validator would have caught many of the doc-side issues
- `docs/procedures/ci-cd-workflow.md` — Operational procedures (to be updated in Phase 5)

## Related Skills (to invoke during this project)

- `/code-review` — for reviewing the new workflow YAML files before merge
- `/adr-check` — to ensure the new workflows themselves comply with repo standards
- `/push-to-github` — at the end of each phase
- `/ai-procedure-maintenance` — if the tiered model requires new patterns or constraints

---

*Next step: Run the Phase 1 diagnostic to understand current failure patterns before writing any new workflow YAML.*
