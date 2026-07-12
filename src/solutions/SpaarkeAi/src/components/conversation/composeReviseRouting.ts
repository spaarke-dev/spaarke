/**
 * composeReviseRouting.ts — pure whole-document revise (DEF-11) disambiguation routing.
 *
 * Mirrors `summarizeRouting.ts`'s `routeSummarizeIntent` shape/style: a deterministic, TOTAL,
 * side-effect-free function of the raw message text alone — no IO, no React, no server round-trip.
 * Same slash-command mechanics as `/summarize` (a closed, structured trigger), not free-text NLP
 * intent detection — ADR-039 keeps CAPABILITY routing server-owned; this module decides UX
 * PRESENTATION only (whether to show the two-path disambiguation, never which Binding runs).
 *
 * `/revise` alone (or followed by text that isn't one of the three named DEF-11 intents) is
 * AMBIGUOUS — the caller should present the disambiguation copy (two paths: highlight a section
 * directly, or describe the whole-document revision) alongside the four next-step suggestions.
 * `/revise <intent> [instruction...]` already specifies an intent — the caller skips disambiguation
 * and can dispatch directly (see `ConversationPane.dispatchComposeAction` — the bindingId itself
 * still comes from capability discovery / the dispatch args, never from this module).
 *
 * @see ./summarizeRouting.ts — the `/summarize` precedent this mirrors
 * @see ../../../client/shared/Spaarke.Compose.Components — DEF-11 `compose-revise-document` contract
 *      (revisionIntent enum: align-clauses | flag-risks | improve-clarity | custom)
 */

/** The four DEF-11 `compose-revise-document` revision intents (contract-closed enum). */
export type RevisionIntent = 'align-clauses' | 'flag-risks' | 'improve-clarity' | 'custom';

/** All four intents, in the presentation order the disambiguation chips/copy use. */
export const REVISION_INTENTS: readonly RevisionIntent[] = [
  'align-clauses',
  'flag-risks',
  'improve-clarity',
  'custom',
];

/** Trigger prefix for the `/revise` slash command (case-insensitive, mirrors `/summarize`). */
export const REVISE_SLASH_PREFIX = '/revise';

/** One suggested next-step for the disambiguation UX — label + the intent it carries. */
export interface RevisionIntentSuggestion {
  readonly revisionIntent: RevisionIntent;
  readonly label: string;
}

/**
 * The four revision-intent suggestions, in presentation order. A real deployment renders these as
 * suggestion chips via the EXISTING chip surface (`useConsumerChips`) once a `compose-revise-document`
 * Binding id is resolvable client-side (capability discovery — out of this pure module's concern,
 * same boundary `ComposeAiToolbar`'s bindingId-registration seam already documents). Until then, the
 * text fallback below (`REVISE_DISAMBIGUATION_MESSAGE`) carries the same four options as typed
 * `/revise <intent>` follow-ups, which `routeReviseIntent` parses deterministically.
 */
export const REVISION_INTENT_SUGGESTIONS: readonly RevisionIntentSuggestion[] = [
  { revisionIntent: 'align-clauses', label: 'Align clauses for consistency' },
  { revisionIntent: 'flag-risks', label: 'Flag risks (no rewrite)' },
  { revisionIntent: 'improve-clarity', label: 'Improve clarity' },
  { revisionIntent: 'custom', label: 'Custom instructions' },
];

/**
 * Deterministic two-path disambiguation copy (DEF-11 UX): highlight-a-section vs whole-document.
 * Emitted verbatim as a local Assistant interjection when `routeReviseIntent` returns `disambiguate`.
 * Do NOT change this wording without updating spec.md (mirrors the `/summarize` prompt-first
 * interjection's spec-driven-string convention).
 */
export const REVISE_DISAMBIGUATION_MESSAGE =
  "I can revise this two ways: highlight the section(s) you want changed directly in the document " +
  '(a targeted edit), or tell me how you\'d like the WHOLE document revised. For the whole document, ' +
  'reply with one of: `/revise align-clauses`, `/revise flag-risks`, `/revise improve-clarity`, or ' +
  '`/revise <describe the change>` for a custom instruction.';

/**
 * Discriminated routing decision returned by {@link routeReviseIntent}.
 *
 * - `not-revise`        → not a `/revise` invocation; pass through unchanged.
 * - `disambiguate`       → bare `/revise` (no intent, no instruction) — present the two-path
 *   disambiguation copy + the four suggestions; do NOT dispatch yet.
 * - `intent-specified`   → `/revise <intent> [instruction...]` — one of the three named intents
 *   (`align-clauses` / `flag-risks` / `improve-clarity`) recognized verbatim as the first token, or
 *   free text after `/revise` that isn't one of those three (routed as `custom` with the free text as
 *   `instruction`). Skip disambiguation; the caller may dispatch directly.
 */
export type ReviseRouteDecision =
  | { kind: 'not-revise'; messageText: string }
  | {
      kind: 'intent-specified';
      messageText: string;
      revisionIntent: RevisionIntent;
      instruction?: string;
    }
  | { kind: 'disambiguate'; messageText: string; interjection: string };

/**
 * Pure tri-branch routing decision for `/revise` (DEF-11 whole-document revision UX), mirroring
 * `routeSummarizeIntent`'s style. Deterministic and total.
 */
export function routeReviseIntent(messageText: string): ReviseRouteDecision {
  const trimmed = messageText.trim();
  const isRevise =
    trimmed.length >= REVISE_SLASH_PREFIX.length &&
    trimmed.slice(0, REVISE_SLASH_PREFIX.length).toLowerCase() === REVISE_SLASH_PREFIX;

  if (!isRevise) {
    return { kind: 'not-revise', messageText: trimmed };
  }

  const rest = trimmed.slice(REVISE_SLASH_PREFIX.length).trim();
  if (rest.length === 0) {
    return { kind: 'disambiguate', messageText: trimmed, interjection: REVISE_DISAMBIGUATION_MESSAGE };
  }

  const spaceIdx = rest.search(/\s/);
  const firstToken = (spaceIdx === -1 ? rest : rest.slice(0, spaceIdx)).toLowerCase();
  const remainder = (spaceIdx === -1 ? '' : rest.slice(spaceIdx + 1)).trim();

  const namedIntent = REVISION_INTENTS.find(i => i === firstToken && i !== 'custom');
  if (namedIntent) {
    return {
      kind: 'intent-specified',
      messageText: trimmed,
      revisionIntent: namedIntent,
      instruction: remainder.length > 0 ? remainder : undefined,
    };
  }

  // Free text after `/revise` that isn't one of the three named intents — the user already told us
  // HOW (just not via a named intent keyword); route as `custom` with the whole remainder as
  // instruction. Never ambiguous once ANY text follows `/revise`.
  return { kind: 'intent-specified', messageText: trimmed, revisionIntent: 'custom', instruction: rest };
}
