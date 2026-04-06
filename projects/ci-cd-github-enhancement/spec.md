# CI/CD GitHub Actions Enhancement

## Executive Summary

Rehabilitate GitHub Actions workflows to provide trustworthy, fast quality gates without blocking development speed. Current CI is slow and unreliable, leading the team to ignore it. This project introduces a tiered CI model (blocking/advisory/info) with documented escape hatches for docs-only and emergency PRs, while preserving every existing quality check.

## Scope

**In Scope:**
- Triage and fix root causes of current `sdap-ci.yml` failures
- Implement tiered CI model: Tier 1 (blocking, <3 min), Tier 2 (advisory, <8 min), Tier 3 (info, post-merge)
- Add escape hatches: `[skip-ci]` commit marker and `[docs-only]` PR label
- Migrate architecture tests, format, lint, unit tests, and doc checks to their appropriate tiers
- Add a markdown file-path validator and Last Reviewed stamp check
- Update branch protection to require only Tier 1 status checks

**Out of Scope:**
- Azure deployment workflows (`deploy-bff-api.yml`, `deploy-promote.yml`, `deploy-slot-swap.yml`, `deploy-infrastructure.yml`, `provision-customer.yml`) — they work and are untouched
- New test frameworks or new quality tooling
- Branch protection rule philosophy discussion (separate concern)
- Nightly/weekly quality pipelines (already follow the right pattern)

## Requirements

1. Tier 1 blocking checks run in **<3 min p95**
2. Tier 2 advisory checks run in **<8 min p95**, post results as a single PR comment, and are **not** required status checks
3. Escape hatches: `[skip-ci]` in commit message skips Tier 1; `[docs-only]` PR label skips Tier 2
4. All existing quality coverage is preserved — no checks are removed, only reorganized by tier
5. Every escape hatch use is auditable via GitHub logs
6. Azure deployment workflows remain completely untouched

## Success Criteria

1. Team reports CI signal is trustworthy (survey after 2 weeks)
2. PR-to-merge time decreases by **>30%**
3. Zero advisory-tier false positives on typical PRs
4. No regression in code quality metrics (coverage %, ADR violations, vulnerable deps) over 2 weeks post-rollout
5. Tier 1 flake rate <1%

## Technical Approach

Phased implementation:

1. **Diagnostic & Triage** — catalog recent failures, measure p50/p95, classify fix vs silence vs remove
2. **Tier 1** — new `ci-tier1-blocking.yml` with build, critical arch tests, markdown link validator, Last Reviewed check
3. **Tier 2** — new `ci-tier2-advisory.yml` posting a PR comment; migrates format, lint, unit tests, full ADR compliance
4. **Tier 3** — move slow integration tests and coverage to post-merge (fold into existing nightly where appropriate)
5. **Escape Hatches** — implement `[skip-ci]` and `[docs-only]` detection; document in `docs/procedures/ci-cd-workflow.md`
6. **Branch Protection & Rollout** — update required checks, retire old `sdap-ci.yml`, monitor for 2 weeks

Keep existing deployment workflows untouched throughout.

## Reference

- `design.md` in this directory — full design with tier tables, phase details, and risks
- `docs/architecture/ci-cd-architecture.md` — current 13-workflow architecture
- `.github/workflows/sdap-ci.yml` — the monolithic workflow being split
- `tests/Spaarke.ArchTests/` — architecture test suite
