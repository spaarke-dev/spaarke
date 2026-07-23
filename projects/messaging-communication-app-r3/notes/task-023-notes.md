# Task 023 — MessageQuickView popover + ConversationView scroll-to-message (FR-05)

**Status**: completed 2026-07-21 · FULL rigor · sonnet/high · parallel Wave 11 (group C) · Step 9.5 gate RUN — verdict **SHIP** (3 minor fixes folded)

## What shipped
- **New `MessageQuickView`** (`components/MessageQuickView/`): a Fluent v9 Popover showing a 200-char message preview (email → to/from/date/subject with graceful fallbacks; chat omits the field block), Esc-dismiss + `trapFocus` + ARIA (copied/improved from `AiSummaryPopover`), and an "open→pin" button that calls a decoupled `onPin?(messageId)` callback then closes. Reuses `TimelineMessage` (NFR-06). HTML stripped before truncation; ellipsis only when actually cut.
- **`ConversationView` gained a scroll-to-message imperative handle** (additive): converted `React.FC` → `React.forwardRef<ConversationViewHandle, ConversationViewProps>` exposing `scrollToMessage(messageId)` via `useImperativeHandle`. Each `MessageBubble` is wrapped in a `<div data-message-id>` anchor (a `Map<id, HTMLDivElement>` via callback ref); `scrollToMessage` scrolls it into view (guarded `scrollIntoView`) and applies a transient ~1.5s highlight (semantic tokens: `colorNeutralBackground3Selected` bg + `colorBrandStroke1` outline — outline, so no layout shift). No-op (never throws) when the id isn't rendered (filtered out / other thread). `MessageBubble.tsx` untouched.
- **Files**: `MessageQuickView/{MessageQuickView.tsx,index.ts,__tests__/MessageQuickView.test.tsx}`; `ConversationView/ConversationView.tsx` + `ConversationView.types.ts` (`ConversationViewHandle`); `ConversationView/__tests__/ConversationView.scrollToMessage.test.tsx`; barrel `components/index.ts` (`export * from './MessageQuickView'`, reconciled by main session).

## Key design decisions
- **forwardRef is transparent** — existing `<ConversationView .../>` callers (tasks 011/013/014: bubbles + compose + filters) pass no ref and are unchanged (gate verified: no production ref callers; all prior ConversationView suites still green).
- **Popover decoupled from ConversationView** (ADR-012): `MessageQuickView` takes `onPin`; the Phase-4 host wires it to `conversationViewRef.current.scrollToMessage`. The popover is NOT hard-coupled to a ConversationView instance.
- **Anchor wrapper doesn't break layout**: `messageAnchor` is `flex column` (default `align-items: stretch`), so `MessageBubble`'s row still spans full width and mine/others alignment, day-divider grouping, filtered-empty, and auto-scroll (keyed on `renderItems.length`) are all preserved.

## Step 9.5 gate outcome — SHIP (no Critical/Major)
Folded 3 prompt-flagged minors: (M1) guarded `scrollIntoView` for environments where it's undefined; (M2 §11) dropped the unused public exports `truncatePreview` + `MESSAGE_QUICK_VIEW_MAX_CHARS` (now module-private — never reach the barrel); (M4) strengthened the no-op test to assert `scrollIntoView` never called + no `[data-highlighted]` on a miss. Left as acceptable: dark-mode smoke test (jsdom can't read tokens), cosmetic HTML-strip leak on malformed markup (React-escaped, no XSS), surrogate-pair truncation edge, static surface aria-label, ref-callback churn.
ADR verdict: ADR-021 PASS · ADR-012 PASS · NFR-06 PASS · NFR-05 PASS · forwardRef-no-regression PASS.

## Verification
- `npm test -- src/components/MessageQuickView src/components/ConversationView` → **53 passed** (5 suites).
- `tsc --noEmit` → 2 pre-existing unrelated errors only. `eslint` clean.

## Acceptance criteria — all met
200-char preview + email fields + graceful degrade ✅ · open→pin scrolls + highlights ✅ · focus-trap + Esc + ARIA + empty/error ✅ · copies AiSummaryPopover, reuses timeline types ✅ · dark mode ✅ · tests pass ✅.
