import { useMemo } from 'react';
import type { DocumentIdentityState, ResolvedRelatedRecord } from '../services/documentIdentityService';

/**
 * useRelatedRecord.ts — spaarkeai-word-add-in-r1 task 026 (FR-09).
 *
 * Derives the related-record CARD's view model from task 013's already-resolved
 * {@link DocumentIdentityState} — a PURE derivation, no network call. Task 012's server resolver already
 * read the association (see `DocumentUrlIdentityResolution.RelatedRecordAttributes` — the FOUR direct slots
 * `sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_workassignment`, in that precedence order; never
 * `sprk_related*`, which the Office save path never writes) and the extended `resolve-identity` response
 * (task 026) already carries the correctly-labeled `displayName`/`number` pair. Reading it again here would
 * risk the pane showing two different answers for the same document — the exact failure this hook avoids by
 * construction. See `projects/spaarkeai-word-add-in-r1/notes/026-slot-scope-decision.md` for the full
 * scoping rationale.
 */

/** Friendly label for the FOUR direct-slot entity types this card can show. */
const FRIENDLY_TYPE_LABELS: Record<string, string> = {
  sprk_matter: 'Matter',
  sprk_project: 'Project',
  sprk_invoice: 'Invoice',
  sprk_workassignment: 'Work Assignment',
};

/** Logical name → friendly label, falling back to the raw logical name for anything unrecognized. */
function friendlyTypeLabel(entityType: string): string {
  return FRIENDLY_TYPE_LABELS[entityType] ?? entityType;
}

export interface RelatedRecordView {
  /** Friendly type label, e.g. "Matter". */
  type: string;
  /** Dataverse logical name, e.g. `sprk_matter` — carried for the task 027 click seam. */
  entityType: string;
  /** Record id, bare lowercase (ADR-044) — carried for the task 027 click seam. */
  id: string;
  /** Descriptive name. Null when the related record has none set. */
  displayName: string | null;
  /** Record number. Null when the entity type has none, or none is set yet (render blank, not an error). */
  number: string | null;
}

/**
 * The related-record card's outcome. Every branch is a defined, honest state:
 * - `'absent'` — no resolved document identity (a new document, an unsettled/indeterminate/denied/error
 *   outcome, still checking, or identity does not apply to this host at all). The card renders NOTHING —
 *   AC7: "the card is absent and no association read call is made" (none is ever made by this hook).
 * - `'unassociated'` — the document IS identified, but none of the four direct slots is populated (or, by
 *   construction, the only populated slot is outside this task's read scope — the server never sends one).
 * - `'associated'` — the document is identified and filed to one of the four direct slots.
 */
export type RelatedRecordOutcome =
  | { kind: 'absent' }
  | { kind: 'unassociated' }
  | ({ kind: 'associated' } & RelatedRecordView);

function toView(record: ResolvedRelatedRecord): RelatedRecordView {
  return {
    type: friendlyTypeLabel(record.entityType),
    entityType: record.entityType,
    id: record.id,
    displayName: record.displayName ?? null,
    number: record.number ?? null,
  };
}

/**
 * Derives the related-record card's view model from the pane's already-resolved document identity.
 * `documentIdentity` is `undefined` for a host without identity resolution (Outlook) — gated by capability
 * (NFR-10), never by a `hostType === 'word'` branch in the caller.
 */
export function useRelatedRecord(documentIdentity: DocumentIdentityState | undefined): RelatedRecordOutcome {
  return useMemo<RelatedRecordOutcome>(() => {
    if (documentIdentity === undefined || documentIdentity === 'checking') {
      return { kind: 'absent' };
    }
    if (documentIdentity.kind !== 'resolved') {
      // 'new' | 'conflict' | 'indeterminate' | 'denied' | 'error' — none of these is an identified document
      // this card can read back; SaveModeSection already communicates each of these outcomes distinctly.
      return { kind: 'absent' };
    }
    if (!documentIdentity.relatedRecord) {
      return { kind: 'unassociated' };
    }
    return { kind: 'associated', ...toView(documentIdentity.relatedRecord) };
  }, [documentIdentity]);
}
