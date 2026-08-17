/**
 * composeHotkeys.test.ts — FR-04 (task 060, UC-5) standalone coverage for the "Describe a change"
 * hotkey predicate. Pure (no @spaarke/* imports, no editor mount) so it runs in the standalone jest
 * lane and directly proves the KNOWN failure mode: an IME composition must NOT trigger the hotkey.
 */

import { matchesDescribeChangeHotkey, type DescribeChangeHotkeyEvent } from './composeHotkeys';

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
    expect(
      matchesDescribeChangeHotkey(ev({ ctrlKey: true, code: 'Space', key: ' ', isComposing: true }))
    ).toBe(false);
  });

  it('does NOT fire for the legacy keyCode=229 composition signal', () => {
    expect(
      matchesDescribeChangeHotkey(ev({ ctrlKey: true, code: 'Space', key: ' ', keyCode: 229 }))
    ).toBe(false);
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
});
