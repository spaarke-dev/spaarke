/**
 * SoftSlashRouter.ts — R6 task 082 / D-D-03 (Pillar 8 soft slashes).
 *
 * The four soft slashes (FR-50, Q6 closed) are intent SHORTCUTS that ROUTE
 * through the agent — they do NOT bypass the LLM. The router decorates the
 * outbound BFF chat payload with an `intentHint` metadata field that the
 * existing `CapabilityRouter` Layer 1 consumes for strong-intent routing.
 *
 * Wire-format field name renamed `commandIntent` → `intentHint` per
 * spaarke-ai-platform-chat-routing-redesign-r1 FR-07 / task 022 (2026-06-22).
 * The TypeScript value-vocabulary type `CommandIntent` is intentionally
 * retained (it names the *set of values*, not the wire field) — see
 * `projects/spaarke-ai-platform-chat-routing-redesign-r1/notes/handoffs/
 * 022-commandintent-rename-decision.md`.
 *
 * Soft-slash vocabulary (Q6 closed at exactly 4 — do NOT extend):
 *   - `/summarize`        → `summarize`         → invoke_playbook(SUM-CHAT@v1)
 *   - `/draft`            → `draft`             → draft-intent flag; agent picks
 *                                                  TextRefinement + WorkingDocument tools
 *   - `/extract-entities` → `extract-entities`  → EntityExtractorHandler (Pillar 2 Wave 2)
 *   - `/analyze`          → `analyze`           → analysis-intent flag; agent picks from
 *                                                  analysis playbook list
 *
 * Composition with references (FR-52):
 *   `/summarize #engagement-letter.docx` produces
 *   `{ command: '/summarize', isSoftSlash: true, references: [{kind:'filename',...}] }`
 *   from `CommandRouter.parse()`. The BFF payload then carries BOTH
 *   `intentHint: 'summarize'` AND attachment metadata when the references
 *   are resolved by task 083 (ReferenceResolver). Reference resolution itself
 *   is out of this module's scope.
 *
 * NFR contracts (verified at integration time):
 *   - NFR-01 (conversational primacy): agent remains conversational after a
 *     soft slash; follow-up messages without a slash route through the
 *     existing CapabilityRouter natural-language path UNCHANGED.
 *   - NFR-11 (backward compat): when no slash is detected, the payload is
 *     RETURNED UNCHANGED (no `intentHint` field). Natural-language
 *     equivalents like "summarize this" still work via Layer 1 keyword
 *     scoring.
 *
 * ADR contracts:
 *   - ADR-013: no new public-contract surface in `Services/Ai/PublicContracts/`.
 *     The BFF reads `intentHint` via an internal pre-pass in
 *     `CapabilityRouter` (mirroring task 069's voice-memory Layer 0 pattern).
 *     The router-side change is an additive optional parameter; no new
 *     interface, no new DI registration.
 *   - ADR-015: telemetry tier-1 fields only. This module does NOT log user
 *     message text; the BFF emits one `context.decision_made` event per soft
 *     slash with `layer: "layer1"`, `decision: "soft_slash"`,
 *     `capabilityName: <intent value>`.
 *   - ADR-029: BFF publish-size delta target = 0 KB; the minimal BFF
 *     pre-pass is single-method addition + 1 optional parameter through
 *     the call chain, no new dependencies.
 *
 * This module is PURE — no IO, no React, no state writes, no async.
 * Deterministic given inputs. Trivially testable with plain values.
 *
 * @see ./CommandRouter.ts — Intent shape (task 080)
 * @see projects/spaarke-ai-platform-unification-r6/spec.md FR-50, NFR-01, NFR-11
 * @see projects/spaarke-ai-platform-unification-r6/CLAUDE.md §Pillar 8
 */

import type { Intent, SoftSlashCommand } from './CommandRouter';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Closed vocabulary of `intentHint` values mirroring the 4 soft slashes.
 * The BFF `CapabilityRouter` recognises EXACTLY these four strings; passing
 * any other value is undefined behaviour (the BFF will fall through to
 * normal Layer 1 keyword scoring).
 *
 * Mapping is 1:1 with `SoftSlashCommand` minus the leading slash.
 */
export type CommandIntent =
  | 'summarize'
  | 'draft'
  | 'extract-entities'
  | 'analyze';

/**
 * Shape of the outbound BFF chat-message payload that this router decorates.
 *
 * Mirrors `ChatSendMessageRequest` on the BFF (`ChatEndpoints.cs`) — fields
 * are camelCase to match the BFF's `JsonSerializerOptions.PropertyNamingPolicy`.
 * The router only TOUCHES the `intentHint` field; other fields pass through
 * unchanged. Using a structurally-typed shape (rather than importing the
 * BFF DTO) keeps the frontend bundle free of BFF-side imports.
 *
 * The decorated body is the JSON-serialised request body for
 * `POST /api/ai/chat/sessions/{sessionId}/messages`.
 */
export interface DecoratedChatBody {
  /**
   * The user's literal message text. May contain the soft-slash command and
   * any reference tokens (`#`, `@`). The BFF Layer 1 pre-pass reads
   * `intentHint` FIRST; `message` continues to flow into the LLM
   * conversation as-is.
   */
  message: string;

  /** Optional document ID (BFF passthrough). */
  documentId?: string;

  /**
   * Optional in-memory chat-message attachments (FR-07). Passthrough — this
   * module does not inspect or modify attachments.
   */
  attachments?: unknown;

  /**
   * R6 Pillar 8 / FR-50: closed-vocabulary intent field set by this router
   * when the parsed `Intent.isSoftSlash === true`. Absent when the intent is
   * NOT a soft slash (NFR-11 backward compat — the BFF defaults to its
   * existing Layer 1 keyword path when this field is missing).
   *
   * The BFF `CapabilityRouter` Layer 1 pre-pass maps this to a synthetic
   * capability name (see `CapabilityRouter.cs`
   * `SoftSlashIntentToCapabilityName`) and short-circuits to a Confident
   * result selecting that capability.
   */
  intentHint?: CommandIntent;

  /**
   * Allow forward-compatible passthrough of any other fields the BFF DTO
   * accepts now or in the future (e.g., HostContext overrides). This module
   * decorates `intentHint` only — everything else is untouched.
   */
  [extraField: string]: unknown;
}

// ---------------------------------------------------------------------------
// Vocabulary mapping (closed)
// ---------------------------------------------------------------------------

/**
 * Deterministic mapping from `SoftSlashCommand` → `CommandIntent` value.
 *
 * The mapping is the soft-slash literal stripped of its leading slash. The
 * table is exhaustive over `SoftSlashCommand` — TypeScript verifies this at
 * compile time via the `Record<SoftSlashCommand, CommandIntent>` type. Adding
 * a new entry without spec-FR sign-off WILL fail the build.
 */
const SOFT_SLASH_TO_INTENT: Record<SoftSlashCommand, CommandIntent> = {
  '/summarize': 'summarize',
  '/draft': 'draft',
  '/extract-entities': 'extract-entities',
  '/analyze': 'analyze',
} as const;

/**
 * Read-only export for downstream consumers (telemetry audits, `/help` UI,
 * documentation). Do NOT mutate. Vocabulary is closed at 4 per Q6.
 */
export const SoftSlashIntents: readonly CommandIntent[] = [
  'summarize',
  'draft',
  'extract-entities',
  'analyze',
] as const;

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Map a parsed soft-slash `Intent` to its `CommandIntent` value, or `null`
 * when the intent is NOT a soft slash.
 *
 * Pure / synchronous. Used by `decorateBody()` and exported for callers that
 * want the raw mapping (e.g., telemetry probes).
 *
 * @param intent The structured intent emitted by `CommandRouter.parse()`.
 * @returns The closed-vocabulary `CommandIntent` string, or `null` when
 *          `intent.isSoftSlash === false` or `intent.command` is null /
 *          a hard slash.
 */
export function toCommandIntent(intent: Intent): CommandIntent | null {
  if (!intent.isSoftSlash || intent.command === null) {
    return null;
  }
  // intent.command is a SlashCommand union — narrow to SoftSlashCommand via
  // the deterministic lookup table. If the command is a hard slash, the lookup
  // returns undefined and we fall back to null (defensive: invariant says
  // isSoftSlash === true ⇒ command is in SOFT_SLASH_TO_INTENT, but null
  // here keeps the function safe under future vocabulary changes).
  const intentValue = SOFT_SLASH_TO_INTENT[intent.command as SoftSlashCommand];
  return intentValue ?? null;
}

/**
 * Decorate the outbound BFF chat-message payload with an `intentHint`
 * metadata field when the parsed intent is a soft slash. Otherwise return
 * the body UNCHANGED (NFR-11 passthrough).
 *
 * Contract:
 *   - When `intent.isSoftSlash === true`: adds `intentHint: <value>` to
 *     the returned body. References (`intent.references[]`) are NOT touched
 *     here — task 083 (ReferenceResolver) handles them in a separate step,
 *     and both decorations COMPOSE on the same body without conflict.
 *   - When `intent.isSoftSlash === false` (hard slash, no slash, or
 *     unrecognized slash): returns the body verbatim with NO `intentHint`
 *     field. The BFF treats a missing field as "no soft-slash hint" and
 *     falls through to existing Layer 1 keyword scoring.
 *
 * The function returns a NEW object — the input body is never mutated. This
 * keeps the call site safe to use in React-rendered code paths where input
 * objects may be referenced elsewhere.
 *
 * Composition example (FR-52):
 *   parse('/summarize #engagement-letter.docx')  // task 080
 *     → Intent { command:'/summarize', isSoftSlash:true,
 *                references: [{kind:'filename', value:'engagement-letter.docx', ...}] }
 *   decorateBody(intent, { message: '/summarize #engagement-letter.docx' })
 *     → { message: '/summarize #engagement-letter.docx',
 *         intentHint: 'summarize' }
 *   (task 083 then adds resolved file content to `attachments[]`)
 *
 * @param intent The structured intent emitted by `CommandRouter.parse()`.
 * @param body   The outbound BFF chat-message payload.
 * @returns      A NEW body. Original body is not mutated.
 */
export function decorateBody(intent: Intent, body: DecoratedChatBody): DecoratedChatBody {
  const intentValue = toCommandIntent(intent);
  if (intentValue === null) {
    // NFR-11: no decoration; return UNCHANGED. We still return a shallow copy
    // so callers can rely on a consistent "new object every call" contract
    // for telemetry / equality checks, but the field set is identical to
    // the input.
    return { ...body };
  }

  return {
    ...body,
    intentHint: intentValue,
  };
}
