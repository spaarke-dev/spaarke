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
  ndaReview: "local:nda-review",
  /**
   * task 021 (FR-08 interactive confirmation gate) — below-threshold "Yes, review as
   * {type}" chip. Accepts the classifier's top (unconfirmed) candidate.
   */
  agreementReviewConfirm: "local:agreement-review-confirm",
  /**
   * task 021 — "Use the general review instead" (below-threshold pick-another) AND the
   * non-agreement decline's "Run a general review anyway" escape hatch. Both branches
   * fall back to the SAME general/fallback pack, so they share one local-action id.
   */
  agreementReviewGeneral: "local:agreement-review-general",
  /** task 021 — composite choice-of-lens "Both" chip: sequential multi-pack dispatch (ADR-016). */
  agreementReviewBoth: "local:agreement-review-both",
} as const;

export type LocalChipActionId = (typeof LOCAL_CHIP)[keyof typeof LOCAL_CHIP];

/**
 * task 021 — reserved prefix for the composite choice-of-lens chips, one per classified
 * candidate (e.g. `local:agreement-review-lens:employment`). A per-candidate dynamic id
 * cannot be a static `LOCAL_CHIP` constant (the candidate set is classifier output, not
 * catalog data), so the sub-domain key is encoded as a suffix and decoded on click —
 * mirrors the `LOCAL_CHIP_PREFIX` reserved-namespace pattern above, one level deeper.
 */
const AGREEMENT_REVIEW_LENS_CHIP_PREFIX = "local:agreement-review-lens:";

/** Builds a composite-lens chip's local bindingId for the given classified sub-domain key. */
export function buildAgreementReviewLensChipId(subDomainKey: string): string {
  return `${AGREEMENT_REVIEW_LENS_CHIP_PREFIX}${subDomainKey}`;
}

/** Extracts the sub-domain key from a composite-lens chip's bindingId, or null if it isn't one. */
export function decodeAgreementReviewLensChipId(bindingId: string): string | null {
  return bindingId.startsWith(AGREEMENT_REVIEW_LENS_CHIP_PREFIX)
    ? bindingId.slice(AGREEMENT_REVIEW_LENS_CHIP_PREFIX.length)
    : null;
}

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

/**
 * ai-advanced-capabilities-nda-r1 follow-up (UAT 2026-07-26): "Review an NDA" as an in-line
 * Suggested-Next-Steps card, alongside "Summarize this file / Revise document", instead of a
 * separate top-of-pane notification card that read as "hidden". It rides the local-chip path
 * because the click needs a client-side bridge (open the file in Compose + dispatch a
 * document-review capability scoped to that file) rather than a bare server Binding dispatch —
 * see the host's `handleReviewNda`. Only appended when the classifier flagged the upload as a
 * reviewable document type (host gates on `ndaReviewFileRef`); `requiresAttachments:true`
 * because it acts on the uploaded file.
 *
 * `cardLabel` defaults to "Review an NDA" (unchanged behavior for the NDA docType) — task 064
 * (ai-advanced-capabilities-analysis-hub-r1, spec §13.5 / FR-22) generalized the caller
 * (ConversationPane's `handleNdaClassified`) to resolve the label from
 * {@link getDocumentReviewCapability} by classified `docType` instead of hardcoding "nda", so a
 * different work-type's review capability (once its Action/Binding exists in the catalog) can
 * pass its own label here without a second chip-builder function.
 */
export function buildNdaReviewChip(cardLabel: string = "Review an NDA"): ConsumerChip {
  return { label: cardLabel, bindingId: LOCAL_CHIP.ndaReview, requiresAttachments: true };
}

/**
 * A classified upload's structural-clause-review capability — the Binding's `consumerType`
 * (resolved from the closed capability catalog per ADR-039, never invented/hardcoded at dispatch
 * time) and the Suggested-Next-Steps card label to show for that document type.
 *
 * task 064 (ai-advanced-capabilities-analysis-hub-r1, spec §13.5 / FR-22): generalizes the
 * previously NDA-hardcoded `docType !== "nda"` gate in ConversationPane's `handleNdaClassified`
 * into a small, extensible table keyed by the classifier's `docType` output. Adding a new
 * work-type's clause-review capability (once its Action/Binding is authored in Dataverse) is a
 * ONE-LINE addition here — no other code change. `isNdaReviewResult` (the structural
 * `{overallRisk, flaggedSections[]}` shape guard in `useNdaReviewAdvisoryCommentsBridge.ts`) is
 * already work-type-agnostic — it matches on OUTPUT SHAPE, not on which docType triggered the
 * dispatch — so no change was needed there.
 */
export interface DocumentReviewCapability {
  /** The classifier's `docType` output this capability applies to (compared case-insensitively). */
  docType: string;
  /** The Action/Binding `consumerType` to resolve a bindingId for via capability discovery. */
  consumerType: string;
  /** Suggested-Next-Steps card label for this document type. */
  cardLabel: string;
}

/**
 * Registered document-review capabilities by classified docType. Only "nda" has a real Action
 * today (ai-advanced-capabilities-nda-r1) — additional entries are inert until a matching
 * Action/Binding exists in the catalog (capability discovery safely resolves an unregistered
 * consumerType to a null bindingId — see ConversationPane's `handleReviewNda` no-bindingId branch).
 */
export const DOCUMENT_REVIEW_CAPABILITIES: readonly DocumentReviewCapability[] = [
  { docType: "nda", consumerType: "nda-review", cardLabel: "Review an NDA" },
];

/**
 * Looks up the document-review capability registered for a classified `docType`, or `null` when
 * the docType is absent/unrecognized (mirrors the prior hardcoded `docType !== "nda"` negative
 * case — any other docType, including empty/unclassified, yields no card).
 */
export function getDocumentReviewCapability(docType: string | undefined): DocumentReviewCapability | null {
  const normalized = typeof docType === "string" ? docType.trim().toLowerCase() : "";
  if (!normalized) return null;
  return DOCUMENT_REVIEW_CAPABILITIES.find((c) => c.docType === normalized) ?? null;
}
