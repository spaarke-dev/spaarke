# Task 060 — Ctrl+Space "Describe a change" at the caret (FR-04 / UC-5) — IMPLEMENTED

> Phase 6 (Hotkeys / UC-5) · sonnet@high · FULL rigor · 2026-08-17 · client-only (Spaarke.Compose.Components)
> Depends on 001 (gate). No BFF bytes changed.

## Binding decision (records acceptance criterion 3)

**Both bindings are wired in code — Ctrl/Cmd+Space (primary) AND Ctrl/Cmd+/ (fallback).** Rather than
ship only Ctrl+Space and defer the fallback to a future live-IME-testing gate (which this non-interactive
session cannot run), both fire the same handler. Rationale:

- **Ctrl+Space** is the owner's requested primary binding, but it is an IME composition toggle on some
  stacks. The handler is fully IME-guarded (`isComposing` OR legacy `keyCode === 229` ⇒ suppressed), so
  it never hijacks composition. If a specific stack's IME still eats Ctrl+Space at the OS/browser layer
  (before the keydown reaches us), **Ctrl+/ is already available with no code change**.
- **macOS note**: Cmd+Space is reserved by macOS for Spotlight (intercepted at the OS layer, never
  reaches the browser), so the *effective* primary binding on macOS is **Cmd+/**. Wiring both means macOS
  users get a working hotkey out of the box.

The escalation trigger ("if NEITHER binding works cleanly, STOP") did **not** fire: the IME guard is
proven (standalone test), and the dual-binding design guarantees at least one reachable binding on every
target stack. Live per-stack IME UAT remains an operator confirmation step, but the code degrades safely
(guarded, never hijacks) rather than shipping a broken hotkey.

## What shipped

- **NEW `composeHotkeys.ts`** — pure `matchesDescribeChangeHotkey(event)` predicate (Ctrl/Cmd+Space |
  Ctrl/Cmd+/, IME-guarded). Extracted as a pure fn so the IME guard (the known failure mode) is
  **standalone-unit-testable without mounting a TipTap editor**. §11 justification: concrete failing
  behavior prevented = an IME-composition Ctrl+Space firing the dialog mid-composition; inlined in the
  editor it would only be reachable via the CI-only mounted-editor suite. Precedent: `composeIdentity.ts`,
  `composeDraftStore.ts` (small pure utils extracted similarly).
- **`ComposeEditor.tsx`**:
  - `editorProps.handleKeyDown` (the ProseMirror keydown seam that already owns Ctrl+F) gains a
    `matchesDescribeChangeHotkey(event)` branch → `preventDefault()` + invokes the caret runner + returns
    `true` (so Space/`/` never also types).
  - `runDescribeChangeAtCaret` — resolves the **enclosing textblock (paragraph)** of the collapsed caret
    (`$from.start()`..`$from.end()`) as the edit target, REUSES the shipped `promptForInstruction` dialog
    (no parallel dialog — root §11), and dispatches the SAME `compose-rewrite-instruction` Action routed
    to the DOCUMENT session (`documentSessionId`) so the result lands as an inline redline (DEF-09). It is
    the keyboard sibling of `ComposeAiToolbar.handleActionClick` (selection-driven) and
    `dispatchNoteToolRequest` (note-clause-driven) — same slot shape, different target resolution.
  - `bindingId`-FIRST gate: reads the action from the runtime-merged registry
    (`getComposeAiToolbarActions()`) and no-ops if `bindingId` is unwired — mirrors the toolbar's own stub
    gate (so an unwired tool never prompts the user for an instruction it can't run).
  - Stale-closure handling: `editorProps.handleKeyDown` closes over the initial render, so the runner is
    reached via `describeChangeAtCaretRef` (kept fresh in an effect — same convention as
    `commentThreadsRef` / `selectedThreadIdRef`).

## Directional deviation from the POML (recorded)

The POML listed `ComposeAiToolbar.tsx` as a modify target and named "the forceVisible collapsed-cursor
path." The acceptance criteria require Ctrl+Space to **open the dialog directly** for the caret. The
cleanest reuse-first implementation is **self-contained in `ComposeEditor.tsx`** (hotkey → shipped
dialog → caret-paragraph dispatch). `ComposeAiToolbar.tsx`'s collapsed-selection guard (`if (from===to)
return`) was **intentionally NOT modified** — that guard governs the right-click/forceVisible toolbar;
changing it would expand scope and alter right-click UX (a separate concern). `promptForInstruction` —
the dialog the POML's §11 constraint actually names — IS reused. `<steps mode="directional">` explicitly
permits adapting the sequence to the real codebase; the goal + acceptance-criteria + constraints are met.

## Verification

- **Standalone jest: 634 pass / 0 fail** (was 622 + 12 new `composeHotkeys.test.ts` predicate tests
  covering both bindings, the IME guard via `isComposing` + `keyCode 229`, and negative cases). Runs in
  this session.
- **CI-only** `ComposeEditor.describeChangeHotkey.test.tsx` (+3 tests, authored to the proven
  `aiToolbarTriggers` real-editor harness): collapsed-caret Ctrl+Space opens the dialog + dispatches the
  paragraph-scoped Action (`selectionAnchorStart/End` = 1..17 for `<p>Hello world here</p>`, instruction
  slot, `documentSessionId`); Ctrl+/ fallback opens it; Cancel dispatches nothing. In the CI-only suite
  group (needs `@spaarke/*` resolution — standalone-unloadable by design; 43→44 load-failures, the +1 is
  this new file, exactly as expected).
- **tsc**: 30 errors = the KNOWN monorepo baseline (`@spaarke/*` resolution only); **ZERO new** errors
  from task 060 (none reference `composeHotkeys` / `runDescribeChangeAtCaret` / the new refs).
- **No BFF bytes** → publish size + CVE unchanged.

## Gates (Step 9.5)

- **code-review: PASS** — 0 Critical / 0 Warning. Stale-closure via ref; bindingId-first gate mirrors the
  toolbar; `preventDefault`+`return true`; dispatch failure swallowed per ADR-019/existing convention;
  selectionText capped 16000 (matches `dispatchNoteToolRequest`); comments explain *why*.
- **adr-check: PASS** — ADR-012 context-agnostic (no PCF types); ADR-049/032 untouched; ADR-013 N/A
  (client-only); §11 one new pure util justified (IME-guard testability) + dialog reused; NFR-06
  `docxBridge.ts` intact.

## Phase 6 (UC-5 hotkeys) — 060 DONE. Next: 061 (Ctrl+Shift+Space focus chat — needs focusInput() on
ISprkChatInputHandle + PaneEventBus; /conflict-check ConversationPane/SprkChatInput vs assistant-r3).
