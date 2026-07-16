/**
 * chipDisplayOrder.ts — deterministic, preference-keyed DISPLAY reorder for
 * Suggested Next Steps (SNS) chips (task 043 / FR-G1, ADR-039).
 *
 * ADR-039 permits the User Model to deterministically reorder ALREADY-GROUNDED
 * chips for DISPLAY, keyed by a user preference signal. This is a pure
 * display-layer sort — it MUST:
 *   - be deterministic (same chips + same preference input → same output order,
 *     every time; no randomness, no clock/session-id salt);
 *   - be preference-KEYED (the sort key is a caller-supplied preference value,
 *     never re-derived from the chip labels/content — that would be a second
 *     intent mechanism, forbidden);
 *   - involve NO model call and NO re-dispatch/re-grounding — it reorders the
 *     SAME `chips` array the caller already has; it never adds, removes,
 *     grants, or re-selects a capability (preference ≠ permission).
 *
 * GAP — surfaced, not guessed (per task 043 instructions): the User Model's
 * STATED preference signal (`sprk_userprofile` — role, focus areas, assistant
 * preferences; read server-side by `StatedProfileReader.cs`) is folded into
 * `ContextBinder.userFragment` as free-text PROMPT CONTENT for the LLM turn
 * ONLY. It is never projected to the client today:
 *   - no GET endpoint returns the stated profile to a browser caller;
 *   - no chat-session-bootstrap payload or SSE frame carries it;
 *   - task 042 ("My Assistant" questionnaire — the client WRITE path for
 *     `sprk_userprofile`) is not yet built (TASK-INDEX.md: 🔲 as of this task).
 * There is therefore NO client-accessible preference source to key this
 * reorder on yet. Rather than fabricate a transport (e.g. hashing a fake
 * value, or reading a field that isn't actually exposed), this module accepts
 * an explicit, optional `ChipDisplayPreference` input and documents that every
 * current call site passes none (`undefined`/`null`) — the reorder is WIRED
 * and READY to accept a real preference the moment a client-accessible
 * projection exists (e.g. a future session-bootstrap field, or a client-side
 * Dataverse read of `sprk_userprofile`), with ZERO change to the sort
 * mechanics below. Until then it deterministically falls back to the
 * server-declared order (the Binding's `sprk_chiptransitions` declaration
 * order, i.e. the array order the caller already received) — itself a
 * legitimate deterministic ordering, not an accidental no-op.
 */

import type { ConsumerChip } from "@spaarke/ui-components";

/**
 * The preference signal this reorder keys on, when available. `preferredBindingOrder`
 * is an ordered list of `sprk_playbookconsumer` Binding ids, most-preferred first
 * (e.g. derived from a future stated/learned preference projection). Chips whose
 * `bindingId` appears earlier in this list rank earlier in the display order;
 * chips not present fall back to their original relative order, appended after
 * every ranked chip.
 */
export interface ChipDisplayPreference {
  readonly preferredBindingOrder?: ReadonlyArray<string>;
}

/**
 * Deterministically reorders `chips` for DISPLAY ONLY (ADR-039 display-layer
 * reorder). Does not add, remove, or mutate any chip — it returns the SAME
 * chip objects in a possibly-different order.
 *
 * - No `preference` (or an empty `preferredBindingOrder`): returns `chips`
 *   UNCHANGED (the server-declared order) — see the GAP note in this file's
 *   header for why every current call site is in this branch today.
 * - With a `preference`: a stable sort keyed by each chip's rank in
 *   `preferredBindingOrder` (ties — including "not present" — keep the
 *   chips' original relative order, so the sort is deterministic even when
 *   two chips share a rank).
 */
export function reorderChipsForDisplay(
  chips: ReadonlyArray<ConsumerChip>,
  preference?: ChipDisplayPreference | null
): ReadonlyArray<ConsumerChip> {
  const preferredOrder = preference?.preferredBindingOrder;
  if (!preferredOrder || preferredOrder.length === 0) {
    return chips;
  }

  const rank = new Map<string, number>(preferredOrder.map((bindingId, index) => [bindingId, index]));
  const UNRANKED = Number.MAX_SAFE_INTEGER;

  return chips
    .map((chip, originalIndex) => ({ chip, originalIndex }))
    .sort((a, b) => {
      const rankA = rank.get(a.chip.bindingId) ?? UNRANKED;
      const rankB = rank.get(b.chip.bindingId) ?? UNRANKED;
      if (rankA !== rankB) return rankA - rankB;
      // Deterministic tiebreak: preserve original (server-declared) relative order.
      return a.originalIndex - b.originalIndex;
    })
    .map((entry) => entry.chip);
}
