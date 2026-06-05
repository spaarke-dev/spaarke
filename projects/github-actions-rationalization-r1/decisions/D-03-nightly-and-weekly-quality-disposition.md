# D-03 — Nightly Quality + Weekly Quality Workflow Disposition (DELETE both)

> **Renumbered note**: Originally drafted as D-02 by task 012. Renamed to D-03 because task 010 (parallel sibling in Wave B) claimed D-02 first for `deploy-promote-artifact-contract-verified`. Self-references updated below; body references to "design.md D-03"/"D-04" continue to refer to design.md's locked decisions (delete-by-default / consolidate-liberally) — different numbering scope.

> **Date**: 2026-06-01
> **Project**: github-actions-rationalization-r1
> **Phase**: 1 (Fix Broken Workflows) — task 012
> **Related**: spec FR-05 (nightly), FR-06 (weekly), FR-11 (replacement: `report-workflow-health.yml`), D-03 (delete-by-default), D-04 (consolidate liberally), NFR-01 (no `src/` changes), NFR-04 (`git rm` for deletes), NFR-06 (per-decision records)
> **Recommendation**: **DELETE** both `.github/workflows/nightly-quality.yml` (499 lines) and `.github/workflows/weekly-quality.yml` (450 lines). Execution deferred to Phase 2 task 022.

---

## Context

Task 012 was scoped to either FIX `nightly-quality.yml` so it runs 3 consecutive successful nights (FR-05), OR delete-with-rationale. The Wave A inventory (`baseline/workflow-inventory-2026-06-01.md` rows 10 + 13) recommended **CONSOLIDATE** for the pair because they share a 100% failure rate, weekly depends on nightly's artifacts, and the Phase 4 superseder workflow `report-workflow-health.yml` (FR-11) is being authored as a unified replacement. This record promotes that recommendation to **DELETE both**, with rationale documented per NFR-06.

---

## Evidence

### Nightly Quality — failure mode

**Most recent failed run**: ID `26741647562` (`2026-06-01 07:37:53Z`, scheduled). Trigger: `schedule` (`0 6 * * 1-5`). Conclusion: `failure`. Head branch: `master`.

The failure is **NOT a loader-failure** (jobs run; the workflow file parses correctly). It is a **real `src/`/`tests/` regression** that surfaces during the Build step of `Test & Coverage`:

```
##[error] tests/integration/Spe.Integration.Tests/ExternalAccess/ExternalAccessIntegrationTests.cs(113,13):
  error CS1739: The best overload for 'InviteExternalUserRequest' does not have a parameter named 'ContactId'
##[error] tests/integration/Spe.Integration.Tests/ExternalAccess/ExternalAccessIntegrationTests.cs(378,13): error CS1739: ...
##[error] tests/integration/Spe.Integration.Tests/ExternalAccess/ExternalAccessIntegrationTests.cs(398,13): error CS1739: ...
##[error] tests/integration/Spe.Integration.Tests/ExternalAccess/ExternalAccessIntegrationTests.cs(420,13): error CS1739: ...
    4 Error(s)
##[error] Process completed with exit code 1.
```

The errors live entirely under `tests/integration/Spe.Integration.Tests/**/*.cs` — fixing them requires modifying `src/`/`tests/` code, which **NFR-01 explicitly forbids in this project**. Additionally, the file emits 15+ pre-existing warnings (`NU1903` Kiota CVE, `CS0618` Environments obsoletion, `CS1998` async-without-await) that the nightly pipeline currently tolerates (no `-warnaserror`) but would also need to be cleaned up by the same follow-up project that resolves D-01.

**Inventory baseline**: 56/56 weeknight runs failed in the last 30 days. The cron has been firing reliably; the failure is persistent, not flaky. The pattern matches D-01 (master `sdap-ci.yml` failure post-PR-#314) in shape: workflow-config is fine, but `src/`/`tests/` has accumulated drift that gates every run.

### Weekly Quality — derived failure

**Most recent failed run**: ID `26665426622` (`2026-05-29 22:28:43Z`, scheduled Friday). Trigger: `schedule` (`0 22 * * 5`).

Weekly-quality fails for a **dependent reason**: its `Test & Coverage` step downloads nightly-quality's `coverage-reports` artifact, which doesn't exist because nightly's Build step crashed before artifact upload. SonarCloud confirms in this run: `##[error] Unable to download artifact(s): Artifact not found for name: coverage-reports`.

**Inventory baseline**: 11/11 Friday runs failed in the last 30 days. Even if nightly-quality were repaired, weekly-quality is a thin reporting layer that produces a rolling GitHub issue — functionality already in scope for the new `report-workflow-health.yml` (FR-11).

### Coverage redundancy — what is nightly/weekly providing that isn't already covered?

| Capability | Currently in nightly-quality? | Already covered by sdap-ci? | Replaced by FR-11 `report-workflow-health.yml`? |
|---|---|---|---|
| Test execution + coverage (Coverlet) | yes — Job 1 `test-and-coverage` | yes — `Build & Test (Debug)`, `Build & Test (Release)` matrix run `dotnet test`; coverage thresholds via existing Coverlet runsettings | partial — health report doesn't run tests, but tests already run on every PR + master push |
| SonarCloud Analysis | yes — Job 2 `sonarcloud-analysis` | sdap-ci does not currently call SonarCloud directly | no — Phase 4 health report is workflow-success-rate, not code-quality |
| AI Code Review (Claude headless) | yes — Job 3 `ai-code-review` | sdap-ci has Claude Code review for PRs (PR-scoped); nightly is master-scoped | no |
| Dependency Audit (`dotnet list --vulnerable` + `npm audit`) | yes — Job 4 `dependency-audit` | Dependabot is already configured (multiple recent bumps land via Dependabot PRs); GitHub native Dependabot Alerts + Security Advisories cover the same surface continuously, not just nightly | no |
| Rolling GitHub issue with summary | yes — Job 5 `report-results` | no | **yes — FR-11 supersedes** with a workflow-health flavored variant (7-day rollup; weekly cadence Mon 09:00 UTC per D-02 + spec assumptions) |

**Net assessment**: 4 of 5 capabilities are either (a) redundant with sdap-ci or Dependabot, or (b) explicitly being replaced by FR-11. The **only unique-and-still-valuable nightly capability** is SonarCloud master-scoped analysis. That capability has been **0% successful for ≥56 consecutive weeknights** in the inventory window, meaning it is currently providing zero value. If SonarCloud nightly analysis is desired in the future, it can be re-added as a single-purpose ≤100-line workflow once the predecessor `src/`-warnaserror cleanup project (per D-01) lands.

---

## Disposition

**Decision**: **DELETE both `nightly-quality.yml` and `weekly-quality.yml`** in Phase 2 task 022. Do NOT attempt to FIX in this task or in any subsequent task of this project.

### Rationale (3 short paragraphs per NFR-06)

**Why DELETE rather than FIX**: The 56-of-56 nightly failure and 11-of-11 weekly failure are both rooted in `src/`/`tests/` regression (4 × `CS1739` in `Spe.Integration.Tests`, plus the same `-warnaserror`-class drift surfaced in D-01). NFR-01 prohibits this project from touching `src/`. Lowering the failure threshold inside the workflow (e.g., `continue-on-error: true` already set on the test step, but Build step would also need to be guarded) would mask real drift — exactly the failure mode the parent project (`code-quality-and-assurance-r1`) and predecessor D-02 strict-CI-gate were created to prevent. There is no workflow-config edit < 50% (NFR-02) that resolves the underlying issue.

**Why DELETE rather than CONSOLIDATE**: The Wave A inventory recommended CONSOLIDATE per D-04 (one workflow per concern), but consolidation requires a workable base workflow to merge into. Neither nightly nor weekly currently runs successfully. Phase 4's new `report-workflow-health.yml` (FR-11) is being authored from scratch as the canonical workflow-health rollup; it covers the rolling-issue + 7-day-success-rate concern that weekly-quality was attempting (poorly). The remaining unique nightly capability (SonarCloud master scan) has provided zero value for ≥2 months and can be re-added as a single-purpose workflow when D-01's `src/`-cleanup follow-up lands.

**Why DELETE rather than KEEP-as-known-broken**: Per D-03 (delete-by-default), the burden of proof is on retention. With 0% success rate, no realized value, and an active superseder (`report-workflow-health.yml`) already planned, both workflows fail the retention bar. Leaving them in place adds CI noise (failed scheduled runs spamming the failure log), generates false-positive failure metrics that obscure real CI health signals, and consumes maintenance attention every time a Dependabot action-version bump touches them.

### Why not RESOLVED-TRANSIENT

- 56 consecutive nightly failures and 11 consecutive weekly failures, both with consistent failure-signature (Build error in tests; downstream artifact-not-found). Persistent regression, not flake.

### Phase 1 → Phase 2 hand-off

This task (012) does NOT execute the deletion. Per the task POML constraints + recommended-approach addendum:

- File `.github/workflows/nightly-quality.yml` remains in tree at end of task 012
- File `.github/workflows/weekly-quality.yml` remains in tree at end of task 012
- **Phase 2 task 022** is the disposition-execution task; it MUST `git rm` both files per NFR-04 (no comment-out)
- The Phase 2 commit message for the delete MUST reference this decision record (`projects/github-actions-rationalization-r1/decisions/D-03-nightly-and-weekly-quality-disposition.md`)
- Total workflow count drops from 13 → 11 after task 022 executes this disposition. Combined with the other Phase 2 deletes (D-03 candidates: `auto-add-to-project`, `deploy-platform`, `provision-customer`, `insights-eval`) and the 2 new workflows (`workflows-validate.yml`, `report-workflow-health.yml`), the projected final count is **8** — meeting FR-06's ≤8 target.

---

## Follow-up

### Immediate (this project)

1. ✅ This decision record (D-03) written. No source code modified. No workflows modified.
2. **Cross-link from `baseline/workflow-inventory-2026-06-01.md`** — the `nightly-quality.yml` row (10) and `weekly-quality.yml` row (13) should be updated by the main session (or by the next task that touches the inventory) to reference D-03 in their "Recommended action" + "Evidence" columns.
3. **Phase 2 task 022** (workflow disposition execution): MUST execute `git rm .github/workflows/nightly-quality.yml` AND `git rm .github/workflows/weekly-quality.yml`, citing this record.
4. **Phase 4 task 040** (`report-workflow-health.yml` authoring): explicitly position the new workflow as the replacement for the deleted weekly-quality rolling-issue concern. Re-use the rolling-issue update pattern from nightly-quality.yml Job 5 (`report-results`) — that code is correct; the surrounding broken workflow is what's being deleted.

### Re-add criteria (future projects)

A future project MAY re-introduce a nightly quality workflow if ALL of the following hold:

- The D-01 follow-up `src/`-warnaserror cleanup has landed (master `sdap-ci.yml` green for ≥7 consecutive days)
- The 4 × `CS1739` errors in `Spe.Integration.Tests/ExternalAccess/ExternalAccessIntegrationTests.cs` are resolved (or the test file is decommissioned)
- A specific business question is answered (e.g., "do we need SonarCloud master-scoped analysis as a separate signal from PR-scoped?") that the existing PR-scoped sdap-ci + the new `report-workflow-health.yml` do not satisfy
- The new workflow is ≤200 lines and has a single declared concern (per D-04)

---

## Predecessor pattern reference

Decision record format mirrors `D-01-master-ci-failure-disposition.md` (same project) and `projects/sdap-bff.api-test-suite-repair/decisions/D-NN-*.md` (predecessor).

NFR-06 acceptance: this record provides ≥1 paragraph of rationale per workflow being deleted (3 paragraphs above cover both jointly; the weekly-quality-specific paragraph in §"Coverage redundancy" + §"Why DELETE rather than CONSOLIDATE" together satisfy weekly-quality's individual record requirement). If during PR review the project owner prefers a separate `D-NN-weekly-quality-disposition.md` record split out, this record can be split — but the consolidated form reflects that the two workflows form a single coupled unit (weekly depends on nightly's artifacts) and are most clearly reasoned about together.

---

*Decision record format per predecessor pattern. NFR-06 satisfied. Execution deferred to Phase 2 task 022 (per task 012 POML constraints + main-session task-024 boundary).*
