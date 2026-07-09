/**
 * projectService.ts
 * Project creation service for the Create New Project wizard.
 *
 * Responsibilities:
 *   1. Search reference tables (project types, practice areas, contacts)
 *   2. Create sprk_project Dataverse record via IDataService
 *
 * Follows the same patterns as CreateMatter/matterService.ts:
 *   - Navigation property discovery via EntityDefinitions metadata API
 *   - OData @odata.bind syntax for lookup bindings
 *   - Cached nav-prop map per entity
 *   - Never throws — returns result object with status/errorMessage
 *
 * @see IDataService — high-level data access abstraction (no IWebApi dependency)
 */

import type { ICreateProjectFormState } from './projectFormTypes';
import type { ILookupItem } from '../../types/LookupTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import type { AssociationResult } from '../AssociateToStep/types';
import {
  EntityCreationService,
  type IUserBuCascadeDefaults,
  type AuthenticatedFetchFn,
} from '../../services/EntityCreationService';
import { discoverNavProps } from '../../services/PolymorphicResolverService';
import { applyFieldMappings } from '../../services/FieldMappingService';

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
  /** Non-fatal warnings (e.g. field-mapping profile fetch failures). Empty when none. */
  warnings: string[];
}

// ---------------------------------------------------------------------------
// Metadata discovery — find correct OData navigation property names
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

/**
 * Find the navigation property name for a relationship that points to the given
 * referenced entity. More robust than matching by column name, since column names
 * can differ between entities (e.g. sprk_mattertype vs sprk_projecttyperef).
 *
 * When multiple relationships point to the same entity (e.g. two contact lookups),
 * use `columnHint` to disambiguate by matching a substring in the column name.
 */
function _findNavProp(entries: NavPropEntry[], referencedEntity: string, columnHint?: string): string | undefined {
  const matches = entries.filter(e => e.referencedEntity === referencedEntity);
  if (matches.length === 0) return undefined;
  if (matches.length === 1) return matches[0].navPropName;
  // Multiple matches — use column hint to disambiguate
  if (columnHint) {
    const hinted = matches.find(e => e.columnName.includes(columnHint));
    if (hinted) return hinted.navPropName;
  }
  return matches[0].navPropName;
}

// ---------------------------------------------------------------------------
// ProjectService class
// ---------------------------------------------------------------------------

export class ProjectService {
  constructor(
    private readonly _dataService: IDataService,
    /**
     * Injected BFF-authenticated fetch. Optional — only required to activate the
     * Field Mapping Framework engine (task 020, spec FR-12). Lookup-only callers
     * (searchProjectTypes/searchPracticeAreas/etc.) may omit it. When absent,
     * the engine call is a graceful no-op — identical to today's behavior.
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
   * Create a sprk_project record in Dataverse.
   *
   * Uses navigation property discovery to resolve the correct OData
   * @odata.bind syntax for each lookup field.
   *
   * BU cascade (FR-WIZ-02, fixes latent gap G2): when `cascadeDefaults` is provided,
   * applies `sprk_containerid` AND `sprk_searchindexname` from the user's owning
   * Business Unit to the create payload via
   * {@link EntityCreationService.applyUserBuDefaults}. Both fields are guarded by
   * INV-5 — explicit values pre-existing on the payload are preserved (never
   * overwritten). Callers (typically `CreateProjectWizard.tsx`) resolve the
   * defaults via {@link EntityCreationService.resolveUserBuDefaults}.
   *
   * Returns ICreateProjectResult — never throws.
   *
   * @param formValues Form state captured by the wizard.
   * @param cascadeDefaults Optional BU-derived defaults (containerId, searchIndexName).
   *   When omitted/undefined the cascade step is a no-op (legacy behavior). New
   *   wizard call sites pass the resolved value; tests can omit it.
   * @param association Optional parent record selected via the wizard's
   *   AssociateToStep (Matter/Account today; same-entity Project parents are
   *   not precluded — no source===target guard, per ADR-024/design.md §10).
   *   When supplied, drives the Field Mapping Framework engine (spec FR-12):
   *   `sourceEntity`/`sourceId` = `association.entityType`/`recordId`. When
   *   omitted, the engine call is skipped — a graceful no-op identical to
   *   today's behavior.
   *
   * @see spec.md FR-WIZ-02
   * @see spec.md FR-WIZ-08 (INV-5)
   * @see design.md §5.0 (BU cascade source)
   */
  async createProject(
    formValues: ICreateProjectFormState,
    cascadeDefaults?: IUserBuCascadeDefaults,
    association?: AssociationResult | null
  ): Promise<ICreateProjectResult> {
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

    // Set sprk_issecure when the user has designated this as a Secure Project.
    // This flag is intentionally only set to true — once a project is secure,
    // the designation is irreversible (enforced by domain logic in the BFF).
    if (formValues.isSecure === true) {
      entity['sprk_issecure'] = true;
    }

    // FR-WIZ-02 / G2 latent-gap fix: cascade `sprk_containerid` AND
    // `sprk_searchindexname` from the current user's owning Business Unit.
    // INV-5 is enforced per-field by applyUserBuDefaults — explicit override
    // values already on the payload are preserved.
    if (cascadeDefaults) {
      const applied = EntityCreationService.applyUserBuDefaults(entity, cascadeDefaults);
      console.info('[ProjectService] BU cascade applied:', applied);
    }

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

    // Phase G: cascade BU's `sprk_ai_search_index` lookup onto the new Project.
    if (cascadeDefaults?.searchIndexId) {
      lookups.push({
        referencedEntity: 'sprk_aisearchindex',
        entitySet: 'sprk_aisearchindexes',
        guid: cascadeDefaults.searchIndexId,
        label: 'AI Search Index',
      });
    }

    for (const lk of lookups) {
      const navProp = _findNavProp(navProps, lk.referencedEntity, lk.columnHint);
      if (navProp) {
        entity[`${navProp}@odata.bind`] = `/${lk.entitySet}(${lk.guid})`;
      } else {
        console.warn(
          `[ProjectService] No nav-prop found for ${lk.label} (referenced entity: ${lk.referencedEntity}) — skipping lookup binding`
        );
      }
    }

    const warnings: string[] = [];

    // Field Mapping Framework (spec FR-12, task 020): apply the configured
    // {parent -> sprk_project} mapping profile (if any) onto the create
    // payload. Runs AFTER the lookup-binding block above and BEFORE
    // createRecord, on the SAME payload object. The engine never throws and
    // is a graceful no-op when there's no association, no profile, or the BFF
    // deps are unavailable (e.g. a lookup-only construction site) -- no
    // behavior change in any of those cases. No source===target guard (
    // same-entity mapping is a supported scenario per ADR-024/design.md §10).
    if (association?.recordId && association.entityType && this._authenticatedFetch && this._bffBaseUrl) {
      const mappingResult = await applyFieldMappings({
        sourceEntity: association.entityType,
        sourceId: association.recordId,
        targetEntity: 'sprk_project',
        payload: entity,
        dataService: this._dataService,
        authenticatedFetch: this._authenticatedFetch,
        bffBaseUrl: this._bffBaseUrl,
      });
      warnings.push(...mappingResult.warnings);
    }

    try {
      console.info('[ProjectService] createRecord payload:', JSON.stringify(entity, null, 2));
      // IDataService.createRecord returns Promise<string> (just the id)
      const projectId = await this._dataService.createRecord('sprk_project', entity);
      console.info('[ProjectService] createRecord success, projectId:', projectId);
      return {
        projectId,
        projectName: formValues.projectName.trim(),
        success: true,
        warnings,
      };
    } catch (err) {
      console.error('[ProjectService] createRecord error:', err);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const errObj = err as any;
      const message = errObj?.message || (err instanceof Error ? err.message : 'Unknown error');
      return {
        success: false,
        errorMessage: `Failed to create project record: ${message}`,
        warnings,
      };
    }
  }
}
