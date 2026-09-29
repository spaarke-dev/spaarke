/**
 * composeAnchorResolution.ts — THE deterministic anchor precedence (spaarkeai-compose-r8 task 055).
 *
 * One rule, three consumers. An AI-produced object that names a document target deterministically
 * does it in exactly two vocabularies — an explicit `w14:paraId` (the address itself) or a legal
 * citation ("clause 4.2") resolved through the projection's `paraIdMap`. Before this module each
 * consumer decided that precedence for itself:
 *
 *  - `usePendingRedline.resolveAnchoredSpans` (task 051) — the AI EDIT path (`edits[]`);
 *  - `ComposeEditor.placeAdvisoryComments` → `resolveDeterministicAnchorSpan` (agreements-r1 task
 *    011) — the NDA/agreement-review advisory-comment path (`sectionRef`);
 *  - `ComposeWorkspace.registerAiReviewComments` (task 055) — the whole-document REVIEW FLAG path
 *    (`comments[]`, the `flag-risks` intent's entire output), which is the third consumer and the
 *    reason this was hoisted rather than copied a third time.
 *
 * WHY A MODULE AND NOT AN EXTENSION OF AN EXISTING ONE (CLAUDE.md §11 three-question template):
 *  1. Existing — `composeCitationResolver.resolveCitation` answers "which paragraphs does this
 *     citation name"; it knows nothing about `paraId` and nothing about precedence when BOTH are
 *     supplied. `resolveAnchoredSpans` has the precedence but is editor-coupled (it returns
 *     ProseMirror spans) and lives in a hook module; `resolveDeterministicAnchorSpan` is a private
 *     function inside a ~4,000-line component. Neither is importable by the third consumer.
 *  2. Extension — extending `composeCitationResolver` would put paraId precedence inside the module
 *     whose whole contract is "mirror of the server `CitationResolver`" (a parity contract that must
 *     not grow client-only semantics). Extending `usePendingRedline` would make `ComposeWorkspace`
 *     import a React hook module for a pure function. So: hoist the precedence, keep the SPAN policy
 *     at each call site (an edit addresses exactly ONE paragraph; an advisory comment legitimately
 *     spans a range — see `resolveDeterministicAnchorSpan`).
 *  3. Cost of doing nothing — three sites deciding precedence independently. Concretely: an object
 *     carrying BOTH a `target_para_id` and a DISAGREEING `target_ref` is refused as ambiguous on the
 *     edit path today, and would have been silently placed at the citation on the flag path, because
 *     the flag path's own copy would have been written fresh. That is UAT-21's "never silently
 *     mis-place" failing on the highest-volume whole-document capability.
 *
 * What this module deliberately does NOT do: touch the editor, resolve spans, or decide whether an
 * unresolved anchor refuses or falls through. Those are per-consumer policies.
 *
 * @see ./composeCitationResolver.ts — citation → paraId(s) (the server `CitationResolver` mirror).
 * @see ./hooks/usePendingRedline.ts — `resolveAnchoredSpans` (edit path: one paragraph, refuses ranges).
 * @see ./ComposeEditor.tsx — `resolveDeterministicAnchorSpan` (advisory path: spans ranges).
 * @see ./ComposeWorkspace.tsx — `registerAiReviewComments` (review-flag path: annotation anchor).
 */
import { resolveCitation } from './composeCitationResolver';
import type { ParaIdMapEntry } from '../types/compose-contracts';

/**
 * The two deterministic vocabularies, normalized. Callers map their own wire names onto these
 * (`target_para_id`/`target_ref` on the AI edit + flag payloads, `paraId`/`sectionRef` on
 * {@link AdvisoryCommentInput}) so each boundary keeps its own contract's spelling.
 */
export interface AnchorRequest {
  /**
   * The exact `w14:paraId` the object targets. Outranks {@link ref}: it IS the address.
   *
   * `null` is part of the WIRE shape (r8 task 053b): the compose Action output schemas declare
   * `target_para_id` as `["string","null"]` and REQUIRE the key, so an unidentified target arrives as
   * an explicit null. `resolveAnchorParaIds` reads it with `?.trim()`, so a null resolves to
   * {@link AnchorParaIdResolution} `none` — "this object named no paragraph", which is the correct
   * answer for THIS function. Deciding what to DO about a null that was ASKED for is a per-consumer
   * policy and deliberately does not live here (the edit path proposes; the annotation paths fall
   * back to prose).
   */
  paraId?: string | null;
  /** The target named as a legal citation ("clause 4.2", "4.2(b)(iii)", "Sections 4-7"). */
  ref?: string | null;
}

/**
 * The outcome of resolving an {@link AnchorRequest} to paragraph id(s).
 *
 * `resolved` carries EVERY paragraph named, in document order — a single-paragraph consumer refuses
 * a multi-entry result, a range-capable one spans it. `none` means no anchor was supplied at all,
 * which is the caller's signal that a legacy text path is permitted; every OTHER outcome is a
 * refusal, and a refusal MUST NOT be converted into a text search (UAT-21).
 */
export type AnchorParaIdResolution =
  | { kind: 'none' }
  | { kind: 'resolved'; paraIds: readonly string[] }
  | { kind: 'not_found' }
  | { kind: 'ambiguous'; matchCount: number };

/** Ordinal-insensitive paraId comparison — the ids are hex strings whose casing varies by producer. */
function sameParaId(a: string, b: string): boolean {
  return a.toUpperCase() === b.toUpperCase();
}

/**
 * Resolve a deterministic anchor to the paragraph id(s) it names.
 *
 * Precedence (identical to the server `ComposeAnchorResolver`/`ComposeEditAnchorPass` contract):
 *  1. An explicit `paraId` is the address. It needs no reference map — the map is the numbering
 *     projection, not the id universe, and a paraId minted this session may not be in it.
 *  2. A `ref` resolves through the reference map. No map, or a citation the map does not name, is a
 *     REFUSAL (`not_found`) — never a fallback to searching prose.
 *  3. Both present: they must agree. A citation naming exactly the same single paragraph
 *     corroborates the paraId; anything else (a different paragraph, or a RANGE alongside a single
 *     paraId) is `ambiguous` with NEITHER preferred, because preferring one is the guess this whole
 *     mechanism exists to remove.
 */
export function resolveAnchorParaIds(
  anchor: AnchorRequest | undefined,
  referenceMap: readonly ParaIdMapEntry[] | undefined
): AnchorParaIdResolution {
  const paraId = anchor?.paraId?.trim();
  const ref = anchor?.ref?.trim();
  if (!paraId && !ref) return { kind: 'none' };

  let refParaIds: readonly string[] | null = null;
  if (ref) {
    if (!referenceMap || referenceMap.length === 0) return { kind: 'not_found' };
    const resolution = resolveCitation(ref, referenceMap);
    if (resolution.matches.length === 0) return { kind: 'not_found' };
    refParaIds = resolution.matches.map(m => m.paraId);
  }

  if (paraId && refParaIds) {
    if (refParaIds.length === 1) {
      return sameParaId(refParaIds[0], paraId)
        ? { kind: 'resolved', paraIds: [paraId] }
        : { kind: 'ambiguous', matchCount: 2 };
    }
    // A range citation next to a single explicit paraId: the two describe different-sized targets
    // and there is no principled way to pick. Refuse, exactly as the edit path already did.
    return { kind: 'ambiguous', matchCount: refParaIds.length };
  }

  return { kind: 'resolved', paraIds: paraId ? [paraId] : refParaIds! };
}

export default resolveAnchorParaIds;
