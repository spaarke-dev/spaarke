# D-09 — insights-eval.yml Disposition

> **Date**: 2026-06-01
> **Project**: github-actions-rationalization-r1
> **Phase**: 2 (Rationalization) — task 020
> **Related**: spec FR-06 (≤8 workflow target), NFR-04 (`git rm` for deletes), NFR-06 (per-decision record), design.md D-03 (delete-by-default) / D-04 (consolidate liberally)
> **Recommendation**: **DELETE**

## Context

`insights-eval.yml` is a 137-line PR-scoped path-filtered "early feedback alias" for the Insights Engine eval-harness gate. It triggers on `pull_request` for paths under `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/**`, `src/server/api/Sprk.Bff.Api/Services/Insights/**`, and 8 other Insights-specific paths. Its declared purpose is to run the Phase-1 Insights Engine eval-harness: SPEC §3.5.4 facade-boundary grep, `Phase1SmokeTest`, and `PredictMatterCostEvalHarness` (golden dataset, RAG-triad metrics). The workflow file itself explicitly notes: "the main `sdap-ci.yml` workflow also runs these tests as part of the full `dotnet test` matrix — this workflow is a path-scoped early-feedback alias, not a redundant gate."

## Evidence

**Wave A inventory data**:
- Last-30-day runs: **0 total** — never invoked in last 30 days
- Triggers: `pull_request` (paths under Insights/Ai/Insights/**)
- Pattern observed: N/A (no runs to analyze — no PR has touched the Insights-Engine paths in 30 days)
- Last meaningful commit: `ecbfc17e` 2026-05-28 (spaarke-dev) — "feat(insights-engine): D-P16 end-to-end smoke + golden dataset + eval harness baseline (task 070)"

**Recent contributor check (live)**: `git log --since='90 days ago' -- .github/workflows/insights-eval.yml` returns the single commit `ecbfc17e` from 2026-05-28 (within the last week — the most recent of the 7 P5 workflows).

**Most recent runs (live `gh run list --limit 10`)**: empty array `[]` — confirms 0 invocations across the full lookup window.

**Value assessment**: The author's own header comment is the most concise statement of the value-vs-cost tradeoff: the workflow is "a path-scoped early-feedback alias, not a redundant gate." This means (a) its capability (running the Insights eval harness on Insights-specific PRs) is ALSO covered by `sdap-ci.yml` (the canonical CI workflow runs `dotnet test` across the entire test surface, including Insights tests), and (b) its only marginal value is **faster PR feedback** specifically for PRs that touch only Insights paths (skipping the full sdap-ci matrix). Zero PRs have triggered this path in 30 days, meaning the marginal value has not been realized. The Insights Engine project (`ai-spaarke-insights-engine-r1`, worktree `c:\code_files\spaarke-wt-ai-spaarke-insights-engine-r1`) is in active development, but the work is happening on a separate worktree branch — PRs from that worktree have not yet been opened against master in the last 30 days. The 2026-05-28 commit (`ecbfc17e`) is the most recent of all P5 workflows, but commit recency is NOT the same as invocation recency.

The "two workflows running the same tests" pattern is exactly what design.md D-04 (consolidate liberally) targets. The `sdap-ci.yml` full-matrix test already covers the same surface; the only path-scoped acceleration this workflow provides is currently unrealized.

## Recommendation

**DELETE** per design.md D-03 (delete-by-default) + D-04 (consolidate liberally). The dual rationale:
1. **D-03 trigger**: 0 invocations in 30 days, despite a recent commit. The recent commit indicates author intent to retain, but the absence of PR triggers indicates the Insights work is not yet at the master-PR phase.
2. **D-04 trigger**: The capability is redundant with `sdap-ci.yml`. The only marginal value (faster PR feedback) is unrealized.

The recent commit (`ecbfc17e` 2026-05-28) deserves a "speak now" mention — the same author may have intent to land Insights PRs against master soon. However, per D-03's burden-of-proof structure, the recommendation is still DELETE: the workflow can be re-added in 1 commit from git history (`git show ecbfc17e:.github/workflows/insights-eval.yml`) **once Insights Engine work begins generating PRs against master at a cadence where the path-scoped early-feedback latency matters**. Re-adding is cheap; the steady-state cost of carrying a never-invoked workflow is low but non-zero (action-version drift, doc-drift in `.github/WORKFLOWS.md`, surface-area cognitive overhead).

## Execution plan (for task 021 or 022)

**Task 022 (non-deploy dispositions)** executes (insights-eval is a test-gate, not a deploy workflow):

```bash
git rm .github/workflows/insights-eval.yml
git commit -m "$(cat <<'EOF'
chore(workflows): delete insights-eval.yml (D-09)

Zero invocations in last 30 days. Workflow's own header notes the
same tests run via sdap-ci.yml dotnet test matrix — this was a
path-scoped early-feedback alias, not a redundant gate. With zero
PRs touching Insights paths in 30d, the marginal value (faster PR
feedback) is unrealized.

The Insights Engine project is in active development on a separate
worktree (ai-spaarke-insights-engine-r1); this workflow can be
recreated from git history (commit ecbfc17e) when Insights work
begins landing PRs against master at a cadence that justifies the
path-scoped acceleration.

Per design.md D-03 (delete-by-default) + D-04 (consolidate liberally,
sdap-ci covers the same surface) + NFR-04 (git rm, not comment-out).

See projects/github-actions-rationalization-r1/decisions/D-09-disposition-insights-eval.md
EOF
)"
```

Net workflow count change: -1.

## Owner sign-off note

Recent contributors (last 90 days): spaarke-dev (`ecbfc17e` 2026-05-28 — 4 days ago at time of writing). This is a "speak now" candidate per the CLAUDE.md NFR-06 + task POML `notes` section ("if a workflow has zero 30-day runs but a recent thoughtful commit within 14 days, give the author a 1-line 'speak now' mention"). 

**Speak-now note to the Insights Engine author (spaarke-dev)**: The 2026-05-28 baseline commit was thoughtful and detailed, but the workflow has not been triggered by any PR in 30 days. If you intend to land Insights PRs against master in the near future and the path-scoped early-feedback latency matters, please surface this objection in the task 022 PR. Default action is DELETE per D-03 + D-04; recovery from git history is trivial when needed.
