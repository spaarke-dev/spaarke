# Task 014 — In-conversation additive filters (FR-09)

**Status**: completed 2026-07-21 · FULL rigor · sonnet/high · Step 9.5 gate RUN (verdict **Ship**; 2 Minor fixed, 3 Minor left as optional polish)

## What shipped
Added an additive filter bar to `ConversationView` (shared lib): two type toggles (Email / Message) + a word-filter `Dropdown`. Purely presentational over the already-polled timeline.

- **Files** (only the POML's declared outputs):
  - `.../ConversationView/ConversationView.tsx` — filter state (internal `useState`, no public prop change), exported pure helpers (`messagePassesFilters`, `messageSearchText`, `extractWordOptions`, `ConversationFilters`), filter bar JSX, filtered timeline, split empty states.
  - `.../ConversationView/__tests__/ConversationView.filters.test.tsx` — new (jest).

## Key contract decisions
- **Additive = AND-of-facets** (FR-09): a bubble renders iff `channelType` toggle is enabled AND (no word OR search-text contains the word). Confirmed against `buildTimeline`: every non-Message `communicationType` (teams/sms/notification/null) folds to `channelType==='email'`, so the two toggles govern 100% of rows — no un-hideable row.
- **Presentational only** (NFR-06): filtering flows through `useMemo` over the already-built `timeline` → filter → `buildConversationRenderItems` (dividers recompute for the filtered set, no orphan dividers). NO dispatch/fetch/cursor touch. `wordOptions` derives from the UNFILTERED timeline so the dropdown stays stable as the user narrows. **`ConversationViewProps` unchanged**; helpers are module-local (NOT re-exported from the folder barrel), consumed only by tests — no package-root surface creep (§11).
- **Distinct empty states**: `isThreadEmpty` ("No messages yet.") vs `isFilteredEmpty` ("No messages match the current filters.") — mutually exclusive + exhaustive with loading/error. Filter bar hidden when the thread is empty (nothing to filter).
- **Type strings unchanged**: filter keys on `channelType`; `COMMUNICATION_TYPE_EMAIL/MESSAGE` constants untouched.

## Step 9.5 gate outcome (adversarial review) — verdict Ship
- **Minor (FIXED)**: the filtered-empty `Text` had `role="status"` nested inside the `role="log"` list → nested live regions (contradicts the file's own compose-bar discipline). Dropped the role; the log already announces content changes.
- **Minor (FIXED)**: the word dropdown surfaced sender-email fragments (e.g. `com`) that match nearly every row. Factored out `messagePlainBody`; `extractWordOptions` now draws from visible body + `senderName` only, while `messageSearchText` (the MATCH) still includes the sender email.
- **Minor (left, optional)**: naive HTML-strip regex on malformed/entity content (well-formed html escapes `<`; low impact); the `value={word||'All messages'}` makes `placeholder` dead (cosmetic); auto-scroll effect keyed on `renderItems.length` re-scrolls on filter change (harmless). Not fixed — no functional defect.
- Verified: AND-of-facets correct, presentational guarantee holds, empty states exclusive+exhaustive, no orphan dividers, ADR-021 tokens only, no props/barrel creep, no-refetch test genuine.

## Verification
- `npm test -- src/components/ConversationView` → **40 passed** (3 suites: 011 bubbles, 013 compose, 014 filters). `act()` warning is the pre-existing benign async-poll-settle one.
- `tsc --noEmit -p tsconfig.json` → **2 errors, both pre-existing + unrelated** (`@spaarke/sdap-client` / `@spaarke/auth` sibling `dist` unbuilt). Zero from this change.
- `eslint src/components/ConversationView` → clean.
- Whole-package `npm run build` NOT run — same sibling-dist gap; scoped tsc + jest is the verification path.

## Acceptance criteria — all met
Word dropdown + Email/Message toggles combine additively ✅ · disabling a type hides that type, both-on shows both ✅ · presentational, no re-fetch, clearing restores ✅ · distinct no-match empty state + keyboard/ARIA ✅ · type strings unchanged ✅ · dark mode ✅ · component tests pass ✅.

## Phase 2 (shared conversation widget core) is now COMPLETE
010 (characterize) · 011 (bubbles) · 012 (two-pane shell) · 013 (compose) · 014 (filters). Next: Phase 3 (Wave 10 = task 020, **opus** — extend SendEmailDialog/EmailComposer with thread id + record link).
