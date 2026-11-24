/**
 * reportCardService.ts
 * Service for the Create New Report Card wizard.
 *
 * Creates `sprk_reportcard` records through the BFF (`POST /api/v1/child-records/sprk_reportcard`), mirroring the
 * canonical `WorkAssignmentService.createWorkAssignment` flow (field-mapping-server-write-path-r1, task 027, FR-16):
 *   nav-prop discovery -> payload (manifest fields) -> applyResolverFields
 *   (host regarding, Matter/Project only) -> createRecordViaBffWithWarnings ->
 *   server warnings (never throws for non-fatal side-effects).
 *
 * The client does NOT map fields. The payload holds only what the user entered or picked plus the regarding keys; the
 * server applies the field mapping (fill-only, FR-02) from the filed-under Matter/Project. No `mappingSource` is sent:
 * the report card's regarding parent IS the mapping source and the route infers it (`InferMappingSourceTables` =
 * sprk_matter, sprk_project). Reads still go through IDataService.
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

import { describeWebApiCreateError } from '../../utils/webApiCreateError';
import { withBffChildWrites, createRecordViaBffWithWarnings } from '../../utils/adapters/bffChildWriteAdapter';
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
// Assigned-resource lookup config (8 fields — manifest §). Each hint is the lookup's FULL logical column name:
// findNavProp matches an exact column first, whereas a fragment binds the first column CONTAINING it, which is whichever
// look-alike Dataverse lists first.
// ---------------------------------------------------------------------------

interface IResourceLookupConfig {
  idField: keyof ICreateReportCardFormState;
  referencedEntity: 'contact' | 'sprk_organization';
  entitySet: 'contacts' | 'sprk_organizations';
  /** Full logical column name passed to findNavProp as the hint (matched exactly; see the config comment). */
  navPropHint: string;
}

const RESOURCE_LOOKUPS: readonly IResourceLookupConfig[] = [
  {
    idField: 'assignedAttorney1Id',
    referencedEntity: 'contact',
    entitySet: 'contacts',
    navPropHint: 'sprk_assignedattorney1',
  },
  {
    idField: 'assignedAttorney2Id',
    referencedEntity: 'contact',
    entitySet: 'contacts',
    navPropHint: 'sprk_assignedattorney2',
  },
  {
    idField: 'assignedParalegal1Id',
    referencedEntity: 'contact',
    entitySet: 'contacts',
    navPropHint: 'sprk_assignedparalegal1',
  },
  {
    idField: 'assignedParalegal2Id',
    referencedEntity: 'contact',
    entitySet: 'contacts',
    navPropHint: 'sprk_assignedparalegal2',
  },
  {
    idField: 'assignedToLawFirm1Id',
    referencedEntity: 'sprk_organization',
    entitySet: 'sprk_organizations',
    navPropHint: 'sprk_assignedtolawfirm1',
  },
  {
    idField: 'assignedLawFirm2Id',
    referencedEntity: 'sprk_organization',
    entitySet: 'sprk_organizations',
    navPropHint: 'sprk_assignedlawfirm2',
  },
  {
    idField: 'assignedToExternalId',
    referencedEntity: 'contact',
    entitySet: 'contacts',
    navPropHint: 'sprk_assignedtoexternal',
  },
  {
    idField: 'assignedToInternalId',
    referencedEntity: 'contact',
    entitySet: 'contacts',
    navPropHint: 'sprk_assignedtointernal',
  },
] as const;

// ---------------------------------------------------------------------------
// ReportCardService class
// ---------------------------------------------------------------------------

export class ReportCardService {
  private readonly _dataService: IDataService;
  private readonly _authenticatedFetch: AuthenticatedFetchFn;
  private readonly _bffBaseUrl: string;

  /**
   * @param authenticatedFetch Injected BFF-authenticated fetch — the report card is created on the BFF child-record
   *   route with it. Mirrors the injected shape already used by `InvoiceService`/`WorkAssignmentService`.
   * @param bffBaseUrl BFF API base URL, same convention as the sibling services.
   */
  constructor(dataService: IDataService, authenticatedFetch: AuthenticatedFetchFn, bffBaseUrl: string) {
    // UAC-r2 task 147 r1 (owner round 28 item 1): every child create / re-file this service makes goes through the BFF
    // (G5) — the server decides the owner; nothing is created as the user.
    this._dataService = withBffChildWrites(dataService, authenticatedFetch, bffBaseUrl);
    // Stored (not just forwarded to withBffChildWrites): the report card is created on the BFF route directly so the
    // server's warnings reach the result.
    this._authenticatedFetch = authenticatedFetch;
    this._bffBaseUrl = bffBaseUrl;
  }

  /**
   * Full report card creation flow:
   *   1. Discover sprk_reportcard nav-props.
   *   2. Build payload from the manifest (name required, narrative, due date,
   *      8 assigned-resource lookups).
   *   3. Apply host regarding (Matter/Project) via applyResolverFields.
   *   4. POST the payload to the BFF child-record route; the server maps (fill-only) from the regarding parent.
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
    }

    let reportCardId: string;
    try {
      console.info('[ReportCardService] create payload:', JSON.stringify(entity, null, 2));
      // No mapping source: the regarding Matter/Project IS the source and the route infers it. A server warning (a
      // mapped value not written, a source that could not be read) is shown like any other.
      const created = await createRecordViaBffWithWarnings(
        this._authenticatedFetch,
        this._bffBaseUrl,
        'sprk_reportcard',
        entity
      );
      reportCardId = created.id;
      warnings.push(...created.warnings);
      console.info('[ReportCardService] create success, reportCardId:', reportCardId);
    } catch (err) {
      console.error('[ReportCardService] create error:', err);
      // #2382: a privilege refusal becomes one plain sentence; the full error is logged above.
      const described = describeWebApiCreateError(err, 'report card');
      const message = described.message;
      return {
        status: 'error',
        errorMessage: described.isAccessDenied ? message : `Failed to create report card record: ${message}`,
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
