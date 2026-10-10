/**
 * documentAssociationWrite.ts
 *
 * FR-05 — Create-on-save optional parent-association write.
 *
 * Writes the user's optional matter / project / invoice / work-assignment
 * selection onto a newly created `sprk_document` record. "None" (no
 * selection) is a valid, first-class outcome — the document remains
 * standalone; `associateDocumentToParent` is a graceful no-op in that case
 * (Save is NEVER blocked on a parent per spec FR-05).
 *
 * Reuse, not reinvention (CLAUDE.md §11): `sprk_document` carries FOUR direct
 * lookup columns for these targets (confirmed live via
 * `describe('tables/sprk_document')`, 2026-07-09):
 *   - sprk_matter          -> sprk_matter
 *   - sprk_project         -> sprk_project
 *   - sprk_invoice         -> sprk_invoice
 *   - sprk_workassignment  -> sprk_workassignment
 * (NOT the 11-field ADR-024 polymorphic-resolver pattern used by
 * sprk_todo/sprk_event/sprk_communication — sprk_document's regarding-parent
 * columns are simple single-valued lookups, no `sprk_regarding{entity}`
 * discriminator set needed here.)
 *
 * UAC-r2 task 147 r1 (owner round 28 item 1): the write is the documents family's existing
 * `PUT /api/v1/documents/{id}` (its `matterLookup` / `projectLookup` / `invoiceLookup` / `workAssignmentLookup`), not a
 * client-side `@odata.bind` — filing a document under a record moves its owner (the Secure Record Owners team under a
 * secure record), and the BFF owns that. A refusal carries the server's message into the warning.
 *
 * @see AssociateToStep (`@spaarke/ui-components`) — the reused UI shell
 * @see CreateOnSaveAssociationPrompt.tsx — the UI that produces the
 *      `AssociationResult` this module writes
 * @see ADR-024 — Polymorphic Resolver Pattern (context; sprk_document itself
 *      does not use the 11-field variant)
 * @see ADR-012 — Shared Component Library (context-agnostic services)
 */

import type { AuthenticatedFetchFn } from '@spaarke/auth';
import { cleanGuid } from '@spaarke/ui-components';
import type { AssociationResult, EntityTypeOption } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// The four selectable parent target types (+ implicit "none")
// ---------------------------------------------------------------------------

/**
 * The four Dataverse logical names `sprk_document` can be associated to.
 * Mirrors the BFF's `GateAssociationTargetType` enum members (excluding
 * `None`, which is the absence of an `AssociationResult` on this side) — see
 * `gateAssociationContract.ts` for the tolerant-reader mapping between the
 * two shapes.
 */
export const DOCUMENT_ASSOCIATION_ENTITY_TYPES = [
  'sprk_matter',
  'sprk_project',
  'sprk_invoice',
  'sprk_workassignment',
] as const;

export type DocumentAssociationEntityType = (typeof DOCUMENT_ASSOCIATION_ENTITY_TYPES)[number];

/**
 * The four selectable parent types, in FR-05 display order, for
 * `AssociateToStep`'s `entityTypes` prop. "None" is NOT a member here — it is
 * modeled as "the user made no selection" (or explicitly cleared one), which
 * `AssociateToStep` already supports natively (`value == null`).
 */
export const DOCUMENT_ASSOCIATION_TARGETS: ReadonlyArray<EntityTypeOption> = [
  { label: 'Matter', entityType: 'sprk_matter' },
  { label: 'Project', entityType: 'sprk_project' },
  { label: 'Invoice', entityType: 'sprk_invoice' },
  { label: 'Work Assignment', entityType: 'sprk_workassignment' },
] as const;

/**
 * UAC-r2 task 147 r1: the `PUT /api/v1/documents/{id}` body property that files the document under each parent type
 * (the BFF's `UpdateDocumentRequest` — `MatterLookup`, `ProjectLookup`, `InvoiceLookup`, `WorkAssignmentLookup`, serialized
 * camelCase). The BFF writes the lookup and re-derives the owner.
 */
const DOCUMENT_LOOKUP_PROPERTY: Record<DocumentAssociationEntityType, string> = {
  sprk_matter: 'matterLookup',
  sprk_project: 'projectLookup',
  sprk_invoice: 'invoiceLookup',
  sprk_workassignment: 'workAssignmentLookup',
};

function _isDocumentAssociationEntityType(value: string): value is DocumentAssociationEntityType {
  return (DOCUMENT_ASSOCIATION_ENTITY_TYPES as readonly string[]).includes(value);
}

/**
 * How the document is re-filed: one call to `PUT /api/v1/documents/{id}` carrying the lookup. Rejects with the server's
 * message (the `ApiError` the BFF fetch throws).
 */
export type DocumentRefile = (documentId: string, body: Record<string, string>) => Promise<void>;

/**
 * UAC-r2 task 147 r1 (owner round 28 item 1): the {@link DocumentRefile} over a BFF-authenticated fetch. `bffBaseUrl` may
 * be `''` when the fetch resolves relative `/api` paths.
 */
export function bffDocumentRefile(
  authenticatedFetch: AuthenticatedFetchFn,
  bffBaseUrl: string
): DocumentRefile {
  return async (documentId, body) => {
    const url = `${(bffBaseUrl ?? '').replace(/\/+$/, '')}/api/v1/documents/${encodeURIComponent(documentId)}`;
    // `authenticatedFetch` THROWS an `ApiError` (message = the server's ProblemDetails detail/title) for a non-2xx, so a
    // refusal rejects here and `associateDocumentToParent`'s catch surfaces that message.
    await authenticatedFetch(url, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  };
}

// ---------------------------------------------------------------------------
// Result type
// ---------------------------------------------------------------------------

export interface IAssociateDocumentResult {
  /** True when the write succeeded OR there was nothing to write ("none"). */
  success: boolean;
  /** Set when `success` is false OR a non-fatal issue occurred (e.g. no nav-prop found). */
  warning?: string;
}

// ---------------------------------------------------------------------------
// associateDocumentToParent
// ---------------------------------------------------------------------------

/**
 * Writes the user's optional parent-association selection onto the new
 * `sprk_document` record. Never throws — mirrors the graceful-degradation
 * convention of `MatterService`/`applyResolverFields` (NFR-06-style
 * graceful-blank): a discovery or write failure returns `{ success: false,
 * warning }` rather than propagating, so a failed association write never
 * blocks the create-on-save completion that already happened.
 *
 * "None" (association is `null`/`undefined`) is a valid, common case — the
 * function no-ops and returns success immediately (spec FR-05: a standalone
 * Document is valid; Save is never blocked on a parent).
 *
 * @param refileDocument The document re-file through the BFF ({@link bffDocumentRefile}) — UAC-r2 task 147 r1: filing a
 *                       document under a record changes its owner, so it is never an `Xrm.WebApi` write.
 * @param documentId     GUID of the newly created `sprk_document` record.
 * @param association    The user's selection from `CreateOnSaveAssociationPrompt`,
 *                        or `null`/`undefined` for "none".
 */
export async function associateDocumentToParent(
  refileDocument: DocumentRefile,
  documentId: string,
  association: AssociationResult | null | undefined
): Promise<IAssociateDocumentResult> {
  if (!association || !association.recordId || !association.entityType) {
    // "None" — a standalone Document is valid. Nothing to write.
    return { success: true };
  }

  if (!_isDocumentAssociationEntityType(association.entityType)) {
    // Defensive: CreateOnSaveAssociationPrompt only offers the four supported
    // types, but guard against a caller passing an unsupported entityType.
    return {
      success: false,
      warning: `Unsupported association target type "${association.entityType}" for sprk_document.`,
    };
  }

  try {
    // cleanGuid (task 100): NEVER hand-roll `.replace(/[{}]/g,'')`.
    const cleanRecordId = cleanGuid(association.recordId);
    const cleanDocumentId = cleanGuid(documentId);

    // UAC-r2 task 147 r1 (owner round 28 item 1): filing the document under a record is a RE-FILE — it moves the
    // document into that record, so its owner follows (the Secure Record Owners team under a secure record). It goes
    // through the documents family's existing `PUT /api/v1/documents/{id}` (task 146: AppendTo on the target as the
    // caller, the owner re-derived and assigned), never through Xrm.WebApi.
    await refileDocument(cleanDocumentId, { [DOCUMENT_LOOKUP_PROPERTY[association.entityType]]: cleanRecordId });

    return { success: true };
  } catch (err) {
    const message = err instanceof Error ? err.message : 'Unknown error';
    console.error('[documentAssociationWrite] Failed to associate document:', err);
    return {
      success: false,
      warning: `Could not associate the document to ${association.recordName ?? association.entityType} (${message}). ` +
        'You can link it manually from the document record.',
    };
  }
}
