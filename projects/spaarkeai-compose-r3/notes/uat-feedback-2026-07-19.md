# spaarkeai-compose-r3 — UAT feedback (2026-07-19, spaarkedev1)

> First browser UAT after the full R3 deploy (BFF `spaarke-bff-dev` + client `sprk_spaarkeai`).
> Context: Compose opened on an **AI-drafted matter-engagement letter** ("Test New Matter via Workspace"), born-in-editor via the DocumentComposeLaunch ribbon / workspace tab. Screenshot shows the letter rendered on the left; a second "Compose" tab on the right showed **"Compose failed to load"**.

## Bugs (deployed R3 regressions — fix next session)

### P1 — `insertBefore` DOM crash on mount / clicking A (styles) or Comments → "Compose failed to load"
- **Symptom:** clicking the **A** (styles-pane FAB, task 043) or **Comments** (comment-thread FAB, task 044) throws, and the editor panel shows **"Compose failed to load — Failed to execute 'insertBefore' on 'Node': The node before which the new node is to be inserted is not a child of this node."** with a Retry button. Console: `[AppInsightsService] trackException('Failed to execute 'insertBefore' …') called before initialize() — dropped.`
- **Leading hypothesis:** this is almost certainly the **now-LIVE imported-marks / comment-anchor application** exposed by the client-wire fix (`8f2cec4a6`). Before that fix `importedRevisions`/`importedComments` were `undefined` → `applyImportedRevisions` / `applyImportedCommentAnchors` were no-ops → no crash. Now they're populated → the apply runs at mount → a mark/anchor is inserted at an **invalid ProseMirror/DOM position** → `insertBefore` throws → mount fails. Suspect files: `src/widgets/importedRevisions.ts` (`applyImportedRevisions`, deletion re-materialize), `src/widgets/importedComments.ts` (`applyImportedCommentAnchors`), and/or `ComposeEditor.tsx` mount effect ordering (they run after `stampParaIds`, before `captureParaIdSnapshot`).
  - ALSO possible: the styles-pane / comments FAB **portal/panel mount** (043/044) doing a bad DOM insert — but the "Compose failed to load" on mount points more at the imported-marks path.
- **Note:** the amber banner *"Couldn't place this suggested edit: its target text was not found in the current document"* is the **FR-19 do-not-guess banner working as intended** (a pending redline whose target didn't resolve) — NOT a bug. But it co-occurs, so the doc has an unresolved pending redline; the import/redline apply on THIS doc is what's crashing.
- **Next step:** reproduce locally (headless is green — this is a real-DOM-only failure), wrap `applyImportedRevisions`/`applyImportedCommentAnchors` position resolution in bounds-checks / try-catch-skip-per-mark (degrade: skip an unplaceable mark, don't crash the whole mount), and add a real-DOM (jsdom won't catch insertBefore reliably — may need a guard + unit test on the position math).

### P2 — Save fails: "content is required and must be non-empty"
- **Symptom:** clicking Save → **"Save failed: content is required and must be non-empty."**
- **Context:** this is a **born-in-editor** doc (AI-drafted letter). Per task 027 the client's `triggerSave` should classify it as born-in-editor → send `{ contentModel }` via **create-on-save** (whose guard was relaxed to `hasContent || hasContentModel`). The error implies the server's content-required guard rejected the request → either (a) the client sent neither `content` nor `contentModel` (mis-classified the 4-case in `ComposeWorkspace.triggerSave`), (b) `buildContentModel(editor)` returned empty, or (c) this save hit the **replace** path (not create-on-save) whose guard is `hasContent || (editedParagraphs + baselineVersionId)` and none were present.
- **Next step:** trace `ComposeWorkspace.triggerSave` classification for a born-in-editor workspace-tab doc (isTransientCreate branch); log which branch + payload it sends; confirm the deployed `ComposeEndpoints` create-on-save guard actually accepts contentModel-only. Likely the doc state (opened from a ledger/AI draft into a workspace tab) isn't matching the born-in-editor predicate.

### P3 — Word "Open in Web" / "Open in Desktop" not activated
- **Symptom:** the **Word** toolbar dropdown's "Open in Web" and "Open in Desktop" items are disabled / do nothing.
- **Leading hypothesis:** they require a **saved document** (a `sprk_document` / SPE item id) and this born-in-editor doc isn't saved yet (blocked by P2), OR a wiring/enablement gap in the Word menu. Fix P2 first, then re-check; if still dead, trace the Word-dropdown item handlers + their enable predicate.

## UX request (not a bug)

### UX-1 — Add "Save" to the Word dropdown
- Add a **Save** item inside the **Word** dropdown (a deliberate duplicate of the toolbar save-disk icon). Rationale: users instinctively look in the Word menu to save. Small addition to `ComposeFormatToolbar.tsx`'s Word dropdown.

## Environment / noise (ignore)
- `[SpaarkeAuth] Token acquired via in-memory-cache(browser-msal)` — normal.
- `Uncaught (in promise) Error: A listener indicated an asynchronous response by returning true, but the message channel closed before a response was received` — browser-extension noise (Chrome message channel), not our code.

## Fix priority
P1 (mount crash — blocks everything) → P2 (save — blocks the round-trip + P3) → P3 (Word open) → UX-1 (Word-dropdown Save). All are client-side (`Spaarke.Compose.Components`), no BFF change expected (contract already deployed). Re-deploy `sprk_spaarkeai` after fixes (BFF unchanged).
