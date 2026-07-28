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
import { render, screen, waitFor, fireEvent, createEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { CommentAnchorMark } from './marks/CommentAnchorMark';
import { findCommentAnchorRange, type ComposeCommentThreadModel } from './ComposeCommentThread.types';
import { ComposeCommentGutter, layoutCommentGutterCards, parseAdvisoryNote } from './ComposeCommentGutter';

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
// 1b. parseAdvisoryNote — Grounded fact / Advisory judgment split (UAT round-5 #7)
// ---------------------------------------------------------------------------

describe('parseAdvisoryNote', () => {
  it('splits a "Grounded fact: … Advisory judgment: …" note into two labelled segments (UAT round-6 #5 display labels)', () => {
    const segments = parseAdvisoryNote(
      'Grounded fact: The NDA imposes a best-efforts standard. Advisory judgment: The standard requires reasonable care.'
    );
    expect(segments).toEqual([
      { label: 'Flagged clause', body: 'The NDA imposes a best-efforts standard.' },
      { label: 'Assessment says', body: 'The standard requires reasonable care.' },
    ]);
  });

  it('maps an older bare "Judgment" label to "Assessment says"', () => {
    const segments = parseAdvisoryNote('Grounded fact — a fact. Judgment — a judgment.');
    expect(segments.map(s => s.label)).toEqual(['Flagged clause', 'Assessment says']);
  });

  it('returns a single unlabelled segment when there are no recognized labels', () => {
    expect(parseAdvisoryNote('Just a plain note with no aspect labels.')).toEqual([
      { body: 'Just a plain note with no aspect labels.' },
    ]);
  });

  it('keeps any lead prose before the first label as an unlabelled segment', () => {
    const segments = parseAdvisoryNote('Preamble text. Grounded fact: the fact.');
    expect(segments[0]).toEqual({ body: 'Preamble text.' });
    expect(segments[1]).toEqual({ label: 'Flagged clause', body: 'the fact.' });
  });

  it('returns an empty array for empty input', () => {
    expect(parseAdvisoryNote('')).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// 2. ComposeCommentGutter — UI over a real editor
// ---------------------------------------------------------------------------

function makeEditor(
  content = '<p>The receiving party shall retain confidential information indefinitely.</p>'
): Editor {
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

    const coordsSpy = jest
      .spyOn(editor.view, 'coordsAtPos')
      .mockReturnValue({ top: 120, bottom: 140, left: 0, right: 0 });

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

  it('truncates a long comment; clicking the card block reveals the full text (UAT round-2 #5 / round-3 D2)', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });

    const longText =
      'Indefinite retention deviates from the standard 3-year term, and this explanation is deliberately ' +
      'long enough to exceed the collapsed body budget so the show-more affordance appears in the card.';
    expect(longText.length).toBeGreaterThan(140);

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter
            editor={editor}
            threads={[makeThread({ text: longText })]}
            scrollContainerRef={scrollContainerRef}
          />
        </div>
      </FluentProvider>
    );

    // Collapsed: the body is truncated (ellipsis), the tail hidden, the whole card is a button with
    // the double-chevron expand cue (round-3 D2 — no separate "Show more" text button).
    const body = await screen.findByTestId('compose-comment-gutter-body-thread-1');
    expect(body.textContent).toContain('…');
    expect(body).not.toHaveTextContent('affordance appears in the card');
    const card = screen.getByTestId('compose-comment-gutter-card-thread-1');
    expect(card).toHaveAttribute('role', 'button');
    expect(card).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByTestId('compose-comment-gutter-expand-thread-1')).toBeInTheDocument(); // chevron cue

    // Clicking anywhere on the card expands it.
    fireEvent.click(card);
    expect(screen.getByTestId('compose-comment-gutter-body-thread-1')).toHaveTextContent(
      'affordance appears in the card'
    );
    expect(screen.getByTestId('compose-comment-gutter-card-thread-1')).toHaveAttribute('aria-expanded', 'true');

    editor.destroy();
  });

  it('resizes the pane when the left-edge handle is dragged left (UAT round-3 D1)', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });
    const onWidthChange = jest.fn();

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter
            editor={editor}
            threads={[makeThread()]}
            scrollContainerRef={scrollContainerRef}
            width={220}
            onWidthChange={onWidthChange}
          />
        </div>
      </FluentProvider>
    );

    const handle = await screen.findByTestId('compose-comment-gutter-resize');
    // jsdom lacks the Pointer Capture API — stub it so the handlers don't throw.
    handle.setPointerCapture = jest.fn();
    handle.releasePointerCapture = jest.fn();
    handle.hasPointerCapture = jest.fn().mockReturnValue(true);

    // jsdom's PointerEvent does not carry clientX from the fireEvent init, so build the event and
    // define clientX explicitly.
    const firePointer = (type: 'pointerDown' | 'pointerMove' | 'pointerUp', clientX: number): void => {
      const ev = createEvent[type](handle, { pointerId: 1 });
      Object.defineProperty(ev, 'clientX', { get: () => clientX });
      fireEvent(handle, ev);
    };

    // The gutter is on the RIGHT — dragging the handle LEFT (smaller clientX) widens it by the delta.
    firePointer('pointerDown', 500);
    firePointer('pointerMove', 440); // 60px left → +60 → 280
    expect(onWidthChange).toHaveBeenLastCalledWith(280);

    // Dragging far left clamps to the max bound (480).
    firePointer('pointerMove', 0);
    expect(onWidthChange).toHaveBeenLastCalledWith(480);

    firePointer('pointerUp', 0);
    editor.destroy();
  });

  it('renders no resize handle when onWidthChange is not wired (fixed-width mount)', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter editor={editor} threads={[makeThread()]} scrollContainerRef={scrollContainerRef} />
        </div>
      </FluentProvider>
    );
    await screen.findByTestId('compose-comment-gutter-card-thread-1');
    expect(screen.queryByTestId('compose-comment-gutter-resize')).not.toBeInTheDocument();
    editor.destroy();
  });

  it('opens a popover with the standard clause text when the "Standard" link is clicked (UAT round-3 D3)', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });
    const resolveStandardText = jest
      .fn()
      .mockResolvedValue('Required: use solely for the Purpose; protect with at least reasonable care.');

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter
            editor={editor}
            threads={[makeThread({ standardRef: 'B5 - Use & disclosure obligations' })]}
            scrollContainerRef={scrollContainerRef}
            resolveStandardText={resolveStandardText}
          />
        </div>
      </FluentProvider>
    );

    const link = await screen.findByTestId('compose-comment-gutter-standard-thread-1');
    expect(link).toHaveTextContent('Standard: B5 - Use & disclosure obligations');

    fireEvent.click(link);
    await waitFor(() => expect(resolveStandardText).toHaveBeenCalledWith('B5 - Use & disclosure obligations'));
    expect(
      await screen.findByText('Required: use solely for the Purpose; protect with at least reasonable care.')
    ).toBeInTheDocument();

    editor.destroy();
  });

  it('renders standardRef as plain text when no resolver is wired (UAT round-3 D3)', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter
            editor={editor}
            threads={[makeThread({ standardRef: 'B5 - Use & disclosure obligations' })]}
            scrollContainerRef={scrollContainerRef}
          />
        </div>
      </FluentProvider>
    );

    const card = await screen.findByTestId('compose-comment-gutter-card-thread-1');
    expect(card).toHaveTextContent('Standard: B5 - Use & disclosure obligations');
    // No resolver → no clickable link button.
    expect(screen.queryByTestId('compose-comment-gutter-standard-thread-1')).not.toBeInTheDocument();

    editor.destroy();
  });

  it('a short comment is not clickable and shows no expand cue (UAT round-2 #5)', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter editor={editor} threads={[makeThread()]} scrollContainerRef={scrollContainerRef} />
        </div>
      </FluentProvider>
    );

    const card = await screen.findByTestId('compose-comment-gutter-card-thread-1');
    // The default thread text is short (< the collapsed budget) — not a button, no cue.
    expect(card).not.toHaveAttribute('role', 'button');
    expect(screen.queryByTestId('compose-comment-gutter-expand-thread-1')).not.toBeInTheDocument();

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
        .mockImplementation((pos: number) =>
          pos < 20 ? { top: 0, bottom: 0, left: 0, right: 0 } : { top: 10, bottom: 10, left: 0, right: 0 }
        );

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
          threads={[
            makeThread({ id: 'thread-plain', riskLevel: undefined, sectionRef: undefined, standardRef: undefined }),
          ]}
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

  // -------------------------------------------------------------------------
  // UAT round-4 #8 — selection (bidirectional linked highlight; select-vs-expand)
  // -------------------------------------------------------------------------

  it('calls onSelectThread when a card is clicked (selection wired) — the card is a select button', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });
    const onSelectThread = jest.fn();

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter
            editor={editor}
            threads={[makeThread()]}
            scrollContainerRef={scrollContainerRef}
            onSelectThread={onSelectThread}
          />
        </div>
      </FluentProvider>
    );

    const card = await screen.findByTestId('compose-comment-gutter-card-thread-1');
    // With selection wired, even a SHORT (non-truncatable) card is a select button.
    expect(card).toHaveAttribute('role', 'button');
    expect(card).toHaveAttribute('aria-pressed', 'false');
    fireEvent.click(card);
    expect(onSelectThread).toHaveBeenCalledWith('thread-1');
    editor.destroy();
  });

  it('renders the selected card with its selected (gray) state via aria-pressed', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter
            editor={editor}
            threads={[makeThread()]}
            scrollContainerRef={scrollContainerRef}
            onSelectThread={jest.fn()}
            selectedThreadId="thread-1"
          />
        </div>
      </FluentProvider>
    );

    const card = await screen.findByTestId('compose-comment-gutter-card-thread-1');
    expect(card).toHaveAttribute('aria-pressed', 'true');
    editor.destroy();
  });

  it('when selection is wired, a truncatable card SELECTS on card click and EXPANDS only via the cue button', async () => {
    const longText =
      'This advisory explanation is deliberately much longer than the collapsed body budget so the ' +
      'card is truncatable and a distinct expand affordance appears in the card body region below.';
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });
    const onSelectThread = jest.fn();

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter
            editor={editor}
            threads={[makeThread({ text: longText })]}
            scrollContainerRef={scrollContainerRef}
            onSelectThread={onSelectThread}
          />
        </div>
      </FluentProvider>
    );

    const card = await screen.findByTestId('compose-comment-gutter-card-thread-1');
    const body = screen.getByTestId('compose-comment-gutter-body-thread-1');
    expect(body.textContent).toContain('…'); // starts collapsed

    // Clicking the card body SELECTS (does not expand).
    fireEvent.click(card);
    expect(onSelectThread).toHaveBeenCalledWith('thread-1');
    expect(screen.getByTestId('compose-comment-gutter-body-thread-1').textContent).toContain('…');

    // The cue is a real button; clicking it EXPANDS without also selecting.
    onSelectThread.mockClear();
    fireEvent.click(screen.getByTestId('compose-comment-gutter-expand-thread-1'));
    expect(screen.getByTestId('compose-comment-gutter-body-thread-1')).toHaveTextContent(
      'expand affordance appears in the card body'
    );
    expect(onSelectThread).not.toHaveBeenCalled();
    editor.destroy();
  });

  // -------------------------------------------------------------------------
  // UAT round-5 — location line (#6), Grounded fact / Advisory judgment (#7)
  // -------------------------------------------------------------------------

  it('renders a clear location line (Pg · Sec · Para) with no § / ¶ glyphs (UAT round-5 #6)', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter
            editor={editor}
            threads={[makeThread({ sectionRef: 'Section 4.2, para 2 (p. 3)' })]}
            scrollContainerRef={scrollContainerRef}
          />
        </div>
      </FluentProvider>
    );

    const card = await screen.findByTestId('compose-comment-gutter-card-thread-1');
    expect(card).toHaveTextContent('Pg 3 · Sec 4.2 · Para 2');
    expect(card).not.toHaveTextContent('§');
    expect(card).not.toHaveTextContent('¶');
    editor.destroy();
  });

  it('renders Grounded fact / Advisory judgment as separate labelled paragraphs when expanded (UAT round-5 #7)', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });
    // A short note (< collapse budget) renders structured immediately (no expand needed).
    const note = 'Grounded fact: A best-efforts standard. Advisory judgment: Needs reasonable care.';

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter
            editor={editor}
            threads={[makeThread({ text: note })]}
            scrollContainerRef={scrollContainerRef}
          />
        </div>
      </FluentProvider>
    );

    const body = await screen.findByTestId('compose-comment-gutter-body-thread-1');
    // UAT round-6 #5 — the reviewer's display labels.
    expect(body).toHaveTextContent('Flagged clause');
    expect(body).toHaveTextContent('A best-efforts standard.');
    expect(body).toHaveTextContent('Assessment says');
    expect(body).toHaveTextContent('Needs reasonable care.');
    // The labels are rendered as their own (semibold) elements, not inline with the body text.
    const labelEls = Array.from(body.querySelectorAll('*')).filter(
      el => el.textContent === 'Flagged clause' || el.textContent === 'Assessment says'
    );
    expect(labelEls.length).toBeGreaterThanOrEqual(2);
    editor.destroy();
  });

  it('renders a ⋮ AI-tools menu per note and runs a tool against the thread on click (UAT round-8 #3/#4)', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });
    const onRunNoteTool = jest.fn();

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter
            editor={editor}
            threads={[makeThread()]}
            scrollContainerRef={scrollContainerRef}
            noteTools={[{ id: 'compose-draft-alternative', label: 'Draft compliant alternative' }]}
            onRunNoteTool={onRunNoteTool}
          />
        </div>
      </FluentProvider>
    );

    const trigger = await screen.findByTestId('compose-comment-gutter-tools-thread-1');
    fireEvent.click(trigger);
    const item = await screen.findByTestId('compose-comment-gutter-tool-thread-1-compose-draft-alternative');
    expect(item).toHaveTextContent('Draft compliant alternative');
    fireEvent.click(item);
    expect(onRunNoteTool).toHaveBeenCalledWith('thread-1', 'compose-draft-alternative');
    editor.destroy();
  });

  it('renders no ⋮ menu when no note tools are wired', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });
    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter editor={editor} threads={[makeThread()]} scrollContainerRef={scrollContainerRef} />
        </div>
      </FluentProvider>
    );
    await screen.findByTestId('compose-comment-gutter-card-thread-1');
    expect(screen.queryByTestId('compose-comment-gutter-tools-thread-1')).not.toBeInTheDocument();
    editor.destroy();
  });

  it('shows the RENAMED label ("Flagged clause") even when the note is COLLAPSED (UAT round-7 follow-up)', async () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'thread-1', 1, 20);
    jest.spyOn(editor.view, 'coordsAtPos').mockReturnValue({ top: 100, bottom: 120, left: 0, right: 0 });
    // A LONG note (> collapse budget) so the card starts COLLAPSED — the collapsed preview must still use
    // the renamed label, never the model's raw "Grounded fact".
    const note =
      'Grounded fact: ' +
      'The NDA imposes a best-efforts protection standard which is materially weaker than the firm ' +
      'standard and this sentence is deliberately long enough to exceed the collapse budget. ' +
      'Advisory judgment: The standard requires at least reasonable care.';

    const scrollContainerRef = React.createRef<HTMLDivElement>();
    render(
      <FluentProvider theme={webLightTheme}>
        <div ref={scrollContainerRef}>
          <ComposeCommentGutter
            editor={editor}
            threads={[makeThread({ text: note })]}
            scrollContainerRef={scrollContainerRef}
          />
        </div>
      </FluentProvider>
    );

    const body = await screen.findByTestId('compose-comment-gutter-body-thread-1');
    // Collapsed: renamed label shown, raw model label never shown, and the "…" truncation cue present.
    expect(body).toHaveTextContent('Flagged clause');
    expect(body).not.toHaveTextContent('Grounded fact');
    expect(body.textContent).toContain('…');
    editor.destroy();
  });
});
