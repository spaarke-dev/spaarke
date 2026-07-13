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

// ---------------------------------------------------------------------------
// Natural-language "revise this document" detection (Wave 4 — end-to-end revise)
// ---------------------------------------------------------------------------
//
// The `/revise` slash above is a CLOSED, structured trigger (mirrors `/summarize`). This section
// adds detection of the NATURAL-LANGUAGE phrasing an attorney actually types about a CHAT-UPLOADED
// document — "revise this document", "review the contract", "flag risks in this agreement",
// "align this document to our playbook". This is still UX-PRESENTATION routing only (ADR-039):
// the function decides WHETHER to run the client-orchestrated mount+ask flow, never WHICH Binding
// runs (the `compose-revise-document` bindingId is resolved from capability discovery, never here)
// and never re-detects intent from the wire. The CALLER additionally gates on an active
// chat-uploaded source document being present — a bare phrasing with no uploaded doc falls through
// to the normal agent turn.

/** The mount-then-ask Assistant interjection shown after auto-mounting the chat-uploaded doc. */
export const REVISE_MOUNT_ASK_MESSAGE =
  'Your file has been loaded into Compose. What type of revision do you have in mind?';

/** One clickable revise-intent chip option — the four DEF-11 intents in owner-confirmed wording. */
export interface ReviseChipOption {
  readonly revisionIntent: RevisionIntent;
  readonly label: string;
}

/** The four revise-intent chip options rendered after the mount-then-ask message (owner wording). */
export const REVISE_CHIP_OPTIONS: readonly ReviseChipOption[] = [
  { revisionIntent: 'align-clauses', label: 'Align to playbook' },
  { revisionIntent: 'flag-risks', label: 'Flag risks' },
  { revisionIntent: 'improve-clarity', label: 'Improve clarity' },
  { revisionIntent: 'custom', label: 'Custom…' },
];

/** Result of {@link detectReviseThisDocumentIntent}. */
export interface ReviseThisDocumentDetection {
  /** True when the message is a natural-language "revise/review this document" request. */
  readonly isReviseThisDocument: boolean;
  /**
   * A named intent extracted from the ORIGINAL message ("flag risks in this document" → flag-risks;
   * "align this document to our playbook" → align-clauses; "make this clearer" → improve-clarity).
   * Undefined when the user did not name one — the caller shows the four intent chips and asks.
   */
  readonly namedIntent?: RevisionIntent;
}

// A revise/review verb. Deliberately excludes past tense ("reviewed", "revised") via word
// boundaries so "I reviewed the doc yesterday" does NOT trigger — only present-tense requests.
const REVISE_VERB_RE =
  /\b(revise|review|redline|red-line|mark[\s-]?up|edit|improve|revising|reviewing|editing|clean[\s-]?up|tighten)\b/i;

// A reference to the document under discussion (this/the/my/our + a document noun).
const DOC_REFERENCE_RE =
  /\b(this|the|my|our)\s+(document|doc|file|contract|agreement|draft|nda|brief|memo|clause|clauses|policy|text)\b/i;

/**
 * Extract a named revision intent from the original message, or undefined. Order matters — the most
 * specific signal wins. Conservative: only returns a named intent on a clear keyword match; anything
 * ambiguous returns undefined so the caller asks (renders the four chips).
 */
function extractNamedReviseIntent(text: string): RevisionIntent | undefined {
  const t = text.toLowerCase();
  if (/\brisk/.test(t) && /\b(flag|identif|spot|highlight|surface|find|call\s?out|assess|review)\b/.test(t)) {
    return 'flag-risks';
  }
  if (/\bplaybook\b/.test(t) || (/\balign\b/.test(t) && /\bclause/.test(t))) {
    return 'align-clauses';
  }
  if (/\b(clarity|clarif|clearer|readab|plain\s?language|simplif|concise)\b/.test(t)) {
    return 'improve-clarity';
  }
  return undefined;
}

/**
 * Pure, total, side-effect-free detection of a natural-language "revise this document" request. Does
 * NOT fire on `/`-prefixed slash commands (those route via {@link routeReviseIntent}) nor on empty
 * text. Requires BOTH a revise verb AND a document reference — the two-signal guard keeps false
 * positives low ("summarize this document", "explain the contract" have no revise verb; "please
 * revise your estimate" has no document reference). The CALLER additionally gates on an active
 * chat-uploaded source document.
 */
export function detectReviseThisDocumentIntent(messageText: string): ReviseThisDocumentDetection {
  const trimmed = messageText.trim();
  if (trimmed.length === 0 || trimmed.startsWith('/')) {
    return { isReviseThisDocument: false };
  }
  // Always require a document reference (the request is ABOUT the open document).
  if (!DOC_REFERENCE_RE.test(trimmed)) {
    return { isReviseThisDocument: false };
  }
  const namedIntent = extractNamedReviseIntent(trimmed);
  // Fire on EITHER a generic revise verb ("revise/review this document") OR a named-intent phrase
  // ("flag risks in this document", "align this document to our playbook") — the latter expresses a
  // revision request even without a bare revise verb.
  if (!REVISE_VERB_RE.test(trimmed) && !namedIntent) {
    return { isReviseThisDocument: false };
  }
  return { isReviseThisDocument: true, namedIntent };
}
