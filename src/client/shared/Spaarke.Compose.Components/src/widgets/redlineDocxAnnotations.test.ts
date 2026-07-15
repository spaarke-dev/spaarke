/**
 * redlineDocxAnnotations.test.ts — redline → Word save-fidelity bridge (UAT-R7 #2/#3/#4).
 *
 * Covers the two PURE functions that make a redlined Save persist as NATIVE Word tracked-changes:
 *   - `redlineMarksToDocxAnnotations` (useComposeWordShuttle) — maps the editor's PENDING
 *     insertion/deletion marks → DocxAnnotationInput[] the BFF DocxAnnotationWriter renders as
 *     w:ins/w:del. This is the producer the redline marks (usePendingRedline) never had.
 *   - `buildRejectBaselineJson` (docxBridge) — reduces the doc to its reject-state BASELINE (drop
 *     proposed insertions, keep struck originals as plain text) so the annotations re-apply cleanly.
 *
 * Both are pure tree transforms — no editor, no docx packer — so these are fast domain tests.
 */

import { redlineMarksToDocxAnnotations, DocxTrackChangeKind } from './useComposeWordShuttle';
import { buildRejectBaselineJson, type TipTapNode } from '../utils/docxBridge';

/** A TipTap text node carrying zero or more redline marks. */
function text(value: string, marks: Array<{ type: string; ledgerRef?: string; binding?: string }> = []): TipTapNode {
  return {
    type: 'text',
    text: value,
    marks: marks.map(m => ({ type: m.type, attrs: { ledgerRef: m.ledgerRef, binding: m.binding } })),
  };
}

/** A paragraph wrapping inline nodes. */
function para(...content: TipTapNode[]): TipTapNode {
  return { type: 'paragraph', content };
}

/** A doc wrapping block nodes. */
function doc(...content: TipTapNode[]): TipTapNode {
  return { type: 'doc', content };
}

const AUTHOR = 'Spaarke Assistant';
const DATE = '2026-07-13T10:00:00.000Z';

describe('redlineMarksToDocxAnnotations — pending redline marks → DocxAnnotationInput[]', () => {
  it('maps a replace pair (deletion + insertion, same ledgerRef) to Insertion-BEFORE-Deletion', () => {
    // Document order after usePendingRedline: struck original ("indemnify", deletion) then the
    // inserted alternative ("defend", insertion), both keyed by the same ledgerRef.
    const json = doc(
      para(
        text('The Supplier shall '),
        text('indemnify', [{ type: 'deletion', ledgerRef: 'b1@t1', binding: 'b1' }]),
        text('defend', [{ type: 'insertion', ledgerRef: 'b1@t1', binding: 'b1' }]),
        text(' the Customer.')
      )
    );

    const result = redlineMarksToDocxAnnotations(json, AUTHOR, DATE);

    expect(result).toEqual([
      { kind: DocxTrackChangeKind.Insertion, targetText: 'indemnify', newText: 'defend', author: AUTHOR, date: DATE },
      { kind: DocxTrackChangeKind.Deletion, targetText: 'indemnify', author: AUTHOR, date: DATE },
    ]);
  });

  it('maps a pure insertion (no deletion half) to a single anchorless Insertion', () => {
    const json = doc(
      para(
        text('Existing sentence.'),
        text(' Appended clause.', [{ type: 'insertion', ledgerRef: 'b2@t1', binding: 'b2' }])
      )
    );

    const result = redlineMarksToDocxAnnotations(json, AUTHOR, DATE);

    expect(result).toEqual([
      {
        kind: DocxTrackChangeKind.Insertion,
        targetText: null,
        newText: ' Appended clause.',
        author: AUTHOR,
        date: DATE,
      },
    ]);
  });

  it('maps a pure deletion to a single Deletion', () => {
    const json = doc(
      para(text('Remove '), text('this clause', [{ type: 'deletion', ledgerRef: 'b3@t1' }]), text(' please.'))
    );

    const result = redlineMarksToDocxAnnotations(json, AUTHOR, DATE);

    expect(result).toEqual([
      { kind: DocxTrackChangeKind.Deletion, targetText: 'this clause', author: AUTHOR, date: DATE },
    ]);
  });

  it('concatenates split runs sharing one ledgerRef and preserves first-seen ledgerRef order', () => {
    const json = doc(
      para(
        text('foo', [{ type: 'deletion', ledgerRef: 'a@t1' }]),
        text('bar', [{ type: 'insertion', ledgerRef: 'a@t1' }]),
        text('baz', [{ type: 'insertion', ledgerRef: 'a@t1' }]) // split insertion run, same ref
      ),
      para(text('qux', [{ type: 'deletion', ledgerRef: 'z@t1' }]))
    );

    const result = redlineMarksToDocxAnnotations(json, AUTHOR, DATE);

    // 'a@t1' (seen first) before 'z@t1'; its insertion halves concatenate to 'barbaz'.
    expect(result).toEqual([
      { kind: DocxTrackChangeKind.Insertion, targetText: 'foo', newText: 'barbaz', author: AUTHOR, date: DATE },
      { kind: DocxTrackChangeKind.Deletion, targetText: 'foo', author: AUTHOR, date: DATE },
      { kind: DocxTrackChangeKind.Deletion, targetText: 'qux', author: AUTHOR, date: DATE },
    ]);
  });

  it('ignores plain text and marks without a ledgerRef (returns empty for a clean doc)', () => {
    const json = doc(para(text('Nothing tracked here.'), text('bold', [{ type: 'bold' }])));
    expect(redlineMarksToDocxAnnotations(json, AUTHOR, DATE)).toEqual([]);
  });
});

describe('buildRejectBaselineJson — reject-state baseline for the Save annotation path', () => {
  it('drops proposed-insertion text and keeps struck-original text as plain text', () => {
    const json = doc(
      para(
        text('The Supplier shall '),
        text('indemnify', [{ type: 'deletion', ledgerRef: 'b1@t1', binding: 'b1' }]),
        text('defend', [{ type: 'insertion', ledgerRef: 'b1@t1', binding: 'b1' }]),
        text(' the Customer.')
      )
    );

    const baseline = buildRejectBaselineJson(json);
    const runs = baseline.content![0].content!;

    // The insertion ("defend") is gone; the deletion original ("indemnify") stays, with its redline
    // mark stripped so it serializes as ordinary text.
    const texts = runs.map(r => r.text);
    expect(texts).toEqual(['The Supplier shall ', 'indemnify', ' the Customer.']);
    const struck = runs.find(r => r.text === 'indemnify')!;
    expect(struck.marks ?? []).toEqual([]);
  });

  it('preserves non-redline marks (e.g. bold) untouched', () => {
    const json = doc(para(text('kept', [{ type: 'bold' }]), text('gone', [{ type: 'insertion', ledgerRef: 'x@t1' }])));

    const baseline = buildRejectBaselineJson(json);
    const runs = baseline.content![0].content!;

    expect(runs.map(r => r.text)).toEqual(['kept']);
    expect(runs[0].marks).toEqual([{ type: 'bold', attrs: { ledgerRef: undefined, binding: undefined } }]);
  });

  it('does not mutate the input tree', () => {
    const json = doc(para(text('gone', [{ type: 'insertion', ledgerRef: 'x@t1' }])));
    const snapshot = JSON.stringify(json);
    buildRejectBaselineJson(json);
    expect(JSON.stringify(json)).toEqual(snapshot);
  });
});
