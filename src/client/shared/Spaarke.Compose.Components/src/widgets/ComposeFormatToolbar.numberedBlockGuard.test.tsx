/**
 * ComposeFormatToolbar.numberedBlockGuard.test.tsx — U-0 (spaarkeai-compose-r8, UAT round 1).
 *
 * TWO suites, and the split is the point:
 *
 *  1. **The mechanism** — a headless editor built from the REAL production extension set, pinning what
 *     `toggleList` actually does to a projected numbered heading. This suite is the guard's evidence: if
 *     an upstream TipTap change ever makes the retype non-destructive, THIS suite goes red and tells us
 *     the refusal in suite 2 can be lifted. A guard whose premise is never re-checked outlives its reason.
 *
 *  2. **The refusal** — the toolbar, driven by a REAL editor (not the sibling file's chainable mock), so
 *     the disabled state is derived from genuine projected HTML rather than from a hand-set flag. A mock
 *     asserting `isActive('heading') === true` would prove only that the mock was configured.
 *
 * Unlike the sibling `ComposeFormatToolbar.test.tsx`, these mount the production `ComposeFormatToolbar`
 * against `@tiptap/core`'s `Editor` — the same technique `composeNumberAtomExtension.test.ts` uses to
 * exercise the real schema without the React/auth surface of a full `ComposeEditor` mount.
 *
 * @see ./ComposeFormatToolbar.tsx — `listToggleWouldDestroyBlockIdentity` (the refusal + its rationale)
 * @see projects/spaarkeai-compose-r8/notes/uat/numbering-editing-design-options.md — the Option C design
 *      that re-enables this control properly
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import type { Editor as ReactEditor } from '@tiptap/react';
import { COMPOSE_NUMBER_ATOM } from './composeNumberAtomExtension';
import { COMPOSE_R3_PARAID } from './paraIdExtension';
import { COMPOSE_R3_STYLES } from './hooks/useComposeDocumentStyles';
import { COMPOSE_INDENT } from './composeIndentExtension';
import { ComposeFormatToolbar, listToggleWouldDestroyBlockIdentity } from './ComposeFormatToolbar';

/**
 * The production extension registration, minus the React host — mirrors `ComposeEditor.tsx`'s
 * `LOCKED_EXTENSIONS` + additive arrays for the block-attribute extensions this behaviour depends on.
 */
function makeEditor(content: string): Editor {
  return new Editor({
    extensions: [
      StarterKit.configure({ heading: { levels: [1, 2, 3, 4, 5, 6] as const } }),
      ...COMPOSE_R3_PARAID,
      ...COMPOSE_NUMBER_ATOM,
      ...COMPOSE_R3_STYLES,
      ...COMPOSE_INDENT,
    ],
    content,
  });
}

/**
 * Exactly what `ComposeDocxProjectionBuilder.RenderParagraph` emits for a numbered Heading 2. The
 * `headingLevel is null ? ListInfo(p, ctx) : null` line there means a numbered HEADING is never wrapped
 * in `<ol>` — which is why `isActive('orderedList')` is false on it, and why the owner's "remove
 * numbering" click was in fact the toggle ADDING a list.
 */
const NUMBERED_HEADING =
  '<h2 data-paraid="AAAA1111" data-computed-number="1.2" data-numbering-level="1">Technical Field of the Invention</h2>';
const NUMBERED_PARAGRAPH =
  '<p data-paraid="BBBB2222" data-computed-number="1.3" data-numbering-level="1">A numbered clause.</p>';
const PLAIN_PARAGRAPH = '<p data-paraid="CCCC3333">Ordinary unnumbered body text.</p>';

function firstBlockAttrs(editor: Editor): Record<string, unknown> {
  const node = editor.state.doc.firstChild;
  if (!node) throw new Error('empty document');
  return node.attrs as Record<string, unknown>;
}

// ---------------------------------------------------------------------------
// 1. The mechanism — why the refusal exists
// ---------------------------------------------------------------------------

describe('U-0 mechanism — toggleList destroys a projected numbered heading', () => {
  it('flattens the heading level, drops the computed number, and RE-MINTS the paraId', () => {
    const editor = makeEditor(NUMBERED_HEADING);

    const before = firstBlockAttrs(editor);
    expect(editor.state.doc.firstChild?.type.name).toBe('heading');
    expect(before.level).toBe(2);
    expect(before.paraId).toBe('AAAA1111');
    expect(before.computedNumber).toBe('1.2');

    // The projection never puts a heading inside <ol>, so the toggle ADDS a list — it does not remove one.
    expect(editor.isActive('orderedList')).toBe(false);
    editor.commands.setTextSelection(3);
    editor.chain().focus().toggleOrderedList().run();

    const listItemParagraph = editor.state.doc.firstChild?.firstChild?.firstChild;
    expect(editor.state.doc.firstChild?.type.name).toBe('orderedList');
    expect(listItemParagraph?.type.name).toBe('paragraph'); // heading level 2 is gone
    const after = listItemParagraph?.attrs as Record<string, unknown>;
    expect(after.computedNumber).toBeNull();
    expect(after.numberingLevel).toBeNull();
    // The session identity is not merely absent — a DIFFERENT id is minted, orphaning any comment or
    // redline anchored to the original.
    expect(after.paraId).toBeTruthy();
    expect(after.paraId).not.toBe('AAAA1111');

    editor.destroy();
  });

  it('a second toggle does not restore the heading — the round trip is irreversible', () => {
    const editor = makeEditor(NUMBERED_HEADING);
    editor.commands.setTextSelection(3);
    editor.chain().focus().toggleOrderedList().run();
    editor.chain().focus().toggleOrderedList().run();

    expect(editor.state.doc.firstChild?.type.name).toBe('paragraph');
    expect(editor.getHTML()).not.toContain('<h2');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 2. The predicate
// ---------------------------------------------------------------------------

describe('listToggleWouldDestroyBlockIdentity', () => {
  it.each([
    ['a projected numbered heading', NUMBERED_HEADING, true],
    ['a projected numbered paragraph', NUMBERED_PARAGRAPH, true],
    ['an ordinary unnumbered paragraph', PLAIN_PARAGRAPH, false],
  ])('%s → %s', (_label, html, expected) => {
    const editor = makeEditor(html);
    editor.commands.setTextSelection(3);
    expect(listToggleWouldDestroyBlockIdentity(editor as unknown as ReactEditor)).toBe(expected);
    editor.destroy();
  });

  it('a null editor is not destructive (host renders the toolbar before the editor mounts)', () => {
    expect(listToggleWouldDestroyBlockIdentity(null)).toBe(false);
  });

  it('an editor without getAttributes reads as not-destructive rather than throwing', () => {
    // Mirrors `canRunTableCommand`'s tolerance of lighter-weight test/host editors.
    const bare = { isActive: () => false } as unknown as ReactEditor;
    expect(() => listToggleWouldDestroyBlockIdentity(bare)).not.toThrow();
    expect(listToggleWouldDestroyBlockIdentity(bare)).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// 3. The refusal, through the real toolbar
// ---------------------------------------------------------------------------

function renderToolbarFor(html: string, caretPos = 3): Editor {
  const editor = makeEditor(html);
  editor.commands.setTextSelection(caretPos);
  render(
    <FluentProvider theme={webLightTheme}>
      <ComposeFormatToolbar editor={editor as unknown as ReactEditor} hasLoadedBaseline />
    </FluentProvider>
  );
  return editor;
}

describe('ComposeFormatToolbar — list toggles on a projected numbered block', () => {
  it('disables BOTH list toggles when the caret is in a numbered heading', async () => {
    const user = userEvent.setup();
    const editor = renderToolbarFor(NUMBERED_HEADING);
    await user.click(screen.getByTestId('compose-format-paragraph-menu'));

    expect(screen.getByTestId('compose-format-ordered-list')).toBeDisabled();
    expect(screen.getByTestId('compose-format-bullet-list')).toBeDisabled();
    // Blockquote is NOT part of this refusal — it does not retype the block away from its identity.
    expect(screen.getByTestId('compose-format-blockquote')).not.toBeDisabled();
    editor.destroy();
  });

  it('disables the list toggles in a numbered paragraph', async () => {
    const user = userEvent.setup();
    const editor = renderToolbarFor(NUMBERED_PARAGRAPH);
    await user.click(screen.getByTestId('compose-format-paragraph-menu'));

    expect(screen.getByTestId('compose-format-ordered-list')).toBeDisabled();
    editor.destroy();
  });

  it('LEAVES the list toggles enabled in an ordinary unnumbered paragraph (R5 task 011 stands)', async () => {
    const user = userEvent.setup();
    const editor = renderToolbarFor(PLAIN_PARAGRAPH);
    await user.click(screen.getByTestId('compose-format-paragraph-menu'));

    expect(screen.getByTestId('compose-format-ordered-list')).not.toBeDisabled();
    expect(screen.getByTestId('compose-format-bullet-list')).not.toBeDisabled();
    editor.destroy();
  });

  it('the refused control explains what the user can do instead, and does not claim to be read-only', async () => {
    const user = userEvent.setup();
    const editor = renderToolbarFor(NUMBERED_HEADING);
    await user.click(screen.getByTestId('compose-format-paragraph-menu'));

    const ordered = screen.getByTestId('compose-format-ordered-list');
    await user.hover(ordered);
    const tip = await screen.findByRole('tooltip');
    expect(tip).toHaveTextContent(/numbering from the document/i);
    expect(tip).toHaveTextContent(/Word/);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 4. Outline depth (UAT round 2 #2)
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Body menu outline depth', () => {
  it('offers Heading 1 through 6, not just 1-3', async () => {
    const user = userEvent.setup();
    const editor = renderToolbarFor(PLAIN_PARAGRAPH);
    await user.click(screen.getByTestId('compose-format-heading-menu'));

    // In a heading-style-numbered document, outline depth IS the numbering depth: reaching 1.1.1.1
    // needs Heading 4. Both ends already supported six levels; only this menu capped it at three.
    for (const level of [1, 2, 3, 4, 5, 6]) {
      expect(screen.getByTestId(`compose-format-heading-${level}`)).toHaveTextContent(`Heading ${level}`);
    }
    editor.destroy();
  });

  it('applies a deep heading and reports it on the menu button (the label ladder used to stop at 3)', async () => {
    const user = userEvent.setup();
    const editor = renderToolbarFor(PLAIN_PARAGRAPH);
    await user.click(screen.getByTestId('compose-format-heading-menu'));
    await user.click(screen.getByTestId('compose-format-heading-4'));

    expect(editor.state.doc.firstChild?.type.name).toBe('heading');
    expect(editor.state.doc.firstChild?.attrs.level).toBe(4);
    // Before the fix this button read "Body" for a Heading 4 paragraph.
    expect(screen.getByTestId('compose-format-heading-menu')).toHaveTextContent('Heading 4');
    editor.destroy();
  });

  it('preserves paraId and the computed number across a depth change', async () => {
    const user = userEvent.setup();
    const editor = renderToolbarFor(NUMBERED_PARAGRAPH);
    await user.click(screen.getByTestId('compose-format-heading-menu'));
    await user.click(screen.getByTestId('compose-format-heading-3'));

    // Unlike the list toggle (suite 1), a depth change is identity-preserving — measured, not assumed.
    const attrs = editor.state.doc.firstChild?.attrs as Record<string, unknown>;
    expect(attrs.paraId).toBe('BBBB2222');
    expect(attrs.computedNumber).toBe('1.3');
    editor.destroy();
  });
});
