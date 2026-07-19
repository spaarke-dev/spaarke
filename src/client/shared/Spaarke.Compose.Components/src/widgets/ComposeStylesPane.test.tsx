/**
 * ComposeStylesPane.test.tsx — FR-22 styles pane, apply-existing-only (spaarkeai-compose-r3 task 043).
 *
 * Three layers, mirroring `ComposeFindReplace.test.tsx`'s convention:
 *  1. HOOK LOGIC — `deriveDocumentStyles` / `applyComposeDocumentStyle` / `useComposeDocumentStyles`
 *     driven against a REAL headless TipTap `@tiptap/core` Editor (StarterKit + `ComposePStyleExtension`,
 *     the same schema-registration path ComposeEditor uses). Covers: the catalog is derived from the
 *     LIVE document (not an invented fixed list — a style level absent from the doc does not appear),
 *     applying an existing style changes the target block's `pStyle` node attribute (+ node type for a
 *     heading), `paraId` survives a style change, and applying a style NOT in the catalog is a no-op
 *     (the scope-guard boundary at the hook layer).
 *  2. UI — `ComposeStylesPane` rendered with a real editor instance: field wiring, clicking a style
 *     button applies it, an ADR-021 dark-mode render check.
 *  3. SCOPE GUARD — asserts NO create/rename/delete/manage-style control is rendered anywhere in the
 *     pane, regardless of document content.
 */
import * as React from 'react';
import { render, screen, act, renderHook } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import {
  useComposeDocumentStyles,
  ComposePStyleExtension,
  deriveDocumentStyles,
  applyComposeDocumentStyle,
} from './hooks/useComposeDocumentStyles';
import { COMPOSE_R3_PARAID } from './paraIdExtension';
import { ComposeStylesPane } from './ComposeStylesPane';

// Mirrors ComposeEditor.tsx's real registration: `pStyle` (this task) mounted ALONGSIDE the R3 paraId
// identity extension (task 011) — production always has both, and the paraId-preservation test below
// depends on `paraId` actually being in the schema's attr spec (an attribute not registered on the
// node type is dropped by ProseMirror's node-creation validation, not merely left `undefined`).
function makeEditor(content: string): Editor {
  return new Editor({
    extensions: [StarterKit, ComposePStyleExtension, ...COMPOSE_R3_PARAID],
    content,
  });
}

function pStyleAt(editor: Editor, text: string): string | null {
  let found: string | null = null;
  editor.state.doc.descendants(node => {
    if (node.type.name === 'paragraph' || node.type.name === 'heading') {
      if (node.textContent === text) found = (node.attrs.pStyle as string | null) ?? null;
    }
    return true;
  });
  return found;
}

function paraIdAt(editor: Editor, text: string): string | null {
  let found: string | null = null;
  editor.state.doc.descendants(node => {
    if (node.type.name === 'paragraph' || node.type.name === 'heading') {
      if (node.textContent === text) found = (node.attrs.paraId as string | null) ?? null;
    }
    return true;
  });
  return found;
}

// ---------------------------------------------------------------------------
// 1. deriveDocumentStyles — the LIVE, non-invented catalog
// ---------------------------------------------------------------------------

describe('deriveDocumentStyles (existing-style catalog, not an invented list)', () => {
  it('lists exactly the styles present in the document, in doc-structure order', () => {
    const editor = makeEditor('<h1>Title</h1><p>Body text here.</p><h2>Section</h2>');
    const styles = deriveDocumentStyles(editor);
    expect(styles.map(s => s.styleId)).toEqual(['Normal', 'Heading1', 'Heading2']);
    expect(styles.map(s => s.displayName)).toEqual(['Body Text', 'Heading 1', 'Heading 2']);
    editor.destroy();
  });

  it('does NOT list a style absent from the document (no invented entries)', () => {
    const editor = makeEditor('<p>Just a plain paragraph.</p>');
    const styles = deriveDocumentStyles(editor);
    expect(styles).toEqual([{ styleId: 'Normal', displayName: 'Body Text' }]);
    expect(styles.some(s => s.styleId.startsWith('Heading'))).toBe(false);
    editor.destroy();
  });

  it('recomputes as the document changes (a newly-typed Heading 3 appears in the catalog)', () => {
    const editor = makeEditor('<p>Body only.</p>');
    expect(deriveDocumentStyles(editor).map(s => s.styleId)).toEqual(['Normal']);

    editor.commands.setContent('<p>Body only.</p><h3>New section</h3>');
    expect(deriveDocumentStyles(editor).map(s => s.styleId)).toEqual(['Normal', 'Heading3']);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 2. applyComposeDocumentStyle — an ATTRIBUTE (+ node-type) change, not a mark
// ---------------------------------------------------------------------------

describe('applyComposeDocumentStyle (selection-scoped pStyle change)', () => {
  it('applying an existing Heading style to a paragraph changes its pStyle to that style and its node type to heading', () => {
    const editor = makeEditor('<h2>Existing heading</h2><p>Plain paragraph.</p>');
    // Place selection inside the plain paragraph — located via descendants (reliable vs. a raw text offset).
    let paragraphPos = -1;
    editor.state.doc.descendants((node, p) => {
      if (node.type.name === 'paragraph' && node.textContent === 'Plain paragraph.') paragraphPos = p;
      return true;
    });
    expect(paragraphPos).toBeGreaterThanOrEqual(0);
    editor.commands.setTextSelection(paragraphPos + 1);

    expect(pStyleAt(editor, 'Plain paragraph.')).toBeNull();

    const applied = applyComposeDocumentStyle(editor, { styleId: 'Heading2', displayName: 'Heading 2' });

    expect(applied).toBe(true);
    expect(pStyleAt(editor, 'Plain paragraph.')).toBe('Heading2');
    // Node type actually became a heading (level 2) — the block-level change FR-22 describes.
    let becameHeading = false;
    editor.state.doc.descendants(node => {
      if (node.type.name === 'heading' && node.textContent === 'Plain paragraph.' && node.attrs.level === 2) {
        becameHeading = true;
      }
      return true;
    });
    expect(becameHeading).toBe(true);
    editor.destroy();
  });

  it('applying "Normal" to a heading changes its pStyle to Normal and its node type back to paragraph', () => {
    const editor = makeEditor('<h1>A heading</h1>');
    editor.commands.setTextSelection(2);

    const applied = applyComposeDocumentStyle(editor, { styleId: 'Normal', displayName: 'Body Text' });

    expect(applied).toBe(true);
    expect(pStyleAt(editor, 'A heading')).toBe('Normal');
    let stillHeading = false;
    editor.state.doc.descendants(node => {
      if (node.type.name === 'heading' && node.textContent === 'A heading') stillHeading = true;
      return true;
    });
    expect(stillHeading).toBe(false);
    editor.destroy();
  });

  it('preserves the paraId attribute across a style change (FR-09 identity survives)', () => {
    const editor = makeEditor('<p>Carries an id.</p>');
    // Stamp a paraId directly (mirrors stampParaIds' tr.setNodeMarkup idiom).
    let pos = -1;
    editor.state.doc.descendants((node, p) => {
      if (node.type.name === 'paragraph') pos = p;
      return true;
    });
    const tr = editor.state.tr.setNodeMarkup(pos, undefined, {
      ...editor.state.doc.nodeAt(pos)!.attrs,
      paraId: 'ABCDEF01',
    });
    editor.view.dispatch(tr);
    expect(paraIdAt(editor, 'Carries an id.')).toBe('ABCDEF01');

    editor.commands.setTextSelection(pos + 1);
    applyComposeDocumentStyle(editor, { styleId: 'Heading1', displayName: 'Heading 1' });

    expect(paraIdAt(editor, 'Carries an id.')).toBe('ABCDEF01');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 3. useComposeDocumentStyles — scope-guard boundary at the hook layer
// ---------------------------------------------------------------------------

describe('useComposeDocumentStyles.applyStyle (scope guard: existing styles only)', () => {
  it('applying a styleId already in the catalog succeeds', () => {
    const editor = makeEditor('<h1>Title</h1><p>Body.</p>');
    const { result } = renderHook(() => useComposeDocumentStyles(editor));
    let pos = -1;
    editor.state.doc.descendants((node, p) => {
      if (node.type.name === 'paragraph') pos = p;
      return true;
    });
    act(() => {
      editor.commands.setTextSelection(pos + 1);
    });

    let ok = false;
    act(() => {
      ok = result.current.applyStyle('Heading1');
    });
    expect(ok).toBe(true);
    expect(pStyleAt(editor, 'Body.')).toBe('Heading1');
    editor.destroy();
  });

  it('applying a styleId NOT present in the document catalog is a no-op (cannot author a new style)', () => {
    const editor = makeEditor('<p>Only a plain paragraph — no headings at all.</p>');
    const { result } = renderHook(() => useComposeDocumentStyles(editor));
    expect(result.current.styles.some(s => s.styleId === 'Heading4')).toBe(false);

    let ok = true;
    act(() => {
      ok = result.current.applyStyle('Heading4');
    });
    expect(ok).toBe(false);
    expect(pStyleAt(editor, 'Only a plain paragraph — no headings at all.')).toBeNull();
    editor.destroy();
  });

  it('activeStyleId reflects the block under the selection anchor', () => {
    const editor = makeEditor('<h2>A heading</h2><p>A paragraph.</p>');
    const { result } = renderHook(() => useComposeDocumentStyles(editor));
    let headingPos = -1;
    editor.state.doc.descendants((node, p) => {
      if (node.type.name === 'heading') headingPos = p;
      return true;
    });
    act(() => {
      editor.commands.setTextSelection(headingPos + 1);
    });
    expect(result.current.activeStyleId).toBe('Heading2');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 4. ComposeStylesPane panel — Fluent v9 UI + dark mode (ADR-021) + SCOPE GUARD
// ---------------------------------------------------------------------------

function renderPane(editor: Editor, opts: { open?: boolean; theme?: typeof webLightTheme; onClose?: () => void } = {}) {
  const onClose = opts.onClose ?? jest.fn();
  const result = render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <ComposeStylesPane editor={editor} open={opts.open ?? true} onClose={onClose} />
    </FluentProvider>
  );
  return { ...result, onClose };
}

describe('ComposeStylesPane panel', () => {
  it('renders nothing when closed', () => {
    const editor = makeEditor('<p>Hello.</p>');
    renderPane(editor, { open: false });
    expect(screen.queryByTestId('compose-styles-pane')).not.toBeInTheDocument();
    editor.destroy();
  });

  it('lists the document’s existing styles as apply buttons', () => {
    const editor = makeEditor('<h1>Title</h1><p>Body copy.</p><h2>Section</h2>');
    renderPane(editor);
    expect(screen.getByTestId('compose-styles-pane')).toBeInTheDocument();
    expect(screen.getByTestId('compose-styles-pane-apply-Normal')).toHaveTextContent('Body Text');
    expect(screen.getByTestId('compose-styles-pane-apply-Heading1')).toHaveTextContent('Heading 1');
    expect(screen.getByTestId('compose-styles-pane-apply-Heading2')).toHaveTextContent('Heading 2');
    // A style absent from the document is not offered.
    expect(screen.queryByTestId('compose-styles-pane-apply-Heading3')).not.toBeInTheDocument();
    editor.destroy();
  });

  it('clicking an existing style applies it to the current selection', async () => {
    const user = userEvent.setup();
    const editor = makeEditor('<h1>Title</h1><p>Body copy.</p>');
    let paragraphPos = -1;
    editor.state.doc.descendants((node, p) => {
      if (node.type.name === 'paragraph') paragraphPos = p;
      return true;
    });
    editor.commands.setTextSelection(paragraphPos + 1);

    renderPane(editor);
    await user.click(screen.getByTestId('compose-styles-pane-apply-Heading1'));

    expect(pStyleAt(editor, 'Body copy.')).toBe('Heading1');
    editor.destroy();
  });

  it('the close button calls onClose', async () => {
    const user = userEvent.setup();
    const editor = makeEditor('<p>Hello.</p>');
    const onClose = jest.fn();
    renderPane(editor, { onClose });

    await user.click(screen.getByTestId('compose-styles-pane-close'));

    expect(onClose).toHaveBeenCalledTimes(1);
    editor.destroy();
  });

  it('an empty-of-styles document (should not normally happen — paragraph is always present) shows no false positives', () => {
    const editor = makeEditor('<p></p>');
    renderPane(editor);
    // An empty paragraph is still a paragraph node, so "Body Text" is offered — never an empty catalog
    // for a real mounted editor (there is always at least one paragraph).
    expect(screen.getByTestId('compose-styles-pane-apply-Normal')).toBeInTheDocument();
    editor.destroy();
  });

  it('ADR-021: renders under a dark theme with no hardcoded hex color', () => {
    const editor = makeEditor('<h1>Title</h1><p>Body.</p>');
    const { container } = renderPane(editor, { theme: webDarkTheme });
    expect(screen.getByTestId('compose-styles-pane')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    editor.destroy();
  });

  it('SCOPE GUARD: renders no create/rename/delete/manage-style affordance anywhere in the pane', () => {
    const editor = makeEditor('<h1>Title</h1><h2>Section</h2><p>Body copy with lots of styles used.</p>');
    const { container } = renderPane(editor);

    // No authoring-verb text anywhere in the pane's rendered output.
    const authoringVerbPattern =
      /\b(new style|create style|add style|rename|delete style|remove style|manage styles|edit style|style options|modify style)\b/i;
    expect(container.textContent ?? '').not.toMatch(authoringVerbPattern);

    // No control carries an authoring-verb accessible name.
    expect(
      screen.queryByRole('button', { name: /new style|create|rename|delete|manage|edit style/i })
    ).not.toBeInTheDocument();

    // No conventionally-named authoring testids exist, regardless of naming.
    for (const verb of ['create', 'new', 'rename', 'delete', 'remove', 'manage', 'edit', 'options']) {
      expect(container.querySelector(`[data-testid*="compose-styles-pane-${verb}"]`)).toBeNull();
    }
    editor.destroy();
  });
});
