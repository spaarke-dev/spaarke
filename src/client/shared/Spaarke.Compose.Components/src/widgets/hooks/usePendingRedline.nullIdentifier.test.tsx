/**
 * usePendingRedline.nullIdentifier.test.tsx — FR-C05/C06 residual (spaarkeai-compose-r8 task 053b).
 *
 * THE DEFECT. Azure OpenAI Structured Outputs requires the `target_para_id` KEY to be present, so "I
 * could not identify the paragraph" arrives as an explicit `null`. Task 052 removed `target_text` from
 * the edit channel, so such an edit has no anchor AND no prose: it failed task 053's
 * `classifyAnchorlessReplay`, fell through to the insertion-at-cursor branch, and returned `applied`.
 * A revised indemnity clause could land in the recitals — wherever the caret happened to be — and the
 * status told the user it succeeded.
 *
 * THE BAR (owner, 2026-08-25, verbatim): *"whatever ensures the document updates and saves — so
 * whatever that takes."* A bare refusal would satisfy the catalog's wording and FAIL that bar: the user
 * asked for a change and would get nothing. So the fix routes the case into task 053's propose-then-
 * confirm machinery, and the tests below are written against that bar rather than against the refusal.
 *
 * WHAT EACH GROUP PROVES, AND WHY IT NEEDS ITS OWN TEST:
 *
 *  1. **The discriminator is key PRESENCE, not truthiness.** `payload?.target_para_id` is falsy for an
 *     absent key AND for an explicit null, and the two mean opposite things. The classifier table pins
 *     every case, including the three that are easy to get wrong: an explicitly-`undefined` value
 *     (TypeScript's spelling of "absent"), an empty string, and a payload with nothing to place.
 *
 *  2. **A null identifier PROPOSES; it never reports `applied` and never silently lands.** Asserted on
 *     the status, on the document, and on the absence of an error banner — this is a question, not a
 *     failure.
 *
 *  3. **A confirmed proposal APPLIES *and SAVES*.** The owner's bar is the document, not the mark, so
 *     the assertion runs the real client save path (`buildImportedContentModel`, the merged model
 *     `ComposeWorkspace` POSTs) and finds the user's text in it — as a tracked insertion while pending,
 *     and as settled text after Accept.
 *
 *  4. **Genuine insertion consumers are untouched.** `compose-draft-document` and Flow-3
 *     `compose_context_insert` send NO target key; they must insert at the caret exactly as before.
 *     This is the thing most likely to break, so it is asserted directly, in the shapes those two
 *     consumers actually send.
 *
 *  5. **Task 053's two structural bounds still hold.** An anchored edit cannot reach either anchorless
 *     leg (proven with the module-boundary tripwire ARMED, so the claim is about the ROUTE taken and
 *     not merely about where the text landed), and no sequence of hook calls places an unconfirmed
 *     proposal.
 *
 * @see ./anchorlessReplayFallback.ts — `classifyUnidentifiedTarget`, the mint under test.
 * @see ./usePendingRedline.anchorlessFallback.test.tsx — task 053's sibling suite (the prose leg).
 * @see projects/spaarkeai-compose-r8/notes/053b-null-identifier-decisions.md
 */
import { renderHook, act } from '@testing-library/react';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { InsertionMark } from '../marks/InsertionMark';
import { DeletionMark } from '../marks/DeletionMark';
import { CommentAnchorMark } from '../marks/CommentAnchorMark';
import { COMPOSE_R3_PARAID } from '../paraIdExtension';
import { collectBlocks } from '../importedRevisions';
import {
  stampParaIds,
  captureParaIdSnapshot,
  buildContentModel,
  buildImportedContentModel,
} from '../../utils/docxBridge';
import { usePendingRedline, collectMarkedRanges } from './usePendingRedline';
import { classifyUnidentifiedTarget } from './anchorlessReplayFallback';
import type { ComposeContentModel, ParaIdMapEntry } from '../../types/compose-contracts';
import type { ComposeDraftPayload } from '../ComposeEditor';

jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

jest.mock('@spaarke/ai-widgets/events', () => ({ useDispatchPaneEvent: () => jest.fn() }));

// ---------------------------------------------------------------------------
// The module-boundary tripwire — same device as usePendingRedline.anchorlessFallback.test.tsx (task
// 053) and usePendingRedline.wholeDocument.test.tsx (task 055). ARMED, any text search throws. It is
// what makes "the unidentified leg does not search" a statement about the ROUTE rather than about the
// outcome: a document assertion alone would pass vacuously whenever a search happened to find the
// right paragraph.
// ---------------------------------------------------------------------------
let mockTripwireArmed = false;
const mockTextSearchTargets: string[] = [];

jest.mock('./redlineTextSearch', () => {
  const actual = jest.requireActual('./redlineTextSearch');
  return {
    ...actual,
    resolveTargetSpans: (...args: unknown[]) => {
      const target = String(args[1] ?? '');
      mockTextSearchTargets.push(target);
      if (mockTripwireArmed) {
        throw new Error(`Text search was invoked for a target ("${target}") that must never be searched for.`);
      }
      return (actual.resolveTargetSpans as (...a: unknown[]) => unknown)(...args);
    },
  };
});

const PROV = { ledgerRef: 'b1@t1', bindingId: 'b1', turn: 1 };

const CLAUSE_1 = '1. Recitals. The parties enter into this Agreement as of the date below.';
const CLAUSE_2 = '2. Indemnity. The receiving party shall indemnify the disclosing party.';
const CLAUSE_3 = '3. Term. This Agreement runs for twelve months unless renewed.';

function entry(index: number, paraId: string): ParaIdMapEntry {
  return { index, paraId, isMinted: false };
}

function makeDoc(): { editor: Editor; referenceMap: ParaIdMapEntry[] } {
  const editor = new Editor({
    extensions: [StarterKit, InsertionMark, DeletionMark, CommentAnchorMark, ...COMPOSE_R3_PARAID],
    content: `<p>${CLAUSE_1}</p><p>${CLAUSE_2}</p><p>${CLAUSE_3}</p>`,
  });
  const referenceMap = [entry(0, 'AAAA0001'), entry(1, 'AAAA0002'), entry(2, 'AAAA0003')];
  stampParaIds(editor, referenceMap);
  return { editor, referenceMap };
}

/** The server-retained content model that mounted this document — the save path's merge base. */
function loadedModelFor(): ComposeContentModel {
  return {
    blocks: [
      { kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: CLAUSE_1 }] },
      { kind: 'Paragraph', paraId: 'AAAA0002', runs: [{ text: CLAUSE_2 }] },
      { kind: 'Paragraph', paraId: 'AAAA0003', runs: [{ text: CLAUSE_3 }] },
    ],
    comments: [],
  };
}

/** Every insertion/deletion mark in the document, regardless of which ledger key owns it. */
function markCount(editor: Editor): number {
  let n = 0;
  editor.state.doc.descendants(node => {
    if (node.isText && node.marks.some(m => m.type.name === 'insertion' || m.type.name === 'deletion')) n += 1;
    return true;
  });
  return n;
}

/** Select the live span of a stamped paragraph — the user highlighting a clause before asking for an edit. */
function selectParagraph(editor: Editor, paraId: string): void {
  const block = collectBlocks(editor).find(b => b.paraId === paraId);
  if (!block) throw new Error(`fixture error: no block ${paraId}`);
  editor.commands.setTextSelection({ from: block.from + 1, to: block.to - 1 });
}

/** Put a bare caret inside a stamped paragraph (no selection) — the ledger-replay shape. */
function caretInParagraph(editor: Editor, paraId: string): void {
  const block = collectBlocks(editor).find(b => b.paraId === paraId);
  if (!block) throw new Error(`fixture error: no block ${paraId}`);
  editor.commands.setTextSelection(block.from + 1);
}

/** The concatenated text of a content model — what the server would render into the .docx. */
function modelText(model: ComposeContentModel): string {
  return model.blocks
    .map(b => ('runs' in b && Array.isArray(b.runs) ? b.runs.map(r => r.text ?? '').join('') : ''))
    .join('\n');
}

/**
 * The text the SAVED document reads once its tracked changes are accepted — every run except the
 * `Deleted` ones. This is the right lens for an accepted edit: R6's render-on-save diffs the editor
 * against the load-time baseline and INTERLEAVES Deleted and Inserted runs, so the raw run
 * concatenation legitimately reads as both versions at once.
 */
function settledText(model: ComposeContentModel): string {
  return model.blocks
    .map(b =>
      'runs' in b && Array.isArray(b.runs)
        ? b.runs
            .filter(r => r.revision?.kind !== 'Deleted')
            .map(r => r.text ?? '')
            .join('')
        : ''
    )
    .join('\n');
}

/** Every run in the model carrying an `Inserted` tracked-change fact. */
function insertedRuns(model: ComposeContentModel): string[] {
  const out: string[] = [];
  for (const block of model.blocks) {
    if (!('runs' in block) || !Array.isArray(block.runs)) continue;
    for (const run of block.runs) {
      if (run.revision?.kind === 'Inserted') out.push(run.text ?? '');
    }
  }
  return out;
}

beforeEach(() => {
  mockTripwireArmed = false;
  mockTextSearchTargets.length = 0;
});

// ---------------------------------------------------------------------------
// 1 — THE DISCRIMINATOR. Key presence, not truthiness.
// ---------------------------------------------------------------------------
describe('classifyUnidentifiedTarget — the only mint, and the whole presence-vs-truthiness question', () => {
  it('mints for the wire shape the defect is about: the key present, the value null', () => {
    const minted = classifyUnidentifiedTarget({ target_para_id: null, new_text: 'Revised indemnity language.' });
    expect(minted).not.toBeNull();
    expect(minted?.declinedKeys).toEqual(['target_para_id']);
    expect(minted?.proposedText).toBe('Revised indemnity language.');
  });

  it('does NOT mint when the key is ABSENT — that is a genuine insertion consumer, not a failed edit', () => {
    expect(classifyUnidentifiedTarget({ new_text: 'A drafted paragraph.' })).toBeNull();
  });

  it('does NOT mint for an explicitly-undefined value — TypeScript spelling of "absent", which JSON cannot produce', () => {
    expect(classifyUnidentifiedTarget({ target_para_id: undefined, new_text: 'X' })).toBeNull();
  });

  it('mints for an empty / whitespace-only identifier: asked for an address, given something that is not one', () => {
    expect(classifyUnidentifiedTarget({ target_para_id: '', new_text: 'X' })).not.toBeNull();
    expect(classifyUnidentifiedTarget({ target_para_id: '   ', new_text: 'X' })).not.toBeNull();
  });

  it('does NOT mint when a USABLE anchor is present — BOUND 1, unchanged for this leg', () => {
    expect(classifyUnidentifiedTarget({ target_para_id: 'AAAA0002', new_text: 'X' })).toBeNull();
    expect(classifyUnidentifiedTarget({ target_para_id: null, target_ref: 'clause 2', new_text: 'X' })).toBeNull();
  });

  it('does NOT mint when the payload carries replayable prose — that is task 053s leg, which has more to show', () => {
    expect(
      classifyUnidentifiedTarget({ target_para_id: null, target_text: 'shall indemnify', new_text: 'X' })
    ).toBeNull();
  });

  it('does NOT mint with nothing to place — an EMPTY superseding entry is the FR-17 RETRACTION, not a question', () => {
    expect(classifyUnidentifiedTarget({ target_para_id: null, new_text: '' })).toBeNull();
    expect(classifyUnidentifiedTarget({ target_para_id: null })).toBeNull();
  });

  it('tolerates junk without throwing (a durable ledger replay is not a trusted shape)', () => {
    expect(classifyUnidentifiedTarget(null)).toBeNull();
    expect(classifyUnidentifiedTarget(undefined)).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// 2 — PROPOSES, NEVER `applied`, NEVER a silent caret insertion.
// ---------------------------------------------------------------------------
describe('usePendingRedline — a null identifier is proposed, not placed', () => {
  it('with a selection: PROPOSES over the passage the user selected and places NOTHING', () => {
    const { editor, referenceMap } = makeDoc();
    selectParagraph(editor, 'AAAA0002');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize(
        { target_para_id: null, new_text: 'The parties shall indemnify each other mutually.' },
        PROV
      );
    });

    // The defect, inverted: this used to be 'applied'.
    expect(status).toBe('proposed');
    expect(status).not.toBe('applied');
    expect(markCount(editor)).toBe(0);
    expect(editor.state.doc.textContent).not.toContain('mutually');
    expect(result.current.pending).toHaveLength(0);
    // A question, not a failure — no error banner.
    expect(result.current.error).toBeNull();
    expect(result.current.legacyProposal).toMatchObject({
      ledgerRef: 'b1@t1',
      bindingId: 'b1',
      reason: 'unidentified-target',
      placement: 'replace-selection',
      matchedText: CLAUSE_2,
      quotedTarget: '', // nothing was quoted — that IS the problem being reported
      proposedText: 'The parties shall indemnify each other mutually.',
    });
    editor.destroy();
  });

  it('with only a caret: PROPOSES an insertion and names the paragraph it would land in', () => {
    const { editor, referenceMap } = makeDoc();
    caretInParagraph(editor, 'AAAA0001'); // the recitals — where the defect used to dump the clause
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_para_id: null, new_text: 'Revised indemnity language.' }, PROV);
    });

    expect(status).toBe('proposed');
    expect(markCount(editor)).toBe(0);
    expect(result.current.legacyProposal).toMatchObject({
      reason: 'unidentified-target',
      placement: 'insert-at-cursor',
      matchedText: '', // nothing would be struck
      contextText: CLAUSE_1, // ...but the user is told exactly where it would go
    });
    editor.destroy();
  });

  it('never reaches the text search — the tripwire stays silent (ADR-049 I-7)', () => {
    const { editor, referenceMap } = makeDoc();
    selectParagraph(editor, 'AAAA0002');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));
    mockTripwireArmed = true;

    act(() => {
      result.current.materialize({ target_para_id: null, new_text: 'Mutual indemnity.' }, PROV);
    });
    act(() => {
      result.current.applyLegacyProposal();
    });

    expect(mockTextSearchTargets).toEqual([]);
    editor.destroy();
  });

  it('dismissing places nothing and leaves the document byte-identical', () => {
    const { editor, referenceMap } = makeDoc();
    selectParagraph(editor, 'AAAA0002');
    const before = editor.getHTML();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materialize({ target_para_id: null, new_text: 'Mutual indemnity.' }, PROV);
    });
    act(() => {
      result.current.dismissLegacyProposal();
    });

    expect(result.current.legacyProposal).toBeNull();
    expect(result.current.pending).toHaveLength(0);
    expect(editor.getHTML()).toBe(before);
    editor.destroy();
  });

  it('NEGATIVE: no sequence of hook calls places it without the confirmation', () => {
    const { editor, referenceMap } = makeDoc();
    selectParagraph(editor, 'AAAA0002');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));
    const payload: ComposeDraftPayload = { target_para_id: null, new_text: 'Mutual indemnity.' };

    act(() => {
      result.current.materialize(payload, PROV);
      result.current.materialize(payload, PROV); // a refresh replay
      result.current.applyStaleTargetAnyway(); // the OTHER question's answer must not release it
      result.current.accept('b1@t1');
      result.current.reject('b1@t1');
    });

    expect(markCount(editor)).toBe(0);
    expect(editor.state.doc.textContent).not.toContain('Mutual indemnity.');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 3 — THE OWNER'S BAR: confirmed ⇒ it APPLIES **and SAVES**.
// ---------------------------------------------------------------------------
describe('a confirmed proposal reaches the SAVED document, not just the editor', () => {
  it('replaces the selected passage, and the change is in the model the save path POSTs', () => {
    const { editor, referenceMap } = makeDoc();
    const loaded = loadedModelFor();
    const snapshot = captureParaIdSnapshot(editor);
    selectParagraph(editor, 'AAAA0002');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materialize(
        { target_para_id: null, new_text: 'The parties shall indemnify each other mutually.' },
        PROV
      );
    });
    act(() => {
      result.current.applyLegacyProposal();
    });

    // (a) it applied, as a normal pending redline — strike + insert, still accept/rejectable
    expect(result.current.legacyProposal).toBeNull();
    expect(result.current.pending).toHaveLength(1);
    expect(collectMarkedRanges(editor, 'deletion', 'b1@t1').length).toBeGreaterThan(0);
    expect(collectMarkedRanges(editor, 'insertion', 'b1@t1').length).toBeGreaterThan(0);
    expect(editor.state.doc.textContent).toContain('indemnify each other mutually');

    // (b) IT SAVES. This is the owner's bar, so the assertion runs the real client save path — the
    //     merged imported model ComposeWorkspace POSTs to /api/compose/documents/{id}/save — and finds
    //     the user's change inside it, carried as a tracked insertion (trackChanges: true).
    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: [],
    });
    expect(modelText(model)).toContain('indemnify each other mutually');
    expect(insertedRuns(model).join('')).toContain('indemnify each other mutually');
    // ...and it did not corrupt the paragraphs nobody touched (project invariant 2).
    expect(modelText(model)).toContain(CLAUSE_1);
    expect(modelText(model)).toContain(CLAUSE_3);

    // (c) after Accept the change is settled text and STILL saves — the redline is not a dead end.
    act(() => {
      result.current.accept('b1@t1');
    });
    const afterAccept = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: [],
    });
    // Read in ACCEPT state: the mapper diffs the edited paragraph against the load-time baseline and
    // interleaves Deleted + Inserted runs (that IS the tracked change being persisted), so the
    // meaningful claim is what the saved document reads once those changes are accepted.
    expect(settledText(afterAccept.model)).toContain('indemnify each other mutually');
    expect(settledText(afterAccept.model)).toContain(CLAUSE_1);
    expect(settledText(afterAccept.model)).toContain(CLAUSE_3);
    expect(editor.state.doc.textContent).not.toContain('The receiving party shall indemnify the disclosing party.');
    editor.destroy();
  });

  it('a confirmed caret insertion also reaches the saved model', () => {
    const { editor, referenceMap } = makeDoc();
    const loaded = loadedModelFor();
    const snapshot = captureParaIdSnapshot(editor);
    caretInParagraph(editor, 'AAAA0003');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_para_id: null, new_text: 'Twenty-four months. ' }, PROV);
    });
    // Held first — without this the save assertion below would pass just as well on the DEFECT, which
    // also put the text at the caret. What changed is that a human said yes.
    expect(status).toBe('proposed');
    expect(markCount(editor)).toBe(0);
    act(() => {
      result.current.applyLegacyProposal();
    });

    expect(result.current.pending).toHaveLength(1);
    // An insertion strikes nothing, so there is no deletion half — same as the pre-053b caret branch.
    expect(result.current.pending[0].hasDeletion).toBe(false);
    expect(collectMarkedRanges(editor, 'deletion', 'b1@t1')).toHaveLength(0);

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: [],
    });
    expect(modelText(model)).toContain('Twenty-four months.');
    expect(insertedRuns(model).join('')).toContain('Twenty-four months.');
    editor.destroy();
  });

  it('a born-in-editor save also carries it once accepted (the other save shape)', () => {
    const { editor, referenceMap } = makeDoc();
    selectParagraph(editor, 'AAAA0002');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_para_id: null, new_text: 'Mutual indemnity applies.' }, PROV);
    });
    expect(status).toBe('proposed');
    expect(markCount(editor)).toBe(0);
    act(() => {
      result.current.applyLegacyProposal();
      result.current.accept('b1@t1');
    });

    // `buildContentModel` is reject-state parity: a PENDING insertion is excluded by design, so this
    // asserts the ACCEPTED text — which is what a born-in-editor save persists.
    expect(modelText(buildContentModel(editor))).toContain('Mutual indemnity applies.');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 4 — NO REGRESSION for the consumers that legitimately have no target.
// ---------------------------------------------------------------------------
describe('genuine insertion consumers are byte-for-byte unchanged', () => {
  it('an absent-key payload inserts at the caret and reports applied (compose-draft-document)', () => {
    const { editor, referenceMap } = makeDoc();
    caretInParagraph(editor, 'AAAA0003');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ new_text: 'A drafted paragraph.' }, PROV);
    });

    expect(status).toBe('applied');
    expect(result.current.legacyProposal).toBeNull();
    expect(result.current.pending).toHaveLength(1);
    expect(editor.state.doc.textContent).toContain('A drafted paragraph.');
    editor.destroy();
  });

  it('the Flow-3 compose_context_insert shape is unchanged (`{ new_text: html }`, no target key)', () => {
    const { editor, referenceMap } = makeDoc();
    caretInParagraph(editor, 'AAAA0001');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      // Exactly what ComposeWorkspace.onContextInsert sends.
      status = result.current.materialize(
        { new_text: '<strong>Precedent clause</strong> from the library.' },
        { ledgerRef: 'context-insert:clause-77', bindingId: 'clause-77', turn: 0 }
      );
    });

    expect(status).toBe('applied');
    expect(result.current.legacyProposal).toBeNull();
    expect(editor.state.doc.textContent).toContain('Precedent clause');
    editor.destroy();
  });

  it('an explicitly-undefined target key still inserts (absent means absent)', () => {
    const { editor, referenceMap } = makeDoc();
    caretInParagraph(editor, 'AAAA0001');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_para_id: undefined, new_text: 'Inserted draft.' }, PROV);
    });

    expect(status).toBe('applied');
    expect(result.current.legacyProposal).toBeNull();
    editor.destroy();
  });

  it('an EMPTY superseding entry with a null identifier is still a RETRACTION, not a question', () => {
    const { editor, referenceMap } = makeDoc();
    caretInParagraph(editor, 'AAAA0001');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    // Place something first so there is a prior redline for the retraction to supersede.
    act(() => {
      result.current.materialize({ new_text: 'First draft.' }, PROV);
    });
    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_para_id: null, new_text: '' }, { ...PROV, ledgerRef: 'b1@t2' });
    });

    expect(status).toBe('retracted');
    expect(result.current.legacyProposal).toBeNull();
    expect(editor.state.doc.textContent).not.toContain('First draft.');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 5 — TASK 053'S BOUNDS, RE-ASSERTED against the new entry.
// ---------------------------------------------------------------------------
describe('task 053 bounds survive the second entry', () => {
  it('an ANCHORED edit still takes the anchored path — it cannot reach either anchorless leg', () => {
    const { editor, referenceMap } = makeDoc();
    caretInParagraph(editor, 'AAAA0001'); // caret deliberately in the WRONG paragraph
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));
    mockTripwireArmed = true;

    let status: string | undefined;
    act(() => {
      status = result.current.materialize(
        { target_para_id: 'AAAA0002', new_text: 'The parties shall indemnify each other mutually.' },
        PROV
      );
    });

    expect(status).toBe('applied');
    expect(result.current.legacyProposal).toBeNull();
    expect(mockTextSearchTargets).toEqual([]);
    // It landed on the paragraph it NAMED, not the one the caret was in.
    const recitals = collectBlocks(editor).find(b => b.paraId === 'AAAA0001');
    expect(recitals?.text).not.toContain('mutually');
    editor.destroy();
  });

  it('an anchored edit whose anchor does NOT resolve is still REFUSED, never proposed at the caret', () => {
    const { editor, referenceMap } = makeDoc();
    selectParagraph(editor, 'AAAA0002');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_para_id: 'DEADBEEF', new_text: 'Nope.' }, PROV);
    });

    expect(status).toBe('target_deleted');
    expect(result.current.legacyProposal).toBeNull();
    expect(markCount(editor)).toBe(0);
    expect(result.current.error).toMatchObject({ kind: 'target_deleted', source: 'anchored' });
    editor.destroy();
  });

  it('a payload with a null identifier AND prose takes task 053s leg, unchanged', () => {
    const { editor, referenceMap } = makeDoc();
    selectParagraph(editor, 'AAAA0001');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize(
        { target_para_id: null, target_text: 'twelve months', new_text: 'twenty-four months' },
        PROV
      );
    });

    expect(status).toBe('proposed');
    expect(result.current.legacyProposal).toMatchObject({
      reason: 'legacy-replay',
      placement: 'matched-span',
      matchedText: 'twelve months',
      quotedTarget: 'twelve months',
    });
    // Confirming places it at the PROSE match (clause 3), not at the selection (clause 1).
    act(() => {
      result.current.applyLegacyProposal();
    });
    const term = collectBlocks(editor).find(b => b.paraId === 'AAAA0003');
    expect(term?.text).toContain('twenty-four months');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 6 — THE BATCHED PASS. Positions must survive the edits placed alongside them.
// ---------------------------------------------------------------------------
describe('materializeMany — a null-identifier edit among anchored ones', () => {
  it('places the anchored edits, PROPOSES the unidentified one, and confirms it at the promised passage', () => {
    const { editor, referenceMap } = makeDoc();
    // The user is on clause 3; the batch also rewrites clause 1, which sits BEFORE it — so every
    // position after clause 1 shifts while the pass runs. Without the transaction remapper the
    // confirmed placement would land mid-clause somewhere the user was never shown.
    selectParagraph(editor, 'AAAA0003');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let statuses: string[] = [];
    act(() => {
      statuses = result.current.materializeMany(
        [
          { target_para_id: 'AAAA0001', new_text: 'REV-1 The parties agree as follows.' },
          { target_para_id: null, new_text: 'REV-2 Term is twenty-four months.' },
        ],
        { ledgerRef: 'rev@t1', bindingId: 'rev', turn: 1 }
      );
    });

    expect(statuses).toEqual(['applied', 'proposed']);
    expect(result.current.legacyProposal).toMatchObject({
      ledgerRef: 'rev@t1',
      reason: 'unidentified-target',
      placement: 'replace-selection',
      matchedText: CLAUSE_3,
      proposedCount: 1,
      totalCount: 2,
    });
    // Nothing of the unidentified edit is in the document yet.
    expect(editor.state.doc.textContent).not.toContain('REV-2');

    act(() => {
      result.current.applyLegacyProposal();
    });

    // It landed on clause 3 — the paragraph the proposal named — despite clause 1 having grown.
    const term = collectBlocks(editor).find(b => b.paraId === 'AAAA0003');
    expect(term?.text).toContain('REV-2 Term is twenty-four months.');
    const recitals = collectBlocks(editor).find(b => b.paraId === 'AAAA0001');
    expect(recitals?.text).not.toContain('REV-2');
    expect(result.current.pending.map(p => p.ledgerRef).sort()).toEqual(['rev@t1#0', 'rev@t1#1']);
    editor.destroy();
  });

  it('a mixed batch replays each held-back edit with the answer IT was held by', () => {
    const { editor, referenceMap } = makeDoc();
    selectParagraph(editor, 'AAAA0002');
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materializeMany(
        [
          { target_para_id: null, target_text: 'twelve months', new_text: 'twenty-four months' }, // 053 leg
          { target_para_id: null, new_text: 'REV-B mutual indemnity.' }, // 053b leg
        ],
        { ledgerRef: 'rev@t1', bindingId: 'rev', turn: 1 }
      );
    });
    expect(result.current.legacyProposal).toMatchObject({ proposedCount: 2, totalCount: 2 });

    act(() => {
      result.current.applyLegacyProposal();
    });

    // BOTH were placed, each by its own route: the prose one at its match (clause 3), the
    // unidentified one at the user's selection (clause 2).
    expect(collectBlocks(editor).find(b => b.paraId === 'AAAA0003')?.text).toContain('twenty-four months');
    expect(collectBlocks(editor).find(b => b.paraId === 'AAAA0002')?.text).toContain('REV-B mutual indemnity.');
    expect(result.current.legacyProposal).toBeNull();
    editor.destroy();
  });
});
