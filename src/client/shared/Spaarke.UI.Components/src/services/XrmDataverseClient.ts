/**
 * XrmDataverseClient — MDA-host implementation of {@link IDataverseClient}.
 *
 * Wraps `Xrm.WebApi` + `Xrm.Utility.getEntityMetadata`. Auto-walks `window` then
 * `window.parent` for the Xrm object so it works in Custom Page iframe contexts
 * (where `window.Xrm` is undefined but `window.parent.Xrm` is the MDA Xrm).
 *
 * Outside MDA (Storybook, Code Pages, Office Add-ins, plain SPA) callers MUST
 * use `BffDataverseClient` instead — this class throws a clear error if Xrm is
 * unavailable so devs see the problem immediately.
 *
 * **Spec**: projects/spaarke-datagrid-framework-r1/design.md §6.2 (FR-DG-02)
 * **ADRs**: ADR-012 (shared-components home), ADR-022 (React-16-safe)
 *
 * @see IDataverseClient — the 5-method contract this class satisfies
 * @see BffDataverseClient — sibling implementation for non-MDA hosts (task 015)
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

import type {
  IDataverseClient,
  SavedQueryResult,
  SavedQuerySummary,
  EntityMetadata,
  EntityAttributeMetadata,
  MetadataAttributeType,
  OptionSetOption,
  FetchMultipleResult,
} from './IDataverseClient';

/**
 * Minimal shape of `Xrm.WebApi` we need. Kept local rather than importing the
 * `XrmContext` types from `utils/xrmContext` to keep this client narrow and
 * future-proof against the larger XrmContext surface evolving.
 */
interface XrmWebApiLike {
  retrieveRecord(entityLogicalName: string, id: string, options?: string): Promise<any>;
  retrieveMultipleRecords(
    entityLogicalName: string,
    options?: string,
    maxPageSize?: number
  ): Promise<{
    entities: Array<Record<string, any>>;
    '@Microsoft.Dynamics.CRM.morerecords'?: boolean;
    '@Microsoft.Dynamics.CRM.fetchxmlpagingcookie'?: string;
    '@odata.nextLink'?: string;
  }>;
}

interface XrmGlobalContextLike {
  getClientUrl(): string;
}

interface XrmUtilityLike {
  /**
   * `Xrm.Utility.getEntityMetadata` returns a Promise of an EntityMetadata object
   * whose shape mirrors the Web API. We project to our framework's narrower shape.
   */
  getEntityMetadata(entityName: string, attributes?: string[]): Promise<any>;
  /**
   * Returns the global Xrm context (used to derive the MDA base URL for direct
   * EntityDefinitions Web API calls when fetching attribute DisplayName labels).
   */
  getGlobalContext?: () => XrmGlobalContextLike;
}

interface XrmLike {
  WebApi: XrmWebApiLike;
  Utility?: XrmUtilityLike;
}

const XRM_MISSING_MESSAGE = 'XrmDataverseClient requires Xrm context. Use BffDataverseClient outside MDA.';

/**
 * Resolve the Xrm object from `window` or `window.parent` (Custom Page iframe case).
 *
 * Throws with a clear, actionable error if Xrm is unavailable so devs in Storybook
 * or other non-MDA contexts see the problem at construction / first call instead
 * of getting a cryptic "WebApi of undefined" further down.
 *
 * @internal
 */
function resolveXrm(): XrmLike {
  // Try window.Xrm first (model-driven app top frame).
  try {
    const windowXrm = (window as any).Xrm;
    if (windowXrm?.WebApi) {
      return windowXrm as XrmLike;
    }
  } catch {
    // Defensive — if window itself is undefined (SSR), fall through.
  }

  // Try window.parent.Xrm (Custom Page in dialog/iframe — parent has Xrm, we don't).
  try {
    if (typeof window !== 'undefined' && window.parent && window.parent !== window) {
      const parentXrm = (window.parent as any).Xrm;
      if (parentXrm?.WebApi) {
        return parentXrm as XrmLike;
      }
    }
  } catch {
    // Cross-origin access denied — expected in some iframe configurations.
  }

  throw new Error(XRM_MISSING_MESSAGE);
}

/** Web API `$select` field list for the savedquery passthrough. */
const SAVEDQUERY_SINGLE_SELECT = '?$select=name,fetchxml,layoutxml,returnedtypecode';

/**
 * Filter for the active system views of a given entity:
 *  - `statecode eq 0` = active
 *  - `querytype eq 0` = main view (not lookup/quick-find/etc.)
 */
function buildSavedQueriesForEntityOptions(entityName: string): string {
  return (
    `?$filter=statecode eq 0 and querytype eq 0 and returnedtypecode eq '${entityName}'` +
    `&$select=savedqueryid,name,isdefault,querytype`
  );
}

/**
 * `AttributeTypeCode` → `MetadataAttributeType` name.
 *
 * **This table is load-bearing.** `Xrm.Utility.getEntityMetadata` is the CLIENT
 * API, and it returns each attribute's `AttributeType` as a **Number** (the
 * `AttributeTypeCode` enum) — NOT the PascalCase string the Web API's
 * `EntityDefinitions` endpoint returns. Microsoft documents this explicitly:
 *
 *   > `AttributeType` | **Number** | Type of a column. For a list of column
 *   > type values, see AttributeTypeCode Enum
 *   — learn.microsoft.com/.../xrm-utility/getentitymetadata, "Attribute objects"
 *
 * Before this table existed, `normalizeAttributeType` returned `'String'` for
 * every non-string input, so EVERY attribute of EVERY entity projected as
 * `String`. Downstream that made every RecordHeader cell derive the `text`
 * renderer, which made lookups get `$select`ed by their BARE logical name
 * (instead of `_<name>_value`), which 400s the whole request. See the
 * `record-header-and-notepad-r2` UAT defect note.
 *
 * Values mirror `XrmEnum.AttributeTypeCode` in `@types/xrm`.
 */
const ATTRIBUTE_TYPE_CODE_TO_NAME: Readonly<Record<number, MetadataAttributeType>> = {
  0: 'Boolean',
  1: 'Customer',
  2: 'DateTime',
  3: 'Decimal',
  4: 'Double',
  5: 'Integer',
  6: 'Lookup',
  7: 'Memo',
  8: 'Money',
  9: 'Owner',
  10: 'PartyList',
  11: 'Picklist',
  12: 'State',
  13: 'Status',
  14: 'String',
  15: 'Uniqueidentifier',
  16: 'CalendarRules',
  17: 'Virtual',
  18: 'BigInt',
  19: 'ManagedProperty',
  20: 'EntityName',
};

/**
 * Normalize an AttributeType from EITHER metadata surface to our framework's
 * `MetadataAttributeType` discriminator.
 *
 * Two shapes must both work, because the two Dataverse metadata surfaces
 * disagree:
 *  - **Client API** (`Xrm.Utility.getEntityMetadata`) → `Number`
 *    (`AttributeTypeCode`), e.g. `6` for Lookup.
 *  - **Web API** (`EntityDefinitions/Attributes`) → PascalCase `String`,
 *    e.g. `"Lookup"`.
 *
 * A string passes through as-is (the type is `string`-open). A number maps
 * through {@link ATTRIBUTE_TYPE_CODE_TO_NAME}. Anything else — including a
 * numeric code outside the enum — yields `undefined`, meaning "type unknown".
 * Callers MUST treat `undefined` as unknown rather than silently substituting
 * `String`: guessing `String` for an unknown attribute is exactly what produced
 * the bare-lookup `$select` defect.
 */
function normalizeAttributeType(attributeType: unknown): MetadataAttributeType | undefined {
  if (typeof attributeType === 'string' && attributeType.length > 0) {
    return attributeType as MetadataAttributeType;
  }
  if (typeof attributeType === 'number' && Number.isFinite(attributeType)) {
    return ATTRIBUTE_TYPE_CODE_TO_NAME[attributeType];
  }
  return undefined;
}

/**
 * Sentinel `attributeType` for an attribute whose type could NOT be determined
 * from the metadata payload.
 *
 * Deliberately NOT `'String'`. Every renderer/chip switch already falls through
 * to its text/no-chip default for an unrecognized discriminator, so this is
 * behaviorally identical to the old `'String'` fallback — but it is
 * *diagnostically honest*: `'String'` asserted a type we did not know, which is
 * how a Lookup attribute silently became a text cell and got `$select`ed by its
 * bare logical name.
 */
export const UNKNOWN_ATTRIBUTE_TYPE = 'Unknown';

/**
 * Project the option-set metadata to our {@link OptionSetOption} list.
 *
 * Three shapes must all work:
 *  - **Web API**: `{ Options: [{ Value, Label: { UserLocalizedLabel: { Label } } }] }`
 *  - **Client API (array)**: the `OptionSet` value is ITSELF the option array
 *    (this is what `@types/xrm` declares: `OptionSet: OptionMetadata[]`)
 *  - **Client API (map)**: a `value → label` key/value bag, which is how
 *    Microsoft documents it for `Xrm.Utility.getEntityMetadata`
 *    ("Options for the column where each option is a key:value pair")
 *
 * Returns `undefined` (never `[]`) when no options can be projected.
 */
function projectOptions(optionSet: any): OptionSetOption[] | undefined {
  if (optionSet === null || optionSet === undefined) {
    return undefined;
  }

  // Client-API "key:value pair" bag — `{ 1: 'Yes', 0: 'No' }`. Detected only
  // when the value is a plain object whose keys are all numeric and whose
  // values are all primitives (never objects, which would be the Web-API shape).
  const options: any[] | undefined = Array.isArray(optionSet) ? optionSet : (optionSet?.Options ?? optionSet?.options);

  if (!Array.isArray(options)) {
    if (typeof optionSet === 'object') {
      const entries = Object.entries(optionSet).filter(
        ([k, v]) => /^-?\d+$/.test(k) && (typeof v === 'string' || typeof v === 'number')
      );
      if (entries.length > 0) {
        return entries.map(([k, v]) => ({ value: Number(k), label: String(v) }));
      }
    }
    return undefined;
  }

  if (options.length === 0) {
    return undefined;
  }
  return options.map(opt => {
    // `Label` is a Label OBJECT on the Web API, but some client-API builds hand
    // back a plain localized string. Probe both before falling back to the value.
    const label =
      opt?.Label?.UserLocalizedLabel?.Label ??
      opt?.Label?.LocalizedLabels?.[0]?.Label ??
      (typeof opt?.Label === 'string' ? opt.Label : undefined) ??
      String(opt?.Value ?? '');
    return {
      value: Number(opt?.Value ?? 0),
      label,
      color: typeof opt?.Color === 'string' ? opt.Color : undefined,
    };
  });
}

/**
 * Project one attribute metadata entry to our {@link EntityAttributeMetadata}.
 *
 * Handles BOTH Dataverse metadata surfaces, which differ in every field that
 * matters:
 *
 * | Field         | Client API (`Xrm.Utility.getEntityMetadata`) | Web API (`EntityDefinitions`) |
 * |---------------|----------------------------------------------|-------------------------------|
 * | `AttributeType` | Number (`AttributeTypeCode`)               | String (`"Lookup"`)           |
 * | `DisplayName`   | String (`"Project Type"`)                  | `{ UserLocalizedLabel: { Label } }` |
 * | `OptionSet`     | array / key:value bag                      | `{ Options: [...] }`          |
 *
 * Parsing only the Web-API shapes (as this function originally did) silently
 * degraded every client-API attribute to type `String` with no label and no
 * options — the root cause of the RecordHeader v1.1.0 UAT defect.
 */
function projectAttribute(attr: any): EntityAttributeMetadata {
  const attributeType = normalizeAttributeType(attr?.AttributeType ?? attr?.attributeType) ?? UNKNOWN_ATTRIBUTE_TYPE;

  // Format is most relevant for String attributes; preserve when present.
  const format =
    typeof attr?.Format === 'string' ? attr.Format : typeof attr?.format === 'string' ? attr.format : undefined;

  // OptionSet location varies across Xrm versions: top-level OptionSet, GlobalOptionSet,
  // or nested under attribute. Probe in priority order.
  const optionSet =
    projectOptions(attr?.OptionSet) ?? projectOptions(attr?.GlobalOptionSet) ?? projectOptions(attr?.optionSet);

  // DisplayName: the Web API exposes `DisplayName.UserLocalizedLabel.Label`
  // (preferred) or the first `LocalizedLabels` entry. The CLIENT API returns a
  // PLAIN STRING — Microsoft documents it as `DisplayName | String | Display
  // name for the column`. Missing that string case is why every header label
  // fell back to a humanized logical name ("Openeddate", "Highpriority").
  const displayNameRaw = attr?.DisplayName ?? attr?.displayName;
  const displayName: string | undefined =
    displayNameRaw?.UserLocalizedLabel?.Label ??
    displayNameRaw?.LocalizedLabels?.[0]?.Label ??
    (typeof displayNameRaw === 'string' && displayNameRaw.length > 0 ? displayNameRaw : undefined);

  // Lookup target entity logical names. Xrm exposes this as `Targets` (PascalCase)
  // on the attribute metadata; probe the camelCase form too for resilience, mirroring
  // FieldUpdateReconcileTab.tsx:149. Guard with Array.isArray so a malformed/absent
  // value never surfaces as `[]` — the contract requires `undefined`, not empty array.
  const targetsRaw = attr?.Targets ?? attr?.targets;
  const targets = Array.isArray(targetsRaw) ? targetsRaw : undefined;

  return {
    attributeType,
    format,
    displayName,
    isPrimaryName: attr?.IsPrimaryName === true || attr?.isPrimaryName === true || undefined,
    isPrimaryId: attr?.IsPrimaryId === true || attr?.isPrimaryId === true || undefined,
    optionSet,
    targets,
  };
}

/**
 * Flatten the `Attributes` member of an `Xrm.Utility.getEntityMetadata` payload
 * into a plain array of attribute metadata objects.
 *
 * `@types/xrm` declares it as
 * `Collection.StringIndexableItemCollection<AttributeMetadata>`, i.e.
 * `Dictionary<T> & ItemCollection<T>` — so at runtime it can present as any of:
 *  - a plain array (Web-API-shaped payloads, and our own test doubles)
 *  - an Xrm collection exposing `getAll()` / `get()` / `forEach()`
 *  - a string-indexed bag keyed by logical name
 *
 * Every form is probed, in that order, and each probe is individually guarded:
 * an older client that throws from `get()` must degrade to "no attributes", not
 * take the whole metadata load down.
 *
 * The string-indexed fallback filters out FUNCTION members, because on a real
 * Xrm collection `Object.values()` would otherwise yield `get`/`getAll`/
 * `forEach`/`getLength` as if they were attributes.
 */
function flattenAttributeCollection(rawAttributes: any): any[] {
  if (!rawAttributes) return [];
  if (Array.isArray(rawAttributes)) return rawAttributes;

  if (typeof rawAttributes.getAll === 'function') {
    try {
      const all = rawAttributes.getAll();
      if (Array.isArray(all) && all.length > 0) return all;
    } catch {
      /* fall through to the next probe */
    }
  }

  if (typeof rawAttributes.get === 'function') {
    try {
      // Xrm's ItemCollection.get() with NO argument returns the entire array.
      const all = rawAttributes.get();
      if (Array.isArray(all) && all.length > 0) return all;
    } catch {
      /* fall through to the next probe */
    }
  }

  if (typeof rawAttributes.forEach === 'function') {
    try {
      const collected: any[] = [];
      rawAttributes.forEach((item: any) => collected.push(item));
      if (collected.length > 0) return collected;
    } catch {
      /* fall through to the next probe */
    }
  }

  if (typeof rawAttributes === 'object') {
    return Object.values(rawAttributes).filter(v => v !== null && typeof v === 'object');
  }

  return [];
}

/**
 * Project the full Xrm EntityMetadata payload to our {@link EntityMetadata} shape.
 */
function projectEntityMetadata(meta: any): EntityMetadata {
  const primaryIdAttribute: string = meta?.PrimaryIdAttribute ?? meta?.primaryIdAttribute ?? '';
  const primaryNameAttribute: string = meta?.PrimaryNameAttribute ?? meta?.primaryNameAttribute ?? '';

  const attributeArray = flattenAttributeCollection(meta?.Attributes ?? meta?.attributes);

  const attributes: Record<string, EntityAttributeMetadata> = {};
  for (const attr of attributeArray) {
    const logicalName: string | undefined = attr?.LogicalName ?? attr?.logicalName ?? attr?.Name ?? attr?.name;
    if (!logicalName) {
      continue;
    }
    attributes[logicalName] = projectAttribute(attr);
  }

  return {
    primaryIdAttribute,
    primaryNameAttribute,
    attributes,
  };
}

// ---------------------------------------------------------------------------
// Entity metadata cache (page-session lifetime) — FR-21 / NFR-01
// ---------------------------------------------------------------------------

/**
 * Normalize a caller-supplied attribute request into a stable, de-duplicated,
 * sorted list — or `undefined` when the caller wants the whole entity.
 *
 * Sorting makes the cache key order-insensitive, so two callers asking for the
 * same set in a different order share one round trip.
 */
function normalizeRequestedAttributes(attributes: readonly string[] | undefined): string[] | undefined {
  if (!Array.isArray(attributes)) return undefined;
  const cleaned = Array.from(
    new Set(attributes.filter((n): n is string => typeof n === 'string' && n.trim().length > 0).map(n => n.trim()))
  ).sort();
  return cleaned.length > 0 ? cleaned : undefined;
}

/**
 * Module-level cache of `retrieveEntityMetadata` results, keyed by entity
 * logical name PLUS the normalized requested-attribute set. Page-session
 * lifetime — mirrors `_navPropCache` in `PolymorphicResolverService.ts:451`.
 *
 * Caches the in-flight {@link Promise} (not just the resolved value) so
 * concurrent first callers for the same entity share one network round-trip
 * instead of racing duplicate `getEntityMetadata` + `EntityDefinitions`
 * fetches. A rejected promise is evicted immediately so a later retry issues
 * a fresh fetch — failures are never cached.
 */
const _entityMetadataCache: Record<string, Promise<EntityMetadata>> = {};

/**
 * Reset the entity metadata cache. Test-only.
 *
 * @param entityName  Optional — clear a single entity's entry; omit to clear all.
 * @internal
 */
export function _resetEntityMetadataCacheForTests(entityName?: string): void {
  if (entityName) {
    // Keys are `<entity>` or `<entity>::<sorted,attrs>` — clear every variant.
    for (const k of Object.keys(_entityMetadataCache)) {
      if (k === entityName || k.startsWith(`${entityName}::`)) {
        delete _entityMetadataCache[k];
      }
    }
    return;
  }
  for (const k of Object.keys(_entityMetadataCache)) {
    delete _entityMetadataCache[k];
  }
}

/**
 * MDA-host implementation of {@link IDataverseClient}.
 *
 * Constructed with no arguments — resolves Xrm lazily on each call so that the
 * "Xrm missing" error fires at call-time rather than at module-import time
 * (lets Storybook / tests instantiate without Xrm available, as long as they
 * don't actually call any methods).
 */
export class XrmDataverseClient implements IDataverseClient {
  /**
   * Cached reference to the resolved Xrm. We resolve lazily but cache the
   * reference so subsequent calls don't re-walk window/parent every time.
   */
  private xrmCache: XrmLike | undefined;

  /**
   * Get the Xrm context, resolving lazily on first access.
   *
   * @internal — exposed for testability (tests stub this when patching Xrm globals).
   */
  private getXrm(): XrmLike {
    if (this.xrmCache) {
      return this.xrmCache;
    }
    this.xrmCache = resolveXrm();
    return this.xrmCache;
  }

  /**
   * Retrieve a single savedquery record. Returns `entityName` (from
   * `returnedtypecode`), `fetchXml`, `layoutXml`, and display `name`.
   */
  async retrieveSavedQuery(savedQueryId: string): Promise<SavedQueryResult> {
    const xrm = this.getXrm();
    const result = await xrm.WebApi.retrieveRecord('savedquery', savedQueryId, SAVEDQUERY_SINGLE_SELECT);

    return {
      entityName: result?.returnedtypecode ?? '',
      fetchXml: result?.fetchxml ?? '',
      layoutXml: result?.layoutxml ?? '',
      name: result?.name ?? '',
    };
  }

  /**
   * Retrieve the active main views (`statecode=0, querytype=0`) for the given entity.
   */
  async retrieveSavedQueriesForEntity(entityName: string): Promise<SavedQuerySummary[]> {
    const xrm = this.getXrm();
    const result = await xrm.WebApi.retrieveMultipleRecords(
      'savedquery',
      buildSavedQueriesForEntityOptions(entityName)
    );

    return (result?.entities ?? []).map(row => ({
      id: String(row?.savedqueryid ?? ''),
      name: String(row?.name ?? ''),
      isDefault: row?.isdefault === true,
      queryType: typeof row?.querytype === 'number' ? row.querytype : 0,
    }));
  }

  /**
   * Retrieve projected entity metadata for `entityName`.
   *
   * Page-session cached (FR-21 / NFR-01): a second call for the same
   * `entityName` **and the same `attributes` request** returns the first
   * call's result (or, if still in flight, its in-flight {@link Promise}) with
   * zero additional network requests. A rejected fetch is evicted from the
   * cache so a later retry re-attempts.
   *
   * @param entityName Entity logical name.
   * @param attributes OPTIONAL explicit attribute logical names to request.
   *        Pass this whenever the caller already knows which attributes it
   *        needs — see {@link fetchEntityMetadataUncached} for why it matters.
   */
  async retrieveEntityMetadata(entityName: string, attributes?: string[]): Promise<EntityMetadata> {
    // The requested attribute set is part of the cache identity: a narrow
    // request must not satisfy a later broader one from cache.
    const requested = normalizeRequestedAttributes(attributes);
    const cacheKey = requested ? `${entityName}::${requested.join(',')}` : entityName;

    const cached = _entityMetadataCache[cacheKey];
    if (cached) {
      return cached;
    }

    const promise = this.fetchEntityMetadataUncached(entityName, requested);
    _entityMetadataCache[cacheKey] = promise;
    // Fire-and-forget eviction subscription: does not alter what
    // `retrieveEntityMetadata` returns to its caller (that's `promise`,
    // returned below), and attaching a `.catch()` here means the rejection
    // is always "handled" — no unhandledrejection noise from this subscription.
    promise.catch(() => {
      delete _entityMetadataCache[cacheKey];
    });
    return promise;
  }

  /**
   * Retrieve projected entity metadata via `Xrm.Utility.getEntityMetadata`.
   *
   * ══════════════════════════════════════════════════════════════════════════
   * WHY THE `attributes` ARGUMENT MATTERS (do not "simplify" it away)
   * ══════════════════════════════════════════════════════════════════════════
   * This method previously ALSO issued
   * `Xrm.WebApi.retrieveMultipleRecords('EntityDefinition', ...)` to pick up
   * attribute DisplayName labels. **That call can never succeed.**
   * `Xrm.WebApi` resolves its first argument to an entity SET name via the
   * client's entity catalog, and `entitydefinition` is not an entity — a live
   * query for `EntityDefinitions?$filter=LogicalName eq 'entitydefinition'`
   * returns an empty set. The repo already documents the same constraint in
   * `SemanticSearchControl/services/DataverseMetadataService.ts` ("Xrm.WebApi
   * doesn't support metadata entities"), and R2's own spec says
   * EntityDefinitions is "unreachable by `Xrm.WebApi`". The call therefore
   * threw on every invocation and its `.catch()` swallowed the throw, so the
   * label/type rescue map was ALWAYS empty. It is deleted rather than
   * re-pointed at a raw `fetch`, which spec NFR-05 forbids.
   *
   * With that path gone, `Xrm.Utility.getEntityMetadata` is the sole source —
   * so its `Attributes` collection MUST be populated. The second argument is
   * the documented way to guarantee that: callers that already know which
   * attributes they need pass them explicitly, which both removes any
   * dependence on the platform's undocumented "omitted argument" behaviour and
   * shrinks the payload. Callers that genuinely need the whole entity omit it.
   *
   * @internal — called only through the cache wrapper {@link retrieveEntityMetadata}.
   */
  private async fetchEntityMetadataUncached(
    entityName: string,
    attributes?: readonly string[]
  ): Promise<EntityMetadata> {
    const xrm = this.getXrm();
    if (!xrm.Utility) {
      throw new Error(`XrmDataverseClient.retrieveEntityMetadata requires Xrm.Utility (entity: ${entityName}).`);
    }

    const legacyMeta =
      attributes && attributes.length > 0
        ? await xrm.Utility.getEntityMetadata(entityName, [...attributes])
        : await xrm.Utility.getEntityMetadata(entityName);

    return projectEntityMetadata(legacyMeta);
  }

  /**
   * Execute a FetchXML query via `Xrm.WebApi.retrieveMultipleRecords`.
   *
   * The Xrm SDK expects the `fetchXml` parameter embedded in the OData
   * `?fetchXml=...` query string, with the FetchXML XML-encoded once.
   */
  async retrieveMultipleRecords<T = Record<string, unknown>>(
    entityName: string,
    fetchXml: string
  ): Promise<FetchMultipleResult<T>> {
    const xrm = this.getXrm();
    const options = `?fetchXml=${encodeURIComponent(fetchXml)}`;
    const result = await xrm.WebApi.retrieveMultipleRecords(entityName, options);

    const moreRecords =
      result?.['@Microsoft.Dynamics.CRM.morerecords'] === true || result?.['@odata.nextLink'] !== undefined;
    const pagingCookie = result?.['@Microsoft.Dynamics.CRM.fetchxmlpagingcookie'];

    return {
      entities: (result?.entities ?? []) as T[],
      moreRecords,
      pagingCookie,
    };
  }

  /**
   * Retrieve a single record by ID. When `select` is provided, builds an OData
   * `$select` clause; otherwise lets Xrm return its default projection.
   */
  async retrieveRecord<T = Record<string, unknown>>(entityName: string, id: string, select?: string[]): Promise<T> {
    const xrm = this.getXrm();
    const options = select && select.length > 0 ? `?$select=${select.join(',')}` : undefined;
    const result = await xrm.WebApi.retrieveRecord(entityName, id, options);
    return result as T;
  }
}

/* eslint-enable @typescript-eslint/no-explicit-any */
