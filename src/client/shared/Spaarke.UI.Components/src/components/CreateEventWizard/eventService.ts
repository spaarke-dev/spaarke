/**
 * eventService.ts
 * Event creation service for the Create New Event wizard.
 *
 * Creates a sprk_event record in Dataverse via IDataService.
 * Follows the same nav-prop discovery pattern as ProjectService.
 *
 * @see IDataService — high-level data access abstraction (no IWebApi dependency)
 */

import type { ICreateEventFormState } from './formTypes';
import type { ILookupItem } from '../../types/LookupTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import type { IWebApiLike } from '../../types/WebApiLike';
import { EntityCreationService } from '../../services/EntityCreationService';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import { applyResolverFields, discoverNavProps, cleanGuid } from '../../services/PolymorphicResolverService';
import { applyFieldMappings } from '../../services/FieldMappingService';

// ---------------------------------------------------------------------------
// Result types
// ---------------------------------------------------------------------------

export interface ICreateEventResult {
  eventId?: string;
  eventName?: string;
  success: boolean;
  errorMessage?: string;
  /** Non-fatal warnings (e.g. field-mapping profile fetch failures). Empty when none. */
  warnings: string[];
}

// ---------------------------------------------------------------------------
// Metadata discovery
// ---------------------------------------------------------------------------

// Nav-prop discovery is provided by the shared `discoverNavProps`
// (PolymorphicResolverService) — consolidated 2026-07-09 (task 011, Path A).
// The local `NavPropEntry` shape below is structurally identical to the shared
// `INavPropEntry` returned by `discoverNavProps`, so `_findNavProp` accepts it.

interface NavPropEntry {
  columnName: string;
  navPropName: string;
  referencedEntity: string;
}

function _findNavProp(entries: NavPropEntry[], referencedEntity: string, columnHint?: string): string | undefined {
  const matches = entries.filter(e => e.referencedEntity === referencedEntity);
  if (matches.length === 0) return undefined;
  if (matches.length === 1) return matches[0].navPropName;
  if (columnHint) {
    const hinted = matches.find(e => e.columnName.includes(columnHint));
    if (hinted) return hinted.navPropName;
  }
  return matches[0].navPropName;
}

// ---------------------------------------------------------------------------
// Regarding-parent resolution helpers (ADR-024)
// ---------------------------------------------------------------------------

/**
 * Explicit logical-name → entity-set (collection) name map for the parent
 * types `sprk_event` can be regarding, used to build the `@odata.bind` URL in
 * `applyResolverFields`. Irregular plurals (`sprk_analysis` → `sprk_analyses`)
 * and OOB entities (`account`/`contact`) are enumerated here; everything else
 * falls back to the naive `${logicalName}s` rule.
 */
const _ENTITY_SET_MAP: Record<string, string> = {
  sprk_matter: 'sprk_matters',
  sprk_project: 'sprk_projects',
  sprk_invoice: 'sprk_invoices',
  sprk_workassignment: 'sprk_workassignments',
  sprk_budget: 'sprk_budgets',
  sprk_analysis: 'sprk_analyses',
  account: 'accounts',
  contact: 'contacts',
};

/** Resolve the entity-set name for a parent entity logical name. */
function _resolveEntitySet(entityLogicalName: string): string {
  return _ENTITY_SET_MAP[entityLogicalName] ?? `${entityLogicalName}s`;
}

/**
 * Derive the column hint used by `findNavProp` to disambiguate the
 * entity-specific regarding lookup when several nav-props reference the same
 * entity (e.g. `sprk_regardingmatter` includes "matter"). Strips the `sprk_`
 * prefix so `sprk_matter` → `matter`; OOB names (`account`) are returned as-is.
 */
function _resolveLookupHint(entityLogicalName: string): string {
  return entityLogicalName.startsWith('sprk_') ? entityLogicalName.slice('sprk_'.length) : entityLogicalName;
}

// ---------------------------------------------------------------------------
// Current-user resolution (Xrm-host best-effort)
// ---------------------------------------------------------------------------

/**
 * Best-effort resolution of the current Dataverse user GUID from the host Xrm global.
 *
 * Matches the established `CreateWorkAssignmentWizard/workAssignmentService._getCurrentUserId`
 * pattern: walks `window`, `window.parent`, `window.top` (cross-origin safe) looking first for
 * `Xrm.Utility.getGlobalContext().userSettings.userId` (Code Page hosted in a Power App iframe),
 * then falling back to `Xrm.Utility.getUserId()` (PCF / direct host).
 *
 * Returns `null` when unavailable so callers can degrade gracefully — the BU cascade is
 * optional and the BFF tenant-default chain handles the fallback server-side.
 *
 * @returns Current user GUID (braces stripped, lowercased), or `null` if Xrm is unreachable.
 */
function _tryGetCurrentUserId(): string | null {
  const frames: Window[] = [window];
  try {
    if (window.parent && window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin */
  }
  try {
    if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top!);
  } catch {
    /* cross-origin */
  }

  for (const frame of frames) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (frame as any).Xrm;
      if (xrm?.Utility?.getGlobalContext) {
        const ctx = xrm.Utility.getGlobalContext();
        const userId = ctx?.userSettings?.userId;
        if (typeof userId === 'string' && userId.trim() !== '') {
          return userId.replace(/^\{|\}$/g, '').toLowerCase();
        }
      }
      if (typeof xrm?.Utility?.getUserId === 'function') {
        const userId = xrm.Utility.getUserId();
        if (typeof userId === 'string' && userId.trim() !== '') {
          return userId.replace(/^\{|\}$/g, '').toLowerCase();
        }
      }
    } catch {
      // Cross-origin frame — skip
    }
  }
  return null;
}

/**
 * Adapt an {@link IDataService} into an {@link IWebApiLike} suitable for
 * `EntityCreationService.resolveUserBuDefaults`.
 */
function _toWebApiLike(dataService: IDataService): IWebApiLike {
  return {
    retrieveRecord: (entityType, id, options) => dataService.retrieveRecord(entityType, id, options),
    retrieveMultipleRecords: (entityType, options) => dataService.retrieveMultipleRecords(entityType, options),
  };
}

// ---------------------------------------------------------------------------
// EventService class
// ---------------------------------------------------------------------------

export class EventService {
  constructor(
    private readonly _dataService: IDataService,
    /**
     * Injected BFF-authenticated fetch. Optional — only required to activate the
     * Field Mapping Framework engine (task 020, spec FR-12). Lookup-only callers
     * (e.g. searchEventTypes) may omit it. When absent, the engine call is a
     * graceful no-op — identical to today's behavior.
     */
    private readonly _authenticatedFetch?: AuthenticatedFetchFn,
    /** BFF API base URL. See {@link _authenticatedFetch}. */
    private readonly _bffBaseUrl?: string
  ) {}

  /**
   * Search sprk_eventtype_ref records by name fragment.
   */
  async searchEventTypes(nameFilter: string): Promise<ILookupItem[]> {
    if (!nameFilter || nameFilter.trim().length < 1) return [];

    const safeFilter = nameFilter.trim().replace(/'/g, "''");
    const query =
      `?$select=sprk_eventtype_refid,sprk_name` +
      `&$filter=contains(sprk_name,'${safeFilter}')` +
      `&$orderby=sprk_name asc` +
      `&$top=10`;

    try {
      const result = await this._dataService.retrieveMultipleRecords('sprk_eventtype_ref', query);
      return result.entities.map(e => ({
        id: e['sprk_eventtype_refid'] as string,
        name: e['sprk_name'] as string,
      }));
    } catch (err) {
      console.error('[EventService] searchEventTypes error:', err);
      throw err;
    }
  }

  /**
   * Create a sprk_event record in Dataverse.
   *
   * @param formValues - Event form state. When `regardingRecordId` is set and
   *   `regardingEntityName` is provided, the event is linked to the parent
   *   matter or project via the appropriate nav-prop discovered at runtime.
   * @param regardingEntityName - Optional logical name of the parent entity
   *   (e.g. 'sprk_matter', 'sprk_project', 'sprk_invoice'). When supplied
   *   together with `formValues.regardingRecordId`, the event is associated via
   *   the ADR-024 Polymorphic Resolver (`applyResolverFields`): the
   *   entity-specific `@odata.bind` lookup AND all 5 denormalized resolver
   *   fields (`sprk_regardingrecordtype`/`id`/`name`/`url`/`number`) are
   *   written. `formValues.regardingRecordName` supplies the display-name
   *   fallback per SRFR-052.
   * @param options - Optional injection points for testing:
   *   - `getCurrentUserId`: override of the Xrm.Utility.getUserId() probe (returns
   *     a GUID without braces, or `null` when no user context is available).
   *
   * FR-WIZ-05 (spaarke-multi-container-multi-index-r1): The Event create payload
   * MUST include BOTH `sprk_containerid` AND `sprk_searchindexname` sourced from
   * the current user's owning Business Unit. INV-5 contract honored — explicit
   * overrides on the payload are preserved.
   */
  async createEvent(
    formValues: ICreateEventFormState,
    regardingEntityName?: string,
    options?: { getCurrentUserId?: () => string | null }
  ): Promise<ICreateEventResult> {
    const warnings: string[] = [];
    const navProps = await discoverNavProps('sprk_event');

    const entity: Record<string, unknown> = {
      sprk_eventname: formValues.eventName.trim(),
      sprk_priority: formValues.priority,
    };

    if (formValues.description?.trim()) {
      entity['sprk_description'] = formValues.description.trim();
    }
    if (formValues.dueDate) {
      entity['sprk_duedate'] = formValues.dueDate;
    }

    // -------------------------------------------------------------------------
    // FR-WIZ-05 (spaarke-multi-container-multi-index-r1): cascade BOTH
    // `sprk_containerid` and `sprk_searchindexname` from the current user's
    // owning Business Unit onto the create payload.
    //
    // INV-5 contract: if the payload already has an explicit non-empty value
    // for either field, DO NOT overwrite. `EntityCreationService.applyUserBuDefaults`
    // enforces this guard per-field independently.
    //
    // Non-fatal: if userId resolution fails (no Xrm host), or the BU has no
    // value for one or both fields, leave the corresponding field unset — the
    // BFF tenant-default chain handles the fallback server-side. We log a
    // warning instead of aborting event creation.
    //
    // Reference: matterService.ts FR-WIZ-01 cascade block (lines ~274-319).
    // -------------------------------------------------------------------------
    try {
      const resolveUserId = options?.getCurrentUserId ?? _tryGetCurrentUserId;
      const userId = resolveUserId();
      if (userId) {
        const webApi = _toWebApiLike(this._dataService);
        const buDefaults = await EntityCreationService.resolveUserBuDefaults(webApi, userId);
        const applied = EntityCreationService.applyUserBuDefaults(entity, buDefaults);
        if (applied.containerIdSet) {
          console.info(
            '[EventService] Cascaded sprk_containerid from user BU:',
            buDefaults.containerId,
            '(BU:',
            buDefaults.businessUnitId,
            ')'
          );
        } else if (buDefaults.containerId) {
          console.info('[EventService] sprk_containerid already explicitly set on payload — preserving (INV-5).');
        }
        if (applied.searchIndexNameSet) {
          console.info(
            '[EventService] Cascaded sprk_searchindexname from user BU:',
            buDefaults.searchIndexName,
            '(BU:',
            buDefaults.businessUnitId,
            ')'
          );
        } else if (buDefaults.searchIndexName) {
          console.info('[EventService] sprk_searchindexname already explicitly set on payload — preserving (INV-5).');
        }
        if (!buDefaults.containerId && !buDefaults.searchIndexName) {
          console.info(
            '[EventService] User BU has neither sprk_containerid nor sprk_searchindexname — leaving payload fields unset; BFF tenant-default chain will apply.'
          );
        }
        // Phase G: cascade BU's `sprk_ai_search_index` lookup onto the new Event.
        if (buDefaults.searchIndexId) {
          const aiNavProp = _findNavProp(navProps, 'sprk_aisearchindex');
          if (aiNavProp) {
            entity[`${aiNavProp}@odata.bind`] = `/sprk_aisearchindexes(${cleanGuid(buDefaults.searchIndexId)})`;
            console.info(
              '[EventService] Cascaded sprk_ai_search_index from user BU:',
              buDefaults.searchIndexId,
              '(BU:',
              buDefaults.businessUnitId,
              ')'
            );
          } else {
            console.warn(
              '[EventService] sprk_ai_search_index nav-prop not discovered on sprk_event — lookup cascade skipped.'
            );
          }
        }
      } else {
        console.warn(
          '[EventService] Xrm.Utility.getUserId() unavailable — skipping BU cascade for sprk_containerid / sprk_searchindexname. BFF tenant-default will apply server-side.'
        );
      }
    } catch (err) {
      // Cascade is best-effort; never block event creation on it.
      console.warn(
        '[EventService] Failed to cascade BU defaults (non-fatal):',
        err instanceof Error ? err.message : err
      );
    }

    // Event type lookup
    if (formValues.eventTypeId) {
      const navProp = _findNavProp(navProps, 'sprk_eventtype_ref');
      if (navProp) {
        entity[`${navProp}@odata.bind`] = `/sprk_eventtype_refs(${cleanGuid(formValues.eventTypeId)})`;
      }
    }

    // Assigned To (contact) -- spaarkeai-assistant-enhancements-r1 task 014 /
    // FR-A4. Optional: an event/task may be created with no assignee. The
    // nav-prop is discovered dynamically (never hardcoded) per the
    // established `discoverNavProps`/`_findNavProp` pattern used throughout
    // this method -- avoids the SchemaName/LogicalName @odata.bind casing
    // gotcha (docs/standards/ODATA-NAMING-CONVENTION.md).
    if (formValues.assignedToId) {
      const navProp = _findNavProp(navProps, 'contact', 'assignedto');
      if (navProp) {
        entity[`${navProp}@odata.bind`] = `/contacts(${cleanGuid(formValues.assignedToId)})`;
      } else {
        warnings.push('Assignee could not be linked -- the Assigned To lookup was not found on this record type.');
      }
    }

    // Regarding record (parent) — ADR-024 Polymorphic Resolver.
    //
    // Writes the entity-specific `@odata.bind` lookup AND all 5 denormalized
    // resolver fields (`sprk_regardingrecordtype` → `sprk_recordtype_ref`,
    // `sprk_regardingrecordid`, `sprk_regardingrecordname`,
    // `sprk_regardingrecordurl`, `sprk_regardingrecordnumber`) via the shared
    // `applyResolverFields` primitive — the ADR-024-mandated flow, mirroring
    // the canonical `WorkAssignmentService`.
    //
    // This replaces the pre-existing ad-hoc matter/project-only map that bound
    // only the entity-specific lookup and skipped every resolver field — a
    // shipped ADR-024 violation (§ "MUST NOT skip resolver fields"). Migrating
    // here is a correctness fix, not a new deviation.
    //
    // Mutual-exclusion (ADR-024 MUST): exactly one parent is supplied per
    // create, so only one entity-specific lookup is ever populated.
    //
    // Graceful degradation (NFR-06): `applyResolverFields` never throws for a
    // missing catalog row or target-field value — it warns and skips the
    // affected write, so event creation is never blocked by resolver metadata.
    if (formValues.regardingRecordId && regardingEntityName) {
      const entitySet = _resolveEntitySet(regardingEntityName);
      const hint = _resolveLookupHint(regardingEntityName);
      // `applyResolverFields` expects an IPolymorphicWebApi (retrieveMultipleRecords
      // with an optional maxPageSize). Wrap IDataService, which omits the third arg.
      const polyWebApi = {
        retrieveMultipleRecords: (entityLogicalName: string, query: string) =>
          this._dataService.retrieveMultipleRecords(entityLogicalName, query),
      };
      await applyResolverFields(
        polyWebApi,
        entity,
        navProps,
        regardingEntityName,
        entitySet,
        formValues.regardingRecordId,
        formValues.regardingRecordName,
        hint
      );

      // Field Mapping Framework (spec FR-12, task 020): apply the configured
      // {parent -> sprk_event} mapping profile (if any) onto the create payload.
      // Runs AFTER applyResolverFields (regarding fields already written) and
      // BEFORE createRecord, on the SAME payload object. The engine never
      // throws and is a graceful no-op when no profile exists or the BFF deps
      // are unavailable (e.g. a lookup-only construction site) -- no behavior
      // change in either case. No source===target guard (same-entity mapping
      // is a supported scenario per ADR-024/design.md §10).
      if (this._authenticatedFetch && this._bffBaseUrl) {
        const mappingResult = await applyFieldMappings({
          sourceEntity: regardingEntityName,
          sourceId: formValues.regardingRecordId,
          targetEntity: 'sprk_event',
          payload: entity,
          dataService: this._dataService,
          authenticatedFetch: this._authenticatedFetch,
          bffBaseUrl: this._bffBaseUrl,
        });
        warnings.push(...mappingResult.warnings);
      }
    }

    try {
      const id = await this._dataService.createRecord('sprk_event', entity);
      return {
        eventId: id,
        eventName: formValues.eventName.trim(),
        success: true,
        warnings,
      };
    } catch (err) {
      console.error('[EventService] createRecord error:', err);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const errObj = err as any;
      const message = errObj?.message || (err instanceof Error ? err.message : 'Unknown error');
      return {
        success: false,
        errorMessage: `Failed to create event record: ${message}`,
        warnings,
      };
    }
  }
}
