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

// ---------------------------------------------------------------------------
// Result types
// ---------------------------------------------------------------------------

export interface ICreateEventResult {
  eventId?: string;
  eventName?: string;
  success: boolean;
  errorMessage?: string;
}

// ---------------------------------------------------------------------------
// Metadata discovery
// ---------------------------------------------------------------------------

interface NavPropEntry {
  columnName: string;
  navPropName: string;
  referencedEntity: string;
}

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
      console.warn(`[EventService] Nav-prop discovery failed for ${entityLogicalName}:`, resp.status);
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

    _navPropCache[entityLogicalName] = entries;
    return entries;
  } catch (err) {
    console.warn(`[EventService] Nav-prop discovery error for ${entityLogicalName}:`, err);
    return [];
  }
}

function _findNavProp(
  entries: NavPropEntry[],
  referencedEntity: string,
  columnHint?: string,
): string | undefined {
  const matches = entries.filter((e) => e.referencedEntity === referencedEntity);
  if (matches.length === 0) return undefined;
  if (matches.length === 1) return matches[0].navPropName;
  if (columnHint) {
    const hinted = matches.find((e) => e.columnName.includes(columnHint));
    if (hinted) return hinted.navPropName;
  }
  return matches[0].navPropName;
}

// ---------------------------------------------------------------------------
// EventService class
// ---------------------------------------------------------------------------

export class EventService {
  constructor(private readonly _dataService: IDataService) {}

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
      return result.entities.map((e) => ({
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
   *   (e.g. 'sprk_matter', 'sprk_project'). When supplied together with
   *   `formValues.regardingRecordId`, the event is associated via the
   *   N:1 relationship nav-prop resolved through metadata discovery.
   */
  async createEvent(
    formValues: ICreateEventFormState,
    regardingEntityName?: string,
  ): Promise<ICreateEventResult> {
    const navProps = await _discoverNavProps('sprk_event');

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
      const navProp = _findNavProp(navProps, 'sprk_eventtype_ref');
      if (navProp) {
        entity[`${navProp}@odata.bind`] = `/sprk_eventtype_refs(${formValues.eventTypeId})`;
      }
    }

    // Regarding record (parent matter / project) — link via nav-prop
    if (formValues.regardingRecordId && regardingEntityName) {
      const pluralMap: Record<string, string> = {
        sprk_matter: 'sprk_matters',
        sprk_project: 'sprk_projects',
      };
      const entitySetName = pluralMap[regardingEntityName] ?? `${regardingEntityName}s`;
      const navProp = _findNavProp(navProps, regardingEntityName);
      if (navProp) {
        entity[`${navProp}@odata.bind`] = `/${entitySetName}(${formValues.regardingRecordId})`;
      } else {
        // Fallback: use well-known column names if metadata discovery missed the relationship
        const fallbackMap: Record<string, string> = {
          sprk_matter: 'sprk_regardingmatterid',
          sprk_project: 'sprk_regardingprojectid',
        };
        const fallback = fallbackMap[regardingEntityName];
        if (fallback) {
          entity[`${fallback}_${regardingEntityName}@odata.bind`] =
            `/${entitySetName}(${formValues.regardingRecordId})`;
        }
      }
    }

    try {
      const id = await this._dataService.createRecord('sprk_event', entity);
      return {
        eventId: id,
        eventName: formValues.eventName.trim(),
        success: true,
      };
    } catch (err) {
      console.error('[EventService] createRecord error:', err);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const errObj = err as any;
      const message = errObj?.message || (err instanceof Error ? err.message : 'Unknown error');
      return {
        success: false,
        errorMessage: `Failed to create event record: ${message}`,
      };
    }
  }
}
