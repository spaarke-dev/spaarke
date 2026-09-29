/**
 * usePendingRedline.deterministicOutcomes.test.tsx — FR-C05's three outcomes (r8 task 052).
 *
 * Retiring text search from the AI edit path means the edge cases it used to absorb now need real
 * answers. This file is the client proof for all three, plus the structural proof that the anchored
 * edit path never reaches the prose-matching collaborator at all.
 *
 *  1. SUB-PARAGRAPH EDIT → the redline covers only the words that changed, diffed LOCALLY inside the
 *     paragraph the anchor already named. Before this task `resolveAnchoredSpans` returned the whole
 *     paragraph content range, so a three-word change struck and replaced a forty-line clause.
 *  2. STALE TARGET → the paraId resolves but the clause is not the text the suggestion was written
 *     against, so NOTHING is placed and "apply anyway?" is raised. Before this task the drift was not
 *     detected at all and `new_text` silently overwrote the user's newer edit.
 *  3. DELETED TARGET → the paragraph the suggestion named is gone; its own `target_deleted` outcome
 *     rather than a bare `not_found` shared with an unresolvable citation.
 *
 * THE TRIPWIRE (step 4). `./redlineTextSearch` is swapped for a throwing double — the client twin of
 * `ThrowIfTextSearched` in `tests/integration/seam/Compose/ComposeEditAnchorPassSeamTests.cs`. Every
 * anchored payload here ALSO carries prose that WOULD have matched, because an anchored edit with no
 * prose takes the insertion-at-cursor branch and never reaches a search regardless — which would make
 * the tripwire's silence meaningless. With prose present, ANY route other than the anchor trips it.
 */
import { renderHook, act } from '@testing-library/react';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { InsertionMark } from '../marks/InsertionMark';
import { DeletionMark } from '../marks/DeletionMark';
import { CommentAnchorMark } from '../marks/CommentAnchorMark';
import { COMPOSE_R3_PARAID } from '../paraIdExtension';
import { collectBlocks } from '../importedRevisions';
import { stampParaIds } from '../../utils/docxBridge';
import { usePendingRedline } from './usePendingRedline';
import { computeLocalEditRange } from './redlineLocalDiff';
import { clearProposalBaselines } from './redlineProposalBaseline';
import type { ParaIdMapEntry } from '../../types/compose-contracts';

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
// The tripwire — `mock`-prefixed module-scope bindings are the only ones jest lets a module factory
// close over. ARMED ⇒ any text search throws, so an anchored edit that fell through fails loudly
// instead of returning something plausible.
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
        throw new Error(
          'Text search was invoked for a target ("' +
            target +
            '") on an edit that carried a deterministic anchor. An anchored edit must resolve through ' +
            'the paraId map and never reach target_text.'
        );
      }
      return (actual.resolveTargetSpans as (...a: unknown[]) => unknown)(...args);
    },
  };
});

const SCOPE = 'session-under-test';
/**
 * Task 052b — every materialize now declares WHERE IT CAME FROM, because the hook's fail-closed
 * default treats an undeclared one as a replay. `PROV` is the LIVE leg: the model produced this
 * proposal against the document as it reads right now, which is the only moment the anchored
 * paragraph may be recorded as the capture-time text. `REPLAY` is a re-materialize from stored
 * ledger state, where an arbitrary amount of editing may sit in between.
 */
const PROV = { ledgerRef: 'b1@t1', bindingId: 'b1', turn: 1, origin: 'live' as const };
const REPLAY = { ...PROV, origin: 'replay' as const };

const CLAUSE_41 =
  'Clause 4.1: The aggregate liability of the Supplier shall not exceed twelve months of fees paid under this Agreement.';
const CLAUSE_42 = 'Clause 4.2: The disclosing party shall indemnify the receiving party for any breach.';

function entry(index: number, paraId: string, computedNumber: string, listPath: number[]): ParaIdMapEntry {
  return { index, paraId, isMinted: false, computedNumber, listPath };
}

function makeDoc(first = CLAUSE_41): { editor: Editor; referenceMap: ParaIdMapEntry[] } {
  const editor = new Editor({
    extensions: [StarterKit, InsertionMark, DeletionMark, CommentAnchorMark, ...COMPOSE_R3_PARAID],
    content: `<p>${first}</p><p>${CLAUSE_42}</p>`,
  });
  const referenceMap = [entry(0, 'STAB0041', '4.1', [4, 1]), entry(1, 'STAB0042', '4.2', [4, 2])];
  stampParaIds(editor, referenceMap);
  return { editor, referenceMap };
}

/** The text a given paraId's live span currently covers. */
function textOf(editor: Editor, paraId: string): string {
  const block = collectBlocks(editor).find(b => b.paraId === paraId);
  return block ? editor.state.doc.textBetween(block.from, block.to, ' ') : '';
}

/** The text carried by the DELETION (struck) half of a redline — i.e. what the edit proposes to remove. */
function struckText(editor: Editor, ledgerRef: string): string {
  let out = '';
  editor.state.doc.descendants(node => {
    if (
      node.isText &&
      typeof node.text === 'string' &&
      node.marks.some(m => m.type.name === 'deletion' && m.attrs.ledgerRef === ledgerRef)
    ) {
      out += node.text;
    }
    return true;
  });
  return out;
}

/** The text carried by the INSERTION half — i.e. what the edit proposes to add. */
function insertedText(editor: Editor, ledgerRef: string): string {
  let out = '';
  editor.state.doc.descendants(node => {
    if (
      node.isText &&
      typeof node.text === 'string' &&
      node.marks.some(m => m.type.name === 'insertion' && m.attrs.ledgerRef === ledgerRef)
    ) {
      out += node.text;
    }
    return true;
  });
  return out;
}

beforeEach(() => {
  mockTripwireArmed = false;
  mockTextSearchTargets.length = 0;
  window.sessionStorage.clear();
  clearProposalBaselines(SCOPE);
});

// ---------------------------------------------------------------------------
// computeLocalEditRange — the pure arithmetic underneath outcome 1
// ---------------------------------------------------------------------------
describe('computeLocalEditRange (FR-C05 outcome 1 — the local diff, as pure string arithmetic)', () => {
  it('isolates a changed phrase and snaps the region to WHOLE WORDS', () => {
    const range = computeLocalEditRange(
      'the cap shall be twelve months of fees',
      'the cap shall be twenty-four months of fees'
    );
    expect(range).not.toBeNull();
    expect('the cap shall be twelve months of fees'.slice(range!.start, range!.endCurrent)).toBe('twelve');
    expect('the cap shall be twenty-four months of fees'.slice(range!.start, range!.endReplacement)).toBe(
      'twenty-four'
    );
  });

  it('returns null for identical texts — there is no changed region to mark', () => {
    expect(computeLocalEditRange('unchanged clause', 'unchanged clause')).toBeNull();
  });

  it('covers the whole string when the replacement shares nothing with it', () => {
    const range = computeLocalEditRange('alpha beta', 'gamma delta');
    expect(range).toEqual({ start: 0, endCurrent: 10, endReplacement: 11 });
  });

  it('never returns an index outside either string (the bound the paragraph scope rests on)', () => {
    const pairs: Array<[string, string]> = [
      ['', 'inserted'],
      ['removed', ''],
      ['a b', 'a X b'],
      ['abc', 'abc def'],
      ['one two three', 'one three'],
    ];
    for (const [current, replacement] of pairs) {
      const r = computeLocalEditRange(current, replacement);
      if (r === null) continue;
      expect(r.start).toBeGreaterThanOrEqual(0);
      expect(r.endCurrent).toBeLessThanOrEqual(current.length);
      expect(r.endReplacement).toBeLessThanOrEqual(replacement.length);
      expect(r.endCurrent).toBeGreaterThanOrEqual(r.start);
      expect(r.endReplacement).toBeGreaterThanOrEqual(r.start);
    }
  });
});

// ---------------------------------------------------------------------------
// OUTCOME 1 — sub-paragraph edit → local diff WITHIN the anchored paragraph
// ---------------------------------------------------------------------------
describe('FR-C05 outcome 1 — a sub-paragraph edit marks only what changed', () => {
  it('strikes ONLY the changed words, not the whole clause', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));

    // A three-word change inside a long clause. Before task 052 the whole 100-character clause was
    // struck and re-inserted.
    act(() => {
      result.current.materialize(
        {
          target_para_id: 'STAB0041',
          new_text: CLAUSE_41.replace('twelve months', 'twenty-four months'),
        },
        PROV
      );
    });

    expect(struckText(editor, 'b1@t1')).toBe('twelve');
    expect(insertedText(editor, 'b1@t1')).toBe('twenty-four');
    // The clause's untouched words were NOT struck — they are still plain text in the paragraph.
    expect(struckText(editor, 'b1@t1')).not.toContain('aggregate liability');
    // And the paragraph still reads correctly around the redline.
    expect(textOf(editor, 'STAB0041')).toContain('The aggregate liability of the Supplier');
    expect(textOf(editor, 'STAB0041')).toContain('twenty-four');
    editor.destroy();
  });

  it('stays INSIDE the anchored paragraph — the neighbouring clause is untouched', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));

    act(() => {
      result.current.materialize(
        { target_para_id: 'STAB0041', new_text: CLAUSE_41.replace('twelve', 'twenty-four') },
        PROV
      );
    });

    expect(textOf(editor, 'STAB0042')).toBe(CLAUSE_42);
    editor.destroy();
  });

  it('accept commits ONLY the local change and leaves the rest of the clause byte-identical', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));

    act(() => {
      result.current.materialize(
        { target_para_id: 'STAB0041', new_text: CLAUSE_41.replace('twelve months', 'twenty-four months') },
        PROV
      );
    });
    act(() => {
      result.current.accept('b1@t1');
    });

    expect(textOf(editor, 'STAB0041')).toBe(CLAUSE_41.replace('twelve months', 'twenty-four months'));
    editor.destroy();
  });

  it('reject restores the clause exactly — the local diff is reversible', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));

    act(() => {
      result.current.materialize(
        { target_para_id: 'STAB0041', new_text: CLAUSE_41.replace('twelve months', 'twenty-four months') },
        PROV
      );
    });
    act(() => {
      result.current.reject('b1@t1');
    });

    expect(textOf(editor, 'STAB0041')).toBe(CLAUSE_41);
    editor.destroy();
  });

  it('a whole-clause rewrite still replaces the whole clause (the diff degrades, it does not break)', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));

    act(() => {
      result.current.materialize(
        { target_para_id: 'STAB0041', new_text: 'Entirely different replacement language.' },
        PROV
      );
    });

    expect(struckText(editor, 'b1@t1')).toBe(CLAUSE_41);
    expect(insertedText(editor, 'b1@t1')).toBe('Entirely different replacement language.');
    editor.destroy();
  });

  it('the local diff does NOT reach the text search (tripwire ARMED, prose present so it COULD)', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));

    mockTripwireArmed = true;
    let status: string | undefined;
    act(() => {
      status = result.current.materialize(
        {
          target_para_id: 'STAB0041',
          // Prose that WOULD have matched. Without it this test would pass vacuously: an anchored
          // payload with no prose takes the insertion-at-cursor branch and never reaches a search.
          target_text: 'twelve months of fees',
          new_text: CLAUSE_41.replace('twelve months', 'twenty-four months'),
        },
        PROV
      );
    });

    expect(status).toBe('applied');
    expect(mockTextSearchTargets).toEqual([]);
    expect(struckText(editor, 'b1@t1')).toBe('twelve');
    editor.destroy();
  });

  it('CONTROL — the tripwire really fires: the same payload WITHOUT an anchor trips it', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));

    mockTripwireArmed = true;
    expect(() => {
      act(() => {
        result.current.materialize({ target_text: 'twelve months of fees', new_text: 'x' }, PROV);
      });
    }).toThrow(/Text search was invoked/);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// OUTCOME 2 — stale target → confirmable, and the resolution is durable
// ---------------------------------------------------------------------------
describe('FR-C05 outcome 2 — a stale target asks instead of overwriting', () => {
  /** Materialize once (recording the proposal baseline), then edit the clause underneath. */
  function proposeThenDrift(): {
    editor: Editor;
    referenceMap: ParaIdMapEntry[];
    payload: { target_para_id: string; new_text: string };
  } {
    const { editor, referenceMap } = makeDoc();
    const payload = {
      target_para_id: 'STAB0041',
      new_text: CLAUSE_41.replace('twelve months', 'twenty-four months'),
    };
    // First materialize on a THROWAWAY hook instance records the baseline for `b1@t1` under SCOPE.
    const first = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    act(() => {
      first.result.current.materialize(payload, PROV);
    });
    // Undo the redline and rewrite the clause — the user's newer edit.
    act(() => {
      first.result.current.reject('b1@t1');
    });
    const block = collectBlocks(editor).find(b => b.paraId === 'STAB0041')!;
    editor
      .chain()
      .setTextSelection({ from: block.from + 1, to: block.to - 1 })
      .insertContent('Clause 4.1: Liability is capped at the fees paid in the preceding six months.')
      .run();
    first.unmount();
    return { editor, referenceMap, payload };
  }

  it('does NOT place the edit and raises "apply anyway?" instead (the silent-overwrite fix)', () => {
    const { editor, referenceMap, payload } = proposeThenDrift();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    const driftedText = textOf(editor, 'STAB0041');

    let status: string | undefined;
    act(() => {
      status = result.current.materialize(payload, REPLAY);
    });

    expect(status).toBe('stale');
    expect(result.current.staleTarget).toMatchObject({
      ledgerRef: 'b1@t1',
      bindingId: 'b1',
      paraId: 'STAB0041',
      staleCount: 1,
      totalCount: 1,
    });
    // Both texts are carried so the confirmation can SHOW the user what changed.
    expect(result.current.staleTarget?.currentText).toBe(driftedText);
    expect(result.current.staleTarget?.proposedAgainst).toBe(CLAUSE_41);
    // NOTHING was placed — the user's newer wording survives untouched.
    expect(textOf(editor, 'STAB0041')).toBe(driftedText);
    expect(editor.getHTML()).not.toContain('data-compose-mark');
    expect(result.current.pending).toHaveLength(0);
    editor.destroy();
  });

  it('"apply anyway" places the edit against the CURRENT clause', () => {
    const { editor, referenceMap, payload } = proposeThenDrift();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));

    act(() => {
      result.current.materialize(payload, REPLAY);
    });
    act(() => {
      result.current.applyStaleTargetAnyway();
    });

    expect(result.current.staleTarget).toBeNull();
    expect(result.current.pending.map(p => p.ledgerRef)).toEqual(['b1@t1']);
    expect(editor.getHTML()).toContain('data-compose-mark="insertion"');
    expect(insertedText(editor, 'b1@t1')).toContain('twenty-four');
    editor.destroy();
  });

  it('"skip this suggestion" places nothing and clears the question', () => {
    const { editor, referenceMap, payload } = proposeThenDrift();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));

    act(() => {
      result.current.materialize(payload, REPLAY);
    });
    const beforeText = textOf(editor, 'STAB0041');
    act(() => {
      result.current.dismissStaleTarget();
    });

    expect(result.current.staleTarget).toBeNull();
    expect(result.current.pending).toHaveLength(0);
    expect(textOf(editor, 'STAB0041')).toBe(beforeText);
    expect(editor.getHTML()).not.toContain('data-compose-mark');
    editor.destroy();
  });

  it('an UNCHANGED clause is never called stale (no friction on the normal path)', () => {
    const { editor, referenceMap } = makeDoc();
    const payload = { target_para_id: 'STAB0041', new_text: CLAUSE_41.replace('twelve', 'twenty-four') };
    const first = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    act(() => {
      first.result.current.materialize(payload, PROV);
    });
    act(() => {
      first.result.current.reject('b1@t1');
    });
    first.unmount();

    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    let status: string | undefined;
    act(() => {
      status = result.current.materialize(payload, REPLAY);
    });

    expect(status).toBe('applied');
    expect(result.current.staleTarget).toBeNull();
    editor.destroy();
  });

  it('O-5 — the resolution is IDEMPOTENT: once the entry is superseded the question cannot return', () => {
    // O-5's acceptance test is "apply anyway → refresh → the prompt does not reappear". The mechanism
    // that makes it true is the host's FR-17 supersession write (ComposeWorkspace.supersedeComposeOutput):
    // the entry stops being the head, so the reopen pass has nothing to re-materialize. A refresh is
    // modelled here as a FRESH hook over the post-refresh document, driven by the ledger state that
    // the supersession produced — a RETRACTION (empty payload) at a higher turn for the same binding.
    const { editor, referenceMap, payload } = proposeThenDrift();
    const first = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    act(() => {
      first.result.current.materialize(payload, PROV);
    });
    expect(first.result.current.staleTarget).not.toBeNull();
    act(() => {
      first.result.current.applyStaleTargetAnyway(); // the user answers
    });
    first.unmount();

    // --- refresh: a brand new hook instance; the ledger head is now the retraction the supersession wrote.
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    let status: string | undefined;
    act(() => {
      status = result.current.materialize({}, { ledgerRef: 'b1@t2', bindingId: 'b1', turn: 2, origin: 'replay' });
    });

    // The retraction re-materializes as a retraction, NOT as a re-ask.
    expect(status).not.toBe('stale');
    expect(result.current.staleTarget).toBeNull();
    editor.destroy();
  });

  it('O-5 (component-local counter-example) — a FRESH hook alone would re-ask; the recorded baseline is what stops it', () => {
    // This is the negative half of the acceptance test: if the answer lived only in `React.useState`
    // (`lastMaterializedKey` is the demonstrated counter-example, assessment §4.3.2), a new hook would
    // raise the question again for the SAME key. It does not, because the answer re-records the
    // proposal baseline — and, durably, because the host superseded the entry.
    const { editor, referenceMap, payload } = proposeThenDrift();
    const first = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    act(() => {
      first.result.current.materialize(payload, PROV);
    });
    act(() => {
      first.result.current.applyStaleTargetAnyway();
    });
    act(() => {
      first.result.current.reject('b1@t1'); // clear the marks so the idempotency guard does not short-circuit
    });
    first.unmount();

    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    let status: string | undefined;
    act(() => {
      status = result.current.materialize(payload, REPLAY);
    });

    expect(status).not.toBe('stale');
    expect(result.current.staleTarget).toBeNull();
    editor.destroy();
  });

  it('with NO proposal scope the check is inert — behaviour is exactly pre-052 (fail-open)', () => {
    const { editor, referenceMap, payload } = proposeThenDrift();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize(payload, REPLAY);
    });

    expect(status).toBe('applied');
    expect(result.current.staleTarget).toBeNull();
    editor.destroy();
  });

  it('a whole-document batch asks ONE batched question and places only the non-stale changes', () => {
    const { editor, referenceMap } = makeDoc();
    const edits = [
      { target_para_id: 'STAB0041', new_text: CLAUSE_41.replace('twelve', 'twenty-four') },
      { target_para_id: 'STAB0042', new_text: CLAUSE_42.replace('any breach', 'any material breach') },
    ];
    const base = { ledgerRef: 'rev@t1', bindingId: 'rev', turn: 1, origin: 'live' as const };

    // Propose, undo, then drift ONLY clause 4.1.
    const first = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    act(() => {
      first.result.current.materializeMany(edits, base);
    });
    act(() => {
      first.result.current.reject('rev@t1');
    });
    const block = collectBlocks(editor).find(b => b.paraId === 'STAB0041')!;
    editor
      .chain()
      .setTextSelection({ from: block.from + 1, to: block.to - 1 })
      .insertContent('Clause 4.1: Liability is capped at six months of fees.')
      .run();
    first.unmount();

    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    let statuses: string[] = [];
    act(() => {
      statuses = result.current.materializeMany(edits, { ...base, origin: 'replay' });
    });

    expect(statuses).toEqual(['stale', 'applied']);
    expect(result.current.staleTarget).toMatchObject({
      ledgerRef: 'rev@t1',
      paraId: 'STAB0041',
      staleCount: 1,
      totalCount: 2,
    });
    // The non-stale change went in; the stale one did not.
    expect(textOf(editor, 'STAB0042')).toContain('material');
    expect(textOf(editor, 'STAB0041')).not.toContain('twenty-four');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// OUTCOME 3 — deleted target gets its own message
// ---------------------------------------------------------------------------
describe('FR-C05 outcome 3 — a deleted target says so', () => {
  it('reports `target_deleted`, distinct from the `not_found` an unresolvable citation gets', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));

    let deleted: string | undefined;
    act(() => {
      deleted = result.current.materialize({ target_para_id: 'DEADBEEF', new_text: 'X' }, PROV);
    });
    expect(deleted).toBe('target_deleted');
    expect(result.current.error).toMatchObject({ kind: 'target_deleted', targetText: 'paragraph DEADBEEF' });

    let unknownCitation: string | undefined;
    act(() => {
      unknownCitation = result.current.materialize(
        { target_ref: 'clause 99.9', new_text: 'X' },
        { ledgerRef: 'b1@t2', bindingId: 'b1', turn: 2 }
      );
    });
    expect(unknownCitation).toBe('not_found');
    editor.destroy();
  });

  it('fires when the paragraph the suggestion named is REALLY deleted from the live document', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));

    // Delete clause 4.1 outright, then replay a suggestion that targeted it.
    const block = collectBlocks(editor).find(b => b.paraId === 'STAB0041')!;
    editor.chain().deleteRange({ from: block.from, to: block.to }).run();

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_para_id: 'STAB0041', new_text: 'X' }, PROV);
    });

    expect(status).toBe('target_deleted');
    expect(result.current.pending).toHaveLength(0);
    editor.destroy();
  });

  it('never falls back to a text search for the deleted paragraph (tripwire ARMED)', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));

    mockTripwireArmed = true;
    let status: string | undefined;
    act(() => {
      status = result.current.materialize(
        {
          target_para_id: 'DEADBEEF',
          // Prose that resolves UNIQUELY. A fallback would place this and report `applied`.
          target_text: 'shall indemnify the receiving party',
          new_text: 'MUST NOT BE PLACED',
        },
        PROV
      );
    });

    expect(status).toBe('target_deleted');
    expect(mockTextSearchTargets).toEqual([]);
    expect(editor.state.doc.textContent).not.toContain('MUST NOT BE PLACED');
    editor.destroy();
  });
});
