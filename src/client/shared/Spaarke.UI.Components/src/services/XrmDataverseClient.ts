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
    maxPageSize?: number,
  ): Promise<{
    entities: Array<Record<string, any>>;
    '@Microsoft.Dynamics.CRM.morerecords'?: boolean;
    '@Microsoft.Dynamics.CRM.fetchxmlpagingcookie'?: string;
    '@odata.nextLink'?: string;
  }>;
}

interface XrmUtilityLike {
  /**
   * `Xrm.Utility.getEntityMetadata` returns a Promise of an EntityMetadata object
   * whose shape mirrors the Web API. We project to our framework's narrower shape.
   */
  getEntityMetadata(entityName: string, attributes?: string[]): Promise<any>;
}

interface XrmLike {
  WebApi: XrmWebApiLike;
  Utility?: XrmUtilityLike;
}

const XRM_MISSING_MESSAGE =
  'XrmDataverseClient requires Xrm context. Use BffDataverseClient outside MDA.';

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
 * Map `Xrm.Utility.getEntityMetadata` AttributeType values to our framework's
 * narrower `MetadataAttributeType` discriminator. Unknown values pass through
 * as-is (the type is `string`-open).
 */
function normalizeAttributeType(attributeType: unknown): MetadataAttributeType {
  if (typeof attributeType !== 'string') {
    return 'String';
  }
  return attributeType as MetadataAttributeType;
}

/**
 * Project the Xrm OptionMetadata shape to our {@link OptionSetOption}.
 * Xrm gives `Value`, `Label.UserLocalizedLabel.Label`, and `Color`.
 */
function projectOptions(optionSet: any): OptionSetOption[] | undefined {
  const options: any[] | undefined = optionSet?.Options ?? optionSet?.options;
  if (!Array.isArray(options) || options.length === 0) {
    return undefined;
  }
  return options.map(opt => {
    const label =
      opt?.Label?.UserLocalizedLabel?.Label ??
      opt?.Label?.LocalizedLabels?.[0]?.Label ??
      String(opt?.Value ?? '');
    return {
      value: Number(opt?.Value ?? 0),
      label,
      color: typeof opt?.Color === 'string' ? opt.Color : undefined,
    };
  });
}

/**
 * Project one Xrm attribute metadata entry to our {@link EntityAttributeMetadata}.
 * The Xrm metadata object usually has:
 *  - `AttributeType` (string discriminator)
 *  - `Format` (sub-type for string attributes, etc.)
 *  - `IsPrimaryName`, `IsPrimaryId`
 *  - `OptionSet` (for Picklist) / `GlobalOptionSet` / state+status attributes have nested OptionSet
 */
function projectAttribute(attr: any): EntityAttributeMetadata {
  const attributeType = normalizeAttributeType(attr?.AttributeType ?? attr?.attributeType);

  // Format is most relevant for String attributes; preserve when present.
  const format =
    typeof attr?.Format === 'string'
      ? attr.Format
      : typeof attr?.format === 'string'
        ? attr.format
        : undefined;

  // OptionSet location varies across Xrm versions: top-level OptionSet, GlobalOptionSet,
  // or nested under attribute. Probe in priority order.
  const optionSet =
    projectOptions(attr?.OptionSet) ??
    projectOptions(attr?.GlobalOptionSet) ??
    projectOptions(attr?.optionSet);

  return {
    attributeType,
    format,
    isPrimaryName: attr?.IsPrimaryName === true || attr?.isPrimaryName === true || undefined,
    isPrimaryId: attr?.IsPrimaryId === true || attr?.isPrimaryId === true || undefined,
    optionSet,
  };
}

/**
 * Project the full Xrm EntityMetadata payload to our {@link EntityMetadata} shape.
 *
 * Xrm's `getEntityMetadata(entityName, ['Attributes'])` returns an object whose
 * `Attributes` (or `attributes`) property is either an array of attribute metadata
 * or a `get()`-style collection. We support both forms for resilience.
 */
function projectEntityMetadata(meta: any): EntityMetadata {
  const primaryIdAttribute: string =
    meta?.PrimaryIdAttribute ?? meta?.primaryIdAttribute ?? '';
  const primaryNameAttribute: string =
    meta?.PrimaryNameAttribute ?? meta?.primaryNameAttribute ?? '';

  const rawAttributes = meta?.Attributes ?? meta?.attributes ?? [];
  let attributeArray: any[] = [];

  if (Array.isArray(rawAttributes)) {
    attributeArray = rawAttributes;
  } else if (typeof rawAttributes?.get === 'function') {
    // Some Xrm versions expose a collection accessor.
    try {
      attributeArray = rawAttributes.get() ?? [];
    } catch {
      attributeArray = [];
    }
  } else if (rawAttributes && typeof rawAttributes === 'object') {
    // Treat as a record of logicalName → metadata.
    attributeArray = Object.values(rawAttributes);
  }

  const attributes: Record<string, EntityAttributeMetadata> = {};
  for (const attr of attributeArray) {
    const logicalName: string | undefined =
      attr?.LogicalName ?? attr?.logicalName ?? attr?.Name ?? attr?.name;
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
    const result = await xrm.WebApi.retrieveRecord(
      'savedquery',
      savedQueryId,
      SAVEDQUERY_SINGLE_SELECT,
    );

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
      buildSavedQueriesForEntityOptions(entityName),
    );

    return (result?.entities ?? []).map(row => ({
      id: String(row?.savedqueryid ?? ''),
      name: String(row?.name ?? ''),
      isDefault: row?.isdefault === true,
      queryType: typeof row?.querytype === 'number' ? row.querytype : 0,
    }));
  }

  /**
   * Retrieve projected entity metadata via `Xrm.Utility.getEntityMetadata`.
   *
   * Throws if `Xrm.Utility` is unavailable (rare — older clients only).
   */
  async retrieveEntityMetadata(entityName: string): Promise<EntityMetadata> {
    const xrm = this.getXrm();
    if (!xrm.Utility) {
      throw new Error(
        `XrmDataverseClient.retrieveEntityMetadata requires Xrm.Utility (entity: ${entityName}).`,
      );
    }
    const meta = await xrm.Utility.getEntityMetadata(entityName, ['Attributes']);
    return projectEntityMetadata(meta);
  }

  /**
   * Execute a FetchXML query via `Xrm.WebApi.retrieveMultipleRecords`.
   *
   * The Xrm SDK expects the `fetchXml` parameter embedded in the OData
   * `?fetchXml=...` query string, with the FetchXML XML-encoded once.
   */
  async retrieveMultipleRecords<T = Record<string, unknown>>(
    entityName: string,
    fetchXml: string,
  ): Promise<FetchMultipleResult<T>> {
    const xrm = this.getXrm();
    const options = `?fetchXml=${encodeURIComponent(fetchXml)}`;
    const result = await xrm.WebApi.retrieveMultipleRecords(entityName, options);

    const moreRecords =
      result?.['@Microsoft.Dynamics.CRM.morerecords'] === true ||
      result?.['@odata.nextLink'] !== undefined;
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
  async retrieveRecord<T = Record<string, unknown>>(
    entityName: string,
    id: string,
    select?: string[],
  ): Promise<T> {
    const xrm = this.getXrm();
    const options =
      select && select.length > 0 ? `?$select=${select.join(',')}` : undefined;
    const result = await xrm.WebApi.retrieveRecord(entityName, id, options);
    return result as T;
  }
}

/* eslint-enable @typescript-eslint/no-explicit-any */
