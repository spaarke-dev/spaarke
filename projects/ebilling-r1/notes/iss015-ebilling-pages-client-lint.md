# ISS-015 (#2743) + ISS-026 (#2877): the five eBilling code pages under client-lint

Task `iss015`. Branch `task/ebilling-iss015`, cut from `origin/work/ebilling-r1` at 72f2e54503.

## What changed

| Where | Change |
|---|---|
| `scripts/quality/client-lint/eslint.client.config.mjs` | `PACKAGES` gains `invoice-files`, `invoice-review`, `create-invoice-wizard`, `rule-review`, `record-procedures` (the same `{ id, dir }` shape as `reporting`). No new config, no per-page config, no rule changed. |
| `.github/workflows/client-lint.yml` | The five `src/solutions/<Page>/**` paths are added to the push `paths:` list and to the `paths-filter` list (the two lists stay identical), and the five `package-lock.json` files are added to the npm cache key. Nothing else: triggers, job and check names, runner, and steps are untouched. |
| `.github/workflows/ci-standalone-checks.yml` | `tests/scripts/Check-TaskStatusDrift.Tests.ps1` is added to the `Invoke-Pester -Path` list of the `ontology-script-tests` job (same job that already lists the other ebilling Pester files; Pester 5.7.1, already the job's pin, which is the version the file declares) plus one comment line. |
| five `src/solutions/<Page>/package.json` | `"lint"` now runs `node ../../../scripts/quality/client-lint/client-lint.mjs check --package <id>` (repointed, not removed; the same form in all five). The old `eslint src --ext .ts,.tsx` could not run: no eslint dependency, no config. `lint:fix` is left as is (it never worked either; out of scope, listed below). |
| `src/solutions/{InvoiceFiles,RecordProcedures,RuleReview}/package-lock.json` | The root `version` field was stale (2.4.0 against 3.1.0 in `package.json`); `client-lint install` rewrites it, and CI's "Lockfiles must be current" step would fail without the commit. One line each. |
| `docs/procedures/testing-and-code-quality.md` | The "Covered" line names the five pages. |
| `scripts/quality/client-lint/eslint-suppressions.json` | 95 inserted lines, 0 removed or changed lines, all under `src/solutions/{InvoiceReview,RecordProcedures,RuleReview}/...`. No other package's entry changed. Entries are alphabetical by path, so the block lands between the `src/client/...` entries and the `src/solutions/LegalWorkspace` entries; PRs #3071 and #3060 add entries for other paths (check at merge: both are expected to be separate hunks). The file keeps its trailing newline (ESLint's own rewrite drops it; restored so the diff has no end-of-file hunk). |

## Findings (found / fixed / ratcheted)

The shared config enables two rule families only: `@typescript-eslint/no-unnecessary-condition` (NUC) and the catch policy. It runs no `react-hooks` rules (they are no-op namespaces by design), so the "hooks-rule" errors named in ISS-015 cannot be found by this toolchain; that is a property of client-lint, not of these pages.

| Page (id) | Found | Fixed | Ratcheted | Rules |
|---|---|---|---|---|
| InvoiceFiles (`invoice-files`) | 2 | 2 | 0 | 2 catch policy |
| InvoiceReview (`invoice-review`) | 25 | 0 | 25 | 25 NUC |
| CreateInvoiceWizard (`create-invoice-wizard`) | 0 | 0 | 0 | |
| RuleReview (`rule-review`) | 2 | 0 | 2 | 2 NUC |
| RecordProcedures (`record-procedures`) | 10 | 0 | 10 | 10 NUC |
| **Total** | **39** | **2** | **37** | |

Fixed (InvoiceFiles, no behaviour change): both are deliberate swallows, so the fix is the policy's own mechanism, a reasoned `eslint-disable-next-line no-restricted-syntax -- <reason>` directly above the reported line.
- `src/main.tsx` `openDocument`: console-only catch around `openRecord`; best-effort navigation, the host owns the form.
- `src/services/fileActions.ts` `openTab`: empty catch around `tab.opener = null`; some hosts refuse the assignment (the existing comment already said so).

Ratcheted (37 NUC): every one is a defensive null/undefined or literal-union check that the declared types call redundant (for example `x !== null && x !== undefined` on a `number | null | undefined` field that the model types as `number | null`, `?.` on a value typed non-null, `step && step.kind === 'remove'` after a `find`). They guard data that comes from the BFF at run time, where the declared type can lag the wire. Removing them is a behaviour decision per site, not a safe local fix, and none of them is a hooks violation or provably dead code. Each stays in `eslint-suppressions.json` with its count; fixing a site and running `client-lint.mjs prune` shrinks the file.

## Results

- `client-lint.mjs controls`: 11 must-fire, 7 must-not-fire, 0 failures.
- `client-lint.mjs check` (all 14 packages): exit 0.
- Must-fail: an empty `catch {}` appended to `CreateInvoiceWizard/src/main.tsx` made `check --package create-invoice-wizard` exit 1 ("Empty catch", line 36); reverted, the same command exits 0. The file is back to its committed bytes.
- `npm run lint` in `src/solutions/RuleReview` runs client-lint for that page and exits 0.
- YAML: both workflow files parse (js-yaml); `client-lint.yml` has 14 push paths and the five new lines in the filter; `ci-standalone-checks.yml` job list unchanged (12 jobs), the Pester line includes the drift test.
- Pester 5.7.1, `tests/scripts/Check-TaskStatusDrift.Tests.ps1`, run locally on Windows: 28 passed, 0 failed.
- Page jest runs (`npx jest` in each page): InvoiceFiles 4 suites / 41 tests pass; InvoiceReview 18 / 653 pass; RecordProcedures 23 / 488 pass; RuleReview 8 of 9 suites pass (233 tests); CreateInvoiceWizard has no test script. The one RuleReview suite that does not run, `src/__tests__/assignmentPanelFirmPicker.test.ts`, fails to load with "Cannot find module '@spaarke/ui-components'": that package resolves to `Spaarke.UI.Components/dist/index.js`, and `dist` is not built in this worktree. It is environmental and not caused by this change (only RuleReview's `lint` script line changed there); building the shared lib's `dist` first would let it run.
- Build: `npm run build` in InvoiceFiles (the only page whose source changed, comment-only) succeeds. The other four pages changed only the `lint` script line, so their builds were not re-run.

## Known limits

- The drift test ran locally on Windows only; the CI job runs it on `ubuntu-latest` (the script and test use `Join-Path` and a child `pwsh`, no Windows-only calls found). If CI shows a path or encoding difference, the fix is in the test, not the workflow entry.
- `lint:fix` in the five packages still points at `eslint --fix` with no eslint installed. ESLint's `--fix` is not part of this ratchet; left alone (scope).
- 27 other `src/solutions/*/package.json` files carry the same `"lint": "eslint src ..."` script. Three of them (LegalWorkspace, Reporting, SpaarkeAi) are already under client-lint and were left as they were; the other 24 are not. `sprk_invoicespage` is the eBilling-adjacent one; none is in this task's list.
- `client-lint install` takes about 33 minutes cold on this machine (sequential `npm install` of 14 packages); CI time grows by the five pages' install time.
