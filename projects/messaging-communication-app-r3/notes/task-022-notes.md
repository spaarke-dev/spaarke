# Task 022 — Forward action → email modal in forward mode (FR-08)

**Status**: completed 2026-07-21 · FULL rigor · sonnet/high · Step 9.5 gate RUN — verdict **Fix-first (docs), then ship** (Major-1 doc-mitigated + 3 minors fixed)

## What shipped
- **`ConversationView`**: optional `onForwardMessage?(message)` prop + a per-message **Forward** affordance (subtle `ArrowForwardRegular` `Button`, contextual `aria-label` via `forwardLabel(message)`, hover/focus-reveal, keyboard-reachable) rendered in the existing `data-message-id` anchor so it applies to BOTH the chat bubble AND the email-in-flow block — WITHOUT editing `MessageBubble`/`EmailInFlowBlock`. Rendered ONLY when a handler is wired (no focusable no-op). ConversationView persists NO draft (ADR-012 — the callback hands the message to the host).
- **`SendEmailDialog` wrapper**: additive optional `sourceRecord?` + `communicationId?` so a host can open `mode="forward"` — they reach `EmailComposer` via the existing `...composerProps` spread (type-only change; no runtime change; existing callers unaffected).
- The host builds a forward `sourceRecord` from the now-enriched `TimelineMessage` (subject/to/sender/body/bodyFormat/attachments from task 021); the composer's EXISTING `deriveForwardState` derives the prefill (`Fwd:` subject via idempotent `dedupSubjectPrefix`, quoted "Forwarded message" body, source attachments `selected:true`). No new forward send path (ADR-045).

## Step 9.5 gate — Ship
- **Major-1 (regarding-in-forward, DOC-mitigated)**: in forward mode the composer derives `associations` from `sourceRecord.associations`, OVERRIDING the dialog's `regarding` fold — so `regarding` alone is dropped on a forwarded send (`threadId` survives — top-level send() prop). Verdict: document-only (the engine-union fix has shared blast radius across reply/forward/draft — deferred as **ISS-005 [#672](https://github.com/spaarke-dev/spaarke/issues/672)**). Added a ⚠️ note to the `onForwardMessage` + `sourceRecord` JSDocs: the host MUST include the regarding record in `sourceRecord.associations` to keep a forwarded email associated.
- **Minor-2 (FIXED)**: Forward buttons had identical `aria-label="Forward message"` → added `forwardLabel(message)` (subject→senderName→sender→'message') for contextual labels.
- **Minor-3 (FIXED)**: gated the affordance render on `onForwardMessage` being wired (no focusable no-op for keyboard users).
- **Minor-4 (FIXED)**: integration test now includes an attachment in the forward `sourceRecord` and asserts it renders checked — proving the full subject+body+ATTACHMENTS prefill end-to-end.
- Gate ADR verdict: ADR-045 PASS · ADR-012 PASS · ADR-021 PASS · NFR-05 PASS · no-regression PASS · §11 PASS (no new export/component/send path).

## Flaky-test hardening (surfaced this task, not caused by it)
The two Fluent-`Dialog`-open integration tests (`ConversationView.emailInFlow.test.tsx` open→dialog, and this task's `ConversationView.forward.test.tsx` forward→dialog) intermittently timed out on `await screen.findByRole('dialog')` (jsdom + tabster + userEvent open-timing — pre-existing fragility, confirmed flaky: passed 3/3 then 6/6 in repeated runs, not a task-022 logic bug). Hardened both with `findByRole('dialog', {}, { timeout: 4000 })`. Combined suite then stable 193/193 across 2 runs.

## Verification
- `npm test -- src/components/ConversationView src/components/EmailComposer` → **193 pass** (16 suites), stable across repeated runs.
- `tsc --noEmit` → 2 pre-existing unrelated errors only. `eslint` clean.

## Acceptance criteria — all met
Forward opens SendEmailDialog in forward mode, prefilled (subject/body/attachments) via existing forward semantics ✅ · reuses existing send path with active thread (regarding via host-supplied `sourceRecord.associations`, documented) ✅ · no conversation-side draft ✅ · keyboard + ARIA ✅ · dark mode ✅ · tests pass ✅.

## Phase 3 status
020, 021, 022, 023, 024 done. Remaining Phase 3: **025** (conversation title → record-scoped modal link).
