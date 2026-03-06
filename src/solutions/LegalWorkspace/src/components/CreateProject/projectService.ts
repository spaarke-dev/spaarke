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

interface NavPropEntry {
  columnName: string;
  navPropName: string;
  referencedEntity: string;
}

/**
 * Query the Dataverse entity metadata API to discover the actual
 * single-valued navigation property names for lookup columns on an entity.
 *
 * Dataverse uses PascalCase navigation property names (e.g. "sprk_ProjectType")
 * which differ from the lowercase column logical names ("sprk_projecttype").
 * The @odata.bind syntax requires the nav-prop name, not the column name.
 *
 * This enhanced version also discovers the referenced (target) entity for each
 * relationship, enabling lookup matching by target entity instead of column name.
 * This is more robust because column names can vary between entities.
 *
 * Results are cached per entity to avoid repeated metadata calls.
 *
 * @param entityLogicalName - e.g. 'sprk_project'
 * @returns Array of NavPropEntry with column name, nav-prop name, and referenced entity
 */
const _navPropCache: Record<string, NavPropEntry[]> = {};

async function _discoverNavProps(entityLogicalName: string): Promise<NavPropEntry[]> {
  if (_navPropCache[entityLogicalName]) {
    return _navPropCache[entityLogicalName];
  }

  try {
    const url =
      `/api/data/v9.0/EntityDefinitions(LogicalName='${entityLogicalName}')/ManyToOneRelationships` +
      `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity`;

    const resp = await fetch(url, { credentials: 'include' });
    if (!resp.ok) {
      console.warn(`[ProjectService] Nav-prop discovery failed for ${entityLogicalName}:`, resp.status);
      return [];
    }

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const json: any = await resp.json();
    const rels: Array<{
      ReferencingAttribute: string;
      ReferencingEntityNavigationPropertyName: string;
      ReferencedEntity: string;
    }> =
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (json as any).value ?? [];

    const entries: NavPropEntry[] = rels.map((r) => ({
      columnName: r.ReferencingAttribute,
      navPropName: r.ReferencingEntityNavigationPropertyName,
      referencedEntity: r.ReferencedEntity,
    }));

    console.info(`[ProjectService] Nav-props for ${entityLogicalName}:`,
      entries.map((e) => `${e.columnName} → ${e.navPropName} (→ ${e.referencedEntity})`));
    _navPropCache[entityLogicalName] = entries;
    return entries;
  } catch (err) {
    console.warn(`[ProjectService] Nav-prop discovery error for ${entityLogicalName}:`, err);
    return [];
  }
}

/**
 * Find the navigation property name for a relationship that points to the given
 * referenced entity. More robust than matching by column name, since column names
 * can differ between entities (e.g. sprk_mattertype vs sprk_projecttyperef).
 *
 * When multiple relationships point to the same entity (e.g. two contact lookups),
 * use `columnHint` to disambiguate by matching a substring in the column name.
 */
function _findNavProp(
  entries: NavPropEntry[],
  referencedEntity: string,
  columnHint?: string,
): string | undefined {
  const matches = entries.filter((e) => e.referencedEntity === referencedEntity);
  if (matches.length === 0) return undefined;
  if (matches.length === 1) return matches[0].navPropName;
  // Multiple matches — use column hint to disambiguate
  if (columnHint) {
    const hinted = matches.find((e) => e.columnName.includes(columnHint));
    if (hinted) return hinted.navPropName;
  }
  return matches[0].navPropName;
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
    // Discover correct OData navigation property names from entity metadata.
    // Matches by referenced (target) entity to avoid hardcoding column names
    // which can differ between entities (e.g. sprk_mattertype vs sprk_projecttyperef).
    const navProps = await _discoverNavProps('sprk_project');

    // Build entity payload with scalar fields
    const entity: WebApiEntity = {
      sprk_projectname: formValues.projectName.trim(),
    };

    if (formValues.description && formValues.description.trim() !== '') {
      entity['sprk_projectdescription'] = formValues.description.trim();
    }

    // Add lookup bindings — match by referenced entity name (robust).
    // columnHint disambiguates when multiple lookups point to the same entity (e.g. contact).
    const lookups: Array<{ referencedEntity: string; entitySet: string; guid: string; label: string; columnHint?: string }> = [];
    if (formValues.projectTypeId) {
      lookups.push({ referencedEntity: 'sprk_projecttype_ref', entitySet: 'sprk_projecttype_refs', guid: formValues.projectTypeId, label: 'Project Type' });
    }
    if (formValues.practiceAreaId) {
      lookups.push({ referencedEntity: 'sprk_practicearea_ref', entitySet: 'sprk_practicearea_refs', guid: formValues.practiceAreaId, label: 'Practice Area' });
    }
    if (formValues.assignedAttorneyId) {
      lookups.push({ referencedEntity: 'contact', entitySet: 'contacts', guid: formValues.assignedAttorneyId, label: 'Attorney', columnHint: 'attorney' });
    }
    if (formValues.assignedParalegalId) {
      lookups.push({ referencedEntity: 'contact', entitySet: 'contacts', guid: formValues.assignedParalegalId, label: 'Paralegal', columnHint: 'paralegal' });
    }
    if (formValues.assignedOutsideCounselId) {
      lookups.push({ referencedEntity: 'sprk_organization', entitySet: 'sprk_organizations', guid: formValues.assignedOutsideCounselId, label: 'Outside Counsel' });
    }

    for (const lk of lookups) {
      const navProp = _findNavProp(navProps, lk.referencedEntity, lk.columnHint);
      if (navProp) {
        entity[`${navProp}@odata.bind`] = `/${lk.entitySet}(${lk.guid})`;
      } else {
        console.warn(`[ProjectService] No nav-prop found for ${lk.label} (referenced entity: ${lk.referencedEntity}) — skipping lookup binding`);
      }
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
