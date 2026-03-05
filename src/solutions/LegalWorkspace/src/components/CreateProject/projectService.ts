/**
 * projectService.ts
 * Project creation service for the Create New Project wizard.
 *
 * Responsibilities:
 *   1. Search reference tables (project types, practice areas, contacts)
 *   2. Create sprk_project Dataverse record via Xrm.WebApi
 *
 * Follows the same patterns as CreateMatter/matterService.ts:
 *   - Navigation property discovery via EntityDefinitions metadata API
 *   - OData @odata.bind syntax for lookup bindings
 *   - Cached nav-prop map per entity
 *   - Never throws — returns result object with status/errorMessage
 */

import type { ICreateProjectFormState } from './projectFormTypes';
import type { ILookupItem } from '../../types/entities';
import type { IWebApi, WebApiEntity } from '../../types/xrm';

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
}

// ---------------------------------------------------------------------------
// Metadata discovery — find correct OData navigation property names
// ---------------------------------------------------------------------------

/**
 * Query the Dataverse entity metadata API to discover the actual
 * single-valued navigation property names for lookup columns on an entity.
 *
 * Dataverse uses PascalCase navigation property names (e.g. "sprk_ProjectType")
 * which differ from the lowercase column logical names ("sprk_projecttype").
 * The @odata.bind syntax requires the nav-prop name, not the column name.
 *
 * Results are cached per entity to avoid repeated metadata calls.
 *
 * @param entityLogicalName - e.g. 'sprk_project'
 * @returns Map: { columnLogicalName -> navigationPropertyName }
 */
const _navPropCache: Record<string, Record<string, string>> = {};

async function _discoverNavProps(entityLogicalName: string): Promise<Record<string, string>> {
  if (_navPropCache[entityLogicalName]) {
    return _navPropCache[entityLogicalName];
  }

  try {
    const url =
      `/api/data/v9.0/EntityDefinitions(LogicalName='${entityLogicalName}')/ManyToOneRelationships` +
      `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName`;

    const resp = await fetch(url, { credentials: 'include' });
    if (!resp.ok) {
      console.warn(`[ProjectService] Nav-prop discovery failed for ${entityLogicalName}:`, resp.status);
      return {};
    }

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const json: any = await resp.json();
    const rels: Array<{ ReferencingAttribute: string; ReferencingEntityNavigationPropertyName: string }> =
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (json as any).value ?? [];

    const map: Record<string, string> = {};
    for (const r of rels) {
      map[r.ReferencingAttribute] = r.ReferencingEntityNavigationPropertyName;
    }

    console.info(`[ProjectService] Nav-props for ${entityLogicalName}:`, map);
    _navPropCache[entityLogicalName] = map;
    return map;
  } catch (err) {
    console.warn(`[ProjectService] Nav-prop discovery error for ${entityLogicalName}:`, err);
    return {};
  }
}

// ---------------------------------------------------------------------------
// ProjectService class
// ---------------------------------------------------------------------------

export class ProjectService {
  constructor(private readonly _webApi: IWebApi) {}

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
      const result = await this._webApi.retrieveMultipleRecords('sprk_projecttype_ref', query, 10);
      console.info('[ProjectService] searchProjectTypes results:', result.entities.length);
      return result.entities.map((e) => ({
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
      const result = await this._webApi.retrieveMultipleRecords('sprk_practicearea_ref', query, 10);
      console.info('[ProjectService] searchPracticeAreas results:', result.entities.length);
      return result.entities.map((e) => ({
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
      const result = await this._webApi.retrieveMultipleRecords('contact', query, 10);
      console.info('[ProjectService] searchContacts results:', result.entities.length);
      return result.entities.map((e) => {
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
      const result = await this._webApi.retrieveMultipleRecords('sprk_organization', query, 10);
      console.info('[ProjectService] searchOrganizations results:', result.entities.length);
      return result.entities.map((e) => ({
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
   * Returns ICreateProjectResult — never throws.
   */
  async createProject(formValues: ICreateProjectFormState): Promise<ICreateProjectResult> {
    // Discover correct OData navigation property names from entity metadata
    const navPropMap = await _discoverNavProps('sprk_project');

    // Build entity payload with scalar fields
    const entity: WebApiEntity = {
      sprk_projectname: formValues.projectName.trim(),
    };

    if (formValues.description && formValues.description.trim() !== '') {
      entity['sprk_projectdescription'] = formValues.description.trim();
    }

    // Add lookup bindings using discovered nav-prop names
    const lookups: Array<{ col: string; entitySet: string; guid: string }> = [];
    if (formValues.projectTypeId) {
      lookups.push({ col: 'sprk_projecttype', entitySet: 'sprk_projecttype_refs', guid: formValues.projectTypeId });
    }
    if (formValues.practiceAreaId) {
      lookups.push({ col: 'sprk_practicearea', entitySet: 'sprk_practicearea_refs', guid: formValues.practiceAreaId });
    }
    if (formValues.assignedAttorneyId) {
      lookups.push({ col: 'sprk_assignedattorney', entitySet: 'contacts', guid: formValues.assignedAttorneyId });
    }
    if (formValues.assignedParalegalId) {
      lookups.push({ col: 'sprk_assignedparalegal', entitySet: 'contacts', guid: formValues.assignedParalegalId });
    }
    if (formValues.assignedOutsideCounselId) {
      lookups.push({ col: 'sprk_assignedoutsidecounsel', entitySet: 'sprk_organizations', guid: formValues.assignedOutsideCounselId });
    }

    for (const lk of lookups) {
      const navProp = navPropMap[lk.col] ?? lk.col;
      entity[`${navProp}@odata.bind`] = `/${lk.entitySet}(${lk.guid})`;
    }

    try {
      console.info('[ProjectService] createRecord payload:', JSON.stringify(entity, null, 2));
      const result = await this._webApi.createRecord('sprk_project', entity);
      console.info('[ProjectService] createRecord success, projectId:', result.id);
      return {
        projectId: result.id,
        projectName: formValues.projectName.trim(),
        success: true,
      };
    } catch (err) {
      console.error('[ProjectService] createRecord error:', err);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const errObj = err as any;
      const message = errObj?.message || (err instanceof Error ? err.message : 'Unknown error');
      return {
        success: false,
        errorMessage: `Failed to create project record: ${message}`,
      };
    }
  }
}
