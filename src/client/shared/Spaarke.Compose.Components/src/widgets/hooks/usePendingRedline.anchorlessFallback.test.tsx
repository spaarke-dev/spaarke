/**
 * usePendingRedline.anchorlessFallback.test.tsx — FR-C06 + FR-C07 (spaarkeai-compose-r8 task 053).
 *
 * Task 052 demoted prose matching out of the AI edit's PRIMARY path. Task 053 bounds what is left: the
 * replayed/legacy leg, reachable only by a `compose` ledger entry written before the catalog change,
 * which now PROPOSES a placement the user confirms and can never place one on its own.
 *
 * The claims proven here, and why each needs its own test:
 *
 *  1. **BOUND 1 — an anchored edit cannot reach the fallback, structurally.** Proven two ways: as a
 *     property of `classifyAnchorlessReplay` (the only mint of the fallback's argument type refuses
 *     every anchored shape) and end-to-end with the module-boundary TRIPWIRE armed, which is what
 *     tells us which ROUTE an edit took rather than only where it landed. Inspecting the document
 *     alone would pass vacuously when a search happens to find the right paragraph.
 *
 *  2. **BOUND 2 — no auto-apply path exists.** `materialize` returns `'proposed'` and the document is
 *     untouched. The fallback module has no `applied` outcome in its type, so this is not a branch
 *     someone could forget to guard; the test pins the observable half of that.
 *
 *  3. **Ambiguity is REFUSED, never proposed.** Showing one of three candidates and asking the user to
 *     confirm it is UAT-21 wearing a dialog. `strict` is the pin, and this is what the pin buys.
 *
 *  4. **UAT-21 does not regress.** With a live selection sitting on unrelated text, an unplaceable
 *     suggestion places NOTHING — not at the selection, not anywhere — and never reports `applied`.
 *     A proposal likewise ignores the selection: it proposes the paragraph the prose is in, or
 *     nothing at all.
 *
 *  5. **FR-C07 — the failure states are source-specific.** An anchored miss reports that the named
 *     paragraph/section is absent (no text was compared, so no wording claim is available to make);
 *     a replayed miss reports that the entry predates paragraph references. Neither says "wording
 *     differs slightly", which is the state FR-C07 eliminates.
 *
 * @see ./anchorlessReplayFallback.ts — the bounded module under test.
 * @see ./usePendingRedline.wholeDocument.test.tsx — where this tripwire pattern was established.
 * @see projects/spaarkeai-compose-r8/notes/wording-differs-elimination-trace.md
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
import { usePendingRedline, collectMarkedRanges } from './usePendingRedline';
import { classifyAnchorlessReplay, resolveAnchorlessReplay } from './anchorlessReplayFallback';
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
// The tripwire — same device as usePendingRedline.wholeDocument.test.tsx (task 055), and the client
// twin of the server's `ThrowIfTextSearched` `IComposeEditValidator`. ARMED, any text search throws;
// DISARMED, the real implementation runs and every call is recorded so the fallback can be proven
// LIVE (which is what makes the armed silence mean something).
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
          `Text search was invoked for a target ("${target}") on an edit that carried a deterministic ` +
            'anchor. An anchored edit must resolve through the paraId map and never reach target_text.'
        );
      }
      return (actual.resolveTargetSpans as (...a: unknown[]) => unknown)(...args);
    },
  };
});

const PROV = { ledgerRef: 'b1@t1', bindingId: 'b1', turn: 1 };

function entry(index: number, paraId: string, computedNumber: string, listPath: number[]): ParaIdMapEntry {
  return { index, paraId, isMinted: false, computedNumber, listPath };
}

/**
 * A three-clause agreement. Clause 5 repeats the phrase clause 2 uses ("shall indemnify"), so a prose
 * search for it is AMBIGUOUS by construction — that is what makes the refusal test a result rather
 * than an accident of the fixture.
 */
function makeDoc(): { editor: Editor; referenceMap: ParaIdMapEntry[] } {
  const editor = new Editor({
    extensions: [StarterKit, InsertionMark, DeletionMark, CommentAnchorMark, ...COMPOSE_R3_PARAID],
    content:
      '<p>1. Term. This Agreement runs for thirty days notice unless renewed.</p>' +
      '<p>2. Indemnity. The receiving party shall indemnify the disclosing party.</p>' +
      '<p>5. Carve-outs. The disclosing party shall indemnify the receiving party.</p>',
  });
  const referenceMap = [entry(0, 'AAAA0001', '1', [1]), entry(1, 'AAAA0002', '2', [2]), entry(2, 'AAAA0005', '5', [5])];
  stampParaIds(editor, referenceMap);
  return { editor, referenceMap };
}

function textOf(editor: Editor, paraId: string): string {
  const block = collectBlocks(editor).find(b => b.paraId === paraId);
  return block ? editor.state.doc.textBetween(block.from, block.to, ' ') : '';
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

beforeEach(() => {
  mockTripwireArmed = false;
  mockTextSearchTargets.length = 0;
});

// ---------------------------------------------------------------------------
// BOUND 1 — the mint. An anchored edit cannot be turned into a fallback argument.
// ---------------------------------------------------------------------------
describe('classifyAnchorlessReplay — the only mint of the fallback argument (BOUND 1)', () => {
  it('refuses a paraId-anchored payload', () => {
    expect(classifyAnchorlessReplay({ target_para_id: 'AAAA0002', target_text: 'shall indemnify' })).toBeNull();
  });

  it('refuses a citation-anchored payload', () => {
    expect(classifyAnchorlessReplay({ target_ref: 'clause 4.2', target_text: 'shall indemnify' })).toBeNull();
  });

  it('refuses a payload carrying BOTH anchors', () => {
    expect(
      classifyAnchorlessReplay({ target_para_id: 'AAAA0002', target_ref: 'clause 2', target_text: 'x' })
    ).toBeNull();
  });

  it('refuses a payload with no prose to replay (an insertion-style draft is not this leg’s business)', () => {
    expect(classifyAnchorlessReplay({})).toBeNull();
    expect(classifyAnchorlessReplay({ target_text: '' })).toBeNull();
    expect(classifyAnchorlessReplay({ target_text: '   \n  ' })).toBeNull();
    expect(classifyAnchorlessReplay(undefined)).toBeNull();
  });

  it('mints ONLY for anchorless prose — the replayed/legacy population', () => {
    const minted = classifyAnchorlessReplay({ target_text: 'thirty days notice' });
    expect(minted).not.toBeNull();
    expect(minted?.quotedTarget).toBe('thirty days notice');
  });
});

// ---------------------------------------------------------------------------
// BOUND 2 — the outcome vocabulary. The module can propose or refuse; it cannot place.
// ---------------------------------------------------------------------------
describe('resolveAnchorlessReplay — proposes or refuses, never applies (BOUND 2)', () => {
  it('proposes the matched paragraph text alongside what the suggestion quoted', () => {
    const { editor } = makeDoc();
    const target = classifyAnchorlessReplay({ target_text: 'thirty days notice' })!;

    const outcome = resolveAnchorlessReplay(editor, target);

    expect(outcome.kind).toBe('proposed');
    if (outcome.kind === 'proposed') {
      expect(outcome.quotedTarget).toBe('thirty days notice');
      expect(outcome.matchedText).toBe('thirty days notice');
      expect(outcome.spans).toHaveLength(1);
    }
    // The document was NOT touched by resolving — the module reads, it never writes.
    expect(markCount(editor)).toBe(0);
    editor.destroy();
  });

  it('refuses an ambiguous phrase rather than proposing one of the candidates', () => {
    const { editor } = makeDoc();
    const target = classifyAnchorlessReplay({ target_text: 'shall indemnify' })!;

    const outcome = resolveAnchorlessReplay(editor, target);

    expect(outcome).toMatchObject({ kind: 'unresolved', reason: 'ambiguous', matchCount: 2 });
    editor.destroy();
  });

  it('refuses prose that is not in the document', () => {
    const { editor } = makeDoc();
    const target = classifyAnchorlessReplay({ target_text: 'a phrase that is nowhere in this agreement' })!;

    expect(resolveAnchorlessReplay(editor, target)).toMatchObject({ kind: 'unresolved', reason: 'not_found' });
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// FR-C06 — the hook-level contract: proposed, then confirmed or dismissed. Never auto-applied.
// ---------------------------------------------------------------------------
describe('usePendingRedline — the bounded confirmable fallback (FR-C06)', () => {
  it('a replayed anchorless edit is PROPOSED and places NOTHING until the user answers', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_text: 'thirty days notice', new_text: 'sixty days notice' }, PROV);
    });

    expect(status).toBe('proposed');
    // Nothing is in the document — not a mark, not the replacement text, not a pending entry.
    expect(markCount(editor)).toBe(0);
    expect(editor.state.doc.textContent).not.toContain('sixty days notice');
    expect(result.current.pending).toHaveLength(0);
    // ...and no error banner either: this is a question, not a failure.
    expect(result.current.error).toBeNull();
    expect(result.current.legacyProposal).toMatchObject({
      ledgerRef: 'b1@t1',
      bindingId: 'b1',
      matchedText: 'thirty days notice',
      quotedTarget: 'thirty days notice',
      proposedCount: 1,
      totalCount: 1,
    });
    editor.destroy();
  });

  it('confirming places exactly what the proposal showed, and clears the question', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materialize({ target_text: 'thirty days notice', new_text: 'sixty days notice' }, PROV);
    });
    act(() => {
      result.current.applyLegacyProposal();
    });

    expect(result.current.legacyProposal).toBeNull();
    expect(textOf(editor, 'AAAA0001')).toContain('sixty days notice');
    expect(collectMarkedRanges(editor, 'deletion', 'b1@t1').length).toBeGreaterThan(0);
    expect(collectMarkedRanges(editor, 'insertion', 'b1@t1').length).toBeGreaterThan(0);
    expect(result.current.pending).toHaveLength(1);
    editor.destroy();
  });

  it('dismissing places nothing and leaves the document exactly as it was', () => {
    const { editor, referenceMap } = makeDoc();
    const before = editor.getHTML();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materialize({ target_text: 'thirty days notice', new_text: 'sixty days notice' }, PROV);
    });
    act(() => {
      result.current.dismissLegacyProposal();
    });

    expect(result.current.legacyProposal).toBeNull();
    expect(result.current.pending).toHaveLength(0);
    expect(editor.getHTML()).toBe(before);
    editor.destroy();
  });

  it('NEGATIVE: no sequence of hook calls places a replayed edit without the confirmation', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));
    const payload: ComposeDraftPayload = { target_text: 'thirty days notice', new_text: 'sixty days notice' };

    // Re-materialize (a refresh replay), then reach for the OTHER question's answer — the stale
    // "apply anyway" leg must not release an anchorless proposal, and vice versa.
    act(() => {
      result.current.materialize(payload, PROV);
      result.current.materialize(payload, PROV);
      result.current.applyStaleTargetAnyway();
      result.current.accept('b1@t1');
      result.current.reject('b1@t1');
    });

    expect(markCount(editor)).toBe(0);
    expect(editor.state.doc.textContent).not.toContain('sixty days notice');
    editor.destroy();
  });

  it('an AMBIGUOUS replayed target is refused, not proposed (no guess dressed as a confirmation)', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_text: 'shall indemnify', new_text: 'shall defend' }, PROV);
    });

    expect(status).toBe('ambiguous');
    expect(result.current.legacyProposal).toBeNull();
    expect(markCount(editor)).toBe(0);
    expect(result.current.error).toMatchObject({ kind: 'ambiguous', source: 'legacy-replay', matchCount: 2 });
    editor.destroy();
  });

  it('a batch of replayed edits raises ONE batched question and confirms them together', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let statuses: string[] = [];
    act(() => {
      statuses = result.current.materializeMany(
        [
          { target_text: 'thirty days notice', new_text: 'sixty days notice' },
          { target_text: 'Carve-outs', new_text: 'Exceptions' },
        ],
        { ledgerRef: 'rev@t1', bindingId: 'rev', turn: 1 }
      );
    });

    expect(statuses).toEqual(['proposed', 'proposed']);
    expect(markCount(editor)).toBe(0);
    // ONE question for the set, keyed by the BASE ledger key the host supersedes (O-4).
    expect(result.current.legacyProposal).toMatchObject({
      ledgerRef: 'rev@t1',
      proposedCount: 2,
      totalCount: 2,
    });

    act(() => {
      result.current.applyLegacyProposal();
    });
    expect(result.current.pending.map(p => p.ledgerRef).sort()).toEqual(['rev@t1#0', 'rev@t1#1']);
    editor.destroy();
  });

  it('the TOLERANT pass proposes too — a whitespace-divergent quote never auto-applies', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    // Precise (1:1-fold) pass misses on the doubled space; the whitespace-collapsed fallback finds it.
    // FR-C06's whole point: that second, looser pass is exactly the reach that must be confirmed.
    let status: string | undefined;
    act(() => {
      status = result.current.materialize(
        { target_text: 'thirty  days   notice', new_text: 'sixty days notice' },
        PROV
      );
    });

    expect(status).toBe('proposed');
    expect(markCount(editor)).toBe(0);
    expect(result.current.legacyProposal?.matchedText).toBe('thirty days notice');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// BOUND 1, end-to-end — the ROUTE an anchored edit takes, not just where it lands.
// ---------------------------------------------------------------------------
describe('an anchored edit cannot reach the fallback (tripwire ARMED)', () => {
  it('an edit whose paraId anchor RESOLVES never touches the text search', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    mockTripwireArmed = true;
    let status: string | undefined;
    act(() => {
      // Carries prose as WELL as the anchor — a pre-052-shaped payload that also has task 051's
      // anchor. If precedence ever inverted, the tripwire fires instead of the test passing quietly.
      status = result.current.materialize(
        { target_para_id: 'AAAA0002', target_text: 'shall indemnify', new_text: 'REV-ANCHORED' },
        PROV
      );
    });

    expect(status).toBe('applied');
    expect(mockTextSearchTargets).toEqual([]);
    expect(textOf(editor, 'AAAA0002')).toContain('REV-ANCHORED');
    // Landed on the paragraph it NAMED, not the other one the prose also matches.
    expect(textOf(editor, 'AAAA0005')).not.toContain('REV-ANCHORED');
    expect(result.current.legacyProposal).toBeNull();
    editor.destroy();
  });

  it('an edit whose anchor FAILS is refused, never retried as a search or proposed', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    mockTripwireArmed = true;
    let status: string | undefined;
    act(() => {
      status = result.current.materialize(
        { target_para_id: 'DEADBEEF', target_text: 'shall indemnify', new_text: 'REV-ORPHAN' },
        PROV
      );
    });

    expect(status).toBe('target_deleted');
    expect(mockTextSearchTargets).toEqual([]);
    expect(result.current.legacyProposal).toBeNull();
    expect(markCount(editor)).toBe(0);
    editor.destroy();
  });

  it('CONTROL — the tripwire really fires: the same payload WITHOUT an anchor reaches the search', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    mockTripwireArmed = false;
    act(() => {
      result.current.materialize({ target_text: 'thirty days notice', new_text: 'sixty days notice' }, PROV);
    });

    // The fallback IS live for an anchorless payload — which is what makes the armed silence above a
    // statement about routing rather than about a search that never runs at all.
    expect(mockTextSearchTargets).toEqual(['thirty days notice']);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// UAT-21 — "never silently mis-place, never a false 'applied'".
// ---------------------------------------------------------------------------
describe('UAT-21 does not regress', () => {
  /** Put a live, unrelated selection on clause 5 — the stale-caret condition UAT-21 describes. */
  function selectClauseFive(editor: Editor): void {
    const block = collectBlocks(editor).find(b => b.paraId === 'AAAA0005')!;
    editor.commands.setTextSelection({ from: block.from + 1, to: block.to - 1 });
  }

  it('an unplaceable replayed suggestion places NOTHING at the live selection and never reports applied', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));
    act(() => {
      selectClauseFive(editor);
    });
    const selectedTextBefore = textOf(editor, 'AAAA0005');

    let status: string | undefined;
    act(() => {
      status = result.current.materialize(
        { target_text: 'a phrase that is nowhere in this agreement', new_text: 'WRONG-PLACE' },
        PROV
      );
    });

    expect(status).toBe('not_found');
    expect(status).not.toBe('applied');
    // The user's selected clause is byte-identical, and "WRONG-PLACE" is nowhere in the document.
    expect(textOf(editor, 'AAAA0005')).toBe(selectedTextBefore);
    expect(editor.state.doc.textContent).not.toContain('WRONG-PLACE');
    expect(markCount(editor)).toBe(0);
    expect(result.current.pending).toHaveLength(0);
    expect(result.current.error).toMatchObject({ kind: 'not_found', source: 'legacy-replay' });
    editor.destroy();
  });

  it('an unplaceable ANCHORED suggestion likewise places nothing at the live selection', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));
    act(() => {
      selectClauseFive(editor);
    });
    const selectedTextBefore = textOf(editor, 'AAAA0005');

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_ref: 'clause 99.9', new_text: 'WRONG-PLACE' }, PROV);
    });

    expect(status).toBe('not_found');
    expect(textOf(editor, 'AAAA0005')).toBe(selectedTextBefore);
    expect(editor.state.doc.textContent).not.toContain('WRONG-PLACE');
    expect(markCount(editor)).toBe(0);
    editor.destroy();
  });

  it('a PROPOSAL ignores the live selection — it proposes where the prose is, or nothing', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));
    act(() => {
      selectClauseFive(editor);
    });

    act(() => {
      // The quoted prose lives in clause 1; the caret is parked on clause 5.
      result.current.materialize({ target_text: 'thirty days notice', new_text: 'sixty days notice' }, PROV);
    });
    expect(result.current.legacyProposal?.matchedText).toBe('thirty days notice');

    act(() => {
      result.current.applyLegacyProposal();
    });

    // Placed in clause 1 (where the prose is), NOT in clause 5 (where the caret was).
    expect(textOf(editor, 'AAAA0001')).toContain('sixty days notice');
    expect(textOf(editor, 'AAAA0005')).not.toContain('sixty days notice');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// FR-C07 — the surviving failure states carry SOURCE, so the banner can say something true.
// ---------------------------------------------------------------------------
describe('FR-C07 — every unresolved-target error names which channel failed', () => {
  it('an anchored miss reports source "anchored" (no text was compared, so no wording claim exists)', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materialize({ target_ref: 'clause 99.9', new_text: 'X' }, PROV);
    });
    expect(result.current.error).toMatchObject({ kind: 'not_found', source: 'anchored' });

    act(() => {
      result.current.clearError();
      result.current.materialize({ target_para_id: 'DEADBEEF', new_text: 'X' }, { ...PROV, ledgerRef: 'b1@t2' });
    });
    expect(result.current.error).toMatchObject({ kind: 'target_deleted', source: 'anchored' });
    editor.destroy();
  });

  it('a replayed miss reports source "legacy-replay"', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materialize({ target_text: 'nowhere in this agreement at all', new_text: 'X' }, PROV);
    });
    expect(result.current.error).toMatchObject({ kind: 'not_found', source: 'legacy-replay' });
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// Issue #853 — WHY there is no anchor. Same mechanics, two populations, two stories.
//
// Every test above omits `origin`, so they all run the fail-closed 'replay' default. That is exactly
// how the bug survived: the whole suite exercised one population and the other one — the LIVE one, the
// only one a user actually hits since task 051 anchored every new suggestion — had no test at all.
// ---------------------------------------------------------------------------
describe('usePendingRedline — live-anchorless is not legacy-replay (#853)', () => {
  const LIVE = { ...PROV, origin: 'live' as const };

  it('a LIVE anchorless edit whose prose IS found is attributed to the assistant, not to history', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_text: 'thirty days notice', new_text: 'sixty days notice' }, LIVE);
    });

    // Mechanics UNCHANGED — still proposed, still nothing placed. Only the attribution differs.
    expect(status).toBe('proposed');
    expect(markCount(editor)).toBe(0);
    expect(result.current.legacyProposal).toMatchObject({
      reason: 'live-anchorless',
      matchedText: 'thirty days notice',
      quotedTarget: 'thirty days notice',
    });
    editor.destroy();
  });

  it('a LIVE anchorless edit whose prose is NOT found reports live-anchorless on the banner', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materialize({ target_text: 'a clause that is not here', new_text: 'x' }, LIVE);
    });

    expect(result.current.error).toMatchObject({ kind: 'not_found', source: 'live-anchorless' });
    editor.destroy();
  });

  it('a LIVE anchorless AMBIGUOUS target reports live-anchorless, and still refuses to guess', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materialize({ target_text: 'shall indemnify', new_text: 'shall defend' }, LIVE);
    });

    expect(result.current.error).toMatchObject({ kind: 'ambiguous', source: 'live-anchorless' });
    expect(markCount(editor)).toBe(0);
    editor.destroy();
  });

  it('FAIL-CLOSED: an UNDECLARED origin is still treated as a replay, never as a live failure', () => {
    // Omitting origin must not accuse the assistant of a contract failure it may not have committed.
    // This is the same asymmetry MaterializeOrigin already chose: the worst an omission costs is a
    // confirmation; the worst a wrong 'live' costs is a false statement about what happened.
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materialize({ target_text: 'thirty days notice', new_text: 'sixty days notice' }, PROV);
    });

    expect(result.current.legacyProposal).toMatchObject({ reason: 'legacy-replay' });
    editor.destroy();
  });

  it('an EXPLICIT replay origin stays legacy-replay', () => {
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materialize(
        { target_text: 'thirty days notice', new_text: 'sixty days notice' },
        { ...PROV, origin: 'replay' as const }
      );
    });

    expect(result.current.legacyProposal).toMatchObject({ reason: 'legacy-replay' });
    editor.destroy();
  });

  it('confirming a LIVE anchorless proposal places exactly what was shown — the guard is unchanged', () => {
    // The confirmation is not a consolation prize for the replay population; it is the bound. A live
    // suggestion that lost its anchor is MORE suspect, not less, so it keeps the same guard.
    const { editor, referenceMap } = makeDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materialize({ target_text: 'thirty days notice', new_text: 'sixty days notice' }, LIVE);
    });
    expect(markCount(editor)).toBe(0);

    act(() => {
      result.current.applyLegacyProposal();
    });

    expect(textOf(editor, 'AAAA0001')).toContain('sixty days notice');
    expect(result.current.legacyProposal).toBeNull();
    editor.destroy();
  });
});
