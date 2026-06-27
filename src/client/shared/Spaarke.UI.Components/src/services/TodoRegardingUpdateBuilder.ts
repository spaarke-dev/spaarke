/**
 * TodoRegardingUpdateBuilder
 *
 * Builds a Web API `updateRecord` payload for `sprk_todo` regarding edits per
 * spec.md FR-13 / ADR-024 (Polymorphic Resolver Pattern).
 *
 * The eleven `sprk_regarding*` lookups on `sprk_todo` are mutually exclusive
 * (at most one populated at a time). When a user changes the regarding from
 * one entity type to another, the previous entity-specific lookup MUST be
 * cleared in the same update. When the user clears regarding entirely, all
 * fifteen fields (11 lookups + 4 resolver fields) MUST be nulled.
 *
 * This helper centralizes that clear-and-set semantic so SmartTodo, future
 * Outlook add-in surfaces, and any other host that edits `sprk_todo` regarding
 * post-creation produce identical payloads.
 *
 * @see spec.md FR-13 (TodoDetail regarding edit)
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see PolymorphicResolverService.applyResolverFields — the underlying primitive
 */

import { applyResolverFields, type INavPropEntry, type IPolymorphicWebApi } from './PolymorphicResolverService';

// ---------------------------------------------------------------------------
// Target catalog (mirrors TODO_REGARDING_TARGETS in AssociateToStep/types.ts)
//
// Kept here as a local constant so this service has no dependency on the
// AssociateToStep component package. The two arrays MUST stay in sync; a unit
// test in AssociateToStep verifies the spec order + lookup attributes.
// ---------------------------------------------------------------------------

/**
 * Catalog entry: (entityType, entitySet, lookupAttribute, navPropHint).
 *
 * - `entityType`: Dataverse logical name (e.g. `sprk_matter`, `contact`).
 * - `entitySet`: OData entity-set name used in `@odata.bind` URLs.
 * - `lookupAttribute`: Lookup column name on `sprk_todo`
 *   (e.g. `sprk_regardingmatter`).
 * - `navPropHint`: Short token used by `findNavProp` to disambiguate when
 *   `sprk_todo` has multiple lookups referencing the same entity. For Todo
 *   regarding lookups each parent entity has exactly one referencing column,
 *   so the hint is just the entity short-name.
 */
export interface ITodoRegardingTargetCatalogEntry {
  entityType: string;
  entitySet: string;
  lookupAttribute: string;
  navPropHint: string;
}

/**
 * The eleven canonical `sprk_todo` regarding targets, in spec.md FR-07 order.
 *
 * Entity-set names follow Dataverse plural convention:
 *   - `sprk_matter` → `sprk_matters`
 *   - `sprk_communication` → `sprk_communications` (custom, plural)
 *   - `contact` → `contacts` (OOB)
 *   - `sprk_analysis` → `sprk_analyses` (irregular plural)
 *
 * @see src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md
 */
export const TODO_REGARDING_CATALOG: ReadonlyArray<ITodoRegardingTargetCatalogEntry> = [
  {
    entityType: 'sprk_matter',
    entitySet: 'sprk_matters',
    lookupAttribute: 'sprk_regardingmatter',
    navPropHint: 'matter',
  },
  {
    entityType: 'sprk_project',
    entitySet: 'sprk_projects',
    lookupAttribute: 'sprk_regardingproject',
    navPropHint: 'project',
  },
  { entityType: 'sprk_event', entitySet: 'sprk_events', lookupAttribute: 'sprk_regardingevent', navPropHint: 'event' },
  {
    entityType: 'sprk_communication',
    entitySet: 'sprk_communications',
    lookupAttribute: 'sprk_regardingcommunication',
    navPropHint: 'communication',
  },
  {
    entityType: 'sprk_workassignment',
    entitySet: 'sprk_workassignments',
    lookupAttribute: 'sprk_regardingworkassignment',
    navPropHint: 'workassignment',
  },
  {
    entityType: 'sprk_invoice',
    entitySet: 'sprk_invoices',
    lookupAttribute: 'sprk_regardinginvoice',
    navPropHint: 'invoice',
  },
  {
    entityType: 'sprk_budget',
    entitySet: 'sprk_budgets',
    lookupAttribute: 'sprk_regardingbudget',
    navPropHint: 'budget',
  },
  {
    entityType: 'sprk_analysis',
    entitySet: 'sprk_analyses',
    lookupAttribute: 'sprk_regardinganalysis',
    navPropHint: 'analysis',
  },
  {
    entityType: 'sprk_organization',
    entitySet: 'sprk_organizations',
    lookupAttribute: 'sprk_regardingorganization',
    navPropHint: 'organization',
  },
  { entityType: 'contact', entitySet: 'contacts', lookupAttribute: 'sprk_regardingcontact', navPropHint: 'contact' },
  {
    entityType: 'sprk_document',
    entitySet: 'sprk_documents',
    lookupAttribute: 'sprk_regardingdocument',
    navPropHint: 'document',
  },
] as const;

// ---------------------------------------------------------------------------
// Payload shape
// ---------------------------------------------------------------------------

/**
 * Generic Dataverse update payload — the shape passed to
 * `Xrm.WebApi.updateRecord("sprk_todo", id, payload)`.
 *
 * Mixed shape because @odata.bind keys are interpolated property names that
 * carry either a `/{entitySet}({guid})` string or `null` to clear the binding.
 */
export type ITodoRegardingUpdate = Record<string, string | number | boolean | null | undefined>;

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Build the Web API payload that sets a `sprk_todo`'s regarding to a single
 * parent record (per FR-13), atomically:
 *
 *   1. Sets the four resolver fields (recordtype, recordid, recordname, recordurl)
 *      via `PolymorphicResolverService.applyResolverFields`.
 *   2. Binds the chosen entity-specific lookup (e.g., `sprk_regardingmatter`).
 *   3. CLEARS the other ten entity-specific lookups by setting their
 *      `@odata.bind` keys to `null` — guaranteeing at most one lookup is
 *      populated at any time.
 *
 * The caller passes the resulting payload to `webApi.updateRecord("sprk_todo", id, payload)`.
 *
 * @param webApi          — IPolymorphicWebApi (Xrm.WebApi-compatible) for
 *                          `sprk_recordtype_ref` lookup inside applyResolverFields.
 * @param navProps        — Nav-prop entries discovered for `sprk_todo` via the
 *                          ManyToOneRelationships metadata endpoint.
 * @param target          — The chosen entity type (one of TODO_REGARDING_CATALOG).
 * @param recordId        — GUID of the selected parent record (with or without braces).
 * @param recordName      — Display name of the selected parent record.
 *
 * @throws Error if `target` is not in `TODO_REGARDING_CATALOG` (i.e., not a known
 *               `sprk_todo` regarding target).
 */
export async function buildTodoRegardingUpdate(
  webApi: IPolymorphicWebApi,
  navProps: INavPropEntry[],
  target: { entityType: string },
  recordId: string,
  recordName: string
): Promise<ITodoRegardingUpdate> {
  const catalogEntry = TODO_REGARDING_CATALOG.find(c => c.entityType === target.entityType);
  if (!catalogEntry) {
    throw new Error(
      `[TodoRegardingUpdateBuilder] Unknown entity type "${target.entityType}". ` +
        `Must be one of: ${TODO_REGARDING_CATALOG.map(c => c.entityType).join(', ')}.`
    );
  }

  // Start with all 10 OTHER lookups explicitly cleared (clear-and-set per FR-13).
  // The chosen lookup is then set by applyResolverFields → overwriting the null.
  const entity: ITodoRegardingUpdate = {};
  for (const other of TODO_REGARDING_CATALOG) {
    if (other.entityType !== catalogEntry.entityType) {
      // Nav-prop name is discovered from `navProps` rather than guessed —
      // applyResolverFields uses the same lookup. For the CLEAR side we set
      // it via the lookup-attribute fallback (Dataverse accepts both navprop
      // and column-name@odata.bind in update payloads, but to maximize
      // compatibility we use the discovered navprop when available).
      const navProp = navProps.find(
        n => n.referencedEntity === other.entityType && n.columnName.toLowerCase().includes(other.navPropHint)
      );
      const key = navProp?.navPropName ?? other.lookupAttribute;
      entity[`${key}@odata.bind`] = null;
    }
  }

  // Delegate to the canonical resolver service (ADR-024). It populates the
  // four resolver fields AND the entity-specific @odata.bind for the chosen
  // target — overwriting the corresponding `null` we set above (if any).
  await applyResolverFields(
    webApi,
    entity as Record<string, unknown>,
    navProps,
    catalogEntry.entityType,
    catalogEntry.entitySet,
    recordId,
    recordName,
    catalogEntry.navPropHint
  );

  return entity;
}

/**
 * Build the Web API payload that CLEARS a `sprk_todo`'s regarding entirely.
 *
 * Per FR-13 + ADR-024, "no regarding" requires nulling all fifteen fields:
 *   - The 11 entity-specific lookups (sprk_regardingmatter, …, sprk_regardingdocument)
 *   - The 4 resolver fields (sprk_regardingrecordtype, sprk_regardingrecordid,
 *     sprk_regardingrecordname, sprk_regardingrecordurl)
 *
 * Resolver text/URL fields are set to `null` (Dataverse accepts null for
 * Text+URL fields). The two lookups (entity-specific + `sprk_regardingrecordtype`)
 * are nulled via `@odata.bind = null`.
 *
 * @param navProps — Nav-prop entries discovered for `sprk_todo`. Used to
 *                   resolve the canonical nav-prop names so the resulting
 *                   payload survives renaming of the underlying nav-props.
 */
export function buildTodoRegardingClear(navProps: INavPropEntry[]): ITodoRegardingUpdate {
  const entity: ITodoRegardingUpdate = {};

  // 1. Null all 11 entity-specific lookups.
  for (const target of TODO_REGARDING_CATALOG) {
    const navProp = navProps.find(
      n => n.referencedEntity === target.entityType && n.columnName.toLowerCase().includes(target.navPropHint)
    );
    const key = navProp?.navPropName ?? target.lookupAttribute;
    entity[`${key}@odata.bind`] = null;
  }

  // 2. Null the resolver record-type lookup.
  const recordTypeNavProp = navProps.find(
    n => n.referencedEntity === 'sprk_recordtype_ref' && n.columnName.toLowerCase().includes('regardingrecordtype')
  );
  const recordTypeKey = recordTypeNavProp?.navPropName ?? 'sprk_RegardingRecordType';
  entity[`${recordTypeKey}@odata.bind`] = null;

  // 3. Null the three resolver text/URL fields.
  entity['sprk_regardingrecordid'] = null;
  entity['sprk_regardingrecordname'] = null;
  entity['sprk_regardingrecordurl'] = null;

  return entity;
}

// ---------------------------------------------------------------------------
// Nav-prop discovery helper (shared with wizard services)
// ---------------------------------------------------------------------------

/**
 * Discover ManyToOne navigation properties for `sprk_todo`.
 *
 * Mirrors the `_discoverNavProps` pattern used by WorkAssignmentService and
 * EventService — kept inline here so SmartTodo doesn't have to import a
 * wizard service. Result is cached per logical name for the lifetime of the
 * page.
 *
 * @internal — exported for tests; the typical caller is `buildTodoRegardingUpdate`.
 */
const _navPropCache: Record<string, INavPropEntry[]> = {};

export async function discoverTodoNavProps(fetchImpl: typeof fetch = globalThis.fetch): Promise<INavPropEntry[]> {
  const cacheKey = 'sprk_todo';
  if (_navPropCache[cacheKey]) {
    return _navPropCache[cacheKey];
  }

  try {
    const url =
      `/api/data/v9.0/EntityDefinitions(LogicalName='${cacheKey}')/ManyToOneRelationships` +
      `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity`;

    const resp = await fetchImpl(url, { credentials: 'include' });
    if (!resp.ok) {
      console.warn(`[TodoRegardingUpdateBuilder] Nav-prop discovery failed for ${cacheKey}: HTTP ${resp.status}`);
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

    _navPropCache[cacheKey] = entries;
    return entries;
  } catch (err) {
    console.warn(`[TodoRegardingUpdateBuilder] Nav-prop discovery error for ${cacheKey}:`, err);
    return [];
  }
}

/**
 * Reset the nav-prop cache. Used by tests to ensure isolation.
 *
 * @internal
 */
export function _resetTodoNavPropCacheForTests(): void {
  delete _navPropCache['sprk_todo'];
}
