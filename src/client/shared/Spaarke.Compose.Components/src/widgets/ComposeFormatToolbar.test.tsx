/**
 * ComposeFormatToolbar.test.tsx — FIX #5 (spaarkeai-compose-r2 UAT) coverage for
 * the CONSOLIDATED single-row toolbar. The former two wrapping rows of icon
 * buttons are now grouped behind labelled Fluent v9 `Menu` dropdowns (Body /
 * Paragraph / Font / Word); Save + Undo/Redo are icon-only buttons pushed to the
 * right edge. The tools inside each dropdown are ICON-only (with a Tooltip name).
 *
 * These tests replace the pre-FIX-#5 assertions that expected the character-format
 * controls to sit DIRECTLY on the toolbar row — they now live inside the "Font"
 * dropdown popover (portaled), reachable after opening the menu. Command wiring,
 * active-state highlight, disabled state, and the Link `window.prompt` flow are
 * asserted unchanged.
 *
 * The editor is a hand-rolled chainable mock (same shape ComposeEditor's own tests
 * use): `editor.chain().focus().toggleBold().run()` must be observable, and
 * `editor.isActive('bold')` drives the active highlight + `aria-pressed`.
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import type { Editor } from '@tiptap/react';
import { ComposeFormatToolbar, type ComposeFormatToolbarProps } from './ComposeFormatToolbar';

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
 * non-focus/non-chain command invoked before `.run()` so a test can assert which
 * command a button fired.
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
  opts: { theme?: typeof webLightTheme; props?: Partial<ComposeFormatToolbarProps> } = {}
) {
  const full: MockEditorControls = {
    commands: controls.commands ?? [],
    active: controls.active ?? new Set<string>(),
    linkHref: controls.linkHref,
  };
  const editor = createMockEditor(full);
  const result = render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <ComposeFormatToolbar editor={editor} {...opts.props} />
    </FluentProvider>
  );
  return { ...result, controls: full };
}

// ---------------------------------------------------------------------------
// 1. Single-row structure — labelled dropdowns + right-aligned Save/Undo/Redo
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — FIX #5 single-row consolidated structure', () => {
  it('renders exactly ONE toolbar row with the Body / Paragraph / Font dropdown triggers', () => {
    renderFormatToolbar();
    const toolbars = screen.getAllByRole('toolbar');
    expect(toolbars).toHaveLength(1);
    expect(toolbars[0]).toBe(screen.getByTestId('compose-format-toolbar'));

    expect(screen.getByTestId('compose-format-heading-menu')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-paragraph-menu')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-font-menu')).toBeInTheDocument();
    // Undo/Redo remain always-visible icon buttons on the row.
    expect(screen.getByTestId('compose-format-undo')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-redo')).toBeInTheDocument();
  });

  it('the toolbar is a single non-wrapping row (flex-wrap: nowrap)', () => {
    renderFormatToolbar();
    expect(getComputedStyle(screen.getByTestId('compose-format-toolbar')).flexWrap).toBe('nowrap');
  });

  it('the Word dropdown is hidden when no Word handlers are wired, shown when they are', () => {
    const { unmount } = renderFormatToolbar();
    expect(screen.queryByTestId('compose-format-word-menu')).not.toBeInTheDocument();
    unmount();

    renderFormatToolbar({}, { props: { onOpenInWord: jest.fn(), onOpenInWordDesktop: jest.fn() } });
    expect(screen.getByTestId('compose-format-word-menu')).toBeInTheDocument();
  });

  it('the Save button is hidden without an onSave handler, shown with one', () => {
    const { unmount } = renderFormatToolbar();
    expect(screen.queryByTestId('compose-format-save')).not.toBeInTheDocument();
    unmount();

    renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: true } });
    expect(screen.getByTestId('compose-format-save')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// 2. Font dropdown — character-format controls (icons, reachable, fire commands)
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Font dropdown (relocated character formatting)', () => {
  it('opening Font reveals Bold / Italic / Underline / Strikethrough / Link (icon-only)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar();
    await user.click(screen.getByTestId('compose-format-font-menu'));

    expect(screen.getByTestId('compose-format-bold')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-italic')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-underline')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-strike')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-link')).toBeInTheDocument();
    // Accessible names preserved (Tooltip hover names + aria-label).
    expect(screen.getByLabelText('Bold')).toBeInTheDocument();
    expect(screen.getByLabelText('Add link')).toBeInTheDocument();
  });

  it('each character-format button fires its TipTap toggle command on click', async () => {
    const user = userEvent.setup();
    const { controls } = renderFormatToolbar();
    await user.click(screen.getByTestId('compose-format-font-menu'));

    await user.click(screen.getByTestId('compose-format-bold'));
    await user.click(screen.getByTestId('compose-format-italic'));
    await user.click(screen.getByTestId('compose-format-underline'));
    await user.click(screen.getByTestId('compose-format-strike'));

    expect(controls.commands).toEqual(['toggleBold', 'toggleItalic', 'toggleUnderline', 'toggleStrike']);
  });

  it('reflects active state via aria-pressed (isActive drives the highlight)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({ active: new Set(['bold', 'link']) });
    await user.click(screen.getByTestId('compose-format-font-menu'));

    expect(screen.getByTestId('compose-format-bold')).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByTestId('compose-format-italic')).toHaveAttribute('aria-pressed', 'false');
  });
});

// ---------------------------------------------------------------------------
// 3. Link add/edit (window.prompt flow preserved)
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Link add/edit (window.prompt preserved)', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('prompts for a URL and applies a link when none is active', async () => {
    const user = userEvent.setup();
    jest.spyOn(window, 'prompt').mockReturnValue('https://example.test');
    const { controls } = renderFormatToolbar();
    await user.click(screen.getByTestId('compose-format-font-menu'));

    await user.click(screen.getByTestId('compose-format-link'));

    expect(window.prompt).toHaveBeenCalled();
    expect(controls.commands).toContain('setLink');
  });

  it('shows "Remove link" and unsets the link when a link is already active', async () => {
    const user = userEvent.setup();
    const { controls } = renderFormatToolbar({ active: new Set(['link']), linkHref: 'https://old.test' });
    await user.click(screen.getByTestId('compose-format-font-menu'));

    expect(screen.getByLabelText('Remove link')).toBeInTheDocument();
    await user.click(screen.getByTestId('compose-format-link'));
    expect(controls.commands).toContain('unsetLink');
  });

  it('cancelling the prompt (null) applies no command', async () => {
    const user = userEvent.setup();
    jest.spyOn(window, 'prompt').mockReturnValue(null);
    const { controls } = renderFormatToolbar();
    await user.click(screen.getByTestId('compose-format-font-menu'));

    await user.click(screen.getByTestId('compose-format-link'));
    expect(controls.commands).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// 4. Paragraph dropdown — lists / blockquote / alignment still reachable
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Paragraph dropdown (lists / blockquote / align)', () => {
  it('opening Paragraph reveals list/blockquote/align controls that fire their commands', async () => {
    const user = userEvent.setup();
    const { controls } = renderFormatToolbar();
    await user.click(screen.getByTestId('compose-format-paragraph-menu'));

    expect(screen.getByTestId('compose-format-bullet-list')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-ordered-list')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-blockquote')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-align-left')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-align-center')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-align-right')).toBeInTheDocument();

    await user.click(screen.getByTestId('compose-format-bullet-list'));
    await user.click(screen.getByTestId('compose-format-align-center'));
    expect(controls.commands).toEqual(['toggleBulletList', 'setTextAlign']);
  });
});

// ---------------------------------------------------------------------------
// 5. Word dropdown — Open-in-Word Web/Desktop + Push to Word wired
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Word dropdown (host-bound handlers)', () => {
  it('opening Word reveals the three actions and each fires its handler', async () => {
    const user = userEvent.setup();
    const onOpenInWord = jest.fn();
    const onOpenInWordDesktop = jest.fn();
    const onPushToWord = jest.fn();
    renderFormatToolbar(
      {},
      { props: { onOpenInWord, onOpenInWordDesktop, onPushToWord, canPushToWord: true } }
    );

    await user.click(screen.getByTestId('compose-format-word-menu'));

    await user.click(screen.getByTestId('compose-format-open-word-web'));
    await user.click(screen.getByTestId('compose-format-open-word-desktop'));
    await user.click(screen.getByTestId('compose-format-push-to-word'));

    expect(onOpenInWord).toHaveBeenCalledTimes(1);
    expect(onOpenInWordDesktop).toHaveBeenCalledTimes(1);
    expect(onPushToWord).toHaveBeenCalledTimes(1);
  });

  it('Open-in-Word items are disabled when wordActionsDisabled is set; Push is disabled without canPushToWord', async () => {
    const user = userEvent.setup();
    renderFormatToolbar(
      {},
      {
        props: {
          onOpenInWord: jest.fn(),
          onOpenInWordDesktop: jest.fn(),
          onPushToWord: jest.fn(),
          wordActionsDisabled: true,
          canPushToWord: false,
        },
      }
    );
    await user.click(screen.getByTestId('compose-format-word-menu'));

    expect(screen.getByTestId('compose-format-open-word-web')).toBeDisabled();
    expect(screen.getByTestId('compose-format-open-word-desktop')).toBeDisabled();
    expect(screen.getByTestId('compose-format-push-to-word')).toBeDisabled();
  });
});

// ---------------------------------------------------------------------------
// 6. Save button — right-aligned icon button honoring canSave / isSaving
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Save button', () => {
  it('is enabled and fires onSave when canSave is true', async () => {
    const user = userEvent.setup();
    const onSave = jest.fn();
    renderFormatToolbar({}, { props: { onSave, canSave: true } });

    const save = screen.getByTestId('compose-format-save');
    expect(save).not.toBeDisabled();
    await user.click(save);
    expect(onSave).toHaveBeenCalledTimes(1);
  });

  it('is disabled when canSave is false or a save is in flight', () => {
    const { unmount } = renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: false } });
    expect(screen.getByTestId('compose-format-save')).toBeDisabled();
    unmount();

    renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: true, isSaving: true } });
    expect(screen.getByTestId('compose-format-save')).toBeDisabled();
  });
});

// ---------------------------------------------------------------------------
// 7. Sticky pin + disabled-all + dark mode
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — sticky pin, global disable, dark mode', () => {
  it('DEF-16: is position: sticky at top:0 so it stays visible while the body scrolls', () => {
    renderFormatToolbar();
    const style = getComputedStyle(screen.getByTestId('compose-format-toolbar'));
    expect(style.position).toBe('sticky');
    expect(style.top).toBe('0px');
  });

  it('disabled prop disables the dropdown triggers and undo/redo', () => {
    renderFormatToolbar({}, { props: { disabled: true } });
    expect(screen.getByTestId('compose-format-heading-menu')).toBeDisabled();
    expect(screen.getByTestId('compose-format-paragraph-menu')).toBeDisabled();
    expect(screen.getByTestId('compose-format-font-menu')).toBeDisabled();
    expect(screen.getByTestId('compose-format-undo')).toBeDisabled();
  });

  it('ADR-021: renders under a dark theme with no hardcoded hex color', () => {
    const { container } = renderFormatToolbar({}, { theme: webDarkTheme });
    expect(screen.getByTestId('compose-format-toolbar')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});
