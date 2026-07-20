/**
 * ComposeFindReplace.test.tsx — FR-17 in-editor find/replace (spaarkeai-compose-r3 task 040).
 *
 * Two layers, mirroring `usePendingRedline.test.tsx`'s convention:
 *  1. HOOK LOGIC — `useComposeFindReplace` driven via `renderHook` over a REAL headless TipTap
 *     `@tiptap/core` Editor (StarterKit + the three FR-15 marks + `ComposeFindReplaceExtension`, the
 *     same schema-registration path ComposeEditor uses). Covers: match search + decoration highlight
 *     (decoration-only, no doc mutation), case-sensitivity scoping, replace-all correctness, and the
 *     load-bearing mark-integrity assertion (a replace over an `InsertionMark` range keeps the mark).
 *  2. UI — `ComposeFindReplace` rendered with a real editor instance: field wiring, match-count
 *     label, case toggle, replace/replace-all buttons, and an ADR-021 dark-mode render check.
 */
import * as React from 'react';
import { render, screen, act, renderHook } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { InsertionMark } from './marks/InsertionMark';
import { DeletionMark } from './marks/DeletionMark';
import { CommentAnchorMark } from './marks/CommentAnchorMark';
import {
  useComposeFindReplace,
  ComposeFindReplaceExtension,
  composeFindReplacePluginKey,
  findAllMatches,
  marksForReplacement,
} from './hooks/useComposeFindReplace';
import { ComposeFindReplace } from './ComposeFindReplace';

function makeEditor(content: string): Editor {
  return new Editor({
    extensions: [StarterKit, InsertionMark, DeletionMark, CommentAnchorMark, ComposeFindReplaceExtension],
    content,
  });
}

function decorationCount(editor: Editor): number {
  return composeFindReplacePluginKey.getState(editor.state)?.find().length ?? 0;
}

// ---------------------------------------------------------------------------
// 1. findAllMatches — pure search (highlight source-of-truth + case-sensitivity)
// ---------------------------------------------------------------------------

describe('findAllMatches (pure literal search)', () => {
  it('finds every non-overlapping occurrence, case-insensitive by default', () => {
    const editor = makeEditor('<p>Agreement between the parties. This Agreement governs.</p>');
    const matches = findAllMatches(editor, 'agreement', false);
    expect(matches).toHaveLength(2);
    editor.destroy();
  });

  it('case-sensitive: only exact-case matches are returned', () => {
    const editor = makeEditor('<p>The Party and the party agree.</p>');
    const caseSensitive = findAllMatches(editor, 'Party', true);
    const caseInsensitive = findAllMatches(editor, 'Party', false);
    expect(caseSensitive).toHaveLength(1);
    expect(caseInsensitive).toHaveLength(2);
    editor.destroy();
  });

  it('an empty term matches nothing', () => {
    const editor = makeEditor('<p>Some text.</p>');
    expect(findAllMatches(editor, '', false)).toHaveLength(0);
    editor.destroy();
  });

  it('does not match across a paragraph boundary', () => {
    const editor = makeEditor('<p>foo</p><p>bar</p>');
    expect(findAllMatches(editor, 'foobar', false)).toHaveLength(0);
    editor.destroy();
  });

  it('spans resolve to the exact matched text at their document positions', () => {
    const editor = makeEditor('<p>Acme Corp and Acme Industries.</p>');
    const matches = findAllMatches(editor, 'Acme', false);
    expect(matches).toHaveLength(2);
    for (const span of matches) {
      expect(editor.state.doc.textBetween(span.from, span.to)).toBe('Acme');
    }
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 2. useComposeFindReplace — highlight (decoration-only), case toggle, clear
// ---------------------------------------------------------------------------

describe('useComposeFindReplace — highlight + case-sensitivity (decoration-only)', () => {
  it('typing a term highlights all matches via decorations WITHOUT mutating the document', () => {
    const editor = makeEditor('<p>Agreement between the parties. This Agreement governs.</p>');
    const { result } = renderHook(() => useComposeFindReplace(editor));
    const originalText = editor.getText();

    act(() => {
      result.current.setSearchTerm('Agreement');
    });

    expect(result.current.matchCount).toBe(2);
    expect(decorationCount(editor)).toBe(2);
    // Decoration-only: the document text is byte-identical to before the search.
    expect(editor.getText()).toBe(originalText);
    editor.destroy();
  });

  it('clearing the term removes the highlights', () => {
    const editor = makeEditor('<p>Agreement and Agreement.</p>');
    const { result } = renderHook(() => useComposeFindReplace(editor));

    act(() => {
      result.current.setSearchTerm('Agreement');
    });
    expect(decorationCount(editor)).toBe(2);

    act(() => {
      result.current.setSearchTerm('');
    });
    expect(result.current.matchCount).toBe(0);
    expect(decorationCount(editor)).toBe(0);
    editor.destroy();
  });

  it('the case-sensitivity toggle scopes matching (on: exact-case only; off: both casings)', () => {
    const editor = makeEditor('<p>The Party and the party agree.</p>');
    const { result } = renderHook(() => useComposeFindReplace(editor));

    act(() => {
      result.current.setSearchTerm('Party');
      result.current.setCaseSensitive(true);
    });
    expect(result.current.matchCount).toBe(1);
    expect(decorationCount(editor)).toBe(1);

    act(() => {
      result.current.setCaseSensitive(false);
    });
    expect(result.current.matchCount).toBe(2);
    expect(decorationCount(editor)).toBe(2);
    editor.destroy();
  });

  it('findNext/findPrevious cycle the current match index (wrapping)', () => {
    const editor = makeEditor('<p>x here and x there and x again.</p>');
    const { result } = renderHook(() => useComposeFindReplace(editor));

    act(() => {
      result.current.setSearchTerm('x');
    });
    expect(result.current.currentMatchIndex).toBe(0);

    act(() => {
      result.current.findNext();
    });
    expect(result.current.currentMatchIndex).toBe(1);

    act(() => {
      result.current.findNext();
    });
    expect(result.current.currentMatchIndex).toBe(2);

    act(() => {
      result.current.findNext(); // wraps back to 0
    });
    expect(result.current.currentMatchIndex).toBe(0);

    act(() => {
      result.current.findPrevious(); // wraps to the last match
    });
    expect(result.current.currentMatchIndex).toBe(2);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 3. Replace-all correctness
// ---------------------------------------------------------------------------

describe('useComposeFindReplace.replaceAll', () => {
  it('replaces every match with the replacement text in one operation', () => {
    const editor = makeEditor('<p>Acme signs with Acme on behalf of Acme.</p>');
    const { result } = renderHook(() => useComposeFindReplace(editor));

    act(() => {
      result.current.setSearchTerm('Acme');
      result.current.setReplaceTerm('Globex');
    });
    expect(result.current.matchCount).toBe(3);

    let replaced = 0;
    act(() => {
      replaced = result.current.replaceAll();
    });

    expect(replaced).toBe(3);
    expect(editor.getText()).toBe('Globex signs with Globex on behalf of Globex.');
    expect(editor.getText()).not.toContain('Acme');
    editor.destroy();
  });

  it('replaceCurrent replaces only the current match', () => {
    const editor = makeEditor('<p>Acme and Acme.</p>');
    const { result } = renderHook(() => useComposeFindReplace(editor));

    act(() => {
      result.current.setSearchTerm('Acme');
      result.current.setReplaceTerm('Globex');
    });
    act(() => {
      result.current.replaceCurrent();
    });

    expect(editor.getText()).toBe('Globex and Acme.');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 4. Mark integrity (FR-17 load-bearing constraint) — replace over InsertionMark /
//    DeletionMark / CommentAnchorMark ranges must preserve the marks.
// ---------------------------------------------------------------------------

function marksAt(editor: Editor, text: string): { markName: string; ledgerRef: unknown }[] {
  const found: { markName: string; ledgerRef: unknown }[] = [];
  editor.state.doc.descendants(node => {
    if (node.isText && node.text === text) {
      for (const mark of node.marks) {
        found.push({ markName: mark.type.name, ledgerRef: mark.attrs.ledgerRef });
      }
    }
    return true;
  });
  return found;
}

describe('useComposeFindReplace — mark integrity over tracked-changes ranges (FR-17)', () => {
  it('marksForReplacement resolves the marks active across a matched range', () => {
    const editor = makeEditor(
      '<p>The <span data-compose-mark="insertion" data-ledger-ref="ins1">Agreement</span> is binding.</p>'
    );
    const [span] = findAllMatches(editor, 'Agreement', false);
    const marks = marksForReplacement(editor, span.from, span.to);
    expect(marks.some(m => m.type.name === 'insertion')).toBe(true);
    editor.destroy();
  });

  it('a replace over an InsertionMark range preserves the mark on the replacement text', () => {
    const editor = makeEditor(
      '<p>The <span data-compose-mark="insertion" data-binding="b1" data-ledger-ref="b1@t1">Agreement</span> is binding.</p>'
    );
    // Sanity: the mark is present before the replace.
    expect(marksAt(editor, 'Agreement')).toEqual([{ markName: 'insertion', ledgerRef: 'b1@t1' }]);

    const { result } = renderHook(() => useComposeFindReplace(editor));
    act(() => {
      result.current.setSearchTerm('Agreement');
      result.current.setReplaceTerm('Contract');
    });
    act(() => {
      result.current.replaceCurrent();
    });

    // The replacement text carries the SAME insertion mark + provenance — never stripped or orphaned.
    expect(marksAt(editor, 'Contract')).toEqual([{ markName: 'insertion', ledgerRef: 'b1@t1' }]);
    expect(editor.getText()).toContain('The Contract is binding.');
    editor.destroy();
  });

  it('a replace over a DeletionMark range preserves the mark', () => {
    const editor = makeEditor(
      '<p>Struck <span data-compose-mark="deletion" data-binding="b1" data-ledger-ref="b1@t1">wording</span> here.</p>'
    );
    const { result } = renderHook(() => useComposeFindReplace(editor));
    act(() => {
      result.current.setSearchTerm('wording');
      result.current.setReplaceTerm('phrase');
    });
    act(() => {
      result.current.replaceCurrent();
    });

    expect(marksAt(editor, 'phrase')).toEqual([{ markName: 'deletion', ledgerRef: 'b1@t1' }]);
    editor.destroy();
  });

  it('a replace over a CommentAnchorMark range preserves the mark', () => {
    const editor = makeEditor(
      '<p>See <span data-compose-mark="comment-anchor" data-comment-id="c1">clause 4</span> below.</p>'
    );
    const { result } = renderHook(() => useComposeFindReplace(editor));
    act(() => {
      result.current.setSearchTerm('clause 4');
      result.current.setReplaceTerm('clause 5');
    });
    act(() => {
      result.current.replaceCurrent();
    });

    let found = false;
    editor.state.doc.descendants(node => {
      if (node.isText && node.text === 'clause 5') {
        found = node.marks.some(m => m.type.name === 'commentAnchor' && m.attrs.commentId === 'c1');
      }
      return true;
    });
    expect(found).toBe(true);
    editor.destroy();
  });

  it('replaceAll preserves marks across every occurrence, not just the first', () => {
    const editor = makeEditor(
      '<p><span data-compose-mark="insertion" data-ledger-ref="a@t1">Acme</span> and ' +
        '<span data-compose-mark="insertion" data-ledger-ref="a@t1">Acme</span> agree.</p>'
    );
    const { result } = renderHook(() => useComposeFindReplace(editor));
    act(() => {
      result.current.setSearchTerm('Acme');
      result.current.setReplaceTerm('Globex');
    });
    act(() => {
      result.current.replaceAll();
    });

    expect(marksAt(editor, 'Globex')).toEqual([
      { markName: 'insertion', ledgerRef: 'a@t1' },
      { markName: 'insertion', ledgerRef: 'a@t1' },
    ]);
    editor.destroy();
  });

  it('an unmarked match is replaced as plain text (no mark is fabricated)', () => {
    const editor = makeEditor('<p>Plain Agreement text.</p>');
    const { result } = renderHook(() => useComposeFindReplace(editor));
    act(() => {
      result.current.setSearchTerm('Agreement');
      result.current.setReplaceTerm('Contract');
    });
    act(() => {
      result.current.replaceCurrent();
    });

    expect(marksAt(editor, 'Contract')).toEqual([]);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 5. ComposeFindReplace panel — Fluent v9 UI + dark mode (ADR-021)
// ---------------------------------------------------------------------------

function renderPanel(
  editor: Editor,
  opts: { open?: boolean; theme?: typeof webLightTheme; onClose?: () => void } = {}
) {
  const onClose = opts.onClose ?? jest.fn();
  const result = render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <ComposeFindReplace editor={editor} open={opts.open ?? true} onClose={onClose} />
    </FluentProvider>
  );
  return { ...result, onClose };
}

describe('ComposeFindReplace panel', () => {
  it('renders nothing when closed', () => {
    const editor = makeEditor('<p>Hello.</p>');
    renderPanel(editor, { open: false });
    expect(screen.queryByTestId('compose-find-replace-panel')).not.toBeInTheDocument();
    editor.destroy();
  });

  it('renders find/replace fields, nav buttons, case toggle, and replace buttons when open', () => {
    const editor = makeEditor('<p>Hello.</p>');
    renderPanel(editor);
    expect(screen.getByTestId('compose-find-replace-panel')).toBeInTheDocument();
    expect(screen.getByTestId('compose-find-replace-find-input')).toBeInTheDocument();
    expect(screen.getByTestId('compose-find-replace-replace-input')).toBeInTheDocument();
    expect(screen.getByTestId('compose-find-replace-prev')).toBeInTheDocument();
    expect(screen.getByTestId('compose-find-replace-next')).toBeInTheDocument();
    expect(screen.getByTestId('compose-find-replace-case-toggle')).toBeInTheDocument();
    expect(screen.getByTestId('compose-find-replace-replace')).toBeInTheDocument();
    expect(screen.getByTestId('compose-find-replace-replace-all')).toBeInTheDocument();
    editor.destroy();
  });

  it('typing a term updates the live match-count label', async () => {
    const user = userEvent.setup();
    const editor = makeEditor('<p>Agreement and Agreement.</p>');
    renderPanel(editor);

    await user.type(screen.getByTestId('compose-find-replace-find-input'), 'Agreement');

    expect(screen.getByTestId('compose-find-replace-count')).toHaveTextContent('1 of 2');
    editor.destroy();
  });

  it('a term with zero matches shows a "0 found" label and disables replace/nav', async () => {
    const user = userEvent.setup();
    const editor = makeEditor('<p>Nothing relevant here.</p>');
    renderPanel(editor);

    await user.type(screen.getByTestId('compose-find-replace-find-input'), 'Absent');

    expect(screen.getByTestId('compose-find-replace-count')).toHaveTextContent('0 found');
    expect(screen.getByTestId('compose-find-replace-replace')).toBeDisabled();
    expect(screen.getByTestId('compose-find-replace-replace-all')).toBeDisabled();
    expect(screen.getByTestId('compose-find-replace-prev')).toBeDisabled();
    expect(screen.getByTestId('compose-find-replace-next')).toBeDisabled();
    editor.destroy();
  });

  it('the case toggle reflects pressed state and scopes matching live', async () => {
    const user = userEvent.setup();
    const editor = makeEditor('<p>The Party and the party agree.</p>');
    renderPanel(editor);

    await user.type(screen.getByTestId('compose-find-replace-find-input'), 'Party');
    expect(screen.getByTestId('compose-find-replace-count')).toHaveTextContent('1 of 2');

    const caseToggle = screen.getByTestId('compose-find-replace-case-toggle');
    expect(caseToggle).toHaveAttribute('aria-pressed', 'false');
    await user.click(caseToggle);
    expect(caseToggle).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByTestId('compose-find-replace-count')).toHaveTextContent('1 of 1');
    editor.destroy();
  });

  it('Replace All mutates the document via the wired hook', async () => {
    const user = userEvent.setup();
    const editor = makeEditor('<p>Acme and Acme.</p>');
    renderPanel(editor);

    await user.type(screen.getByTestId('compose-find-replace-find-input'), 'Acme');
    await user.type(screen.getByTestId('compose-find-replace-replace-input'), 'Globex');
    await user.click(screen.getByTestId('compose-find-replace-replace-all'));

    expect(editor.getText()).toBe('Globex and Globex.');
    editor.destroy();
  });

  it('the close button clears the search and calls onClose', async () => {
    const user = userEvent.setup();
    const editor = makeEditor('<p>Agreement.</p>');
    const onClose = jest.fn();
    renderPanel(editor, { onClose });

    await user.type(screen.getByTestId('compose-find-replace-find-input'), 'Agreement');
    expect(decorationCount(editor)).toBe(1);

    await user.click(screen.getByTestId('compose-find-replace-close'));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(decorationCount(editor)).toBe(0);
    editor.destroy();
  });

  it('ADR-021: renders under a dark theme with no hardcoded hex color', () => {
    const editor = makeEditor('<p>Hello.</p>');
    const { container } = renderPanel(editor, { theme: webDarkTheme });
    expect(screen.getByTestId('compose-find-replace-panel')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    editor.destroy();
  });
});
