/**
 * composeHotkeys.test.ts — FR-04 (task 060, UC-5) standalone coverage for the "Describe a change"
 * hotkey predicate. Pure (no @spaarke/* imports, no editor mount) so it runs in the standalone jest
 * lane and directly proves the KNOWN failure mode: an IME composition must NOT trigger the hotkey.
 */

import { matchesDescribeChangeHotkey, matchesFocusChatHotkey, type DescribeChangeHotkeyEvent } from './composeHotkeys';

/** Minimal event factory — only the fields the predicate reads. */
function ev(overrides: Partial<DescribeChangeHotkeyEvent>): DescribeChangeHotkeyEvent {
  return {
    ctrlKey: false,
    metaKey: false,
    code: '',
    key: '',
    isComposing: false,
    keyCode: 0,
    ...overrides,
  };
}

describe('matchesDescribeChangeHotkey — primary binding (Ctrl/Cmd+Space)', () => {
  it('matches Ctrl+Space (code=Space)', () => {
    expect(matchesDescribeChangeHotkey(ev({ ctrlKey: true, code: 'Space', key: ' ' }))).toBe(true);
  });

  it('matches Cmd+Space (metaKey)', () => {
    expect(matchesDescribeChangeHotkey(ev({ metaKey: true, code: 'Space', key: ' ' }))).toBe(true);
  });

  it('matches when only key is a space and code is absent (fallback key check)', () => {
    expect(matchesDescribeChangeHotkey(ev({ ctrlKey: true, code: '', key: ' ' }))).toBe(true);
  });
});

describe('matchesDescribeChangeHotkey — fallback binding (Ctrl/Cmd+/)', () => {
  it('matches Ctrl+/', () => {
    expect(matchesDescribeChangeHotkey(ev({ ctrlKey: true, key: '/', code: 'Slash' }))).toBe(true);
  });

  it('matches Cmd+/ (the effective macOS binding, since Cmd+Space is Spotlight)', () => {
    expect(matchesDescribeChangeHotkey(ev({ metaKey: true, key: '/', code: 'Slash' }))).toBe(true);
  });
});

describe('matchesDescribeChangeHotkey — IME guard (the known failure mode)', () => {
  it('does NOT fire during an IME composition (isComposing) even for Ctrl+Space', () => {
    expect(matchesDescribeChangeHotkey(ev({ ctrlKey: true, code: 'Space', key: ' ', isComposing: true }))).toBe(false);
  });

  it('does NOT fire for the legacy keyCode=229 composition signal', () => {
    expect(matchesDescribeChangeHotkey(ev({ ctrlKey: true, code: 'Space', key: ' ', keyCode: 229 }))).toBe(false);
  });

  it('does NOT fire during composition for the Ctrl+/ fallback either', () => {
    expect(matchesDescribeChangeHotkey(ev({ ctrlKey: true, key: '/', isComposing: true }))).toBe(false);
  });
});

describe('matchesDescribeChangeHotkey — negative cases', () => {
  it('does NOT fire for Space with no modifier (normal typing)', () => {
    expect(matchesDescribeChangeHotkey(ev({ code: 'Space', key: ' ' }))).toBe(false);
  });

  it('does NOT fire for / with no modifier (normal typing)', () => {
    expect(matchesDescribeChangeHotkey(ev({ key: '/', code: 'Slash' }))).toBe(false);
  });

  it('does NOT fire for Ctrl+F (a different shortcut)', () => {
    expect(matchesDescribeChangeHotkey(ev({ ctrlKey: true, code: 'KeyF', key: 'f' }))).toBe(false);
  });

  it('does NOT fire for a bare modifier press (Ctrl alone)', () => {
    expect(matchesDescribeChangeHotkey(ev({ ctrlKey: true, code: 'ControlLeft', key: 'Control' }))).toBe(false);
  });

  it('does NOT fire for Ctrl+SHIFT+Space (that is the focus-chat hotkey — disambiguation)', () => {
    expect(matchesDescribeChangeHotkey(ev({ ctrlKey: true, shiftKey: true, code: 'Space', key: ' ' }))).toBe(false);
  });
});

describe('matchesFocusChatHotkey — FR-05 (Ctrl/Cmd+Shift+Space)', () => {
  it('matches Ctrl+Shift+Space', () => {
    expect(matchesFocusChatHotkey(ev({ ctrlKey: true, shiftKey: true, code: 'Space', key: ' ' }))).toBe(true);
  });

  it('matches Cmd+Shift+Space', () => {
    expect(matchesFocusChatHotkey(ev({ metaKey: true, shiftKey: true, code: 'Space', key: ' ' }))).toBe(true);
  });

  it('does NOT match Ctrl+Space without Shift (that is the describe-change hotkey)', () => {
    expect(matchesFocusChatHotkey(ev({ ctrlKey: true, code: 'Space', key: ' ' }))).toBe(false);
  });

  it('does NOT fire during an IME composition even with Ctrl+Shift+Space', () => {
    expect(
      matchesFocusChatHotkey(ev({ ctrlKey: true, shiftKey: true, code: 'Space', key: ' ', isComposing: true }))
    ).toBe(false);
  });

  it('does NOT fire for the legacy keyCode=229 composition signal', () => {
    expect(matchesFocusChatHotkey(ev({ ctrlKey: true, shiftKey: true, code: 'Space', key: ' ', keyCode: 229 }))).toBe(
      false
    );
  });

  it('does NOT fire for Shift+Space with no Ctrl/Cmd modifier', () => {
    expect(matchesFocusChatHotkey(ev({ shiftKey: true, code: 'Space', key: ' ' }))).toBe(false);
  });

  it('does NOT fire for Ctrl+Shift+/ (only Space is the focus-chat key)', () => {
    expect(matchesFocusChatHotkey(ev({ ctrlKey: true, shiftKey: true, key: '/', code: 'Slash' }))).toBe(false);
  });
});
