/**
 * intentMatcher.ts — R5 task 036 / P2-CLOSEOUT-05.
 *
 * Extensible, config-driven intent matcher for the chat-pane send funnel.
 *
 * Operator UX requirement (2026-06-05 SC-18 walkthrough):
 * routing should be **intent-driven, not surface-driven**. When the user's
 * message matches a registered intent (via slash command, keyword pattern, or
 * button-id) AND the session has held files, the dispatcher promotes the
 * held files via `POST /api/ai/chat/sessions/{id}/documents` and then runs
 * the action deterministically — bypassing the LLM for playbook selection.
 *
 * This module is PURE — no IO, no side effects, no React. Deterministic given
 * inputs. Trivially testable with plain values.
 *
 * Initial registry contains ONE entry (`summarize-session`) per operator
 * scope clarification: "let's keep this activity focused on the summarize use
 * case". The registry is shaped so that adding `create-matter` or
 * `add-to-dms` later is a config-only change.
 *
 * See `projects/spaarke-ai-platform-unification-r5/notes/task-036-design-2026-06-05.md`
 * §3.2 for the design rationale.
 *
 * @see ADR-028 — Auth v2; this module does NO IO, so no auth concerns
 * @see ADR-030 — PaneEventBus channels closed at 4; this module emits NO events
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Stable executor key. The dispatcher switches on this; do NOT rename without
 * updating the executor call sites (`executeSummarizeIntent.ts`).
 */
export type IntentExecutor = 'summarize-session';

/**
 * Configuration entry for a single intent. The registry is an array of these.
 *
 * Fields:
 * - `id`           — stable identifier; logged + emitted as telemetry key.
 * - `slash`        — exact slash trigger (lowercase, includes leading `/`). The
 *                    matcher accepts the slash command exactly OR with trailing
 *                    arguments separated by whitespace ("/summarize this").
 * - `patterns`     — case-insensitive RegExp variants (e.g. "please summarize").
 *                    Matched against the trimmed message text. ANY match counts.
 * - `buttonIds`    — host-supplied button identifiers (e.g. "action:summarize").
 *                    When the dispatcher passes `buttonId`, exact match counts.
 * - `requiresFiles`— if true, match is GATED on `hasFiles === true`. A matching
 *                    intent with `requiresFiles=true` and `hasFiles=false`
 *                    returns `null` (the dispatcher should fall through to the
 *                    upstream prompt-first / inline-attachment paths).
 * - `executor`     — the executor key consumed by the dispatcher to pick the
 *                    orchestrator implementation.
 */
export interface IntentMatcher {
  readonly id: string;
  readonly slash?: string;
  readonly patterns?: ReadonlyArray<RegExp>;
  readonly buttonIds?: ReadonlyArray<string>;
  readonly requiresFiles: boolean;
  readonly executor: IntentExecutor;
}

/**
 * Result of {@link matchIntent}. Carries the matched matcher id + executor
 * key and a discriminator describing HOW the match was made (slash / pattern /
 * button). Useful for telemetry.
 */
export interface IntentMatch {
  readonly id: string;
  readonly executor: IntentExecutor;
  readonly via: 'slash' | 'pattern' | 'button';
}

// ---------------------------------------------------------------------------
// Registry
// ---------------------------------------------------------------------------

/**
 * The Summarize intent. Matches:
 *   - Slash: `/summarize` (case-insensitive) with optional trailing arguments
 *     ("/summarize this please")
 *   - Patterns: "summarize", "summarise" (British spelling), "please summarize",
 *     and similar (case-insensitive, word-boundary aware)
 *   - Button ID: `action:summarize` (button-driven flows in SprkChat's
 *     predefined prompts surface, R5 task 019)
 *
 * Requires ≥ 1 held file (`requiresFiles: true`). When the user types
 * `/summarize` with zero held files, the matcher returns `null` and the
 * upstream `routeSummarizeIntent` prompt-first branch handles it (interjection
 * "Upload the file(s) you'd like me to summarize").
 */
const SUMMARIZE_INTENT: IntentMatcher = {
  id: 'summarize-session',
  slash: '/summarize',
  patterns: [
    // Word-boundary-anchored "summari[sz]e" — matches at start or after
    // optional polite prefix like "please ".
    /^summari[sz]e\b/i,
    /\bplease\s+summari[sz]e\b/i,
  ],
  buttonIds: ['action:summarize'],
  requiresFiles: true,
  executor: 'summarize-session',
};

/**
 * The intent registry. ORDERED — first match wins. Today there is exactly
 * ONE entry. Future R5 closeout / R6 tasks can append additional matchers
 * (create-matter, add-to-dms, etc.) without changing the matcher engine.
 */
export const IntentMatchers: ReadonlyArray<IntentMatcher> = [SUMMARIZE_INTENT];

// ---------------------------------------------------------------------------
// Matching
// ---------------------------------------------------------------------------

/**
 * Pure, deterministic match function.
 *
 * Match precedence (per matcher entry, first-match wins):
 *   1. `buttonId` exact match against `buttonIds`
 *   2. Slash exact / prefix match against `slash` (with optional trailing args)
 *   3. RegExp match against `patterns`
 *
 * If any matcher reports a match BUT requires files (`requiresFiles=true`)
 * and `hasFiles=false`, the match is suppressed (returns `null`). This lets
 * upstream prompt-first interjections handle the "user typed /summarize with
 * no files" case.
 *
 * @param messageText  Trimmed (caller-trimmed) message text from the user.
 *                     The matcher re-trims defensively.
 * @param hasFiles     Whether the session has ≥ 1 held file ready for promotion.
 * @param buttonId     Optional button identifier when the message was generated
 *                     by a predefined prompt button click (e.g. "action:summarize").
 * @returns The matching intent (with the discriminator describing HOW it
 *          matched), or `null` if no matcher matches OR a matcher matched but
 *          its `requiresFiles` gate failed.
 */
export function matchIntent(
  messageText: string,
  hasFiles: boolean,
  buttonId?: string
): IntentMatch | null {
  const trimmed = messageText.trim();

  for (const matcher of IntentMatchers) {
    let via: IntentMatch['via'] | null = null;

    // Precedence 1: button-id (exact, case-sensitive). Button ids are
    // generated by the chat host and never localized.
    if (buttonId && matcher.buttonIds && matcher.buttonIds.includes(buttonId)) {
      via = 'button';
    }

    // Precedence 2: slash command (case-insensitive). Accept exact match
    // OR slash followed by whitespace + arguments ("/summarize this").
    if (via === null && matcher.slash && trimmed.length > 0) {
      const lower = trimmed.toLowerCase();
      const slashLower = matcher.slash.toLowerCase();
      if (lower === slashLower) {
        via = 'slash';
      } else if (
        lower.length > slashLower.length &&
        lower.startsWith(slashLower) &&
        /\s/.test(lower[slashLower.length] ?? '')
      ) {
        via = 'slash';
      }
    }

    // Precedence 3: pattern match (case-insensitive via RegExp `i` flag).
    if (via === null && matcher.patterns && trimmed.length > 0) {
      for (const pattern of matcher.patterns) {
        if (pattern.test(trimmed)) {
          via = 'pattern';
          break;
        }
      }
    }

    if (via === null) {
      continue;
    }

    // Gate on requiresFiles. A matched intent that needs files but has none
    // is suppressed — the upstream prompt-first interjection handles it.
    if (matcher.requiresFiles && !hasFiles) {
      return null;
    }

    return {
      id: matcher.id,
      executor: matcher.executor,
      via,
    };
  }

  return null;
}
