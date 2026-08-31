/**
 * usePendingRedline.staleDurability.test.tsx — FR-C05 residual (r8 task 052b): the stale-target
 * QUESTION survives beyond the tab that first rendered the suggestion.
 *
 * WHAT 052 LEFT OPEN. Task 052 made the user's ANSWER ledger-durable (the FR-17 supersession write).
 * The QUESTION needs a second datum — the clause as the model saw it — and 052 kept that in
 * `sessionStorage`. A reopen in a DIFFERENT tab, an eviction past the entry cap, or a
 * storage-disabled environment therefore left the check inert, and "no prompt" IS the pre-052
 * behaviour: the silent overwrite.
 *
 * THE ACCEPTANCE SHAPE. A re-materialize with NO tab-local state at all — that is exactly what a
 * different tab looks like to this code. Every test here drives that state deliberately:
 *   - `wipeAllStorage()`  = a different DEVICE / cleared browser (nothing recorded anywhere);
 *   - `wipeTabStorage()`  = a different TAB / new window (sessionStorage gone, localStorage intact);
 *   - `breakStorage()`    = private browsing / quota / storage disabled (both stores throw).
 *
 * THE TWO NEW GUARANTEES ASSERTED HERE:
 *   1. DURABLE DETECTION — a drifted clause is still detected when only the origin-scoped
 *      fingerprint survives, so the cross-tab case asks instead of overwriting.
 *   2. HONEST UNDETERMINABLE — when detection cannot be established AT ALL, a REPLAYED materialize
 *      asks before it places. It never silently applies, which is the outcome 052b exists to remove.
 *
 * ADR-015: the durable tier stores a one-way fingerprint, never paragraph text. Asserted, not
 * assumed (see "the durable store never holds Tier-3 text").
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
import {
  clearProposalBaselines,
  compareProposalBaseline,
  fingerprintParagraph,
  recordProposalBaseline,
} from './redlineProposalBaseline';
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

// The same tripwire the deterministic-outcomes suite arms: an anchored edit must never reach the
// prose-matching collaborator, and 052's guarantee must survive this task untouched.
let mockTripwireArmed = false;
jest.mock('./redlineTextSearch', () => {
  const actual = jest.requireActual('./redlineTextSearch');
  return {
    ...actual,
    resolveTargetSpans: (...args: unknown[]) => {
      if (mockTripwireArmed) {
        throw new Error(`Text search was invoked for a target ("${String(args[1] ?? '')}") on an anchored edit.`);
      }
      return (actual.resolveTargetSpans as (...a: unknown[]) => unknown)(...args);
    },
  };
});

const SCOPE = 'doc-session-under-test';
const LIVE = { ledgerRef: 'b1@t1', bindingId: 'b1', turn: 1, origin: 'live' as const };
const REPLAY = { ledgerRef: 'b1@t1', bindingId: 'b1', turn: 1, origin: 'replay' as const };
/** Deliberately WITHOUT `origin` — the fail-closed-default proof. */
const UNDECLARED = { ledgerRef: 'b1@t1', bindingId: 'b1', turn: 1 };

const CLAUSE_41 =
  'Clause 4.1: The aggregate liability of the Supplier shall not exceed twelve months of fees paid under this Agreement.';
const CLAUSE_42 = 'Clause 4.2: The disclosing party shall indemnify the receiving party for any breach.';
const USER_REWRITE = 'Clause 4.1: Liability is capped at the fees paid in the preceding six months.';

const PAYLOAD = { target_para_id: 'STAB0041', new_text: CLAUSE_41.replace('twelve months', 'twenty-four months') };

function entry(index: number, paraId: string, computedNumber: string, listPath: number[]): ParaIdMapEntry {
  return { index, paraId, isMinted: false, computedNumber, listPath };
}

function makeDoc(): { editor: Editor; referenceMap: ParaIdMapEntry[] } {
  const editor = new Editor({
    extensions: [StarterKit, InsertionMark, DeletionMark, CommentAnchorMark, ...COMPOSE_R3_PARAID],
    content: `<p>${CLAUSE_41}</p><p>${CLAUSE_42}</p>`,
  });
  const referenceMap = [entry(0, 'STAB0041', '4.1', [4, 1]), entry(1, 'STAB0042', '4.2', [4, 2])];
  stampParaIds(editor, referenceMap);
  return { editor, referenceMap };
}

function textOf(editor: Editor, paraId: string): string {
  const block = collectBlocks(editor).find(b => b.paraId === paraId);
  return block ? editor.state.doc.textBetween(block.from, block.to, ' ') : '';
}

/** Rewrite clause 4.1 with the user's own newer wording — the text a stale apply would destroy. */
function driftClause(editor: Editor): string {
  const block = collectBlocks(editor).find(b => b.paraId === 'STAB0041')!;
  editor
    .chain()
    .setTextSelection({ from: block.from + 1, to: block.to - 1 })
    .insertContent(USER_REWRITE)
    .run();
  return textOf(editor, 'STAB0041');
}

/** A DIFFERENT TAB: sessionStorage is per-tab, localStorage is per-origin. */
function wipeTabStorage(): void {
  window.sessionStorage.clear();
}

/** A DIFFERENT DEVICE / cleared browser: nothing was ever recorded anywhere this code can see. */
function wipeAllStorage(): void {
  window.sessionStorage.clear();
  window.localStorage.clear();
}

/** Private browsing / quota / storage disabled: every access throws. */
function breakStorage(): () => void {
  const descriptors = (['sessionStorage', 'localStorage'] as const).map(name => ({
    name,
    original: Object.getOwnPropertyDescriptor(window, name),
  }));
  for (const { name } of descriptors) {
    Object.defineProperty(window, name, {
      configurable: true,
      get() {
        throw new Error('storage disabled');
      },
    });
  }
  return () => {
    for (const { name, original } of descriptors) {
      if (original) Object.defineProperty(window, name, original);
      else delete (window as unknown as Record<string, unknown>)[name];
    }
  };
}

/** Materialize once with LIVE origin — the model produced this proposal moments ago. */
function proposeLive(editor: Editor, referenceMap: ParaIdMapEntry[]): void {
  const first = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
  act(() => {
    first.result.current.materialize(PAYLOAD, LIVE);
  });
  act(() => {
    first.result.current.reject('b1@t1');
  });
  first.unmount();
}

beforeEach(() => {
  mockTripwireArmed = false;
  window.sessionStorage.clear();
  window.localStorage.clear();
  clearProposalBaselines(SCOPE);
});

// ---------------------------------------------------------------------------
// 1. THE ACCEPTANCE SCENARIO — no tab-local state at all
// ---------------------------------------------------------------------------
describe('052b — a replayed suggestion with NO recorded baseline anywhere', () => {
  it('does NOT place the edit; it asks, with an honest "cannot verify" reason', () => {
    const { editor, referenceMap } = makeDoc();
    wipeAllStorage(); // a different device / cleared browser — nothing was ever recorded
    const before = textOf(editor, 'STAB0041');

    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    let status: string | undefined;
    act(() => {
      status = result.current.materialize(PAYLOAD, REPLAY);
    });

    expect(status).toBe('stale');
    expect(result.current.staleTarget).toMatchObject({
      ledgerRef: 'b1@t1',
      paraId: 'STAB0041',
      reason: 'unverifiable',
      staleCount: 1,
      totalCount: 1,
    });
    // We have no capture-time text, and we do NOT invent one.
    expect(result.current.staleTarget?.proposedAgainst).toBeNull();
    expect(result.current.staleTarget?.currentText).toBe(before);
    // NOTHING was placed — the pre-052 silent overwrite is gone.
    expect(textOf(editor, 'STAB0041')).toBe(before);
    expect(editor.getHTML()).not.toContain('data-compose-mark');
    expect(result.current.pending).toHaveLength(0);
    editor.destroy();
  });

  it('"apply anyway" still places it, and re-baselines so the question does not return', () => {
    const { editor, referenceMap } = makeDoc();
    wipeAllStorage();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    act(() => {
      result.current.materialize(PAYLOAD, REPLAY);
    });
    // Non-vacuous guard: the placement must be WAITING on the answer. Without this the assertions
    // below also hold under the defect, because a silent apply leaves the same pending redline.
    expect(result.current.staleTarget).not.toBeNull();
    expect(result.current.pending).toHaveLength(0);

    act(() => {
      result.current.applyStaleTargetAnyway();
    });

    expect(result.current.staleTarget).toBeNull();
    expect(result.current.pending.map(p => p.ledgerRef)).toEqual(['b1@t1']);
    expect(editor.getHTML()).toContain('data-compose-mark="insertion"');
    // Re-baselined: the same question cannot return for this key in this browser.
    expect(compareProposalBaseline(SCOPE, 'b1@t1', textOf(editor, 'STAB0041')).status).not.toBe('unrecorded');
    editor.destroy();
  });

  it('a provenance that DECLARES NOTHING is treated as a replay (fail-closed by default)', () => {
    const { editor, referenceMap } = makeDoc();
    wipeAllStorage();
    const before = textOf(editor, 'STAB0041');

    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    let status: string | undefined;
    act(() => {
      status = result.current.materialize(PAYLOAD, UNDECLARED);
    });

    // Omission must never buy the caller the unsafe outcome.
    expect(status).toBe('stale');
    expect(result.current.staleTarget?.reason).toBe('unverifiable');
    expect(textOf(editor, 'STAB0041')).toBe(before);
    editor.destroy();
  });

  it('a LIVE materialize records the baseline and places without asking (no new friction)', () => {
    const { editor, referenceMap } = makeDoc();
    wipeAllStorage();

    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    let status: string | undefined;
    act(() => {
      status = result.current.materialize(PAYLOAD, LIVE);
    });

    expect(status).toBe('applied');
    expect(result.current.staleTarget).toBeNull();
    expect(editor.getHTML()).toContain('data-compose-mark="insertion"');
    // Non-vacuous guard: it recorded DURABLY, not just per-tab. Wipe the tab and ask again — the
    // answer must still be "unchanged", which is what keeps the other tab quiet.
    wipeTabStorage();
    expect(compareProposalBaseline(SCOPE, 'b1@t1', CLAUSE_41)).toEqual({ status: 'unchanged' });
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 2. THE CROSS-TAB CASE — the durable fingerprint answers when the tab record is gone
// ---------------------------------------------------------------------------
describe('052b — a different tab still detects the drift', () => {
  it('DRIFTED clause: detected as changed even with sessionStorage wiped', () => {
    const { editor, referenceMap } = makeDoc();
    proposeLive(editor, referenceMap); // tab A recorded the baseline
    const drifted = driftClause(editor);
    wipeTabStorage(); // tab B: per-tab state is gone, the origin-scoped record is not

    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    let status: string | undefined;
    act(() => {
      status = result.current.materialize(PAYLOAD, REPLAY);
    });

    expect(status).toBe('stale');
    expect(result.current.staleTarget?.reason).toBe('changed');
    // The fingerprint proves the drift; it cannot reproduce the words, and we do not pretend it can.
    expect(result.current.staleTarget?.proposedAgainst).toBeNull();
    expect(result.current.staleTarget?.currentText).toBe(drifted);
    expect(textOf(editor, 'STAB0041')).toBe(drifted);
    expect(result.current.pending).toHaveLength(0);
    editor.destroy();
  });

  it('UNCHANGED clause: no question at all in the other tab (the fingerprint keeps it quiet)', () => {
    const { editor, referenceMap } = makeDoc();
    proposeLive(editor, referenceMap);
    wipeTabStorage();

    // Non-vacuous guard: assert the QUIET comes from the surviving durable record. Without this the
    // test also passes under the defect, which reaches "no question" by having no record at all.
    expect(compareProposalBaseline(SCOPE, 'b1@t1', CLAUSE_41)).toEqual({ status: 'unchanged' });

    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    let status: string | undefined;
    act(() => {
      status = result.current.materialize(PAYLOAD, REPLAY);
    });

    expect(status).toBe('applied');
    expect(result.current.staleTarget).toBeNull();
    editor.destroy();
  });

  it('SAME tab keeps the richer question — the capture-time wording is shown', () => {
    const { editor, referenceMap } = makeDoc();
    proposeLive(editor, referenceMap);
    driftClause(editor); // no wipe: this IS the tab that proposed

    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    act(() => {
      result.current.materialize(PAYLOAD, REPLAY);
    });

    expect(result.current.staleTarget?.reason).toBe('changed');
    expect(result.current.staleTarget?.proposedAgainst).toBe(CLAUSE_41);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 3. EVICTION + STORAGE-DISABLED — the two remaining 052 holes
// ---------------------------------------------------------------------------
describe('052b — eviction and storage-disabled reach a defined outcome', () => {
  it('survives eviction of the per-tab text tier', () => {
    const { editor, referenceMap } = makeDoc();
    proposeLive(editor, referenceMap);
    // Flood the per-tab text tier well past its cap; the durable tier is far larger and unaffected.
    for (let i = 0; i < 400; i++) recordProposalBaseline(SCOPE, `flood@t${i}`, `filler paragraph ${i}`);
    const drifted = driftClause(editor);

    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    act(() => {
      result.current.materialize(PAYLOAD, REPLAY);
    });

    expect(result.current.staleTarget).not.toBeNull();
    expect(result.current.staleTarget?.reason).toBe('changed');
    expect(textOf(editor, 'STAB0041')).toBe(drifted);
    editor.destroy();
  });

  it('storage disabled: a replay asks rather than silently applying', () => {
    const { editor, referenceMap } = makeDoc();
    const before = textOf(editor, 'STAB0041');
    const restore = breakStorage();
    try {
      const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
      let status: string | undefined;
      act(() => {
        status = result.current.materialize(PAYLOAD, REPLAY);
      });
      expect(status).toBe('stale');
      expect(result.current.staleTarget?.reason).toBe('unverifiable');
      expect(textOf(editor, 'STAB0041')).toBe(before);
    } finally {
      restore();
    }
    editor.destroy();
  });

  it('storage disabled: a LIVE materialize still places (recording is best-effort, never fatal)', () => {
    const { editor, referenceMap } = makeDoc();
    const restore = breakStorage();
    try {
      const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
      let status: string | undefined;
      act(() => {
        status = result.current.materialize(PAYLOAD, LIVE);
      });
      expect(status).toBe('applied');
      expect(result.current.staleTarget).toBeNull();
    } finally {
      restore();
    }
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 3b. A WHOLE-DOCUMENT BATCH — one question, and copy that does not over-claim
// ---------------------------------------------------------------------------
describe('052b — a replayed whole-document change list', () => {
  const EDITS = [
    { target_para_id: 'STAB0041', new_text: CLAUSE_41.replace('twelve', 'twenty-four') },
    { target_para_id: 'STAB0042', new_text: CLAUSE_42.replace('any breach', 'any material breach') },
  ];
  const BASE_LIVE = { ledgerRef: 'rev@t1', bindingId: 'rev', turn: 1, origin: 'live' as const };
  const BASE_REPLAY = { ...BASE_LIVE, origin: 'replay' as const };

  it('asks ONCE for the whole set — a cross-device reopen costs one click, not N', () => {
    const { editor, referenceMap } = makeDoc();
    wipeAllStorage();

    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    let statuses: string[] = [];
    act(() => {
      statuses = result.current.materializeMany(EDITS, BASE_REPLAY);
    });

    expect(statuses).toEqual(['stale', 'stale']);
    expect(result.current.staleTarget).toMatchObject({
      ledgerRef: 'rev@t1',
      reason: 'unverifiable',
      staleCount: 2,
      totalCount: 2,
    });
    expect(result.current.staleTarget?.proposedAgainst).toBeNull();
    // Nothing is in the document while the question stands.
    expect(editor.getHTML()).not.toContain('data-compose-mark');
    editor.destroy();
  });

  it('a MIXED batch reports the stronger, still-true reason: at least one clause definitely changed', () => {
    const { editor, referenceMap } = makeDoc();

    // Baseline BOTH clauses (live), then drop 4.2's record only — so 4.1 is provably changed while
    // 4.2 is merely unverifiable. 4.1 is the SECOND item in the list, so a first-wins rule would
    // report 'unverifiable' and under-claim.
    const first = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    act(() => {
      first.result.current.materializeMany(EDITS, BASE_LIVE);
    });
    act(() => {
      first.result.current.reject('rev@t1');
    });
    first.unmount();
    driftClause(editor); // only clause 4.1 drifts
    clearProposalBaselines('unused'); // no-op; keeps the intent explicit below
    // Drop every record for the FIRST-listed edit (4.1) so it reads as unverifiable, and keep 4.2's.
    // (Sub-keys are `{base}#{i}` — 4.1 is #0, 4.2 is #1.)
    const textKey = 'spaarke.compose.redline-proposal-baseline.' + SCOPE;
    const textMap = JSON.parse(window.sessionStorage.getItem(textKey) ?? '{}') as Record<string, string>;
    delete textMap['rev@t1#0'];
    window.sessionStorage.setItem(textKey, JSON.stringify(textMap));
    const fpKey = 'spaarke.compose.redline-proposal-fingerprint';
    const fpMap = JSON.parse(window.localStorage.getItem(fpKey) ?? '{}') as Record<string, string>;
    delete fpMap[`${SCOPE.length}|${SCOPE}|rev@t1#0`];
    window.localStorage.setItem(fpKey, JSON.stringify(fpMap));
    // ...and drift 4.2 too, so it is the one that is provably changed.
    const block = collectBlocks(editor).find(b => b.paraId === 'STAB0042')!;
    editor
      .chain()
      .setTextSelection({ from: block.from + 1, to: block.to - 1 })
      .insertContent('Clause 4.2: The parties owe each other no indemnity.')
      .run();

    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    act(() => {
      result.current.materializeMany(EDITS, BASE_REPLAY);
    });

    expect(result.current.staleTarget).toMatchObject({ reason: 'changed', staleCount: 2, paraId: 'STAB0042' });
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 4. THE MODULE CONTRACT — fingerprint behaviour + ADR-015
// ---------------------------------------------------------------------------
describe('052b — redlineProposalBaseline module contract', () => {
  it('reports `unrecorded` for a key it never saw', () => {
    expect(compareProposalBaseline(SCOPE, 'never@t9', CLAUSE_41)).toEqual({ status: 'unrecorded' });
  });

  it('reports `unchanged` for the same text and `changed` for any mutation of it', () => {
    recordProposalBaseline(SCOPE, 'b1@t1', CLAUSE_41);
    expect(compareProposalBaseline(SCOPE, 'b1@t1', CLAUSE_41)).toEqual({ status: 'unchanged' });

    const mutations = [
      CLAUSE_41.replace('twelve', 'twenty-four'),
      CLAUSE_41 + ' ',
      ' ' + CLAUSE_41,
      CLAUSE_41.replace('.', ''),
      CLAUSE_41.toUpperCase(),
      CLAUSE_41.slice(0, -1),
      CLAUSE_41.replace('Supplier', 'Suppiler'), // length-equal transposition — must still differ
      '',
    ];
    for (const mutated of mutations) {
      expect(compareProposalBaseline(SCOPE, 'b1@t1', mutated)).toMatchObject({ status: 'changed' });
    }
  });

  it('the durable store never holds Tier-3 text — only a one-way fingerprint (ADR-015)', () => {
    recordProposalBaseline(SCOPE, 'b1@t1', CLAUSE_41);
    let durable = '';
    for (let i = 0; i < window.localStorage.length; i++) {
      const key = window.localStorage.key(i)!;
      durable += key + '=' + (window.localStorage.getItem(key) ?? '') + '\n';
    }
    expect(durable).not.toBe(''); // it DID record something durable
    expect(durable).not.toContain('aggregate liability');
    expect(durable).not.toContain('Supplier');
    expect(durable).toContain(fingerprintParagraph(CLAUSE_41));
  });

  it('is stable and total: same text means same fingerprint, and no input throws', () => {
    expect(fingerprintParagraph(CLAUSE_41)).toBe(fingerprintParagraph(CLAUSE_41));
    expect(fingerprintParagraph('')).toBe(fingerprintParagraph(''));
    expect(() => fingerprintParagraph('mixed unicode and surrogates')).not.toThrow();
    expect(fingerprintParagraph('a')).not.toBe(fingerprintParagraph('b'));
  });

  it('scopes are isolated — one document cannot answer for another', () => {
    recordProposalBaseline(SCOPE, 'b1@t1', CLAUSE_41);
    expect(compareProposalBaseline('other-document', 'b1@t1', CLAUSE_41)).toEqual({ status: 'unrecorded' });
  });

  it('clearProposalBaselines drops BOTH tiers for the scope and leaves other scopes intact', () => {
    recordProposalBaseline(SCOPE, 'b1@t1', CLAUSE_41);
    recordProposalBaseline('other-document', 'b1@t1', CLAUSE_41);
    clearProposalBaselines(SCOPE);
    expect(compareProposalBaseline(SCOPE, 'b1@t1', CLAUSE_41)).toEqual({ status: 'unrecorded' });
    expect(compareProposalBaseline('other-document', 'b1@t1', CLAUSE_41)).toEqual({ status: 'unchanged' });
  });
});

// ---------------------------------------------------------------------------
// 5. NEGATIVE — 052's guarantee is untouched
// ---------------------------------------------------------------------------
describe('052b — no placement path searches document text', () => {
  it('the unverifiable question is raised by the ANCHOR, never by prose', () => {
    const { editor, referenceMap } = makeDoc();
    wipeAllStorage();
    mockTripwireArmed = true;

    const { result } = renderHook(() => usePendingRedline(editor, referenceMap, { proposalScope: SCOPE }));
    act(() => {
      // Prose IS present and WOULD have matched — any route other than the anchor trips the wire.
      result.current.materialize({ ...PAYLOAD, target_text: 'twelve months of fees' }, REPLAY);
    });

    expect(result.current.staleTarget?.reason).toBe('unverifiable');
    editor.destroy();
  });
});
