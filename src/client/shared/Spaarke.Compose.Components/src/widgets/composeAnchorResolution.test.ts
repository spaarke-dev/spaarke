/**
 * composeAnchorResolution.test.ts — the ONE deterministic anchor precedence (r8 task 055).
 *
 * Three call sites now name a document target deterministically: the edit path
 * (`usePendingRedline.resolveAnchoredSpans`), the advisory-comment path
 * (`ComposeEditor.placeAdvisoryComments`) and the whole-document review-flag path
 * (`ComposeWorkspace.registerAiReviewComments`). This module is the single place the
 * paraId-vs-citation PRECEDENCE lives, so it cannot drift between them; each caller keeps
 * its own SPAN policy (an edit addresses one paragraph, an advisory comment may span a range).
 */
import { resolveAnchorParaIds } from './composeAnchorResolution';
import type { ParaIdMapEntry } from '../types/compose-contracts';

function entry(index: number, paraId: string, computedNumber: string, listPath: number[]): ParaIdMapEntry {
  return { index, paraId, isMinted: false, computedNumber, listPath };
}

/**
 * Sections 4 / 4.1 / 4.2 / 5 — enough for a SINGLE citation, a SUB-ITEM citation, and a top-level
 * RANGE ("Sections 4-5", whose shipped semantics sweep every paragraph whose first ordinal falls in
 * the range, sub-items included) to resolve to genuinely different match sets.
 */
const MAP: ParaIdMapEntry[] = [
  entry(0, 'STAB0040', '4', [4]),
  entry(1, 'STAB0041', '4.1', [4, 1]),
  entry(2, 'STAB0042', '4.2', [4, 2]),
  entry(3, 'STAB0050', '5', [5]),
];

describe('resolveAnchorParaIds — the shared deterministic anchor precedence', () => {
  it('returns `none` when no anchor is supplied — the ONLY route back to a text path', () => {
    expect(resolveAnchorParaIds(undefined, MAP)).toEqual({ kind: 'none' });
    expect(resolveAnchorParaIds({}, MAP)).toEqual({ kind: 'none' });
    expect(resolveAnchorParaIds({ paraId: '   ', ref: '  ' }, MAP)).toEqual({ kind: 'none' });
  });

  it('takes an explicit paraId as the address itself — no map consultation required', () => {
    expect(resolveAnchorParaIds({ paraId: 'STAB0042' }, undefined)).toEqual({
      kind: 'resolved',
      paraIds: ['STAB0042'],
    });
  });

  it('resolves a citation through the reference map', () => {
    expect(resolveAnchorParaIds({ ref: 'clause 4.2' }, MAP)).toEqual({
      kind: 'resolved',
      paraIds: ['STAB0042'],
    });
  });

  it('returns EVERY paragraph a range citation names, in document order — the range policy is the caller’s', () => {
    const r = resolveAnchorParaIds({ ref: 'Sections 4-5' }, MAP);
    expect(r).toEqual({ kind: 'resolved', paraIds: ['STAB0040', 'STAB0041', 'STAB0042', 'STAB0050'] });
  });

  it('refuses a citation with no reference map to validate it against — never guesses', () => {
    expect(resolveAnchorParaIds({ ref: 'clause 4.2' }, undefined)).toEqual({ kind: 'not_found' });
    expect(resolveAnchorParaIds({ ref: 'clause 4.2' }, [])).toEqual({ kind: 'not_found' });
  });

  it('refuses a citation that names nothing in the map', () => {
    expect(resolveAnchorParaIds({ ref: 'clause 99.9' }, MAP)).toEqual({ kind: 'not_found' });
  });

  it('corroborates a paraId with an agreeing citation (case-insensitive) and resolves to the paraId', () => {
    expect(resolveAnchorParaIds({ paraId: 'stab0042', ref: '4.2' }, MAP)).toEqual({
      kind: 'resolved',
      paraIds: ['stab0042'],
    });
  });

  it('refuses two anchors that name DIFFERENT paragraphs, preferring neither', () => {
    expect(resolveAnchorParaIds({ paraId: 'STAB0041', ref: '4.2' }, MAP)).toEqual({
      kind: 'ambiguous',
      matchCount: 2,
    });
  });

  it('refuses a paraId paired with a RANGE citation — the two name differently-sized targets', () => {
    expect(resolveAnchorParaIds({ paraId: 'STAB0041', ref: 'Sections 4-5' }, MAP)).toEqual({
      kind: 'ambiguous',
      matchCount: 4,
    });
  });

  it('refuses when the paraId is present but its paired citation resolves to nothing', () => {
    expect(resolveAnchorParaIds({ paraId: 'STAB0041', ref: 'clause 99.9' }, MAP)).toEqual({ kind: 'not_found' });
  });
});
