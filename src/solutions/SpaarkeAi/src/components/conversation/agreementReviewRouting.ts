/**
 * agreementReviewRouting.ts — pure routing + gate-decision logic for the interactive
 * document-driven classifier + orientation + confirmation gate.
 *
 * ai-advanced-capabilities-agreements-r1 task 021 (spec FR-07/FR-08/FR-09 interactive half;
 * design Lens 3d). Mirrors composeReviseRouting.ts / composeDraftRouting.ts's style:
 * deterministic, TOTAL, side-effect-free functions of raw inputs — no IO, no React, no server
 * round-trip. ADR-039: these functions decide UX PRESENTATION (which gate branch to show,
 * which chips to render) — never WHICH Binding runs (bindingIds always come from capability
 * discovery, resolved by the caller — never invented here) and never re-detect capability
 * intent beyond the narrow "does this read as a review/assessment request about the
 * uploaded document" signal below.
 *
 * @see ./useAgreementReviewGate.ts — the stateful hook that drives classify dispatch, the
 *      registry read, and chip rendering using the pure decisions computed here.
 * @see ./composeReviseRouting.ts — the sibling "revise this document" pure-routing module;
 *      {@link detectAgreementReviewIntent} is deliberately checked FIRST by the caller
 *      (ConversationPane) and excludes every verb/keyword `detectReviseThisDocumentIntent`
 *      already owns, so the two detectors never both fire on the same message.
 */

import type { ConsumerChip } from "@spaarke/ui-components";
import { LOCAL_CHIP, buildAgreementReviewLensChipId } from "./localActionChips";

// ---------------------------------------------------------------------------
// Review-intent detection (Text-path interception, checked BEFORE the generic
// revise detector in ConversationPane.handleDecorateOutboundBodyWithRevise)
// ---------------------------------------------------------------------------

// A review/assessment verb. Deliberately narrower than composeReviseRouting.ts's
// REVISE_VERB_RE (which also matches "review" as a revise-flow trigger) — this
// module is checked FIRST by the caller, so "review this document" (no other
// signal) routes here instead of the generic whole-document revise/flag-risks flow.
const REVIEW_VERB_RE = /\b(review|assess|evaluate)\b/i;

// A reference to the document under discussion — a narrower list than
// composeReviseRouting.ts's DOC_REFERENCE_RE (correspondence-specific nouns like
// "letter"/"response" are deliberately excluded; those are revise/draft territory,
// not agreement-review territory).
const DOC_REFERENCE_RE =
  /\b(this|the|my|our)\s+(document|doc|file|contract|agreement|nda|lease)\b/i;

// Keywords/verbs already owned by the generic revise flow (composeReviseRouting.ts's
// detectReviseThisDocumentIntent + extractNamedReviseIntent) — a revise-specific verb,
// or a flag-risks/align-clauses/improve-clarity signal. Excluding these keeps the two
// detectors from double-firing on the same message (e.g. "review the contract and
// identify risks" stays on the shipped flag-risks path; "review this document" alone
// routes to the agreement-classify gate).
const REVISE_OWNED_RE =
  /\b(revise|redline|red-line|mark[\s-]?up|edit|rewrite|re-write|redraft|re-draft|rework|tighten|risks?|playbook|align|clarity|clarif|clearer|readab|plain\s?language|simplif|concise)\b/i;

/**
 * Pure, total, side-effect-free detection of a natural-language "review this document"
 * request that should route to the agreement-classify confirmation gate (task 021),
 * NOT the generic whole-document revise flow (compose-revise-document). Requires BOTH
 * a review/assessment verb AND a document reference; excludes any message already owned
 * by the revise flow's verb/keyword set. The CALLER additionally gates on an
 * attached/target document being present (negative criterion: a bare "review" with no
 * document reference, or no active/uploading document, never fires the gate).
 */
export function detectAgreementReviewIntent(messageText: string): boolean {
  const trimmed = messageText.trim();
  if (trimmed.length === 0 || trimmed.startsWith("/")) return false;
  if (REVISE_OWNED_RE.test(trimmed)) return false;
  if (!DOC_REFERENCE_RE.test(trimmed)) return false;
  return REVIEW_VERB_RE.test(trimmed);
}

// ---------------------------------------------------------------------------
// Classify result + registry shapes
// ---------------------------------------------------------------------------

/** One candidate from the `agreement-classify` Action's structured output (task 020). */
export interface AgreementClassifyCandidate {
  readonly subDomainKey: string;
  readonly confidence: number;
}

/** The `agreement-classify` Action's structured output shape (task 020). */
export interface AgreementClassifyResult {
  readonly isAgreement: boolean;
  readonly candidates: readonly AgreementClassifyCandidate[];
  readonly composite: boolean;
  readonly reasoning?: string;
}

/** Tolerant runtime guard — the classify dispatch's `result` is `unknown` on the wire. */
export function isAgreementClassifyResult(value: unknown): value is AgreementClassifyResult {
  if (!value || typeof value !== "object") return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v.isAgreement === "boolean" &&
    typeof v.composite === "boolean" &&
    Array.isArray(v.candidates) &&
    v.candidates.every(
      (c) =>
        c &&
        typeof c === "object" &&
        typeof (c as Record<string, unknown>).subDomainKey === "string" &&
        typeof (c as Record<string, unknown>).confidence === "number"
    )
  );
}

/**
 * A projected `sprk_agreementtype` registry row — read CLIENT-SIDE via the host-context
 * Xrm.WebApi (the SAME direct-read pattern the hub's `CreateAnalysisWizardWidget` picker
 * uses, per DATA-ACCESS-DECISION-CRITERIA — reference/lookup data, no BFF round-trip).
 * `confidenceThreshold` is the per-type override of the global 0.85 baseline (Dataverse
 * `sprk_confidencethreshold`, Decimal; null = use the global baseline).
 */
export interface AgreementTypeRegistryEntry {
  readonly key: string;
  readonly name: string;
  readonly isFallback: boolean;
  readonly confidenceThreshold: number | null;
}

/** Near-certain baseline (design Lens 3d, owner 2026-07-29) — per-type override via the registry row. */
export const GLOBAL_CONFIDENCE_THRESHOLD = 0.85;

/** The literal fallback key used when the registry is unavailable (defensive only — every seeded env has an IsFallback row). */
const DEFAULT_FALLBACK_KEY = "general";

/** Resolves the effective confirm-gate threshold for a candidate: per-type override, else the global baseline. */
export function resolveConfidenceThreshold(
  subDomainKey: string,
  registry: readonly AgreementTypeRegistryEntry[]
): number {
  const row = registry.find((r) => r.key === subDomainKey);
  return row?.confidenceThreshold ?? GLOBAL_CONFIDENCE_THRESHOLD;
}

/** Resolves the registry's fallback (general) row key via its `IsFallback` flag — never a hardcoded string when the registry is available. */
export function resolveFallbackKey(registry: readonly AgreementTypeRegistryEntry[]): string {
  return registry.find((r) => r.isFallback)?.key ?? DEFAULT_FALLBACK_KEY;
}

/** Human display name for a sub-domain key: the registry's `sprk_name`, else a key-derived title-case fallback. */
export function displayNameFor(key: string, registry: readonly AgreementTypeRegistryEntry[]): string {
  const row = registry.find((r) => r.key === key);
  if (row?.name) return row.name;
  return key
    .split(/[-_]/)
    .filter((w) => w.length > 0)
    .map((w) => (w.toUpperCase() === w ? w : w.charAt(0).toUpperCase() + w.slice(1)))
    .join(" ");
}

/** Rounds a [0,1] confidence to a display percentage, clamped defensively. */
export function formatConfidencePercent(confidence: number): number {
  return Math.round(Math.min(Math.max(confidence, 0), 1) * 100);
}

// ---------------------------------------------------------------------------
// Gate decision (the confirmation-gate branch — ADR-041 D-F1, FR-08)
// ---------------------------------------------------------------------------

export type AgreementReviewGateDecision =
  | { readonly kind: "non-agreement" }
  | { readonly kind: "auto-proceed"; readonly subDomainKey: string }
  | {
      readonly kind: "confirm";
      readonly subDomainKey: string;
      readonly confidence: number;
      readonly threshold: number;
    }
  | { readonly kind: "composite"; readonly candidates: readonly AgreementClassifyCandidate[] };

/**
 * The confirmation-gate decision (FR-08): NEVER a silent wrong-grounding run, NEVER a
 * silent decline.
 * - `isAgreement=false` (or no candidates) → `non-agreement` (explicit decline + general option).
 * - `composite=true` with ≥2 candidates → `composite` (choice-of-lens incl. "both").
 * - Single top candidate ≥ its resolved threshold → `auto-proceed`.
 * - Single top candidate < its resolved threshold → `confirm` (proposed type + pick-another).
 */
export function resolveAgreementReviewGateDecision(
  result: AgreementClassifyResult,
  registry: readonly AgreementTypeRegistryEntry[]
): AgreementReviewGateDecision {
  if (!result.isAgreement || result.candidates.length === 0) {
    return { kind: "non-agreement" };
  }
  if (result.composite && result.candidates.length >= 2) {
    return { kind: "composite", candidates: result.candidates };
  }
  const top = [...result.candidates].sort((a, b) => b.confidence - a.confidence)[0];
  const threshold = resolveConfidenceThreshold(top.subDomainKey, registry);
  if (top.confidence >= threshold) {
    return { kind: "auto-proceed", subDomainKey: top.subDomainKey };
  }
  return { kind: "confirm", subDomainKey: top.subDomainKey, confidence: top.confidence, threshold };
}

// ---------------------------------------------------------------------------
// Chat messages (deterministic copy — exported for tests + the host)
// ---------------------------------------------------------------------------

/** NEVER silent-decline (project constraint): explicit non-agreement message + a general-review option follows as a chip. */
export const AGREEMENT_REVIEW_NON_AGREEMENT_MESSAGE =
  "This doesn't look like an agreement, so I didn't run an Agreement Review. If you'd like a general review anyway, choose the option below.";

export function buildAgreementReviewConfirmMessage(displayName: string, confidence: number): string {
  return `This looks like it might be a **${displayName}** (${formatConfidencePercent(confidence)}% confidence) — is that right?`;
}

export const AGREEMENT_REVIEW_COMPOSITE_MESSAGE =
  "This document looks like it covers more than one agreement type. Which would you like reviewed?";

// ---------------------------------------------------------------------------
// Chip building (wire shape — fed through the SAME `acceptChips` every server
// chip carrier uses; ConversationPane's local-chip interceptor routes clicks
// back to the gate, see localActionChips.ts's `isLocalChip` + the reserved
// `local:agreement-review-*` ids)
// ---------------------------------------------------------------------------

/** Below-threshold confirm chips: proposed type + pick-another (FR-08). Max 2 — throwaway turn-follow-on (ASSISTANT-UI-ELEMENT-CRITERIA). */
export function buildAgreementReviewConfirmChips(displayName: string): ConsumerChip[] {
  return [
    {
      label: `Yes, review as ${displayName}`,
      bindingId: LOCAL_CHIP.agreementReviewConfirm,
      requiresAttachments: true,
    },
    {
      label: "Use the general review instead",
      bindingId: LOCAL_CHIP.agreementReviewGeneral,
      requiresAttachments: true,
    },
  ];
}

/** Composite choice-of-lens chips: one per candidate + "Both" (sequential multi-pack, ADR-016). */
export function buildAgreementReviewCompositeChips(
  candidates: readonly AgreementClassifyCandidate[],
  registry: readonly AgreementTypeRegistryEntry[]
): ConsumerChip[] {
  const lensChips: ConsumerChip[] = candidates.map((c) => ({
    label: `Review as ${displayNameFor(c.subDomainKey, registry)}`,
    bindingId: buildAgreementReviewLensChipId(c.subDomainKey),
    requiresAttachments: true,
  }));
  return [
    ...lensChips,
    { label: "Both", bindingId: LOCAL_CHIP.agreementReviewBoth, requiresAttachments: true },
  ];
}

/** Non-agreement decline chip: the general-review escape hatch (never a silent decline). */
export function buildAgreementReviewNonAgreementChips(): ConsumerChip[] {
  return [
    {
      label: "Run a general review anyway",
      bindingId: LOCAL_CHIP.agreementReviewGeneral,
      requiresAttachments: true,
    },
  ];
}

/**
 * Converts a `ConsumerChip[]` to the wire shape `acceptChips`/`parseConsumerChips` expects
 * (camelCase `ConsumerChipWire` fields) — these gate chips are synthesized client-side
 * (never server-declared), so they are round-tripped through the SAME tolerant wire parser
 * every other chip carrier uses rather than adding a second typed-chip entry point to
 * `useConsumerChips` (§11 reuse-first).
 */
export function toConsumerChipWire(chips: readonly ConsumerChip[]): Record<string, unknown>[] {
  return chips.map((c) => ({
    targetBindingId: c.bindingId,
    chipLabel: c.label,
    requiresAttachments: c.requiresAttachments === true,
  }));
}
