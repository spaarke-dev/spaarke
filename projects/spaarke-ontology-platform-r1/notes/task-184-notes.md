# Task 184 notes - retire EmailStep and the dead wizard steps; wizard lookup/email pattern guard

Branch `task/ontology-184-retire-emailstep-and-guard` from origin/master `ab75d18874`. Rigor FULL, Sonnet 5.5 @ high, directional steps.

## Importer audit (git grep over the whole repo: *.ts *.tsx *.js *.json *.md, Solution bundles and projects/ excluded)

Run 2026-11-14 on `ab75d18874`. "Importer" = any file outside the candidate's own folder that names the file or its exported symbol. Every candidate had zero, so none is kept; the escalation trigger (importer outside the shared library) did not fire.

| Retired file (all under `src/client/shared/Spaarke.UI.Components/src/components/`) | Last importer / reference | Result |
|---|---|---|
| `EmailStep/index.ts` | none; not re-exported by `components/index.ts` or `src/index.ts`. Last consumer was `DocumentEmailWizard` until task 183 (#2824) | retired |
| `EmailStep/SendEmailStep.tsx` | only `EmailStep/index.ts` | retired |
| `EmailStep/LookupField.tsx` | only `EmailStep/SendEmailStep.tsx` and `EmailStep/index.ts` | retired |
| `EmailStep/emailHelpers.ts` (`extractEmailFromUserName`) | only `EmailStep/SendEmailStep.tsx` and `EmailStep/index.ts` | retired |
| `CreateRecordWizard/steps/AssignResourcesStep.tsx` | only the `CreateRecordWizard/index.ts` barrel (value + `IAssignResourcesStepProps` type); no JSX consumer, no import of the name from `@spaarke/ui-components` anywhere in `src/` | retired, barrel lines removed |
| `CreateMatterWizard/AssignResourcesStep.tsx` | none (not in the barrel) | retired |
| `CreateMatterWizard/AssignCounselStep.tsx` | only the `CreateMatterWizard/index.ts` barrel (value + `IAssignCounselStepProps` type); no consumer | retired, barrel lines removed |
| `CreateMatterWizard/DraftSummaryStep.tsx` | none (not in the barrel) | retired |
| `CreateMatterWizard/RecipientField.tsx` | only `CreateMatterWizard/DraftSummaryStep.tsx` (same task). The `RecipientField` everyone else imports is `EmailComposer/subcomponents/RecipientField` | retired |
| `CreateMatterWizard/LookupField.tsx` (not on the POML list) | only `CreateMatterWizard/AssignResourcesStep.tsx`, retired in this task; it wrapped the shared `LookupField` as `SharedLookupField`, so the guard counts it as an inline-lookup violation | retired because this task orphaned it (a third rendering of the lookup pattern, owner directive 2026-11-12) |

**Deviation from "move, never delete":** the ten files were removed with `git rm`, not moved. Reason: the repo has no archive folder for retired source code (`docs/archive`, `scripts/_archive`, `.claude/archive` and the project-notes folders hold docs, scripts and prompts), and every earlier wizard retirement (tasks 021-024, `b026daf489`, `1dc190f427`) relied on git history. The importer audit above is the safeguard; all ten files are recoverable from `ab75d18874` (`git show ab75d18874:<path>`).

Fix round 2: `IRecipientItem` in `CreateRecordWizard/types.ts` had no user left anywhere in `src/` (the retired `RecipientField` was its only consumer; the one remaining mention is a history comment in `WizardFollowOns/index.ts`), so it was removed with its section header. The `SearchCallback` section comment no longer names the retired steps.

## Guard

`src/client/shared/Spaarke.UI.Components/src/__tests__/wizardLookupPatternGuard.test.ts`. Model: `src/utils/__tests__/pcfBundleReactRuntime.guard.test.ts` and `xrmCapabilityUsage.guard.test.ts` (the POML's canonical reference `noAccessEntryForm.test.ts` is a jsdom test of a web resource, not a source scan). Pure scanner functions, a repository test, controls, and a ratchet.

Rules (as of fix round 2), over non-test sources in `components/Create*Wizard/` and `components/WizardFollowOns/`:
1. `inline-lookup` - uses `LookupField` as a VALUE outside an import declaration: JSX (`<LookupField`, `<ns.LookupField`), `React.createElement(LookupField`, `const L = LookupField`, `export { LookupField as X } from`, and every name it is imported as (`import { LookupField as P }` then `<P`, `createElement(P`, `export { P }`). `DataverseLookupField` never matches; an import never used is not a rendering.
2. `email-step` - references a module whose path ends in `EmailStep`, only after `from`, `import(`, `import '...'`, `require(` or `jest.mock`-family calls. A bare `'EmailStep'` string (a step id) and `DocumentEmailStep` do not match.
3. `send-email-step` (whole package) - exactly one module defines or exports `SendEmailStep`: inline `export const|function|class`, `export { X as SendEmailStep }` (with or without `from`), or a local declaration exported later by an export list or `export default`. A plain re-export of the same binding does not count.

Scanning: a string-aware walk (`walkCode`): comments are dropped, but a `//` or `/*` inside a quote string, a template literal's text or a JSX attribute (`accept="image/*"`) starts no comment; `${...}` in a template is walked as code; for rule 1 string text is blanked so `'<LookupField>'` in prose never counts. Known limits (K1/K4, a scan not a parser): a regex literal containing a quote or `//`, and an apostrophe in JSX text, can open a bogus string; a quote string ends at the line end, so the damage stops at that line.

Ratchet: `BASELINE` is empty. Tasks 179-183 converted every field (reference tables too, owner D-219), and this task retired the leftovers, so nothing is kept inline and no reference-table entry exists. The ratchet still works: a violation not listed fails, a listed entry that no longer violates fails, each entry needs a reason; all four are exercised by controls.

Controls (66 tests in the file: 6 run the repository scan, 60 are controls). Must-fire: seeded `<LookupField` under Create*Wizard and WizardFollowOns; alias import; namespace import; the review's five probes (createElement, an alias const, a re-exported alias, a `//` inside a string before the tag on the same line, a `/*` inside a string that would swallow code up to a later `*/`), plus an imported alias exported on, an alias passed as a value, a `$` in an alias name, an apostrophe in JSX text, code inside a template expression; eight EmailStep reference shapes (import, deep import, export-from, dynamic import, `jest.mock`, `require`, side-effect import, type import); two modules defining `SendEmailStep` and five second-export shapes (alias export, alias re-export, function/const exported later through a list, function exported later by default). Must-not-fire: `DataverseLookupField` (plain, aliased, namespace); the word in line and block comments, in strings, in template text and in a JSX attribute; an unused import; code after a `//` or `/*` held inside a string; a bare `'EmailStep'` step id; composer / `DocumentEmailStep` / `SendEmailFollowOnStep` imports; inline `LookupField` outside the wizard folders; test files; a single `SendEmailStep` definition, its re-export, a declared-but-unexported one. Also: the comment walk (5), the ratchet (4) and the scope table (9).

Fails-before proof: the guard run on `ab75d18874` + the new test file (nothing retired) failed 3 of 35: `inline-lookup` on `CreateMatterWizard/AssignResourcesStep.tsx`, `CreateMatterWizard/LookupField.tsx`, `CreateRecordWizard/steps/AssignResourcesStep.tsx`; `send-email-step` on both `EmailComposer/wrappers/SendEmailStep.tsx` and `EmailStep/SendEmailStep.tsx`; "old EmailStep folder is gone". After the retirement: 35/35 pass.

Wiring: a new step in the existing `xrm-capability-guard` job of `.github/workflows/ci-tier1-blocking.yml` (blocking, runs on any `src/client/**` or `src/solutions/**` change; same install, ~2 s more), next to task 183's `pcfBundleReactRuntime` step. No new job. The job's header says its CLAUDE.md 6.5 path-A exception is scoped to that one job; the new step stays inside it, as 183's did. Flagged for the owner in the report.

**Enforced by: `src/client/shared/Spaarke.UI.Components/src/__tests__/wizardLookupPatternGuard.test.ts`** (CI: `xrm-capability-guard` job, "Wizard lookup / email pattern guard" step; also the package's jest suite).

## `.claude/` edit for the main session

`.claude/patterns/ui/wizard-side-pane-lookup-and-email-compose.md`, last bullet of "Key Rules", currently reads "Do **not** import the old `components/EmailStep/` in new code; it is being retired (ontology task 184, with a source-scan guard)." Replace with:

> The old `components/EmailStep/` and the unused `AssignResourcesStep` / `AssignCounselStep` / `DraftSummaryStep` / `RecipientField` / inline `LookupField` wrapper were retired by ontology task 184. Enforced by `src/client/shared/Spaarke.UI.Components/src/__tests__/wizardLookupPatternGuard.test.ts` (fails on `<LookupField` or an `EmailStep` import under `components/Create*Wizard/` or `components/WizardFollowOns/`, and on a second `SendEmailStep` export). Do not add a baseline entry for a new step; use `DataverseLookupField` or `EmailComposer`.

## Fix round 2 (coordinator request after the independent review; no F-class findings)

1. Rule 1 rewritten as above (string-aware walk; value uses). 2. Rule 3 covers `export { X as SendEmailStep }` and a function exported later through a list or `default`. 3. Rule 2 matches module references only. 4. `CreateRecordWizard/types.ts`: stale "Draft Summary / AssignResources" comments fixed, unused `IRecipientItem` removed. 5. `CreateMatterWizard/matterService.ts`: the unreachable `else if (uploadedFiles.length > 0 && !this._containerId)` "File upload skipped -- no SPE container" branch removed with a note (since task 076 the upload does not depend on `_containerId`; this also clears the ESLint `no-dupe-else-if` error that was listed below as out of scope). 6. The deviation from "move, never delete" is recorded above.

## Other edits (comments only, made stale by the retirement)

- `CreateMatterWizard/matterService.ts` lines 43, 460, 577: comments named `AssignCounselStep` / `DraftSummaryStep` as consumers.
- `WizardFollowOns/index.ts` header: said the draft-summary step "is its own `CreateMatterWizard/DraftSummaryStep.tsx`".
- Barrels `CreateRecordWizard/index.ts` and `CreateMatterWizard/index.ts`: export lines removed, retirement noted.

## Results

- Full shared-library jest (`--maxWorkers=4`), before: 407 suites / 6608 tests, 1 failed (`AccessGrantModal.notesBell.test.tsx`, passes 11/11 alone; load flake, unrelated). After: 408 suites / 6643 tests, 0 failed (+1 suite, +35 tests = the guard; the guard later grew to 37 and, in fix round 2, to 66; fix round 2 re-ran the guard plus the CreateMatterWizard, CreateRecordWizard and WizardFollowOns suites: 24 suites / 193 tests, 0 failed).
- `npx tsc --noEmit -p .` (Spaarke.UI.Components): exit 0, before and after.
- ESLint on the guard, the barrels, `types.ts` and `matterService.ts`: clean (the pre-existing `no-dupe-else-if` in `matterService.ts` went with the unreachable branch removed in fix round 2). tsc exit 0. client-lint ui-components: ok.
- Prettier: clean on all touched files.
- client-lint (`check`, all 10 packages, then `--package ui-components` again on the final tree): ok, no new violation, no unpruned suppression; none of the retired files had a recorded suppression.
- UI test (POML `<ui-tests>`, "Wizards still open after the retirement"): not run; it waits on deploy task 185. No component changed, only dead files and barrels, and every wizard is covered by its jest suite, which passes.

## Out-of-scope defects

1. (Fixed in fix round 2: the unreachable "File upload skipped" branch in `CreateMatterWizard/matterService.ts`.)
2. `CreateMatterWizard/matterService.ts` `streamAiDraftSummary`: exported through the `CreateMatterWizard` barrel, zero consumers in the repo (dead code candidate; `fetchAiDraftSummary` is used by `CreateRecordStep`).

rework: fix-round-2 task 184
