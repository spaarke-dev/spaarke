# Task 031 — G9 Comment pane scroll-sync — Deviations & Notes

> **Status**: ✅ COMPLETE · 2026-07-30 · FULL rigor (overridden UP from POML STANDARD: modifies shared
> `ComposeEditor.tsx`/`ComposeCommentThread.tsx` on the NFR-09 surface) · sonnet/high · client-only.

## What shipped
- **`commentScrollSync.ts` (NEW)** — pure scroll-sync helpers: `resolveThreadAnchorPositions`
  (thread → live PM anchor pos, sorted, via the existing `findCommentAnchorRange`),
  `pickActiveThreadId` (the thread nearest at/above the viewport top — the testable core),
  `scrollEditorToThreadAnchor` (pane→doc: select the live anchor span + `scrollIntoView`),
  `resolveViewportTopPos` (map the scroll container's top to a doc pos via `posAtCoords`).
- **`commentAnchorRange.ts` (NEW, leaf)** — `findCommentAnchorRange` + `COMMENT_ANCHOR_MARK_NAME`
  EXTRACTED from `ComposeCommentThread.types.ts` so the scroll-sync helpers can import the primitive
  without transitively loading that file's persistence-vocabulary chain (`useComposeWordShuttle` →
  `@spaarke/auth`). `ComposeCommentThread.types.ts` re-exports both symbols — **single implementation,
  no duplication (§11)**; every existing import site is unaffected.
- **`ComposeCommentThread.tsx`** — position-linked the pane:
  - **pane → doc**: each thread card's header is a keyboard-reachable jump affordance
    (`role="button"`, Enter/Space) — clicking it scrolls the editor to the comment's live anchor +
    marks it active.
  - **doc → pane**: a new `scrollContainerRef` prop (the editor scroll container). An rAF-throttled
    scroll listener resolves the viewport-top doc pos → `pickActiveThreadId` → highlights the active
    card (`threadActive` brand-token style) + scrolls it into pane view (`block:'nearest'` — no smooth
    animation, correctness over jank per the task failure-mode note).
- **`ComposeEditor.tsx`** — passes `scrollContainerRef={editorScrollRef}` to the panel.

## Escalation trigger — did NOT fire
Trigger: "if scroll-sync forces a persistence/anchor-storage change to work correctly, STOP." It stayed
**purely client-side** — anchors resolve from LIVE `commentAnchor` mark positions (`findCommentAnchorRange`
+ `posAtCoords`/`scrollIntoView`); no persistence, no save-contract, no anchor-storage change (ADR-049
client-is-view+controller). No escalation warranted.

## Verification
- `commentScrollSync` **9/9** (pure `pickActiveThreadId` cases + `scrollEditorToThreadAnchor` over a
  chainable editor mock, incl. anchor-gone no-op). Runnable client suite **55/55** (9 scroll-sync +
  7 external-change banner + 39 toolbar).
- Client typecheck clean for all new/changed files (only the known pre-existing `@spaarke/*`-unlinked
  cascades remain — identical on master).
- **No C# / BFF change** → Compose C# suite, byte-diff 24/24, publish size (48.13 MB), ArchTests all
  unchanged from task 030.
- `ComposeCommentThread.test.tsx` + `.anchoredComments.test.ts` fail ONLY on `Cannot find module
  '@spaarke/auth'` (the worktree monorepo-linking limitation, identical on master — NOT caused by this
  change; the extraction re-export keeps their import paths valid). The scroll-sync logic is covered by
  the leaf-importable `commentScrollSync.test.ts`, which runs green.

## jsdom limitation (honest note)
Scroll geometry (`getBoundingClientRect`, `posAtCoords`, `scrollIntoView`) returns zero/undefined in
jsdom, so the doc→pane scroll-tracking EFFECT cannot be exercised in a unit test — `resolveViewportTopPos`
returns null there and the effect no-ops by design. The testable core (`pickActiveThreadId`) is fully
covered; the DOM wiring is best-effort and degrades gracefully (no crash, no active change) when layout
is unavailable.

## Step 9.5 quality gates (applied)
- **code-review**: pure helpers well-tested; panel wiring additive; no persistence/security surface; no
  AI code smells; the `findCommentAnchorRange` leaf extraction avoids duplication (§11) rather than
  copying.
- **adr-check**: ADR-049 (client view+controller, resolves by live mark position — I-7 no text-search,
  no byte authoring, no persistence), ADR-021 (active-card style uses `colorBrandStroke1` token, no
  hex; sibling dark-mode-safe), ADR-013 (no AI type), §11 (single implementation via leaf re-export).
  No BFF touched → §10 N/A.

## PR obligations
- `/conflict-check` before the shared-client PR (soft-warn: ComposeEditor.tsx overlaps task 030 — both
  now merged in sequence on this branch; analysis-hub-r1 #694 shares Spaarke.Compose.Components,
  NFR-09 reopen-restore parity unaffected — no persistence/contract change). No new runtime package (NFR-03).
