/**
 * @spaarke/ai-widgets — InsightSummaryCard citation type discriminator (Task 033)
 *
 * Implements FR-07 (spec.md): citation envelope is extensible via a `type`
 * discriminator. r1 ships two known variants:
 *
 *   - `assessment` → in-product navigation link to a `sprk_kpiassessment` row
 *   - `document`   → SharePoint Embedded `spe://drive/X/item/Y` href (or
 *                    Dataverse `sprk_document` GUID) — host decides how to
 *                    open it
 *
 * Adding a new citation type means adding a new variant here PLUS a new
 * `case` in the `renderCitation` switch in `InsightSummaryCard.tsx`. The
 * component MUST gracefully fall back to a plain-text label for unknown
 * `type` values (no throw) so the BFF can ship new types ahead of the UI
 * without breaking the card.
 *
 * Q-U3 — owner deferral honoured: NO feedback affordance here. Citations
 * do not surface thumbs / free-text fields.
 *
 * Q-U1 — owner ban honoured: NO `@v1`/`@vN` identifier-suffix vernacular.
 *
 * @see projects/ai-spaarke-insights-engine-widgets-r1/spec.md FR-07
 * @see ADR-021 — semantic tokens only (consumed by useInsightSummaryCardStyles)
 */

// ---------------------------------------------------------------------------
// Common shape — every variant carries an `id` + optional `label`.
// ---------------------------------------------------------------------------

/**
 * Fields shared by every citation variant.
 *
 * `id` is required so the host can de-duplicate / key React lists; `label`
 * is the human-readable text rendered in the link (or plain-text fallback
 * for unknown types).
 */
interface ICitationBase {
  /** Citation identifier (typically `${sourceId}#${index}` or BFF-supplied id). */
  id: string;
  /** Optional source label rendered as the link text / fallback text. */
  label?: string;
}

// ---------------------------------------------------------------------------
// Known variants (FR-07 initial set)
// ---------------------------------------------------------------------------

/**
 * `assessment` — in-product navigation citation.
 *
 * Points at a specific `sprk_kpiassessment` row. The host translates
 * `assessmentId` into an in-product URL (typically MDA form deep-link) when
 * `onCitationClick` fires.
 */
export interface AssessmentCitation extends ICitationBase {
  type: 'assessment';
  /** GUID of the target `sprk_kpiassessment` row. */
  assessmentId: string;
}

/**
 * `document` — source document citation.
 *
 * Points at a SharePoint Embedded driveItem (`spe://drive/{driveId}/item/{itemId}`)
 * OR a Dataverse `sprk_document` record. The host decides which to use
 * based on whichever field is populated (`speHref` preferred when both are
 * present per r2 conventions).
 */
export interface DocumentCitation extends ICitationBase {
  type: 'document';
  /** SPE driveItem href, shape: `spe://drive/{driveId}/item/{itemId}`. */
  speHref?: string;
  /** GUID of a `sprk_document` row (fallback when SPE href unavailable). */
  documentId?: string;
}

// ---------------------------------------------------------------------------
// Unknown variant — forward-compatible fallback.
// ---------------------------------------------------------------------------

/**
 * `unknown` — any non-`assessment` / non-`document` type value.
 *
 * This variant exists so the BFF can ship a new citation type ahead of the
 * UI (e.g. r2's `passage` or `policy-rule`) without crashing the card. The
 * component renders these as plain-text labels and does NOT invoke
 * `onCitationClick` for them.
 *
 * Consumers should treat the union non-exhaustively: a `switch` with a
 * `default` arm landing in `UnknownCitation` rendering is the canonical
 * shape. The reducer-style `never` exhaustiveness check used elsewhere in
 * this component would defeat the FR-07 "gracefully ignores unknown types"
 * acceptance criterion.
 */
export interface UnknownCitation extends ICitationBase {
  // `type` is widened to `string` so any other string lands here. We do NOT
  // use a literal `'unknown'` token — the BFF would have to know about it
  // to opt-in. The widening is intentional and is what makes the union
  // forward-compatible.
  type: string;
}

// ---------------------------------------------------------------------------
// Discriminated union — the public Citation type.
// ---------------------------------------------------------------------------

/**
 * Discriminated citation union surfaced by `InsightEnvelope.citations[]`
 * and passed to `onCitationClick`.
 *
 * TypeScript narrows by `type` literal: `assessment` → `AssessmentCitation`,
 * `document` → `DocumentCitation`, anything else → `UnknownCitation`.
 *
 * NOTE on extensibility: adding a new known variant is a 3-step change:
 *   1. Add the interface here (e.g. `PassageCitation`).
 *   2. Add it to this union.
 *   3. Add a `case 'passage':` arm to `renderCitation` in
 *      `InsightSummaryCard.tsx`.
 *
 * No new component, no new prop — that's the FR-07 extensibility contract.
 */
export type Citation = AssessmentCitation | DocumentCitation | UnknownCitation;

// ---------------------------------------------------------------------------
// Type guards — used by the renderer + by hosts wiring `onCitationClick`.
// ---------------------------------------------------------------------------

/**
 * Narrow a `Citation` to `AssessmentCitation`.
 *
 * Hosts wiring `onCitationClick` use this to route assessment clicks to
 * an in-product navigation handler (e.g. `Xrm.Navigation.openForm`).
 */
export function isAssessmentCitation(c: Citation): c is AssessmentCitation {
  return c.type === 'assessment';
}

/**
 * Narrow a `Citation` to `DocumentCitation`.
 *
 * Hosts wiring `onCitationClick` use this to route document clicks to
 * the SPE document viewer (or `sprk_document` form).
 */
export function isDocumentCitation(c: Citation): c is DocumentCitation {
  return c.type === 'document';
}
