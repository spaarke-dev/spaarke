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

    const entries: NavPropEntry[] = rels.map(r => ({
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
   *   (e.g. 'sprk_matter', 'sprk_project'). When supplied together with
   *   `formValues.regardingRecordId`, the event is associated via the
   *   N:1 relationship nav-prop resolved through metadata discovery.
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
            entity[`${aiNavProp}@odata.bind`] = `/sprk_aisearchindexes(${buDefaults.searchIndexId})`;
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
