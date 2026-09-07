/**
 * PolymorphicResolverService.ts
 *
 * Shared service for the Polymorphic Resolver pattern (ADR-024).
 * Provides helpers to populate both entity-specific lookups and
 * denormalized resolver fields when programmatically creating or
 * updating Dataverse records.
 *
 * Used by: WorkAssignmentService, EventService, CommunicationService,
 *          EntityCreationService, and any future wizard/service that
 *          creates child records with regarding associations.
 *
 * The pattern uses two field groups on the child entity:
 *   1. Entity-specific lookup: sprk_regarding{entity} (one per parent type)
 *   2. Resolver fields (denormalized for cross-entity views):
 *      - sprk_regardingrecordtype    (Lookup → sprk_recordtype_ref)
 *      - sprk_regardingrecordid      (Text — parent GUID)
 *      - sprk_regardingrecordname    (Text — parent display name)
 *      - sprk_regardingrecordurl     (URL — clickable link to parent)
 *      - sprk_regardingrecordnumber  (Text — target record's business number,
 *                                     e.g., sprk_matternumber value;
 *                                     added set-regarding-and-field-mapping-
 *                                     resolver-r1 per FR-A4-01 / FR-C1-01)
 *
 * The 5th field (`sprk_regardingrecordnumber`) is sourced via data-driven
 * resolution over `sprk_recordtype_ref.sprk_regardingrecordnumberfield`. The
 * catalog entry names the source-field on the target entity (e.g., Matter →
 * `sprk_matternumber`, Account → `accountnumber`); the resolver then queries
 * the target record for that field's value and writes it to the host's
 * `sprk_regardingrecordnumber`. When the catalog value is null (Contact/Person
 * per Q-06 owner clarification) or the target-record value is null, the
 * resolver logs a warn and skips the 5th field per NFR-06 graceful-blank.
 *
 * SRFR-052 (2026-07-06) — display-name resolution:
 *   The 3rd field (`sprk_regardingrecordname`) is now ALSO resolved through
 *   the same catalog mechanism, via `sprk_recordtype_ref.sprk_recorddisplaynamefield`.
 *   Owner UAT surfaced that `Xrm.Utility.lookupObjects` returns the target
 *   record's PRIMARY NAME, but for sprk_matter the Primary Name column is
 *   `sprk_matternumber` (NOT `sprk_mattername`) — so the picker was handing
 *   back the number as the display name. The resolver now queries the target
 *   record for the catalog-nominated display-name field (e.g., Matter →
 *   `sprk_mattername`, Account → `name`, Contact → `fullname`) and writes
 *   that value to `sprk_regardingrecordname`. Falls back to
 *   `parentRecordName` when the catalog value is null OR the target record's
 *   value is null/empty (NFR-06 graceful-blank).
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Minimal WebApi interface matching both Xrm.WebApi and our IWebApi type. */
export interface IPolymorphicWebApi {
  retrieveMultipleRecords(
    entityLogicalName: string,
    query: string,
    maxPageSize?: number
  ): Promise<{ entities: Record<string, unknown>[] }>;
}

/** Result of querying sprk_recordtype_ref for an entity's record type. */
export interface IRecordTypeRef {
  id: string;
  name: string;
}

/** Nav-prop entry from ManyToOneRelationships metadata discovery. */
export interface INavPropEntry {
  columnName: string;
  navPropName: string;
  referencedEntity: string;
}

/** All the fields needed to populate the polymorphic resolver on a record. */
export interface IResolverFieldValues {
  /** Entity-specific lookup @odata.bind value (e.g., `/sprk_matters(guid)`). */
  entitySpecificBind?: { navProp: string; value: string };
  /** sprk_regardingrecordtype @odata.bind value. */
  recordTypeBind?: { navProp: string; value: string };
  /** sprk_regardingrecordid — parent GUID as text. */
  recordId: string;
  /** sprk_regardingrecordname — parent display name. */
  recordName: string;
  /** sprk_regardingrecordurl — clickable URL to parent record. */
  recordUrl: string;
}

/**
 * Optional trailing-argument bag for `applyResolverFields()`.
 *
 * Added by `set-regarding-and-field-mapping-resolver-r1` (FR-A4-01 / FR-C1-01)
 * to carry the new `sprk_regardingrecordnumber` extension without a breaking
 * signature change. Absent options object = identical 4-field behavior — no
 * regression for existing callers.
 */
export interface IApplyResolverFieldsOptions {
  /**
   * Explicit override for the source-field name on the target entity to read
   * `sprk_regardingrecordnumber` from (e.g., `sprk_matternumber`,
   * `accountnumber`). When omitted, the resolver consults
   * `sprk_recordtype_ref.sprk_regardingrecordnumberfield` for the
   * `parentEntityLogicalName`. Supply this to bypass metadata lookup for
   * performance or testing.
   */
  sourceRecordNumberField?: string;
  /**
   * Explicit override for the source-field name on the target entity to read
   * `sprk_regardingrecordname` (display name) from (e.g., `sprk_mattername`,
   * `name`, `fullname`). When omitted, the resolver consults
   * `sprk_recordtype_ref.sprk_recorddisplaynamefield` for the
   * `parentEntityLogicalName`. Supply this to bypass metadata lookup for
   * performance or testing. Introduced by SRFR-052 (2026-07-06) after owner
   * UAT surfaced Matter's Primary Name column is sprk_matternumber, causing
   * the picker to return the number as the display name.
   */
  sourceDisplayNameField?: string;
}

/**
 * Result payload returned from `applyResolverFields()`. Includes the resolved
 * `recordNumber` value so callers (e.g., CREATE-mode presave bridge) can
 * propagate it downstream without re-querying.
 *
 * Backward compat: callers that ignored the previous `void` return value
 * still work — TypeScript upcasts `Promise<void>` awaiters to `Promise<T>`
 * transparently.
 */
export interface IApplyResolverFieldsResult {
  /**
   * The resolved record-number value written to
   * `entity['sprk_regardingrecordnumber']`, or `null` when metadata was
   * missing OR the target record's value was null/empty (NFR-06
   * graceful-blank).
   */
  recordNumber: string | null;
  /**
   * The source-field name that was consulted on the target entity, or
   * `null` when metadata was missing. Useful for diagnostics.
   */
  recordNumberSourceField: string | null;
  /**
   * The resolved display-name value written to
   * `entity['sprk_regardingrecordname']`. When metadata resolves and the
   * target record has a value, this is that value; otherwise it is the
   * `parentRecordName` fallback (NFR-06 graceful-blank). Introduced by
   * SRFR-052 (2026-07-06).
   */
  displayName?: string | null;
}

// ---------------------------------------------------------------------------
// Record Type Ref cache
// ---------------------------------------------------------------------------

const _recordTypeCache = new Map<string, IRecordTypeRef>();

/**
 * Query sprk_recordtype_ref to get the record-type GUID for an entity logical name.
 * Results are cached for the lifetime of the page.
 */
export async function resolveRecordType(
  webApi: IPolymorphicWebApi,
  entityLogicalName: string
): Promise<IRecordTypeRef | null> {
  const cached = _recordTypeCache.get(entityLogicalName);
  if (cached) return cached;

  try {
    const query =
      `?$filter=sprk_recordlogicalname eq '${entityLogicalName}' and statecode eq 0` +
      `&$select=sprk_recordtype_refid,sprk_recorddisplayname`;
    const result = await webApi.retrieveMultipleRecords('sprk_recordtype_ref', query);

    if (result.entities?.length > 0) {
      const rec = result.entities[0];
      const entry: IRecordTypeRef = {
        id: rec['sprk_recordtype_refid'] as string,
        name: rec['sprk_recorddisplayname'] as string,
      };
      _recordTypeCache.set(entityLogicalName, entry);
      return entry;
    }
  } catch (err) {
    console.warn(`[PolymorphicResolver] resolveRecordType(${entityLogicalName}) error:`, err);
  }
  return null;
}

// ---------------------------------------------------------------------------
// Record Number source-field cache (per-entity, per-page-lifetime)
// ---------------------------------------------------------------------------

/**
 * Cache of resolved source-field names keyed by target entity logical name.
 *
 * Value semantics:
 *   - string  → source-field name to read from target record (e.g., `sprk_matternumber`)
 *   - null    → catalog row exists but `sprk_regardingrecordnumberfield` is null/empty
 *               (Contact/Person intentional-null per Q-06 graceful-blank; also any
 *                target entity without a natural business-key text field)
 *
 * Absent key → not yet queried; caller drives the query.
 */
const _recordNumberFieldCache = new Map<string, string | null>();

/**
 * Query `sprk_recordtype_ref` for the `sprk_regardingrecordnumberfield` value
 * associated with a target entity's logical name. Returns the source-field
 * name (e.g., `sprk_matternumber`, `accountnumber`) or `null` when no catalog
 * row matches OR the catalog value is null/empty.
 *
 * NFR-06 (graceful-blank): a null return is a valid "no record-number" signal.
 * Callers MUST NOT treat null as an error; instead they log a warn and skip
 * the record-number write.
 *
 * Cached per page-lifetime keyed on `entityLogicalName`.
 *
 * @param webApi              WebApi shim (Xrm.WebApi-compatible)
 * @param entityLogicalName   Target entity logical name (e.g., 'sprk_matter', 'contact')
 * @returns The source-field name on the target entity, or `null` for graceful-blank
 */
export async function resolveRecordNumberFieldName(
  webApi: IPolymorphicWebApi,
  entityLogicalName: string
): Promise<string | null> {
  if (_recordNumberFieldCache.has(entityLogicalName)) {
    return _recordNumberFieldCache.get(entityLogicalName) ?? null;
  }

  try {
    const query =
      `?$filter=sprk_recordlogicalname eq '${entityLogicalName}' and statecode eq 0` +
      `&$select=sprk_regardingrecordnumberfield`;
    const result = await webApi.retrieveMultipleRecords('sprk_recordtype_ref', query);

    if (result.entities && result.entities.length > 0) {
      const raw = result.entities[0]['sprk_regardingrecordnumberfield'];
      const value = typeof raw === 'string' && raw.trim().length > 0 ? raw.trim() : null;
      _recordNumberFieldCache.set(entityLogicalName, value);
      return value;
    }

    // No catalog row at all — treat as graceful-blank.
    _recordNumberFieldCache.set(entityLogicalName, null);
    return null;
  } catch (err) {
    console.warn(`[PolymorphicResolver] resolveRecordNumberFieldName(${entityLogicalName}) error:`, err);
    // Do not cache on error — allow retry on next call.
    return null;
  }
}

/**
 * Reset the record-number source-field cache. Test-only.
 *
 * @internal
 */
export function _resetRecordNumberFieldCacheForTests(): void {
  _recordNumberFieldCache.clear();
}

// ---------------------------------------------------------------------------
// Display-name source-field cache (per-entity, per-page-lifetime) — SRFR-052
// ---------------------------------------------------------------------------

/**
 * Cache of resolved display-name source-field names keyed by target entity
 * logical name.
 *
 * Value semantics:
 *   - string  → source-field name to read from target record (e.g.,
 *               `sprk_mattername`, `name`, `fullname`)
 *   - null    → catalog row exists but `sprk_recorddisplaynamefield` is
 *               null/empty; caller should fall back to `parentRecordName`
 *
 * Absent key → not yet queried; caller drives the query.
 *
 * Introduced by SRFR-052 (2026-07-06) — see file docstring.
 */
const _displayNameFieldCache = new Map<string, string | null>();

/**
 * Query `sprk_recordtype_ref` for the `sprk_recorddisplaynamefield` value
 * associated with a target entity's logical name. Returns the source-field
 * name (e.g., `sprk_mattername`, `name`, `fullname`) or `null` when no
 * catalog row matches OR the catalog value is null/empty.
 *
 * Parallels {@link resolveRecordNumberFieldName} — same cache/query pattern,
 * different catalog column. Introduced by SRFR-052 after owner UAT surfaced
 * that Matter's Primary Name column is `sprk_matternumber` (NOT
 * `sprk_mattername`) so `Xrm.Utility.lookupObjects` returned the number as
 * the display name. Reading the catalog-nominated display-name field on the
 * target record yields the true business name.
 *
 * NFR-06 (graceful-blank): a null return means "no catalog mapping" — the
 * caller (applyResolverFields) falls back to the picker-provided
 * `parentRecordName` rather than skipping the write. This preserves the
 * legacy 4-field write shape when the catalog is unpopulated.
 *
 * Cached per page-lifetime keyed on `entityLogicalName`.
 *
 * @param webApi              WebApi shim (Xrm.WebApi-compatible)
 * @param entityLogicalName   Target entity logical name (e.g., 'sprk_matter', 'contact')
 * @returns The source-field name on the target entity, or `null` when no mapping
 */
export async function resolveRecordDisplayNameFieldName(
  webApi: IPolymorphicWebApi,
  entityLogicalName: string
): Promise<string | null> {
  if (_displayNameFieldCache.has(entityLogicalName)) {
    return _displayNameFieldCache.get(entityLogicalName) ?? null;
  }

  try {
    const query =
      `?$filter=sprk_recordlogicalname eq '${entityLogicalName}' and statecode eq 0` +
      `&$select=sprk_recorddisplaynamefield`;
    const result = await webApi.retrieveMultipleRecords('sprk_recordtype_ref', query);

    if (result.entities && result.entities.length > 0) {
      const raw = result.entities[0]['sprk_recorddisplaynamefield'];
      const value = typeof raw === 'string' && raw.trim().length > 0 ? raw.trim() : null;
      _displayNameFieldCache.set(entityLogicalName, value);
      return value;
    }

    // No catalog row at all — treat as no-mapping (caller falls back to parentRecordName).
    _displayNameFieldCache.set(entityLogicalName, null);
    return null;
  } catch (err) {
    console.warn(`[PolymorphicResolver] resolveRecordDisplayNameFieldName(${entityLogicalName}) error:`, err);
    // Do not cache on error — allow retry on next call.
    return null;
  }
}

/**
 * Reset the display-name source-field cache. Test-only.
 *
 * @internal
 */
export function _resetDisplayNameFieldCacheForTests(): void {
  _displayNameFieldCache.clear();
}

// ---------------------------------------------------------------------------
// GUID normalization (canonical) — the ONE place braces get stripped
// ---------------------------------------------------------------------------

/**
 * Normalize a GUID for use in a Dataverse OData `@odata.bind` key predicate
 * (or any `/entityset(guid)` reference URL).
 *
 * Several sources hand back GUIDs in registry/braced format — e.g.
 * `Xrm.Utility.lookupObjects` and `Xrm.Utility.getGlobalContext().userSettings`
 * return `{39CDE3E3-9D15-...}`, and `Xrm.WebApi.createRecord` can return a braced
 * id. Dataverse rejects `({GUID})` in a key predicate with a generic HTTP 400
 * "Bad Request - Error in query syntax."
 *
 * This is the single canonical normalizer — strip braces + whitespace and
 * lowercase (Dataverse GUID keys are case-insensitive). Every wizard/service/
 * adapter that builds an `@odata.bind` value (or ingests an Xrm-sourced GUID)
 * routes through here rather than interpolating it raw.
 *
 * Null/undefined-safe: returns '' for a falsy input. No-op on already-bare ids.
 */
export function cleanGuid(id: string | null | undefined): string {
  if (!id) return '';
  return id.replace(/[{}]/g, '').trim().toLowerCase();
}

// ---------------------------------------------------------------------------
// Record URL builder
// ---------------------------------------------------------------------------

/**
 * Build a Dataverse record URL for the sprk_regardingrecordurl field.
 * Tries to resolve clientUrl and appId from the Xrm context; falls back
 * to a relative URL.
 */
export function buildRecordUrl(entityLogicalName: string, recordId: string): string {
  const cleanId = cleanGuid(recordId);

  try {
    // Walk frames to find Xrm
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = (window as any).Xrm ?? (window.parent as any)?.Xrm ?? (window.top as any)?.Xrm;
    const globalCtx = xrm?.Utility?.getGlobalContext?.();
    const clientUrl: string = globalCtx?.getClientUrl?.() ?? '';

    if (clientUrl) {
      const url = new URL('/main.aspx', clientUrl);
      // Try to get app ID from URL params
      const appId =
        new URLSearchParams(window.location.search).get('appid') ??
        new URLSearchParams(window.parent?.location?.search ?? '').get('appid') ??
        '';
      if (appId) url.searchParams.set('appid', appId.replace(/[{}]/g, '').toLowerCase());
      url.searchParams.set('pagetype', 'entityrecord');
      url.searchParams.set('etn', entityLogicalName);
      url.searchParams.set('id', cleanId);
      return url.toString();
    }
  } catch {
    // Cross-origin or missing Xrm — fall back
  }

  // Fallback: relative URL
  return `/main.aspx?pagetype=entityrecord&etn=${entityLogicalName}&id=${cleanId}`;
}

// ---------------------------------------------------------------------------
// Nav-prop helpers
// ---------------------------------------------------------------------------

/**
 * Find a navigation property by referenced entity and optional column hint.
 */
export function findNavProp(
  entries: INavPropEntry[],
  referencedEntity: string,
  columnHint?: string
): string | undefined {
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
// Nav-prop discovery (shared) — consolidated 2026-07-09 (task 011, Path A)
// ---------------------------------------------------------------------------

/**
 * Module-level cache of discovered ManyToOne nav-props, keyed by entity logical
 * name. Lifetime = page session. Shared by the array-form wizard services
 * (Event / Invoice / Project / WorkAssignment / ReportCard / Todo), each of
 * which previously kept an identical private copy of this cache + discovery fn.
 *
 * `matterService` also consumes this function — it resolves nav-props BY COLUMN
 * name (not by referenced entity), so it derives a `Record<columnName, navProp>`
 * map from these entries via {@link toNavPropMap}. That convergence (task 016)
 * replaced matter's former private map-form discovery; the derived map is
 * byte-identical to the old one, so matter's create payload is unchanged.
 * See `projects/set-regarding-and-field-mapping-resolver-r2/notes/task-011-BLOCKED.md`.
 */
const _navPropCache: Record<string, INavPropEntry[]> = {};

/**
 * Discover the single-valued ManyToOne navigation properties for an entity via
 * the Dataverse `EntityDefinitions` metadata endpoint. Returns an array of
 * `{ columnName, navPropName, referencedEntity }` entries for resolution with
 * {@link findNavProp}. Results are cached per entity for the page lifetime.
 *
 * Context-agnostic per ADR-012: uses the host-relative `/api/data/v9.0/...`
 * fetch already used by the wizard services — no `Xrm.WebApi` / PCF APIs. The
 * optional `fetchImpl` parameter is a test seam (defaults to the global
 * `fetch`, resolved at call time); production callers omit it.
 *
 * Never throws — returns `[]` on a non-OK response or a fetch error. Callers
 * treat an empty set as "no nav-prop" and log a warn downstream.
 *
 * @param entityLogicalName  e.g. `'sprk_event'`, `'sprk_invoice'`, `'sprk_document'`
 * @param fetchImpl          Fetch implementation (test seam; default global `fetch`)
 * @returns                  Array of {@link INavPropEntry}; empty on failure
 */
export async function discoverNavProps(
  entityLogicalName: string,
  fetchImpl: typeof fetch = globalThis.fetch
): Promise<INavPropEntry[]> {
  if (_navPropCache[entityLogicalName]) {
    return _navPropCache[entityLogicalName];
  }

  try {
    const url =
      `/api/data/v9.0/EntityDefinitions(LogicalName='${entityLogicalName}')/ManyToOneRelationships` +
      `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity`;

    const resp = await fetchImpl(url, { credentials: 'include' });
    if (!resp.ok) {
      console.warn(`[PolymorphicResolver] Nav-prop discovery failed for ${entityLogicalName}: HTTP ${resp.status}`);
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

    const entries: INavPropEntry[] = rels.map(r => ({
      columnName: r.ReferencingAttribute,
      navPropName: r.ReferencingEntityNavigationPropertyName,
      referencedEntity: r.ReferencedEntity,
    }));

    _navPropCache[entityLogicalName] = entries;
    return entries;
  } catch (err) {
    console.warn(`[PolymorphicResolver] Nav-prop discovery error for ${entityLogicalName}:`, err);
    return [];
  }
}

/**
 * Derive a `columnLogicalName → navPropName` map from discovered nav-prop
 * entries — the shape `matterService` consumes to resolve lookups BY COLUMN
 * name in its create-payload path (`map[col] ?? col`).
 *
 * This is the adapter that lets matter reuse the single shared
 * {@link discoverNavProps} while keeping its column-keyed resolution contract.
 * It reproduces the exact map matter's former private discovery built
 * (`map[ReferencingAttribute] = ReferencingEntityNavigationPropertyName`),
 * INCLUDING last-write-wins when two relationships share a `columnName` —
 * iteration order over `entries` matches the metadata response order, so the
 * emitted `@odata.bind` keys/values are byte-identical to the pre-convergence
 * output (task 016).
 *
 * @param entries  Result of {@link discoverNavProps}
 * @returns        `Record<columnName, navPropName>`
 */
export function toNavPropMap(entries: INavPropEntry[]): Record<string, string> {
  const map: Record<string, string> = {};
  for (const e of entries) {
    map[e.columnName] = e.navPropName;
  }
  return map;
}

/**
 * Reset the shared nav-prop cache. Test-only.
 *
 * @param entityLogicalName  Optional — clear a single entity's entry; omit to clear all.
 * @internal
 */
export function _resetNavPropCacheForTests(entityLogicalName?: string): void {
  if (entityLogicalName) {
    delete _navPropCache[entityLogicalName];
    return;
  }
  for (const k of Object.keys(_navPropCache)) {
    delete _navPropCache[k];
  }
}

// ---------------------------------------------------------------------------
// High-level: apply resolver fields to an entity payload
// ---------------------------------------------------------------------------

/**
 * Populate all polymorphic resolver fields on an entity payload object.
 *
 * Sets:
 *   - Entity-specific lookup via @odata.bind (if navProps provided)
 *   - sprk_regardingrecordid (text)
 *   - sprk_regardingrecordname (text) — SRFR-052 (2026-07-06): resolved via
 *     `sprk_recordtype_ref.sprk_recorddisplaynamefield`, falls back to
 *     `parentRecordName` when catalog value is null OR target record's
 *     value is null/empty. Fixes owner UAT bug where `Xrm.Utility.lookupObjects`
 *     returned Matter's `sprk_matternumber` (its Primary Name column) instead
 *     of `sprk_mattername`.
 *   - sprk_regardingrecordurl (URL)
 *   - sprk_regardingrecordtype via @odata.bind to sprk_recordtype_ref
 *   - sprk_regardingrecordnumber (text) — 5th field, added in
 *     set-regarding-and-field-mapping-resolver-r1 per FR-A4-01 / FR-C1-01.
 *     Source-field name is resolved from
 *     `sprk_recordtype_ref.sprk_regardingrecordnumberfield` (or from the
 *     optional `options.sourceRecordNumberField` override) and the resolver
 *     queries the target record for that field's value. When metadata is
 *     null OR target record's field value is null/empty, the resolver logs
 *     a `console.warn` and skips the write per NFR-06 (graceful-blank).
 *
 * Backward compatibility (spec FR-C1-01 + SRFR-052): callers that do NOT
 * supply the new `options` argument still get the historical 4-field write
 * when the catalog metadata is null. The 5th field is written only when both
 * the source-field name AND the target record's value resolve to non-null.
 * The display-name field always writes SOMETHING (resolved value OR
 * `parentRecordName` fallback) — never left blank.
 *
 * @param webApi                    WebApi for querying sprk_recordtype_ref +
 *                                  target records
 * @param entity                    The entity payload to populate (mutated in
 *                                  place)
 * @param navProps                  Nav-props for the child entity (from
 *                                  discoverNavProps)
 * @param parentEntityLogicalName   e.g. "sprk_matter"
 * @param parentEntitySet           e.g. "sprk_matters"
 * @param parentRecordId            GUID of the parent record
 * @param parentRecordName          Display name of the parent record (picker
 *                                  returns the Primary Name; used as fallback
 *                                  for the resolved display-name write per
 *                                  SRFR-052)
 * @param entityLookupHint          Hint for finding the entity-specific
 *                                  nav-prop (e.g. "matter")
 * @param options                   Optional trailing options (introduced by
 *                                  FR-A4-01 for the record-number extension;
 *                                  extended by SRFR-052 for display-name
 *                                  override). See {@link IApplyResolverFieldsOptions}.
 * @returns                         {@link IApplyResolverFieldsResult} with the
 *                                  resolved record-number value (or null when
 *                                  graceful-blank) and the resolved display-name
 *                                  value (or fallback). Return type widened from
 *                                  `Promise<void>` — TypeScript back-compat.
 */
export async function applyResolverFields(
  webApi: IPolymorphicWebApi,
  entity: Record<string, unknown>,
  navProps: INavPropEntry[],
  parentEntityLogicalName: string,
  parentEntitySet: string,
  parentRecordId: string,
  parentRecordName: string,
  entityLookupHint?: string,
  options?: IApplyResolverFieldsOptions
): Promise<IApplyResolverFieldsResult> {
  // Dataverse `Xrm.Utility.lookupObjects` returns GUIDs wrapped in curly braces
  // (e.g. `{39CDE3E3-9D15-...}`). The OData `@odata.bind` URL syntax rejects
  // braced GUIDs with HTTP 400 "Error in query syntax". Normalize once here so
  // every downstream use sees a bare lowercase GUID.
  const cleanRecordId = cleanGuid(parentRecordId);

  // 1. Bind entity-specific regarding lookup
  const entityNavProp = findNavProp(navProps, parentEntityLogicalName, entityLookupHint);
  if (entityNavProp) {
    entity[`${entityNavProp}@odata.bind`] = `/${parentEntitySet}(${cleanRecordId})`;
  } else {
    console.warn(
      `[PolymorphicResolver] No nav-prop for ${parentEntityLogicalName} (hint: ${entityLookupHint}), skipping entity-specific lookup`
    );
  }

  // 2. Populate denormalized ID + URL fields (fields 2 + 4 of the 5-field
  //    write set). Field 3 (`sprk_regardingrecordname`) is written AFTER the
  //    target-record query below so the resolved display-name replaces the
  //    picker-provided fallback per SRFR-052.
  entity['sprk_regardingrecordid'] = cleanRecordId;
  entity['sprk_regardingrecordurl'] = buildRecordUrl(parentEntityLogicalName, cleanRecordId);

  // 3. Bind sprk_regardingrecordtype lookup to sprk_recordtype_ref
  const recordType = await resolveRecordType(webApi, parentEntityLogicalName);
  if (recordType) {
    const rtNavProp = findNavProp(navProps, 'sprk_recordtype_ref', 'regardingrecordtype');
    if (rtNavProp) {
      entity[`${rtNavProp}@odata.bind`] = `/sprk_recordtype_refs(${recordType.id})`;
    } else {
      console.warn('[PolymorphicResolver] No nav-prop for sprk_recordtype_ref (regardingrecordtype)');
    }
  } else {
    console.warn(`[PolymorphicResolver] Record type ref not found for ${parentEntityLogicalName}`);
  }

  // 4. Resolve source-field NAMES for both record-number (FR-A4-01) and
  //    display-name (SRFR-052). Explicit overrides in `options` take precedence
  //    over catalog lookup. Catalog reads are cached per entity per page.
  const explicitNumberOverride =
    typeof options?.sourceRecordNumberField === 'string' && options.sourceRecordNumberField.trim().length > 0
      ? options.sourceRecordNumberField.trim()
      : null;
  const explicitDisplayNameOverride =
    typeof options?.sourceDisplayNameField === 'string' && options.sourceDisplayNameField.trim().length > 0
      ? options.sourceDisplayNameField.trim()
      : null;

  const numberSourceField =
    explicitNumberOverride ?? (await resolveRecordNumberFieldName(webApi, parentEntityLogicalName));
  const displayNameSourceField =
    explicitDisplayNameOverride ?? (await resolveRecordDisplayNameFieldName(webApi, parentEntityLogicalName));

  // 5. Query the target record for BOTH resolved source fields at once. Use
  //    `retrieveMultipleRecords` with `$filter` on the entity's primary-id
  //    attribute + a `$select` containing whichever resolved fields are
  //    non-null. Single round-trip per SRFR-052 design; skipped entirely if
  //    both resolved fields are null.
  //
  // NFR-06 graceful-blank: any null in either step → warn + skip that specific
  // write; never throw from a missing optional mapping.
  let recordNumberValue: string | null = null;
  let resolvedDisplayNameValue: string | null = null;
  const selectFields: string[] = [];
  if (numberSourceField) selectFields.push(numberSourceField);
  if (displayNameSourceField) selectFields.push(displayNameSourceField);

  if (selectFields.length > 0) {
    try {
      const primaryIdAttr = `${parentEntityLogicalName}id`;
      const query = `?$filter=${primaryIdAttr} eq ${cleanRecordId}` + `&$select=${selectFields.join(',')}` + `&$top=1`;
      const result = await webApi.retrieveMultipleRecords(parentEntityLogicalName, query, 1);

      if (result.entities && result.entities.length > 0) {
        const row = result.entities[0];

        if (numberSourceField) {
          const rawNumber = row[numberSourceField];
          if (typeof rawNumber === 'string' && rawNumber.trim().length > 0) {
            recordNumberValue = rawNumber.trim();
          } else if (typeof rawNumber === 'number') {
            recordNumberValue = String(rawNumber);
          }
        }

        if (displayNameSourceField) {
          const rawName = row[displayNameSourceField];
          if (typeof rawName === 'string' && rawName.trim().length > 0) {
            resolvedDisplayNameValue = rawName.trim();
          } else if (typeof rawName === 'number') {
            resolvedDisplayNameValue = String(rawName);
          }
        }
      }
    } catch (err) {
      // Query failure → warn + graceful degradation. Do NOT throw; NFR-06
      // graceful-blank takes precedence over strict error propagation. Both
      // record-number and display-name will fall through to their skip/fallback
      // paths below.
      console.warn(
        `[PolymorphicResolver] Failed to read [${selectFields.join(',')}] from ${parentEntityLogicalName}(${cleanRecordId}):`,
        err
      );
    }
  }

  // 6. Resolve final display-name value (SRFR-052).
  //    Priority: resolved-from-catalog > parentRecordName fallback.
  //    Never left blank — the write always happens.
  const finalDisplayName =
    resolvedDisplayNameValue !== null && resolvedDisplayNameValue.length > 0
      ? resolvedDisplayNameValue
      : parentRecordName;
  entity['sprk_regardingrecordname'] = finalDisplayName;

  // Log a warn when we fell back (helps operators diagnose stale catalog rows).
  if (resolvedDisplayNameValue === null) {
    if (!displayNameSourceField) {
      console.warn(
        `[PolymorphicResolver] No sprk_recorddisplaynamefield mapping for "${parentEntityLogicalName}"; falling back to picker-provided name for sprk_regardingrecordname (NFR-06).`
      );
    } else {
      console.warn(
        `[PolymorphicResolver] Target ${parentEntityLogicalName}(${cleanRecordId}) has null/empty "${displayNameSourceField}"; falling back to picker-provided name for sprk_regardingrecordname (NFR-06).`
      );
    }
  }

  // 7. Populate sprk_regardingrecordnumber (5th field — FR-A4-01 / FR-C1-01).
  //    Unlike display-name, the record-number is SKIPPED (not written) on
  //    null — the field intentionally stays blank per Q-06 owner clarification.
  if (!numberSourceField) {
    console.warn(
      `[PolymorphicResolver] No sprk_regardingrecordnumberfield mapping for "${parentEntityLogicalName}"; skipping sprk_regardingrecordnumber write (NFR-06).`
    );
    return {
      recordNumber: null,
      recordNumberSourceField: null,
      displayName: finalDisplayName,
    };
  }

  if (recordNumberValue === null) {
    console.warn(
      `[PolymorphicResolver] Target ${parentEntityLogicalName}(${cleanRecordId}) has null/empty "${numberSourceField}"; skipping sprk_regardingrecordnumber write (NFR-06).`
    );
    return {
      recordNumber: null,
      recordNumberSourceField: numberSourceField,
      displayName: finalDisplayName,
    };
  }

  entity['sprk_regardingrecordnumber'] = recordNumberValue;
  return {
    recordNumber: recordNumberValue,
    recordNumberSourceField: numberSourceField,
    displayName: finalDisplayName,
  };
}

// ===========================================================================
// FR-26 — Core-ancestor derivation (unified-access-control-r2, task 050)
// ===========================================================================
//
// WHY THIS EXISTS
// ---------------
// The access model splits records into two classes (spec.md FR-26 /
// design.md §4.3):
//
//   CORE  — sprk_project, sprk_matter, sprk_workassignment, sprk_servicerequest
//           Access requires a DIRECT grant.
//   CHILD — sprk_invoice, sprk_communication, sprk_document, sprk_event,
//           sprk_todo, sprk_analysis
//           Access is INHERITED from the child's core ancestor.
//
// The evaluator's child-inheritance term is a set-membership test of the shape
// `child.sprk_regarding{core} ∈ {accessible core ids}`. That test can only read
// a lookup the child ROW already carries — it cannot walk a chain. So a
// todo → communication → matter chain is inexpressible unless the ULTIMATE
// core ancestor is denormalized onto the todo at write time
// (notes/investigation/06-adversarial-critique.md §F1 proved this).
//
// Denormalizing the ancestor is what keeps every chain ONE hop, which is why
// ADR-034's 1-hop cap holds with no exception. This module is where that stamp
// is derived, on the ONE shared client write path (ADR-024: no consumer may
// reimplement resolver field-write logic).
//
// TWO RULES THAT ARE EASY TO GET BACKWARDS
// ----------------------------------------
//  1. Matter does NOT inherit from Project. Both are CORE. Selecting a core
//     target stamps ONLY that target — never its own parent associations.
//     Inverting this silently grants every Project-holder access to every
//     Matter under it.
//  2. Derivation reads the target's OWN core-ancestor lookups and stops
//     (ADR-034 1-hop). It never recurses. Those lookups are themselves FR-26
//     stamps, which is what makes one read sufficient.

/**
 * CORE record entities — direct grants required; these never inherit.
 *
 * Pinned literally by a unit test. Changing this set changes who can see what,
 * so it must be a deliberate, reviewed edit — not a drive-by.
 *
 * @see projects/unified-access-control-r2/spec.md FR-26
 * @see projects/unified-access-control-r2/design.md §4.3
 */
export const CORE_RECORD_ENTITIES: ReadonlyArray<string> = [
  'sprk_project',
  'sprk_matter',
  'sprk_workassignment',
  'sprk_servicerequest',
] as const;

/**
 * CHILD record entities — inherit their core ancestor's rights (1 hop, via the
 * stamp this module writes).
 *
 * Pinned literally by a unit test alongside {@link CORE_RECORD_ENTITIES}.
 *
 * NOTE: entities in NEITHER set (e.g. `sprk_budget`, `sprk_organization`,
 * `contact`, `sprk_reportcard`) are intentionally unclassified for FR-26.
 * They confer access through other evaluator terms (org-expansion, explicit
 * grant) — not through core-ancestor inheritance — so no stamp is derived for
 * them. That is a distinct, non-error state; see
 * {@link CoreAncestorDerivationStatus}.
 */
export const CHILD_RECORD_ENTITIES: ReadonlyArray<string> = [
  'sprk_invoice',
  'sprk_communication',
  'sprk_document',
  'sprk_event',
  'sprk_todo',
  'sprk_analysis',
] as const;

/** True when `entityLogicalName` is a CORE record (direct grants required). */
export function isCoreRecordEntity(entityLogicalName: string): boolean {
  return CORE_RECORD_ENTITIES.includes(entityLogicalName?.toLowerCase?.() ?? entityLogicalName);
}

/** True when `entityLogicalName` is a CHILD record (inherits via core ancestor). */
export function isChildRecordEntity(entityLogicalName: string): boolean {
  return CHILD_RECORD_ENTITIES.includes(entityLogicalName?.toLowerCase?.() ?? entityLogicalName);
}

/**
 * The core-ancestor lookup column that carries each CORE entity's stamp on a
 * child row, plus the OData entity-set needed to build the `@odata.bind` value.
 *
 * These four columns are the ONLY access-conferring ancestor lookups. Any other
 * `sprk_regarding*` column on a child (e.g. `sprk_regardingcommunication`) is a
 * relationship, not an access edge.
 *
 * ⚠️ Not every child entity carries all four. `sprk_todo`, for example, has no
 * `sprk_regardingservicerequest` column (verified: 11 regarding lookups, none
 * for service request — notes/investigation/06-adversarial-critique.md §F1).
 * Presence is therefore always resolved against the entity's DISCOVERED
 * nav-props rather than assumed; a `$select` of a non-existent column would
 * otherwise 400 and fail an otherwise-valid write.
 */
export const CORE_ANCESTOR_LOOKUPS: ReadonlyArray<{
  entityType: string;
  entitySet: string;
  lookupAttribute: string;
}> = [
  { entityType: 'sprk_project', entitySet: 'sprk_projects', lookupAttribute: 'sprk_regardingproject' },
  { entityType: 'sprk_matter', entitySet: 'sprk_matters', lookupAttribute: 'sprk_regardingmatter' },
  {
    entityType: 'sprk_workassignment',
    entitySet: 'sprk_workassignments',
    lookupAttribute: 'sprk_regardingworkassignment',
  },
  {
    entityType: 'sprk_servicerequest',
    entitySet: 'sprk_servicerequests',
    lookupAttribute: 'sprk_regardingservicerequest',
  },
] as const;

/** One resolved core-ancestor stamp to write onto the child being saved. */
export interface ICoreAncestorStamp {
  /** Core entity logical name (e.g. `sprk_matter`). */
  entityType: string;
  /** OData entity set for the `@odata.bind` value (e.g. `sprk_matters`). */
  entitySet: string;
  /** Lookup column on the child that carries this stamp. */
  lookupAttribute: string;
  /** Bare lowercase GUID of the core record. */
  recordId: string;
}

/**
 * Outcome of {@link deriveCoreAncestorStamps}. The status is deliberately a
 * closed set of DISTINCT states rather than "stamps.length === 0", because
 * "the target has no ancestor" and "we could not find out" must never collapse
 * into the same branch (NFR-01 fail-closed).
 *
 * - `core-target`   — target is itself CORE. The stamp is the target. Its own
 *                     parent associations are NOT ancestors (Matter ≠ child of
 *                     Project).
 * - `derived`       — target is CHILD and carried ≥1 core-ancestor stamp.
 * - `no-ancestor`   — target is CHILD and every core-ancestor lookup is null.
 *                     A real, legitimate state (an orphan communication); the
 *                     child simply inherits nothing.
 * - `unclassified`  — target is neither CORE nor CHILD (budget, organization,
 *                     contact, report card). No ancestor concept applies.
 * - `error`         — the ancestor read failed. The caller MUST NOT write.
 */
export type CoreAncestorDerivationStatus = 'core-target' | 'derived' | 'no-ancestor' | 'unclassified' | 'error';

/** Result of {@link deriveCoreAncestorStamps}. */
export interface ICoreAncestorDerivationResult {
  status: CoreAncestorDerivationStatus;
  /** Stamps to write. Empty for `no-ancestor` / `unclassified` / `error`. */
  stamps: ICoreAncestorStamp[];
  /** Populated only when `status === 'error'`. */
  error?: string;
}

/**
 * Derive the ultimate CORE-record ancestor stamp(s) for a selected regarding
 * target — the FR-26 write-time step that keeps every access chain one hop.
 *
 * Behaviour by target class:
 *
 * | Target class | Read performed | Result |
 * |---|---|---|
 * | CORE  | none | `core-target`, stamp = the target itself |
 * | CHILD | ONE `$select` of the target's own core-ancestor lookups | `derived` / `no-ancestor` |
 * | other | none | `unclassified` |
 *
 * **1-hop, enforced structurally (ADR-034).** The read selects the target's own
 * `sprk_regarding{core}` columns and stops. There is no recursion and no
 * grandparent walk — those columns are themselves FR-26 stamps written by this
 * same function when the target was saved, which is exactly why one read is
 * enough.
 *
 * **Fail-closed (NFR-01).** A read failure returns `status: 'error'`. Callers
 * MUST abort the write rather than saving a child that silently carries no
 * inherited access. Note the asymmetry with the resolver's other optional
 * fields (record-number / display-name), which degrade gracefully per NFR-06:
 * those are cosmetic, this one is an access edge.
 *
 * @param webApi        WebApi shim used to read the target row.
 * @param targetEntityLogicalName  Logical name of the selected regarding target.
 * @param targetRecordId           GUID of the target (braced or bare).
 * @param fetchImpl     Fetch implementation for nav-prop discovery (test seam).
 */
export async function deriveCoreAncestorStamps(
  webApi: IPolymorphicWebApi,
  targetEntityLogicalName: string,
  targetRecordId: string,
  fetchImpl: typeof fetch = globalThis.fetch
): Promise<ICoreAncestorDerivationResult> {
  const targetEntity = (targetEntityLogicalName ?? '').toLowerCase();
  const cleanTargetId = cleanGuid(targetRecordId);

  // --- CORE target: the target IS the ancestor. Do not look at its own
  //     parents — a Matter associated to a Project does not inherit from it
  //     (design.md §4.3). One stamp, no read.
  if (isCoreRecordEntity(targetEntity)) {
    const core = CORE_ANCESTOR_LOOKUPS.find(c => c.entityType === targetEntity);
    if (!core) {
      // Unreachable while CORE_RECORD_ENTITIES and CORE_ANCESTOR_LOOKUPS agree;
      // the taxonomy parity test pins that. Fail closed rather than guess.
      return {
        status: 'error',
        stamps: [],
        error:
          `[PolymorphicResolver] "${targetEntity}" is a CORE entity with no entry in CORE_ANCESTOR_LOOKUPS. ` +
          `The taxonomy and the lookup table have diverged.`,
      };
    }
    return {
      status: 'core-target',
      stamps: [
        {
          entityType: core.entityType,
          entitySet: core.entitySet,
          lookupAttribute: core.lookupAttribute,
          recordId: cleanTargetId,
        },
      ],
    };
  }

  // --- Neither core nor child: no ancestor concept. Not an error.
  if (!isChildRecordEntity(targetEntity)) {
    return { status: 'unclassified', stamps: [] };
  }

  // --- CHILD target: read ITS core-ancestor stamps (the single hop).
  //
  // Only select columns that actually exist on the target entity — selecting a
  // missing lookup returns HTTP 400 and would turn a schema gap into a blocked
  // save. Nav-props are the presence oracle and are cached per entity.
  const targetNavProps = await discoverNavProps(targetEntity, fetchImpl);
  const presentColumns = new Set(targetNavProps.map(n => n.columnName.toLowerCase()));
  const applicable = CORE_ANCESTOR_LOOKUPS.filter(c => presentColumns.has(c.lookupAttribute.toLowerCase()));

  if (targetNavProps.length === 0) {
    // Discovery failed (HTTP error / network). We cannot distinguish "target
    // has no core-ancestor columns" from "we could not read the metadata", and
    // guessing the optimistic branch would write an unstamped child. Fail closed.
    return {
      status: 'error',
      stamps: [],
      error:
        `[PolymorphicResolver] Could not discover nav-props for child target "${targetEntity}"; ` +
        `core-ancestor derivation cannot be verified (FR-26 / NFR-01 fail-closed).`,
    };
  }

  if (applicable.length === 0) {
    // The target is child-class but carries no core-ancestor lookup at all.
    // Its own chain is already broken upstream; nothing to inherit.
    console.warn(
      `[PolymorphicResolver] Child target "${targetEntity}" has none of the core-ancestor lookups ` +
        `(${CORE_ANCESTOR_LOOKUPS.map(c => c.lookupAttribute).join(', ')}); no ancestor stamp can be derived.`
    );
    return { status: 'no-ancestor', stamps: [] };
  }

  const selectFields = applicable.map(c => `_${c.lookupAttribute}_value`);
  let row: Record<string, unknown> | undefined;
  try {
    const primaryIdAttr = `${targetEntity}id`;
    const query = `?$filter=${primaryIdAttr} eq ${cleanTargetId}&$select=${selectFields.join(',')}&$top=1`;
    const result = await webApi.retrieveMultipleRecords(targetEntity, query, 1);
    row = result.entities?.[0];
  } catch (err) {
    return {
      status: 'error',
      stamps: [],
      error:
        `[PolymorphicResolver] Failed to read core-ancestor lookups from ${targetEntity}(${cleanTargetId}): ` +
        `${err instanceof Error ? err.message : String(err)}`,
    };
  }

  if (!row) {
    // The target row is unreadable by this caller (or gone). Writing an
    // unstamped child here would silently under-grant, so fail closed.
    return {
      status: 'error',
      stamps: [],
      error: `[PolymorphicResolver] Core-ancestor read returned no row for ${targetEntity}(${cleanTargetId}).`,
    };
  }

  const stamps: ICoreAncestorStamp[] = [];
  for (const c of applicable) {
    const raw = row[`_${c.lookupAttribute}_value`];
    if (typeof raw === 'string' && raw.trim().length > 0) {
      stamps.push({
        entityType: c.entityType,
        entitySet: c.entitySet,
        lookupAttribute: c.lookupAttribute,
        recordId: cleanGuid(raw),
      });
    }
  }

  if (stamps.length === 0) {
    console.warn(
      `[PolymorphicResolver] Child target ${targetEntity}(${cleanTargetId}) carries no core-ancestor stamp; ` +
        `the record being written will inherit no access (FR-26 no-ancestor).`
    );
    return { status: 'no-ancestor', stamps: [] };
  }

  return { status: 'derived', stamps };
}

// ---------------------------------------------------------------------------
// Combined, ordering-safe payload assembly (FR-26)
// ---------------------------------------------------------------------------

/** A regarding target as the catalogs describe it. */
export interface IRegardingTargetDescriptor {
  entityType: string;
  entitySet: string;
  lookupAttribute: string;
  navPropHint: string;
}

/** Result of {@link buildRegardingSelectionPayload}. */
export interface IRegardingSelectionPayloadResult {
  /** False when derivation failed — the caller MUST NOT write (NFR-01). */
  success: boolean;
  /** The assembled payload. Undefined when `success` is false. */
  payload?: Record<string, unknown>;
  /** The derivation outcome that produced (or blocked) the ancestor stamps. */
  ancestor: ICoreAncestorDerivationResult;
  /** Pass-through from {@link applyResolverFields}. */
  resolverResult?: IApplyResolverFieldsResult;
  /**
   * Core-ancestor stamps that were derived but could NOT be written because the
   * host entity has no matching lookup column (e.g. a `sprk_todo` regarding a
   * communication whose ancestor is a Service Request — `sprk_todo` has no
   * `sprk_regardingservicerequest`). Surfaced rather than swallowed: each entry
   * is a real hole in child inheritance for that host/ancestor pair and is a
   * schema finding, not a runtime condition to paper over.
   */
  unstampable: string[];
  /** Populated when `success` is false. */
  error?: string;
}

/**
 * Build the COMPLETE regarding-selection payload for a child record, in the one
 * order that is correct — the reason this is exported instead of leaving
 * consumers to assemble it.
 *
 * Sequence (each step's position is load-bearing):
 *
 *  1. **Derive the core ancestor FIRST.** If derivation fails the function
 *     returns before any payload exists, so a failed derivation cannot become a
 *     partially-built write (NFR-01).
 *  2. **Pre-clear** every other regarding lookup that exists on the host —
 *     the FR-13 mutual-exclusivity contract — INCLUDING the four core-ancestor
 *     lookups even when the host catalog does not list them. Without that
 *     union, a reparent leaves the previous ancestor stamp behind and the child
 *     stays visible to the OLD parent's principals.
 *  3. **`applyResolverFields`** sets the chosen lookup + the 5 resolver fields.
 *  4. **Apply the ancestor stamps LAST**, so a stamp this payload is setting can
 *     never be nulled by the pre-clear in step 2. (Step 2 before step 4 is the
 *     whole point of centralizing this.)
 *
 * @param webApi        WebApi shim.
 * @param hostNavProps  Nav-props DISCOVERED for the host (child) entity. Doubles
 *                      as the presence oracle for which lookups may be written.
 * @param catalog       The host's allowed regarding targets (pre-clear set).
 * @param target        The selected target descriptor.
 * @param recordId      Selected target GUID (braced or bare).
 * @param recordName    Selected target display name (fallback for the 3rd field).
 * @param options       Passed through to {@link applyResolverFields}.
 * @param fetchImpl     Fetch implementation for nav-prop discovery (test seam).
 */
export async function buildRegardingSelectionPayload(
  webApi: IPolymorphicWebApi,
  hostNavProps: INavPropEntry[],
  catalog: ReadonlyArray<IRegardingTargetDescriptor>,
  target: IRegardingTargetDescriptor,
  recordId: string,
  recordName: string,
  options?: IApplyResolverFieldsOptions,
  fetchImpl: typeof fetch = globalThis.fetch
): Promise<IRegardingSelectionPayloadResult> {
  // --- 1. Derivation first. A failure here aborts before any payload exists.
  const ancestor = await deriveCoreAncestorStamps(webApi, target.entityType, recordId, fetchImpl);
  if (ancestor.status === 'error') {
    return {
      success: false,
      ancestor,
      unstampable: [],
      error: ancestor.error ?? 'Core-ancestor derivation failed.',
    };
  }

  const payload: Record<string, unknown> = {};

  // --- 2. Pre-clear. The clear set is the UNION of the host catalog and the
  //        four core-ancestor lookups, intersected with what the host actually
  //        has. The union matters: a host whose catalog omits a core lookup
  //        (sprk_todo has no service-request entry) would otherwise never clear
  //        a stale stamp on reparent.
  const clearTargets: Array<{ entityType: string; lookupAttribute: string; navPropHint?: string }> = [
    ...catalog.map(c => ({ entityType: c.entityType, lookupAttribute: c.lookupAttribute, navPropHint: c.navPropHint })),
    ...CORE_ANCESTOR_LOOKUPS.map(c => ({ entityType: c.entityType, lookupAttribute: c.lookupAttribute })),
  ];
  const seenClear = new Set<string>();
  for (const other of clearTargets) {
    if (other.entityType === target.entityType) continue;
    if (seenClear.has(other.lookupAttribute.toLowerCase())) continue;
    seenClear.add(other.lookupAttribute.toLowerCase());
    const navProp = findHostNavPropForLookup(hostNavProps, other.entityType, other.lookupAttribute, other.navPropHint);
    if (!navProp) continue; // Column absent on this host — writing it would 400.
    payload[`${navProp}@odata.bind`] = null;
  }

  // --- 3. The canonical SET path (ADR-024). Never reimplemented.
  const resolverResult = await applyResolverFields(
    webApi,
    payload,
    hostNavProps,
    target.entityType,
    target.entitySet,
    recordId,
    recordName,
    target.navPropHint,
    options
  );

  // --- 4. Ancestor stamps LAST so they survive step 2's nulls.
  const unstampable: string[] = [];
  for (const stamp of ancestor.stamps) {
    if (stamp.entityType === target.entityType) continue; // Already bound by step 3.
    const navProp = findHostNavPropForLookup(hostNavProps, stamp.entityType, stamp.lookupAttribute);
    if (!navProp) {
      unstampable.push(stamp.lookupAttribute);
      console.warn(
        `[PolymorphicResolver] Derived core ancestor ${stamp.entityType}(${stamp.recordId}) cannot be stamped: ` +
          `host entity has no "${stamp.lookupAttribute}" lookup. This child will NOT inherit that ancestor's access (FR-26 gap).`
      );
      continue;
    }
    payload[`${navProp}@odata.bind`] = `/${stamp.entitySet}(${stamp.recordId})`;
  }

  return { success: true, payload, ancestor, resolverResult, unstampable };
}

/**
 * Resolve the host nav-prop name for a regarding lookup column, or `undefined`
 * when the host has no such column.
 *
 * Matches on the DISCOVERED column name first (exact, unambiguous) and falls
 * back to the historical `referencedEntity` + hint heuristic so existing
 * catalog-driven callers keep their current resolution behaviour.
 *
 * @internal — exported for the ancestor-derivation tests.
 */
export function findHostNavPropForLookup(
  hostNavProps: INavPropEntry[],
  referencedEntity: string,
  lookupAttribute: string,
  navPropHint?: string
): string | undefined {
  const byColumn = hostNavProps.find(n => n.columnName.toLowerCase() === lookupAttribute.toLowerCase());
  if (byColumn) return byColumn.navPropName;

  const hint = navPropHint?.toLowerCase();
  const byEntity = hostNavProps.find(
    n => n.referencedEntity === referencedEntity && (!hint || n.columnName.toLowerCase().includes(hint))
  );
  return byEntity?.navPropName;
}
