/**
 * localActionChips.ts — client-only "local action" chips (UAT R4-6 + R4-11).
 *
 * The Suggested-Next-Steps strip (`ConsumerChips` / `useConsumerChips`) is
 * built around server-declared `sprk_chiptransitions`: every card carries a
 * real `target_binding_id` and clicking it runs the ONE shared
 * `dispatchConsumer(bindingId, …)` path (ADR-039). Some UAT-requested
 * next-actions are NOT dispatch bindings, though — they are CLIENT bridges or
 * prompt nudges with no `sprk_playbookconsumer` row:
 *
 *   - **Send as email** (R4-6)      → the editor/email `draft-email` widget bridge
 *   - **Save to document** (R4-6)   → the Compose `add-to-dms` create-on-save bridge
 *   - **Ask about these files** (R4-11) → a prompt nudge (the files are already
 *     attached, so a normal chat turn is grounded — no capability to dispatch)
 *
 * These ride the SAME `ConsumerChip` shape but use a reserved `local:` sentinel
 * `bindingId`. The host (`useConsumerChips.handleConsumerChipClick`) intercepts
 * any `local:`-prefixed chip and routes it to `onLocalChipAction` instead of
 * `dispatchConsumer` — so no fake/broken Binding dispatch is ever attempted and
 * `parseConsumerChips` (which requires a non-empty bindingId) is bypassed
 * because local chips are injected directly, never parsed off the wire.
 *
 * The genuinely dispatchable post-Draft / post-Summarize actions ("Create a
 * matter", "Draft a response") stay server-declared on the `draft-correspondence`
 * and `chat-summarize` bindings' `sprk_chiptransitions` — this module only adds
 * the non-binding companions alongside them.
 */

import type { ConsumerChip } from "@spaarke/ui-components";

/** Reserved bindingId namespace for client-only action chips. */
export const LOCAL_CHIP_PREFIX = "local:";

/** Stable local-action ids (the `bindingId` of each local chip). */
export const LOCAL_CHIP = {
  sendAsEmail: "local:send-as-email",
  saveToDocument: "local:save-to-document",
  askAboutFiles: "local:ask-about-files",
  reviseInCompose: "local:revise-in-compose",
} as const;

export type LocalChipActionId = (typeof LOCAL_CHIP)[keyof typeof LOCAL_CHIP];

/** True when a chip's bindingId is a client-only local action (not a real Binding). */
export function isLocalChip(bindingId: string | undefined | null): boolean {
  return typeof bindingId === "string" && bindingId.startsWith(LOCAL_CHIP_PREFIX);
}

/**
 * R4-6 — the two non-binding companions shown after "Draft a response" opens a
 * pre-filled Compose tab. Ordered before the server "Create a matter" chip so
 * the strip reads "Send as email · Save to document · Create a matter".
 * `requiresAttachments:false` — they act on the drafted Compose document, which
 * exists regardless of the session attachment count.
 */
export function buildPostDraftLocalChips(): ConsumerChip[] {
  return [
    { label: "Send as email", bindingId: LOCAL_CHIP.sendAsEmail, requiresAttachments: false },
    { label: "Save to document", bindingId: LOCAL_CHIP.saveToDocument, requiresAttachments: false },
  ];
}

/**
 * R4-11 — the non-binding companion shown after a summary, alongside the server
 * "Create a matter" / "Draft a response" chips. Gated on attachments because it
 * only makes sense with files in the session.
 */
export function buildAskAboutFilesChip(): ConsumerChip {
  return { label: "Ask about these files", bindingId: LOCAL_CHIP.askAboutFiles, requiresAttachments: true };
}

/**
 * R5-1 — "Revise in Compose" as an inline action card, in line with the post-attach cards
 * (Summarize this file / Create a matter / Draft a response) instead of a separate button in
 * the files tray. Opens the attached file(s) in the Compose editor. Requires attachments.
 */
export function buildReviseInComposeChip(): ConsumerChip {
  // R6-1 (UAT 2026-07-21): labeled "Revise document" (was "Revise in Compose").
  return { label: "Revise document", bindingId: LOCAL_CHIP.reviseInCompose, requiresAttachments: true };
}
