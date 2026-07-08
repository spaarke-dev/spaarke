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
// Record URL builder
// ---------------------------------------------------------------------------

/**
 * Build a Dataverse record URL for the sprk_regardingrecordurl field.
 * Tries to resolve clientUrl and appId from the Xrm context; falls back
 * to a relative URL.
 */
export function buildRecordUrl(entityLogicalName: string, recordId: string): string {
  const cleanId = recordId.replace(/[{}]/g, '').toLowerCase();

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
  const cleanRecordId = parentRecordId.replace(/[{}]/g, '').toLowerCase();

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
