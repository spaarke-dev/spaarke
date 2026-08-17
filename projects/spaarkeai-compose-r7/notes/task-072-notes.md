# Task 072 — Add Comment toolbar affordance (R6 D7 / FR-10) — IMPLEMENTED (UI entry point onto shipped machinery)

> Phase 7 · sonnet@high · FULL rigor · 2026-08-17 · client-only. No BFF bytes.

## Root cause of D7 (missing UI entry point — the machinery was already shipped)

The comment round-trip machinery shipped + seam-proven in R6 (tasks 024/026):
- `useComposeCommentThreads(editor, author).createThread(text, {from,to})` — the hook that applies the
  `commentAnchor` mark + records the thread (proven in `ComposeCommentThread.test.tsx`).
- `<ComposeCommentThread pendingRange={…} onThreadCreated={…}>` — the composer; opens when a
  `pendingRange` is set, calls `createThread` on submit (`ComposeCommentThread.tsx` ~313).
- `handleToggleComments` (`ComposeEditor.tsx` ~2869) — THE shipped seam: captures the editor's live
  selection at click time → `setPendingCommentRange({from,to,preview})` → opens the composer.

The gap: the UI trigger (a floating "Comments" FAB) was **REMOVED in UAT round-6 #3b** (ComposeEditor.tsx
~3600 comment: *"The ComposeCommentThread panel + useComposeCommentThreads instance remain in the codebase
(now unreachable from the UI) so the capability can be re-exposed later without re-plumbing."*). So D7 is
purely a missing entry point onto intact machinery.

## What shipped (re-expose, don't rebuild — §11)

- **`ComposeFormatToolbar.tsx`** — new optional props `commentsOpen?: boolean` + `onToggleComments?: () =>
  void`; a new **"Add Comment"** icon-only `ToolbarButton` in Group 3 (next to Review Notes / Track
  Changes), rendered only when `onToggleComments` is supplied. Mirrors the shipped Track Changes toggle
  EXACTLY: `appearance={commentsOpen ? 'primary' : 'subtle'}` (ADR-021 — the primary/subtle state carries
  on/off, dark-mode-correct, no hardcoded colors), `aria-pressed`, `aria-label="Add comment"`, Tooltip
  "Add a comment on the selected text", `CommentAdd24Regular` icon, `data-testid="compose-format-add-comment"`.
- **`ComposeEditor.tsx`** — thread `commentsOpen={commentsOpen}` + `onToggleComments={handleToggleComments}`
  into `<ComposeFormatToolbar>`. That's the entire wiring: the button drives the EXISTING
  `handleToggleComments` → `pendingCommentRange` → `ComposeCommentThread` composer → `createThread`. No new
  comment pipeline, no new state, no new hook.

## Directional deviation (recorded)

The POML listed `ComposeAiToolbar.tsx` too. I placed the single entry point in `ComposeFormatToolbar` (the
PERSISTENT top toolbar) rather than the selection-triggered AI bubble: it is always visible (more
discoverable), sits with the sibling document-level toggles (Review Notes / Track Changes), and mirrors the
semantics of the removed Comments FAB. §11 asks for ONE entry wired to the existing machinery, not two.
`ComposeAiToolbar.tsx` was intentionally left unmodified.

## Verification

- **Standalone jest: 650 pass / 0 fail** (645 + 5 new `ComposeFormatToolbar.test.tsx` Add Comment tests —
  runs in this session): not rendered without a handler; renders + fires `onToggleComments` on click
  (aria-label + aria-pressed asserted); reflects the open state via aria-pressed; disabled under global
  disable; **ADR-021 dark-mode — no hardcoded hex in the button subtree**.
- The button → `handleToggleComments` → `createThread` chain reuses the R6-shipped, seam-proven machinery
  (the `createThread` behavior is covered by `ComposeCommentThread.test.tsx`); the full-editor click-to-
  comment flow is the CI-only ui-test (ComposeEditor imports `@spaarke/*`).
- **tsc**: 30 = KNOWN `@spaarke/*` baseline; **0 new-symbol errors** (`onToggleComments`/`commentsOpen`/
  `CommentAdd24Regular` all resolve).
- **No BFF bytes** → publish/CVE unchanged.

## Gates (Step 9.5)

- **code-review: PASS** — mirrors the shipped Track Changes toggle; additive optional props; threads to the
  existing `handleToggleComments` seam; no new pipeline/state/hook; no smells.
- **adr-check: PASS** — ADR-021 (Fluent v9 semantic tokens, primary/subtle state, dark-mode verified — no
  hardcoded colors); ADR-012 (context-agnostic generic callback prop); §11 (reuses the shipped machinery —
  the explicit constraint); ADR-049 save path untouched; NFR-06 `docxBridge.ts` intact.
- **UI testing (Step 9.7)**: the ADR-021 dark-mode requirement is covered by the jest dark-theme test; live
  browser UAT deferred (no `--chrome`/deployed env this session).

## Phase 7: 072 DONE (19→20 impl; 20/20 incl. 073/075). Remaining: 074 (apply-template ETag/404) → 090 wrap-up.
