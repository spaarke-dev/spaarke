/**
 * anchorlessReplayFallback.ts — FR-C06's BOUNDED CONFIRMABLE FALLBACK (spaarkeai-compose-r8 task 053).
 *
 * ─────────────────────────────────────────────────────────────────────────────────────────────────
 * WHAT THIS IS FOR, AND WHAT IT MUST NEVER BECOME
 * ─────────────────────────────────────────────────────────────────────────────────────────────────
 *
 * Task 051 gave every compose EDIT Action a deterministic anchor (`target_para_id` / `target_ref`).
 * Task 052 retired `target_text` / `match_mode` from those four Actions' output schemas, so no NEWLY
 * produced edit can be anchorless. What remains is one bounded, *shrinking* population:
 *
 *   **`compose`-disposition ledger entries written BEFORE that catalog change, replayed afterwards.**
 *
 * A Compose session's outputs are durable (ADR-040; Cosmos `sessions`, 90-day default retention,
 * indefinite when filed — `StoredSession.Ttl`), and `ComposeWorkspace.materializeComposeDraftFromLedger`
 * re-materializes the head compose output on every document open, refresh and Flow-5 signal. So an edit
 * proposed against the OLD schema is replayed against the NEW client for as long as its session lives.
 * Those payloads carry prose (`target_text`) and no anchor. They are the ONLY input this module accepts.
 *
 * FR-C06's bar for them is not "match harder", it is **"propose, never place"**:
 *
 *   > Tolerant matching survives for those ONLY as a bounded fallback: low confidence produces a
 *   > PROPOSED placement the user confirms. Never auto-apply. Never for a source that has a real anchor.
 *
 * The risk this module carries — named explicitly in task 053's POML — is becoming the back door for
 * what task 052 just closed. Two STRUCTURAL bounds hold it shut; neither is a comment or a convention:
 *
 *  **BOUND 1 — an anchored edit cannot be expressed in this module's input type.**
 *  {@link resolveAnchorlessReplay} accepts an {@link AnchorlessReplayTarget}, a branded type whose brand
 *  symbol is module-private. {@link classifyAnchorlessReplay} is its only mint, and it returns `null`
 *  the moment a payload carries `target_para_id` or `target_ref`. An anchored payload therefore cannot
 *  be turned into an argument this module will take.
 *
 *  **BOUND 2 — this module has no "applied" outcome to return.**
 *  {@link AnchorlessReplayOutcome} is `proposed | unresolved`. There is no member a caller could act on
 *  as a completed placement, so "auto-apply" is not a branch someone forgot to guard — it is a state the
 *  type system cannot express. The spans a proposal carries are only ever placed by the caller AFTER the
 *  user confirms (`usePendingRedline`'s `confirmed: 'anchorless-replay'` gate, the single route through).
 *
 * ─────────────────────────────────────────────────────────────────────────────────────────────────
 * WHY THIS REUSES `resolveTargetSpans` AND ADDS NO NEW MATCHER (root CLAUDE.md §11)
 * ─────────────────────────────────────────────────────────────────────────────────────────────────
 *
 * `resolveTargetSpans` (in `./redlineTextSearch`) IS the tolerant matcher: a 1:1 typographic fold
 * (curly quotes / NBSP / en-em dashes) with a whitespace-collapse + invisible-strip second pass. FR-C06
 * says tolerant matching *survives* as a bounded fallback — it does not ask for a new scorer, and a new
 * one would fail §11's three questions:
 *
 *   1. **Existing** — `resolveTargetSpans` already folds; `AnnotationReanchorService` already scores the
 *      return-from-Word case server-side.
 *   2. **Extension** — a scored/paraphrase mode would be an extension of `resolveTargetSpans`, not a new
 *      component; but see (3).
 *   3. **Cost-of-doing-nothing** — a legacy entry whose quoted prose no longer folds to anything in the
 *      document gets an honest, specific "re-run this suggestion" message, and re-running it produces a
 *      properly anchored edit. That is a complete remedy, one click, on a population that expires with
 *      its session. A paraphrase scorer would buy a marginally shorter path for a shrinking cohort at
 *      the cost of a permanent, more-tolerant placement channel — precisely the back door task 052 shut.
 *
 * The mode is pinned to `strict` for the same reason task 052 pinned it: `first` picks an occurrence
 * when several match, which IS the UAT-21 silent mis-placement; `all` is retired in full
 * (`notes/052-text-search-demotion-decisions.md` §2). Ambiguity is REFUSED, never proposed — showing the
 * user one of three candidates and asking them to confirm it dresses a guess up as a decision.
 *
 * @see ./redlineTextSearch.ts — the tolerant matcher this module bounds (and its three surviving
 *      annotation/decoration consumers, which are NOT placements and are untouched).
 * @see ./usePendingRedline.ts — the ONLY caller. `planAndApplyTargeted` is the single call site.
 * @see ../composeAnchorResolution.ts — the deterministic anchor path this is the fallback for.
 * @see projects/spaarkeai-compose-r8/notes/wording-differs-elimination-trace.md
 */
import type { Editor } from '@tiptap/core';

import { resolveTargetSpans } from './redlineTextSearch';
import type { RedlineMatchMode, RedlineSpan } from './redlineTextSearch';

/**
 * The mode the anchorless replay leg is pinned to (carried over from task 052's
 * `LEGACY_REPLAY_MATCH_MODE`). Named so the pin is a decision, not an argument literal:
 *
 *  - `all` is retired in full — an over-applied defined-term sweep is an INVISIBLE wrong edit, and
 *    user-invoked find/replace (`useComposeFindReplace`) already does that job with a visible count;
 *  - `first` picks an occurrence when several match — the UAT-21 failure, verbatim;
 *  - `strict` REFUSES rather than guesses, so pinning can only convert a would-be guess into an honest
 *    refusal. A replayed entry can never land somewhere `strict` would not have put it.
 */
const REPLAY_MATCH_MODE: RedlineMatchMode = 'strict';

/**
 * Module-private brand. NOT exported — a caller in another module cannot name it, so it cannot build a
 * conforming object literal, and the only supported way to obtain an {@link AnchorlessReplayTarget} is
 * {@link classifyAnchorlessReplay}.
 */
declare const ANCHORLESS_REPLAY_BRAND: unique symbol;

/**
 * A payload PROVEN to carry no deterministic anchor and to carry replayable prose — BOUND 1's carrier.
 *
 * The brand exists so `resolveAnchorlessReplay`'s signature, not a comment, is what stops an anchored
 * edit reaching the text search. There is deliberately no public constructor.
 */
export interface AnchorlessReplayTarget {
  /** @internal Brand — module-private; see {@link classifyAnchorlessReplay}. */
  readonly [ANCHORLESS_REPLAY_BRAND]: true;
  /** The prose the replayed suggestion quoted as its target (Tier 3 — never logged). */
  readonly quotedTarget: string;
}

/** The shape {@link classifyAnchorlessReplay} inspects — the anchor fields plus the legacy prose field. */
export interface AnchorlessReplayCandidatePayload {
  target_para_id?: string | null;
  target_ref?: string | null;
  target_text?: string | null;
}

/**
 * BOUND 1 — the ONLY mint of {@link AnchorlessReplayTarget}.
 *
 * Returns `null` — meaning "this module is not applicable" — for:
 *  - any payload carrying a deterministic anchor (`target_para_id` or `target_ref`, in any resolvable
 *    state). An anchor that is PRESENT and does not resolve is REFUSED upstream, never searched: that is
 *    the anchored path's own contract (`resolveAnchoredSpans`), and re-searching here would hand exactly
 *    the edits that named their target precisely back to the wrong-occurrence risk;
 *  - any payload with no prose to replay (nothing to do — the caller treats it as an insertion-style
 *    draft, unchanged behaviour).
 *
 * Whitespace-only prose counts as no prose: `resolveTargetSpans` would return `not_found` for it anyway,
 * and refusing here keeps the "a proposal always has something to show the user" property.
 */
export function classifyAnchorlessReplay(
  payload: AnchorlessReplayCandidatePayload | null | undefined
): AnchorlessReplayTarget | null {
  // An anchor of ANY kind disqualifies the payload — this is the structural half of
  // "never for a source that has a real anchor" (FR-C06).
  if (typeof payload?.target_para_id === 'string' && payload.target_para_id.trim().length > 0) return null;
  if (typeof payload?.target_ref === 'string' && payload.target_ref.trim().length > 0) return null;

  const quotedTarget = typeof payload?.target_text === 'string' ? payload.target_text : '';
  if (quotedTarget.trim().length === 0) return null;

  return { quotedTarget } as AnchorlessReplayTarget;
}

/**
 * BOUND 2 — the outcome vocabulary. There is no `applied` member, by design: this module can propose or
 * refuse, and nothing else. Whatever the caller does with a proposal happens behind the user's explicit
 * confirmation, in `usePendingRedline`.
 */
export type AnchorlessReplayOutcome =
  | {
      kind: 'proposed';
      /** The span(s) the proposal WOULD occupy — inert until the user confirms. */
      readonly spans: readonly RedlineSpan[];
      /** Tier 3 — the document text the proposal would strike, as it reads now. Shown truncated. */
      readonly matchedText: string;
      /** Tier 3 — the prose the replayed suggestion quoted as its target. Shown truncated. */
      readonly quotedTarget: string;
    }
  | {
      kind: 'unresolved';
      /**
       * `not_found` — the quoted prose is not in the document (the honest end of the road for a replayed
       * suggestion: re-run it and get an anchored one). `ambiguous` — it occurs more than once, and
       * proposing one occurrence would be UAT-21 wearing a confirmation dialog.
       */
      readonly reason: 'not_found' | 'ambiguous';
      readonly matchCount: number;
      readonly quotedTarget: string;
    };

/**
 * Resolve a replayed, anchorless suggestion to a PROPOSED placement — or refuse.
 *
 * The parameter type is the bound: an anchored edit cannot be minted into an
 * {@link AnchorlessReplayTarget}, so it cannot be passed here. See the file header.
 */
export function resolveAnchorlessReplay(editor: Editor, target: AnchorlessReplayTarget): AnchorlessReplayOutcome {
  const { quotedTarget } = target;
  const resolved = resolveTargetSpans(editor, quotedTarget, REPLAY_MATCH_MODE);

  if (!resolved.ok) {
    return { kind: 'unresolved', reason: resolved.kind, matchCount: resolved.matchCount, quotedTarget };
  }

  // `strict` guarantees exactly one span here; read the document text it covers so the confirmation can
  // show the user what would actually be struck rather than only what the model quoted.
  const span = resolved.spans[0];
  const matchedText = editor.state.doc.textBetween(span.from, span.to, ' ', ' ');
  return { kind: 'proposed', spans: resolved.spans, matchedText, quotedTarget };
}
