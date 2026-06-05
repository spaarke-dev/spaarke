/**
 * IDataverseClient — Dataverse access contract for the DataGrid framework.
 *
 * Defines the 5-method surface the new `<DataGrid configId={...} />` framework needs
 * to discover savedqueries, project entity metadata, execute FetchXML queries, and
 * retrieve single records — independent of host (MDA Custom Page vs. Code Page vs.
 * SpaarkeAi widget vs. test harness).
 *
 * **Implementations** (R1):
 * - `XrmDataverseClient` — wraps `Xrm.WebApi` + `Xrm.Utility.getEntityMetadata`. Auto-walks
 *   `window` / `window.parent` for the Xrm object (Custom Page iframe case). For MDA hosts.
 * - `BffDataverseClient` — wraps `authenticatedFetch` from `@spaarke/auth` against the
 *   5 BFF passthrough endpoints under `Sprk.Bff.Api/Api/Dataverse/`. For non-MDA hosts.
 *
 * **Relationship to existing types** (brownfield clarification):
 * - For pure CRUD without metadata/savedquery operations, prefer the existing
 *   {@link IDataService} in `types/serviceInterfaces.ts`. The two interfaces overlap
 *   on `retrieveRecord` + `retrieveMultipleRecords` but are intentionally independent
 *   — IDataverseClient is the framework's metadata + query contract.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/design.md §6.2
 * **ADR**: ADR-012 (shared component library home), ADR-021 (Fluent v9), ADR-028 (auth)
 *
 * @see DataGridConfiguration — the configjson schema this client serves
 * @see IDataService — legacy CRUD contract (coexists)
 */

/**
 * Result shape returned by {@link IDataverseClient.retrieveSavedQuery}.
 */
export interface SavedQueryResult {
  /** Logical name of the entity the savedquery targets (e.g. "sprk_event"). */
  entityName: string;
  /** FetchXML query body, including filter / sort / link-entity / paging placeholders. */
  fetchXml: string;
  /** Layout XML declaring `<row id="...">`, `<grid jump="...">`, and per-column `<cell>`. */
  layoutXml: string;
  /** Display name of the savedquery. */
  name: string;
}

/**
 * Summary shape returned by {@link IDataverseClient.retrieveSavedQueriesForEntity}.
 */
export interface SavedQuerySummary {
  /** GUID of the savedquery record. */
  id: string;
  /** Display name. */
  name: string;
  /** Whether this is the default view for the entity. */
  isDefault: boolean;
  /** Dataverse `querytype` (0 = system / user view; other values exist for specialty queries). */
  queryType: number;
}

/**
 * Attribute type discriminator. The named values are the common set used by filter chip
 * auto-derivation (FR-DG-06); other Dataverse types may surface as the open-ended `string`
 * branch and are handled by default renderers.
 */
export type MetadataAttributeType =
  | 'String'
  | 'Integer'
  | 'Money'
  | 'DateTime'
  | 'Lookup'
  | 'Picklist'
  | 'Status'
  | 'State'
  | 'Boolean'
  | 'Decimal'
  | string;

/**
 * One option in a Picklist / Status / State option set.
 * `color` is a hex string sourced from Dataverse metadata — it is DATA (not styling intent)
 * and is exempt from the framework's NO-RAW-HEX rule.
 */
export interface OptionSetOption {
  value: number;
  label: string;
  color?: string;
}

/**
 * Projected attribute metadata used by the framework. Mirrors the BFF
 * `EntityMetadataDto.AttributeDto` shape (FR-BFF-03) and the Xrm
 * `getEntityMetadata` payload.
 */
export interface EntityAttributeMetadata {
  attributeType: MetadataAttributeType;
  /** Sub-type for String attributes (e.g., 'Email', 'Phone', 'Url', 'TextArea'). */
  format?: string;
  /**
   * User-localized DisplayName (e.g., "Invoice Number") from Xrm/BFF metadata.
   * Framework column-label resolution prefers this over humanized logical names.
   */
  displayName?: string;
  isPrimaryName?: boolean;
  isPrimaryId?: boolean;
  /** Option set values (Picklist, Status, State only). */
  optionSet?: OptionSetOption[];
}

/**
 * Projected entity metadata returned by {@link IDataverseClient.retrieveEntityMetadata}.
 * Map of `logicalName` → attribute metadata; this is the shape consumed by the DataGrid
 * core, the filter chip auto-derivation logic, and the column renderer service.
 */
export interface EntityMetadata {
  primaryIdAttribute: string;
  primaryNameAttribute: string;
  attributes: Record<string, EntityAttributeMetadata>;
}

/**
 * Result shape returned by {@link IDataverseClient.retrieveMultipleRecords}.
 *
 * @template T - Row shape (defaults to a key/value bag).
 */
export interface FetchMultipleResult<T = Record<string, unknown>> {
  entities: T[];
  /** Whether more records remain past this page. */
  moreRecords: boolean;
  /**
   * Opaque paging cookie to pass into the next FetchXML query (`<fetch page="N" paging-cookie="...">`).
   * Absent when `moreRecords` is `false`.
   */
  pagingCookie?: string;
}

/**
 * 5-method contract every DataGrid host implementation must satisfy.
 *
 * @see XrmDataverseClient (task 002) — MDA wrapper for `Xrm.WebApi`
 * @see BffDataverseClient (task 015) — BFF passthrough wrapper for `authenticatedFetch`
 */
export interface IDataverseClient {
  /**
   * Retrieve a single savedquery record by ID.
   *
   * Used by the framework to resolve `source.savedQueryId` (or each entry in a
   * `source.savedquery-set`) into the FetchXML + layoutXml the DataGrid renders.
   *
   * @param savedQueryId - GUID of the savedquery record.
   */
  retrieveSavedQuery(savedQueryId: string): Promise<SavedQueryResult>;

  /**
   * Retrieve the active savedqueries for an entity (`querytype = 0`, `statecode = 0`).
   *
   * Used by `source.type = "savedquery-set"` to auto-discover views available for an
   * entity — eliminating config drift when a Dataverse admin adds a view.
   *
   * @param entityName - Logical name of the entity (e.g. "sprk_event").
   */
  retrieveSavedQueriesForEntity(entityName: string): Promise<SavedQuerySummary[]>;

  /**
   * Retrieve projected attribute metadata for an entity.
   *
   * Used by the framework to (a) resolve `primaryIdAttribute` / `primaryNameAttribute`,
   * (b) auto-derive filter chips from layoutXml columns whose `AttributeType` matches
   * `OptionSet | Status | State | Lookup | DateTime | Boolean` (FR-DG-06), and
   * (c) drive per-cell renderers via `attributeType` / `format` / `optionSet.color`.
   *
   * Implementations SHOULD cache aggressively (the BFF caches 6h per FR-BFF-03).
   *
   * @param entityName - Logical name of the entity.
   */
  retrieveEntityMetadata(entityName: string): Promise<EntityMetadata>;

  /**
   * Execute a FetchXML query against an entity.
   *
   * @template T - Row shape (defaults to a key/value bag).
   * @param entityName - Logical name of the primary entity.
   * @param fetchXml - FetchXML query body. May include `<filter>`, `<order>`,
   *                   `<link-entity>`, `<attribute>`, and paging attributes
   *                   (`page`, `count`, `paging-cookie`).
   */
  retrieveMultipleRecords<T = Record<string, unknown>>(
    entityName: string,
    fetchXml: string
  ): Promise<FetchMultipleResult<T>>;

  /**
   * Retrieve a single record by ID.
   *
   * @template T - Record shape (defaults to a key/value bag).
   * @param entityName - Logical name of the entity.
   * @param id - GUID of the record.
   * @param select - Optional list of attribute logical names to project. When omitted,
   *                 implementations SHOULD return at least the primary id + primary name.
   */
  retrieveRecord<T = Record<string, unknown>>(entityName: string, id: string, select?: string[]): Promise<T>;
}
