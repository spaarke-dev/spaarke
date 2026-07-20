/**
 * ComposeCommentThread.test.tsx — FR-23 richer comment-thread UI (spaarkeai-compose-r3 task 044).
 *
 * Three layers, mirroring `ComposeFindReplace.test.tsx` / `usePendingRedline.test.tsx`'s convention:
 *  1. HOOK LOGIC — `useComposeCommentThreads` driven via `renderHook` over a REAL headless TipTap
 *     `@tiptap/core` Editor (StarterKit + `CommentAnchorMark`, the same schema-registration path
 *     ComposeEditor uses). Covers: create-on-selection (mark application + no-op on a collapsed
 *     selection/empty text), reply ordering, resolve, and the FR-25 `importThreads` upsert seam.
 *  2. PERSISTENCE SHAPE — `composeCommentThreadsToDocxAnnotations` (pure) asserts the native
 *     `w:comment`-compatible mapping (root + each reply → its own `Comment`-kind annotation, same
 *     anchor) and the "no anchorText → skipped" guard.
 *  3. UI — `ComposeCommentThread` rendered with a real editor instance: thread author/timestamp,
 *     create-on-selection via the composer, reply-appends-in-order, resolve, the SCOPE GUARD flat-
 *     render of a reply set carrying `parentReplyId` provenance, and an ADR-021 dark-mode check.
 */
import * as React from 'react';
import { render, screen, act, renderHook } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { CommentAnchorMark } from './marks/CommentAnchorMark';
import { useComposeCommentThreads } from './hooks/useComposeCommentThreads';
import { ComposeCommentThread } from './ComposeCommentThread';
import { composeCommentThreadsToDocxAnnotations, type ComposeCommentThreadModel } from './ComposeCommentThread.types';
import { DocxTrackChangeKind } from './useComposeWordShuttle';

function makeEditor(content = '<p>Hello world. Second sentence.</p>'): Editor {
  return new Editor({
    extensions: [StarterKit, CommentAnchorMark],
    content,
  });
}

function commentAnchorIds(editor: Editor): string[] {
  const ids: string[] = [];
  editor.state.doc.descendants(node => {
    if (node.isText) {
      for (const mark of node.marks) {
        if (mark.type.name === 'commentAnchor' && typeof mark.attrs.commentId === 'string') {
          ids.push(mark.attrs.commentId);
        }
      }
    }
    return true;
  });
  return ids;
}

// ---------------------------------------------------------------------------
// 1. useComposeCommentThreads — create / reply / resolve / import
// ---------------------------------------------------------------------------

describe('useComposeCommentThreads — create/reply/resolve/import', () => {
  it('createThread applies a commentAnchor mark to the resolved range and records the root comment', () => {
    const editor = makeEditor();
    const { result } = renderHook(() => useComposeCommentThreads(editor, 'Alex Author'));

    let id: string | null = null;
    act(() => {
      id = result.current.createThread('Please clarify.', { from: 1, to: 6 }); // "Hello"
    });

    expect(id).not.toBeNull();
    expect(result.current.threads).toHaveLength(1);
    expect(result.current.threads[0]).toMatchObject({
      id,
      author: 'Alex Author',
      text: 'Please clarify.',
      anchorText: 'Hello',
      resolved: false,
      replies: [],
    });
    expect(commentAnchorIds(editor)).toEqual([id]);
    editor.destroy();
  });

  it('createThread with a collapsed selection is a no-op (returns null, no mark applied)', () => {
    const editor = makeEditor();
    const { result } = renderHook(() => useComposeCommentThreads(editor, 'Alex Author'));

    let id: string | null = 'not-null';
    act(() => {
      id = result.current.createThread('Note', { from: 3, to: 3 });
    });

    expect(id).toBeNull();
    expect(result.current.threads).toHaveLength(0);
    expect(commentAnchorIds(editor)).toEqual([]);
    editor.destroy();
  });

  it('createThread with empty/whitespace-only text is a no-op', () => {
    const editor = makeEditor();
    const { result } = renderHook(() => useComposeCommentThreads(editor, 'Alex Author'));

    let id: string | null = 'not-null';
    act(() => {
      id = result.current.createThread('   ', { from: 1, to: 6 });
    });

    expect(id).toBeNull();
    expect(result.current.threads).toHaveLength(0);
    editor.destroy();
  });

  it('createThread falls back to the editor current selection when no range is supplied', () => {
    const editor = makeEditor();
    editor.commands.setTextSelection({ from: 1, to: 6 });
    const { result } = renderHook(() => useComposeCommentThreads(editor, 'Alex Author'));

    let id: string | null = null;
    act(() => {
      id = result.current.createThread('Note');
    });

    expect(id).not.toBeNull();
    expect(result.current.threads[0].anchorText).toBe('Hello');
    editor.destroy();
  });

  it('reply appends in order', () => {
    const editor = makeEditor();
    const { result } = renderHook(() => useComposeCommentThreads(editor, 'Alex Author'));

    let id: string | null = null;
    act(() => {
      id = result.current.createThread('Root comment', { from: 1, to: 6 });
    });
    act(() => {
      result.current.reply(id as string, 'First reply');
    });
    act(() => {
      result.current.reply(id as string, 'Second reply');
    });

    const thread = result.current.threads.find(t => t.id === id);
    expect(thread?.replies.map(r => r.text)).toEqual(['First reply', 'Second reply']);
    editor.destroy();
  });

  it('reply with empty text is a no-op', () => {
    const editor = makeEditor();
    const { result } = renderHook(() => useComposeCommentThreads(editor, 'Alex Author'));

    let id: string | null = null;
    act(() => {
      id = result.current.createThread('Root comment', { from: 1, to: 6 });
    });
    act(() => {
      result.current.reply(id as string, '   ');
    });

    expect(result.current.threads.find(t => t.id === id)?.replies).toHaveLength(0);
    editor.destroy();
  });

  it('resolve sets resolved on the target thread only', () => {
    const editor = makeEditor();
    const { result } = renderHook(() => useComposeCommentThreads(editor, 'Alex Author'));

    let id1: string | null = null;
    let id2: string | null = null;
    act(() => {
      id1 = result.current.createThread('Thread 1', { from: 1, to: 6 });
    });
    act(() => {
      id2 = result.current.createThread('Thread 2', { from: 8, to: 13 }); // "world"
    });
    act(() => {
      result.current.resolve(id1 as string);
    });

    expect(result.current.threads.find(t => t.id === id1)?.resolved).toBe(true);
    expect(result.current.threads.find(t => t.id === id2)?.resolved).toBe(false);
    editor.destroy();
  });

  it('importThreads upserts by id — new threads added, existing ids replaced', () => {
    const editor = makeEditor();
    const { result } = renderHook(() => useComposeCommentThreads(editor, 'Alex Author'));

    let id: string | null = null;
    act(() => {
      id = result.current.createThread('Original', { from: 1, to: 6 });
    });
    act(() => {
      result.current.importThreads([
        {
          id: id as string,
          author: 'Imported',
          timestamp: '2026-01-01T00:00:00.000Z',
          text: 'Replaced',
          resolved: false,
          replies: [],
        },
        {
          id: 'imported-2',
          author: 'Word User',
          timestamp: '2026-01-02T00:00:00.000Z',
          text: 'A recovered comment',
          anchorText: 'world',
          resolved: false,
          replies: [],
        },
      ]);
    });

    expect(result.current.threads).toHaveLength(2);
    expect(result.current.threads.find(t => t.id === id)?.text).toBe('Replaced');
    expect(result.current.threads.find(t => t.id === 'imported-2')?.author).toBe('Word User');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 2. composeCommentThreadsToDocxAnnotations — persistence shape (pure)
// ---------------------------------------------------------------------------

describe('composeCommentThreadsToDocxAnnotations (persistence shape → native w:comment)', () => {
  it('maps a thread with replies to one Comment-kind annotation per comment, all anchored to the same targetText', () => {
    const threads: ComposeCommentThreadModel[] = [
      {
        id: 't1',
        author: 'Alex Author',
        timestamp: '2026-01-01T00:00:00.000Z',
        text: 'Root comment',
        anchorText: 'Hello',
        resolved: false,
        replies: [
          { id: 'r1', author: 'Sam Reviewer', timestamp: '2026-01-01T01:00:00.000Z', text: 'First reply' },
          { id: 'r2', author: 'Alex Author', timestamp: '2026-01-01T02:00:00.000Z', text: 'Second reply' },
        ],
      },
    ];

    const annotations = composeCommentThreadsToDocxAnnotations(threads);

    expect(annotations).toHaveLength(3);
    expect(annotations.every(a => a.kind === DocxTrackChangeKind.Comment)).toBe(true);
    expect(annotations.every(a => a.targetText === 'Hello')).toBe(true);
    expect(annotations.map(a => a.commentText)).toEqual(['Root comment', 'First reply', 'Second reply']);
    expect(annotations.map(a => a.author)).toEqual(['Alex Author', 'Sam Reviewer', 'Alex Author']);
  });

  it('skips a thread with no captured anchorText (the server requires a non-empty targetText for a Comment)', () => {
    const threads: ComposeCommentThreadModel[] = [
      {
        id: 't1',
        author: 'Alex Author',
        timestamp: '2026-01-01T00:00:00.000Z',
        text: 'No anchor',
        resolved: false,
        replies: [],
      },
    ];

    expect(composeCommentThreadsToDocxAnnotations(threads)).toEqual([]);
  });

  it('an empty thread list maps to an empty annotation list', () => {
    expect(composeCommentThreadsToDocxAnnotations([])).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// 3. ComposeCommentThread panel — Fluent v9 UI
// ---------------------------------------------------------------------------

function renderPanel(
  editor: Editor,
  opts: {
    open?: boolean;
    theme?: typeof webLightTheme;
    onClose?: () => void;
    pendingRange?: { from: number; to: number; preview: string } | null;
    onThreadCreated?: (id: string) => void;
    initialThreads?: ComposeCommentThreadModel[];
  } = {}
) {
  const onClose = opts.onClose ?? jest.fn();
  const onThreadCreated = opts.onThreadCreated ?? jest.fn();
  const result = render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <ComposeCommentThread
        editor={editor}
        open={opts.open ?? true}
        onClose={onClose}
        author="Alex Author"
        pendingRange={opts.pendingRange}
        onThreadCreated={onThreadCreated}
        initialThreads={opts.initialThreads}
      />
    </FluentProvider>
  );
  return { ...result, onClose, onThreadCreated };
}

describe('ComposeCommentThread panel', () => {
  it('renders nothing when closed', () => {
    const editor = makeEditor();
    renderPanel(editor, { open: false });
    expect(screen.queryByTestId('compose-comment-thread-panel')).not.toBeInTheDocument();
    editor.destroy();
  });

  it('shows a "select text" hint (not the composer) when there is no pendingRange', () => {
    const editor = makeEditor();
    renderPanel(editor, { pendingRange: null });
    expect(screen.getByTestId('compose-comment-new-hint')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-comment-new-composer')).not.toBeInTheDocument();
    editor.destroy();
  });

  it('creating a thread on a selection: typing + Comment applies the mark, renders the thread, and notifies the host', async () => {
    const user = userEvent.setup();
    const editor = makeEditor();
    const { onThreadCreated } = renderPanel(editor, { pendingRange: { from: 1, to: 6, preview: 'Hello' } });

    expect(screen.getByTestId('compose-comment-new-composer')).toBeInTheDocument();
    expect(screen.getByTestId('compose-comment-new-composer-preview')).toHaveTextContent('On: “Hello”');
    expect(screen.getByTestId('compose-comment-new-post')).toBeDisabled();

    await user.type(screen.getByTestId('compose-comment-new-input'), 'Please clarify this clause.');
    expect(screen.getByTestId('compose-comment-new-post')).toBeEnabled();
    await user.click(screen.getByTestId('compose-comment-new-post'));

    expect(onThreadCreated).toHaveBeenCalledTimes(1);
    expect(commentAnchorIds(editor)).toHaveLength(1);
    editor.destroy();
  });

  it('a thread renders with author and timestamp (FR-23 acceptance)', () => {
    const editor = makeEditor();
    const thread: ComposeCommentThreadModel = {
      id: 't1',
      author: 'Alex Author',
      timestamp: '2026-01-01T12:00:00.000Z',
      text: 'Root comment',
      anchorText: 'Hello',
      resolved: false,
      replies: [],
    };
    renderPanel(editor, { initialThreads: [thread] });

    expect(screen.getByTestId('compose-comment-author-t1')).toHaveTextContent('Alex Author');
    expect(screen.getByTestId('compose-comment-timestamp-t1')).not.toHaveTextContent('');
    expect(screen.getByTestId('compose-comment-body-t1')).toHaveTextContent('Root comment');
    editor.destroy();
  });

  it('a reply appends in order (rendered below the root, after existing replies)', async () => {
    const user = userEvent.setup();
    const editor = makeEditor();
    const thread: ComposeCommentThreadModel = {
      id: 't1',
      author: 'Alex Author',
      timestamp: '2026-01-01T12:00:00.000Z',
      text: 'Root comment',
      anchorText: 'Hello',
      resolved: false,
      replies: [{ id: 'r1', author: 'Sam Reviewer', timestamp: '2026-01-01T12:05:00.000Z', text: 'First reply' }],
    };
    renderPanel(editor, { initialThreads: [thread] });

    await user.type(screen.getByTestId('compose-comment-reply-input-t1'), 'Second reply');
    await user.click(screen.getByTestId('compose-comment-reply-submit-t1'));

    const repliesContainer = screen.getByTestId('compose-comment-replies-t1');
    const replyTexts = Array.from(repliesContainer.children).map(el => el.textContent ?? '');
    expect(replyTexts[0]).toContain('First reply');
    expect(replyTexts[1]).toContain('Second reply');
    editor.destroy();
  });

  it('resolving a thread sets its resolved state and hides the reply composer', async () => {
    const user = userEvent.setup();
    const editor = makeEditor();
    const thread: ComposeCommentThreadModel = {
      id: 't1',
      author: 'Alex Author',
      timestamp: '2026-01-01T12:00:00.000Z',
      text: 'Root comment',
      anchorText: 'Hello',
      resolved: false,
      replies: [],
    };
    renderPanel(editor, { initialThreads: [thread] });

    expect(screen.queryByTestId('compose-comment-resolved-t1')).not.toBeInTheDocument();
    await user.click(screen.getByTestId('compose-comment-resolve-t1'));

    expect(screen.getByTestId('compose-comment-thread-t1')).toHaveAttribute('data-resolved', 'true');
    expect(screen.getByTestId('compose-comment-resolved-t1')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-comment-reply-input-t1')).not.toBeInTheDocument();
    editor.destroy();
  });

  it('SCOPE GUARD: a reply chain carrying parentReplyId provenance still renders as a FLAT list (no nested tree)', () => {
    const editor = makeEditor();
    // Simulates an imported thread whose source lacks the modern-comments 4-part structure: `r3`
    // carries a `parentReplyId` pointing at `r2` (a would-be depth-2 reply), but the UI must NEVER
    // build a tree from it.
    const thread: ComposeCommentThreadModel = {
      id: 't1',
      author: 'Word User',
      timestamp: '2026-01-01T12:00:00.000Z',
      text: 'Root comment',
      anchorText: 'Hello',
      resolved: false,
      replies: [
        { id: 'r1', author: 'A', timestamp: '2026-01-01T12:01:00.000Z', text: 'Reply 1' },
        { id: 'r2', author: 'B', timestamp: '2026-01-01T12:02:00.000Z', text: 'Reply 2' },
        { id: 'r3', author: 'C', timestamp: '2026-01-01T12:03:00.000Z', text: 'Reply 3 (deep)', parentReplyId: 'r2' },
      ],
    };
    renderPanel(editor, { initialThreads: [thread] });

    const repliesContainer = screen.getByTestId('compose-comment-replies-t1');
    // All three replies are DIRECT children of ONE flat container — none nested inside another reply.
    expect(repliesContainer.children).toHaveLength(3);
    for (const child of Array.from(repliesContainer.children)) {
      expect(child.querySelector('[data-testid^="compose-comment-reply-"]')).toBeNull();
    }
    expect(screen.getByTestId('compose-comment-reply-r3')).toBeInTheDocument();
    editor.destroy();
  });

  it('ADR-021: renders under a dark theme with no hardcoded hex color', () => {
    const editor = makeEditor();
    const thread: ComposeCommentThreadModel = {
      id: 't1',
      author: 'Alex Author',
      timestamp: '2026-01-01T12:00:00.000Z',
      text: 'Root comment',
      anchorText: 'Hello',
      resolved: true,
      replies: [{ id: 'r1', author: 'Sam Reviewer', timestamp: '2026-01-01T12:05:00.000Z', text: 'Reply' }],
    };
    const { container } = renderPanel(editor, { theme: webDarkTheme, initialThreads: [thread] });

    expect(screen.getByTestId('compose-comment-thread-panel')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    editor.destroy();
  });
});
