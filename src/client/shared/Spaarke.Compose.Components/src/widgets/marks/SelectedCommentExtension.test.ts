/**
 * SelectedCommentExtension.test.ts — UAT round-4 #8 selected-advisory-comment highlight decoration.
 *
 * Drives the plugin against a REAL headless TipTap editor (same harness as TrackChangesExtension.test.ts)
 * carrying a `commentAnchor` mark: selecting a thread id paints the SELECTED decoration over that
 * thread's CURRENT anchor span (live position); selecting an absent id or clearing paints nothing. The
 * decoration is a VIEW layer — it never mutates the document (the property that keeps it out of the
 * saved DOCX), so these tests assert on the plugin state + rendered decoration class, never on doc text.
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { CommentAnchorMark } from './CommentAnchorMark';
import {
  SelectedCommentExtension,
  selectedCommentPluginKey,
  SELECTED_COMMENT_ANCHOR_CLASS,
} from './SelectedCommentExtension';

function makeEditor(content = '<p>hello world advisory clause</p>'): Editor {
  return new Editor({ extensions: [StarterKit, CommentAnchorMark, SelectedCommentExtension], content });
}

/** Apply a commentAnchor mark over [from, to) (mirrors createThread's own tr.addMark). */
function applyCommentAnchor(editor: Editor, commentId: string, from: number, to: number): void {
  const markType = editor.state.schema.marks.commentAnchor;
  editor.view.dispatch(editor.state.tr.addMark(from, to, markType.create({ commentId })));
}

function selectedClassCount(editor: Editor): number {
  return editor.view.dom.querySelectorAll(`.${SELECTED_COMMENT_ANCHOR_CLASS}`).length;
}

describe('SelectedCommentExtension', () => {
  it('starts with no selection and no decoration', () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'c1', 1, 6);
    expect(selectedCommentPluginKey.getState(editor.state)).toBeNull();
    expect(selectedClassCount(editor)).toBe(0);
    editor.destroy();
  });

  it('paints the selected class over the selected thread’s anchor span', () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'c1', 1, 6); // "hello"
    editor.view.dispatch(editor.state.tr.setMeta(selectedCommentPluginKey, { type: 'select', commentId: 'c1' }));
    expect(selectedCommentPluginKey.getState(editor.state)).toBe('c1');
    expect(selectedClassCount(editor)).toBeGreaterThanOrEqual(1);
    editor.destroy();
  });

  it('paints nothing when the selected id has no anchor in the document (never guesses)', () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'c1', 1, 6);
    editor.view.dispatch(editor.state.tr.setMeta(selectedCommentPluginKey, { type: 'select', commentId: 'ghost' }));
    expect(selectedCommentPluginKey.getState(editor.state)).toBe('ghost');
    expect(selectedClassCount(editor)).toBe(0);
    editor.destroy();
  });

  it('clears the decoration on a clear meta', () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'c1', 1, 6);
    editor.view.dispatch(editor.state.tr.setMeta(selectedCommentPluginKey, { type: 'select', commentId: 'c1' }));
    expect(selectedClassCount(editor)).toBeGreaterThanOrEqual(1);
    editor.view.dispatch(editor.state.tr.setMeta(selectedCommentPluginKey, { type: 'clear' }));
    expect(selectedCommentPluginKey.getState(editor.state)).toBeNull();
    expect(selectedClassCount(editor)).toBe(0);
    editor.destroy();
  });

  it('moves the selection to a different thread when re-dispatched', () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'c1', 1, 6); // "hello"
    applyCommentAnchor(editor, 'c2', 7, 12); // "world"
    editor.view.dispatch(editor.state.tr.setMeta(selectedCommentPluginKey, { type: 'select', commentId: 'c1' }));
    expect(selectedClassCount(editor)).toBeGreaterThanOrEqual(1);
    editor.view.dispatch(editor.state.tr.setMeta(selectedCommentPluginKey, { type: 'select', commentId: 'c2' }));
    // Still exactly one selected region (the OTHER thread), not two.
    expect(selectedCommentPluginKey.getState(editor.state)).toBe('c2');
    expect(selectedClassCount(editor)).toBeGreaterThanOrEqual(1);
    editor.destroy();
  });

  it('does NOT serialize into the document HTML (view decoration, DOCX-safe)', () => {
    const editor = makeEditor();
    applyCommentAnchor(editor, 'c1', 1, 6);
    editor.view.dispatch(editor.state.tr.setMeta(selectedCommentPluginKey, { type: 'select', commentId: 'c1' }));
    // The transient selected class is a decoration — it must never appear in getHTML() output.
    expect(editor.getHTML()).not.toContain(SELECTED_COMMENT_ANCHOR_CLASS);
    editor.destroy();
  });
});
