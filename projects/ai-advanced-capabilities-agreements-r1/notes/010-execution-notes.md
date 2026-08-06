# Task 010 — Execution Notes: rename `ndaClauseLocation` → `clauseLocation` (+ summary-panel naming)

> Rigor: FULL · Model tier: sonnet @ medium · Step mode: prescriptive · Status: complete

## Step 0 — importer grep (blast-radius record)

Repo-wide grep for `ndaClauseLocation` and `NdaReviewSummaryPanel` BEFORE any rename.

**`ndaClauseLocation` importers/references (17 files found; only 4 are code):**
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ndaClauseLocation.ts` (the file itself)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ndaClauseLocation.test.ts` (sibling test)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` (imports `deriveClauseLocationLabel`)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeCommentGutter.tsx` (imports `deriveClauseLocationLabel`)
- Remaining 13 hits are docs/notes/specs/POML history (`projects/**`, `docs/**`) — excluded per acceptance criterion.

**`NdaReviewSummaryPanel` importers/references (33 files found; only 7 are code):**
- `src/client/shared/Spaarke.Compose.Components/src/widgets/NdaReviewSummaryPanel.tsx` (the file itself)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/NdaReviewSummaryPanel.test.tsx` (sibling test)
- `src/client/shared/Spaarke.Compose.Components/src/index.ts` (barrel export)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` (imports `NdaReviewFindingSummary` type only)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` (imports component + `NDA_REVIEW_DISCLAIMER_TEXT` + type; renders JSX)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeCommentGutter.tsx` (imports `riskBadgeColor`, `formatClauseLocation`; comment references)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeCommentGutter.test.tsx` (comment reference only, no import)
- **2 hits OUTSIDE Compose.Components — verified COMMENT-ONLY, zero imports:**
  - `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts:909` — JSDoc mention "(see `NdaReviewSummaryPanel.tsx`)"
  - `src/solutions/SpaarkeAi/src/components/conversation/useNdaReviewAdvisoryCommentsBridge.ts:145` — comment mention
  - Confirmed via targeted grep of `src/solutions/LegalWorkspace/` + `src/solutions/SpaarkeAi/` for `NdaReviewSummaryPanel|NdaReviewFindingSummary|NdaSummarySort`: **zero actual code imports** outside Compose.Components.
- Remaining hits are docs/notes/specs/POML history.

**Escalation check (task trigger): "importers outside Compose.Components/SpaarkeAi that a re-export alias cannot cover (e.g., string-keyed registry references)."** Result: **no escalation** — the only 2 out-of-package hits are comment prose (not imports, not string-keyed registry lookups); they resolve fine regardless of the rename since they never reference the module path. Proceeded with the rename per plan.

## Scope decision (component + "types renamed")

Renamed exactly: the component `NdaReviewSummaryPanel` → `AgreementReviewSummaryPanel`, and its Props type
`NdaReviewSummaryPanelProps` → `AgreementReviewSummaryPanelProps` (the two symbols the POML's relevant-files/steps
name explicitly). Left unchanged (out of scope for this pure rename, and not covered by the acceptance criteria):
`NdaReviewFindingSummary` (data-contract type mirroring the NDA-REVIEW Action's schema field names — a Phase-2
generalization concern, tasks 020-023, not this task), `NdaSummarySort`, `NDA_REVIEW_DISCLAIMER_TEXT`, and all
runtime string literals (`data-testid="nda-review-summary-*"`, `aria-label="NDA review summary"`, disclaimer body
text, etc.) — changing those would be a behavior/contract change, not a rename.

## Step 1 — `ndaClauseLocation.ts` → `clauseLocation.ts`

`git mv` both the source file and its `.test.ts` sibling. Fixed imports in the 2 known importers
(`ComposeCommentGutter.tsx`, `ComposeEditor.tsx`) plus the file's own import of `formatClauseLocation`. Per
constraint, generalized "NDA" wording in the moved file's comments to "agreement"/"advisory-review" phrasing (2
spots: the file-header description of the `sectionRef` source model, and a body comment in the test file). Left
the project-provenance reference `(ai-advanced-capabilities-nda-r1, UAT round-5 ...)` untouched — historical fact,
not a description of NDA-specific behavior.

## Step 2 — `NdaReviewSummaryPanel.tsx` → `AgreementReviewSummaryPanel.tsx`

`git mv` the component file and its `.test.tsx` sibling. Renamed the function, the `Props` interface, `.displayName`,
and the default export. Added two deprecated re-export aliases at the bottom of the file (component + Props type),
each with an `@deprecated` JSDoc pointing to the new name, per ADR-012 export-stability + the POML's explicit
mandate ("the alias is mandatory" — NdaReviewSummaryPanel is on the hub's KEEP list). Updated
`src/client/shared/Spaarke.Compose.Components/src/index.ts` to export both the new canonical names and the
deprecated aliases (component + Props type) from the renamed file path. Updated the 3 consumer files
(`ComposeCommentGutter.tsx`, `ComposeEditor.tsx`, `ComposeWorkspace.tsx`) to import from the new path/name, and
updated their comment-only mentions of the old identifier/filename for accuracy. Left the 2 out-of-package
comment-only mentions (`Spaarke.AI.Widgets/PaneEventTypes.ts`, `SpaarkeAi/useNdaReviewAdvisoryCommentsBridge.ts`)
untouched — out of this task's file scope (Compose.Components + the two named consumer files only), zero
functional impact since they never import the module.

## Step 3 — build + test

**Environment note**: `node_modules` did not exist yet in this worktree for `Spaarke.Compose.Components` or its
`file:`-linked sibling packages (`Spaarke.Auth`, `Spaarke.AI.Widgets`, `Spaarke.DocumentOperations`,
`Spaarke.SdapClient`) or `SpaarkeAi`. Ran `npm install --legacy-peer-deps --no-audit --no-fund` in each (never
`npm ci`, per CLAUDE.md §12) and `npm run build` in the 4 sibling packages first (all built clean — 0 errors) so
their `dist/*.d.ts` would resolve for the Compose.Components + SpaarkeAi builds. This was a one-time environment
bootstrap, not part of the rename diff.

- **`Spaarke.Compose.Components` build** (`npm run build` = `tsc`): **0 errors.**
- **Touched-suite tests** (`clauseLocation.test.ts`, `AgreementReviewSummaryPanel.test.tsx`,
  `ComposeCommentGutter.test.tsx`): **3 suites / 75 tests — all pass**, assertions byte-identical to pre-rename
  (only import paths + the rendered component's identifier changed).
- **Broader regression check** (`ComposeEditor.*.test.tsx` + `ComposeWorkspace.*.test.tsx`, since those 2 files'
  imports were touched): 30 suites, 189 tests → 25 pass / 5 fail (6 failing tests). **Verified via a controlled
  `git stash` isolation** (stashed all Compose.Components changes, reran the exact same failing suites against the
  ORIGINAL pre-rename code, then `git stash pop` to restore) that **all 6 failures are pre-existing and unrelated
  to this rename**:
  - `ComposeEditor.advisoryComments.test.tsx` — the documented **DEF-01** bug (`placed=2` vs expected `1`); per
    `notes/HANDOFF-from-compose-fidelity-r4.5.md` ITEM 1, "Pre-existing on master... present identically
    before/after" — explicitly task 012's scope, not this task's.
  - `ComposeWorkspace.search.test.tsx`, `ComposeWorkspace.imports.test.tsx`,
    `ComposeWorkspace.bornInEditorSave.test.tsx`, `ComposeWorkspace.saveOpLogPreservation.test.tsx` (5 tests) —
    reproduced byte-identically on the stashed (pre-rename) code; unrelated to the renamed symbols (their
    `jest.mock('./ComposeEditor', ...)` / `@spaarke/*` mocks are untouched by this diff).
- **`SpaarkeAi` (downstream consumer)**: confirmed **zero direct imports** of the renamed symbols (only 2
  comment-only mentions, see Step 0). Built anyway per task instruction: `npm run typecheck`
  (`tsc-surface-gate.mjs`) → **Surface-owned: 0** errors (74 pre-existing shared-lib errors are explicitly deferred
  to "Phase B" by the gate's own convention, unrelated to this change). `npm run build` (vite + ribbon bundles) →
  **succeeded**, `dist/spaarkeai.html` + 4 ribbon bundles emitted.

## Acceptance criteria

| Criterion | Result |
|---|---|
| `grep ndaClauseLocation` → zero hits repo-wide (excl. docs/notes) | **PASS** — zero hits in `src/**/*.ts(x)` |
| Touched tests pass UNCHANGED; lib + SpaarkeAi build green | **PASS** — 75/75 touched-suite tests pass; both packages build with 0 own-code errors |
| `NdaReviewSummaryPanel` alias resolves — no consumer breaks | **PASS** — deprecated const + type alias exported from both the widget file and the barrel `index.ts` |
| Negative: no logic-line changes | **PASS** — full diff reviewed line-by-line; every hunk is an identifier rename, comment text, import-path string, filename, `describe()`/JSX-tag rename, or an additive `@deprecated` alias export |

## Deviations / escalations

None. No ADR conflict. No scope creep — `NdaReviewFindingSummary`/`NdaSummarySort`/`NDA_REVIEW_DISCLAIMER_TEXT`
and all runtime string literals were deliberately left untouched (see "Scope decision" above).

## Files touched

- `src/client/shared/Spaarke.Compose.Components/src/widgets/ndaClauseLocation.ts` → `clauseLocation.ts` (renamed + comment generalization)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ndaClauseLocation.test.ts` → `clauseLocation.test.ts` (renamed + import fix + 1 comment)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/NdaReviewSummaryPanel.tsx` → `AgreementReviewSummaryPanel.tsx` (renamed component + Props type + displayName + default export; added 2 deprecated aliases)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/NdaReviewSummaryPanel.test.tsx` → `AgreementReviewSummaryPanel.test.tsx` (renamed + import/JSX/describe updates)
- `src/client/shared/Spaarke.Compose.Components/src/index.ts` (barrel: new names + deprecated aliases)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeCommentGutter.tsx` (import paths + comment refs)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeCommentGutter.test.tsx` (1 comment ref)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` (import paths + JSX tag + comment refs)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` (import path + comment refs)

Not touched (in scope per grep but out of file-scope / comment-only, documented above):
`src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts`,
`src/solutions/SpaarkeAi/src/components/conversation/useNdaReviewAdvisoryCommentsBridge.ts`.
