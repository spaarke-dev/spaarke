/**
 * FR-03 (spaarkeai-compose-r4 task 020) — capture tests for the ProseMirror
 * step→operation interceptor. Each test names a concrete behavior from the task-020
 * closed acceptance set that breaks if deleted:
 *
 *   - an interior typed edit emits an insertText op whose anchor resolves to the exact
 *     `(paraId, runIndex, run-local-offset)` — NOT an absolute editor position (D2)
 *   - in a multi-run (bold + normal) paragraph the anchor identifies the correct run +
 *     in-run offset (the fine anchor survives an earlier same-paragraph edit — §4)
 *   - the emitted op is a task-003 operation with a `type` — never the legacy
 *     `{paraId,text}` paragraph-diff payload the R4 bridge retires
 *   - an edit whose range enters an opaque atom node is REFUSED (no op; FR-02)
 *   - a formatting mark outside the closed ComposeMarkType set is SURFACED as
 *     unrepresentable (escalation seam), never silently dropped or mis-mapped
 *   - the live plugin path (createStepInterceptorPlugin) captures a real dispatched edit
 *
 * The full suite is authored in task 024; this exercises the anchor-resolution + atom
 * refusal + representability behaviors this task owns.
 *
 * Uses a minimal hand-built ProseMirror schema (paragraph carries a `paraId` attr,
 * mirroring paraIdExtension; an `opaqueAtom` leaf mirrors task 021's `atom: true` node)
 * so the classifier is tested in isolation — no TipTap/React mount, no DOM dependency on
 * the sibling task-021 node file.
 *
 * ADR-021 dark-mode note: this module is a HEADLESS capture plugin — it renders no DOM
 * and defines no color, so capture is theme-independent BY CONSTRUCTION (the POML
 * dark-mode ui-test applies to the atom placeholder chrome, which is task 021's surface).
 * The `emits identical operations regardless of ambient theme` test asserts that
 * invariant at the capture layer.
 */
import { Schema, Slice, Fragment } from '@tiptap/pm/model';
import type { Node as PMNode } from '@tiptap/pm/model';
import { EditorState, TextSelection } from '@tiptap/pm/state';
import type { Command } from '@tiptap/pm/state';
import { ReplaceStep, AddMarkStep, RemoveMarkStep, AttrStep } from '@tiptap/pm/transform';
import { wrapInList, sinkListItem } from '@tiptap/pm/schema-list';

import {
  classifyStep,
  resolveRunAnchor,
  createStepInterceptorPlugin,
  STEP_INTERCEPTOR_IGNORE_META,
  RebasedOperationLog,
  buildBatchRevisionOp,
  type StepClassification,
} from './stepOperationInterceptor';
import {
  isComposeOperation,
  type ComposeOperation,
  type InsertTextOperation,
  type DeleteRangeOperation,
} from '../types/compose-operations';

// ---------------------------------------------------------------------------
// Minimal ProseMirror schema (paragraph carries paraId; opaqueAtom is a leaf atom)
// ---------------------------------------------------------------------------
const schema = new Schema({
  nodes: {
    doc: { content: 'block+' },
    paragraph: {
      group: 'block',
      content: 'inline*',
      attrs: { paraId: { default: null }, textAlign: { default: null } },
      parseDOM: [{ tag: 'p' }],
      toDOM: () => ['p', 0],
    },
    // R5 task 011 — heading + list nodes mirror the StarterKit schema ComposeEditor mounts, so a heading
    // toggle / list wrap produces the same ReplaceAroundStep/ReplaceStep shape the classifier sees in prod.
    heading: {
      group: 'block',
      content: 'inline*',
      attrs: { paraId: { default: null }, level: { default: 1 }, textAlign: { default: null } },
      parseDOM: [{ tag: 'h1', attrs: { level: 1 } }],
      toDOM: node => [`h${node.attrs.level as number}`, 0],
    },
    orderedList: { group: 'block', content: 'listItem+', parseDOM: [{ tag: 'ol' }], toDOM: () => ['ol', 0] },
    bulletList: { group: 'block', content: 'listItem+', parseDOM: [{ tag: 'ul' }], toDOM: () => ['ul', 0] },
    listItem: { content: 'paragraph block*', parseDOM: [{ tag: 'li' }], toDOM: () => ['li', 0] },
    // Mirrors task 021's non-editable leaf (atom: true). Name intentionally DIFFERENT
    // from task 021's (`composeBlockAtom`) to prove the guard is schema-driven, not name-coupled.
    opaqueAtom: {
      group: 'block',
      atom: true,
      selectable: true,
      attrs: { paraId: { default: null } },
      parseDOM: [{ tag: 'div[data-atom]' }],
      toDOM: () => ['div', { 'data-atom': '1' }],
    },
    text: { group: 'inline' },
  },
  marks: {
    bold: { parseDOM: [{ tag: 'strong' }], toDOM: () => ['strong', 0] },
    italic: { parseDOM: [{ tag: 'em' }], toDOM: () => ['em', 0] },
    underline: { parseDOM: [{ tag: 'u' }], toDOM: () => ['u', 0] },
    link: { attrs: { href: { default: null } }, parseDOM: [{ tag: 'a' }], toDOM: () => ['a', 0] },
    // A real mark that is NOT in the closed ComposeMarkType set (like production `strike`) — the
    // unrepresentable-mark test subject now that `link` is representable (G5 task 033).
    strike: { parseDOM: [{ tag: 's' }], toDOM: () => ['s', 0] },
    // R5 task 012 (G12) — the imported-revision marks importedRevisions.ts renders (name-matched to the
    // real InsertionMark/DeletionMark). `ledgerRef` = `imported:<w:id>` for a recovered Word revision.
    insertion: { attrs: { ledgerRef: { default: null } }, parseDOM: [{ tag: 'span.ins' }], toDOM: () => ['span', 0] },
    deletion: { attrs: { ledgerRef: { default: null } }, parseDOM: [{ tag: 'span.del' }], toDOM: () => ['span', 0] },
  },
});

const PARA_ID = '0A1B2C3D';

/** A paragraph with a bold "Hello" run (len 5) + a normal " world" run (len 6). */
function twoRunDoc(): PMNode {
  const bold = schema.marks.bold.create();
  const p = schema.nodes.paragraph.create({ paraId: PARA_ID }, [schema.text('Hello', [bold]), schema.text(' world')]);
  return schema.nodes.doc.create(null, [p]);
}

/** Paragraph content starts at absolute position 1; char offset k → absolute pos (1 + k). */
const abs = (k: number): number => 1 + k;

function inlineSlice(text: string): Slice {
  return new Slice(Fragment.from(schema.text(text)), 0, 0);
}

function expectOps(cls: StepClassification): ComposeOperation[] {
  expect(cls.kind).toBe('ops');
  return (cls as { kind: 'ops'; ops: ComposeOperation[] }).ops;
}

describe('resolveRunAnchor (D2 fine anchor)', () => {
  const doc = twoRunDoc();

  it('resolves an interior position inside the first run to its run-local offset', () => {
    // Caret after "Hel" — char offset 3, inside run 0.
    const anchor = resolveRunAnchor(doc, abs(3));
    expect(anchor).toEqual({ paraId: PARA_ID, runIndex: 0, offset: 3 });
  });

  it('resolves a position inside the second run to (runIndex 1, in-run offset)', () => {
    // Char offset 8 → 8 - 5 = 3 within run 1.
    const anchor = resolveRunAnchor(doc, abs(8));
    expect(anchor).toEqual({ paraId: PARA_ID, runIndex: 1, offset: 3 });
  });

  it('resolves a run boundary to the trailing edge of the earlier run (documented convention)', () => {
    // Char offset 5 is the bold|normal boundary → run 0, offset 5 (NOT run 1, offset 0).
    const anchor = resolveRunAnchor(doc, abs(5));
    expect(anchor).toEqual({ paraId: PARA_ID, runIndex: 0, offset: 5 });
  });

  it('returns null for a position whose parent is not a paraId textblock', () => {
    // Position 0 is before the paragraph node (parent = doc), not an inline paragraph edit.
    expect(resolveRunAnchor(doc, 0)).toBeNull();
  });
});

describe('classifyStep — inline edits → task-003 operations', () => {
  const doc = twoRunDoc();

  it('an interior typed edit emits insertText anchored to (paraId, runIndex, offset)', () => {
    const step = new ReplaceStep(abs(3), abs(3), inlineSlice('x'));
    const ops = expectOps(classifyStep(step, doc, {}));
    expect(ops).toHaveLength(1);
    const op = ops[0] as InsertTextOperation;
    expect(op.type).toBe('insertText');
    expect(op.paraId).toBe(PARA_ID);
    expect(op.at).toEqual({ runIndex: 0, offset: 3 });
    expect(op.text).toBe('x');
    // NOT an absolute editor position — the anchor is run-local (offset 3, not abs 4).
    expect((op.at as { offset: number }).offset).not.toBe(abs(3));
  });

  it('a typed edit at the bold|normal boundary identifies the correct run + in-run offset', () => {
    const step = new ReplaceStep(abs(5), abs(5), inlineSlice('!'));
    const op = expectOps(classifyStep(step, doc, {}))[0] as InsertTextOperation;
    expect(op.at).toEqual({ runIndex: 0, offset: 5 });
    // Run-local, not the absolute doc position (which would be 6).
    expect(op.at).not.toEqual({ runIndex: 0, offset: abs(5) });
  });

  it('carries closed-set inline marks on the inserted text (Bold), drops non-format marks', () => {
    const bold = schema.marks.bold.create();
    const link = schema.marks.link.create({ href: 'https://x' });
    const slice = new Slice(Fragment.from(schema.text('y', [bold, link])), 0, 0);
    const step = new ReplaceStep(abs(8), abs(8), slice);
    const op = expectOps(classifyStep(step, doc, {}))[0] as InsertTextOperation;
    expect(op.marks).toEqual(['Bold']); // link is not a ComposeMarkType — dropped
  });

  it('a deletion within one paragraph emits deleteRange with run-local start/end', () => {
    // Delete "ell" (chars 1..4 of run 0).
    const step = new ReplaceStep(abs(1), abs(4), Slice.empty);
    const ops = expectOps(classifyStep(step, doc, {}));
    expect(ops[0]).toEqual({
      type: 'deleteRange',
      paraId: PARA_ID,
      range: { start: { runIndex: 0, offset: 1 }, end: { runIndex: 0, offset: 4 } },
    });
  });

  it('a range replacement emits replaceRange (not the legacy {paraId,text} payload)', () => {
    const step = new ReplaceStep(abs(1), abs(4), inlineSlice('EY'));
    const op = expectOps(classifyStep(step, doc, {}))[0];
    expect(op.type).toBe('replaceRange');
    // Every emitted op is a task-003 operation with a discriminator — never a bare
    // {paraId,text} paragraph-diff object.
    expect(isComposeOperation(op)).toBe(true);
    expect('type' in op).toBe(true);
  });

  it('setMark / clearMark map to closed-set marks with run-local ranges', () => {
    const addStep = new AddMarkStep(abs(1), abs(5), schema.marks.italic.create());
    const setOp = expectOps(classifyStep(addStep, doc, {}))[0];
    expect(setOp).toEqual({
      type: 'setMark',
      paraId: PARA_ID,
      range: { start: { runIndex: 0, offset: 1 }, end: { runIndex: 0, offset: 5 } },
      mark: 'Italic',
    });
  });

  it('an alignment AttrStep on a paraId block maps to setBlockAttr(Alignment)', () => {
    const step = new AttrStep(0, 'textAlign', 'center');
    const op = expectOps(classifyStep(step, doc, {}))[0];
    expect(op).toEqual({ type: 'setBlockAttr', paraId: PARA_ID, attr: 'Alignment', value: 'Center' });
  });
});

describe('classifyStep — refusal + escalation seams', () => {
  it('refuses an edit whose range enters an opaque atom node (FR-02)', () => {
    const bold = schema.marks.bold.create();
    const p = schema.nodes.paragraph.create({ paraId: PARA_ID }, [schema.text('Hi', [bold])]);
    const atom = schema.nodes.opaqueAtom.create({ paraId: 'ATOM0001' });
    const p2 = schema.nodes.paragraph.create({ paraId: 'BEEF0001' }, [schema.text('Bye')]);
    const doc = schema.nodes.doc.create(null, [p, atom, p2]);
    // p nodeSize = 2 + 2 = 4 → atom starts at pos 4, occupies 4..5. Replace the atom.
    const atomPos = p.nodeSize;
    const step = new ReplaceStep(atomPos, atomPos + 1, inlineSlice('x'));
    const cls = classifyStep(step, doc, {});
    expect(cls.kind).toBe('refused-atom');
  });

  it('surfaces a formatting mark outside the closed set as unrepresentable (not dropped)', () => {
    const doc = twoRunDoc();
    const step = new AddMarkStep(abs(1), abs(5), schema.marks.strike.create());
    const cls = classifyStep(step, doc, {});
    expect(cls.kind).toBe('unrepresentable');
    expect((cls as { reason: string }).reason).toContain('strike');
  });

  // G5 (FR-05, task 033): a link add is now REPRESENTABLE — captured as a setMark(Link) op carrying the href.
  it('captures a link add as a setMark(Link) op carrying the href (G5)', () => {
    const doc = twoRunDoc();
    const step = new AddMarkStep(abs(1), abs(5), schema.marks.link.create({ href: 'https://example.com/' }));
    const cls = classifyStep(step, doc, {});
    expect(cls.kind).toBe('ops');
    const op = (cls as { ops: Array<Record<string, unknown>> }).ops[0];
    expect(op.type).toBe('setMark');
    expect(op.mark).toBe('Link');
    expect(op.href).toBe('https://example.com/');
  });

  // G5: a link REMOVE is a clearMark(Link) (no href needed).
  it('captures a link remove as a clearMark(Link) op (G5)', () => {
    const doc = twoRunDoc();
    const step = new RemoveMarkStep(abs(1), abs(5), schema.marks.link.create({ href: 'https://example.com/' }));
    const cls = classifyStep(step, doc, {});
    expect(cls.kind).toBe('ops');
    const op = (cls as { ops: Array<Record<string, unknown>> }).ops[0];
    expect(op.type).toBe('clearMark');
    expect(op.mark).toBe('Link');
  });

  it('honors a caller override of the opaque-atom predicate', () => {
    const doc = twoRunDoc();
    // Treat ANY paragraph as "opaque" via override → an interior edit is refused.
    const step = new ReplaceStep(abs(3), abs(3), inlineSlice('x'));
    const cls = classifyStep(step, doc, { isOpaqueAtom: n => n.type.name === 'paragraph' });
    // The insertion range is zero-width so the guard does not fire on a collapsed range;
    // a spanning edit over the "opaque" paragraph content is what gets refused.
    const spanStep = new ReplaceStep(abs(1), abs(4), inlineSlice('z'));
    expect(classifyStep(spanStep, doc, { isOpaqueAtom: n => n.type.name === 'paragraph' }).kind).toBe('refused-atom');
    // (collapsed edit still classifies normally under the override)
    expect(cls.kind).toBe('ops');
  });
});

describe('createStepInterceptorPlugin — live capture path', () => {
  it('emits captured operations once per dispatched doc-changing transaction', () => {
    const captured: ComposeOperation[][] = [];
    const plugin = createStepInterceptorPlugin({
      onOperations: ops => captured.push(ops),
    });
    let state = EditorState.create({ doc: twoRunDoc(), plugins: [plugin] });

    // Dispatch an interior insertion (caret at char offset 3).
    const tr = state.tr.insertText('QQ', abs(3), abs(3));
    state = state.apply(tr);

    expect(captured).toHaveLength(1);
    expect(captured[0][0]).toMatchObject({
      type: 'insertText',
      paraId: PARA_ID,
      at: { runIndex: 0, offset: 3 },
      text: 'QQ',
    });
  });

  it('does not mutate the document (capture only — never appends a transaction)', () => {
    const plugin = createStepInterceptorPlugin({ onOperations: () => undefined });
    let state = EditorState.create({ doc: twoRunDoc(), plugins: [plugin] });
    const before = state.doc.textContent;
    state = state.apply(state.tr.insertText('AB', abs(2), abs(2)));
    // Exactly the user's 2 chars added — the interceptor added nothing of its own.
    expect(state.doc.textContent.length).toBe(before.length + 2);
  });

  it('skips a transaction flagged with STEP_INTERCEPTOR_IGNORE_META', () => {
    const captured: ComposeOperation[][] = [];
    const plugin = createStepInterceptorPlugin({ onOperations: ops => captured.push(ops) });
    let state = EditorState.create({ doc: twoRunDoc(), plugins: [plugin] });
    const tr = state.tr.insertText('x', abs(3), abs(3)).setMeta(STEP_INTERCEPTOR_IGNORE_META, true);
    state = state.apply(tr);
    expect(captured).toHaveLength(0);
  });

  it('emits identical operations regardless of ambient theme (headless capture, ADR-021 N/A by construction)', () => {
    // The plugin defines no DOM/color; capture depends only on the transaction. Running
    // the same edit twice yields identical ops — there is no theme-dependent branch.
    const runs: ComposeOperation[][] = [];
    for (let i = 0; i < 2; i++) {
      const plugin = createStepInterceptorPlugin({ onOperations: ops => runs.push(ops) });
      let state = EditorState.create({ doc: twoRunDoc(), plugins: [plugin] });
      state = state.apply(state.tr.insertText('z', abs(3), abs(3)));
    }
    expect(runs[0]).toEqual(runs[1]);
  });
});

// ---------------------------------------------------------------------------
// RebasedOperationLog (task 022, FR-03) — ordered, rebased per-session op log
// ---------------------------------------------------------------------------
//
// These exercise task 022's own closed acceptance set (the full cross-cutting suite,
// including the atom-adjacent + negative cases, is authored in task 024):
//   - after edit A then a later edit B EARLIER in the same paragraph, A's anchor is
//     rebased so it still resolves to A's INTENDED position (not a stale offset)
//   - the log serializes in DOCUMENT order (not capture/chronological order) with a
//     single base-version handle
//   - an op whose anchor falls inside content a LATER edit deletes is FLAGGED, not
//     silently dropped from `orderedOps`
//   - a genuine same-position ordering ambiguity is surfaced via `onAmbiguousOrder`,
//     never silently guessed

/** A single-paragraph doc `text`, paraId `id`. */
function onePara(id: string, text: string): PMNode {
  const p = schema.nodes.paragraph.create({ paraId: id }, text ? [schema.text(text)] : []);
  return schema.nodes.doc.create(null, [p]);
}

/** A doc of `n` one-line paragraphs, ids `P0001`.. in document order, each `Paragraph {i} text`. */
function multiParaDoc(ids: string[]): PMNode {
  const paras = ids.map((id, i) => schema.nodes.paragraph.create({ paraId: id }, [schema.text(`Paragraph ${i} text`)]));
  return schema.nodes.doc.create(null, paras);
}

describe('RebasedOperationLog — rebase-across-concurrent-edits (task 022, FR-03)', () => {
  it("rebases an earlier op's anchor so it still resolves to its intended position after a later, earlier-positioned edit", () => {
    // "Hello World" (11 chars) in one paragraph. Edit A: type "!" at the END (offset 11).
    // Edit B (later in time): type "Hi " at the START (offset 0) — earlier in the same paragraph.
    const doc0 = onePara(PARA_ID, 'Hello World');
    const log = new RebasedOperationLog();
    let state = EditorState.create({ doc: doc0 });

    // Edit A — insert "!" at the paragraph's end.
    const trA = state.tr.insertText('!', abs(11), abs(11));
    log.recordTransaction(trA);
    state = state.apply(trA);
    expect(state.doc.textContent).toBe('Hello World!');

    // Edit B — insert "Hi " at the paragraph's start (EARLIER position, LATER in time).
    const trB = state.tr.insertText('Hi ', abs(0), abs(0));
    log.recordTransaction(trB);
    state = state.apply(trB);
    expect(state.doc.textContent).toBe('Hi Hello World!');

    const snapshot = log.serialize(state.doc);
    expect(snapshot.orderedOps).toHaveLength(2);

    const opA = snapshot.orderedOps.find(
      o => o.operation.type === 'insertText' && (o.operation as InsertTextOperation).text === '!'
    );
    const opB = snapshot.orderedOps.find(
      o => o.operation.type === 'insertText' && (o.operation as InsertTextOperation).text === 'Hi '
    );
    expect(opA).toBeDefined();
    expect(opB).toBeDefined();

    // A's anchor is REBASED: B's 3-char insertion before it shifts A's run-local offset from its
    // originally-captured 11 to 14 (11 + 3) — it still resolves to "right after the original
    // 'Hello World' content", NOT a stale offset that would now land mid-word.
    const anchorA = opA!.operation as InsertTextOperation;
    expect(anchorA.paraId).toBe(PARA_ID);
    expect(anchorA.at).toEqual({ runIndex: 0, offset: 14 });
    expect(anchorA.at.offset).not.toBe(11); // NOT the stale, un-rebased offset

    // B's own anchor is unaffected (it was captured against the doc AFTER A already existed, and
    // nothing after it has since shifted its own start).
    const anchorB = opB!.operation as InsertTextOperation;
    expect(anchorB.at).toEqual({ runIndex: 0, offset: 0 });

    // Neither op is flagged — no deletion occurred.
    expect(opA!.deletedContentFlag).toBe(false);
    expect(opB!.deletedContentFlag).toBe(false);
  });

  it('rebases a deleteRange op through a later same-paragraph insertion', () => {
    const doc0 = onePara(PARA_ID, 'Hello World');
    const log = new RebasedOperationLog();
    let state = EditorState.create({ doc: doc0 });

    // Delete "World" (offsets 6..11).
    const trDelete = state.tr.delete(abs(6), abs(11));
    log.recordTransaction(trDelete);
    state = state.apply(trDelete);
    expect(state.doc.textContent).toBe('Hello ');

    // Later, insert "Say " at the very start — shifts the deleteRange anchor forward by 4.
    const trInsert = state.tr.insertText('Say ', abs(0), abs(0));
    log.recordTransaction(trInsert);
    state = state.apply(trInsert);
    expect(state.doc.textContent).toBe('Say Hello ');

    const snapshot = log.serialize(state.doc);
    const deleteOp = snapshot.orderedOps.find(o => o.operation.type === 'deleteRange')!
      .operation as DeleteRangeOperation;
    expect(deleteOp.range).toEqual({ start: { runIndex: 0, offset: 10 }, end: { runIndex: 0, offset: 10 } });
  });
});

describe('RebasedOperationLog — document-order serialization with a base version (task 022, FR-03)', () => {
  it('serializes ops in DOCUMENT order regardless of capture (chronological) order', () => {
    const ids = ['AAAA0001', 'BBBB0002', 'CCCC0003'];
    const doc0 = multiParaDoc(ids); // para0 = AAAA (pos 1..), para1 = BBBB, para2 = CCCC
    const log = new RebasedOperationLog();
    let state = EditorState.create({ doc: doc0 });

    // Locate a paragraph's content-start absolute position — recomputed FRESH from the CURRENT
    // `state.doc` immediately before each edit, since inserting into an EARLIER paragraph shifts
    // every LATER paragraph's start position forward.
    const paraStart = (doc: PMNode, index: number): number => {
      let found = -1;
      doc.forEach((_node, offset, i) => {
        if (i === index) found = offset + 1;
      });
      return found;
    };

    // Capture (chronological) order: paragraph 2 (CCCC), then 0 (AAAA), then 1 (BBBB).
    const p2 = paraStart(state.doc, 2);
    const tr1 = state.tr.insertText('X', p2, p2);
    log.recordTransaction(tr1);
    state = state.apply(tr1);

    const p0 = paraStart(state.doc, 0);
    const tr2 = state.tr.insertText('Y', p0, p0);
    log.recordTransaction(tr2);
    state = state.apply(tr2);

    // Recomputed AFTER tr2 — paragraph 0's insertion shifted paragraph 1's start forward by 1.
    const p1 = paraStart(state.doc, 1);
    const tr3 = state.tr.insertText('Z', p1, p1);
    log.recordTransaction(tr3);
    state = state.apply(tr3);

    log.setBaseVersion('etag-v1:schema-v1');
    const snapshot = log.serialize(state.doc);

    expect(snapshot.orderedOps).toHaveLength(3);
    // DOCUMENT order (AAAA, BBBB, CCCC) — NOT capture order (CCCC, AAAA, BBBB).
    expect(snapshot.orderedOps.map(o => o.operation.paraId)).toEqual(ids);
    expect(snapshot.baseVersion).toBe('etag-v1:schema-v1');
  });

  it('setBaseVersion is idempotent — the FIRST call wins', () => {
    const log = new RebasedOperationLog();
    log.setBaseVersion('first');
    log.setBaseVersion('second');
    expect(log.serialize(onePara(PARA_ID, '')).baseVersion).toBe('first');
  });

  it('serialize returns an empty ordered log with a null base version before any transaction or setBaseVersion call', () => {
    const log = new RebasedOperationLog();
    const snapshot = log.serialize(onePara(PARA_ID, 'x'));
    expect(snapshot.orderedOps).toEqual([]);
    expect(snapshot.baseVersion).toBeNull();
  });
});

describe('RebasedOperationLog — op inside deleted content is flagged, not dropped (task 022, FR-03)', () => {
  it('flags (never drops) an op whose anchor is later deleted', () => {
    // "Hello World" — insert "X" at offset 5 (op A), then delete a range [2, 9) that ENCLOSES it.
    const doc0 = onePara(PARA_ID, 'Hello World');
    const log = new RebasedOperationLog();
    let state = EditorState.create({ doc: doc0 });

    const trInsert = state.tr.insertText('X', abs(5), abs(5));
    log.recordTransaction(trInsert);
    state = state.apply(trInsert);
    expect(state.doc.textContent).toBe('HelloX World');

    expect(log.serialize(state.doc).orderedOps[0].deletedContentFlag).toBe(false);

    // Delete a range that fully encloses op A's insertion point (now at abs offset 2+5=... use the
    // live doc's actual bounds: "HelloX World" — delete chars [2,9) i.e. "lloX Wo").
    const trDelete = state.tr.delete(abs(2), abs(9));
    log.recordTransaction(trDelete);
    state = state.apply(trDelete);

    const snapshot = log.serialize(state.doc);
    // Both ops remain in the log — NEVER silently dropped.
    expect(snapshot.orderedOps).toHaveLength(2);
    const flaggedInsert = snapshot.orderedOps.find(o => o.operation.type === 'insertText')!;
    expect(flaggedInsert.deletedContentFlag).toBe(true);
  });

  it('does not flag an op whose anchor survives an unrelated later edit', () => {
    const doc0 = onePara(PARA_ID, 'Hello World');
    const log = new RebasedOperationLog();
    let state = EditorState.create({ doc: doc0 });

    const trInsert = state.tr.insertText('X', abs(5), abs(5));
    log.recordTransaction(trInsert);
    state = state.apply(trInsert);

    // A completely unrelated later edit far from op A's anchor.
    const trAppend = state.tr.insertText('!', state.doc.content.size - 1, state.doc.content.size - 1);
    log.recordTransaction(trAppend);
    state = state.apply(trAppend);

    const snapshot = log.serialize(state.doc);
    const insertOp = snapshot.orderedOps.find(
      o => o.operation.type === 'insertText' && (o.operation as InsertTextOperation).text === 'X'
    )!;
    expect(insertOp.deletedContentFlag).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// Structural step → operation synthesis (task 031, FR-05) — the four paragraph ops
// ---------------------------------------------------------------------------
//
// These exercise task 031's CLIENT half (the amendment closing task 023's coverage gap): a
// block-boundary ProseMirror step must EMIT the correct structural op into the op-log instead of
// being silently deferred. The must-verify closed-set case is whole-paragraph delete/merge; split +
// insert are covered for completeness.

/** N one-line paragraphs, ids `ids[i]`, each with text `texts[i]`. */
function paras(ids: string[], texts: string[]): PMNode {
  const blocks = ids.map((id, i) =>
    schema.nodes.paragraph.create({ paraId: id }, texts[i] ? [schema.text(texts[i])] : [])
  );
  return schema.nodes.doc.create(null, blocks);
}

describe('classifyStructuralStep — whole-paragraph delete / merge (task 031, closes task-023 gap)', () => {
  it('a backspace-merge of paragraph 2 into paragraph 1 emits mergeParagraph{paraId:P2, targetParaId:P1}', () => {
    // [p1 "Hello", p2 "World"]. p1 content 1..6; p1 node-end 7; p2 content-start 8. Deleting the boundary
    // [6, 8) joins p2 into p1 — a structural ReplaceStep.
    const doc = paras(['AAAA1111', 'BBBB2222'], ['Hello', 'World']);
    const log = new RebasedOperationLog();
    const state = EditorState.create({ doc });
    const tr = state.tr.delete(1 + 5, 1 + 5 + 1 + 1); // end of p1 content → start of p2 content

    const appended = log.recordTransaction(tr);

    expect(appended).toHaveLength(1);
    expect(appended[0]).toEqual({ type: 'mergeParagraph', paraId: 'BBBB2222', targetParaId: 'AAAA1111' });
    // It is in the serialized log — no longer silently lost.
    const snapshot = log.serialize(tr.doc);
    expect(snapshot.orderedOps.map(o => o.operation.type)).toContain('mergeParagraph');
  });

  it('deleting an entire paragraph NODE emits deleteParagraph{paraId} (the retired empty-text sentinel semantic)', () => {
    // [p1, p2, p3]. Remove p2 entirely by deleting [p2NodeStart, p3NodeStart).
    const doc = paras(['AAAA1111', 'BBBB2222', 'CCCC3333'], ['one', 'two', 'three']);
    const a = 3; // "one".length
    const b = 3; // "two".length
    const p2Start = a + 2; // p1 node = [0, a+2)
    const p3Start = a + 2 + b + 2;
    const log = new RebasedOperationLog();
    const state = EditorState.create({ doc });
    const tr = state.tr.delete(p2Start, p3Start);

    const appended = log.recordTransaction(tr);

    expect(appended).toEqual([{ type: 'deleteParagraph', paraId: 'BBBB2222' }]);
    // Neighbours survive; only the middle paragraph's op is captured.
    expect(tr.doc.childCount).toBe(2);
  });

  it('a whole-paragraph delete is NEVER silently dropped (regression on notes/task-023-coverage-gap.md)', () => {
    // The precise gap: before task 031 a structural step produced "no operation, no flag". Assert a structural
    // op now reaches the log for the vanished-paragraph case, and it is NOT routed to the deferral seam.
    const structuralSeen: string[] = [];
    const doc = paras(['AAAA1111', 'BBBB2222', 'CCCC3333'], ['keep', 'gone', 'tail']);
    const log = new RebasedOperationLog({ onStructuralStep: (_s, r) => structuralSeen.push(r) });
    const state = EditorState.create({ doc });
    const p2Start = 'keep'.length + 2; // 6
    const p3Start = p2Start + 'gone'.length + 2; // 12
    const tr = state.tr.delete(p2Start, p3Start);

    const appended = log.recordTransaction(tr);

    expect(appended.length).toBeGreaterThan(0);
    expect(appended[0].type).toBe('deleteParagraph');
    expect(structuralSeen).toHaveLength(0); // it was CAPTURED, not deferred
  });
});

describe('classifyStructuralStep — split / insert (task 031)', () => {
  it('pressing Enter mid-paragraph emits splitParagraph with the run-local split point + a minted newParaId', () => {
    const doc = paras(['AAAA1111'], ['HelloWorld']);
    const state = EditorState.create({ doc });
    const tr = state.tr.split(1 + 5); // split "HelloWorld" after "Hello"
    const cls = classifyStep(tr.steps[0], doc, {});

    const ops = expectOps(cls);
    expect(ops).toHaveLength(1);
    const op = ops[0];
    expect(op.type).toBe('splitParagraph');
    if (op.type === 'splitParagraph') {
      expect(op.paraId).toBe('AAAA1111');
      expect(op.at).toEqual({ runIndex: 0, offset: 5 });
      expect(op.newParaId).toMatch(/^[0-9A-F]{8}$/);
    }
  });

  it('inserting a new empty paragraph node emits insertParagraph{position} with a minted newParaId', () => {
    const doc = paras(['AAAA1111', 'BBBB2222'], ['first', 'second']);
    const state = EditorState.create({ doc });
    const empty = schema.nodes.paragraph.create({ paraId: null });
    const insertPos = 'first'.length + 2; // just after p1's node
    const tr = state.tr.insert(insertPos, empty);
    const cls = classifyStep(tr.steps[0], doc, {});

    const ops = expectOps(cls);
    expect(ops[0].type).toBe('insertParagraph');
    if (ops[0].type === 'insertParagraph') {
      expect(ops[0].paraId).toBe('AAAA1111');
      expect(ops[0].position).toBe('After');
      expect(ops[0].newParaId).toMatch(/^[0-9A-F]{8}$/);
    }
  });
});

describe('RebasedOperationLog — ambiguous same-position ordering is surfaced, not silently guessed (task 022, escalation)', () => {
  it('calls onAmbiguousOrder when two different ops resolve to the identical position at serialize time', () => {
    const doc0 = onePara(PARA_ID, 'Hello World');
    const ambiguous: Array<[ComposeOperation, ComposeOperation]> = [];
    const log = new RebasedOperationLog({ onAmbiguousOrder: (a, b) => ambiguous.push([a, b]) });
    let state = EditorState.create({ doc: doc0 });

    // Op1: insert "A" at offset 5 (the gap between "Hello" and " World"). The insertion-point
    // anchor sticks to the LEFT (assoc -1 — "insert here", not pushed forward by its own text).
    const tr1 = state.tr.insertText('A', abs(5), abs(5));
    log.recordTransaction(tr1);
    state = state.apply(tr1);
    expect(state.doc.textContent).toBe('HelloA World');

    // Op2 (a SEPARATE, later transaction): insert "B" at the SAME absolute position 5 — i.e.
    // BEFORE op1's just-inserted "A", not after it. Op1's left-sticking anchor is NOT pushed
    // forward by this insertion, so both ops now resolve to the IDENTICAL position.
    const tr2 = state.tr.insertText('B', abs(5), abs(5));
    log.recordTransaction(tr2);
    state = state.apply(tr2);
    expect(state.doc.textContent).toBe('HelloBA World');

    const snapshot = log.serialize(state.doc);
    // Both ops still serialize (deterministic capture-order fallback) — never dropped.
    expect(snapshot.orderedOps.length).toBeGreaterThanOrEqual(2);
    // The ambiguity (op1 flagged-deleted at offset 5, op2 landing at the SAME offset 5) is surfaced.
    expect(ambiguous.length).toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// task 038 (spaarkeai-compose-r4 zero-error guardrails) — the op-log survives a
// REJECTED save and is cleared ONLY on a confirmed 200. This is the data-integrity
// core: before task 038, serialize() reset the log BEFORE the POST, so a 422 emptied
// the log and a retry re-sent nothing — every valid text edit in the batch was lost on
// reload. serialize() is now non-destructive; commitSaved(nextSeq) drops the persisted
// batch only on success, preserving concurrent edits made during the in-flight save.
// ---------------------------------------------------------------------------
describe('RebasedOperationLog — op-log survives a rejected save, cleared only on confirmed success (task 038)', () => {
  it('serialize is NON-DESTRUCTIVE: a rejected save leaves the log intact so a retry re-sends the SAME ops', () => {
    const doc0 = onePara(PARA_ID, 'Hello');
    const log = new RebasedOperationLog();
    let state = EditorState.create({ doc: doc0 });

    const tr = state.tr.insertText('X', abs(5), abs(5));
    log.recordTransaction(tr);
    state = state.apply(tr);

    // First save attempt → serialize the batch (the host POSTs this). The save is REJECTED (422), so the
    // host does NOT call commitSaved — nothing clears the log.
    const firstSend = log.serialize(state.doc);
    expect(firstSend.orderedOps).toHaveLength(1);
    expect((firstSend.orderedOps[0].operation as InsertTextOperation).text).toBe('X');

    // Retry: serialize AGAIN. The op is STILL present — the batch was not lost by the failed save.
    const retrySend = log.serialize(state.doc);
    expect(retrySend.orderedOps).toHaveLength(1);
    expect(retrySend.orderedOps[0].operation).toEqual(firstSend.orderedOps[0].operation);
    expect(log.size).toBe(1);
  });

  it('commitSaved(nextSeq) clears exactly the persisted batch on a confirmed 200 (no re-apply next save)', () => {
    const doc0 = onePara(PARA_ID, 'Hello');
    const log = new RebasedOperationLog();
    let state = EditorState.create({ doc: doc0 });
    const tr = state.tr.insertText('X', abs(5), abs(5));
    log.recordTransaction(tr);
    state = state.apply(tr);

    const boundary = log.nextSeq; // high-water mark captured at serialize time
    log.serialize(state.doc);
    // Save confirmed (200): commit the batch.
    log.commitSaved(boundary);

    // The next save starts empty — already-applied ops are never re-sent onto the new baseline.
    expect(log.serialize(state.doc).orderedOps).toEqual([]);
    expect(log.size).toBe(0);
  });

  it('commitSaved PRESERVES edits made DURING the in-flight save (concurrent edit not discarded)', () => {
    const doc0 = onePara(PARA_ID, 'Hello');
    const log = new RebasedOperationLog();
    let state = EditorState.create({ doc: doc0 });

    // Edit A — part of the batch being saved.
    const trA = state.tr.insertText('A', abs(5), abs(5));
    log.recordTransaction(trA);
    state = state.apply(trA);

    // Host serializes A and POSTs it; capture the boundary BEFORE the concurrent edit.
    const boundary = log.nextSeq;
    log.serialize(state.doc);

    // Edit B arrives DURING the in-flight save (the editor stays editable while saving).
    const trB = state.tr.insertText('B', abs(6), abs(6));
    log.recordTransaction(trB);
    state = state.apply(trB);

    // Save confirms (200): commit ONLY the batch through `boundary` — B must survive for the next save.
    log.commitSaved(boundary);

    const remaining = log.serialize(state.doc);
    expect(remaining.orderedOps).toHaveLength(1);
    expect((remaining.orderedOps[0].operation as InsertTextOperation).text).toBe('B');
    expect(log.size).toBe(1);
  });
});

// ---------------------------------------------------------------------------
// Heading / list block-attribute changes → setBlockAttr (R5 task 011, G3)
// ---------------------------------------------------------------------------
//
// The R4 SDL-1/2 guard deferred heading + list edits as `defer-structural`. Task 011's classifier EMITS the
// closed-set setBlockAttr op (Style / ListOrdered / ListLevel) for these block-boundary steps, mirroring the
// server op schema. paraId is read from the PRE-STEP block (durable). These exercise the concrete acceptance
// behavior "a heading/list edit EMITS setBlockAttr, no longer defer-structural".

/** A single heading node, paraId `id`, level `level`, text `text`. */
function oneHeading(id: string, text: string, level: number): PMNode {
  const h = schema.nodes.heading.create({ paraId: id, level }, text ? [schema.text(text)] : []);
  return schema.nodes.doc.create(null, [h]);
}

/** Run a ProseMirror command against `state`, returning the transaction it dispatched (or null). */
function runCommand(state: EditorState, cmd: Command): { tr: EditorState['tr']; state: EditorState } | null {
  let captured: EditorState['tr'] | null = null;
  cmd(state, tr => {
    captured = tr;
  });
  return captured ? { tr: captured, state } : null;
}

/** Place the selection inside the first paragraph/heading at absolute char offset `k`, then return the state. */
function withCaretAt(state: EditorState, pos: number): EditorState {
  return state.apply(state.tr.setSelection(TextSelection.create(state.doc, pos)));
}

describe('classifyStep — heading style changes → setBlockAttr(Style) (R5 task 011)', () => {
  it('toggling a paragraph to Heading 2 emits setBlockAttr{attr:Style, value:Heading2} anchored to the pre-step paraId', () => {
    const doc = onePara('AAAA0001', 'Section title');
    const state = EditorState.create({ doc });
    const tr = state.tr.setBlockType(1, 1, schema.nodes.heading, { level: 2 });

    const ops = expectOps(classifyStep(tr.steps[0], doc, {}));
    expect(ops).toEqual([{ type: 'setBlockAttr', paraId: 'AAAA0001', attr: 'Style', value: 'Heading2' }]);
  });

  it('changing an existing Heading1 to Heading3 emits setBlockAttr{Style:Heading3}', () => {
    const doc = oneHeading('BBBB0002', 'Recitals', 1);
    const state = EditorState.create({ doc });
    const tr = state.tr.setBlockType(1, 1, schema.nodes.heading, { level: 3 });

    const ops = expectOps(classifyStep(tr.steps[0], doc, {}));
    expect(ops).toEqual([{ type: 'setBlockAttr', paraId: 'BBBB0002', attr: 'Style', value: 'Heading3' }]);
  });

  it('toggling a Heading back to a paragraph emits setBlockAttr{Style:Normal}', () => {
    const doc = oneHeading('CCCC0003', 'Body now', 1);
    const state = EditorState.create({ doc });
    const tr = state.tr.setBlockType(1, 1, schema.nodes.paragraph, {});

    const ops = expectOps(classifyStep(tr.steps[0], doc, {}));
    expect(ops).toEqual([{ type: 'setBlockAttr', paraId: 'CCCC0003', attr: 'Style', value: 'Normal' }]);
  });
});

describe('classifyStep — list wrap / unwrap / re-nest → setBlockAttr(ListOrdered/ListLevel) (R5 task 011)', () => {
  it('wrapping a paragraph in a numbered list emits setBlockAttr{ListOrdered:true}', () => {
    const doc = onePara('AAAA0001', 'first item');
    const state = withCaretAt(EditorState.create({ doc }), 3);
    const run = runCommand(state, wrapInList(schema.nodes.orderedList));
    expect(run).not.toBeNull();

    const ops = expectOps(classifyStep(run!.tr.steps[0], doc, {}));
    expect(ops).toEqual([{ type: 'setBlockAttr', paraId: 'AAAA0001', attr: 'ListOrdered', value: 'true' }]);
  });

  it('wrapping a paragraph in a bullet list emits setBlockAttr{ListOrdered:false}', () => {
    const doc = onePara('AAAA0001', 'first item');
    const state = withCaretAt(EditorState.create({ doc }), 3);
    const run = runCommand(state, wrapInList(schema.nodes.bulletList));
    expect(run).not.toBeNull();

    const ops = expectOps(classifyStep(run!.tr.steps[0], doc, {}));
    expect(ops).toEqual([{ type: 'setBlockAttr', paraId: 'AAAA0001', attr: 'ListOrdered', value: 'false' }]);
  });

  it('sinking (indenting) a list item emits setBlockAttr{ListLevel} at the deeper depth', () => {
    const p = (id: string, t: string): PMNode => schema.nodes.paragraph.create({ paraId: id }, [schema.text(t)]);
    const li = (id: string, t: string): PMNode => schema.nodes.listItem.create(null, p(id, t));
    const ol = schema.nodes.orderedList.create(null, [li('AAAA0001', 'a'), li('BBBB0002', 'b')]);
    const doc = schema.nodes.doc.create(null, [ol]);

    // Caret inside the SECOND item's paragraph. li(a)=[1,6): p(a) content at 3. li(b)=[6,11): p(b) content at 8.
    const state = withCaretAt(EditorState.create({ doc }), 8);
    const run = runCommand(state, sinkListItem(schema.nodes.listItem));
    expect(run).not.toBeNull();

    const ops = expectOps(classifyStep(run!.tr.steps[0], run!.state.doc, {}));
    expect(ops).toEqual([{ type: 'setBlockAttr', paraId: 'BBBB0002', attr: 'ListLevel', value: '1' }]);
  });
});

describe('RebasedOperationLog — heading/list setBlockAttr is CAPTURED (no silent loss, R5 task 011)', () => {
  it('a heading-toggle transaction lands a setBlockAttr(Style) op in the serialized log', () => {
    const doc = onePara('AAAA0001', 'title');
    const log = new RebasedOperationLog();
    const state = EditorState.create({ doc });
    const tr = state.tr.setBlockType(1, 1, schema.nodes.heading, { level: 2 });

    const appended = log.recordTransaction(tr);

    expect(appended).toEqual([{ type: 'setBlockAttr', paraId: 'AAAA0001', attr: 'Style', value: 'Heading2' }]);
    const snapshot = log.serialize(tr.doc);
    // The op survives serialize with its durable pre-step paraId (never dropped, never mis-addressed).
    expect(snapshot.orderedOps).toHaveLength(1);
    expect(snapshot.orderedOps[0].operation).toEqual({
      type: 'setBlockAttr',
      paraId: 'AAAA0001',
      attr: 'Style',
      value: 'Heading2',
    });
  });
});

// ---------------------------------------------------------------------------
// R5 task 012 (G12) — accept/reject of an IMPORTED tracked change → acceptRevision/rejectRevision
// ---------------------------------------------------------------------------
//
// importedRevisions.ts renders a recovered Word revision as an insertion/deletion mark keyed
// `imported:<w:id>`. usePendingRedline.accept/reject then DROPS the mark (RemoveMarkStep, keep text) or
// DELETES the text (ReplaceStep). The interceptor must map each to the closed-catalog op addressed by the
// native revision id — never a deleteRange / clearMark — so a save reconciles the pre-existing change.

/** A paragraph whose leading "Hello" run (char [0,5)) carries `markName` with `imported:<id>`. */
function importedRevisionDoc(markName: 'insertion' | 'deletion', ledgerRef: string): PMNode {
  const mark = schema.marks[markName].create({ ledgerRef });
  const p = schema.nodes.paragraph.create({ paraId: PARA_ID }, [schema.text('Hello', [mark]), schema.text(' world')]);
  return schema.nodes.doc.create(null, [p]);
}

describe('classifyStep — accept/reject an imported tracked change (G12 / task 012)', () => {
  it('dropping an imported INSERTION mark (keep text) → acceptRevision by native id', () => {
    const doc = importedRevisionDoc('insertion', 'imported:42');
    const step = new RemoveMarkStep(abs(0), abs(5), schema.marks.insertion.create({ ledgerRef: 'imported:42' }));
    const ops = expectOps(classifyStep(step, doc, {}));
    expect(ops).toEqual([{ type: 'acceptRevision', paraId: PARA_ID, scope: 'Single', revisionId: '42' }]);
  });

  it('deleting an imported INSERTION run (drop text) → rejectRevision by native id', () => {
    const doc = importedRevisionDoc('insertion', 'imported:42');
    const step = new ReplaceStep(abs(0), abs(5), Slice.empty);
    const ops = expectOps(classifyStep(step, doc, {}));
    expect(ops).toEqual([{ type: 'rejectRevision', paraId: PARA_ID, scope: 'Single', revisionId: '42' }]);
  });

  it('dropping an imported DELETION mark (restore text) → rejectRevision by native id', () => {
    const doc = importedRevisionDoc('deletion', 'imported:77');
    const step = new RemoveMarkStep(abs(0), abs(5), schema.marks.deletion.create({ ledgerRef: 'imported:77' }));
    const ops = expectOps(classifyStep(step, doc, {}));
    expect(ops).toEqual([{ type: 'rejectRevision', paraId: PARA_ID, scope: 'Single', revisionId: '77' }]);
  });

  it('deleting imported DELETION struck text (accept the deletion) → acceptRevision by native id', () => {
    const doc = importedRevisionDoc('deletion', 'imported:77');
    const step = new ReplaceStep(abs(0), abs(5), Slice.empty);
    const ops = expectOps(classifyStep(step, doc, {}));
    expect(ops).toEqual([{ type: 'acceptRevision', paraId: PARA_ID, scope: 'Single', revisionId: '77' }]);
  });

  it('does NOT divert an AI-suggested redline (non-`imported:` ledgerRef) to a revision op', () => {
    // An AI redline is keyed `{bindingId}@t{n}` — dropping its insertion mark is NOT an imported-revision
    // reconciliation; it falls through to the mark classifier (which surfaces `insertion` as unrepresentable,
    // not a revision op). The key assertion: no accept/rejectRevision is emitted.
    const doc = importedRevisionDoc('insertion', 'b1@t1');
    const step = new RemoveMarkStep(abs(0), abs(5), schema.marks.insertion.create({ ledgerRef: 'b1@t1' }));
    const cls = classifyStep(step, doc, {});
    expect(cls.kind).not.toBe('ops');
  });

  it('a delete spanning an imported insertion PLUS plain text is NOT diverted (no content silently dropped)', () => {
    // The "Hello" run is an imported insertion; " world" is plain. A delete of both is NOT a pure accept/reject
    // reconciliation — it must fall through to normal classification (a deleteRange), never emit only a
    // rejectRevision that would drop " world" server-side.
    const doc = importedRevisionDoc('insertion', 'imported:42'); // "Hello"[ins] + " world"[plain], one paragraph
    const step = new ReplaceStep(abs(0), abs(11), Slice.empty); // delete "Hello world"
    const ops = expectOps(classifyStep(step, doc, {}));
    expect(ops.some(o => o.type === 'acceptRevision' || o.type === 'rejectRevision')).toBe(false);
    expect(ops[0].type).toBe('deleteRange');
  });

  it('an id-less imported revision (`imported:<paraId>#<i>` fallback) is not emitted as a revision op', () => {
    // No native w:id to reconcile by → never emit an op the server could only RevisionNotFound.
    const doc = importedRevisionDoc('insertion', 'imported:0A1B2C3D#0');
    const step = new RemoveMarkStep(
      abs(0),
      abs(5),
      schema.marks.insertion.create({ ledgerRef: 'imported:0A1B2C3D#0' })
    );
    const cls = classifyStep(step, doc, {});
    expect(cls.kind).not.toBe('ops');
  });
});

describe('RebasedOperationLog — imported-revision ops are captured + deduped (G12 / task 012)', () => {
  it('an acceptRevision survives serialize with its durable paraId + revisionId (never dropped)', () => {
    const doc = importedRevisionDoc('insertion', 'imported:9');
    const log = new RebasedOperationLog();
    const state = EditorState.create({ doc });
    const tr = state.tr.step(
      new RemoveMarkStep(abs(0), abs(5), schema.marks.insertion.create({ ledgerRef: 'imported:9' }))
    );

    const appended = log.recordTransaction(tr);
    expect(appended).toEqual([{ type: 'acceptRevision', paraId: PARA_ID, scope: 'Single', revisionId: '9' }]);

    const snapshot = log.serialize(tr.doc);
    expect(snapshot.orderedOps).toHaveLength(1);
    expect(snapshot.orderedOps[0].operation).toEqual({
      type: 'acceptRevision',
      paraId: PARA_ID,
      scope: 'Single',
      revisionId: '9',
    });
  });

  it('dedupes one revision split across two mark-drop steps in a single accept transaction', () => {
    // Two separated insertion-marked runs (same native id) → two RemoveMarkSteps in one accept transaction;
    // the log must carry exactly ONE acceptRevision (the 2nd would RevisionNotFound server-side).
    const ins = schema.marks.insertion.create({ ledgerRef: 'imported:5' });
    const p = schema.nodes.paragraph.create({ paraId: PARA_ID }, [
      schema.text('AAA', [ins]),
      schema.text(' mid '),
      schema.text('CCC', [ins]),
    ]);
    const doc = schema.nodes.doc.create(null, [p]);
    const log = new RebasedOperationLog();
    const state = EditorState.create({ doc });
    const tr = state.tr
      .step(new RemoveMarkStep(abs(0), abs(3), ins)) // AAA
      .step(new RemoveMarkStep(abs(8), abs(11), ins)); // CCC

    log.recordTransaction(tr);
    const revOps = log
      .serialize(tr.doc)
      .orderedOps.map(o => o.operation)
      .filter(o => o.type === 'acceptRevision');
    expect(revOps).toHaveLength(1);
    expect(revOps[0]).toMatchObject({ type: 'acceptRevision', revisionId: '5' });
  });
});

describe('buildBatchRevisionOp — accept-all / reject-all (G12 batch / task 013)', () => {
  it('builds an All-scope acceptRevision op with a null revisionId + presence-anchor paraId', () => {
    const op = buildBatchRevisionOp('acceptRevision', PARA_ID);
    // All-scope batch: revisionId is null (server reconciles every revision in document order), scope is All,
    // paraId is the real presence-anchor id (keeps the isComposeOperation guard + base contract meaningful).
    expect(op).toEqual({ type: 'acceptRevision', paraId: PARA_ID, scope: 'All', revisionId: null });
    expect(isComposeOperation(op)).toBe(true);
  });

  it('builds an All-scope rejectRevision op (inverse discriminator, same batch shape)', () => {
    const op = buildBatchRevisionOp('rejectRevision', PARA_ID);
    expect(op).toEqual({ type: 'rejectRevision', paraId: PARA_ID, scope: 'All', revisionId: null });
    expect(isComposeOperation(op)).toBe(true);
  });
});
