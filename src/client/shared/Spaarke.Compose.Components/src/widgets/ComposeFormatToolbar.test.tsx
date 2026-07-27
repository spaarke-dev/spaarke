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
// 3. Link — DISABLED in both modes (task 038; supersedes the old window.prompt flow).
//    Hyperlinks are not representable in R4 (no mark op, no content-model href — R5 G5),
//    so the control is present-but-disabled and fires neither the prompt nor a command.
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Link disabled in both modes (task 038)', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('the link button is present but DISABLED and does NOT open the URL prompt when clicked', async () => {
    const user = userEvent.setup();
    const promptSpy = jest.spyOn(window, 'prompt').mockReturnValue('https://example.test');
    const { controls } = renderFormatToolbar();
    await user.click(screen.getByTestId('compose-format-font-menu'));

    const link = screen.getByTestId('compose-format-link');
    expect(link).toBeDisabled();
    // Clicking a disabled control fires no prompt and no TipTap command.
    await user.click(link);
    expect(promptSpy).not.toHaveBeenCalled();
    expect(controls.commands).toHaveLength(0);
  });

  it('the link button stays DISABLED even when a link mark is active (no "Remove link" command)', async () => {
    const user = userEvent.setup();
    const { controls } = renderFormatToolbar({ active: new Set(['link']), linkHref: 'https://old.test' });
    await user.click(screen.getByTestId('compose-format-font-menu'));

    const link = screen.getByTestId('compose-format-link');
    expect(link).toBeDisabled();
    await user.click(link);
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
// 5. Word dropdown — Open-in-Word Web/Desktop wired
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Word dropdown (host-bound handlers)', () => {
  it('opening Word reveals the Open-in-Word actions and each fires its handler', async () => {
    const user = userEvent.setup();
    const onOpenInWord = jest.fn();
    const onOpenInWordDesktop = jest.fn();
    renderFormatToolbar({}, { props: { onOpenInWord, onOpenInWordDesktop } });

    await user.click(screen.getByTestId('compose-format-word-menu'));

    await user.click(screen.getByTestId('compose-format-open-word-web'));
    await user.click(screen.getByTestId('compose-format-open-word-desktop'));

    expect(onOpenInWord).toHaveBeenCalledTimes(1);
    expect(onOpenInWordDesktop).toHaveBeenCalledTimes(1);
  });

  it('Open-in-Word items are disabled when wordActionsDisabled is set', async () => {
    const user = userEvent.setup();
    renderFormatToolbar(
      {},
      {
        props: {
          onOpenInWord: jest.fn(),
          onOpenInWordDesktop: jest.fn(),
          wordActionsDisabled: true,
        },
      }
    );
    await user.click(screen.getByTestId('compose-format-word-menu'));

    expect(screen.getByTestId('compose-format-open-word-web')).toBeDisabled();
    expect(screen.getByTestId('compose-format-open-word-desktop')).toBeDisabled();
  });
});

// ---------------------------------------------------------------------------
// 5b. Table dropdown — Insert table INVERTED to born-in-editor-only (task 038,
//     supersedes task 037: the renderer authors born-in-editor tables cleanly;
//     the engine silently drops loaded-doc tables — so the polarity is flipped).
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Table insert INVERTED to born-in-editor-only (task 038)', () => {
  it('Insert table is ENABLED by default (hasLoadedBaseline omitted ⇒ born-in-editor treatment ⇒ enabled)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar();
    await user.click(screen.getByTestId('compose-format-table-menu'));
    // Regression guard: existing callers that never pass the prop keep table authoring.
    expect(screen.getByTestId('compose-format-table-insert')).not.toBeDisabled();
  });

  it('Insert table is ENABLED in BORN-IN-EDITOR mode (hasLoadedBaseline=false — renderer authors tables cleanly)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { hasLoadedBaseline: false } });
    await user.click(screen.getByTestId('compose-format-table-menu'));
    expect(screen.getByTestId('compose-format-table-insert')).not.toBeDisabled();
  });

  it('Insert table is DISABLED on a LOADED doc (hasLoadedBaseline=true — engine has no table op, would silently drop)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { hasLoadedBaseline: true } });
    await user.click(screen.getByTestId('compose-format-table-menu'));
    // task 038 inverts task 037: a loaded/imported doc cannot have a NEW table inserted (SDL-3).
    expect(screen.getByTestId('compose-format-table-insert')).toBeDisabled();
  });
});

// ---------------------------------------------------------------------------
// 5c. Deferred edit-path controls (task 038 zero-error guardrails) — alignment /
//     heading / list disabled on a LOADED doc, enabled born-in-editor; hyperlink
//     disabled in BOTH modes. Maps to failure modes ET-1 (alignment), SDL-1/2
//     (heading/list), SDL-4/5 (hyperlink).
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — deferred edit-path controls gated on a LOADED doc (task 038)', () => {
  it('on a LOADED doc, the heading dropdown is DISABLED (SDL-1)', () => {
    renderFormatToolbar({}, { props: { hasLoadedBaseline: true } });
    // Rendered as a disabled, tooltip-bearing button (no openable menu) on a loaded doc.
    expect(screen.getByTestId('compose-format-heading-menu')).toBeDisabled();
  });

  it('on a LOADED doc, the bullet + numbered list buttons are DISABLED (SDL-2)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { hasLoadedBaseline: true } });
    // The Paragraph trigger stays enabled (blockquote is still reachable) — open it, then assert.
    await user.click(screen.getByTestId('compose-format-paragraph-menu'));
    expect(screen.getByTestId('compose-format-bullet-list')).toBeDisabled();
    expect(screen.getByTestId('compose-format-ordered-list')).toBeDisabled();
  });

  it('on a LOADED doc, the alignment buttons are DISABLED (ET-1)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { hasLoadedBaseline: true } });
    await user.click(screen.getByTestId('compose-format-paragraph-menu'));
    expect(screen.getByTestId('compose-format-align-left')).toBeDisabled();
    expect(screen.getByTestId('compose-format-align-center')).toBeDisabled();
    expect(screen.getByTestId('compose-format-align-right')).toBeDisabled();
  });

  it('on a BORN-IN-EDITOR doc, heading / list / alignment are all ENABLED', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { hasLoadedBaseline: false } });
    // Heading renders as an openable menu trigger (not the disabled loaded-doc button).
    expect(screen.getByTestId('compose-format-heading-menu')).not.toBeDisabled();
    await user.click(screen.getByTestId('compose-format-paragraph-menu'));
    expect(screen.getByTestId('compose-format-bullet-list')).not.toBeDisabled();
    expect(screen.getByTestId('compose-format-ordered-list')).not.toBeDisabled();
    expect(screen.getByTestId('compose-format-align-left')).not.toBeDisabled();
    expect(screen.getByTestId('compose-format-align-center')).not.toBeDisabled();
    expect(screen.getByTestId('compose-format-align-right')).not.toBeDisabled();
  });

  it('the hyperlink button is DISABLED in BOTH modes (SDL-4/5 — links not representable in R4)', async () => {
    const user = userEvent.setup();
    // Born-in-editor.
    const { unmount } = renderFormatToolbar({}, { props: { hasLoadedBaseline: false } });
    await user.click(screen.getByTestId('compose-format-font-menu'));
    expect(screen.getByTestId('compose-format-link')).toBeDisabled();
    unmount();

    // Loaded.
    renderFormatToolbar({}, { props: { hasLoadedBaseline: true } });
    await user.click(screen.getByTestId('compose-format-font-menu'));
    expect(screen.getByTestId('compose-format-link')).toBeDisabled();
  });

  it('blockquote stays ENABLED on a loaded doc (not in the deferred set — the paste banner is its safety net)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { hasLoadedBaseline: true } });
    await user.click(screen.getByTestId('compose-format-paragraph-menu'));
    expect(screen.getByTestId('compose-format-blockquote')).not.toBeDisabled();
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
// 6b. Track Changes toggle (item 4, UAT round-4)
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Track Changes toggle (item 4)', () => {
  it('is not rendered when no onToggleTrackChanges handler is wired', () => {
    renderFormatToolbar();
    expect(screen.queryByTestId('compose-format-track-changes')).not.toBeInTheDocument();
  });

  it('renders and fires onToggleTrackChanges when clicked', async () => {
    const user = userEvent.setup();
    const onToggleTrackChanges = jest.fn();
    renderFormatToolbar({}, { props: { onToggleTrackChanges, trackChangesEnabled: false } });

    const toggle = screen.getByTestId('compose-format-track-changes');
    expect(toggle).toHaveAttribute('aria-pressed', 'false');
    await user.click(toggle);
    expect(onToggleTrackChanges).toHaveBeenCalledTimes(1);
  });

  it('reflects the ON state via aria-pressed', () => {
    renderFormatToolbar({}, { props: { onToggleTrackChanges: jest.fn(), trackChangesEnabled: true } });
    expect(screen.getByTestId('compose-format-track-changes')).toHaveAttribute('aria-pressed', 'true');
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

// ---------------------------------------------------------------------------
// Review dropdown (ai-advanced-capabilities-nda-r1 UAT round-2 items #1/#2)
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Review dropdown', () => {
  const reviewProps = (over: Partial<ComposeFormatToolbarProps> = {}): Partial<ComposeFormatToolbarProps> => ({
    hasReview: true,
    reviewSummaryOpen: false,
    reviewNotesOpen: true,
    onToggleReviewSummary: jest.fn(),
    onToggleReviewNotes: jest.fn(),
    ...over,
  });

  it('is hidden when no review is present (hasReview falsy)', () => {
    renderFormatToolbar({}, { props: reviewProps({ hasReview: false }) });
    expect(screen.queryByTestId('compose-format-review-menu')).not.toBeInTheDocument();
  });

  it('is hidden when the toggle handlers are not wired', () => {
    renderFormatToolbar({}, { props: { hasReview: true } });
    expect(screen.queryByTestId('compose-format-review-menu')).not.toBeInTheDocument();
  });

  it('shows the icon-only Review trigger when a review is present and handlers are wired', () => {
    renderFormatToolbar({}, { props: reviewProps() });
    const trigger = screen.getByTestId('compose-format-review-menu');
    expect(trigger).toBeInTheDocument();
    expect(trigger).toHaveAttribute('aria-label', 'Review');
  });

  it('toggling "Review Summary" fires onToggleReviewSummary (and not Notes)', async () => {
    const user = userEvent.setup();
    const onToggleReviewSummary = jest.fn();
    const onToggleReviewNotes = jest.fn();
    renderFormatToolbar({}, { props: reviewProps({ onToggleReviewSummary, onToggleReviewNotes }) });

    await user.click(screen.getByTestId('compose-format-review-menu'));
    await user.click(await screen.findByTestId('compose-format-review-summary-toggle'));

    expect(onToggleReviewSummary).toHaveBeenCalledTimes(1);
    expect(onToggleReviewNotes).not.toHaveBeenCalled();
  });

  it('toggling "Review Notes" fires onToggleReviewNotes (and not Summary)', async () => {
    const user = userEvent.setup();
    const onToggleReviewSummary = jest.fn();
    const onToggleReviewNotes = jest.fn();
    renderFormatToolbar({}, { props: reviewProps({ onToggleReviewSummary, onToggleReviewNotes }) });

    await user.click(screen.getByTestId('compose-format-review-menu'));
    await user.click(await screen.findByTestId('compose-format-review-notes-toggle'));

    expect(onToggleReviewNotes).toHaveBeenCalledTimes(1);
    expect(onToggleReviewSummary).not.toHaveBeenCalled();
  });
});
