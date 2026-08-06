# Task 013 — Delete the mammoth fallback + `docxToTipTapHtml` from the Compose path (FR-04 / F-2) — implementation notes

**Status**: implementation complete, build + client jest suite green. This note is written by the
executing subagent, which is NOT permitted to edit `tasks/TASK-INDEX.md` or `current-task.md` —
the orchestrator/human should flip task 013 to ✅ there.

## Re-grep (Step 1/3) — call sites found and deleted

`grep -rn "mammoth\|docxToTipTapHtml" src/client/shared/Spaarke.Compose.Components` before this task
confirmed exactly the two known branches the POML cited, plus their supporting export surface:

1. `ComposeEditor.tsx` — the `else { docxToTipTapHtml(...).then/.catch/.finally }` fallback branch
   inside the `docxBytes` mount effect (POML cited `:1667`/`:1718`; actual anchor after 010/011/012's
   edits was `:2033` — line-number drift only, same branch).
2. `docxBridge.ts` — the `docxToTipTapHtml` function + its `MammothConversionResult` return type.
3. `index.ts` — barrel re-exports of both (`export { docxToTipTapHtml, ... }` /
   `export type { MammothConversionResult, ... }`).

No unexpected consumer was found (escalation trigger did not fire). All three were deleted/updated:

- **`ComposeEditor.tsx`**: removed the `docxToTipTapHtml`/`stampParaIds` imports (both were used ONLY
  inside the deleted branch — `stampParaIds` is no longer called anywhere in this file since the
  server projection's HTML already carries `data-paraid`); deleted the entire async-import branch
  (`let cancelled = false; setIsImporting(true); docxToTipTapHtml(docxBytes)...`); replaced it with
  the projection-unavailable error-state branch (see reconciliation below).
- **`docxBridge.ts`**: deleted `docxToTipTapHtml` + `MammothConversionResult`; rewrote the file
  header to describe the module as export-side-only + paraId-carry-helpers-only post-task-013;
  `stampParaIds`/`captureParaIdSnapshot`/`buildBaselineParaIdMap`/`buildContentModel` (and their
  supporting types) are UNCHANGED and still exported — they are reader-agnostic and still consumed
  (`captureParaIdSnapshot`/`buildContentModel`/`buildBaselineParaIdMap` by `ComposeEditor.tsx`;
  `stampParaIds` by three test files that call it directly against a headless editor).
- **`index.ts`**: removed the `docxToTipTapHtml`/`MammothConversionResult` re-exports; updated the
  header comment describing the DOCX bridge licensing/architecture.

`mammoth` was **NOT** uninstalled — `package.json` (`"mammoth": "^1.8.0"`) is untouched, confirmed by
`git diff --stat -- '**/package.json'` showing no change to this package's manifest. `@spaarke/ui-
components` (SprkChat + Notepad) has its OWN, separate `mammoth` dependency in its own
`package.json`/`package-lock.json` — untouched, unaffected by this task (different package).

## Grep-proof (Step 3)

Post-deletion, `grep -rn "mammoth\|docxToTipTapHtml" src/client/shared/Spaarke.Compose.Components`
returns **zero functional call sites** — confirmed three ways:

1. `grep "from 'mammoth'\|import('mammoth')"` → **0 matches** anywhere in `src/`.
2. `grep -l "docxToTipTapHtml" src | grep -v '\.test\.'` → only `index.ts`, `docxBridge.ts`,
   `ComposeEditor.tsx` (all three are COMMENT-only references documenting the deletion —
   `docxToTipTapHtml(` as an actual invocation does not appear), plus `importedComments.ts` /
   `importedRevisions.ts` (pre-existing historical doc-comments explaining why revision/comment
   anchoring logic works the way it does — these two files were NOT in the POML's target-file list
   and were left untouched; the comment still describes a still-true fact: whichever reader supplies
   the HTML flattens native `w:ins`/`w:del`/`w:comment` into plain prose, which is why the
   anchor-materialization logic exists).
3. Raw `grep -c "mammoth"` across `src/` + `README.md` (excluding `package-lock.json`) returns 67 —
   ALL of these are either (a) doc comments I added/updated in `ComposeEditor.tsx`, `docxBridge.ts`,
   `ComposeWorkspace.tsx`, `ComposeWorkspace.types.ts` explicitly narrating "task 013 deleted the
   mammoth reader" (the expected, intentional trail a deletion of this kind leaves), (b)
   `package.json`'s retained dependency line, or (c) pre-existing comments in files outside this
   task's two-file scope (README.md, `importedRevisions.ts`, `importedComments.ts`,
   `paraIdExtension.ts`, `useComposeDocumentStyles.ts`, and several `.test.tsx` files whose test
   TITLES/comments reference "mammoth" as the historical name of the behavior being guarded
   against). None are a live `import`, `require`, or invocation of a mammoth-backed reader.

`mammoth` still resolves for SprkChat/Notepad: `grep -l "mammoth" src/client/shared/
Spaarke.UI.Components` still matches `useChatFileAttachment.ts` + its two test files + its own
`package.json` — untouched by this task.

## Reconciliation (Step 4b) — the null-projection error state

Per the task brief's CRITICAL RECONCILIATION: once the mammoth fallback is gone, a `projection: null`
docx mount has NO reader. Implemented exactly as directed — **not** a second reader, an explicit
error/unavailable state:

- Added a new `projectionUnavailable: { fileName?: string } | null` state to `ComposeEditor.tsx`,
  parallel to the existing `referenceOnly` state (same architectural shape/lifecycle — set/cleared at
  the same points, mutually exclusive).
- In the `docxBytes` mount effect: after the `isEditableDocx` gate passes (this IS a valid docx) and
  `projection` is falsy, the editor now sets `<p></p>` content, resets the op-log, reports
  `onDirtyChange(false)`, and sets `projectionUnavailable({ fileName })` — **no** async import is
  attempted, **no** second client-side parser runs.
- New render branch (`data-testid="compose-projection-unavailable"`): an `ErrorCircle24Regular` icon
  (`tokens.colorStatusDangerForeground1` — semantic token, ADR-021 dark-mode-correct) + "Couldn't
  prepare '{fileName}' for editing" headline + a detail line pointing the user to retry / noting the
  file is still available to the Assistant for reference. Reuses the existing `referenceOnly`/
  `referenceOnlyDetail` layout classes (calm, centered, semantic-token-only) — only the icon class
  and copy differ, since this is a genuinely different (failure, not by-design) state.
- The document remains **available to the Assistant for reference** even in this state — the mount
  still completes (React component instance mounts, `registerActiveDocumentRef` still fires from
  `ComposeWorkspace.tsx`'s Browse door) — only the EDITABLE ProseMirror surface is withheld. This
  mirrors the existing `referenceOnly` pattern exactly (a non-docx file also still "mounts" but shows
  a calm non-editable panel) and preserves the "file available to Assistant" capability the task
  brief's suggested copy implies.

### Updated task 011's browse best-effort-null fallback

Per the brief: "Update task 011's browse best-effort-null fallback accordingly (browse now
hard-requires the projection round-trip; on failure show the error state rather than mounting an
unreadable editor)." Concretely:

- **No functional change** to `ComposeWorkspace.tsx`'s Browse door dispatch logic — the
  `POST /api/compose/project` call stays best-effort AT THE NETWORK LAYER (an unconfigured
  `bffBaseUrl` or a thrown fetch still falls through to `projection: null`, and `mountTransient` still
  dispatches unconditionally). This is intentional, not a shortcut: blocking the dispatch entirely
  would also have blocked `registerActiveDocumentRef` (the Assistant-reference registration), losing
  a capability the error-state's own copy promises ("still available to the Assistant for
  reference").
- **Updated ~10 comments** across `ComposeWorkspace.tsx` and `ComposeWorkspace.types.ts` that
  described the old "falls back to the client mammoth convert" behavior — all now describe the new
  contract ("(task 013, F-2) the editor renders an explicit error/unavailable state"). This is the
  actual reconciliation: the WORDS describing what happens on a null projection changed because what
  ACTUALLY happens changed (mediated entirely inside `ComposeEditor.tsx`'s render, not in
  `ComposeWorkspace.tsx`'s dispatch).
- Verified this reconciliation is genuinely load-bearing, not cosmetic, via a NEW test:
  `ComposeWorkspace.browse.test.tsx` — "BFF unreachable (fetch throws): the Browse mount still
  proceeds with `projection: null` (never blocked)" — proves the mount-still-proceeds half of the
  contract at the `ComposeWorkspace` boundary; the render-the-error-state half is proven at the
  `ComposeEditor` unit level (below).

No offline/zero-BFF browse-render requirement was found that this reconciliation breaks — Browse's
zero-BFF-dependency contract was always about the MOUNT (tab navigation, Assistant registration)
succeeding without a server round-trip, never about EDITABLE render fidelity without one (mammoth was
already a lossy fallback, not a peer-quality reader — see design.md T-2 / F-2). No escalation fired.

## Tests added (Step 5 — ui-tests)

**`ComposeEditor.projection.test.tsx`** (extended — the canonical projection-mount contract suite):
- `null projection (BFF unreachable/failed round-trip): renders the error/unavailable state — NOT
  blank, NOT mammoth` — asserts `compose-projection-unavailable` renders, `role="textbox"` and
  `compose-reference-only` are both ABSENT, and the regression-guard `docxToTipTapHtml` mock is never
  called.
- `null projection: mounting a subsequent VALID projection clears the error/unavailable state` —
  proves the state is not sticky across a re-mount.
- `null projection: renders theme-correct under dark mode (ADR-021)` — renders under `webDarkTheme`
  and asserts the panel renders (semantic-token-only styling makes this a structural, not
  per-property, guarantee).

**`ComposeWorkspace.browse.test.tsx`** (extended): the BFF-unreachable regression guard described
above.

**Fixed (not new — pre-existing tests broken by the deletion, restored to green)**:
Four existing test files rendered the REAL `ComposeEditor` with real `docxBytes` and NO `projection`
prop, relying on the (now-deleted) mammoth branch to produce an editable surface:
- `ComposeEditor.referenceOnly.test.tsx` — the "DOCX buffer → editable editor" case now supplies a
  `projection` prop; the assertion flipped from "`docxToTipTapHtml` called once" to "never called"
  (F-2 regression guard, strengthened).
- `ComposeEditor.dirtyOnMount.test.tsx` — all three dirty-reporting fixtures now supply a
  `SUCCESS_PROJECTION`.
- `ComposeEditor.paneToggleCrash.test.tsx` — the BubbleMenu-crash-guard fixture now supplies a
  `PANE_TOGGLE_PROJECTION`.
- `ComposeEditor.advisoryComments.test.tsx` — now supplies an `ADVISORY_COMMENTS_PROJECTION` with the
  SAME body text the mocked mammoth bridge used to return, so the pre-existing (task-031-owned,
  unrelated) "`placed` expected 1, received 2" defect still reproduces identically — confirmed via
  test run (see Verification).
- `ComposeWorkspace.redline-from-ledger.test.tsx` — the mocked `/api/compose/documents/` (Load)
  response gained a `projection` field (this file exercises the REAL `ComposeWorkspace` + REAL
  `ComposeEditor`, unmocked) — without it all three tests in this file timed out waiting for
  `role="textbox"` that never appeared.

None of these five fixes change PRODUCTION code — only test fixtures, so they are within the
"regression-repair" scope of finishing this deletion cleanly, not a scope expansion.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors** (23 pre-existing warnings, unchanged —
  no server file touched by this task).
- TypeScript: `npx tsc --noEmit` in `Spaarke.Compose.Components` → **8 errors, byte-identical set**
  to a stash/pop A-B baseline (confirmed via `diff`; only line-number shifts from added lines). All 8
  are the SAME pre-existing `@spaarke/ai-widgets` workspace-package-resolution gap (not built in this
  standalone environment) plus 3 pre-existing implicit-any errors in untouched `ComposeWorkspace.tsx`
  code — zero NEW errors.
- Client jest: `npx jest` (full package suite) → **635 passed, 1 failed, 636 total** (up from task
  012's reported 631/1/632 — the delta is exactly the 4 new tests this task added: 3 in
  `ComposeEditor.projection.test.tsx`, 1 in `ComposeWorkspace.browse.test.tsx`). The 1 failure is the
  **pre-existing, unrelated** `ComposeEditor.advisoryComments.test.tsx` defect ("placed" expected 1,
  received 2 — task 031's target per task 012's own notes) — confirmed NOT newly introduced or
  altered by this task (same failure mode, now reached via the projection branch instead of mammoth,
  proving the defect is in the shared `resolveTargetSpans`/comment-thread logic, not the reader).
- Grep proof: see "Grep-proof (Step 3)" above — zero functional `mammoth`/`docxToTipTapHtml` call
  sites in `Spaarke.Compose.Components`; `mammoth` still resolves for `@spaarke/ui-components`
  (SprkChat/Notepad), untouched.
- `mammoth` still installed: `package.json` line `"mammoth": "^1.8.0"` unchanged (`git diff` on this
  package's `package.json` is empty).

## Placement Justification (root CLAUDE.md §10 / §11)

Client-only task — no BFF/server surface touched (`git diff --stat` on `src/server/` is empty for
this task). No new component/service/abstraction was introduced: the `projectionUnavailable` state
is a sibling of the EXISTING `referenceOnly` state (same shape, same lifecycle, same render pattern)
— an extension of an established pattern, not a new one (root CLAUDE.md §11). Publish-size impact:
**0 MB** (no `.cs`/`.csproj` file touched).

## Files changed

- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` (mammoth branch
  deleted; projection-unavailable error state added)
- `src/client/shared/Spaarke.Compose.Components/src/utils/docxBridge.ts` (`docxToTipTapHtml` +
  `MammothConversionResult` deleted)
- `src/client/shared/Spaarke.Compose.Components/src/index.ts` (barrel exports updated)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` (stale
  "falls back to mammoth" comments updated to describe the new error-state contract; no functional
  change)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.types.ts` (same —
  comment-only)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.projection.test.tsx`
  (extended: null-projection error state, dark-mode, clears-on-retry)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.browse.test.tsx`
  (extended: BFF-unreachable regression guard)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.referenceOnly.test.tsx`,
  `ComposeEditor.dirtyOnMount.test.tsx`, `ComposeEditor.paneToggleCrash.test.tsx`,
  `ComposeEditor.advisoryComments.test.tsx`, `ComposeWorkspace.redline-from-ledger.test.tsx` (fixture
  repair: added `projection` prop/mocked-response field so these pre-existing suites still exercise
  an editable mount post-deletion — no assertion intent changed except the referenceOnly suite's
  `docxToTipTapHtml`-called-once → never-called flip, which is the intended F-2 strengthening)

No `.cs`/`.csproj` file was modified. No `package.json`/`package-lock.json` was modified (mammoth
retained per constraint).

## Deviation from the POML for the orchestrator/reviewer

The POML's `<outputs>` section named only `ComposeEditor.tsx` and `docxBridge.ts`. Two categories of
additional files were touched, both load-bearing rather than scope creep:

1. `ComposeWorkspace.tsx` / `ComposeWorkspace.types.ts` — comment-only updates, required by the
   POML's own Step 4b reconciliation instruction ("Update task 011's browse best-effort-null fallback
   accordingly").
2. Five test files — fixture repairs, not scope creep. The POML's acceptance criteria explicitly
   require "all three entry paths render via the server projection with no console errors" and a
   green client build; leaving these five broken would have shipped a red client test suite for a
   change explicitly scoped to be verifiable via `ui-tests`. All five failures were 100%
   attributable to the deletion (missing `projection` prop on a real, unmocked `ComposeEditor` mount)
   — none pointed to a genuine behavioral regression once fixed.
