/**
 * ComposeFormatToolbar.test.tsx — task 111 (UAT-R2, 2026-07-10) coverage for
 * the persistent top formatting toolbar, focused on the inline character-format
 * controls (Bold / Italic / Underline / Strikethrough / Link) RELOCATED here
 * from the selection BubbleMenu per the owner decision. Asserts they are
 * present, reachable, fire their TipTap toggle commands, and reflect active
 * state.
 *
 * The editor is a hand-rolled chainable mock (the same shape ComposeEditor's
 * own tests use): `editor.chain().focus().toggleBold().run()` must be
 * observable, and `editor.isActive('bold')` drives the active highlight +
 * `aria-pressed`.
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import type { Editor } from '@tiptap/react';
import { ComposeFormatToolbar } from './ComposeFormatToolbar';

// ---------------------------------------------------------------------------
// Chainable editor mock
// ---------------------------------------------------------------------------

interface MockEditorControls {
  /** Records each terminal command name run through a chain (e.g. 'toggleBold'). */
  commands: string[];
  /** Marks/attrs the mock reports active for `isActive`. */
  active: Set<string>;
  /** Link href reported by getAttributes('link'). */
  linkHref?: string;
}

/**
 * Builds a TipTap-`Editor`-shaped mock. The chain proxy records the LAST
 * non-focus/non-chain command invoked before `.run()` so a test can assert
 * which command a button fired.
 */
function createMockEditor(controls: MockEditorControls): Editor {
  const listeners = new Map<string, Set<() => void>>();

  const makeChain = () => {
    let lastCommand: string | null = null;
    const chain: Record<string, (...args: unknown[]) => unknown> = {};
    const passthrough = ['focus', 'extendMarkRange'];
    const commandNames = [
      'toggleBold',
      'toggleItalic',
      'toggleUnderline',
      'toggleStrike',
      'toggleBulletList',
      'toggleOrderedList',
      'toggleBlockquote',
      'setTextAlign',
      'toggleHeading',
      'setParagraph',
      'undo',
      'redo',
      'setLink',
      'unsetLink',
    ];
    for (const name of passthrough) {
      chain[name] = () => chain;
    }
    for (const name of commandNames) {
      chain[name] = () => {
        lastCommand = name;
        return chain;
      };
    }
    chain.run = () => {
      if (lastCommand) controls.commands.push(lastCommand);
      return true;
    };
    return chain;
  };

  const mock = {
    chain: () => makeChain(),
    isActive: (nameOrAttrs: string | Record<string, unknown>) => {
      if (typeof nameOrAttrs === 'string') return controls.active.has(nameOrAttrs);
      // textAlign object form → key like 'align:left'
      const entries = Object.entries(nameOrAttrs);
      return entries.some(([k, v]) => controls.active.has(`${k}:${String(v)}`));
    },
    getAttributes: (mark: string) => (mark === 'link' ? { href: controls.linkHref } : {}),
    can: () => ({ undo: () => true, redo: () => true }),
    on: (event: string, handler: () => void) => {
      const set = listeners.get(event) ?? new Set<() => void>();
      set.add(handler);
      listeners.set(event, set);
      return mock;
    },
    off: (event: string, handler: () => void) => {
      listeners.get(event)?.delete(handler);
      return mock;
    },
  };
  return mock as unknown as Editor;
}

function renderFormatToolbar(
  controls: Partial<MockEditorControls> = {},
  opts: { disabled?: boolean; theme?: typeof webLightTheme } = {}
) {
  const full: MockEditorControls = {
    commands: controls.commands ?? [],
    active: controls.active ?? new Set<string>(),
    linkHref: controls.linkHref,
  };
  const editor = createMockEditor(full);
  const result = render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <ComposeFormatToolbar editor={editor} disabled={opts.disabled} />
    </FluentProvider>
  );
  return { ...result, controls: full };
}

// ---------------------------------------------------------------------------
// 1. Inline character-format controls present + reachable (task 111)
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — inline character-format controls relocated here (task 111)', () => {
  it('renders Bold / Italic / Underline / Strikethrough / Link in the top toolbar', () => {
    renderFormatToolbar();
    const toolbar = screen.getByTestId('compose-format-toolbar');
    expect(screen.getByTestId('compose-format-bold')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-italic')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-underline')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-strike')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-link')).toBeInTheDocument();
    // All are inside the single persistent Toolbar (reachable, not orphaned).
    expect(toolbar).toContainElement(screen.getByTestId('compose-format-bold'));
    expect(toolbar).toContainElement(screen.getByTestId('compose-format-link'));
    // Accessible names preserved from the former BubbleMenu impl.
    expect(screen.getByLabelText('Bold')).toBeInTheDocument();
    expect(screen.getByLabelText('Italic')).toBeInTheDocument();
    expect(screen.getByLabelText('Underline')).toBeInTheDocument();
    expect(screen.getByLabelText('Strikethrough')).toBeInTheDocument();
    expect(screen.getByLabelText('Add link')).toBeInTheDocument();
  });

  it('each character-format button fires its TipTap toggle command on click', async () => {
    const user = userEvent.setup();
    const { controls } = renderFormatToolbar();

    await user.click(screen.getByTestId('compose-format-bold'));
    await user.click(screen.getByTestId('compose-format-italic'));
    await user.click(screen.getByTestId('compose-format-underline'));
    await user.click(screen.getByTestId('compose-format-strike'));

    expect(controls.commands).toEqual(['toggleBold', 'toggleItalic', 'toggleUnderline', 'toggleStrike']);
  });

  it('reflects active state via appearance="primary" + aria-pressed (isActive drives the highlight)', () => {
    renderFormatToolbar({ active: new Set(['bold', 'link']) });

    const bold = screen.getByTestId('compose-format-bold');
    const italic = screen.getByTestId('compose-format-italic');
    expect(bold).toHaveAttribute('aria-pressed', 'true');
    expect(italic).toHaveAttribute('aria-pressed', 'false');
  });
});

// ---------------------------------------------------------------------------
// 2. Link add/edit behavior (window.prompt flow preserved from BubbleMenu)
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Link add/edit (window.prompt preserved)', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('prompts for a URL and applies a link when none is active', async () => {
    const user = userEvent.setup();
    jest.spyOn(window, 'prompt').mockReturnValue('https://example.test');
    const { controls } = renderFormatToolbar();

    await user.click(screen.getByTestId('compose-format-link'));

    expect(window.prompt).toHaveBeenCalled();
    expect(controls.commands).toContain('setLink');
  });

  it('shows "Remove link" and unsets the link when a link is already active', async () => {
    const user = userEvent.setup();
    const { controls } = renderFormatToolbar({ active: new Set(['link']), linkHref: 'https://old.test' });

    expect(screen.getByLabelText('Remove link')).toBeInTheDocument();
    await user.click(screen.getByTestId('compose-format-link'));
    expect(controls.commands).toContain('unsetLink');
  });

  it('cancelling the prompt (null) applies no command', async () => {
    const user = userEvent.setup();
    jest.spyOn(window, 'prompt').mockReturnValue(null);
    const { controls } = renderFormatToolbar();

    await user.click(screen.getByTestId('compose-format-link'));
    expect(controls.commands).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// 3. Existing block-format controls still reachable (no regression)
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — DEF-16 pinned/sticky top toolbar', () => {
  it('is position: sticky at top:0 so it stays visible while the document body scrolls', () => {
    renderFormatToolbar();
    const toolbar = screen.getByTestId('compose-format-toolbar');
    const style = getComputedStyle(toolbar);
    expect(style.position).toBe('sticky');
    expect(style.top).toBe('0px');
  });
});

describe('ComposeFormatToolbar — existing block controls unregressed', () => {
  it('still renders lists/blockquote/align/undo-redo/heading menu', () => {
    renderFormatToolbar();
    expect(screen.getByTestId('compose-format-heading-menu')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-bullet-list')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-ordered-list')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-blockquote')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-align-left')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-undo')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-redo')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// 4. Dark mode (ADR-021)
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — dark mode (ADR-021)', () => {
  it('renders under a dark theme with no hardcoded hex color', () => {
    const { container } = renderFormatToolbar({}, { theme: webDarkTheme });
    expect(screen.getByTestId('compose-format-bold')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});
