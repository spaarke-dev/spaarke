# Task 088 — Deviations

> **Task**: 088 — Coordinate `.github/workflows/**` gate wiring via PR to `ci-cd-unit-test-remediation-r1`
> **Date completed**: 2026-08-18
> **Rigor**: STANDARD (doc-only coord spec authoring)
> **Deliverable**: `projects/customer-provisioning-orchestration-r1/notes/phase-h-ci-wiring-coord-pr.md`

## Deviations from POML

**None material.** All 6 acceptance criteria met as authored.

## Small choices worth flagging

1. **Consolidation strategy for task 067's partial coord spec** — POML offered two options: (a) MOVE 067's content into this task's spec + mark 067 superseded, or (b) KEEP 067's file as detail source + reference from Section 3. Chose **(b)** to preserve 067's 194 lines of detailed YAML + secrets-contract + failure-mode analysis intact and avoid a lossy re-summarization. Added a `Status (2026-08-18)` header note to 067's file pointing at the umbrella spec so future readers know the relationship. Umbrella §3.1 explicitly states "preserves 067's spec as the canonical detail source" and defers exact YAML to 067 §3.a.

2. **Section 2 workflow-diff target — dedicated NEW job vs new step in `code-quality`** — Chose NEW top-level `tenant-isolation` job (not a step under `code-quality`) because: (i) the existing `code-quality` job has `continue-on-error: true` at the JOB level (line 441 of `sdap-ci.yml`) which would silently swallow any tenant-isolation red — this matches the exact pattern that `eval-gate` and `compose-fidelity-gate` deliberately separated from `build-test` for the same reason (see the comment blocks on those jobs, lines 232-271 and 730-748 of `sdap-ci.yml`); (ii) I1–I5 are security-invariant checks (cross-tenant leak) that MUST NEVER be advisory. Documented rationale in Section 2.3 preamble.

3. **Section 1 workflow-diff target — step in `code-quality` (not a new job)** — Naming-conformance is a static architectural-invariant scan in the same category as the ADR ArchTests already running in `code-quality`. Adding a step keeps naming next to its cousins without job-startup overhead. Advisory posture matches the current `continue-on-error: true` at the job level, so no separate blocking-vs-advisory conflict emerges.

4. **Redundancy note in Section 2.5** — The existing `code-quality > ADR architecture tests` step already runs ALL of `Spaarke.ArchTests` (including I1–I5) unfiltered. Adding the dedicated `tenant-isolation` job double-runs them (~5 s cost). Explicitly documented as intentional belt-and-suspenders with an optional post-flip simplification path — did not remove the duplication because (a) it's cheap, (b) removing would require ci-cd-r1 to touch two things in one PR (add new job + add exclude filter to existing step), which is against the "small reviewable PRs" principle.

## Verification steps executed

1. Read `spec.md` NFR-08 + FR-35 + FR-13 via project CLAUDE.md tag mapping (implicit via project loading; POML `<constraints>` cite exact FR/NFR IDs).
2. Read `notes/r3-handoff.md` in full — confirmed 28-day ci-cd-r1 window + naming-conformance-check as r3 asset + Graph app-role parity as r1-owned test.
3. Read `notes/graph-app-role-parity-coord-pr.md` (067's partial spec) in full — confirmed 194 lines, complete YAML + secrets + failure modes.
4. Read `.github/workflows/sdap-ci.yml` in full (784 lines) — mapped current job shape: `security-scan`, `build-test`, `eval-gate`, `client-quality`, `code-quality`, `integration-readiness`, `adr-pr-comment`, `summary`, `compose-fidelity-gate`.
5. Read `.github/workflows/nightly-health.yml` in full (779 lines) — confirmed jobs: `flake-hunt`, `bundle-size`, `vuln-scan`, `full-integration`, `coverage-observation`, `trivy-fs`, `dep-audit`, `report`.
6. Grep-verified `naming-conformance-check.ps1` — located at `scripts/naming-conformance-check.ps1` + confirmed 1 existing consumer at `scripts/Validate-DeployedEnvironment.ps1:392` (H13 deploy-time gate).
7. Confirmed dependency tasks 064/065/066/067 + 084/085/086/087 exist as POMLs.
8. Verified all 14 file paths referenced in the spec against current branch HEAD — table in umbrella spec §5.

## Zero-workflow-edit verification

`git diff .github/workflows/` at completion is empty — this task modified NO workflow files (per POML SCOPE constraint). The three workflow diffs described in the spec are applied by ci-cd-r1, not this task.

## Files changed by this task

- **NEW**: `projects/customer-provisioning-orchestration-r1/notes/phase-h-ci-wiring-coord-pr.md` (umbrella coord spec, 3 sections + coord message + verification table + acceptance)
- **NEW**: `projects/customer-provisioning-orchestration-r1/notes/task-088-deviations.md` (this file)
- **EDIT**: `projects/customer-provisioning-orchestration-r1/notes/graph-app-role-parity-coord-pr.md` (added top-of-file "PRESERVED AS DETAIL SOURCE" note pointing at the umbrella; content unchanged)
- **EDIT**: `projects/customer-provisioning-orchestration-r1/tasks/TASK-INDEX.md` (088 row 🔲/⏸ → ✅)
