# D-07 — deploy-office-addins.yml Disposition

> **Date**: 2026-06-01
> **Project**: github-actions-rationalization-r1
> **Phase**: 2 (Rationalization) — task 020
> **Related**: spec FR-06 (≤8 workflow target), NFR-06 (per-decision record), design.md D-03 (delete-by-default — overridden by evidence-of-value)
> **Recommendation**: **KEEP**

## Context

`deploy-office-addins.yml` is a 51-line workflow that builds Spaarke Office Add-ins (Outlook, Word) and deploys to an Azure Static Web App via `Azure/static-web-apps-deploy@v1`. It is triggered by (a) `push` to `master` or `work/SDAP-outlook-office-add-in` on paths `src/client/office-addins/**`, and (b) `workflow_dispatch`. It hardcodes `ADDIN_CLIENT_ID`, `TENANT_ID`, `BFF_API_CLIENT_ID`, and `BFF_API_BASE_URL` as `env:` values (this is a smell but not a security issue — they are public client IDs and a base URL, not secrets).

## Evidence

**Wave A inventory data**:
- Last-30-day runs: 46 total / 30 success / 16 failure → **65.2% success** (17 on master, 12 success on master)
- Triggers: `push` (paths `src/client/office-addins/**`) + `workflow_dispatch`
- Pattern observed: NOT loader-failure. The workflow runs to completion; the failure mode is mostly transient SWA deploy retries.
- Last meaningful commit: `0bbebe73` 2026-05-26 (Spaarke Dev) — "sdap-bff-api-remediation: Phase 4 + 5 (demo) + Phase 6 docs + LegalWorkspace client /api fix (#295)"

**Recent contributor check (live)**: `git log --since='90 days ago' -- .github/workflows/deploy-office-addins.yml` returns the single commit `0bbebe73` from 2026-05-26 (within the last week, via PR #295).

**Most recent runs (live `gh run list --limit 10`)**: 10 runs since 2026-01-31; mix of `success` and `failure` across `push` and `workflow_dispatch` triggers. **2 most recent runs (2026-05-26, 2026-05-20) both failed**, suggesting potential regression — but the bulk of the 30-day window (30 of 46 runs successful) is still real usage. The active branch `work/SDAP-outlook-office-add-in` exists and the workflow specifically tracks pushes to it, indicating ongoing Office Add-ins development.

**Value assessment**: This is an actively-used deploy workflow with real successful deployments in the last 30 days (30 successes on production-relevant pushes — not no-op skips like deploy-slot-swap). The Office Add-ins (Outlook + Word) are a documented Spaarke client surface per CLAUDE.md (worktree `C:\code_files\spaarke-wt-email-communication-solution-r2`). The 65.2% success rate is below FR-14's 90% target but well above the D-03 delete-by-default threshold (zero realized value). Deletion would force manual deploys for an actively-used surface and would be a strict regression.

## Recommendation

**KEEP** per evidence-of-value override of design.md D-03 default. 30 successful deploys in 30 days is a clear signal that this workflow provides realized value. The 65.2% success rate (16 failures) is a quality issue — the 2 most recent failures (2026-05-20 and 2026-05-26) may indicate a regression worth investigating, but that investigation is OUT OF SCOPE for this project (NFR-01 forbids touching `src/client/office-addins/**`; the workflow itself is fine). The hardcoded `env:` values (`ADDIN_CLIENT_ID`, `TENANT_ID`, etc.) are a configuration smell but not a security issue (these are public client IDs). They can be refactored to repository variables in a follow-up project; out of scope for this disposition task.

Per D-03's burden-of-proof structure, this workflow CLEARS retention because (a) recent successful deploys exist, (b) it is the only path to deploy Office Add-ins (no equivalent local script per `docs/guides/` review), (c) the 2026-05-26 commit (`0bbebe73`) shows active maintenance, and (d) it is referenced in CLAUDE.md's tool inventory (worktree exists for Office Add-ins work).

## Execution plan (for task 021 or 022)

**Task 021 (deploy-* dispositions)**: NO `git rm`. NO consolidation. Action:

1. KEEP `.github/workflows/deploy-office-addins.yml` as-is. Do not edit in this project (NFR-02 <50% replacement, and the file is functioning).
2. Document the hardcoded-env-values smell as a follow-up item in `.github/WORKFLOWS.md` (FR-09 deliverable in task 042). Do not refactor in this project.
3. Document the 2 recent failures (2026-05-20, 2026-05-26) as a follow-up investigation item — the failure mode is SWA-deploy-side; ownership belongs to the Office Add-ins workstream, not this project.
4. The workflow remains a P5 "untested" only in the sense that it has not been formally smoke-tested by this project; observed run history confirms it works.

Net workflow count change: 0 (KEEP).

## Owner sign-off note

Recent contributors (last 90 days): Spaarke Dev (`0bbebe73` 2026-05-26 via PR #295 — sdap-bff-api-remediation Phase 4+5+6). The workflow is touched as part of broader remediation work; the underlying Office Add-ins development is ongoing per the worktree existence. No "speak now" trigger required — recommendation is KEEP.
