/**
 * composeHotkeys.ts — pure keydown predicates for the Compose editor's keyboard affordances.
 *
 * FR-04 (task 060, UC-5): the "Describe a change" instruction dialog can be opened at the CURRENT
 * CARET/PARAGRAPH (no selection) via a hotkey. The matching logic is extracted here as a pure
 * function so the IME guard — the known failure mode (Ctrl+Space is an IME composition toggle on
 * some stacks) — is unit-testable WITHOUT mounting a TipTap/ProseMirror editor. `ComposeEditor`'s
 * `editorProps.handleKeyDown` consumes it.
 *
 * Bindings:
 *  - Primary:  Ctrl/Cmd + Space
 *  - Fallback: Ctrl/Cmd + /   (ALWAYS wired, no code change needed if a stack's IME eats Ctrl+Space;
 *              note macOS reserves Cmd+Space for Spotlight at the OS layer, so the effective binding
 *              on macOS is Cmd+/)
 *
 * The handler is suppressed entirely while an IME composition is in flight (`isComposing`, or the
 * legacy `keyCode === 229` signal some stacks emit during composition) so it never hijacks IME input.
 */

/** The subset of `KeyboardEvent` fields the predicates read (keeps them DOM-free + trivially testable). */
export interface DescribeChangeHotkeyEvent {
  readonly ctrlKey: boolean;
  readonly metaKey: boolean;
  /** Optional so existing callers/tests that omit it read as "no shift" (undefined ⇒ falsy). */
  readonly shiftKey?: boolean;
  readonly code: string;
  readonly key: string;
  readonly isComposing?: boolean;
  readonly keyCode?: number;
}

/**
 * True when the event should open "Describe a change" at the caret: Ctrl/Cmd+Space (primary) or
 * Ctrl/Cmd+/ (fallback), NOT during an IME composition, and NOT when Shift is held (Ctrl+Shift+Space
 * is the separate FR-05 focus-chat hotkey — see `matchesFocusChatHotkey` — so the two never collide).
 */
export function matchesDescribeChangeHotkey(event: DescribeChangeHotkeyEvent): boolean {
  // IME guard FIRST — never fire mid-composition. `isComposing` is the standard signal; `keyCode 229`
  // is the legacy signal some browsers/IMEs emit for composition keydowns.
  if (event.isComposing || event.keyCode === 229) return false;
  // Shift disambiguates from the focus-chat hotkey (FR-05): Ctrl+Space (describe) vs Ctrl+Shift+Space.
  if (event.shiftKey) return false;

  const modifier = event.ctrlKey || event.metaKey;
  if (!modifier) return false;

  // `code === 'Space'` is layout-independent; `key === ' '` is the fallback for environments that
  // don't populate `code`. `/` is matched on `key` (its physical `code` varies by keyboard layout).
  const isSpace = event.code === 'Space' || event.key === ' ';
  const isSlash = event.key === '/';
  return isSpace || isSlash;
}

/**
 * FR-05 (task 061, UC-6) — True when the event should move focus into the Assistant chat input:
 * Ctrl/Cmd+Shift+Space, NOT during an IME composition. Shift is REQUIRED (that is what distinguishes
 * it from the FR-04 caret hotkey). macOS reserves Cmd+Space for Spotlight, but Cmd+Shift+Space is
 * free, so the binding is reachable on macOS too. IME-guarded the same way as `matchesDescribeChangeHotkey`.
 */
export function matchesFocusChatHotkey(event: DescribeChangeHotkeyEvent): boolean {
  if (event.isComposing || event.keyCode === 229) return false;
  const modifier = event.ctrlKey || event.metaKey;
  if (!modifier || !event.shiftKey) return false;
  return event.code === 'Space' || event.key === ' ';
}
