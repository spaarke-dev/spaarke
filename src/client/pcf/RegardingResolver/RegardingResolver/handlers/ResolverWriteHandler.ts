/**
 * ResolverWriteHandler — the SOLE write path inside the RegardingResolver PCF.
 *
 * Wraps `PolymorphicResolverService.buildRegardingSelectionPayload` from
 * `@spaarke/ui-components`. This module exists to satisfy two binding constraints:
 *
 *   1. ADR-024 / FR-21 — the PCF MUST NOT reimplement the FR-13 mutual-exclusivity
 *      logic, nor (since task 051) the FR-26 core-ancestor derivation. All payload
 *      assembly lives in the shared service. This handler is a thin coordinator:
 *      discover nav-props, look up the catalog entry, hand off to
 *      `buildRegardingSelectionPayload`, then write the resulting payload via
 *      `webApi.updateRecord`.
 *
 *   2. FR-22 — zero entity-specific code branches. The host entity is passed in
 *      via `hostEntity` (the manifest `entity` input property); the catalog of 12
 *      regarding targets is the same for any host entity that follows the resolver
 *      pattern. There are NO `sprk_todo` / `sprk_communication` literals here —
 *      every Dataverse logical name is a parameter or comes from the shared
 *      `TODO_REGARDING_CATALOG` / `CORE_ANCESTOR_LOOKUPS` constants.
 *
 * # v1.5.0 (unified-access-control-r2 task 051 — FR-26 ancestor re-stamp)
 *
 * A CHILD record's access is a ONE-HOP set-membership test against a denormalized
 * core-ancestor lookup that the child row itself carries
 * (`child.sprk_regarding{core} ∈ {accessible core ids}`). That stamp IS the access
 * boundary, so all THREE transitions must maintain it:
 *
 *   - **set**      — stamp the selected target's ultimate core ancestor.
 *   - **reparent** — write the NEW ancestor and null the OLD one in the SAME
 *                    payload. A surviving stale stamp is simultaneously an
 *                    over-grant (the old parent's principals still reach the row)
 *                    and an under-grant (the new parent's principals do not).
 *   - **clear**    — null the ancestor stamps along with the regarding fields.
 *                    Detaching a parent must not leave the child visible to it.
 *
 * The ordering that makes reparent correct (derive → pre-clear → set → stamp LAST)
 * lives in the shared `buildRegardingSelectionPayload`, NOT here — see
 * `projects/unified-access-control-r2/notes/phase3-derivation-rules.md` §4. This
 * handler deliberately holds no ordering assumptions of its own.
 *
 * Fail-closed (NFR-01): when the shared derivation returns `status: 'error'` the
 * handler surfaces the error and writes NOTHING. A partially-stamped child is
 * worse than an unsaved one.
 *
 * The handler also covers the new-record / existing-record split:
 *   - For an existing record (recordId is a real GUID), the payload is written
 *     immediately via `Xrm.WebApi.updateRecord`.
 *   - For a new record (no GUID yet), the caller mutates the form's pre-save
 *     buffer via `Xrm.Page.getAttribute(...).setValue(...)` for each field, so
 *     the resolver payload — INCLUDING the ancestor stamps — rides the form's
 *     CREATE transaction in one INSERT. Never a follow-up update: a crash between
 *     create and stamp would leave an unscoped child.
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver.md
 * @see projects/unified-access-control-r2/notes/phase3-derivation-rules.md
 * @see projects/smart-todo-r4/notes/regarding-resolver-audit.md §4
 */

// Deep (per-module) imports — NOT the root '@spaarke/ui-components' barrel
// (dist/index re-exports SprkChat → pdfjs, which the PCF webpack build can't
// transform). ADR-012 PCF Import Pattern.
import {
  buildRegardingSelectionPayload,
  findHostNavPropForLookup,
  CORE_ANCESTOR_LOOKUPS,
  type CoreAncestorDerivationStatus,
  type ICoreAncestorStamp,
  type INavPropEntry,
  type IPolymorphicWebApi,
} from '@spaarke/ui-components/dist/services/PolymorphicResolverService';
import {
  TODO_REGARDING_CATALOG,
  type ITodoRegardingTargetCatalogEntry,
} from '@spaarke/ui-components/dist/services/TodoRegardingUpdateBuilder';

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
  /**
   * Resolved `sprk_regardingrecordnumber` value from the target record, as
   * returned by the shared `applyResolverFields` service (SRFR-020). `null` when
   * metadata was missing OR the target's value was null/empty (NFR-06
   * graceful-blank — e.g., Contact / Account intentional-null per Q-06). The
   * CREATE-mode presave bridge (`__sprk_regarding_pending__.recordNumber`)
   * propagates this to the OnSave handler so it can stage
   * `sprk_regardingrecordnumber` onto the form for the INSERT transaction
   * (SRFR-032 / SRFR-040 FR-A5-04).
   */
  recordNumber?: string | null;
  /**
   * v1.4.5 (SRFR-054): resolved display-name value from the target record.
   * The shared `applyResolverFields` (SRFR-052) resolves this via
   * `sprk_recordtype_ref.sprk_recorddisplaynamefield` metadata and target-record
   * query. `null` when metadata was missing OR target's value was null/empty
   * (NFR-06). Consumer (RegardingResolverApp CREATE-mode presave bridge) MUST
   * propagate this into `__sprk_regarding_pending__.recordName` so the presave
   * webresource stages the correct display-name for the INSERT transaction —
   * NOT the picker-returned Primary Name (which for sprk_matter is the number).
   */
  displayName?: string | null;
  /**
   * v1.5.0 (FR-26 / task 051): the core-ancestor stamps the shared derivation
   * produced for this selection, in ATTRIBUTE-logical-name terms (the payload
   * itself is keyed by nav-prop name, which form attributes cannot consume).
   *
   * UPDATE mode already wrote these inside `payload`. CREATE mode MUST stage
   * them onto form attributes so they ride the INSERT — see the presave bridge
   * in `RegardingResolverApp` + `sprk_todo_regarding_presave.js`.
   *
   * Includes a CORE target's self-stamp (where the stamp column IS the chosen
   * lookup). Staging it twice is idempotent — same id, same entity — and keeps
   * the access edge belt-and-braces even if `lookupAttribute` were ever dropped
   * from the bridge.
   */
  ancestorStamps?: ICoreAncestorStamp[];
  /**
   * v1.5.0 (FR-26): the derivation status from the shared service. Distinct
   * states matter — `no-ancestor` ("this record inherits nothing") and `error`
   * ("we could not find out") must never share a branch (NFR-01).
   */
  ancestorStatus?: CoreAncestorDerivationStatus;
  /**
   * v1.5.0 (FR-26 / F-050-2): ancestor lookups that were DERIVED but cannot be
   * written because the host entity has no such column. Each entry is a real
   * hole in child inheritance for that host/ancestor pair; surfaced, never
   * swallowed.
   *
   * No entity is named here on purpose. The stock example used to be
   * "`sprk_todo` has no `sprk_regardingservicerequest`" — which live metadata
   * disproved on 2026-09-04. Whether a given host lacks a given column is a
   * schema question this code never assumes: presence is resolved from the
   * host's DISCOVERED nav-props on every single write, which is why the wrong
   * belief never became a wrong behaviour.
   */
  unstampable?: string[];
  /**
   * v1.5.0 (FR-26): regarding/ancestor lookup columns this payload NULLS, in
   * ATTRIBUTE-logical-name terms. This is the reparent half of the contract —
   * CREATE mode must stage these clears so a pick-A-then-pick-B before the first
   * save does not leave A's lookup (and A's ancestor stamp) on the INSERT.
   *
   * Derived by mapping the shared payload's null `@odata.bind` keys back through
   * the host nav-props, so it cannot drift from what the shared service actually
   * clears.
   */
  clearLookups?: string[];
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
    // v1.4.2 (SRFR-050): Dataverse entity logical names are lowercase-normalized;
    // if the maker set `entity` input with wrong case (e.g., `sprk_Communication`),
    // the metadata endpoint returns 200 with EMPTY results (not 404), which
    // silently breaks nav-prop discovery + all downstream writes. Lowercase
    // defensively.
    const hostEntityLower = hostEntity.toLowerCase();
    const url =
      `/api/data/v9.0/EntityDefinitions(LogicalName='${hostEntityLower}')/ManyToOneRelationships` +
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

    // v1.4.2 (SRFR-050): diagnostic warning if discovery returned no entries.
    // Common cause: `entity` input on the manifest was set to a non-existent /
    // misspelled logical name. Silent empty was the v1.4.0/1.4.1 failure mode
    // on sprk_communication placement.
    if (entries.length === 0) {
      console.warn(
        `[RegardingResolver] Nav-prop discovery returned zero entries for hostEntity="${hostEntity}" (normalized "${hostEntityLower}"). ` +
          `Verify the Host Entity input on the form matches an actual entity logical name. All resolver writes will silently fail.`
      );
    }

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
 * Map the null `@odata.bind` keys of an assembled payload back to the host's
 * lookup COLUMN names.
 *
 * The payload is keyed by navigation-property name (`sprk_RegardingMatter`),
 * which is what the Web API needs. Form attributes are keyed by column logical
 * name (`sprk_regardingmatter`), which is what the CREATE-mode presave bridge
 * needs. This translates one to the other using the host's discovered nav-props
 * — deriving the clear set FROM the shared service's own output rather than
 * recomputing it, so the two can never disagree about what a reparent clears.
 *
 * Only NULL binds are returned: a stamp this payload is SETTING carries a
 * non-null value by the time the shared builder returns (it applies stamps last,
 * after the pre-clear), so it correctly never appears here.
 */
function nulledLookupColumns(payload: Record<string, unknown>, navProps: INavPropEntry[]): string[] {
  const BIND_SUFFIX = '@odata.bind';
  const byNavProp = new Map(navProps.map(n => [n.navPropName.toLowerCase(), n.columnName]));
  const columns: string[] = [];
  for (const [key, value] of Object.entries(payload)) {
    if (value !== null) continue;
    if (!key.endsWith(BIND_SUFFIX)) continue;
    const navProp = key.slice(0, -BIND_SUFFIX.length);
    const column = byNavProp.get(navProp.toLowerCase());
    if (column) columns.push(column);
  }
  return columns;
}

/**
 * Apply a regarding-target selection to the host record.
 *
 * Steps:
 *   1. Find the catalog entry for the selected entity type. Reject if unknown.
 *   2. Discover (or fetch from cache) the host entity's nav-prop table.
 *   3. Delegate the ENTIRE payload to the shared
 *      `buildRegardingSelectionPayload` (task 050). It owns, in this order:
 *        a. FR-26 core-ancestor derivation (fails closed — see step 4);
 *        b. the FR-13 pre-clear of every OTHER regarding lookup that exists on
 *           the host, unioned with the four core-ancestor lookups (so a stale
 *           stamp cannot survive a reparent even when the host catalog omits
 *           that core entity — `sprk_todo` has no service-request entry);
 *        c. `applyResolverFields` for the chosen lookup + the 5 resolver fields;
 *        d. the ancestor stamps LAST, so a stamp being set can never be nulled
 *           by (b).
 *      This handler holds NO ordering assumptions of its own — that is the whole
 *      reason the shared builder is a single exported function.
 *   4. Fail closed on a derivation error: surface it and write NOTHING. Under-
 *      or over-granting a child is worse than a failed save (NFR-01).
 *   5. Persist:
 *      - When `hostRecordId` is a real GUID → `webApi.updateRecord(...)`, ONE
 *        call carrying the target lookup, the resolver fields, the new ancestor
 *        stamp AND the old ancestor's null.
 *      - Otherwise (new record) → return the payload plus `ancestorStamps` /
 *        `clearLookups` for the caller to stage into form attributes during the
 *        pre-save handler, so everything rides a single INSERT.
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

  // Delegate the whole payload — pre-clear, resolver fields, and the FR-26
  // ancestor stamps — to the shared builder (ADR-024 / FR-21 / FR-26).
  //
  // The pre-clear catalog is the FULL canonical `TODO_REGARDING_CATALOG`, NOT
  // the (possibly maker-restricted) `catalog` argument. `catalog` governs what a
  // user may SELECT; the clear set must span every lookup the host can carry, or
  // narrowing the maker's `regardingTargets` list would strand a previously-set
  // lookup — and, with it, a stale ancestor stamp. Presence on the host is still
  // resolved from `navProps` inside the shared builder, preserving the SRFR-048
  // rule that we never write a column the host does not have.
  const built = await buildRegardingSelectionPayload(
    ctx.webApi,
    navProps,
    TODO_REGARDING_CATALOG,
    catalogEntry,
    selection.recordId,
    selection.recordName,
    undefined,
    fetchImpl
  );

  // Fail closed (NFR-01): a failed derivation means we do not know this child's
  // ancestor. Writing anyway would silently under-grant. Nothing is persisted
  // and nothing is staged — the caller surfaces the error state.
  if (!built.success || !built.payload) {
    return {
      success: false,
      catalogEntry,
      ancestorStatus: built.ancestor.status,
      error:
        built.error ??
        `[RegardingResolver] Core-ancestor derivation failed for ` +
          `${catalogEntry.entityType}(${selection.recordId}); refusing to write an unstamped record (FR-26 / NFR-01).`,
    };
  }

  const payload = built.payload;
  const ancestorStamps = built.ancestor.stamps.filter(s => !built.unstampable.includes(s.lookupAttribute));
  const clearLookups = nulledLookupColumns(payload, navProps);

  // Persist immediately if we have a host record; otherwise return for pre-save staging.
  const hasHostGuid = Boolean(ctx.hostRecordId && ctx.hostRecordId.replace(/[{}]/g, '').length === 36);
  if (hasHostGuid) {
    try {
      await ctx.webApi.updateRecord(ctx.hostEntity, (ctx.hostRecordId as string).replace(/[{}]/g, ''), payload);
    } catch (err) {
      return {
        success: false,
        catalogEntry,
        payload,
        recordNumber: built.resolverResult?.recordNumber,
        displayName: built.resolverResult?.displayName,
        ancestorStamps,
        ancestorStatus: built.ancestor.status,
        unstampable: built.unstampable,
        clearLookups,
        error: err instanceof Error ? err.message : 'updateRecord failed',
      };
    }
  }

  return {
    success: true,
    catalogEntry,
    payload,
    recordNumber: built.resolverResult?.recordNumber,
    // v1.5.0: `displayName` was resolved by the shared service since SRFR-052 but
    // was never forwarded on the SUCCESS branch (only the failure branch), so the
    // CREATE-mode presave bridge's `result.displayName ?? selection.recordName`
    // always fell through to the picker's Primary Name — the exact defect
    // SRFR-054 set out to fix. Forwarded here.
    displayName: built.resolverResult?.displayName,
    ancestorStamps,
    ancestorStatus: built.ancestor.status,
    unstampable: built.unstampable,
    clearLookups,
  };
}

// ---------------------------------------------------------------------------
// Public: clear the regarding entirely
// ---------------------------------------------------------------------------

/**
 * Clear the regarding for the host record.
 *
 * Per FR-13 / ADR-024, "no regarding" requires nulling all sixteen fields:
 *   - the 12 entity-specific lookups
 *   - sprk_regardingrecordtype (lookup to sprk_recordtype_ref)
 *   - sprk_regardingrecordid (text)
 *   - sprk_regardingrecordname (text)
 *   - sprk_regardingrecordurl (URL)
 *
 * # v1.5.0 (FR-26 / task 051) — clear the ancestor stamps too
 *
 * A cleared child has no parent, therefore no inherited access. Leaving a
 * `sprk_regarding{core}` stamp behind would keep the row visible to the former
 * ancestor's principals *after the user believes they detached it* — the same
 * silent over-grant as a missed reparent, and the transition most likely to be
 * missed because "clear" reads like a no-op for a field nobody set by hand.
 *
 * The catalog covers three of the four core entities (matter, project, work
 * assignment) but NOT `sprk_servicerequest` — which is exactly why the union
 * below exists (finding F-050-3): on any host that carries
 * `sprk_regardingservicerequest`, the catalog loop alone would leave that stamp
 * standing after the user detached the parent.
 *
 * That gap is wider than it was first believed to be. `sprk_todo` was thought
 * not to have the column at all; live metadata (2026-09-04) says it does — so
 * the union is load-bearing on the most common host, not an edge case. This is a
 * CATALOG fact, not a schema fact: which hosts carry the column is resolved at
 * runtime, immediately below.
 *
 * Presence is still resolved against the host's discovered nav-props, preserving
 * the SRFR-048 rule: writing a column the host lacks makes Dataverse reject the
 * whole update with "Invalid property", turning a schema gap into a blocked save.
 */
export async function clearRegarding(
  ctx: IResolverWriteContext,
  fetchImpl: typeof fetch = globalThis.fetch
): Promise<IResolverWriteResult> {
  const navProps = await discoverHostNavProps(ctx.hostEntity, fetchImpl);

  const payload: Record<string, unknown> = {};

  // 1. Null every catalog lookup that ACTUALLY EXISTS on this host (SRFR-048).
  //    Nav-prop resolution goes through the shared `findHostNavPropForLookup` so
  //    the CLEAR path names columns exactly the way the SET path does — if the
  //    two resolved differently, a reparent could null one key while setting
  //    another for the same underlying column.
  const seen = new Set<string>();
  for (const target of TODO_REGARDING_CATALOG) {
    seen.add(target.lookupAttribute.toLowerCase());
    const navProp = findHostNavPropForLookup(navProps, target.entityType, target.lookupAttribute, target.navPropHint);
    if (!navProp) continue; // Lookup doesn't exist on this host entity — skip.
    payload[`${navProp}@odata.bind`] = null;
  }

  // 2. Null any FR-26 core-ancestor lookup the catalog does not already cover.
  //    Today that is `sprk_regardingservicerequest`; the loop is written against
  //    the shared constant rather than that literal so a fifth core entity is
  //    picked up automatically.
  for (const core of CORE_ANCESTOR_LOOKUPS) {
    if (seen.has(core.lookupAttribute.toLowerCase())) continue;
    const navProp = findHostNavPropForLookup(navProps, core.entityType, core.lookupAttribute);
    if (!navProp) continue;
    payload[`${navProp}@odata.bind`] = null;
  }

  const recordTypeNavProp = navProps.find(
    n => n.referencedEntity === 'sprk_recordtype_ref' && n.columnName.toLowerCase().includes('regardingrecordtype')
  );
  const recordTypeKey = recordTypeNavProp?.navPropName ?? 'sprk_RegardingRecordType';
  payload[`${recordTypeKey}@odata.bind`] = null;

  payload['sprk_regardingrecordid'] = null;
  payload['sprk_regardingrecordname'] = null;
  payload['sprk_regardingrecordurl'] = null;

  const clearLookups = nulledLookupColumns(payload, navProps);

  const hasHostGuid = Boolean(ctx.hostRecordId && ctx.hostRecordId.replace(/[{}]/g, '').length === 36);
  if (hasHostGuid) {
    try {
      await ctx.webApi.updateRecord(ctx.hostEntity, (ctx.hostRecordId as string).replace(/[{}]/g, ''), payload);
    } catch (err) {
      return {
        success: false,
        payload,
        clearLookups,
        error: err instanceof Error ? err.message : 'updateRecord failed',
      };
    }
  }

  return { success: true, payload, ancestorStamps: [], clearLookups };
}
