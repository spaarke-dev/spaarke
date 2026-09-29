/**
 * usePendingRedline.anchor.test.tsx — FR-C01/C02/C03 (spaarkeai-compose-r8 task 051).
 *
 * The client half of the anchor contract, and the mirror of
 * `tests/integration/seam/Compose/ComposeEditAnchorPassSeamTests.cs`. Before this task an AI edit named
 * its target in PROSE (`target_text`) and the apply path searched the document for the model's echoed
 * wording — so a clause that appeared twice, or wording the model lightly paraphrased, produced either a
 * refusal or (worse, historically) a redline on the wrong occurrence.
 *
 * What is proven here:
 *  - a `target_para_id` edit lands on THAT paragraph, and lands on it even when `target_text` would have
 *    resolved somewhere else entirely — the anchor OUTRANKS the prose, it does not merely supplement it;
 *  - a `target_ref` citation ("clause 4.2") resolves through the paraId map, with no text search;
 *  - every anchor failure REFUSES rather than falling back to a search — the fallback is what would
 *    quietly re-introduce the wrong-occurrence risk for exactly the edits that named their target
 *    exactly;
 *  - un-anchored edits still take the legacy text path unchanged (task 052 retires it, not this one).
 *
 * Uses the same headless `@tiptap/core` Editor + `stampParaIds` + `COMPOSE_R3_PARAID` convention as
 * `composeCitationResolver.test.ts`, so paraIds on the live document are real, not simulated.
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
import { usePendingRedline, resolveAnchoredSpans } from './usePendingRedline';
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

const PROV = { ledgerRef: 'b1@t1', bindingId: 'b1', turn: 1 };

function entry(index: number, paraId: string, computedNumber: string, listPath: number[]): ParaIdMapEntry {
  return { index, paraId, isMinted: false, computedNumber, listPath };
}

/**
 * Two clauses that share the phrase "shall indemnify". A text search for that phrase is AMBIGUOUS by
 * construction, which is what makes "the anchor placed it anyway" a real result rather than a
 * coincidence.
 */
function makeTwoClauseDoc(): { editor: Editor; referenceMap: ParaIdMapEntry[] } {
  const editor = new Editor({
    extensions: [StarterKit, InsertionMark, DeletionMark, CommentAnchorMark, ...COMPOSE_R3_PARAID],
    content:
      '<p>Clause 4.1: The receiving party shall indemnify the disclosing party for any breach.</p>' +
      '<p>Clause 4.2: The disclosing party shall indemnify the receiving party for any breach.</p>',
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

// ---------------------------------------------------------------------------
// resolveAnchoredSpans — the pure resolution contract
// ---------------------------------------------------------------------------
describe('resolveAnchoredSpans (deterministic anchor contract)', () => {
  it('returns null when no anchor is present — the ONLY route back to the text path', () => {
    const { editor, referenceMap } = makeTwoClauseDoc();
    expect(resolveAnchoredSpans(editor, { target_text: 'shall indemnify' }, referenceMap)).toBeNull();
    editor.destroy();
  });

  it('resolves a paraId anchor to that paragraph, case-insensitively', () => {
    const { editor, referenceMap } = makeTwoClauseDoc();

    const r = resolveAnchoredSpans(editor, { target_para_id: 'stab0042' }, referenceMap);

    expect(r?.ok).toBe(true);
    const span = r!.ok ? r!.spans[0] : null;
    expect(editor.state.doc.textBetween(span!.from, span!.to, ' ')).toContain('Clause 4.2');
    editor.destroy();
  });

  it('resolves a citation through the paraId map without any text search', () => {
    const { editor, referenceMap } = makeTwoClauseDoc();

    const r = resolveAnchoredSpans(editor, { target_ref: 'clause 4.2' }, referenceMap);

    expect(r?.ok).toBe(true);
    const span = r!.ok ? r!.spans[0] : null;
    expect(editor.state.doc.textBetween(span!.from, span!.to, ' ')).toContain('Clause 4.2');
    editor.destroy();
  });

  it('refuses a paraId that is not in the live document — never repairs it (task 052: target_deleted)', () => {
    const { editor, referenceMap } = makeTwoClauseDoc();
    const r = resolveAnchoredSpans(editor, { target_para_id: 'DEADBEEF' }, referenceMap);
    // FR-C05 outcome 3 (task 052): the anchor RESOLVED to a paragraph identity that is no longer in
    // the document — a DISTINCT outcome from `not_found` (an anchor that never resolved at all), so
    // the banner can say "the text this suggestion referred to no longer exists".
    expect(r).toEqual({ ok: false, kind: 'target_deleted', matchCount: 0, paraId: 'DEADBEEF' });
    editor.destroy();
  });

  it('refuses a citation naming several clauses rather than narrowing to the first', () => {
    const { editor, referenceMap } = makeTwoClauseDoc();
    const r = resolveAnchoredSpans(editor, { target_ref: 'Sections 1-9' }, referenceMap);
    expect(r).toEqual({ ok: false, kind: 'ambiguous', matchCount: 2 });
    editor.destroy();
  });

  it('refuses a citation anchor when no reference map is available — does NOT text-search instead', () => {
    const { editor } = makeTwoClauseDoc();
    const r = resolveAnchoredSpans(editor, { target_ref: 'clause 4.2' }, undefined);
    expect(r).toEqual({ ok: false, kind: 'not_found', matchCount: 0 });
    editor.destroy();
  });

  it('refuses two anchors that name different paragraphs, preferring neither', () => {
    const { editor, referenceMap } = makeTwoClauseDoc();
    const r = resolveAnchoredSpans(editor, { target_para_id: 'STAB0041', target_ref: 'clause 4.2' }, referenceMap);
    expect(r).toEqual({ ok: false, kind: 'ambiguous', matchCount: 2 });
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// materialize — the anchor actually places the redline
// ---------------------------------------------------------------------------
describe('usePendingRedline — anchored materialize (FR-C01/C02)', () => {
  it('places on the anchored paragraph even though the same target_text is ambiguous', () => {
    const { editor, referenceMap } = makeTwoClauseDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize(
        {
          // Ambiguous on its own: "shall indemnify" appears in BOTH clauses, so the legacy path would
          // have refused this edit outright. The anchor is what makes it placeable.
          target_text: 'shall indemnify',
          match_mode: 'strict',
          new_text: 'shall fully indemnify',
          target_para_id: 'STAB0042',
        },
        PROV
      );
    });

    expect(status).toBe('applied');
    expect(result.current.error).toBeNull();
    // The strike landed in clause 4.2 and nothing happened to 4.1.
    expect(textOf(editor, 'STAB0042')).toContain('shall fully indemnify');
    expect(textOf(editor, 'STAB0041')).not.toContain('fully indemnify');
    editor.destroy();
  });

  it('places on the clause a citation names, with no target_text supplied at all', () => {
    const { editor, referenceMap } = makeTwoClauseDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materialize({ new_text: 'A rewritten indemnity.', target_ref: 'clause 4.1' }, PROV);
    });

    expect(result.current.error).toBeNull();
    expect(textOf(editor, 'STAB0041')).toContain('A rewritten indemnity.');
    expect(textOf(editor, 'STAB0042')).not.toContain('A rewritten indemnity.');
    editor.destroy();
  });

  it('an unresolvable anchor is refused and surfaced — it does NOT fall back to searching target_text', () => {
    const { editor, referenceMap } = makeTwoClauseDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize(
        {
          // This target_text resolves UNIQUELY. If the anchor failure fell through to the text path,
          // the edit would be placed and this test would report 'applied' — that is the regression.
          target_text: 'Clause 4.1',
          match_mode: 'strict',
          new_text: 'REWRITTEN',
          target_para_id: 'DEADBEEF',
        },
        PROV
      );
    });

    // Task 052: a resolvable-but-absent paraId is `target_deleted`, not the generic `not_found`.
    expect(status).toBe('target_deleted');
    expect(result.current.error?.kind).toBe('target_deleted');
    expect(textOf(editor, 'STAB0041')).not.toContain('REWRITTEN');
    editor.destroy();
  });

  it('names the anchor in the banner when there is no target_text to quote', () => {
    const { editor, referenceMap } = makeTwoClauseDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    act(() => {
      result.current.materialize({ new_text: 'X', target_ref: 'clause 91' }, PROV);
    });

    // An empty quoted string would make the banner claim nothing was targeted when something very
    // specific was.
    expect(result.current.error?.targetText).toBe('clause 91');
    editor.destroy();
  });

  it('un-anchored edits take the BOUNDED fallback: proposed, nothing placed until confirmed (task 053)', () => {
    const { editor, referenceMap } = makeTwoClauseDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize(
        { target_text: 'Clause 4.2', match_mode: 'strict', new_text: 'Clause 4.2 (revised)' },
        PROV
      );
    });

    // Task 052 left this leg pinned to `strict` and auto-applying. FR-C06 (task 053) bounded it: an
    // anchorless replayed entry PROPOSES and places nothing until a human answers.
    expect(status).toBe('proposed');
    expect(textOf(editor, 'STAB0042')).not.toContain('Clause 4.2 (revised)');
    expect(result.current.legacyProposal).toMatchObject({ ledgerRef: PROV.ledgerRef, quotedTarget: 'Clause 4.2' });

    // ...and the confirmation places exactly what the proposal showed.
    act(() => {
      result.current.applyLegacyProposal();
    });
    expect(status).toBe('proposed');
    expect(textOf(editor, 'STAB0042')).toContain('Clause 4.2 (revised)');
    expect(result.current.legacyProposal).toBeNull();
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// materializeMany — the whole-document change list takes the same ordering
// ---------------------------------------------------------------------------
describe('usePendingRedline — anchored change list (materializeMany)', () => {
  it('applies each change at its own anchor, mixing anchored and legacy entries', () => {
    const { editor, referenceMap } = makeTwoClauseDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let statuses: string[] = [];
    act(() => {
      statuses = result.current.materializeMany(
        [
          { new_text: 'ANCHORED-BY-ID', target_para_id: 'STAB0041' },
          { new_text: 'ANCHORED-BY-REF', target_ref: 'clause 4.2' },
        ],
        PROV
      );
    });

    expect(statuses).toEqual(['applied', 'applied']);
    expect(textOf(editor, 'STAB0041')).toContain('ANCHORED-BY-ID');
    expect(textOf(editor, 'STAB0042')).toContain('ANCHORED-BY-REF');
    editor.destroy();
  });

  it('skips only the change whose anchor fails, and keeps the rest — one bad anchor is not a batch failure', () => {
    const { editor, referenceMap } = makeTwoClauseDoc();
    const { result } = renderHook(() => usePendingRedline(editor, referenceMap));

    let statuses: string[] = [];
    act(() => {
      statuses = result.current.materializeMany(
        [
          { new_text: 'GOOD', target_para_id: 'STAB0041' },
          { new_text: 'BAD', target_para_id: 'DEADBEEF' },
        ],
        PROV
      );
    });

    // Task 052: the dead paraId is `target_deleted` (FR-C05 outcome 3), not the generic `not_found`.
    expect(statuses).toEqual(['applied', 'target_deleted']);
    expect(textOf(editor, 'STAB0041')).toContain('GOOD');
    expect(result.current.error?.failedCount).toBe(1);
    editor.destroy();
  });
});
