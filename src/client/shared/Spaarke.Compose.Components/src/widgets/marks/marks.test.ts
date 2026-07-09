/**
 * marks.test.ts — coverage for the three R2 custom marks (task 031, FR-15):
 * InsertionMark / DeletionMark / CommentAnchorMark.
 *
 * Exercised through a headless TipTap `Editor` (StarterKit provides doc/paragraph/text; the
 * three marks are registered additively) — the same schema-registration path ComposeEditor uses.
 * Protects the FR-15 invariants: the marks are applied/removed via commands, render as their
 * redline/anchor DOM (`ins` / `del` / highlighted `span`), carry provenance attributes
 * (`data-binding` / `data-ledger-ref` / `data-comment-id`) so FR-16/17 can address them, delegate
 * ALL color to the token-based `compose-mark-*` classes (ADR-021 — no inline hex), and survive a
 * parse round-trip.
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { InsertionMark } from './InsertionMark';
import { DeletionMark } from './DeletionMark';
import { CommentAnchorMark } from './CommentAnchorMark';

function makeEditor(content = '<p>hello world</p>'): Editor {
  return new Editor({
    extensions: [StarterKit, InsertionMark, DeletionMark, CommentAnchorMark],
    content,
  });
}

describe('Compose R2 custom marks (task 031, FR-15)', () => {
  it('registers insertion, deletion, and commentAnchor in the editor schema', () => {
    const editor = makeEditor();
    const markNames = Object.keys(editor.schema.marks);
    expect(markNames).toEqual(expect.arrayContaining(['insertion', 'deletion', 'commentAnchor']));
    editor.destroy();
  });

  it('setInsertion renders an added-style span with provenance attributes', () => {
    const editor = makeEditor();
    editor.chain().selectAll().setInsertion({ binding: 'b1', ledgerRef: 'b1@t3' }).run();

    const html = editor.getHTML();
    expect(html).toContain('data-compose-mark="insertion"');
    expect(html).toContain('class="compose-mark-insertion"');
    expect(html).toContain('data-binding="b1"');
    expect(html).toContain('data-ledger-ref="b1@t3"');
    editor.destroy();
  });

  it('setDeletion renders a removed-style span with provenance attributes', () => {
    const editor = makeEditor();
    editor.chain().selectAll().setDeletion({ binding: 'b2', ledgerRef: 'b2@t7' }).run();

    const html = editor.getHTML();
    expect(html).toContain('data-compose-mark="deletion"');
    expect(html).toContain('class="compose-mark-deletion"');
    expect(html).toContain('data-binding="b2"');
    expect(html).toContain('data-ledger-ref="b2@t7"');
    editor.destroy();
  });

  it('setCommentAnchor renders a highlighted <span> carrying its data-comment-id', () => {
    const editor = makeEditor();
    editor.chain().selectAll().setCommentAnchor({ commentId: 'c1', binding: 'b3' }).run();

    const html = editor.getHTML();
    expect(html).toContain('class="compose-mark-comment-anchor"');
    expect(html).toContain('data-comment-id="c1"');
    expect(html).toContain('data-binding="b3"');
    editor.destroy();
  });

  it('delegates all styling to token classes — no inline hex/style on rendered marks (ADR-021)', () => {
    const editor = makeEditor();
    editor.chain().selectAll().setInsertion({ binding: 'b1' }).run();

    const html = editor.getHTML();
    // Color comes from the compose-mark-* class (token-based makeStyles), never inline.
    expect(html).not.toContain('style=');
    expect(html).not.toMatch(/#[0-9a-fA-F]{3,6}/);
    editor.destroy();
  });

  it('unsetInsertion removes the mark', () => {
    const editor = makeEditor();
    editor.chain().selectAll().setInsertion({ binding: 'b1' }).run();
    expect(editor.getHTML()).toContain('compose-mark-insertion');

    editor.chain().selectAll().unsetInsertion().run();
    expect(editor.getHTML()).not.toContain('compose-mark-insertion');
    editor.destroy();
  });

  it('does NOT claim bare <s>/<del> — those stay StarterKit Strike, not a pending deletion', () => {
    const editor = makeEditor('<p><s>plain strikethrough</s></p>');
    // A plain strikethrough imported from a .docx must not render as a track-change deletion.
    expect(editor.getHTML()).not.toContain('compose-mark-deletion');
    editor.destroy();
  });

  it('round-trips a data-marked deletion (its own serialized form)', () => {
    const editor = makeEditor(
      '<p><span data-compose-mark="deletion" data-binding="b2" data-ledger-ref="b2@t7">gone</span></p>'
    );
    const html = editor.getHTML();
    expect(html).toContain('class="compose-mark-deletion"');
    expect(html).toContain('data-binding="b2"');
    editor.destroy();
  });

  it('parses previously-serialized redline HTML back into marks (round-trip)', () => {
    const editor = makeEditor(
      '<p><span data-compose-mark="insertion" data-binding="b1" data-ledger-ref="b1@t3">added</span></p>'
    );
    // The schema recognizes the mark on load and re-emits it (not stripped to plain text).
    const html = editor.getHTML();
    expect(html).toContain('class="compose-mark-insertion"');
    expect(html).toContain('data-binding="b1"');
    expect(html).toContain('data-ledger-ref="b1@t3"');
    editor.destroy();
  });
});
