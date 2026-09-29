/**
 * docxBridge.field.test.ts — task 057 (spaarkeai-compose-r8), the CLIENT half of the field carry.
 *
 * Task 049 made a Word field survive the SERVER path (projection → content model → renderer). It did not
 * make one survive a KEYSTROKE: an edited paragraph is rebuilt from the client's own nodes, and a `field`
 * atom contributed nothing to that rebuild, so the field never reached the posted model. A producer with no
 * consumer. This file measures the consumer.
 *
 * Three things are asserted here, and the second matters more than the first:
 *
 *   1. A field in an EDITED paragraph round-trips as a `field` marker run carrying its instruction
 *      verbatim, plus the complex/locked/dirty facts the document stated.
 *   2. The TEXT COORDINATE SPACE does not move. A field is the first segment that is present in the run
 *      stream and ABSENT from the coordinate space — it contributes ZERO characters, where task 048's tab
 *      and symbol each contributed exactly one. `collectSegments`' walk MUST stay byte-identical to
 *      `rejectStateText`'s (that function's own doc comment states the contract), because every offset
 *      after the field — and therefore the whole diff/redline path — is measured in it. Proven twice
 *      below, in both tiers that consume it, rather than asserted once.
 *   3. The server's carryability refusal is honoured client-side: an atom with NO `data-field-instr` (a
 *      nested or instruction-less field, which the server structurally cannot re-emit) produces NO field
 *      run. The client never invents a field.
 *
 * Neither `collectSegments` nor `rejectStateText` is exported, so (2) is measured through the two surfaces
 * that consume BOTH walks and compare them:
 *
 *   * the VERBATIM tier's gate (`rejectText === baseline` inside `mergeLeafBlock` — a `===` between the two
 *     walks' outputs, observable as object-identity pass-through), and
 *   * the REBUILD tier's redline diff (computed between the two walks, so any disagreement surfaces as
 *     phantom Inserted/Deleted runs).
 *
 * Both were verified to FAIL when the field segment was deliberately given its display text instead of the
 * empty string, so neither is a test that only ever passes.
 *
 * @see ../widgets/opaqueAtomNode.ts (the node and its field payload attributes)
 * @see ../../../../../src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs (FieldAtomDataAttributes / AppendAtom)
 * @see projects/spaarkeai-compose-r8/notes/049-field-carry-decisions.md §7 (this task's charter)
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Underline from '@tiptap/extension-underline';
import Link from '@tiptap/extension-link';
import Table from '@tiptap/extension-table';
import TableRow from '@tiptap/extension-table-row';
import TableHeader from '@tiptap/extension-table-header';
import TableCell from '@tiptap/extension-table-cell';
import { InsertionMark } from '../widgets/marks/InsertionMark';
import { DeletionMark } from '../widgets/marks/DeletionMark';
import { CommentAnchorMark } from '../widgets/marks/CommentAnchorMark';
import { COMPOSE_R3_PARAID } from '../widgets/paraIdExtension';
import { COMPOSE_R4_OPAQUE_ATOMS } from '../widgets/opaqueAtomNode';
import { stampParaIds, captureParaIdSnapshot, buildImportedContentModel } from './docxBridge';
import type { TipTapNode } from './docxBridge';
import type { ComposeContentModel, ComposeInlineRun, ParaIdMapEntry } from '../types/compose-contracts';

/** Mirrors the REAL editor's extension list (`ComposeEditor.tsx`) — the atoms must be the shipped schema. */
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
      ...COMPOSE_R4_OPAQUE_ATOMS,
    ],
    content,
  });
}

function stamp(editor: Editor, ids: string[]): void {
  const map: ParaIdMapEntry[] = ids.map((paraId, index) => ({ index, paraId, isMinted: false }));
  stampParaIds(editor, map);
}

const NO_THREADS: never[] = [];

/**
 * The corpus's real cross-reference, in the COMPLEX (`w:fldChar` run-sequence) form — exactly the markup
 * `ComposeDocxProjectionBuilder.AppendAtom` emits once `FieldAtomDataAttributes` says the field is
 * carryable. Leading/trailing spaces in the instruction are Word's own and are part of the field.
 */
const REF_INSTRUCTION = ' REF _Ref_Confidentiality \\r \\h ';
const REF_FIELD_ATOM =
  '<span class="compose-atom" data-atom-kind="field" data-field-instr="' +
  REF_INSTRUCTION +
  '" data-field-complex="1" contenteditable="false">4</span>';

/**
 * The compact `w:fldSimple` form (no `data-field-complex`), frozen by the author and marked dirty. The
 * embedded double quotes arrive `&quot;`-escaped because that is what `AppendEscapedAttr` writes — so this
 * constant also exercises entity round-tripping through the parse, not just the plain-ASCII happy path.
 */
const DATE_INSTRUCTION = ' DATE \\@ "d MMMM yyyy" ';
const DATE_FIELD_ATOM =
  '<span class="compose-atom" data-atom-kind="field" data-field-instr=" DATE \\@ &quot;d MMMM yyyy&quot; "' +
  ' data-field-locked="1" data-field-dirty="1" contenteditable="false">1 January 2026</span>';

/**
 * A field the server REFUSED to make carryable — a nested `{ IF { PAGE } = 1 … }` or one with no
 * recoverable instruction. Note what is missing: `data-field-instr`. Its ABSENCE is the whole contract.
 */
const UNCARRYABLE_FIELD_ATOM = '<span class="compose-atom" data-atom-kind="field" contenteditable="false">1</span>';

/** The `field` payload the loaded (server-projected) model carries for the REF atom above. */
const REF_LOADED_RUN: ComposeInlineRun = {
  text: '',
  field: { instruction: REF_INSTRUCTION, cachedResult: '4', complex: true, locked: false, dirty: false },
};

function loadedRefParagraph(): ComposeContentModel {
  return {
    blocks: [
      {
        kind: 'Paragraph',
        paraId: 'AAAA0001',
        runs: [{ text: 'Section ' }, { ...REF_LOADED_RUN }, { text: ' applies.' }],
      },
    ],
  };
}

/** Depth-first find of the single `composeInlineAtom` node in the editor's JSON. */
function findAtom(node: TipTapNode): TipTapNode | undefined {
  if (node.type === 'composeInlineAtom') return node;
  for (const child of node.content ?? []) {
    const found = findAtom(child);
    if (found) return found;
  }
  return undefined;
}

describe('task 057 — the field atom carries its payload through the editor', () => {
  it('survives parse -> edit -> serialize (the payload half is usable at all)', () => {
    // The task's second escalation trigger, measured rather than assumed: if the server's data-field-*
    // attributes do not reach the ProseMirror node, there is nothing for the mapper to read and the carry
    // cannot be built. ProseMirror keeps only the attributes the node's schema DECLARES.
    const editor = makeEditor();
    editor.commands.setContent(`<p>Section ${REF_FIELD_ATOM} applies.</p>`);

    const atom = findAtom(editor.getJSON() as TipTapNode);
    expect(atom).toBeDefined();
    expect(atom!.attrs?.kind).toBe('field');
    expect(atom!.attrs?.fieldInstruction).toBe(REF_INSTRUCTION);
    expect(atom!.attrs?.fieldComplex).toBe(true);

    // An edit ELSEWHERE in the same paragraph must not disturb it — this is the case the whole task exists
    // for (a keystroke edit rebuilds the paragraph around the atom).
    editor.commands.insertContentAt(editor.state.doc.content.size - 1, ' X');
    const afterEdit = findAtom(editor.getJSON() as TipTapNode);
    expect(afterEdit!.attrs?.fieldInstruction).toBe(REF_INSTRUCTION);

    // Serialize: `getHTML()` is what the local draft store persists (`ComposeEditor.getDraftHtml`), so a
    // payload that is parsed but not re-emitted would be lost by a draft restore — the same round trip that
    // has to survive, one layer out.
    const html = editor.getHTML();
    expect(html).toContain('data-field-instr=');
    expect(html).toContain('data-field-complex="1"');

    editor.destroy();
  });

  it('survives a getHTML() -> re-parse round trip without the placeholder LABEL entering the payload', () => {
    // Found while implementing, and it is the reason `data-atom-display` exists. An OPAQUE atom renders as
    // "<label>: <displayText>", so re-parsing this node's own `getHTML()` output used to read `Field: 4`
    // back as the display text, and a second pass `Field: Field: 4`. Harmless while that string was only a
    // UI label — NOT harmless once task 057 made it the field's `cachedResult`, a string the renderer
    // writes into the saved document.
    //
    // The round trip is reachable, not theoretical: `ComposeEditor.getDraftHtml` persists `getHTML()` to
    // the local draft store on the ~15s dirty-autosave tick (for imported documents too), and the FR-03
    // recovery path re-mounts exactly that HTML.
    const first = makeEditor(`<p>Section ${REF_FIELD_ATOM} applies.</p>`);
    const second = makeEditor(first.getHTML());
    const third = makeEditor(second.getHTML());

    for (const editor of [second, third]) {
      const atom = findAtom(editor.getJSON() as TipTapNode)!;
      expect(atom.attrs?.displayText).toBe('4');
      expect(atom.attrs?.fieldInstruction).toBe(REF_INSTRUCTION);
      expect(atom.attrs?.fieldComplex).toBe(true);
    }

    first.destroy();
    second.destroy();
    third.destroy();
  });

  it('carries the lock and dirty flags, and distinguishes the simple form from the complex one', () => {
    // Dropping `w:fldLock` is the ONE way this carry could be worse than flattening: it converts a field
    // the author deliberately froze into a live one (049 decisions §3).
    const editor = makeEditor();
    editor.commands.setContent(`<p>Dated ${DATE_FIELD_ATOM}.</p>`);

    const atom = findAtom(editor.getJSON() as TipTapNode)!;
    expect(atom.attrs?.fieldInstruction).toBe(DATE_INSTRUCTION);
    expect(atom.attrs?.fieldComplex).toBe(false); // w:fldSimple — the form is reproduced, not normalised
    expect(atom.attrs?.fieldLocked).toBe(true);
    expect(atom.attrs?.fieldDirty).toBe(true);

    editor.destroy();
  });
});

describe('task 057 — the text coordinate space does not move', () => {
  it('a field contributes ZERO characters to the reject-state text', () => {
    // Unchanged from before this task and asserted here so it cannot drift: a field's display text is a UI
    // label ("Field: 3"), and injecting a label into the document's coordinate space would be worse than
    // the zero it contributes. This is what makes a field NOT the tab/symbol case.
    const editor = makeEditor();
    editor.commands.setContent(`<p>Section ${REF_FIELD_ATOM} applies.</p>`);
    stamp(editor, ['AAAA0001']);

    expect(captureParaIdSnapshot(editor).get('AAAA0001')).toBe('Section  applies.');

    editor.destroy();
  });

  it('the posted run stream carries the reject-state text and nothing else', () => {
    // The OUTPUT-side half of the same invariant: what the server receives must read exactly as the editor
    // reads, so the field's cached result can never be duplicated into prose beside the marker run that
    // re-authors it. Measured through the FRESH-block path (no loaded counterpart, so no merge decision
    // intervenes); the snapshot is `rejectStateText`'s output for the same paragraph.
    //
    // Note what this does NOT prove: the marker branch discards its segment's text by contract, so this
    // assertion survives a segment carrying the wrong number of characters. The byte-identity of the two
    // WALKS is proven by the two tests below, both of which were verified to fail when the field segment
    // was given its display text instead of ''.
    const editor = makeEditor();
    editor.commands.setContent(`<p>Section ${REF_FIELD_ATOM} applies.</p><p>Second ${DATE_FIELD_ATOM} one.</p>`);
    stamp(editor, ['AAAA0001', 'BBBB0002']);
    const snapshot = captureParaIdSnapshot(editor);

    const { model } = buildImportedContentModel(editor, { blocks: [] }, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    // No insertion-marked content in this document, so the fresh run stream's text is exactly the
    // non-insertion segment concatenation the reject-state walk also produces.
    expect(model.blocks[0].runs!.map(r => r.text).join('')).toBe(snapshot.get('AAAA0001'));
    expect(model.blocks[1].runs!.map(r => r.text).join('')).toBe(snapshot.get('BBBB0002'));

    editor.destroy();
  });

  it('a formatting-only edit around a field produces NO phantom redline regions', () => {
    // BYTE-IDENTITY PROOF #1, in the coordinate space that actually matters. The rebuild tier diffs the
    // load-time baseline (`rejectStateText`'s output, via the snapshot) against the CURRENT segment walk's
    // concatenation. If the two walks disagree by even one character, the diff reports an insert and a
    // delete the user never made, and every offset after the field shifts. Equal walks => zero regions =>
    // no run carries a revision fact.
    //
    // Verified to bite: with the field segment carrying its display text instead of '', this test fails
    // with 21 lines of Inserted/Deleted runs for text nobody typed.
    const editor = makeEditor();
    editor.commands.setContent(`<p>Section ${REF_FIELD_ATOM} applies.</p>`);
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);

    // Bold "Section " — the text is untouched, so this drops to the rebuild tier via `formattingUnchanged`
    // while leaving the reject text exactly equal to the baseline.
    editor.commands.setTextSelection({ from: 1, to: 9 });
    editor.commands.setBold();

    const loaded = loadedRefParagraph();
    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    const runs = model.blocks[0].runs!;
    expect(model.blocks[0]).not.toBe(loaded.blocks[0]); // it really did rebuild
    expect(runs.some(r => r.bold === true)).toBe(true); // and really did take the formatting edit
    expect(runs.filter(r => r.revision !== undefined)).toEqual([]); // …with no phantom redline

    editor.destroy();
  });

  it('an UNTOUCHED paragraph containing a field is still passed through by object identity', () => {
    // BYTE-IDENTITY PROOF #2, and the most direct one available from outside the module: the verbatim tier
    // is gated on `rejectText === baseline` — a `===` between `collectSegments`' concatenation and
    // `rejectStateText`'s output for the same paragraph — plus `formattingUnchanged`, which walks the two
    // char streams (editor segments vs loaded runs) in parallel. A field counted on one side but not the
    // other desynchronizes them. Either failure demotes this block to the rebuild tier, forfeiting the
    // verbatim clone that ADR-049's preserve-untouched invariant is built on.
    //
    // Verified to bite: with the field segment carrying its display text, this returns a rebuilt block.
    const editor = makeEditor();
    editor.commands.setContent(`<p>Section ${REF_FIELD_ATOM} applies.</p><p>Untouched too.</p>`);
    stamp(editor, ['AAAA0001', 'BBBB0002']);
    const snapshot = captureParaIdSnapshot(editor);

    const fieldBlock = loadedRefParagraph().blocks[0];
    const loaded: ComposeContentModel = {
      blocks: [fieldBlock, { kind: 'Paragraph', paraId: 'BBBB0002', runs: [{ text: 'Untouched too.' }] }],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    // Object identity, not deep equality: a rebuilt block that happens to look the same would pass a
    // structural comparison while having lost every server-set fact the original carried.
    expect(model.blocks[0]).toBe(fieldBlock);

    editor.destroy();
  });
});

describe('task 057 — a field in an EDITED paragraph reaches the posted model', () => {
  it('round-trips a complex field as a marker run carrying the instruction VERBATIM, in place', () => {
    const editor = makeEditor();
    editor.commands.setContent(`<p>Section ${REF_FIELD_ATOM} applies.</p>`);
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);

    // The keystroke this whole task is about.
    editor.commands.insertContentAt(editor.state.doc.content.size - 1, ' X');

    const { model } = buildImportedContentModel(editor, loadedRefParagraph(), snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    const runs = model.blocks[0].runs!;
    const fieldRuns = runs.filter(r => r.field !== undefined);
    expect(fieldRuns.length).toBe(1);

    // Verbatim, spaces included: Word writes " REF _Ref1 \h " and trimming it changes the field.
    expect(fieldRuns[0].field).toEqual({
      instruction: REF_INSTRUCTION,
      cachedResult: '4',
      complex: true,
      locked: false,
      dirty: false,
    });

    // Marker-run contract: the run IS the field, so it carries no text of its own — and the cached result
    // must NOT also survive as prose, which would print "4" twice.
    expect(fieldRuns[0].text).toBe('');
    expect(runs.map(r => r.text).join('')).not.toContain('4');

    // Position is meaning: a cross-reference in the wrong place is its own kind of damage, so presence
    // alone would be a test that passes while the document is wrong.
    const markerIndex = runs.findIndex(r => r.field !== undefined);
    expect(
      runs
        .slice(0, markerIndex)
        .map(r => r.text)
        .join('')
    ).toBe('Section ');
    expect(
      runs
        .slice(markerIndex + 1)
        .map(r => r.text)
        .join('')
    ).toContain('applies.');

    editor.destroy();
  });

  it('round-trips the simple form as simple, with its lock and dirty flags', () => {
    const editor = makeEditor();
    editor.commands.setContent(`<p>Dated ${DATE_FIELD_ATOM}.</p>`);
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);

    editor.commands.insertContentAt(editor.state.doc.content.size - 1, ' X');

    const loaded: ComposeContentModel = {
      blocks: [
        {
          kind: 'Paragraph',
          paraId: 'AAAA0001',
          runs: [
            { text: 'Dated ' },
            {
              text: '',
              field: {
                instruction: DATE_INSTRUCTION,
                cachedResult: '1 January 2026',
                complex: false,
                locked: true,
                dirty: true,
              },
            },
            { text: '.' },
          ],
        },
      ],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    const fieldRuns = model.blocks[0].runs!.filter(r => r.field !== undefined);
    expect(fieldRuns.length).toBe(1);
    expect(fieldRuns[0].field).toEqual({
      instruction: DATE_INSTRUCTION,
      cachedResult: '1 January 2026',
      complex: false, // w:fldSimple comes back as w:fldSimple — the form is reproduced, not normalised
      locked: true,
      dirty: true,
    });

    editor.destroy();
  });

  it('honours the server refusal: an atom with NO data-field-instr produces NO field run', () => {
    // The gate lives in ONE place — the server's `TryCarryField` rule, mirrored into the read-side payload.
    // A nested or instruction-less field gets no payload, so the client structurally cannot hand back a
    // construct the server would have to refuse. This paragraph must behave exactly as it did before this
    // task: the atom contributes nothing, and no field is invented from its display text.
    const editor = makeEditor();
    editor.commands.setContent(`<p>Clause ${UNCARRYABLE_FIELD_ATOM} follows.</p>`);
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);
    expect(snapshot.get('AAAA0001')).toBe('Clause  follows.');

    editor.commands.insertContentAt(editor.state.doc.content.size - 1, ' X');

    const loaded: ComposeContentModel = {
      blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Clause ' }, { text: ' follows.' }] }],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    const runs = model.blocks[0].runs!;
    expect(runs.every(r => r.field === undefined)).toBe(true);
    // …and nothing invented from the label or the display text either.
    expect(runs.map(r => r.text).join('')).not.toContain('Field');
    expect(runs.map(r => r.text).join('')).not.toContain('1');

    editor.destroy();
  });
});
