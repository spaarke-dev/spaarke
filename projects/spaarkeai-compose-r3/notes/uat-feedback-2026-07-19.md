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

---

## RESOLUTION (2026-07-19 — all fixed, client-only, no BFF change)

**P1 — `insertBefore` crash on Styles/Comments FAB — FIXED (commit e31f99cf2).**
Root cause was NOT the imported marks (import-independent; agent reproduced with zero imports).
"Compose failed to load" is the `WidgetErrorBoundary` fallback → the error is a React commit-phase
DOM error. TipTap's `BubbleMenu` plugin calls `this.element.remove()` on mount, detaching its
wrapper `<div>` from the DOM while React's fiber still records it as a live child. The `<BubbleMenu>`
rendered as a sibling BETWEEN the toggleable Comments/Styles panes (which `return null` when closed)
and the always-mounted editor scroll region; toggling a pane `null`→`<div>` made React's
`getHostSibling` resolve the insert-anchor to the DETACHED BubbleMenu node → `insertBefore` throws.
Latent since tasks 043/044; the imports wire-fix (8f2cec4a6) only made the stored-Word-doc UAT path
reachable. **Fix:** relocated the `<BubbleMenu>` to be the LAST child of the editor container, so
every conditional sibling anchors on the always-mounted `editorScrollWrap`. Regression test:
`ComposeEditor.paneToggleCrash.test.tsx` (3).

**P2 — born-in-editor Save "content is required" — FIXED (commit c1f57cb54).**
The message was a paraphrase of the server replace-path guard "Provide the retained-original
'content' bytes, or 'editedParagraphs' + 'baselineVersionId'…". A born-in-editor draft (mountDraftHtml)
saved fine the FIRST time (create-on-save → contentModel) but the SECOND save failed: the
save-response's `driveId` + `versionId` were discarded, so `saveSucceeded` set `speDriveItemId`
(→ second save takes the replace path) but left `versionId` null and `documentRef.driveId` undefined
→ replace body sent content=undefined + baselineVersionId=undefined → server couldn't resolve a
baseline (born-in-editor holds no docxBytes). **Fix (client-only):** retain the response's `driveId`
+ `versionId`; adopt versionId ADOPT-ONLY-WHEN-NULL (born-in-editor first version = fixed baseline;
stored doc's load-time versionId never advanced — FR-01); replace-save prefers `documentRef.driveId`.
Verified the paraId round-trip holds (renderer preserves client paraIds) and the synthesizer emits
nothing for unchanged paragraphs (no redundant tracked-changes, no snapshot reset needed).
Regression test: `ComposeWorkspace.saveBaseline.test.ts` (4, reducer-level).

**P3 — Word "Open in Web/Desktop" inactive — RESOLVED by P2 (no separate code change).**
`wordActionsDisabled = isSavingNow || !hasWordDocument || isWordActing`; `hasWordDocument` needs a
persisted `sprkDocumentId`/`speDriveItemId`. A born-in-editor draft has neither until saved
(correctly disabled — no SPE doc to open). The user couldn't save (P2), so the doc never got a
persisted id and Word-open stayed grey. With P2 fixed, the first save populates both ids via
`saveSucceeded` → Word-open activates. Re-verify in the next UAT after saving the draft.

**UX-1 — Save in the Word dropdown — DONE (commit e31f99cf2).**
Added a Save item inside the Word dropdown (`ComposeFormatToolbar.tsx`, testid
`compose-format-word-save`) — a deliberate duplicate of the right-aligned Save icon.

**Verify:** tsc green; jest 348/348 parallel AND --runInBand. Client-only; BFF unchanged.
**Next:** rebuild + re-deploy `sprk_spaarkeai` (clear Vite cache — the SpaarkeAi vite.config aliases
`@spaarke/compose-components` to SOURCE), then re-UAT P1→P2→P3→UX-1.
