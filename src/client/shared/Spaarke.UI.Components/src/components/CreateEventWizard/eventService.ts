/**
 * eventService.ts
 * Event creation service for the Create New Event wizard.
 *
 * Creates a sprk_event record through the BFF child-records route
 * (`POST /api/v1/child-records/sprk_event`). The wizard sends only what the user entered or picked, plus the regarding
 * keys the route files the event by; the server owns field mapping (fill-only, FR-02), the owner (FR-09), the
 * search-index default (FR-38/FR-40), the I-1 stamp and the event number (the platform autonumber, I-11)
 * (field-mapping-server-write-path-r1 task 023, FR-16).
 * Follows the same nav-prop discovery pattern as ProjectService.
 *
 * @see IDataService — high-level data access abstraction (no IWebApi dependency)
 */

import { describeWebApiCreateError } from '../../utils/webApiCreateError';
import { ChildRecordWriteError, createRecordViaBffWithWarnings } from '../../utils/adapters/bffChildWriteAdapter';
import type { ICreateEventFormState } from './formTypes';
import type { ILookupItem } from '../../types/LookupTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import {
  applyResolverFields,
  discoverNavProps,
  findNavProp,
  cleanGuid,
} from '../../services/PolymorphicResolverService';

// ---------------------------------------------------------------------------
// Result types
// ---------------------------------------------------------------------------

export interface ICreateEventResult {
  eventId?: string;
  eventName?: string;
  success: boolean;
  errorMessage?: string;
  /** Non-fatal warnings (a mapped value the server did not write, an assignee that could not be linked). Empty when none. */
  warnings: string[];
}

// ---------------------------------------------------------------------------
// Metadata discovery
// ---------------------------------------------------------------------------

// Nav-prop discovery AND resolution are the shared `discoverNavProps` / `findNavProp`
// (PolymorphicResolverService) — consolidated 2026-07-09 (task 011, Path A); task 162 retired the
// local `_findNavProp` copy so the exact-column-first rule lives in one place.

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
// EventService class
// ---------------------------------------------------------------------------

export class EventService {
  constructor(
    private readonly _dataService: IDataService,
    /**
     * Injected BFF-authenticated fetch. Required for {@link createEvent}: the create goes to the BFF child-records
     * route (never `Xrm.WebApi`), so a service built without it fails closed on create. Lookup-only callers
     * (e.g. searchEventTypes) may omit it.
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
   * Create a sprk_event record through the BFF child-records route.
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
   *   fallback per SRFR-052. That parent is also the explicit mapping source
   *   (see below).
   * @param _options - Unused. `getCurrentUserId` fed the BU search-index cascade, which task 023 deleted (the index
   *   columns are the server's); kept only so existing callers and tests that pass it still type-check.
   *
   * Task 023 (FR-16): the payload holds ONLY the user's own values (name, priority, description, due date, event type,
   * assigned to) and the regarding keys the route files the event by. It holds no mapped field (the server maps
   * fill-only, FR-02), no owner / owning team / impersonation value (FR-09), no search-index value (`SearchIndexed`,
   * FR-38/FR-40), no container (task 076) and no `sprk_eventnumber` (the platform autonumber issues it, I-11).
   *
   * Mapping source: the event's regarding parent, named explicitly in the route's query string. The route infers a
   * source from the row only for the tables in `ChildRecordEndpoints.InferMappingSourceTables`, which does not yet
   * list `sprk_event`; naming it keeps the mapping working on every BFF build, and the body stays the user's values
   * (the source travels in the query string).
   */
  async createEvent(
    formValues: ICreateEventFormState,
    regardingEntityName?: string,
    _options?: { getCurrentUserId?: () => string | null }
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

    // Event type lookup
    if (formValues.eventTypeId) {
      const navProp = findNavProp(navProps, 'sprk_eventtype_ref');
      if (navProp) {
        entity[`${navProp}@odata.bind`] = `/sprk_eventtype_refs(${cleanGuid(formValues.eventTypeId)})`;
      }
    }

    // Assigned To (contact) -- spaarkeai-assistant-enhancements-r1 task 014 /
    // FR-A4. Optional: an event/task may be created with no assignee. The
    // nav-prop is discovered dynamically (never hardcoded) per the
    // established `discoverNavProps`/`findNavProp` pattern used throughout
    // this method -- avoids the SchemaName/LogicalName @odata.bind casing
    // gotcha (docs/standards/ODATA-NAMING-CONVENTION.md).
    if (formValues.assignedToId) {
      const navProp = findNavProp(navProps, 'contact', 'assignedto');
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
    // the canonical `WorkAssignmentService`. These are the filing keys the
    // route needs (the regarding pair files the event and decides its owner);
    // the server keeps the pair as sent and canonicalises the record URL
    // (task 045f: a URL that names another record than the pair is refused).
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
    const hasRegarding = !!formValues.regardingRecordId && !!regardingEntityName;
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
    }

    try {
      // Task 023 (FR-16): the create goes to the BFF child-records route, never to Xrm.WebApi. The server maps the fields
      // fill-only (the user's own value wins), owns the record by the caller's rules (FR-09), picks the search index and
      // the platform autonumber issues the event number. A server warning (a source that could not be read, a mapped value
      // that was not written) is shown like any other.
      if (!this._authenticatedFetch || typeof this._bffBaseUrl !== 'string') {
        throw new ChildRecordWriteError(
          'The sprk_event record was not saved: this screen is not connected to the Spaarke service, which must save it.',
          0,
          'child_record.bff_not_configured'
        );
      }
      const created = await createRecordViaBffWithWarnings(
        this._authenticatedFetch,
        this._bffBaseUrl,
        'sprk_event',
        entity,
        hasRegarding ? { mappingSource: { entity: regardingEntityName!, id: formValues.regardingRecordId } } : undefined
      );
      warnings.push(...created.warnings);
      return {
        eventId: created.id,
        eventName: formValues.eventName.trim(),
        success: true,
        warnings,
      };
    } catch (err) {
      console.error('[EventService] createRecord error:', err);
      // #2382: a privilege refusal becomes one plain sentence; the full error is logged above.
      // An event with no regarding parent is a root create: the route's uniform not-found can only be a selected value.
      const described = describeWebApiCreateError(err, 'event', { rootCreate: !hasRegarding });
      const message = described.message;
      return {
        success: false,
        errorMessage: described.isAccessDenied ? message : `Failed to create event record: ${message}`,
        warnings,
      };
    }
  }
}
