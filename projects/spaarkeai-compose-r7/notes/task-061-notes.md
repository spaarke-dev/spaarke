# Task 061 — Ctrl+Shift+Space focuses the Assistant chat input (FR-05 / UC-6) — IMPLEMENTED

> Phase 6 (Hotkeys / UC-6) · sonnet@high · FULL rigor · 2026-08-17 · client-only, cross-package
> (Spaarke.Compose.Components + Spaarke.UI.Components + Spaarke.AI.Widgets + SpaarkeAi). No BFF bytes.
> Depends on 001 (gate) + task 060 (shared `composeHotkeys.ts` + editor keydown seam).

## /conflict-check (mandatory — SpaarkeAi hot-path) — CLEAR (escalation trigger did NOT fire)

The task touches `ConversationPane.tsx` (SpaarkeAi code page) + `SprkChatInput.tsx` / `SprkChat/types.ts`
(shared UI), a surface historically shared with active `spaarkeai-assistant-enhancements-r3`/`-r4`.
Checked: **no open PR** touches these files (assistant-r3 has no open PR; only Compose-relevant PR is
#690/ci-lfs — irrelevant); **neither active worktree** (r3 @ `5cdc8a2f8`, r4 @ `09c5e52a8`) has any
uncommitted OR branch-ahead commit to `SprkChatInput.tsx` / `SprkChat/types.ts` / `ConversationPane.tsx`.
No in-flight overlap → the POML's escalation trigger ("if assistant-r3 has an in-flight change to
SprkChatInput useImperativeHandle or ConversationPane's bridge, STOP and coordinate") did NOT fire. Soft
warn only (shared surface, no overlap) — sequence the PR normally.

## Architecture (editor → chat, across panes, via the existing PaneEventBus)

The editor and the Assistant chat live in DIFFERENT panes, so the signal crosses the pane boundary via the
existing PaneEventBus (ADR-030), NOT a new transport:

```
ComposeEditor  Ctrl+Shift+Space (matchesFocusChatHotkey, IME-guarded)
   → dispatch('conversation', { type: 'focus_chat_input', sessionId })      [ONE additive discriminant]
       → ConversationPane usePaneEvent('conversation') → setFocusInputSignal(n => n+1)   [relay, host layer]
           → <SprkChat focusInputSignal={n}>  effect (lastFocusSignalRef one-shot guard)
               → inputHandleRef.current.focusInput()      [SprkChatInput imperative handle]
                   → inputRef.current.focus()             [textarea gets focus, content untouched]
```

Every hop reuses an EXISTING seam. `focusInputSignal` is a monotonic number-nonce that mirrors the shipped
`pendingOutboundMessage` host→send seam EXACTLY (same `lastFocusSignalRef` re-render guard idiom), so a
repeated Ctrl+Shift+Space re-focuses each time. §11: both `focusInput()` and the `focus_chat_input` event
are EXTENSIONS of existing surfaces (per the POML `<justification>`), not new components.

## What shipped (5 files across 4 packages + shared hotkey util)

- **`composeHotkeys.ts`** (Compose) — new pure `matchesFocusChatHotkey(event)` (Ctrl/Cmd+Shift+Space,
  IME-guarded). ALSO added a **Shift-guard to `matchesDescribeChangeHotkey`** (task 060) so Ctrl+Shift+Space
  no longer ALSO matches the FR-04 caret hotkey — the two never collide. `shiftKey?` added to the event
  interface (optional ⇒ existing callers/tests read as "no shift").
- **`SprkChat/types.ts`** (UI) — `focusInput()` added to `ISprkChatInputHandle`; `focusInputSignal?: number`
  added to `ISprkChatProps` (both additive/optional — existing consumers byte-identical).
- **`SprkChatInput.tsx`** (UI) — `focusInput: () => inputRef.current?.focus()` in the useImperativeHandle
  (alongside `triggerSlashMode`). Focus-only, no content mutation.
- **`SprkChat.tsx`** (UI) — destructures `focusInputSignal` + a consume-effect (`lastFocusSignalRef` guard)
  that calls `inputHandleRef.current?.focusInput()` on each new value. Mirrors the `pendingOutboundMessage`
  effect directly.
- **`PaneEventTypes.ts`** (AI.Widgets) — `| 'focus_chat_input'` added to the `conversation` channel's
  `ConversationPaneEvent` type union (reuses existing `sessionId`/`timestamp` fields; carries NO content —
  Tier-1 only, ADR-015). Additive per ADR-030.
- **`ConversationPane.tsx`** (SpaarkeAi) — new `focusInputSignal` state; a `focus_chat_input` branch added
  to the EXISTING `usePaneEvent('conversation', …)` handler (single-subscriber-per-concern convention, not
  a 2nd subscription); `focusInputSignal={focusInputSignal}` passed to `<SprkChat>`.
- **`ComposeEditor.tsx`** (Compose) — Ctrl+Shift+Space branch in the existing `editorProps.handleKeyDown`
  (checked before the FR-04 branch) → `emitFocusChat` (reached via fresh `focusChatRef`, stale-closure
  convention) → `dispatch('conversation', { type: 'focus_chat_input', sessionId })`. Plus the
  `aria-keyshortcuts` discoverability hint (see below).

## Discoverability hint — decision (acceptance criterion 3)

The criterion asks for "a tooltip/shortcut hint." I exposed it as **`aria-keyshortcuts="Control+Space
Control+Shift+Space"`** on the editor's root textbox (advertising BOTH editor hotkeys — FR-04 caret +
FR-05 focus-chat). Rationale for choosing the ARIA-standard shortcut advertisement over a visible hover
tooltip:
- A native `title` on the whole editing surface would pop on EVERY hover (intrusive, poor UX).
- Putting an app-specific shortcut string into the shared, context-agnostic `SprkChat` would violate
  ADR-012 (the shortcut belongs to the editor, not the generic chat component).
- `aria-keyshortcuts` is the a11y-standard, screen-reader-discoverable, non-intrusive, testable mechanism,
  and it lives on the element that owns the shortcut behavior (the editor textbox).

This is a directional adaptation from the ui-test's "hover → tooltip" wording; if the owner wants a
VISIBLE chip/tooltip affordance, that is a trivial additive follow-up (flagged, not blocking).

## Verification

- **Standalone jest (Compose): 642 pass / 0 fail** (was 634 + 8 new `composeHotkeys.test.ts` tests:
  focus-chat matches, Shift disambiguation, IME guard, negatives). Runs in this session — proves the IME
  guard + both hotkeys + the 060/061 disambiguation directly.
- **CI-only** (needs `@spaarke/*` resolution — standalone-unloadable by design):
  - `ComposeEditor.focusChatHotkey.test.tsx` (Compose, +4): Ctrl+Shift+Space dispatches
    `conversation.focus_chat_input` with the sessionId (real PaneEventBus subscriber records it);
    `aria-keyshortcuts` present; plain Ctrl+Space does NOT emit (disambiguation); `isComposing` suppresses.
  - `SprkChatInput.focusInput.test.tsx` (UI, +2): `focusInput()` focuses the textarea; does not change its
    value. (UI.Components/SpaarkeAi/AI.Widgets have no package-level jest — workspace/CI only.)
- **tsc**: Compose package 30 errors = KNOWN baseline (`@spaarke/*` resolution only), **0 new-symbol
  errors** (no reference to `matchesFocusChat`/`emitFocusChat`/`focusChatRef`/`focus_chat_input`).
  AI.Widgets `PaneEventTypes.ts` typechecks **clean** — the new discriminant is valid. UI.Components/SpaarkeAi
  standalone tsc is dominated by the missing-`react` cascade (every prop shows implicit-any, incl.
  pre-existing ones) — not a valid standalone typecheck env; the cross-package type check runs in CI.
- **No BFF bytes** → publish size + CVE unchanged.

## Test-story honesty

The two NOVEL ends are directly tested: the editor EMIT (Compose CI test) and the SprkChatInput handle
FOCUS (UI CI test). The middle two hops — ConversationPane's relay branch and SprkChat's consume-effect —
are byte-for-byte mirrors of the shipped-and-tested `pendingOutboundMessage` host→send seam, and the event
discriminant typechecks (AI.Widgets clean). Full cross-package type+runtime validation runs in CI.

## Gates (Step 9.5)

- **code-review: PASS** — number-nonce mirrors the proven pendingOutboundMessage one-shot; Shift
  disambiguation; stale-closure via ref; IME-guarded; preventDefault+return true; no AI smells.
- **adr-check: PASS** — ADR-030 (one additive discriminant, existing bus/transport, existing single
  subscriber extended); ADR-012 (context-agnostic focusInput/focusInputSignal; app shortcut only in the
  editor); ADR-015 (focus_chat_input carries no content); §11 (extensions per POML justification); NFR-06
  docxBridge.ts intact.

## Phase 6 (UC-5 + UC-6 hotkeys) — COMPLETE (060 caret describe-change + 061 focus chat). 17/20 done.
## Next: Phase 7 — 070 (blank page editable), 071 (restore-from-source), 072 (add-comment), 074 (apply-template ETag/404). Then 090 wrap-up.
