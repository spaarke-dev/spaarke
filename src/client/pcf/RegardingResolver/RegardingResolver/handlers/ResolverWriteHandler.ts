/**
 * ResolverWriteHandler — the SOLE write path inside the RegardingResolver PCF.
 *
 * Wraps `PolymorphicResolverService.applyResolverFields` from `@spaarke/ui-components`.
 * This module exists to satisfy two binding constraints:
 *
 *   1. ADR-024 / FR-21 — the PCF MUST NOT reimplement the FR-13 mutual-exclusivity
 *      logic. All field-write logic lives in the shared service. This handler is a
 *      thin coordinator: discover nav-props, look up the catalog entry, hand off
 *      to `applyResolverFields`, then write the resulting payload via
 *      `webApi.updateRecord`.
 *
 *   2. FR-22 — zero entity-specific code branches. The host entity is passed in
 *      via `hostEntity` (the manifest `entity` input property); the catalog of 11
 *      regarding targets is the same for any host entity that follows the resolver
 *      pattern. There are NO `sprk_todo` / `sprk_communication` literals here —
 *      every Dataverse logical name is a parameter or comes from the shared
 *      `TODO_REGARDING_CATALOG` constant.
 *
 * The handler also covers the new-record / existing-record split:
 *   - For an existing record (recordId is a real GUID), the payload is written
 *     immediately via `Xrm.WebApi.updateRecord`.
 *   - For a new record (no GUID yet), the caller mutates the form's pre-save
 *     buffer via `Xrm.Page.getAttribute(...).setValue(...)` for each field, so
 *     the resolver payload rides the form's CREATE transaction (matches the
 *     pattern proven by `AssociationResolver` v1.1.0 — see audit §3 R-02).
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver.md
 * @see projects/smart-todo-r4/notes/regarding-resolver-audit.md §4
 */

import {
  applyResolverFields,
  TODO_REGARDING_CATALOG,
  type INavPropEntry,
  type IPolymorphicWebApi,
  type ITodoRegardingTargetCatalogEntry,
} from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface IRegardingSelection {
  /** Dataverse logical name of the selected parent entity (e.g., 'sprk_matter'). */
  entityType: string;
  /** GUID of the selected parent record (with or without braces / case). */
  recordId: string;
  /** Display name of the selected parent record (used for sprk_regardingrecordname). */
  recordName: string;
}

export interface IResolverWriteContext {
  /** WebApi instance (Xrm.WebApi-compatible — context.webAPI from PCF). */
  webApi: IPolymorphicWebApi & {
    updateRecord: (entityLogicalName: string, id: string, data: Record<string, unknown>) => Promise<unknown>;
  };
  /** Host entity logical name from the manifest `entity` input property. FR-22 lever. */
  hostEntity: string;
  /** Host record GUID. Empty / undefined when the form is creating a new record. */
  hostRecordId?: string;
}

export interface IResolverWriteResult {
  success: boolean;
  /** The selected target catalog entry, if found. */
  catalogEntry?: ITodoRegardingTargetCatalogEntry;
  /** The full payload that was written (or staged for the form save). */
  payload?: Record<string, unknown>;
  /** Error message if any step failed. */
  error?: string;
}

// ---------------------------------------------------------------------------
// Nav-prop discovery (per-host-entity, cached)
// ---------------------------------------------------------------------------

const _navPropCache: Record<string, INavPropEntry[]> = {};

/**
 * Discover ManyToOne navigation properties for an arbitrary host entity.
 *
 * Mirrors the `discoverTodoNavProps` pattern from
 * `@spaarke/ui-components/services/TodoRegardingUpdateBuilder` but parameterized
 * on host entity so the PCF works for `sprk_todo`, `sprk_communication`, or any
 * future resolver-pattern entity per FR-22.
 *
 * @param hostEntity  - Logical name of the host entity (e.g., 'sprk_todo').
 * @param fetchImpl   - Fetch implementation (overridable for tests).
 */
export async function discoverHostNavProps(
  hostEntity: string,
  fetchImpl: typeof fetch = globalThis.fetch
): Promise<INavPropEntry[]> {
  if (_navPropCache[hostEntity]) {
    return _navPropCache[hostEntity];
  }

  try {
    const url =
      `/api/data/v9.0/EntityDefinitions(LogicalName='${hostEntity}')/ManyToOneRelationships` +
      `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity`;

    const resp = await fetchImpl(url, { credentials: 'include' });
    if (!resp.ok) {
      console.warn(`[RegardingResolver] Nav-prop discovery failed for ${hostEntity}: HTTP ${resp.status}`);
      return [];
    }

    const json = (await resp.json()) as {
      value?: Array<{
        ReferencingAttribute: string;
        ReferencingEntityNavigationPropertyName: string;
        ReferencedEntity: string;
      }>;
    };

    const entries: INavPropEntry[] = (json.value ?? []).map(r => ({
      columnName: r.ReferencingAttribute,
      navPropName: r.ReferencingEntityNavigationPropertyName,
      referencedEntity: r.ReferencedEntity,
    }));

    _navPropCache[hostEntity] = entries;
    return entries;
  } catch (err) {
    console.warn(`[RegardingResolver] Nav-prop discovery error for ${hostEntity}:`, err);
    return [];
  }
}

/**
 * Reset the nav-prop cache. Test-only.
 *
 * @internal
 */
export function _resetNavPropCacheForTests(): void {
  for (const k of Object.keys(_navPropCache)) {
    delete _navPropCache[k];
  }
}

// ---------------------------------------------------------------------------
// Catalog lookup
// ---------------------------------------------------------------------------

/**
 * Parse the manifest `regardingTargets` input — a comma-separated list of
 * allowed parent entity logical names. Returns the subset of
 * `TODO_REGARDING_CATALOG` matching that list. When the input is empty or
 * undefined, returns the full canonical catalog.
 */
export function resolveAllowedCatalog(
  regardingTargetsRaw: string | null | undefined
): ReadonlyArray<ITodoRegardingTargetCatalogEntry> {
  if (!regardingTargetsRaw || !regardingTargetsRaw.trim()) {
    return TODO_REGARDING_CATALOG;
  }
  const allowed = new Set(
    regardingTargetsRaw
      .split(',')
      .map(s => s.trim().toLowerCase())
      .filter(Boolean)
  );
  return TODO_REGARDING_CATALOG.filter(c => allowed.has(c.entityType.toLowerCase()));
}

// ---------------------------------------------------------------------------
// Public: apply a selection
// ---------------------------------------------------------------------------

/**
 * Apply a regarding-target selection to the host record.
 *
 * Steps:
 *   1. Find the catalog entry for the selected entity type. Reject if unknown.
 *   2. Discover (or fetch from cache) the host entity's nav-prop table.
 *   3. Build the clear-and-set payload:
 *      - All OTHER 10 entity-specific lookups → @odata.bind = null
 *      - Delegate to applyResolverFields (the SHARED service) for the chosen
 *        lookup + 4 resolver fields. NEVER reimplements that logic.
 *   4. Persist:
 *      - When `hostRecordId` is a real GUID → `webApi.updateRecord(...)`.
 *      - Otherwise (new record) → return the payload for the caller to stage
 *        into form attributes during the pre-save handler.
 */
export async function applyRegardingSelection(
  ctx: IResolverWriteContext,
  selection: IRegardingSelection,
  catalog: ReadonlyArray<ITodoRegardingTargetCatalogEntry> = TODO_REGARDING_CATALOG,
  fetchImpl: typeof fetch = globalThis.fetch
): Promise<IResolverWriteResult> {
  const catalogEntry = catalog.find(c => c.entityType === selection.entityType);
  if (!catalogEntry) {
    return {
      success: false,
      error:
        `[RegardingResolver] Unknown entity type "${selection.entityType}". ` +
        `Allowed: ${catalog.map(c => c.entityType).join(', ')}.`,
    };
  }

  const navProps = await discoverHostNavProps(ctx.hostEntity, fetchImpl);

  // 1. Pre-clear the 10 OTHER entity-specific lookups (clear-and-set per FR-13).
  const payload: Record<string, unknown> = {};
  for (const other of TODO_REGARDING_CATALOG) {
    if (other.entityType === catalogEntry.entityType) continue;
    const navProp = navProps.find(
      n => n.referencedEntity === other.entityType && n.columnName.toLowerCase().includes(other.navPropHint)
    );
    const key = navProp?.navPropName ?? other.lookupAttribute;
    payload[`${key}@odata.bind`] = null;
  }

  // 2. Delegate to the shared service for the SET path (chosen lookup + 4 resolver fields).
  //    THIS IS THE SOLE WRITE LOGIC — never reimplemented per FR-21 / ADR-024.
  await applyResolverFields(
    ctx.webApi,
    payload,
    navProps,
    catalogEntry.entityType,
    catalogEntry.entitySet,
    selection.recordId,
    selection.recordName,
    catalogEntry.navPropHint
  );

  // 3. Persist immediately if we have a host record; otherwise return for pre-save staging.
  const hasHostGuid = Boolean(ctx.hostRecordId && ctx.hostRecordId.replace(/[{}]/g, '').length === 36);
  if (hasHostGuid) {
    try {
      await ctx.webApi.updateRecord(ctx.hostEntity, (ctx.hostRecordId as string).replace(/[{}]/g, ''), payload);
    } catch (err) {
      return {
        success: false,
        catalogEntry,
        payload,
        error: err instanceof Error ? err.message : 'updateRecord failed',
      };
    }
  }

  return { success: true, catalogEntry, payload };
}

// ---------------------------------------------------------------------------
// Public: clear the regarding entirely
// ---------------------------------------------------------------------------

/**
 * Clear the regarding for the host record.
 *
 * Per FR-13 / ADR-024, "no regarding" requires nulling all fifteen fields:
 *   - 11 entity-specific lookups
 *   - sprk_regardingrecordtype (lookup to sprk_recordtype_ref)
 *   - sprk_regardingrecordid (text)
 *   - sprk_regardingrecordname (text)
 *   - sprk_regardingrecordurl (URL)
 */
export async function clearRegarding(
  ctx: IResolverWriteContext,
  fetchImpl: typeof fetch = globalThis.fetch
): Promise<IResolverWriteResult> {
  const navProps = await discoverHostNavProps(ctx.hostEntity, fetchImpl);

  const payload: Record<string, unknown> = {};

  for (const target of TODO_REGARDING_CATALOG) {
    const navProp = navProps.find(
      n => n.referencedEntity === target.entityType && n.columnName.toLowerCase().includes(target.navPropHint)
    );
    const key = navProp?.navPropName ?? target.lookupAttribute;
    payload[`${key}@odata.bind`] = null;
  }

  const recordTypeNavProp = navProps.find(
    n => n.referencedEntity === 'sprk_recordtype_ref' && n.columnName.toLowerCase().includes('regardingrecordtype')
  );
  const recordTypeKey = recordTypeNavProp?.navPropName ?? 'sprk_RegardingRecordType';
  payload[`${recordTypeKey}@odata.bind`] = null;

  payload['sprk_regardingrecordid'] = null;
  payload['sprk_regardingrecordname'] = null;
  payload['sprk_regardingrecordurl'] = null;

  const hasHostGuid = Boolean(ctx.hostRecordId && ctx.hostRecordId.replace(/[{}]/g, '').length === 36);
  if (hasHostGuid) {
    try {
      await ctx.webApi.updateRecord(ctx.hostEntity, (ctx.hostRecordId as string).replace(/[{}]/g, ''), payload);
    } catch (err) {
      return {
        success: false,
        payload,
        error: err instanceof Error ? err.message : 'updateRecord failed',
      };
    }
  }

  return { success: true, payload };
}
