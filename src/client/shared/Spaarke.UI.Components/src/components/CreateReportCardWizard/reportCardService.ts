/**
 * reportCardService.ts
 * Service for the Create New Report Card wizard.
 *
 * Creates `sprk_reportcard` records in Dataverse via IDataService, mirroring
 * the canonical `WorkAssignmentService.createWorkAssignment` flow per task 040
 * (visual-host-create-button-r1):
 *   nav-prop discovery -> payload (manifest fields) -> applyResolverFields
 *   (host regarding, Matter/Project only) -> createRecord('sprk_reportcard') ->
 *   warnings (never throws for non-fatal side-effects).
 *
 * NO file pipeline (owner decision — no Add Files step for this wizard; see
 * notes/field-manifests/reportcard.md). No Business-Unit-defaults cascade
 * either — that cascade exists to route `sprk_containerid` /
 * `sprk_searchindexname` for SPE file uploads + RAG indexing, neither of
 * which applies here (sprk_reportcard's `sprk_containerid` column is
 * deliberately left unpopulated by this wizard).
 *
 * Field manifest (Phase-0-validated, resolver-ready, zero schema gap):
 *   sprk_name (NOT NULL), sprk_narrative, sprk_duedate, plus 8
 *   assigned-resource lookups (sprk_assignedattorney1/2,
 *   sprk_assignedparalegal1/2, sprk_assignedtolawfirm1, sprk_assignedlawfirm2,
 *   sprk_assignedtoexternal, sprk_assignedtointernal — note the asymmetric
 *   "assignedtolawfirm1" vs "assignedlawfirm2" naming, real schema names,
 *   not "fixed").
 *
 * `sprk_reportcardnumber` is deliberately NOT set client-side (manifest:
 * "likely autonumber or set server-side; no NOT NULL constraint observed").
 *
 * Dependencies are injected via constructor -- no solution-specific imports.
 *
 * @see CreateWorkAssignmentWizard/workAssignmentService.ts — canonical reference
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see projects/visual-host-create-button-r1/notes/field-manifests/reportcard.md
 */

import type { ICreateReportCardFormState } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import type { AssociationResult } from '../AssociateToStep/types';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import {
  applyResolverFields,
  findNavProp,
  discoverNavProps,
  cleanGuid,
  _resetNavPropCacheForTests,
} from '../../services/PolymorphicResolverService';
import { applyFieldMappings } from '../../services/FieldMappingService';

// ---------------------------------------------------------------------------
// Result type
// ---------------------------------------------------------------------------

export interface ICreateReportCardResult {
  status: 'success' | 'partial' | 'error';
  reportCardId?: string;
  reportCardName?: string;
  errorMessage?: string;
  warnings: string[];
}

// ---------------------------------------------------------------------------
// Metadata discovery (same pattern as WorkAssignmentService/InvoiceService)
// ---------------------------------------------------------------------------

// Nav-prop discovery is provided by the shared `discoverNavProps`
// (PolymorphicResolverService) — consolidated 2026-07-09 (task 011, Path A).
// Previously a private per-service copy.

/**
 * @internal — for tests, reset the shared module-level nav-prop cache between
 * cases. Delegates to the shared `_resetNavPropCacheForTests` (clears the whole
 * shared cache — a superset of the previous `sprk_reportcard` clear; safe for
 * test isolation).
 */
export function _resetReportCardServiceNavPropCacheForTests(): void {
  _resetNavPropCacheForTests();
}

// ---------------------------------------------------------------------------
// Host entity-set / lookup-hint resolution (Matter + Project only — manifest §)
// ---------------------------------------------------------------------------

const _ENTITY_SET_MAP: Record<string, string> = {
  sprk_matter: 'sprk_matters',
  sprk_project: 'sprk_projects',
};

function _resolveEntitySet(entityLogicalName: string): string {
  return _ENTITY_SET_MAP[entityLogicalName] ?? `${entityLogicalName}s`;
}

function _resolveLookupHint(entityLogicalName: string): string {
  return entityLogicalName.startsWith('sprk_') ? entityLogicalName.slice('sprk_'.length) : entityLogicalName;
}

// ---------------------------------------------------------------------------
// Assigned-resource lookup config (8 fields — manifest §, note-hints must be
// unique among lookups sharing the same referencedEntity so findNavProp's
// disambiguation-by-substring works correctly).
// ---------------------------------------------------------------------------

interface IResourceLookupConfig {
  idField: keyof ICreateReportCardFormState;
  referencedEntity: 'contact' | 'sprk_organization';
  entitySet: 'contacts' | 'sprk_organizations';
  /** Hint passed to findNavProp — must be a substring unique to this column
   * among sibling lookups referencing the same entity (see doc comment). */
  navPropHint: string;
}

const RESOURCE_LOOKUPS: readonly IResourceLookupConfig[] = [
  { idField: 'assignedAttorney1Id', referencedEntity: 'contact', entitySet: 'contacts', navPropHint: 'attorney1' },
  { idField: 'assignedAttorney2Id', referencedEntity: 'contact', entitySet: 'contacts', navPropHint: 'attorney2' },
  { idField: 'assignedParalegal1Id', referencedEntity: 'contact', entitySet: 'contacts', navPropHint: 'paralegal1' },
  { idField: 'assignedParalegal2Id', referencedEntity: 'contact', entitySet: 'contacts', navPropHint: 'paralegal2' },
  {
    idField: 'assignedToLawFirm1Id',
    referencedEntity: 'sprk_organization',
    entitySet: 'sprk_organizations',
    navPropHint: 'lawfirm1',
  },
  {
    idField: 'assignedLawFirm2Id',
    referencedEntity: 'sprk_organization',
    entitySet: 'sprk_organizations',
    navPropHint: 'lawfirm2',
  },
  { idField: 'assignedToExternalId', referencedEntity: 'contact', entitySet: 'contacts', navPropHint: 'external' },
  { idField: 'assignedToInternalId', referencedEntity: 'contact', entitySet: 'contacts', navPropHint: 'internal' },
] as const;

// ---------------------------------------------------------------------------
// ReportCardService class
// ---------------------------------------------------------------------------

export class ReportCardService {
  private readonly _dataService: IDataService;
  private readonly _authenticatedFetch: AuthenticatedFetchFn;
  private readonly _bffBaseUrl: string;

  /**
   * @param authenticatedFetch Injected BFF-authenticated fetch — required so the
   *   field-mapping engine (task 022) can fetch the {host -> Report Card}
   *   profile. Mirrors the injected shape already used by
   *   `InvoiceService`/`WorkAssignmentService`.
   * @param bffBaseUrl BFF API base URL, same convention as the sibling services.
   */
  constructor(dataService: IDataService, authenticatedFetch: AuthenticatedFetchFn, bffBaseUrl: string) {
    this._dataService = dataService;
    this._authenticatedFetch = authenticatedFetch;
    this._bffBaseUrl = bffBaseUrl;
  }

  /**
   * Full report card creation flow:
   *   1. Discover sprk_reportcard nav-props.
   *   2. Build payload from the manifest (name required, narrative, due date,
   *      8 assigned-resource lookups).
   *   3. Apply host regarding (Matter/Project) via applyResolverFields.
   *   4. createRecord('sprk_reportcard', entity).
   *
   * @param form         Enter Info manifest field values.
   * @param association  The host record this report card is regarding (from
   *                     AssociateToStep / the Visual Host locked-launch
   *                     seed), or `null` when no association was made.
   *
   * Returns ICreateReportCardResult -- never throws.
   */
  async createReportCard(
    form: ICreateReportCardFormState,
    association: AssociationResult | null
  ): Promise<ICreateReportCardResult> {
    const warnings: string[] = [];

    const navProps = await discoverNavProps('sprk_reportcard');

    const entity: Record<string, unknown> = {
      sprk_name: form.name.trim(),
    };

    if (form.narrative?.trim()) {
      entity['sprk_narrative'] = form.narrative.trim();
    }
    if (form.dueDate) {
      entity['sprk_duedate'] = form.dueDate;
    }

    // Helper: bind a lookup if the nav-prop exists on the entity (same
    // graceful-skip pattern as WorkAssignmentService/InvoiceService).
    const bindLookup = (referencedEntity: string, entitySet: string, guid: string, columnHint: string) => {
      const navProp = findNavProp(navProps, referencedEntity, columnHint);
      if (navProp) {
        entity[`${navProp}@odata.bind`] = `/${entitySet}(${cleanGuid(guid)})`;
      } else {
        console.warn(
          `[ReportCardService] No nav-prop found for ${referencedEntity} (hint: ${columnHint}), skipping binding`
        );
      }
    };

    // 8 assigned-resource lookups (manifest §, all optional).
    for (const cfg of RESOURCE_LOOKUPS) {
      const guid = form[cfg.idField] as string;
      if (guid) {
        bindLookup(cfg.referencedEntity, cfg.entitySet, guid, cfg.navPropHint);
      }
    }

    // Host regarding (Matter or Project only) — ADR-024 Polymorphic Resolver.
    // Writes the entity-specific @odata.bind lookup AND all 5 denormalized
    // resolver fields via the shared `applyResolverFields` primitive.
    if (association?.recordId && association.entityType) {
      const entitySet = _resolveEntitySet(association.entityType);
      const hint = _resolveLookupHint(association.entityType);
      const polyWebApi = {
        retrieveMultipleRecords: (entityLogicalName: string, query: string) =>
          this._dataService.retrieveMultipleRecords(entityLogicalName, query),
      };
      await applyResolverFields(
        polyWebApi,
        entity,
        navProps,
        association.entityType,
        entitySet,
        association.recordId,
        association.recordName,
        hint
      );

      // Field-mapping engine (task 022) — apply the configured {host -> Report
      // Card} field-mapping profile (if any) onto the SAME payload, after
      // resolver fields and before createRecord. Graceful no-op when no
      // profile is configured (profileFound:false) — mirrors
      // applyResolverFields' NFR-06 no-op contract; the engine never throws.
      // Target field names are NEVER hard-coded here — they come from the
      // seeded profile/rules (task 030).
      const mappingResult = await applyFieldMappings({
        sourceEntity: association.entityType,
        sourceId: cleanGuid(association.recordId),
        targetEntity: 'sprk_reportcard',
        payload: entity,
        dataService: this._dataService,
        authenticatedFetch: this._authenticatedFetch,
        bffBaseUrl: this._bffBaseUrl,
      });
      if (mappingResult.warnings.length > 0) {
        warnings.push(...mappingResult.warnings);
      }
    }

    let reportCardId: string;
    try {
      console.info('[ReportCardService] createRecord payload:', JSON.stringify(entity, null, 2));
      reportCardId = await this._dataService.createRecord('sprk_reportcard', entity);
      console.info('[ReportCardService] createRecord success, reportCardId:', reportCardId);
    } catch (err) {
      console.error('[ReportCardService] createRecord error:', err);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const errObj = err as any;
      const message = errObj?.message || (err instanceof Error ? err.message : 'Unknown error');
      return {
        status: 'error',
        errorMessage: `Failed to create report card record: ${message}`,
        warnings: [],
      };
    }

    return {
      status: warnings.length > 0 ? 'partial' : 'success',
      reportCardId,
      reportCardName: form.name.trim(),
      warnings,
    };
  }
}
