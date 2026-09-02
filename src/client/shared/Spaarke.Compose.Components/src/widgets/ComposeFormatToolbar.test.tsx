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
import { render, screen, within } from '@testing-library/react';
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

describe('ComposeFormatToolbar — Link ENABLED both modes (G5 task 033)', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('the link button is ENABLED and opens the URL prompt + fires setLink when clicked', async () => {
    const user = userEvent.setup();
    const promptSpy = jest.spyOn(window, 'prompt').mockReturnValue('https://example.test');
    const { controls } = renderFormatToolbar();
    await user.click(screen.getByTestId('compose-format-font-menu'));

    const link = screen.getByTestId('compose-format-link');
    expect(link).not.toBeDisabled();
    await user.click(link);
    expect(promptSpy).toHaveBeenCalled();
    expect(controls.commands).toContain('setLink');
  });

  it('when a link mark is active, clicking removes it (unsetLink, no prompt)', async () => {
    const user = userEvent.setup();
    const promptSpy = jest.spyOn(window, 'prompt');
    const { controls } = renderFormatToolbar({ active: new Set(['link']), linkHref: 'https://old.test' });
    await user.click(screen.getByTestId('compose-format-font-menu'));

    const link = screen.getByTestId('compose-format-link');
    expect(link).not.toBeDisabled();
    await user.click(link);
    expect(promptSpy).not.toHaveBeenCalled(); // an active link removes directly, no URL prompt
    expect(controls.commands).toContain('unsetLink');
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
// 5c. Deferred edit-path controls (task 038 zero-error guardrails) — heading /
//     list disabled on a LOADED doc, enabled born-in-editor; hyperlink disabled
//     in BOTH modes. Maps to failure modes SDL-1/2 (heading/list), SDL-4/5
//     (hyperlink). ET-1 (alignment) was RE-ENABLED on loaded docs by R5 task 010
//     (G3 alignment applier — ComposeShadowPatchEngine now emits a tracked
//     w:pPrChange) — see the alignment-specific describe block below.
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — edit-path controls on a LOADED doc (task 038; heading/list re-enabled by R5 task 011)', () => {
  it('on a LOADED doc, the heading dropdown is ENABLED (R5 task 011 — SDL-1 guard removed)', () => {
    renderFormatToolbar({}, { props: { hasLoadedBaseline: true } });
    // The engine now applies a setBlockAttr Style op as a tracked w:pPrChange — the menu is openable.
    expect(screen.getByTestId('compose-format-heading-menu')).not.toBeDisabled();
  });

  it('on a LOADED doc, the bullet + numbered list buttons are ENABLED (R5 task 011 — SDL-2 guard removed)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { hasLoadedBaseline: true } });
    await user.click(screen.getByTestId('compose-format-paragraph-menu'));
    expect(screen.getByTestId('compose-format-bullet-list')).not.toBeDisabled();
    expect(screen.getByTestId('compose-format-ordered-list')).not.toBeDisabled();
  });

  it('on a BORN-IN-EDITOR doc, heading / list are ENABLED', async () => {
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

  it('the hyperlink button is ENABLED in BOTH modes (G5 task 033 — SDL-4/5 guard removed)', async () => {
    const user = userEvent.setup();
    // Born-in-editor (authored clean w:hyperlink path).
    const { unmount } = renderFormatToolbar({}, { props: { hasLoadedBaseline: false } });
    await user.click(screen.getByTestId('compose-format-font-menu'));
    expect(screen.getByTestId('compose-format-link')).not.toBeDisabled();
    unmount();

    // Loaded (tracked w:hyperlink edit path).
    renderFormatToolbar({}, { props: { hasLoadedBaseline: true } });
    await user.click(screen.getByTestId('compose-format-font-menu'));
    expect(screen.getByTestId('compose-format-link')).not.toBeDisabled();
  });

  it('blockquote stays ENABLED on a loaded doc (not in the deferred set — the paste banner is its safety net)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { hasLoadedBaseline: true } });
    await user.click(screen.getByTestId('compose-format-paragraph-menu'));
    expect(screen.getByTestId('compose-format-blockquote')).not.toBeDisabled();
  });
});

// ---------------------------------------------------------------------------
// 5d. Alignment re-enabled on a LOADED doc (R5 task 010 — G3 alignment applier,
//     removes the R4 ET-1 guard). ComposeShadowPatchEngine now applies a
//     setBlockAttr Alignment op as a tracked w:pPrChange, so the alignment
//     buttons are gated ONLY by the read-only `disabled` prop, independent of
//     `hasLoadedBaseline`. Heading/list joined this set in R5 task 011; only
//     table-insert stays deferred (to R5 task 014).
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — alignment controls (R5 task 010, ET-1 guard removed)', () => {
  it('on a LOADED doc, the alignment buttons are ENABLED', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { hasLoadedBaseline: true } });
    await user.click(screen.getByTestId('compose-format-paragraph-menu'));
    expect(screen.getByTestId('compose-format-align-left')).not.toBeDisabled();
    expect(screen.getByTestId('compose-format-align-center')).not.toBeDisabled();
    expect(screen.getByTestId('compose-format-align-right')).not.toBeDisabled();
    // Heading/list are ALSO re-enabled on the SAME loaded doc (R5 task 011); only table stays deferred (task 014).
    expect(screen.getByTestId('compose-format-bullet-list')).not.toBeDisabled();
    expect(screen.getByTestId('compose-format-ordered-list')).not.toBeDisabled();
  });

  it('on a BORN-IN-EDITOR doc, the alignment buttons are ENABLED (unchanged)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { hasLoadedBaseline: false } });
    await user.click(screen.getByTestId('compose-format-paragraph-menu'));
    expect(screen.getByTestId('compose-format-align-left')).not.toBeDisabled();
    expect(screen.getByTestId('compose-format-align-center')).not.toBeDisabled();
    expect(screen.getByTestId('compose-format-align-right')).not.toBeDisabled();
  });

  it('the alignment buttons are unreachable when the toolbar is read-only (Paragraph trigger itself disabled), on either doc mode', () => {
    const { unmount } = renderFormatToolbar({}, { props: { hasLoadedBaseline: true, disabled: true } });
    expect(screen.getByTestId('compose-format-paragraph-menu')).toBeDisabled();
    unmount();

    renderFormatToolbar({}, { props: { hasLoadedBaseline: false, disabled: true } });
    expect(screen.getByTestId('compose-format-paragraph-menu')).toBeDisabled();
  });
});

// ---------------------------------------------------------------------------
// 6. Save split-button (G7 task 022) — "Save Version" primary + "Save New Document" menu item
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Save / Save As dropdown + Auto Save (FR-01 task 020)', () => {
  // The SplitButton root carries the testid; its first inner <button> is the primary action.
  const getPrimary = () => within(screen.getByTestId('compose-format-save')).getAllByRole('button')[0];

  it('renders "Save" as the primary action and Save + Save As in the caret menu', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: true } });

    // Primary action = Save (append an SPE version — ADR-049).
    expect(getPrimary()).toHaveAccessibleName(/^save$/i);
    // The caret menu carries both the explicit Save and the Save As (fork) items.
    await user.click(screen.getByRole('button', { name: /save options/i }));
    expect(await screen.findByTestId('compose-format-save-version')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-save-new')).toHaveTextContent(/save as/i);
  });

  it('the primary action fires onSave("version")', async () => {
    const user = userEvent.setup();
    const onSave = jest.fn();
    renderFormatToolbar({}, { props: { onSave, canSave: true } });

    await user.click(getPrimary());
    expect(onSave).toHaveBeenCalledWith('version');
  });

  it('the caret-menu "Save" item fires onSave("version")', async () => {
    const user = userEvent.setup();
    const onSave = jest.fn();
    renderFormatToolbar({}, { props: { onSave, canSave: true } });

    await user.click(screen.getByRole('button', { name: /save options/i }));
    await user.click(await screen.findByTestId('compose-format-save-version'));
    expect(onSave).toHaveBeenCalledWith('version');
  });

  it('the caret-menu "Save As" fires onSave("new") — a real fork per task 012', async () => {
    const user = userEvent.setup();
    const onSave = jest.fn();
    renderFormatToolbar({}, { props: { onSave, canSave: true } });

    await user.click(screen.getByRole('button', { name: /save options/i }));
    await user.click(await screen.findByTestId('compose-format-save-new'));
    expect(onSave).toHaveBeenCalledWith('new');
  });

  it('renders the Auto Save toggle (checked) when the host wired autosave, and fires onAutoSaveToggle', async () => {
    const user = userEvent.setup();
    const onAutoSaveToggle = jest.fn();
    renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: true, autoSaveEnabled: true, onAutoSaveToggle } });

    await user.click(screen.getByRole('button', { name: /save options/i }));
    const toggle = await screen.findByTestId('compose-format-autosave-toggle');
    // UAT round 2 #6: the label is "Keep recovery draft", NOT "Auto Save". The toggle controls a
    // localStorage recovery draft that never reaches the BFF, and sat directly under Save/Save As
    // where the old wording read as a third save mode. Asserting the literal copy is deliberate —
    // a /save/i-style regex would pass on a relapse to the misleading label.
    expect(toggle).toHaveTextContent('Keep recovery draft');
    expect(toggle).not.toHaveTextContent(/auto ?save/i);
    expect(toggle).toHaveAttribute('aria-checked', 'true'); // reflects autoSaveEnabled
    await user.click(toggle);
    expect(onAutoSaveToggle).toHaveBeenCalledWith(false); // toggling an ON item turns it OFF
  });

  it('does NOT render the Auto Save toggle when the host has not wired autosave', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: true } }); // no autoSaveEnabled/onAutoSaveToggle

    await user.click(screen.getByRole('button', { name: /save options/i }));
    await screen.findByTestId('compose-format-save-new');
    expect(screen.queryByTestId('compose-format-autosave-toggle')).not.toBeInTheDocument();
  });

  it('the primary action is disabled when canSave is false or a save is in flight', () => {
    const { unmount } = renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: false } });
    expect(getPrimary()).toBeDisabled();
    unmount();

    renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: true, isSaving: true } });
    expect(getPrimary()).toBeDisabled();
  });

  it('ADR-021: renders the dropdown under a dark theme (theme tokens, no crash)', () => {
    renderFormatToolbar({}, { theme: webDarkTheme, props: { onSave: jest.fn(), canSave: true } });
    expect(getPrimary()).toHaveAccessibleName(/^save$/i);
  });
});

// ---------------------------------------------------------------------------
// 6a. Refresh Profile button (G10 task 040)
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Refresh profile (UAT round 2 #5h: moved into the Save menu)', () => {
  /** Open the Save split-button's caret menu, where Refresh profile now lives. */
  async function openSaveMenu(user: ReturnType<typeof userEvent.setup>): Promise<void> {
    await user.click(screen.getByRole('button', { name: /save options/i }));
  }

  it('is not rendered when no onRefreshProfile handler is wired (unpromoted doc)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { onSave: jest.fn() } });
    await openSaveMenu(user);
    expect(screen.queryByTestId('compose-format-refresh-profile')).not.toBeInTheDocument();
  });

  it('renders inside the Save menu and fires onRefreshProfile when clicked (promoted doc)', async () => {
    const user = userEvent.setup();
    const onRefreshProfile = jest.fn();
    renderFormatToolbar({}, { props: { onSave: jest.fn(), onRefreshProfile } });

    // Not on the toolbar row any more — it is a Save-menu item now.
    expect(screen.queryByTestId('compose-format-refresh-profile')).not.toBeInTheDocument();
    await openSaveMenu(user);
    const item = screen.getByTestId('compose-format-refresh-profile');
    expect(item).toHaveTextContent('Refresh profile');
    await user.click(item);
    expect(onRefreshProfile).toHaveBeenCalledTimes(1);
  });

  it('ADR-021: renders under a dark theme', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { theme: webDarkTheme, props: { onSave: jest.fn(), onRefreshProfile: jest.fn() } });
    await openSaveMenu(user);
    expect(screen.getByTestId('compose-format-refresh-profile')).toBeInTheDocument();
  });

  it('UAT #9 (task 054): disables the item and shows the in-flight label while a refresh is running', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { onSave: jest.fn(), onRefreshProfile: jest.fn(), isRefreshingProfile: true } });
    await openSaveMenu(user);
    const item = screen.getByTestId('compose-format-refresh-profile');
    expect(item.getAttribute('aria-disabled') === 'true' || (item as HTMLButtonElement).disabled).toBe(true);
    expect(item).toHaveTextContent('Refreshing profile…');
  });

  it('DOCUMENTED COUPLING: moving it into the Save menu makes it unreachable without a wired onSave', async () => {
    // Recorded rather than hidden. The Save menu is the container, so a host that wires
    // onRefreshProfile but NOT onSave now has no way to reach the action. ComposeWorkspace always wires
    // both, so this is not a live defect — but if that ever stops being true, this test says why the
    // action vanished instead of leaving someone to find it the hard way.
    renderFormatToolbar({}, { props: { onRefreshProfile: jest.fn() } });
    expect(screen.queryByRole('button', { name: /save options/i })).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-format-refresh-profile')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// UAT #5 (task 053) — "Reload from source" button. Distinct from Refresh Profile:
// it reloads the latest SPE bytes on demand; the host wires it only for a doc with an SPE source.
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Reload from source button (UAT #5 task 053)', () => {
  it('is not rendered when no onReloadFromSource handler is wired (born-in-editor / no SPE source)', () => {
    renderFormatToolbar();
    expect(screen.queryByTestId('compose-format-reload-from-source')).not.toBeInTheDocument();
  });

  it('renders and fires onReloadFromSource when clicked (doc with an SPE source)', async () => {
    const user = userEvent.setup();
    const onReloadFromSource = jest.fn();
    renderFormatToolbar({}, { props: { onReloadFromSource } });

    const btn = screen.getByTestId('compose-format-reload-from-source');
    expect(btn).toBeInTheDocument();
    await user.click(btn);
    expect(onReloadFromSource).toHaveBeenCalledTimes(1);
  });

  it('is distinct from Refresh profile — Reload stays a toolbar button, Refresh moved into the Save menu', async () => {
    const user = userEvent.setup();
    renderFormatToolbar(
      {},
      { props: { onSave: jest.fn(), onReloadFromSource: jest.fn(), onRefreshProfile: jest.fn() } }
    );
    // UAT round 2 #5b/#5h: the owner's order names Reload alongside Word and Save, so it stays a button;
    // Refresh profile is a save-adjacent action and moved into the menu. Both still reachable, and the
    // two are still separate actions — the confusion this test has always guarded against.
    expect(screen.getByTestId('compose-format-reload-from-source')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-format-refresh-profile')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /save options/i }));
    expect(screen.getByTestId('compose-format-refresh-profile')).toBeInTheDocument();
  });

  it('ADR-021: renders under a dark theme', () => {
    renderFormatToolbar({}, { theme: webDarkTheme, props: { onReloadFromSource: jest.fn() } });
    expect(screen.getByTestId('compose-format-reload-from-source')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// 6c. Open Document button — opens the source Dataverse Document in the shared
//     preview modal (RichFilePreviewDialog + BFF preview-url), wired by the host.
//     Gated by `onOpenDocument`: rendered only when the host wires a handler
//     (a doc with a preview source is loaded); hidden otherwise. Mirrors the
//     onRefreshProfile / onReloadFromSource gating pattern.
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Open in preview (UAT round 2 #5f: moved into the Word menu)', () => {
  const wordProps = { onOpenInWord: jest.fn(), onOpenInWordDesktop: jest.fn() };

  async function openWordMenu(user: ReturnType<typeof userEvent.setup>): Promise<void> {
    await user.click(screen.getByTestId('compose-format-word-menu'));
  }

  it('is not rendered when no onOpenDocument handler is wired (no previewable document)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { ...wordProps } });
    await openWordMenu(user);
    expect(screen.queryByTestId('compose-format-open-document')).not.toBeInTheDocument();
  });

  it('renders inside the Word menu as "Open in preview" and fires onOpenDocument', async () => {
    const user = userEvent.setup();
    const onOpenDocument = jest.fn();
    renderFormatToolbar({}, { props: { ...wordProps, onOpenDocument } });

    // The three ways to open this document now sit together — that grouping IS the fix.
    await openWordMenu(user);
    const item = screen.getByTestId('compose-format-open-document');
    expect(item).toHaveTextContent('Open in preview');
    expect(item).toHaveAttribute('aria-label', 'Open in preview');
    await user.click(item);
    expect(onOpenDocument).toHaveBeenCalledTimes(1);
  });

  it('is unreachable when the toolbar is read-only — the Word MENU itself is disabled', () => {
    renderFormatToolbar({}, { props: { ...wordProps, onOpenDocument: jest.fn(), disabled: true } });
    // Before the move this was a top-level button carrying its own disabled state. Now the containing
    // menu trigger is what read-only disables, so the gate moved UP a level — the item cannot be opened
    // at all. Asserting the trigger (rather than an item that no longer renders) states the real contract.
    expect(screen.getByTestId('compose-format-word-menu')).toBeDisabled();
    expect(screen.queryByTestId('compose-format-open-document')).not.toBeInTheDocument();
  });

  it('ADR-021: renders under a dark theme', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { theme: webDarkTheme, props: { ...wordProps, onOpenDocument: jest.fn() } });
    await openWordMenu(user);
    expect(screen.getByTestId('compose-format-open-document')).toBeInTheDocument();
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

describe('ComposeFormatToolbar — Add Comment affordance (FR-10 / R6 D7, task 072)', () => {
  it('is not rendered when no onToggleComments handler is wired', () => {
    renderFormatToolbar();
    expect(screen.queryByTestId('compose-format-add-comment')).not.toBeInTheDocument();
  });

  it('renders and fires onToggleComments when clicked (drives the shipped comment machinery via the host)', async () => {
    const user = userEvent.setup();
    const onToggleComments = jest.fn();
    renderFormatToolbar({}, { props: { onToggleComments, commentsOpen: false } });

    const btn = screen.getByTestId('compose-format-add-comment');
    expect(btn).toHaveAttribute('aria-label', 'Add comment');
    expect(btn).toHaveAttribute('aria-pressed', 'false');
    await user.click(btn);
    expect(onToggleComments).toHaveBeenCalledTimes(1);
  });

  it('reflects the open state via aria-pressed', () => {
    renderFormatToolbar({}, { props: { onToggleComments: jest.fn(), commentsOpen: true } });
    expect(screen.getByTestId('compose-format-add-comment')).toHaveAttribute('aria-pressed', 'true');
  });

  it('is disabled when the toolbar is globally disabled', () => {
    renderFormatToolbar({}, { props: { onToggleComments: jest.fn(), disabled: true } });
    expect(screen.getByTestId('compose-format-add-comment')).toBeDisabled();
  });

  it('ADR-021: renders under a dark theme with no hardcoded hex color', () => {
    const { container } = renderFormatToolbar({}, { theme: webDarkTheme, props: { onToggleComments: jest.fn() } });
    const btn = screen.getByTestId('compose-format-add-comment');
    expect(btn).toBeInTheDocument();
    // The button subtree carries only Fluent v9 semantic tokens — no literal hex colors (ADR-021).
    expect(btn.outerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
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

describe('ComposeFormatToolbar — Review Summary / Notes toggles (UAT round-7 #5: two separate icons)', () => {
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
    expect(screen.queryByTestId('compose-format-review-summary-toggle')).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-format-review-notes-toggle')).not.toBeInTheDocument();
  });

  it('is hidden when the toggle handlers are not wired', () => {
    renderFormatToolbar({}, { props: { hasReview: true } });
    expect(screen.queryByTestId('compose-format-review-summary-toggle')).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-format-review-notes-toggle')).not.toBeInTheDocument();
  });

  it('shows two separate icon toggles (no dropdown) when a review is present and handlers are wired', () => {
    renderFormatToolbar({}, { props: reviewProps() });
    expect(screen.queryByTestId('compose-format-review-menu')).not.toBeInTheDocument(); // the dropdown is gone
    const summary = screen.getByTestId('compose-format-review-summary-toggle');
    const notes = screen.getByTestId('compose-format-review-notes-toggle');
    expect(summary).toHaveAttribute('aria-label', 'Toggle Review Summary');
    expect(notes).toHaveAttribute('aria-label', 'Toggle Review Notes');
    // Pressed state reflects the visibility booleans (summary closed, notes open).
    expect(summary).toHaveAttribute('aria-pressed', 'false');
    expect(notes).toHaveAttribute('aria-pressed', 'true');
  });

  it('clicking the Review Summary icon fires onToggleReviewSummary (and not Notes)', async () => {
    const user = userEvent.setup();
    const onToggleReviewSummary = jest.fn();
    const onToggleReviewNotes = jest.fn();
    renderFormatToolbar({}, { props: reviewProps({ onToggleReviewSummary, onToggleReviewNotes }) });

    await user.click(screen.getByTestId('compose-format-review-summary-toggle'));

    expect(onToggleReviewSummary).toHaveBeenCalledTimes(1);
    expect(onToggleReviewNotes).not.toHaveBeenCalled();
  });

  it('clicking the Review Notes icon fires onToggleReviewNotes (and not Summary)', async () => {
    const user = userEvent.setup();
    const onToggleReviewSummary = jest.fn();
    const onToggleReviewNotes = jest.fn();
    renderFormatToolbar({}, { props: reviewProps({ onToggleReviewSummary, onToggleReviewNotes }) });

    await user.click(screen.getByTestId('compose-format-review-notes-toggle'));

    expect(onToggleReviewNotes).toHaveBeenCalledTimes(1);
    expect(onToggleReviewSummary).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// FR-14 (ai-advanced-capabilities-agreements-r1 task 051) — "Create Summary Memo" dropdown.
// Pure forwarder (mirrors the Word dropdown pattern): the toolbar only calls the host-provided
// onGenerateMemo/onEmailMemo handlers — it owns no fetch/download/EmailComposer logic itself.
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Create Summary Memo dropdown (FR-14, task 051)', () => {
  it('is hidden when no review is present, even with both handlers wired', () => {
    renderFormatToolbar({}, { props: { hasReview: false, onGenerateMemo: jest.fn(), onEmailMemo: jest.fn() } });
    expect(screen.queryByTestId('compose-format-memo-menu')).not.toBeInTheDocument();
  });

  it('is hidden when a review is present but neither handler is wired', () => {
    renderFormatToolbar({}, { props: { hasReview: true } });
    expect(screen.queryByTestId('compose-format-memo-menu')).not.toBeInTheDocument();
  });

  it('shows the dropdown trigger when a review is present and at least one handler is wired', () => {
    renderFormatToolbar({}, { props: { hasReview: true, onGenerateMemo: jest.fn() } });
    expect(screen.getByTestId('compose-format-memo-menu')).toBeInTheDocument();
  });

  it('opening the dropdown reveals Generate + Email items and each fires its own handler', async () => {
    const user = userEvent.setup();
    const onGenerateMemo = jest.fn();
    const onEmailMemo = jest.fn();
    renderFormatToolbar({}, { props: { hasReview: true, onGenerateMemo, onEmailMemo } });

    await user.click(screen.getByTestId('compose-format-memo-menu'));
    await user.click(screen.getByTestId('compose-format-memo-generate'));

    expect(onGenerateMemo).toHaveBeenCalledTimes(1);
    expect(onEmailMemo).not.toHaveBeenCalled();

    await user.click(screen.getByTestId('compose-format-memo-menu'));
    await user.click(screen.getByTestId('compose-format-memo-email'));

    expect(onEmailMemo).toHaveBeenCalledTimes(1);
    expect(onGenerateMemo).toHaveBeenCalledTimes(1);
  });

  it('a menu item without its handler wired renders disabled (never silently no-ops on click)', async () => {
    const user = userEvent.setup();
    const onGenerateMemo = jest.fn();
    renderFormatToolbar({}, { props: { hasReview: true, onGenerateMemo } });

    await user.click(screen.getByTestId('compose-format-memo-menu'));

    // Fluent's MenuItem renders a <div role="menuitem"> (not a native button/input), so its
    // disabled state surfaces as aria-disabled, not the native `disabled` attribute jest-dom's
    // toBeDisabled() checks for.
    expect(screen.getByTestId('compose-format-memo-generate')).not.toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByTestId('compose-format-memo-email')).toHaveAttribute('aria-disabled', 'true');
  });

  it('disables both actions and shows a spinner on the trigger while isMemoActionInFlight', async () => {
    const user = userEvent.setup();
    renderFormatToolbar(
      {},
      {
        props: {
          hasReview: true,
          onGenerateMemo: jest.fn(),
          onEmailMemo: jest.fn(),
          isMemoActionInFlight: true,
        },
      }
    );

    expect(screen.getByTestId('compose-format-memo-menu')).toBeDisabled();
  });

  it('is disabled entirely when the global `disabled` prop is set', () => {
    renderFormatToolbar(
      {},
      {
        props: {
          hasReview: true,
          onGenerateMemo: jest.fn(),
          onEmailMemo: jest.fn(),
          disabled: true,
        },
      }
    );

    expect(screen.getByTestId('compose-format-memo-menu')).toBeDisabled();
  });
});

// ---------------------------------------------------------------------------
// UAT round-1 (2026-08-03) owner feedback — three toolbar layout/labelling changes:
//   #3 Create Summary Memo → icon-only, repositioned to the FAR LEFT of the toolbar.
//   #5 Save Version → icon-only (split/dropdown behavior unchanged).
//   #6 Word dropdown → moved OUT of the format-menus row to the action side (right,
//      near Save), icon-only.
// Each control keeps its existing testid + wiring; these tests assert the NEW
// icon-only/no-visible-text presentation, the accessible name (aria-label) + Tooltip,
// and — for #3/#6 — the new DOM position relative to the other toolbar controls.
//
// #3's FAR-LEFT placement is SUPERSEDED by UAT round-4 #12 (2026-08-04, see the
// dedicated describe block below) — the owner moved Memo off the far-left position
// into the regrouped action-icon area. The far-left-position assertion below was
// replaced accordingly; #5/#6 (icon-only + Word's post-format-menu, pre-Save
// position) remain accurate under the round-4 layout and are unchanged.
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — UAT round-1 (2026-08-03): repositioning + icon-only (#3/#5/#6)', () => {
  /** Returns the data-testid of every descendant of the toolbar, in DOM order. */
  function toolbarTestIdOrder(): string[] {
    const toolbar = screen.getByTestId('compose-format-toolbar');
    return Array.from(toolbar.querySelectorAll('[data-testid]')).map(el => el.getAttribute('data-testid') as string);
  }

  it('#3 (superseded position, round-4 #12): Create Summary Memo is icon-only (no visible text), carries aria-label + Tooltip — no longer the far-left/first control (see the round-4 describe block for its current position)', () => {
    renderFormatToolbar({}, { props: { hasReview: true, onGenerateMemo: jest.fn() } });

    const memoTrigger = screen.getByTestId('compose-format-memo-menu');
    expect(memoTrigger).toHaveAttribute('aria-label', 'Create Summary Memo');
    expect(memoTrigger.textContent).toBe(''); // icon-only — no visible label text

    // Round-4 #12: Memo now sits AFTER the format-menu group, not ahead of it.
    const order = toolbarTestIdOrder();
    expect(order[0]).not.toBe('compose-format-memo-menu');
    expect(order.indexOf('compose-format-memo-menu')).toBeGreaterThan(order.indexOf('compose-format-table-menu'));
  });

  it('#3: the memo dropdown still opens and fires its handlers from its (now regrouped) position (behavior unchanged)', async () => {
    const user = userEvent.setup();
    const onGenerateMemo = jest.fn();
    renderFormatToolbar({}, { props: { hasReview: true, onGenerateMemo } });

    await user.click(screen.getByTestId('compose-format-memo-menu'));
    await user.click(screen.getByTestId('compose-format-memo-generate'));
    expect(onGenerateMemo).toHaveBeenCalledTimes(1);
  });

  it('#5: Save is icon-only (no visible text) — the accessible name survives via aria-label on the primary action', () => {
    // FR-01 (task 020): primary label is now "Save" (append an SPE version); the fork moved to the
    // caret-menu "Save As" item.
    renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: true } });

    const saveWrapper = screen.getByTestId('compose-format-save');
    const primaryButton = within(saveWrapper).getAllByRole('button')[0];
    expect(primaryButton).toHaveAttribute('aria-label', 'Save');
    expect(primaryButton.textContent).toBe(''); // icon-only — no visible "Save" text
  });

  it('#5: a Tooltip is present on the Save trigger (hover reveals the "Save" label)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: true } });

    const saveWrapper = screen.getByTestId('compose-format-save');
    const primaryButton = within(saveWrapper).getAllByRole('button')[0];
    await user.hover(primaryButton);
    expect(await screen.findByText('Save')).toBeInTheDocument();
  });

  it('#6: the Word dropdown is icon-only (aria-label "Word", no visible text) and RELOCATED to the action side — after every format-menu trigger, immediately before Save', async () => {
    const user = userEvent.setup();
    renderFormatToolbar(
      {},
      { props: { onOpenInWord: jest.fn(), onOpenInWordDesktop: jest.fn(), onSave: jest.fn(), canSave: true } }
    );

    const wordTrigger = screen.getByTestId('compose-format-word-menu');
    expect(wordTrigger).toHaveAttribute('aria-label', 'Word');
    expect(wordTrigger.textContent).toBe(''); // icon-only — no visible "Word" text

    const order = toolbarTestIdOrder();
    // Moved OUT of the format-menus row: now sits after Body/Paragraph/Font/Table...
    expect(order.indexOf('compose-format-word-menu')).toBeGreaterThan(order.indexOf('compose-format-heading-menu'));
    expect(order.indexOf('compose-format-word-menu')).toBeGreaterThan(order.indexOf('compose-format-paragraph-menu'));
    expect(order.indexOf('compose-format-word-menu')).toBeGreaterThan(order.indexOf('compose-format-font-menu'));
    expect(order.indexOf('compose-format-word-menu')).toBeGreaterThan(order.indexOf('compose-format-table-menu'));
    // ...and on the action side, immediately before Save ("near the save/run controls").
    expect(order.indexOf('compose-format-word-menu')).toBeLessThan(order.indexOf('compose-format-save'));

    // Popover content is UNCHANGED — Open web / Open desktop still reachable and still fire.
    await user.click(wordTrigger);
    expect(screen.getByTestId('compose-format-open-word-web')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-open-word-desktop')).toBeInTheDocument();
  });

  it('#6: a Tooltip is present on the Word trigger (hover reveals the "Word" label)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: { onOpenInWord: jest.fn() } });

    const wordTrigger = screen.getByTestId('compose-format-word-menu');
    await user.hover(wordTrigger);
    expect(await screen.findByText('Word')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// UAT round-4 (2026-08-04) owner feedback — two toolbar changes:
//   #11 MENU ALLOCATION — the Word dropdown must contain ONLY "Open in Word (web)" /
//        "Open in Word (desktop)"; its legacy Save/Save New Document duplicate
//        entries (round-1's "UX-1 parity affordance") are REMOVED. Save stays the
//        ONE split-button (Save Version primary / Save New Document caret). Zero
//        functional overlap between the two menus.
//   #12 REGROUP + SPACING — format menus (Body/Paragraph/Font/Table) stay at the
//        left; the action icons regroup left→right as FOUR `ToolbarDivider`-
//        separated groups: [Review Summary · Create Summary Memo] | [Word · Save] |
//        [Review Notes · Track Changes] | [Undo · Redo · Info]. Supersedes round-1
//        #3's far-left Memo placement. Open Document / Reload from source / Refresh
//        Profile (not named in the owner's order) are placed immediately after the
//        format-menu group and before the regrouped action groups — a flagged
//        placement decision, not a deletion.
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — UAT round-4 (2026-08-04): menu allocation (#11) + regroup/spacing (#12)', () => {
  /** Returns the data-testid of every descendant of the toolbar, in DOM order. */
  function toolbarTestIdOrder(): string[] {
    const toolbar = screen.getByTestId('compose-format-toolbar');
    return Array.from(toolbar.querySelectorAll('[data-testid]')).map(el => el.getAttribute('data-testid') as string);
  }

  const allControlsProps = (): Partial<ComposeFormatToolbarProps> => ({
    onOpenInWord: jest.fn(),
    onOpenInWordDesktop: jest.fn(),
    onSave: jest.fn(),
    canSave: true,
    hasReview: true,
    reviewSummaryOpen: false,
    onToggleReviewSummary: jest.fn(),
    reviewNotesOpen: false,
    onToggleReviewNotes: jest.fn(),
    onToggleTrackChanges: jest.fn(),
    trackChangesEnabled: false,
    onGenerateMemo: jest.fn(),
    onEmailMemo: jest.fn(),
    onOpenDocument: jest.fn(),
    onReloadFromSource: jest.fn(),
    onRefreshProfile: jest.fn(),
    // UAT round 2 #5i — Apply template moved INTO the Word menu, so it must be wired here for the
    // relocation test to be able to look for it.
    onApplyTemplate: jest.fn(),
    reviewDisclaimer: 'Not legal advice.',
  });

  // ---- #11: menu allocation ----

  it('#11: the Word menu contains ONLY the two Open-in-Word items — no Save / Save New Document duplicate, even when onSave is wired', async () => {
    const user = userEvent.setup();
    renderFormatToolbar(
      {},
      { props: { onOpenInWord: jest.fn(), onOpenInWordDesktop: jest.fn(), onSave: jest.fn(), canSave: true } }
    );

    await user.click(screen.getByTestId('compose-format-word-menu'));

    expect(screen.getByTestId('compose-format-open-word-web')).toBeInTheDocument();
    expect(screen.getByTestId('compose-format-open-word-desktop')).toBeInTheDocument();
    // The former Word-menu Save duplicate testids must be entirely absent from the DOM.
    expect(screen.queryByTestId('compose-format-word-save')).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-format-word-save-new')).not.toBeInTheDocument();
    expect(screen.queryByText('Save Version')).not.toBeInTheDocument();
    expect(screen.queryByText('Save New Document')).not.toBeInTheDocument();
  });

  it('#11: Save remains the ONE entry point — the split-button still renders "Save Version" primary + "Save New Document" caret item', async () => {
    const user = userEvent.setup();
    const onSave = jest.fn();
    renderFormatToolbar({}, { props: { onSave, canSave: true } });

    const saveWrapper = screen.getByTestId('compose-format-save');
    const primaryButton = within(saveWrapper).getAllByRole('button')[0];
    await user.click(primaryButton);
    expect(onSave).toHaveBeenCalledWith('version');

    await user.click(screen.getByRole('button', { name: /save options/i }));
    const saveNew = await screen.findByTestId('compose-format-save-new');
    await user.click(saveNew);
    expect(onSave).toHaveBeenCalledWith('new');
  });

  // ---- #12: regroup + spacing ----

  // ---------------------------------------------------------------------------
  // UAT ROUND 2 #5 (2026-09-02) — SUPERSEDES the round-4 #12 order asserted above.
  // The owner re-specified the row: history at the far LEFT, format menus next, and the tool icons
  // right-aligned. Open Document / Apply template moved INTO the Word menu, Refresh profile into the
  // Save menu, so the three "unmentioned icons" the round-4 tests protected no longer sit on the row —
  // which is why those tests are replaced rather than repaired.
  // ---------------------------------------------------------------------------

  it('#5c: Undo · Redo open the row, to the LEFT of every format menu', () => {
    renderFormatToolbar({}, { props: allControlsProps() });
    const order = toolbarTestIdOrder();
    const idx = (id: string): number => order.indexOf(id);

    expect(idx('compose-format-undo')).toBeLessThan(idx('compose-format-redo'));
    expect(idx('compose-format-redo')).toBeLessThan(idx('compose-format-divider-history'));
    expect(idx('compose-format-divider-history')).toBeLessThan(idx('compose-format-heading-menu'));
  });

  it('#5a: the format menus stay together and in order — Body · Paragraph · Font · Table', () => {
    renderFormatToolbar({}, { props: allControlsProps() });
    const order = toolbarTestIdOrder();
    const idx = (id: string): number => order.indexOf(id);

    expect(idx('compose-format-heading-menu')).toBeLessThan(idx('compose-format-paragraph-menu'));
    expect(idx('compose-format-paragraph-menu')).toBeLessThan(idx('compose-format-font-menu'));
    expect(idx('compose-format-font-menu')).toBeLessThan(idx('compose-format-table-menu'));
  });

  it('#5b: the tool icons are right-aligned and ordered Word · Save · Reload — separator — Comments · Track changes', () => {
    renderFormatToolbar({}, { props: allControlsProps() });
    const order = toolbarTestIdOrder();
    const idx = (id: string): number => order.indexOf(id);

    // Everything in the tool group sits after the last format menu.
    expect(idx('compose-format-table-menu')).toBeLessThan(idx('compose-format-word-menu'));
    expect(idx('compose-format-word-menu')).toBeLessThan(idx('compose-format-save'));
    expect(idx('compose-format-save')).toBeLessThan(idx('compose-format-reload-from-source'));
    // ...separator...
    expect(idx('compose-format-reload-from-source')).toBeLessThan(idx('compose-format-divider-2'));
    // ...then the comment/track-changes group.
    expect(idx('compose-format-divider-2')).toBeLessThan(idx('compose-format-review-notes-toggle'));
    expect(idx('compose-format-review-notes-toggle')).toBeLessThan(idx('compose-format-track-changes'));
  });

  it('#5f/#5h/#5i: the three relocated controls are GONE from the toolbar row, not deleted', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: allControlsProps() });

    // The point of the restructure was to reclaim row space, so their absence here is the fix...
    expect(screen.queryByTestId('compose-format-open-document')).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-format-apply-template')).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-format-refresh-profile')).not.toBeInTheDocument();

    // ...but every one must still be REACHABLE. A restructure that quietly drops a capability is the
    // failure mode this asserts against, and it is the reason these are checked together.
    await user.click(screen.getByTestId('compose-format-word-menu'));
    expect(screen.getByTestId('compose-format-open-document')).toHaveTextContent('Open in preview');
    expect(screen.getByTestId('compose-format-apply-template')).toHaveTextContent('Apply template');
    await user.keyboard('{Escape}');

    await user.click(screen.getByRole('button', { name: /save options/i }));
    expect(screen.getByTestId('compose-format-refresh-profile')).toHaveTextContent('Refresh profile');
  });

  it('#5g: the Word menu reads "Open in web" / "Open in desktop" (supersedes round-4 #11 wording)', async () => {
    const user = userEvent.setup();
    renderFormatToolbar({}, { props: allControlsProps() });
    await user.click(screen.getByTestId('compose-format-word-menu'));

    expect(screen.getByTestId('compose-format-open-word-web')).toHaveTextContent('Open in web');
    expect(screen.getByTestId('compose-format-open-word-desktop')).toHaveTextContent('Open in desktop');
    // The old "(web)"/"(desktop)" parenthetical is gone — asserted explicitly so a relapse fails.
    expect(screen.queryByText(/Open in Word \(web\)/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Open in Word \(desktop\)/)).not.toBeInTheDocument();
  });

  it('#5e: the Word trigger carries a dropdown chevron so it reads as a menu, like the Save group', () => {
    renderFormatToolbar({}, { props: allControlsProps() });
    const trigger = screen.getByTestId('compose-format-word-menu');
    expect(trigger).toHaveAttribute('aria-label', 'Word');
    // The chevron is an svg child; before #5e the trigger was a bare icon button with no menu affordance.
    expect(trigger.querySelectorAll('svg').length).toBeGreaterThanOrEqual(2);
  });

  it('renders the whole row without review-only controls (no hasReview) — no crash, groups intact', () => {
    renderFormatToolbar(
      {},
      {
        props: {
          onOpenInWord: jest.fn(),
          onOpenInWordDesktop: jest.fn(),
          onSave: jest.fn(),
          canSave: true,
          onOpenDocument: jest.fn(),
          onReloadFromSource: jest.fn(),
          onRefreshProfile: jest.fn(),
        },
      }
    );
    const order = toolbarTestIdOrder();
    const idx = (id: string): number => order.indexOf(id);
    expect(idx('compose-format-undo')).toBeLessThan(idx('compose-format-heading-menu'));
    expect(idx('compose-format-table-menu')).toBeLessThan(idx('compose-format-word-menu'));
    expect(idx('compose-format-word-menu')).toBeLessThan(idx('compose-format-save'));
  });
});

describe('ComposeFormatToolbar — unsaved state on the Save icon (UAT round 2 #5d)', () => {
  // The "Saving… / Unsaved / Saved · Auto Save On" TEXT indicator was REMOVED at the owner's request to
  // reclaim toolbar space. Its two halves deliberately went to different places, and these tests pin both:
  //   · "Unsaved" → a warning triangle on the Save icon, plus the button's aria-label.
  //   · "Recovery draft On/Off" → the checkable item in the Save menu (covered by the Save-menu suite).

  it('shows a warning badge on the Save icon when there are unsaved edits', () => {
    renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: true, hasUnsavedEdits: true } });
    expect(screen.getByTestId('compose-format-save-unsaved-badge')).toBeInTheDocument();
  });

  it('shows NO badge when everything is saved', () => {
    renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: true, hasUnsavedEdits: false } });
    expect(screen.queryByTestId('compose-format-save-unsaved-badge')).not.toBeInTheDocument();
  });

  it('announces the unsaved state through the Save button aria-label, so removing the text costs nothing to assistive tech', () => {
    renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: true, hasUnsavedEdits: true } });
    expect(screen.getByRole('button', { name: /unsaved changes/i })).toBeInTheDocument();
  });

  it('a save in flight announces "Saving" and drops the unsaved badge', () => {
    renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: true, hasUnsavedEdits: true, isSaving: true } });
    // isSaving overrides the dirty state — the same precedence the old text indicator had.
    expect(screen.getByRole('button', { name: /^saving$/i })).toBeInTheDocument();
  });

  it('the removed text indicator does not come back', () => {
    renderFormatToolbar({}, { props: { onSave: jest.fn(), canSave: true, hasUnsavedEdits: true } });
    // Asserting the ABSENCE explicitly: the space this reclaimed is the whole point of #5d, so a
    // reintroduced indicator should fail rather than quietly re-consume the row.
    expect(screen.queryByTestId('compose-save-state-indicator')).not.toBeInTheDocument();
    expect(screen.queryByText(/Auto Save (On|Off)/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Recovery draft (On|Off)/)).not.toBeInTheDocument();
  });

  it('renders correctly in dark mode (ADR-021 — no crash, badge present)', () => {
    renderFormatToolbar(
      {},
      { theme: webDarkTheme, props: { onSave: jest.fn(), canSave: true, hasUnsavedEdits: true } }
    );
    expect(screen.getByTestId('compose-format-save-unsaved-badge')).toBeInTheDocument();
  });
});
