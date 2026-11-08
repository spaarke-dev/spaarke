/**
 * projectService.ts
 * Project creation service for the Create New Project wizard.
 *
 * Responsibilities:
 *   1. Search reference tables (project types, practice areas, contacts) through IDataService
 *   2. Create the sprk_project record through the BFF child-records route (task 022, FR-16)
 *
 * Follows the same patterns as CreateMatterWizard/matterService.ts:
 *   - Navigation property discovery via EntityDefinitions metadata API
 *   - OData @odata.bind syntax for lookup bindings
 *   - Cached nav-prop map per entity
 *   - Never throws — returns result object with status/errorMessage
 *
 * @see IDataService — high-level data access abstraction (no IWebApi dependency)
 */

import { describeWebApiCreateError } from '../../utils/webApiCreateError';
import type { ICreateProjectFormState } from './projectFormTypes';
import type { ILookupItem } from '../../types/LookupTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import type { AssociationResult } from '../AssociateToStep/types';
import type { IUserBuCascadeDefaults, AuthenticatedFetchFn } from '../../services/EntityCreationService';
import { discoverNavProps, findNavProp, cleanGuid } from '../../services/PolymorphicResolverService';
import { createRecordViaBffWithWarnings, type IBffCreateOptions } from '../../utils/adapters/bffChildWriteAdapter';

// ---------------------------------------------------------------------------
// Result types
// ---------------------------------------------------------------------------

export interface ICreateProjectResult {
  /** The GUID of the created sprk_project record (present on success). */
  projectId?: string;
  /** Display name of the created project. */
  projectName?: string;
  /** Whether the creation succeeded. */
  success: boolean;
  /** Human-readable error message (set when success is false). */
  errorMessage?: string;
  /** Non-fatal warnings (the server's: a mapping source it could not read, a mapped value it did not write). Empty when none. */
  warnings: string[];
  /**
   * Task 049 fix round 2: the project was created secure from creation (the BFF create with `secure=true`), so the
   * provisioning call that follows only does what is left — its `already_provisioned` answer is then success. Task 022:
   * every secure-requested create that succeeds sends the intent, so this is set on every such success.
   */
  securedAtCreate?: boolean;
}

// ---------------------------------------------------------------------------
// Metadata discovery — find correct OData navigation property names
// ---------------------------------------------------------------------------

// Nav-prop discovery AND resolution are the shared `discoverNavProps` / `findNavProp`
// (PolymorphicResolverService) — consolidated 2026-07-09 (task 011, Path A); task 162 retired the
// local `_findNavProp` copy so the exact-column-first rule lives in one place. `columnHint` below
// disambiguates when several lookups point to the same entity (e.g. two contact lookups).

// ---------------------------------------------------------------------------
// ProjectService class
// ---------------------------------------------------------------------------

export class ProjectService {
  constructor(
    private readonly _dataService: IDataService,
    /**
     * Injected BFF-authenticated fetch. Required by `createProject`, which creates through the BFF child-records route
     * (task 022); without it (or without a base URL) the create is refused and nothing is written. Lookup-only callers
     * (searchProjectTypes/searchPracticeAreas/etc.) may omit it.
     */
    private readonly _authenticatedFetch?: AuthenticatedFetchFn,
    /** BFF API base URL. See {@link _authenticatedFetch}. */
    private readonly _bffBaseUrl?: string
  ) {}

  // ── Lookup search methods ─────────────────────────────────────────────

  /**
   * Search sprk_projecttype_ref records by name fragment.
   * Returns up to 10 matching project types as ILookupItem.
   */
  async searchProjectTypes(nameFilter: string): Promise<ILookupItem[]> {
    if (!nameFilter || nameFilter.trim().length < 1) {
      return [];
    }

    const safeFilter = nameFilter.trim().replace(/'/g, "''");
    const query =
      `?$select=sprk_projecttype_refid,sprk_name` +
      `&$filter=contains(sprk_name,'${safeFilter}')` +
      `&$orderby=sprk_name asc` +
      `&$top=10`;

    console.info('[ProjectService] searchProjectTypes query:', 'sprk_projecttype_ref', query);
    try {
      const result = await this._dataService.retrieveMultipleRecords('sprk_projecttype_ref', query);
      console.info('[ProjectService] searchProjectTypes results:', result.entities.length);
      return result.entities.map(e => ({
        id: e['sprk_projecttype_refid'] as string,
        name: e['sprk_name'] as string,
      }));
    } catch (err) {
      console.error('[ProjectService] searchProjectTypes error:', err);
      throw err;
    }
  }

  /**
   * Search sprk_practicearea_ref records by name fragment.
   * Returns up to 10 matching practice areas as ILookupItem.
   */
  async searchPracticeAreas(nameFilter: string): Promise<ILookupItem[]> {
    if (!nameFilter || nameFilter.trim().length < 1) {
      return [];
    }

    const safeFilter = nameFilter.trim().replace(/'/g, "''");
    const query =
      `?$select=sprk_practicearea_refid,sprk_practiceareaname` +
      `&$filter=contains(sprk_practiceareaname,'${safeFilter}')` +
      `&$orderby=sprk_practiceareaname asc` +
      `&$top=10`;

    console.info('[ProjectService] searchPracticeAreas query:', 'sprk_practicearea_ref', query);
    try {
      const result = await this._dataService.retrieveMultipleRecords('sprk_practicearea_ref', query);
      console.info('[ProjectService] searchPracticeAreas results:', result.entities.length);
      return result.entities.map(e => ({
        id: e['sprk_practicearea_refid'] as string,
        name: e['sprk_practiceareaname'] as string,
      }));
    } catch (err) {
      console.error('[ProjectService] searchPracticeAreas error:', err);
      throw err;
    }
  }

  /**
   * Search contact records by name fragment.
   * Returns up to 10 matching contacts as ILookupItem.
   * Name format: "Full Name (email)" for disambiguation.
   */
  async searchContacts(nameFilter: string): Promise<ILookupItem[]> {
    if (!nameFilter || nameFilter.trim().length < 2) {
      return [];
    }

    const safeFilter = nameFilter.trim().replace(/'/g, "''");
    const query =
      `?$select=contactid,fullname,emailaddress1` +
      `&$filter=contains(fullname,'${safeFilter}')` +
      `&$orderby=fullname asc` +
      `&$top=10`;

    console.info('[ProjectService] searchContacts query:', 'contact', query);
    try {
      const result = await this._dataService.retrieveMultipleRecords('contact', query);
      console.info('[ProjectService] searchContacts results:', result.entities.length);
      return result.entities.map(e => {
        const fullname = e['fullname'] as string;
        const email = e['emailaddress1'] as string | undefined;
        return {
          id: e['contactid'] as string,
          name: fullname + (email ? ` (${email})` : ''),
        };
      });
    } catch (err) {
      console.error('[ProjectService] searchContacts error:', err);
      throw err;
    }
  }

  /**
   * Search sprk_organization records by name fragment.
   * Returns up to 10 matching organizations as ILookupItem.
   */
  async searchOrganizations(nameFilter: string): Promise<ILookupItem[]> {
    if (!nameFilter || nameFilter.trim().length < 2) {
      return [];
    }

    const safeFilter = nameFilter.trim().replace(/'/g, "''");
    const query =
      `?$select=sprk_organizationid,sprk_name` +
      `&$filter=contains(sprk_name,'${safeFilter}')` +
      `&$orderby=sprk_name asc` +
      `&$top=10`;

    console.info('[ProjectService] searchOrganizations query:', 'sprk_organization', query);
    try {
      const result = await this._dataService.retrieveMultipleRecords('sprk_organization', query);
      console.info('[ProjectService] searchOrganizations results:', result.entities.length);
      return result.entities.map(e => ({
        id: e['sprk_organizationid'] as string,
        name: e['sprk_name'] as string,
      }));
    } catch (err) {
      console.error('[ProjectService] searchOrganizations error:', err);
      throw err;
    }
  }

  // ── Record creation ───────────────────────────────────────────────────

  /**
   * Create a sprk_project record through the BFF child-records route (`POST /api/v1/child-records/sprk_project`).
   *
   * Field-mapping write path (project field-mapping-server-write-path-r1, task 022, FR-16, FR-15): the payload holds ONLY
   * what the user entered or picked. This method does not map fields, default a search index, resolve an owner or write
   * the secure flag — the server does each of them in the one create (fill-only mapping FR-02, owner FR-09, search index
   * FR-38/FR-40, the project number by the platform autonumber). A mapped value the caller may not write is dropped by
   * the server with a warning, which arrives in `warnings`. It never falls back to `Xrm.WebApi`: with no BFF connection
   * the create is refused and nothing is written.
   *
   * Uses navigation property discovery to resolve the correct OData @odata.bind syntax for each lookup field.
   *
   * Returns ICreateProjectResult — never throws.
   *
   * @param formValues Form state captured by the wizard. `isSecure` asks for the project to be secure from creation
   *   (`secure=true` on the route, task 049 fix round 2): the server creates it into isolation, and the caller's
   *   provisioning call afterwards does only what is left.
   * @param _cascadeDefaults BU-derived defaults. Accepted so existing hosts keep compiling, but not read: neither index
   *   column is written onto the project (both are the server's, FR-38; the index follows the content type, FR-40), and
   *   `sprk_containerid` is provisioning's (task 076).
   * @param association Optional parent record selected via the wizard's AssociateToStep (Matter/Account today). It is
   *   sent as the explicit mapping source (`?mappingSourceEntity=&mappingSourceId=`), because the project payload carries
   *   no filing key the server could infer the source from: the wizard files the project under its matter AFTER the
   *   create (task 195; task 049's known limit). The body never carries it. When omitted, no source is named and the
   *   server maps nothing.
   */
  async createProject(
    formValues: ICreateProjectFormState,
    _cascadeDefaults?: IUserBuCascadeDefaults,
    association?: AssociationResult | null
  ): Promise<ICreateProjectResult> {
    const warnings: string[] = [];

    // Fail closed: the create is the server's. With no BFF connection nothing is sent to Xrm.WebApi, which would make the
    // project user-owned, unmapped and (for a secure request) briefly ordinary.
    const authenticatedFetch = this._authenticatedFetch;
    const bffBaseUrl = this._bffBaseUrl;
    if (!authenticatedFetch || !bffBaseUrl) {
      console.error(
        '[ProjectService] createRecord refused: no BFF connection (authenticatedFetch / bffBaseUrl missing)'
      );
      return {
        success: false,
        errorMessage:
          'The project was not created: this screen is not connected to the Spaarke service, which must save it.',
        warnings,
      };
    }

    // Discover correct OData navigation property names from entity metadata.
    // Matches by referenced (target) entity to avoid hardcoding column names
    // which can differ between entities (e.g. sprk_mattertype vs sprk_projecttyperef).
    const navProps = await discoverNavProps('sprk_project');

    // Build entity payload with scalar fields
    const entity: Record<string, unknown> = {
      sprk_projectname: formValues.projectName.trim(),
    };

    if (formValues.description && formValues.description.trim() !== '') {
      entity['sprk_projectdescription'] = formValues.description.trim();
    }

    // Task 022 (FR-16): the payload is the user's own values — nothing else.
    //   - `sprk_issecure` is NEVER written (unified-access-control-r2 task 150: field-secured, the server's). A secure
    //     project is asked for with the route's `secure=true` query parameter, never in the body.
    //   - The BU cascade (`sprk_searchindexname`, the `sprk_ai_search_index` bind) is NOT written: both columns are the
    //     server's on a project (FR-38; `RecordTableRule.SearchIndexed`), and since task 041 (FR-40) the index follows
    //     the content type.
    //   - `sprk_containerid` stays unwritten (task 076): provisioning records it.
    //   - No owner, owning team or impersonation value (FR-09): the BFF decides ownership from the caller.
    //   - No mapped value (FR-02): the server maps fill-only, so a value the user typed here wins.

    // Add lookup bindings — match by referenced entity name (robust).
    // columnHint disambiguates when multiple lookups point to the same entity (e.g. contact).
    const lookups: Array<{
      referencedEntity: string;
      entitySet: string;
      guid: string;
      label: string;
      columnHint?: string;
    }> = [];
    if (formValues.projectTypeId) {
      lookups.push({
        referencedEntity: 'sprk_projecttype_ref',
        entitySet: 'sprk_projecttype_refs',
        guid: formValues.projectTypeId,
        label: 'Project Type',
      });
    }
    if (formValues.practiceAreaId) {
      lookups.push({
        referencedEntity: 'sprk_practicearea_ref',
        entitySet: 'sprk_practicearea_refs',
        guid: formValues.practiceAreaId,
        label: 'Practice Area',
      });
    }
    if (formValues.assignedAttorneyId) {
      lookups.push({
        referencedEntity: 'contact',
        entitySet: 'contacts',
        guid: formValues.assignedAttorneyId,
        label: 'Attorney',
        columnHint: 'attorney',
      });
    }
    if (formValues.assignedParalegalId) {
      lookups.push({
        referencedEntity: 'contact',
        entitySet: 'contacts',
        guid: formValues.assignedParalegalId,
        label: 'Paralegal',
        columnHint: 'paralegal',
      });
    }
    if (formValues.assignedOutsideCounselId) {
      lookups.push({
        referencedEntity: 'sprk_organization',
        entitySet: 'sprk_organizations',
        guid: formValues.assignedOutsideCounselId,
        label: 'Outside Counsel',
      });
    }

    for (const lk of lookups) {
      const navProp = findNavProp(navProps, lk.referencedEntity, lk.columnHint);
      if (navProp) {
        entity[`${navProp}@odata.bind`] = `/${lk.entitySet}(${cleanGuid(lk.guid)})`;
      } else {
        console.warn(
          `[ProjectService] No nav-prop found for ${lk.label} (referenced entity: ${lk.referencedEntity}) — skipping lookup binding`
        );
      }
    }

    // The route's query: the association as the explicit mapping source, and the secure intent. Both travel in the query
    // string, so the body stays the user's own values.
    const options: IBffCreateOptions = {};
    if (association?.recordId && association.entityType) {
      options.mappingSource = { entity: association.entityType, id: association.recordId };
    }
    const secure = formValues.isSecure === true;
    if (secure) options.secure = true;

    try {
      console.info('[ProjectService] createRecord payload:', JSON.stringify(entity, null, 2));
      // Task 022 (FR-16): the create goes to the BFF child-records route, never to Xrm.WebApi. The server maps the fields
      // fill-only (the user's own value wins), decides the owner record-first, and the platform autonumber issues the
      // project number. A secure request is created INTO isolation (task 049 fix round 2): there is no moment at which
      // the secure-requested project exists as an ordinary record.
      const created = await createRecordViaBffWithWarnings(
        authenticatedFetch,
        bffBaseUrl,
        'sprk_project',
        entity,
        Object.keys(options).length > 0 ? options : undefined
      );
      const projectId = created.id;
      warnings.push(...created.warnings);
      console.info('[ProjectService] createRecord success, projectId:', projectId);

      // The project's "Assigned *" people get their access from the create above, not from a client call: on a root
      // create the server grants the maker's own share inside the request and queues the full I-12 L1 pass for every other
      // subject (tasks 049/049b); for a secure-from-creation project it waits for the securing, and the reconciliation job
      // grants it when the create could not finish it. So there is no client access sync here (task 022; the Matter
      // wizard dropped its call in task 021).

      return {
        projectId,
        projectName: formValues.projectName.trim(),
        success: true,
        warnings,
        ...(secure ? { securedAtCreate: true } : {}),
      };
    } catch (err) {
      console.error('[ProjectService] createRecord error:', err);
      // #2382: a privilege refusal becomes one plain sentence; the full error is logged above.
      // The create names no parent (the wizard files the project after it), so the route's uniform not-found can only be
      // one of the values the user selected: the root-create sentence, never "filed under".
      const described = describeWebApiCreateError(err, 'project', { rootCreate: true });
      const message = described.message;
      return {
        success: false,
        errorMessage: described.isAccessDenied ? message : `Failed to create project record: ${message}`,
        warnings,
      };
    }
  }
}
