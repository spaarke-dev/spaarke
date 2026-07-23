# Task 062 — Shared conversation components: Teams-style redesign (UAT §B3–B9)

Owned surface: `src/client/shared/Spaarke.UI.Components/src/components/ConversationWorkspace/**`
and `.../ConversationView/**` (incl. `MessageBubble` / `EmailInFlowBlock`) + their tests.
Did NOT touch the PCF (`CommunicationConversationPanel/**` — task 061), the BFF (060),
TASK-INDEX.md, or current-task.md.

## What changed, per UAT §B item

- **§B3 Teams-like layout** — achieved via the sum of §B4–B9: contrast side pane,
  icon toolbar, header-above-bubble message blocks, and the jump-to-latest affordance.
- **§B4 Remove "Filter threads" text input** — deleted the `<Input>` from `ThreadList`'s
  toolbar; removed the search machinery from `ConversationWorkspace` (state, debounce,
  server `search` param, client-side narrowing). The list now always shows the full,
  access-filtered set for the current scope. `IThreadListProps.searchTerm` /
  `onSearchTermChange` removed (ThreadList is an internal subcomponent — no host constructs
  it directly, so this is not a host API break).
- **§B5 `+ New` → `+`** — the create-thread `<Button>` is now icon-only (`AddRegular`),
  wrapped in a `Tooltip`; accessible name stays `"New thread"` (aria-label) so keyboard +
  SR users still identify it (NFR-05).
- **§B6 Side-pane contrast** — `ThreadList` root fill → `colorNeutralBackground2` (message
  pane stays `colorNeutralBackground1`). Semantic tokens → adapts in dark mode (ADR-021).
- **§B7 Chat-area icon toolbar** — replaced the Email/Message text `ToggleButton`s + the
  "All messages" word `Dropdown` with an icon toolbar in `ConversationView`:
  - Email icon toggle (`MailRegular`, aria-pressed, aria-label "Show email messages")
  - Message icon toggle (`ChatRegular`, aria-pressed, "Show chat messages")
  - **Unread** icon toggle (`MailUnreadRegular`, aria-pressed, "Show unread messages only")
  - Search icon (`SearchRegular`, aria-pressed/aria-expanded, "Search messages") that
    reveals a labeled text `<Input>` ("Filter messages by text").
  The **additive AND-of-facets semantics are unchanged** — `messagePassesFilters` (type +
  word) is untouched; the Unread facet is combined additively in the `filteredTimeline`
  memo. `extractWordOptions` / `messageSearchText` remain exported (still unit-tested);
  the word dropdown that consumed `extractWordOptions` was simply replaced by the search box.
- **§B8 Circle down-arrow jump-to-latest** — a circular `Button` (`ArrowCircleDownRegular`,
  `shape="circular"`, aria-label "Jump to latest messages") floats bottom-right of the
  message list, shown only while scrolled up (driven by the existing at-bottom detection).
  Auto-scroll behavior is unchanged; the button re-arms auto-scroll on click. Keyboard-
  operable (native button).
- **§B9 Teams message blocks** — `MessageBubble` now renders the **type pill
  (`ChannelBadge`) + privacy markers + sender name + date/time ON A HEADER ROW ABOVE the
  bubble**; the bubble body below is **light-gray (`colorNeutralBackground3`) for others**
  and **light-blue (`colorBrandBackground2`) for the current user** — both semantic tokens
  (ADR-021 dark-mode-safe). Own messages omit the redundant self-name (Teams convention),
  which also keeps the existing "status on own only / name on others only" test green.
  The delivery status stays INSIDE the bubble (`role="article"`). `EmailInFlowBlock` is
  intentionally left as its own distinct compact block (task 021) — not restructured.

## Unread facet — semantics decision (documented)

The read-model (`ThreadMessageDto` / `TimelineMessage`) carries **no per-message read
flag**, and the unread-count endpoint returns only a thread-level count, not which rows.
The honest, data-available definition adopted: a message is **"unread" when it arrived
strictly after the caller first opened this conversation** (newer than the newest
`createdOn` captured on the initial load — `initialCursorRef`). This is Teams-consistent
("new since you looked"), deterministic, additive, and keyboard/aria-operable. On a static
open thread the facet narrows everything out (all rows are "already read"), surfacing the
existing "No messages match the current filters." state — verified by test. No fork, no new
backend, no new state machine.

## Preserved behaviors — all still render + pass their suites

compose/send (013), additive filters (014), scrollToMessage (023), forward (022),
email-in-flow block (021), privacy/privilege markers (043), attachment open + attach-on-
compose (042), thread pin toggle (041). None regressed.

## Verification

- Scoped `tsc --noEmit` (Spaarke.UI.Components): **clean, 0 errors**.
- `jest ConversationView ConversationWorkspace`: **8 suites / 82 tests pass**
  (ConversationView.test, .filters, .compose, .emailInFlow, .forward, .scrollToMessage,
  .titleLink, ConversationWorkspace.test). The `act()` console warning in the compose suite
  is pre-existing (poll dispatch), not introduced here.
- Prettier: changed files formatted.

## Hard-rule compliance

- ADR-021: all new colors are Fluent v9 semantic tokens (light-gray/light-blue bubbles,
  contrast pane, jump button) — no hardcoded colors; dark mode adapts via host FluentProvider.
- NFR-06: no fork / no second conversation component — same core, edited in place.
- NFR-05: icon filters carry aria-pressed; search input labeled; jump-to-latest + toggles
  are native buttons (keyboard-operable).
- ADR-012: components stay context-agnostic (no Xrm / platform imports added).
- MODAL-DECISION-CRITERIA: shared content only; the custom Fluent Dialog shell is the host's
  ConversationModal (task 061) — untouched here.
