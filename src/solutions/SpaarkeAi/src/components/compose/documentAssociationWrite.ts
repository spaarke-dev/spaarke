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
 * The nav-prop discovery + `@odata.bind` write mechanics are the SAME
 * mechanism already used by matter/project/invoice/work-assignment record
 * creation (`CreateMatterWizard`/`CreateEventWizard`/etc. via
 * `discoverNavProps` + `findNavProp` from `@spaarke/ui-components`,
 * consolidated `set-regarding-and-field-mapping-resolver-r2` task 011). This
 * module does NOT fork a new association surface — it is a thin,
 * document-specific consumer of that existing mechanism, reused unmodified.
 *
 * @see AssociateToStep (`@spaarke/ui-components`) — the reused UI shell
 * @see CreateOnSaveAssociationPrompt.tsx — the UI that produces the
 *      `AssociationResult` this module writes
 * @see ADR-024 — Polymorphic Resolver Pattern (context; sprk_document itself
 *      does not use the 11-field variant)
 * @see ADR-012 — Shared Component Library (context-agnostic services)
 */

import { discoverNavProps, findNavProp } from '@spaarke/ui-components';
import type { AssociationResult, EntityTypeOption, IDataService } from '@spaarke/ui-components';

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
 * Entity-set (collection) name per parent logical name, for the
 * `@odata.bind` URL. Mirrors the same map already duplicated across the
 * wizard services (`CreateEventWizard/eventService.ts` `_ENTITY_SET_MAP`,
 * `CreateWorkAssignmentWizard/workAssignmentService.ts`, etc.) — scoped here
 * to exactly the four types `sprk_document` supports directly.
 */
const _ENTITY_SET_MAP: Record<DocumentAssociationEntityType, string> = {
  sprk_matter: 'sprk_matters',
  sprk_project: 'sprk_projects',
  sprk_invoice: 'sprk_invoices',
  sprk_workassignment: 'sprk_workassignments',
};

function _isDocumentAssociationEntityType(value: string): value is DocumentAssociationEntityType {
  return (DOCUMENT_ASSOCIATION_ENTITY_TYPES as readonly string[]).includes(value);
}

/** Strips the `sprk_` prefix for the `findNavProp` column-hint disambiguator (e.g. `sprk_matter` -> `matter`). */
function _resolveLookupHint(entityType: string): string {
  return entityType.startsWith('sprk_') ? entityType.slice('sprk_'.length) : entityType;
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
 * @param dataService   Context-agnostic Dataverse write surface (ADR-012).
 *                       Production callers supply `createXrmDataService()`
 *                       (client-side Dataverse call, per ADR-028 — this is
 *                       NOT a BFF fetch, so there is no raw-Bearer concern).
 * @param documentId     GUID of the newly created `sprk_document` record.
 * @param association    The user's selection from `CreateOnSaveAssociationPrompt`,
 *                        or `null`/`undefined` for "none".
 */
export async function associateDocumentToParent(
  dataService: IDataService,
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
    const navPropEntries = await discoverNavProps('sprk_document');
    const navProp = findNavProp(navPropEntries, association.entityType, _resolveLookupHint(association.entityType));

    if (!navProp) {
      console.warn(
        `[documentAssociationWrite] No nav-prop discovered for sprk_document -> ${association.entityType}; ` +
          'falling back to the column logical name.'
      );
    }

    const cleanRecordId = association.recordId.replace(/[{}]/g, '').toLowerCase();
    const entitySet = _ENTITY_SET_MAP[association.entityType];
    const bindProp = navProp ?? association.entityType;

    await dataService.updateRecord('sprk_document', documentId, {
      [`${bindProp}@odata.bind`]: `/${entitySet}(${cleanRecordId})`,
    });

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
