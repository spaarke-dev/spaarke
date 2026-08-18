# task-066 deviations note — verify Register-EntraAppRegistrations.ps1 fix + generalize I1 scan

> **Task**: `projects/customer-provisioning-orchestration-r1/tasks/066-verify-register-entra-fix-and-pre-commit-scan.poml`
> **Wave**: Wave 4 Batch 4C
> **Author**: main-session (Sonnet 5, effort high per POML `<model-tier>`)
> **Date**: 2026-08-17
> **Rigor**: FULL (test-modifying override per root CLAUDE.md §8)

---

## 1. Scope realization vs POML expectation

POML step 4 anticipated the task might need to **generalize** the I1 scan from a single-file scan of `Register-EntraAppRegistrations.ps1` to a repo-wide `.ps1` scan. Empirical verification against the just-landed task 064 code (commit `40b09f837`) showed the scan was **already generic** — `Directory.EnumerateFiles(scriptsDir, "*.ps1", SearchOption.AllDirectories)` at `I1_NoHardcodedTenantTests.cs:210` — and had in fact caught three sibling-script offenders task 064's deviations note filed against task 065 (§4 of the verification report).

**No generalization was needed.** Task 066's work realized as:

1. **Verification** (POML steps 1-3) — confirmed commit `1834b77bc` is in the branch AND that the fix is intact in the working tree.
2. **Refactor for testability** — extracted the inline scan in `ProvisioningScripts_HaveNoHardcodedTenantDefault` into a new private helper `ScanForI1Offenders(scanRoot, excludedRelPrefixes)`, and threaded the excludes parameter through `EnumerateProvisioningScripts`. Behavior of the main test unchanged.
3. **Regression seed** (POML step 6) — new test `ScanForI1Offenders_TempScriptWithTenantDefault_ReportsFileAndLine` authors a temp `.ps1` under `Path.GetTempPath()`, invokes the scanner against just the temp dir, asserts the offender is caught with correct file:line, and deletes the temp file in `finally`. Closes the negative-control gap the two regex-only tests left open (proved regex catches shape in-memory, not that the scanner catches a real file end-to-end).

---

## 2. Test-suite state at commit time

`dotnet test tests/Spaarke.ArchTests/ --filter "FullyQualifiedName~I1_NoHardcoded"` result at verification time (task 065's WIP applied to disk, uncommitted; task 066's changes staged for `git commit --only`):

| Test | Result | Owner of resolution |
|---|---|---|
| `ProvisioningScripts_HaveNoHardcodedTenantDefault` | **PASS** | task 065 fixed the 3 sibling scripts on disk before this task's verification ran; when task 065's commit lands, this test PASSES for real |
| `TenantIdDefaultRegex_FlagsHardcodedGuidDefault` | PASS | task 064 (already landed) |
| `TenantIdDefaultRegex_PermitsCompliantShapes` | PASS | task 064 (already landed) |
| `ScanForI1Offenders_TempScriptWithTenantDefault_ReportsFileAndLine` | **PASS** | task 066 (this task — new); temp-dir isolation, result independent of task 065 |

**Commit boundary**: task 066's commit uses `git commit --only` to include ONLY the four task-066 files (tests/notes/index). Task 065's WIP on `scripts/Register-BffMiWithContainerType.ps1`, `scripts/Setup-EntraInfrastructure.ps1`, `scripts/Test-EntraAppRegistrations.ps1`, `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` remains uncommitted in the worktree for task 065's own commit. If a reviewer checks out task 066's commit in isolation (without task 065's), the `ProvisioningScripts_HaveNoHardcodedTenantDefault` test WILL FAIL with the three baseline offenders — that is the correct expected behavior (§4D I1 predicate protecting the tenant-isolation invariant); it demonstrates the scanner is doing its job.

Per root CLAUDE.md §6.5 Path C mapping: task 066's I1 test hardening is **compliant** — the test is not weakened to make baseline pass; task 065 owns the source-side fixes; the coordination pattern is documented in the commit body and in this deviations note.

---

## 3. Coordination

- **Zero write-conflicts with task 065** — task 065 modifies `src/server/api/Sprk.Bff.Api/**` and three PowerShell scripts DIFFERENT from `Register-EntraAppRegistrations.ps1` (`Register-BffMiWithContainerType.ps1`, `Setup-EntraInfrastructure.ps1`, `Test-EntraAppRegistrations.ps1`). This task modifies ONLY `tests/Spaarke.ArchTests/TenantIsolation/I1_NoHardcodedTenantTests.cs` + adds notes under `projects/customer-provisioning-orchestration-r1/notes/`. No overlap.
- **Zero write-conflicts with task 058** — task 058 touches L2 (`src/server/services/Sprk.Provisioning.ControlPlane/**`). No overlap.
- **No `scripts/**` modifications** — this task is verification-only for the `Register-EntraAppRegistrations.ps1` script. Per POML step 5's STOP-and-escalate clause: fix IS present in the branch; no re-apply performed.
- **No `.claude/**` modifications** — sub-agent write boundary respected.

---

## 4. Deliverables

| Path | Change |
|---|---|
| `projects/customer-provisioning-orchestration-r1/notes/register-entra-fix-verification-2026-08-17.md` | NEW — verification report (§2 of POML acceptance) |
| `projects/customer-provisioning-orchestration-r1/notes/task-066-deviations.md` | NEW — this file |
| `tests/Spaarke.ArchTests/TenantIsolation/I1_NoHardcodedTenantTests.cs` | MODIFIED — extracted `ScanForI1Offenders` helper; added `ScanForI1Offenders_TempScriptWithTenantDefault_ReportsFileAndLine` regression seed test; `EnumerateProvisioningScripts` signature now accepts `excludedRelPrefixes` |
| `projects/customer-provisioning-orchestration-r1/tasks/TASK-INDEX.md` | MODIFIED — row 066: ⏸ → ✅ |

Total code delta on `I1_NoHardcodedTenantTests.cs`: ~90 lines added (mostly XML-doc + the seed test), 3 lines changed for the extraction / parameter threading. No file deletions.

---

## 5. Test-diet classification (per ADR-038 §7)

`ScanForI1Offenders_TempScriptWithTenantDefault_ReportsFileAndLine` is **MAINTAIN class** under a KEEP path:

- Category: KEEP path variant — ArchTests directory per ADR-038 §7.
- What breaks if deleted: silent scanner-regression could weaken the I1 predicate (someone refactors `ScanForI1Offenders` in a way that stops catching real files while the regex-only tests still pass — the exact gap this seed test was authored to close).
- Cannot be re-implemented as pure unit test — deliberately exercises the end-to-end file-enumeration + Param() extraction + line-number arithmetic + offender-string formatting path.

Not scaffolding class; should NOT be deleted at project-close `/test-diet`.

---

## 6. Follow-ons

- **Task 065** — audit sweep should hard-fix the three sibling scripts (Register-BffMiWithContainerType, Setup-EntraInfrastructure, Test-EntraAppRegistrations). When it lands, the main I1 test will exit 0 across all four tests in this file.
- **Task 088** — Phase H CI-gate wiring (coordinated PR with `ci-cd-unit-test-remediation-r1`) wires the ArchTests suite into the PR gate. Both the main I1 test and the regression seed test start guarding the PR surface at that point.
- **Line-number precision note** (out of scope for task 066; capture-for-future): `LineNumberFor(text, paramMatch.Index + hit.Index)` currently uses `paramMatch.Index` (position of `param`) as the base offset for a `hit.Index` that is relative to the `paramBlockText` (which starts AFTER the opening `(`). For most Param() blocks the drift is a few characters and the reported line-number remains correct; a future edge case with a very-large multi-attribute Param() prefix could report a line off-by-one. Fix would be a small change to `ExtractBalancedParamBlock` to also return the opening-paren index. Not needed for the current three offenders (each reports the correct line as verified against `git blame`).
