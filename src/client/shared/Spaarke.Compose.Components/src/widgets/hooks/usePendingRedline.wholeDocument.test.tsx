/**
 * usePendingRedline.wholeDocument.test.tsx — FR-C03 whole-document half (r8 task 055).
 *
 * Task 051 proved the anchor branch on SINGLE edits and on a two-entry change list. This file does
 * the thing task 055 exists to do: exercise the branch against a payload shaped like a REAL
 * `compose-revise-document` output — a ten-paragraph agreement and an eight-change revision mixing
 * paraId anchors, citation anchors, legacy prose targets and one deliberately broken anchor — and
 * prove the two properties that a single-edit test cannot:
 *
 *  1. ZERO text matching for anchored items, asserted STRUCTURALLY. The text-search collaborator
 *     (`./redlineTextSearch`, extracted in this task precisely so it CAN be replaced) is swapped for
 *     a throwing/recording double — the client mirror of `ThrowIfTextSearched` /
 *     `RecordingTextValidator` in `tests/integration/seam/Compose/ComposeEditAnchorPassSeamTests.cs`.
 *     Inspecting the resulting document would only tell us where the edits LANDED; the tripwire tells
 *     us which ROUTE they took, which is the actual contract.
 *  2. Per-item failure isolation and honest reporting under a PARTIALLY-anchored batch — one
 *     unresolvable anchor skips only its own item, and the banner's N-of-M counts describe what
 *     really happened (UAT-21: never report `applied` for something that was not placed).
 *
 * The document is built so that a text search for the phrase two clauses share is AMBIGUOUS by
 * construction. That is what makes "the anchored edit landed on the right one" a result rather than
 * a coincidence.
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
import type { ParaIdMapEntry } from '../../types/compose-contracts';
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
// The tripwire — the client twin of ComposeEditAnchorPassSeamTests' ThrowIfTextSearched.
//
// `mock`-prefixed module-scope bindings are the only ones jest lets a module factory close over.
// ARMED    -> any text search throws, so an anchored item that fell through fails the test loudly
//             instead of returning something plausible.
// DISARMED -> the real implementation still runs, and every call is RECORDED, so the un-anchored
//             leg can be proven LIVE (which is what makes the armed silence meaningful).
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
            '") on a batch whose items carried deterministic anchors. An anchored edit must resolve ' +
            'through the paraId map and never reach target_text.'
        );
      }
      return (actual.resolveTargetSpans as (...a: unknown[]) => unknown)(...args);
    },
  };
});

const PROV = { ledgerRef: 'revise-binding@t1', bindingId: 'revise-binding', turn: 1 };

function entry(index: number, paraId: string, computedNumber: string, listPath: number[]): ParaIdMapEntry {
  return { index, paraId, isMinted: false, computedNumber, listPath };
}

/**
 * A ten-paragraph agreement — the smallest document that still behaves like the real thing: numbered
 * top-level clauses, two sub-clauses under 4, and the phrase "shall indemnify" recurring in TWO
 * clauses (2 and 4.2) so a prose search for it is genuinely ambiguous.
 */
const PARAGRAPHS: ReadonlyArray<{ paraId: string; number: string; path: number[]; text: string }> = [
  { paraId: 'AAAA0001', number: '1', path: [1], text: '1. Definitions. In this Agreement the following terms apply.' },
  {
    paraId: 'AAAA0002',
    number: '2',
    path: [2],
    text: '2. Confidentiality. The receiving party shall indemnify the disclosing party for any breach.',
  },
  { paraId: 'AAAA0003', number: '3', path: [3], text: '3. Term. This Agreement commences on the Effective Date.' },
  {
    paraId: 'AAAA0004',
    number: '4',
    path: [4],
    text: '4. Liability. The aggregate liability of the Supplier shall not exceed the fees paid.',
  },
  {
    paraId: 'AAAA0041',
    number: '4.1',
    path: [4, 1],
    text: '4.1 Cap. The liability cap shall be twelve months of fees.',
  },
  {
    paraId: 'AAAA0042',
    number: '4.2',
    path: [4, 2],
    text: '4.2 Carve-outs. The disclosing party shall indemnify the receiving party for any breach.',
  },
  {
    paraId: 'AAAA0005',
    number: '5',
    path: [5],
    text: '5. Termination. Either party may terminate on thirty days notice.',
  },
  {
    paraId: 'AAAA0006',
    number: '6',
    path: [6],
    text: '6. Governing Law. This Agreement is governed by the laws of Delaware.',
  },
  { paraId: 'AAAA0007', number: '7', path: [7], text: '7. Notices. All notices shall be in writing.' },
  {
    paraId: 'AAAA0008',
    number: '8',
    path: [8],
    text: '8. Entire Agreement. This document constitutes the entire agreement.',
  },
];

function makeAgreement(): { editor: Editor; referenceMap: ParaIdMapEntry[] } {
  const editor = new Editor({
    extensions: [StarterKit, InsertionMark, DeletionMark, CommentAnchorMark, ...COMPOSE_R3_PARAID],
    content: PARAGRAPHS.map(p => '<p>' + p.text + '</p>').join(''),
  });
  const referenceMap = PARAGRAPHS.map((p, i) => entry(i, p.paraId, p.number, p.path));
  stampParaIds(editor, referenceMap);
  return { editor, referenceMap };
}

/** The text a given paraId's live span currently covers. */
function textOf(editor: Editor, paraId: string): string {
  const block = collectBlocks(editor).find(b => b.paraId === paraId);
  return block ? editor.state.doc.textBetween(block.from, block.to, ' ') : '';
}

/**
 * The whole-document change list. Eight changes: four by paraId, two by citation, two by legacy
 * prose. Kept as a factory so each test gets its own array.
 */
function wholeDocumentEdits(): ComposeDraftPayload[] {
  return [
    { new_text: 'REV-1 mutual indemnity language.', target_para_id: 'AAAA0002' },
    { new_text: 'REV-2 revised liability ceiling.', target_para_id: 'AAAA0004' },
    { new_text: 'REV-3 twenty-four month cap.', target_ref: 'clause 4.1' },
    { new_text: 'REV-4 narrowed carve-outs.', target_ref: '4.2' },
    { new_text: 'REV-5 sixty days notice.', target_text: 'thirty days notice' },
    { new_text: 'REV-6 New York law.', target_text: 'laws of Delaware' },
    { new_text: 'REV-7 electronic notices permitted.', target_para_id: 'AAAA0007' },
    { new_text: 'REV-8 severability added.', target_para_id: 'AAAA0008' },
  ];
}

const ANCHORED_INDEXES = [0, 1, 2, 3, 6, 7];
const LEGACY_INDEXES = [4, 5];

beforeEach(() => {
  mockTripwireArmed = false;
  mockTextSearchTargets.length = 0;
});

// ---------------------------------------------------------------------------
// Step 1 — the anchor branch against a REAL multi-change payload
// ---------------------------------------------------------------------------
describe('materializeMany — a whole-document change list (8 changes, mixed anchoring)', () => {
  it('places every change at its own target and returns one index-aligned status per change', () => {
    const { editor, referenceMap } = makeAgreement();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));
    const edits = wholeDocumentEdits();

    let statuses: string[] = [];
    act(() => {
      statuses = result.current.materializeMany(edits, PROV);
    });

    expect(statuses).toHaveLength(edits.length);
    // FR-C06 (task 053): the six ANCHORED changes place immediately; the two LEGACY prose changes are
    // held back as a PROPOSAL until the user confirms. That asymmetry is the requirement, not a gap —
    // an address places, a resemblance asks.
    expect(ANCHORED_INDEXES.map(i => statuses[i])).toEqual(ANCHORED_INDEXES.map(() => 'applied'));
    expect(LEGACY_INDEXES.map(i => statuses[i])).toEqual(LEGACY_INDEXES.map(() => 'proposed'));
    expect(result.current.legacyProposal).toMatchObject({ proposedCount: 2, totalCount: edits.length });
    // Nothing is in the document for a held-back change while the question is open.
    expect(editor.state.doc.textContent).not.toContain('REV-5');
    expect(editor.state.doc.textContent).not.toContain('REV-6');

    // The user confirms; both proposals land where the proposal said they would.
    act(() => {
      result.current.applyLegacyProposal();
    });
    expect(result.current.legacyProposal).toBeNull();

    // Anchored changes landed on the paragraph they NAMED, not on a paragraph that merely reads alike.
    expect(textOf(editor, 'AAAA0002')).toContain('REV-1');
    expect(textOf(editor, 'AAAA0004')).toContain('REV-2');
    expect(textOf(editor, 'AAAA0041')).toContain('REV-3');
    expect(textOf(editor, 'AAAA0042')).toContain('REV-4');
    expect(textOf(editor, 'AAAA0007')).toContain('REV-7');
    expect(textOf(editor, 'AAAA0008')).toContain('REV-8');
    // Legacy prose changes landed where their (unique) target text was.
    expect(textOf(editor, 'AAAA0005')).toContain('REV-5');
    expect(textOf(editor, 'AAAA0006')).toContain('REV-6');
    // Untouched clauses stayed untouched.
    expect(textOf(editor, 'AAAA0001')).not.toContain('REV-');
    expect(textOf(editor, 'AAAA0003')).not.toContain('REV-');

    expect(result.current.pending).toHaveLength(edits.length);
    expect(result.current.error).toBeNull();
    editor.destroy();
  });

  it('an anchored change outranks the prose: the SAME target_text would have been ambiguous', () => {
    const { editor, referenceMap } = makeAgreement();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    // "shall indemnify" occurs in BOTH clause 2 and clause 4.2 — a strict text search refuses it.
    act(() => {
      result.current.materializeMany(
        [
          { new_text: 'ANCHORED-TO-2', target_text: 'shall indemnify', target_para_id: 'AAAA0002' },
          { new_text: 'ANCHORED-TO-FOUR-TWO', target_text: 'shall indemnify', target_ref: '4.2' },
        ],
        PROV
      );
    });

    expect(textOf(editor, 'AAAA0002')).toContain('ANCHORED-TO-2');
    expect(textOf(editor, 'AAAA0002')).not.toContain('ANCHORED-TO-FOUR-TWO');
    expect(textOf(editor, 'AAAA0042')).toContain('ANCHORED-TO-FOUR-TWO');
    editor.destroy();
  });

  it('each change keeps its own sub-key so per-change accept/reject stays granular across the batch', () => {
    const { editor, referenceMap } = makeAgreement();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));
    const edits = wholeDocumentEdits();

    act(() => {
      result.current.materializeMany(edits, PROV);
    });
    // FR-C06 (task 053): the two legacy prose changes are PROPOSED, so they are not pending yet.
    expect(result.current.pending.map(p => p.ledgerRef)).toEqual(ANCHORED_INDEXES.map(i => PROV.ledgerRef + '#' + i));
    act(() => {
      result.current.applyLegacyProposal();
    });
    // After the confirmation every change is pending under its own sub-key. Order follows placement
    // (anchored first, then the confirmed proposals), so compare as sets.
    expect(result.current.pending.map(p => p.ledgerRef).sort()).toEqual(
      edits.map((_, i) => PROV.ledgerRef + '#' + i).sort()
    );

    // Reject ONE anchored change — the other seven survive, and only that clause reverts.
    act(() => {
      result.current.reject(PROV.ledgerRef + '#0');
    });
    expect(result.current.pending).toHaveLength(edits.length - 1);
    expect(textOf(editor, 'AAAA0002')).not.toContain('REV-1');
    expect(textOf(editor, 'AAAA0004')).toContain('REV-2');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// Step 4 — ZERO text matching for anchored items, proven structurally
// ---------------------------------------------------------------------------
describe('materializeMany — the text-search collaborator is never reached by an anchored change', () => {
  it('an all-anchored whole-document batch applies with the tripwire ARMED', () => {
    const { editor, referenceMap } = makeAgreement();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));
    // Every anchored change ALSO carries prose. That is deliberate: without it, a regression that
    // stopped honouring anchors would fall into the no-target insertion branch and never reach the
    // search, so the tripwire would stay silent and this test would pass vacuously. With prose
    // present, ANY route other than the anchor trips the wire. (Verified by mutation: disabling
    // `resolveAnchoredSpans` makes this test fail with the tripwire's own message.)
    const anchoredOnly = wholeDocumentEdits()
      .filter((_, i) => ANCHORED_INDEXES.includes(i))
      .map(e => ({ ...e, target_text: 'shall be' }));

    mockTripwireArmed = true;
    let statuses: string[] = [];
    act(() => {
      statuses = result.current.materializeMany(anchoredOnly, PROV);
    });

    expect(statuses).toEqual(anchoredOnly.map(() => 'applied'));
    expect(mockTextSearchTargets).toEqual([]);
    editor.destroy();
  });

  it('CONTROL — the tripwire really fires: one un-anchored change in the batch trips it', () => {
    const { editor, referenceMap } = makeAgreement();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    mockTripwireArmed = true;
    expect(() => {
      act(() => {
        result.current.materializeMany(
          [
            { new_text: 'anchored', target_para_id: 'AAAA0002' },
            { new_text: 'prose', target_text: 'thirty days notice' },
          ],
          PROV
        );
      });
    }).toThrow(/Text search was invoked/);
    editor.destroy();
  });

  it('CONTROL — with the tripwire disarmed, the search is reached by EXACTLY the un-anchored changes', () => {
    const { editor, referenceMap } = makeAgreement();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));
    const edits = wholeDocumentEdits();

    act(() => {
      result.current.materializeMany(edits, PROV);
    });

    // Recorded targets are the legacy entries' prose, in order — nothing else was searched for.
    expect(mockTextSearchTargets).toEqual(LEGACY_INDEXES.map(i => edits[i].target_text));
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// Step 5 — per-item failure isolation + honest reporting on a PARTIALLY-anchored batch
// ---------------------------------------------------------------------------
describe('materializeMany — per-item isolation and honest N-of-M reporting', () => {
  /** The eight-change list, plus three that cannot be placed: a dead paraId, an unknown citation,
   *  and prose that is not in the document. */
  function partiallyPlaceable(): ComposeDraftPayload[] {
    const edits = wholeDocumentEdits();
    edits.splice(2, 0, { new_text: 'DEAD-ANCHOR', target_para_id: 'DEADBEEF' });
    edits.splice(6, 0, { new_text: 'DEAD-CITATION', target_ref: 'clause 99.9' });
    edits.push({ new_text: 'DEAD-PROSE', target_text: 'a phrase that is nowhere in this agreement' });
    return edits;
  }

  it('skips ONLY the unplaceable changes and applies every other one', () => {
    const { editor, referenceMap } = makeAgreement();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));
    const edits = partiallyPlaceable();

    let statuses: string[] = [];
    act(() => {
      statuses = result.current.materializeMany(edits, PROV);
    });

    expect(statuses).toEqual([
      'applied', // REV-1 paraId
      'applied', // REV-2 paraId
      // Task 052 (FR-C05 outcome 3): a paraId that RESOLVES as an identity but is absent from the live
      // document is `target_deleted` — distinguishable from a citation that never resolved at all.
      'target_deleted', // DEAD-ANCHOR
      'applied', // REV-3 citation
      'applied', // REV-4 citation
      // FR-C06 (task 053): a legacy prose change PROPOSES; it is not placed until the user confirms.
      'proposed', // REV-5 prose
      'not_found', // DEAD-CITATION (the numbering map has no clause 99.9 — nothing to delete)
      'proposed', // REV-6 prose
      'applied', // REV-7 paraId
      'applied', // REV-8 paraId
      'not_found', // DEAD-PROSE (prose that is nowhere in the document — refused, never proposed)
    ]);
    expect(statuses.filter(s => s === 'applied')).toHaveLength(6);
    expect(statuses.filter(s => s === 'proposed')).toHaveLength(2);

    act(() => {
      result.current.applyLegacyProposal();
    });

    // The whole document still received its eight real revisions.
    expect(textOf(editor, 'AAAA0002')).toContain('REV-1');
    expect(textOf(editor, 'AAAA0008')).toContain('REV-8');
    // Nothing anywhere in the document carries the text of a change that was refused.
    expect(editor.state.doc.textContent).not.toContain('DEAD-ANCHOR');
    expect(editor.state.doc.textContent).not.toContain('DEAD-CITATION');
    expect(editor.state.doc.textContent).not.toContain('DEAD-PROSE');
    editor.destroy();
  });

  it('reports honest N-of-M counts, and never reports `applied` for a change it did not place', () => {
    const { editor, referenceMap } = makeAgreement();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));
    const edits = partiallyPlaceable();

    let statuses: string[] = [];
    act(() => {
      statuses = result.current.materializeMany(edits, PROV);
    });

    expect(result.current.error).not.toBeNull();
    expect(result.current.error?.failedCount).toBe(3);
    // M counts the changes that NAMED a target (all of them here — none is an insertion-at-cursor).
    expect(result.current.error?.totalCount).toBe(edits.length);
    // Every pending redline corresponds to an `applied` status; the refused three left none behind.
    expect(result.current.pending).toHaveLength(statuses.filter(s => s === 'applied').length);
    const appliedSubKeys = statuses
      .map((s, i) => (s === 'applied' ? PROV.ledgerRef + '#' + i : null))
      .filter((v): v is string => v !== null);
    expect(result.current.pending.map(p => p.ledgerRef)).toEqual(appliedSubKeys);
    editor.destroy();
  });

  it('a failed ANCHOR is named in the banner even though it has no prose to quote (UAT-21 honesty)', () => {
    const { editor, referenceMap } = makeAgreement();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materializeMany(
        [
          { new_text: 'ok', target_para_id: 'AAAA0002' },
          { new_text: 'nope', target_para_id: 'DEADBEEF' },
        ],
        PROV
      );
    });

    expect(result.current.error?.targetText).toBe('paragraph DEADBEEF');
    expect(result.current.error?.failedCount).toBe(1);
    expect(result.current.error?.totalCount).toBe(2);
    editor.destroy();
  });

  it('an unplaceable anchor is REFUSED, not retried as a text search (tripwire ARMED)', () => {
    const { editor, referenceMap } = makeAgreement();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    mockTripwireArmed = true;
    let statuses: string[] = [];
    act(() => {
      statuses = result.current.materializeMany(
        [
          // Carries BOTH a dead anchor and prose that WOULD have matched. Falling back would place it.
          { new_text: 'must not be placed', target_para_id: 'DEADBEEF', target_text: 'thirty days notice' },
        ],
        PROV
      );
    });

    expect(statuses).toEqual(['target_deleted']);
    expect(mockTextSearchTargets).toEqual([]);
    expect(editor.state.doc.textContent).not.toContain('must not be placed');
    expect(textOf(editor, 'AAAA0005')).toContain('thirty days notice');
    editor.destroy();
  });
});
