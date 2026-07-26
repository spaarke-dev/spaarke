/**
 * ComposeCommentGutter.test.tsx — right-gutter comment layout (ai-advanced-capabilities-nda-r1 task
 * 032).
 *
 * Two layers, mirroring `NdaReviewSummaryPanel.test.tsx`'s convention:
 *  1. `layoutCommentGutterCards` — pure collision/stacking function (no editor/DOM dependency).
 *  2. UI — `ComposeCommentGutter` rendered over a REAL headless TipTap editor (same harness as
 *     `ComposeCommentThread.test.tsx`): live-position resolution via the mark's CURRENT span (never
 *     the thread's stale `anchorText`), `coordsAtPos`-driven placement, the risk-badge/citation
 *     metadata passthrough, an unresolvable anchor being omitted (never guessed), the
 *     estimate-then-re-measure follow-up pass (code-review self-gate finding, task 032), and an
 *     ADR-021 dark-mode check.
 */
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { CommentAnchorMark } from './marks/CommentAnchorMark';
import { findCommentAnchorRange, type ComposeCommentThreadModel } from './ComposeCommentThread.types';
import { ComposeCommentGutter, layoutCommentGutterCards } from './ComposeCommentGutter';

// ---------------------------------------------------------------------------
// 1. layoutCommentGutterCards — pure collision/stacking
// ---------------------------------------------------------------------------

describe('layoutCommentGutterCards', () => {
  it('leaves well-separated cards at their raw position', () => {
    const result = layoutCommentGutterCards(
      [
        { id: 'a', top: 0 },
        { id: 'b', top: 500 },
      ],
      new Map([
        ['a', 40],
        ['b', 40],
      ])
    );
    expect(result).toEqual({ a: 0, b: 500 });
  });

  it('pushes an overlapping second card down to clear the first (height + gap)', () => {
    const result = layoutCommentGutterCards(
      [
        { id: 'a', top: 0 },
        { id: 'b', top: 10 }, // overlaps "a" (height 40)
      ],
      new Map([
        ['a', 40],
        ['b', 40],
      ])
    );
    expect(result.a).toBe(0);
    expect(result.b).toBeGreaterThanOrEqual(0 + 40 + 8); // CARD_GAP_PX = 8
  });

  it('chain-pushes three overlapping cards in ascending top order, regardless of input order', () => {
    const result = layoutCommentGutterCards(
      [
        { id: 'c', top: 5 },
        { id: 'a', top: 0 },
        { id: 'b', top: 2 },
      ],
      new Map([
        ['a', 30],
        ['b', 30],
        ['c', 30],
      ])
    );
    // a is first (top 0), b pushed to clear a, c pushed to clear b.
    expect(result.a).toBe(0);
    expect(result.b).toBeGreaterThanOrEqual(result.a + 30 + 8);
    expect(result.c).toBeGreaterThanOrEqual(result.b + 30 + 8);
  });

  it('falls back to the default estimated height for an unmeasured card', () => {
    const result = layoutCommentGutterCards(
      [
        { id: 'a', top: 0 },
        { id: 'b', top: 1 },
      ],
      new Map() // no measured heights — both fall back to DEFAULT_CARD_HEIGHT_PX (96)
    );
    expect(result.b).toBeGreaterThanOrEqual(0 + 96 + 8);
  });

  it('returns an empty layout for an empty input', () => {
    expect(layoutCommentGutterCards([], new Map())).toEqual({});
  });
});

// ---------------------------------------------------------------------------
// 2. ComposeCommentGutter — UI over a real editor
// ---------------------------------------------------------------------------

function makeEditor(content = '<p>The receiving party shall retain confidential information indefinitely.</p>'): Editor {
  return new Editor({
    extensions: [StarterKit, CommentAnchorMark],
    content,
  });
}

/** Applies a commentAnchor mark over [from, to) directly (mirrors createThread's own tr.addMark). */
function applyCommentAnchor(editor: Editor, commentId: string, from: number, to: number): void {
  const markType = editor.state.schema.marks.commentAnchor;
  const tr = editor.state.tr.addMark(from, to, markType.create({ commentId }));
  editor.view.dispatch(tr);
}

function makeThread(overrides: Partial<ComposeCommentThreadModel> = {}): ComposeCommentThreadModel {
  return {
    id: 'thread-1',
    author: 'AI Advisory Review',
    timestamp: new Date().toISOString(),
    text: 'Indefinite retention deviates from the standard 3-year term.',
    anchorText: 'STALE — should never be read for positioning',
    resolved: false,
    replies: [],
    ...overrides,
  };
}

describe('ComposeCommentGutter', () => {
  it('resolves the LIVE anchor span (never the stale anchorText) and positions the card via coordsAtPos', async () => {
    const editor = makeEditor();
    // "The receiving party shall retain confidential information indefinitely." — anchor the first
    // few words, deliberately NOT matching `anchorText` above (proving position comes from the mark).
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    const liveSpan = findCommentAnchorRange(editor.state.doc, 'thread-1');
    expect(liveSpan).not.toBeNull();

    const coordsSpy = jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 120, bottom: 140, left: 0, right: 0 });

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter
            editor={editor}
            threads={[makeThread({ riskLevel: 'High', sectionRef: '3.2', standardRef: 'B3 - Retention' })]}
            scrollContainerRef={scrollContainerRef}
          />
        </div>
      </FluentProvider>
    );

    const card = await screen.findByTestId('compose-comment-gutter-card-thread-1');
    expect(card).toBeInTheDocument();
    // coordsAtPos was called with the LIVE resolved position, not any value derived from anchorText.
    expect(coordsSpy).toHaveBeenCalledWith(liveSpan!.from);
    // The rail's own bounding rect is 0 in jsdom, so top = coords.top - 0 = 120.
    expect(card.style.top).toBe('120px');

    expect(screen.getByTestId('compose-comment-gutter-risk-thread-1')).toHaveTextContent('High');
    expect(card).toHaveTextContent('3.2');
    expect(card).toHaveTextContent('Standard: B3 - Retention');
    expect(card).toHaveTextContent('Indefinite retention deviates from the standard 3-year term.');

    editor.destroy();
  });

  it('re-measures with REAL card heights after first paint, tightening the initial DEFAULT_CARD_HEIGHT_PX-based stacking estimate', async () => {
    // Code-review self-gate finding (task 032): the FIRST pass positions every not-yet-mounted card
    // using the DEFAULT_CARD_HEIGHT_PX (96) estimate (no card is in the DOM yet to measure). Once that
    // pass mounts the cards, `recompute` schedules exactly one follow-up pass (via requestAnimationFrame)
    // that reads each card's REAL `offsetHeight` and re-runs the collision/stacking math — proving the
    // estimate never silently persists past first paint. jsdom's `offsetHeight` is always 0 by default,
    // so this test overrides it to a small, deterministic value to make the tightened stacking visible.
    const realOffsetHeight = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetHeight');
    Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 40 });

    try {
      const editor = makeEditor();
      applyCommentAnchor(editor, 'thread-1', 1, 20);
      applyCommentAnchor(editor, 'thread-2', 21, 30);
      jest
        .spyOn(editor.view, 'coordsAtPos')
        .mockImplementation((pos: number) => (pos < 20 ? { top: 0, bottom: 0, left: 0, right: 0 } : { top: 10, bottom: 10, left: 0, right: 0 }));

      const scrollContainerRef = React.createRef<HTMLDivElement>();
      render(
        <FluentProvider theme={webLightTheme}>
          <ComposeCommentGutter
            editor={editor}
            threads={[makeThread({ id: 'thread-1' }), makeThread({ id: 'thread-2' })]}
            scrollContainerRef={scrollContainerRef}
          />
        </FluentProvider>
      );

      // First paint positions thread-2 using the 96px ESTIMATE height (neither card is mounted yet to
      // measure) — pushed to 0 + 96 + 8 = 104. `findByTestId`'s own polling can race past the
      // requestAnimationFrame follow-up pass before this assertion runs, so this only asserts the
      // EVENTUAL, converged state: the follow-up pass re-measures both cards at the REAL (mocked) 40px
      // height and tightens the stacking to 0 + 40 + 8 = 48 — proving the 96px estimate does not
      // silently persist.
      const cardTwo = await screen.findByTestId('compose-comment-gutter-card-thread-2');
      await waitFor(() => expect(cardTwo.style.top).toBe('48px'));

      editor.destroy();
    } finally {
      if (realOffsetHeight) Object.defineProperty(HTMLElement.prototype, 'offsetHeight', realOffsetHeight);
    }
  });

  it('omits a card whose anchor mark is no longer present in the document (never guesses a fallback position)', () => {
    const editor = makeEditor();
    // No commentAnchor mark applied for 'thread-missing' — simulates a later edit that deleted the
    // anchored text.
    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <ComposeCommentGutter
          editor={editor}
          threads={[makeThread({ id: 'thread-missing' })]}
          scrollContainerRef={scrollContainerRef}
        />
      </FluentProvider>
    );

    expect(screen.queryByTestId('compose-comment-gutter-card-thread-missing')).not.toBeInTheDocument();
    editor.destroy();
  });

  it('renders nothing when there are no threads', () => {
    const editor = makeEditor();
    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <ComposeCommentGutter editor={editor} threads={[]} scrollContainerRef={scrollContainerRef} />
      </FluentProvider>
    );
    expect(screen.queryByTestId('compose-comment-gutter')).not.toBeInTheDocument();
    editor.destroy();
  });

  it('renders nothing when the editor is not yet mounted', () => {
    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <ComposeCommentGutter editor={null} threads={[makeThread()]} scrollContainerRef={scrollContainerRef} />
      </FluentProvider>
    );
    expect(screen.queryByTestId('compose-comment-gutter')).not.toBeInTheDocument();
  });

  it('omits the risk badge / standard citation when the thread carries no metadata (session comments)', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-plain', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 10, bottom: 30, left: 0, right: 0 });

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <ComposeCommentGutter
          editor={editor}
          threads={[makeThread({ id: 'thread-plain', riskLevel: undefined, sectionRef: undefined, standardRef: undefined })]}
          scrollContainerRef={scrollContainerRef}
        />
      </FluentProvider>
    );

    const card = await screen.findByTestId('compose-comment-gutter-card-thread-plain');
    expect(screen.queryByTestId('compose-comment-gutter-risk-thread-plain')).not.toBeInTheDocument();
    expect(card).toHaveTextContent('Comment'); // header falls back to the generic label
    editor.destroy();
  });

  it('ADR-021: renders with only semantic tokens — no hex literals — in light and dark mode', async () => {
    const editorLight = makeEditor();
    applyCommentAnchor(editorLight, 'thread-1', 1, 20);
    jest.spyOn(editorLight.view, 'coordsAtPos').mockReturnValue({ top: 30, bottom: 50, left: 0, right: 0 });
    const scrollRefLight = React.createRef<HTMLDivElement>();
    const light = render(
      <FluentProvider theme={webLightTheme}>
        <ComposeCommentGutter
          editor={editorLight}
          threads={[makeThread({ riskLevel: 'Critical' })]}
          scrollContainerRef={scrollRefLight}
        />
      </FluentProvider>
    );
    await light.findByTestId('compose-comment-gutter-card-thread-1');
    expect(light.container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    light.unmount();
    editorLight.destroy();

    const editorDark = makeEditor();
    applyCommentAnchor(editorDark, 'thread-1', 1, 20);
    jest.spyOn(editorDark.view, 'coordsAtPos').mockReturnValue({ top: 30, bottom: 50, left: 0, right: 0 });
    const scrollRefDark = React.createRef<HTMLDivElement>();
    const dark = render(
      <FluentProvider theme={webDarkTheme}>
        <ComposeCommentGutter
          editor={editorDark}
          threads={[makeThread({ riskLevel: 'Critical' })]}
          scrollContainerRef={scrollRefDark}
        />
      </FluentProvider>
    );
    await dark.findByTestId('compose-comment-gutter-card-thread-1');
    expect(dark.container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    dark.unmount();
    editorDark.destroy();
  });
});
