/**
 * docxBridge.importedModel.test.ts — R6 (spaarkeai-compose-r6, task 012 — render-on-save cutover).
 *
 * Exercises the pure IMPORTED-document model mapper (`buildImportedContentModel`) through a HEADLESS
 * TipTap `Editor` (same pattern as docxBridge.contentModel.test.ts): the paraId-anchored merge of
 * editor state with the RETAINED server `ComposeContentModel` — verbatim pass-through of untouched
 * blocks (object identity; every server-set fact preserved), diff-driven redlining of user edits,
 * mark→revision-fact translation, session/advisory comment folding, and the aggregated fidelity
 * warnings. Also covers the BORN-IN-EDITOR comment-folding sibling (`buildContentModelWithComments`,
 * task 012 scope amendment — the server removed the engine-based comment bake for ALL ContentModel
 * saves).
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Underline from '@tiptap/extension-underline';
import Link from '@tiptap/extension-link';
import Table from '@tiptap/extension-table';
import TableRow from '@tiptap/extension-table-row';
import TableHeader from '@tiptap/extension-table-header';
import TableCell from '@tiptap/extension-table-cell';
import { COMPOSE_R3_PARAID } from '../widgets/paraIdExtension';
import { InsertionMark } from '../widgets/marks/InsertionMark';
import { DeletionMark } from '../widgets/marks/DeletionMark';
import { CommentAnchorMark } from '../widgets/marks/CommentAnchorMark';
import {
  stampParaIds,
  captureParaIdSnapshot,
  buildContentModel,
  buildImportedContentModel,
  buildContentModelWithComments,
  type ImportedModelThreadInput,
} from './docxBridge';
import type { ComposeContentModel, ParaIdMapEntry } from '../types/compose-contracts';

function makeEditor(content = '<p></p>'): Editor {
  return new Editor({
    extensions: [
      StarterKit,
      Underline,
      Link.configure({ openOnClick: false, autolink: false }),
      Table,
      TableRow,
      TableHeader,
      TableCell,
      InsertionMark,
      DeletionMark,
      CommentAnchorMark,
      ...COMPOSE_R3_PARAID,
    ],
    content,
  });
}

function stamp(editor: Editor, ids: string[]): void {
  const map: ParaIdMapEntry[] = ids.map((paraId, index) => ({ index, paraId, isMinted: false }));
  stampParaIds(editor, map);
}

const NO_THREADS: ImportedModelThreadInput[] = [];

describe('buildImportedContentModel — verbatim pass-through', () => {
  it('passes untouched blocks through by OBJECT IDENTITY, preserving every server-set field', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Alpha clause.</p><p>Beta clause.</p>');
    stamp(editor, ['AAAA0001', 'BBBB0002']);
    // Mount-realism (F1): the loaded block's Inserted-revision run ("clause.") renders as an
    // IMPORTED insertion mark in the editor (applyImportedRevisions) — apply it BEFORE the snapshot,
    // exactly like the mount does, so the formatting signature aligns (both sides exclude the span).
    editor.commands.setTextSelection({ from: 7, to: 14 }); // "clause."
    editor.commands.setMark('insertion', {
      ledgerRef: 'imported:0',
      author: 'Jane Doe',
      date: '2026-01-01T00:00:00Z',
    });
    const snapshot = captureParaIdSnapshot(editor);
    expect(snapshot.get('AAAA0001')).toBe('Alpha '); // reject-state excludes the imported insertion

    const loaded: ComposeContentModel = {
      blocks: [
        {
          kind: 'Paragraph',
          paraId: 'AAAA0001',
          runs: [
            { text: 'Alpha ' },
            { text: '', isPageBreak: true },
            {
              text: 'clause.',
              bold: true,
              revision: { kind: 'Inserted', author: 'Jane Doe', date: '2026-01-01T00:00:00Z' },
              formatChange: { author: 'Jane Doe', previousPropertiesXml: '<w:rPr/>' },
            },
            { text: '', commentAnchor: { kind: 'Start', id: 3 } },
            { text: '', commentAnchor: { kind: 'End', id: 3 } },
          ],
          pageBreakBefore: true,
          markRevision: { kind: 'Inserted', author: 'Jane Doe' },
          propertiesChange: { author: 'Jane Doe', previousPropertiesXml: '<w:pPr/>' },
        },
        { kind: 'ListItem', paraId: 'BBBB0002', level: 0, ordered: true, numId: 7, runs: [{ text: 'Beta clause.' }] },
      ],
      comments: [{ id: 3, author: 'Bob', text: 'existing comment' }],
    };
    // NOTE: block 2 is a loaded ListItem while the editor renders it as a plain paragraph — that IS a
    // props mismatch, so restrict this identity assertion to block 1 and give block 2 matching props.
    loaded.blocks[1] = { kind: 'Paragraph', paraId: 'BBBB0002', numId: 7, runs: [{ text: 'Beta clause.' }] };

    const { model, warnings } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.blocks[0]).toBe(loaded.blocks[0]); // identity — isPageBreak/commentAnchor/revision/formatChange/markRevision/propertiesChange/pageBreakBefore all exact
    expect(model.blocks[1]).toBe(loaded.blocks[1]); // numId exact
    expect(model.comments).toEqual([{ id: 3, author: 'Bob', text: 'existing comment' }]);
    expect(model.comments).not.toBe(loaded.comments); // NEW array
    expect(model.blocks).not.toBe(loaded.blocks); // NEW array
    expect(warnings).toEqual([]);
    editor.destroy();
  });

  it('passes an untouched table through by identity, preserving table/row/cell structural facts', () => {
    const editor = makeEditor();
    editor.commands.setContent(
      '<table><tbody><tr><td><p>Term</p></td><td><p>Meaning</p></td></tr></tbody></table>'
    );
    stamp(editor, ['CCCC0001', 'CCCC0002']);
    const snapshot = captureParaIdSnapshot(editor);

    const loaded: ComposeContentModel = {
      blocks: [
        {
          kind: 'Table',
          table: {
            rows: [
              {
                repeatAsHeaderRow: true,
                cells: [
                  {
                    blocks: [{ kind: 'Paragraph', paraId: 'CCCC0001', runs: [{ text: 'Term' }] }],
                    gridSpan: 2,
                    vMerge: 'Restart',
                    width: { type: 'dxa', value: '2000' },
                    verticalAlignment: 'center',
                  },
                  { blocks: [{ kind: 'Paragraph', paraId: 'CCCC0002', runs: [{ text: 'Meaning' }] }] },
                ],
              },
            ],
            styleId: 'TableGrid',
            width: { type: 'pct', value: '5000' },
            borders: { top: { val: 'single', size: 4 } },
            gridColumnWidthsTwips: ['2000', '3000'],
            lookHex: '04A0',
          },
        },
      ],
    };

    const { model, warnings } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.blocks[0]).toBe(loaded.blocks[0]);
    expect(warnings).toEqual([]);
    editor.destroy();
  });
});

describe('buildImportedContentModel — props-only change', () => {
  it('overrides editable props from the editor while keeping the loaded runs + facts untouched', () => {
    const editor = makeEditor();
    // Bold in the editor mirrors the loaded run's bold (F1: the formatting signature must align for
    // the props-only tier to apply — only the LEVEL differs here).
    editor.commands.setContent('<h2><strong>Title</strong></h2>');
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);

    const loaded: ComposeContentModel = {
      blocks: [
        {
          kind: 'Heading',
          level: 1,
          paraId: 'AAAA0001',
          runs: [{ text: 'Title', bold: true }],
          propertiesChange: { author: 'Z' },
        },
      ],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.blocks[0]).not.toBe(loaded.blocks[0]);
    expect(model.blocks[0].kind).toBe('Heading');
    expect(model.blocks[0].level).toBe(2); // the editor's level wins
    expect(model.blocks[0].runs).toBe(loaded.blocks[0].runs); // runs kept by reference
    expect(model.blocks[0].propertiesChange).toBe(loaded.blocks[0].propertiesChange);
    editor.destroy();
  });
});

describe('buildImportedContentModel — diff-driven user edits (trackChanges)', () => {
  it('emits Deleted + Inserted runs at the correct positions for a word replace', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>The quick brown fox</p>');
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);

    editor.commands.insertContentAt({ from: 5, to: 10 }, 'swift'); // "quick" → "swift"

    const loaded: ComposeContentModel = {
      blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'The quick brown fox' }] }],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.blocks[0].runs).toEqual([
      { text: 'The ' },
      { text: 'quick', revision: { kind: 'Deleted' } }, // author-less — the server attributes the saving user
      { text: 'swift', revision: { kind: 'Inserted' } },
      { text: ' brown fox' },
    ]);
    editor.destroy();
  });

  it('trackChanges=false yields plain runs with no diff-derived revisions and omits deleted paragraphs', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>The quick brown fox</p><p>Beta</p>');
    stamp(editor, ['AAAA0001', 'BBBB0002']);
    const snapshot = captureParaIdSnapshot(editor);

    editor.commands.insertContentAt({ from: 5, to: 10 }, 'swift');
    // Delete the second paragraph entirely (node spans [21, 27) after the same-length replace).
    const firstParaSize = editor.state.doc.content.firstChild!.nodeSize;
    editor.commands.deleteRange({ from: firstParaSize, to: firstParaSize + editor.state.doc.content.child(1).nodeSize });

    const loaded: ComposeContentModel = {
      blocks: [
        { kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'The quick brown fox' }] },
        { kind: 'Paragraph', paraId: 'BBBB0002', runs: [{ text: 'Beta' }] },
      ],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: false,
      sessionThreads: NO_THREADS,
    });

    expect(model.blocks).toHaveLength(1); // deleted paragraph OMITTED on the clean path
    expect(model.blocks[0].runs).toEqual([{ text: 'The swift brown fox' }]);
    expect(model.blocks[0].runs!.every(r => r.revision === undefined)).toBe(true);
    editor.destroy();
  });
});

describe('buildImportedContentModel — insertion/deletion marks → revision facts', () => {
  it('preserves imported author/date and attributes AI binding marks to Spaarke Assistant', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Base text here.</p>');
    stamp(editor, ['AAAA0001']);

    // Imported Word revision (applyImportedRevisions shape): ledgerRef 'imported:*', author+date set.
    editor.commands.setTextSelection({ from: 1, to: 5 }); // "Base"
    editor.commands.setMark('insertion', {
      ledgerRef: 'imported:0',
      author: 'Jane Doe',
      date: '2026-01-02T03:04:05Z',
    });
    // AI redline: binding+ledgerRef set, author/date null.
    editor.commands.setTextSelection({ from: 6, to: 10 }); // "text"
    editor.commands.setMark('deletion', { ledgerRef: 'b1@t1', binding: 'b1' });

    // Mount-order parity: the snapshot is captured AFTER imported marks are applied.
    const snapshot = captureParaIdSnapshot(editor);
    expect(snapshot.get('AAAA0001')).toBe(' text here.'); // insertion-marked text excluded

    const loaded: ComposeContentModel = {
      blocks: [
        {
          kind: 'Paragraph',
          paraId: 'AAAA0001',
          runs: [
            { text: 'Base', revision: { kind: 'Inserted', author: 'Jane Doe', date: '2026-01-02T03:04:05Z' } },
            { text: ' text here.' },
          ],
        },
      ],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.blocks[0].runs).toEqual([
      { text: 'Base', revision: { kind: 'Inserted', author: 'Jane Doe', date: '2026-01-02T03:04:05Z' } },
      { text: ' ' },
      { text: 'text', revision: { kind: 'Deleted', author: 'Spaarke Assistant' } },
      { text: ' here.' },
    ]);
    editor.destroy();
  });
});

describe('buildImportedContentModel — user-deleted loaded blocks', () => {
  it('redlines a deleted paragraph as all-Deleted runs + markRevision Deleted, keeping existing Deleted runs untouched', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Alpha</p><p>Beta</p>');
    stamp(editor, ['AAAA0001', 'BBBB0002']);
    const snapshot = captureParaIdSnapshot(editor);

    editor.commands.deleteRange({ from: 7, to: 13 }); // remove the "Beta" paragraph node

    const alreadyDeletedRun = { text: '!', revision: { kind: 'Deleted' as const, author: 'K' } };
    const loaded: ComposeContentModel = {
      blocks: [
        { kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Alpha' }] },
        {
          kind: 'Paragraph',
          paraId: 'BBBB0002',
          numId: 3,
          runs: [{ text: 'Be' }, { text: 'ta', revision: { kind: 'Inserted', author: 'Jane Doe' } }, alreadyDeletedRun],
        },
      ],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.blocks).toHaveLength(2);
    expect(model.blocks[0]).toBe(loaded.blocks[0]);
    const deleted = model.blocks[1];
    expect(deleted.markRevision).toEqual({ kind: 'Deleted' });
    expect(deleted.numId).toBe(3); // every other fact preserved
    expect(deleted.runs).toEqual([
      { text: 'Be', revision: { kind: 'Deleted' } },
      { text: 'ta', revision: { kind: 'Deleted' } }, // Inserted overwritten (innermost-wins baseline)
      { text: '!', revision: { kind: 'Deleted', author: 'K' } },
    ]);
    expect(deleted.runs![2]).toBe(alreadyDeletedRun); // existing Deleted run untouched (identity)
    expect(loaded.blocks[1].markRevision).toBeUndefined(); // input never mutated
    editor.destroy();
  });
});

describe('buildImportedContentModel — new paragraphs', () => {
  it('builds a new block with Inserted runs + markRevision Inserted, with a client-minted paraId', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Alpha</p>');
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);

    editor.commands.insertContentAt(editor.state.doc.content.size, '<p>Brand new</p>');

    const loaded: ComposeContentModel = {
      blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Alpha' }] }],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.blocks).toHaveLength(2);
    expect(model.blocks[0]).toBe(loaded.blocks[0]);
    expect(model.blocks[1]).toMatchObject({
      kind: 'Paragraph',
      markRevision: { kind: 'Inserted' },
      runs: [{ text: 'Brand new', revision: { kind: 'Inserted' } }],
    });
    // The paraId minting extension (COMPOSE_R3_PARAID / @tiptap/extension-unique-id) mints an
    // OOXML-shaped id for the new paragraph — model, rendered docx, and editor agree on ids.
    expect(model.blocks[1].paraId).toMatch(/^[0-9A-F]{8}$/);
    editor.destroy();
  });
});

describe('buildImportedContentModel — comment folding', () => {
  const thread: ImportedModelThreadInput = {
    id: 'thread-1',
    author: 'Ann',
    timestamp: '2026-08-01T00:00:00Z',
    text: 'Root note',
    replies: [{ text: 'Reply one', author: 'Cee', timestamp: '2026-08-02T00:00:00Z' }],
  };

  it('allocates non-colliding ids for a session thread, appends comments, and emits Start/End pairs (root + each reply)', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Flagged clause text.</p>');
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);

    editor.commands.setTextSelection({ from: 9, to: 15 }); // "clause"
    editor.commands.setMark('commentAnchor', { commentId: 'thread-1' });

    const loaded: ComposeContentModel = {
      blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Flagged clause text.' }] }],
      comments: [{ id: 3, author: 'Bob', text: 'existing comment' }],
    };

    const { model, warnings } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: [thread],
    });

    expect(model.comments).toEqual([
      { id: 3, author: 'Bob', text: 'existing comment' }, // loaded entry verbatim
      { id: 4, author: 'Ann', date: '2026-08-01T00:00:00Z', text: 'Root note' }, // > max loaded id
      { id: 5, author: 'Cee', date: '2026-08-02T00:00:00Z', text: 'Reply one' }, // reply gets its OWN id
    ]);
    expect(model.blocks[0].runs).toEqual([
      { text: 'Flagged ' },
      { text: '', commentAnchor: { kind: 'Start', id: 4 } },
      { text: '', commentAnchor: { kind: 'Start', id: 5 } }, // reply anchored at the SAME span
      { text: 'clause' },
      { text: '', commentAnchor: { kind: 'End', id: 4 } },
      { text: '', commentAnchor: { kind: 'End', id: 5 } },
      { text: ' text.' },
    ]);
    expect(warnings).toEqual([]);
    editor.destroy();
  });

  it("keeps an IMPORTED comment anchor id (runtime 'imported-thread:<n>' mark shape) and never re-appends its comment", () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Keep imported anchor.</p>');
    stamp(editor, ['AAAA0001']);

    editor.commands.setTextSelection({ from: 6, to: 14 }); // "imported"
    // F2 (step-9.5 review): the RUNTIME mark id shape is `imported-thread:<n>`
    // (applyImportedCommentAnchors / IMPORTED_COMMENT_THREAD_PREFIX), NOT the bare numeric id.
    editor.commands.setMark('commentAnchor', { commentId: 'imported-thread:3' });

    const snapshot = captureParaIdSnapshot(editor);
    editor.commands.insertContentAt(1, 'Now '); // user edit forces the rebuild path

    const loaded: ComposeContentModel = {
      blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Keep imported anchor.' }] }],
      comments: [{ id: 3, author: 'Bob', text: 'imported comment' }],
    };

    const { model, warnings } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.comments).toEqual([{ id: 3, author: 'Bob', text: 'imported comment' }]);
    const runs = model.blocks[0].runs!;
    expect(runs).toContainEqual({ text: '', commentAnchor: { kind: 'Start', id: 3 } });
    expect(runs).toContainEqual({ text: '', commentAnchor: { kind: 'End', id: 3 } });
    expect(runs).toContainEqual({ text: 'Now ', revision: { kind: 'Inserted' } });
    expect(warnings).toEqual([]);
    editor.destroy();
  });

  it('also accepts a bare-numeric imported anchor id', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Bare numeric id.</p>');
    stamp(editor, ['AAAA0001']);

    editor.commands.setTextSelection({ from: 1, to: 5 }); // "Bare"
    editor.commands.setMark('commentAnchor', { commentId: '3' });

    const snapshot = captureParaIdSnapshot(editor);
    editor.commands.insertContentAt(editor.state.doc.content.size - 1, ' X'); // force rebuild

    const loaded: ComposeContentModel = {
      blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Bare numeric id.' }] }],
      comments: [{ id: 3, author: 'Bob', text: 'imported comment' }],
    };

    const { model, warnings } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.comments).toEqual([{ id: 3, author: 'Bob', text: 'imported comment' }]);
    expect(model.blocks[0].runs).toContainEqual({ text: '', commentAnchor: { kind: 'Start', id: 3 } });
    expect(warnings).toEqual([]);
    editor.destroy();
  });

  it('F2 regression: an UNTOUCHED paragraph whose only anchor is an imported-thread mark stays VERBATIM', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Keep imported anchor.</p>');
    stamp(editor, ['AAAA0001']);
    editor.commands.setTextSelection({ from: 6, to: 14 }); // "imported"
    editor.commands.setMark('commentAnchor', { commentId: 'imported-thread:3' });
    const snapshot = captureParaIdSnapshot(editor);

    const loaded: ComposeContentModel = {
      blocks: [
        {
          kind: 'Paragraph',
          paraId: 'AAAA0001',
          runs: [
            { text: '', commentAnchor: { kind: 'Start', id: 3 } },
            { text: 'Keep imported anchor.' },
            { text: '', commentAnchor: { kind: 'End', id: 3 } },
            { text: '', isPageBreak: true }, // fact that a wrong force-rebuild would have dropped
          ],
        },
      ],
      comments: [{ id: 3, author: 'Bob', text: 'imported comment' }],
    };

    const { model, warnings } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.blocks[0]).toBe(loaded.blocks[0]); // NOT force-rebuilt (the pre-fix behavior)
    expect(warnings).toEqual([]);
    editor.destroy();
  });

  it('reports an unresolvable anchor (not imported, not a session thread) and skips it', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Ghost anchor span.</p>');
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);

    editor.commands.setTextSelection({ from: 1, to: 6 }); // "Ghost"
    editor.commands.setMark('commentAnchor', { commentId: 'ghost-thread' });

    const loaded: ComposeContentModel = {
      blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Ghost anchor span.' }] }],
    };

    const { model, warnings } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.blocks[0].runs!.some(r => r.commentAnchor !== undefined)).toBe(false);
    expect(warnings).toContainEqual({ code: 'comment-anchor-unresolved', count: 1 });
    editor.destroy();
  });
});

describe('buildImportedContentModel — hyperlinks', () => {
  it('maps the TipTap link mark to run.href on a rebuilt block', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Visit example</p>');
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);

    editor.commands.setTextSelection({ from: 7, to: 14 }); // "example"
    editor.commands.setMark('link', { href: 'https://example.com' });
    editor.commands.insertContentAt(1, 'Now '); // user edit forces the rebuild path

    const loaded: ComposeContentModel = {
      blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Visit example' }] }],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.blocks[0].runs).toContainEqual({ text: 'example', href: 'https://example.com' });
    editor.destroy();
  });
});

describe('buildImportedContentModel — fidelity warnings', () => {
  it('counts a dropped page-break marker run when its paragraph is edited', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>AlphaBeta</p>');
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);

    editor.commands.insertContentAt(editor.state.doc.content.size - 1, ' X');

    const loaded: ComposeContentModel = {
      blocks: [
        {
          kind: 'Paragraph',
          paraId: 'AAAA0001',
          runs: [{ text: 'Alpha' }, { text: '', isPageBreak: true }, { text: 'Beta' }],
        },
      ],
    };

    const { model, warnings } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.blocks[0].runs!.some(r => r.isPageBreak)).toBe(false);
    expect(warnings).toContainEqual({ code: 'edited-paragraph-page-break-dropped', count: 1 });
    editor.destroy();
  });

  it('counts a dropped hardBreak on an edited paragraph (server BuildRun renders one w:t — no newline split)', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>One<br>Two</p>');
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);
    expect(snapshot.get('AAAA0001')).toBe('One\nTwo');

    editor.commands.insertContentAt(editor.state.doc.content.size - 1, 'X');

    const loaded: ComposeContentModel = {
      blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'One' }, { text: 'Two' }] }],
    };

    const { model, warnings } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(model.blocks[0].runs!.every(r => !r.text.includes('\n'))).toBe(true);
    expect(warnings).toContainEqual({ code: 'edited-paragraph-line-break-dropped', count: 1 });
    editor.destroy();
  });

  it('rebuilds a shape-changed table once, preserving table-level + positional cell facts', () => {
    const editor = makeEditor();
    editor.commands.setContent(
      '<table><tbody><tr><td><p>A</p></td></tr><tr><td><p>B</p></td></tr></tbody></table>'
    );
    stamp(editor, ['AAAA0001', 'BBBB0002']);
    const snapshot = captureParaIdSnapshot(editor);

    const loaded: ComposeContentModel = {
      blocks: [
        {
          kind: 'Table',
          table: {
            rows: [
              {
                cells: [
                  { blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'A' }] }], gridSpan: 2 },
                ],
              },
            ],
            styleId: 'TableGrid',
            borders: { top: { val: 'single', size: 4 } },
            gridColumnWidthsTwips: ['2000'],
            lookHex: '04A0',
          },
        },
      ],
    };

    const { model, warnings } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    const table = model.blocks[0].table!;
    expect(table.rows).toHaveLength(2); // editor shape wins
    expect(table.styleId).toBe('TableGrid');
    expect(table.borders).toEqual({ top: { val: 'single', size: 4 } });
    expect(table.gridColumnWidthsTwips).toEqual(['2000']);
    expect(table.lookHex).toBe('04A0');
    expect(table.rows[0].cells[0].gridSpan).toBe(2); // positionally-paired cell fact preserved
    expect(warnings).toContainEqual({ code: 'edited-table-structure-rebuilt', count: 1 });
    editor.destroy();
  });
});

describe('buildImportedContentModel — formatting-only edits (F1, step-9.5 review)', () => {
  it('rebuilds a block whose ONLY change is formatting, carrying the new formatting without diff revisions', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Make this bold</p>');
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);

    editor.commands.setTextSelection({ from: 11, to: 15 }); // "bold"
    editor.commands.setMark('bold');

    const loaded: ComposeContentModel = {
      blocks: [
        { kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Make this bold' }], pageBreakBefore: true },
      ],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    // Pre-fix behavior: verbatim identity — the bold silently lost. Now: rebuilt with the bold.
    expect(model.blocks[0]).not.toBe(loaded.blocks[0]);
    expect(model.blocks[0].runs).toEqual([{ text: 'Make this ' }, { text: 'bold', bold: true }]);
    // Formatting changes are NOT redlined (text unchanged → no diff-derived revisions) — matches the
    // retired op-log path's untracked SetMark behavior.
    expect(model.blocks[0].runs!.every(r => r.revision === undefined)).toBe(true);
    expect(model.blocks[0].pageBreakBefore).toBe(true); // server-set facts survive the rebuild
    editor.destroy();
  });
});

describe('buildImportedContentModel — build-time snapshot (F4, step-9.5 review)', () => {
  it('returns the build-time reject-state snapshot, unaffected by edits made after building', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Original text</p>');
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);
    editor.commands.insertContentAt(editor.state.doc.content.size - 1, ' edited');

    const loaded: ComposeContentModel = {
      blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Original text' }] }],
    };
    const result = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    expect(result.snapshot.get('AAAA0001')).toBe('Original text edited'); // the build-time state

    // A mid-flight edit AFTER build must not leak into the returned snapshot (adopting it on 200
    // keeps that edit different from the baseline, so it still saves next time).
    editor.commands.insertContentAt(editor.state.doc.content.size - 1, ' MORE');
    expect(result.snapshot.get('AAAA0001')).toBe('Original text edited');
    expect(captureParaIdSnapshot(editor).get('AAAA0001')).toBe('Original text edited MORE'); // live doc moved on
    editor.destroy();
  });
});

describe('buildImportedContentModel — input immutability', () => {
  it('never mutates the loaded model', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Alpha</p><p>Beta</p>');
    stamp(editor, ['AAAA0001', 'BBBB0002']);
    const snapshot = captureParaIdSnapshot(editor);

    editor.commands.setTextSelection({ from: 2, to: 4 });
    editor.commands.setMark('commentAnchor', { commentId: 'thread-1' });
    editor.commands.deleteRange({ from: 7, to: 13 }); // delete "Beta"

    const loaded: ComposeContentModel = {
      blocks: [
        { kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Alpha' }] },
        { kind: 'Paragraph', paraId: 'BBBB0002', runs: [{ text: 'Beta', revision: { kind: 'Inserted' } }] },
      ],
      comments: [{ id: 1, author: 'Bob', text: 'existing' }],
    };
    const before = JSON.stringify(loaded);

    buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: [
        { id: 'thread-1', author: 'Ann', timestamp: '2026-08-01T00:00:00Z', text: 'Note', replies: [] },
      ],
    });

    expect(JSON.stringify(loaded)).toBe(before);
    editor.destroy();
  });
});

describe('buildContentModelWithComments — born-in-editor comment folding (task 012 scope amendment)', () => {
  it('delegates to buildContentModel when there are no session threads (exact legacy output)', () => {
    const editor = makeEditor();
    editor.commands.setContent('<h1>Definitions</h1><p>Plain <strong>bold</strong>.</p>');

    const { model, warnings } = buildContentModelWithComments(editor, []);

    expect(model).toEqual(buildContentModel(editor));
    expect(warnings).toEqual([]);
    editor.destroy();
  });

  it('folds session threads as Start/End anchor runs + comments allocated from 1, keeping reject-state text parity', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Hello world</p>');

    editor.commands.setTextSelection({ from: 7, to: 12 }); // "world"
    editor.commands.setMark('commentAnchor', { commentId: 'thread-9' });
    // A pending AI insertion — reject-state parity: still EXCLUDED from the born-in-editor model.
    editor.commands.setTextSelection(editor.state.doc.content.size - 1);
    editor.commands.insertContent({
      type: 'text',
      text: ' PENDING',
      marks: [{ type: 'insertion', attrs: { ledgerRef: 'b1@t1', binding: 'b1' } }],
    });

    const { model, warnings } = buildContentModelWithComments(editor, [
      {
        id: 'thread-9',
        author: 'Ann',
        timestamp: '2026-08-01T00:00:00Z',
        text: 'Advisory note',
        replies: [{ text: 'Reply', author: 'Cee', timestamp: '2026-08-02T00:00:00Z' }],
      },
    ]);

    expect(model.comments).toEqual([
      { id: 1, author: 'Ann', date: '2026-08-01T00:00:00Z', text: 'Advisory note' },
      { id: 2, author: 'Cee', date: '2026-08-02T00:00:00Z', text: 'Reply' },
    ]);
    expect(model.blocks[0].runs).toEqual([
      { text: 'Hello ' },
      { text: '', commentAnchor: { kind: 'Start', id: 1 } },
      { text: '', commentAnchor: { kind: 'Start', id: 2 } },
      { text: 'world' },
      { text: '', commentAnchor: { kind: 'End', id: 1 } },
      { text: '', commentAnchor: { kind: 'End', id: 2 } },
      // ' PENDING' (insertion-marked) excluded — reject-state parity; no revision facts on this path
    ]);
    expect(model.blocks[0].runs!.every(r => r.revision === undefined)).toBe(true);
    expect(warnings).toEqual([]);
    editor.destroy();
  });
});
