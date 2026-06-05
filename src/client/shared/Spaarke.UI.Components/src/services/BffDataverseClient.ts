/**
 * BffDataverseClient — BFF-passthrough implementation of {@link IDataverseClient}.
 *
 * Wraps the 5 Spaarke BFF Dataverse passthrough endpoints (shipped in B-Wave-1,
 * commit 2e3e32b1) for use in non-MDA hosts: Code Pages, workspace widgets,
 * Office Add-ins, Storybook, plain SPAs — anywhere `window.Xrm` is absent.
 *
 * Sibling of {@link XrmDataverseClient}: both implement the same 5-method
 * `IDataverseClient` contract so consumers can swap implementations without
 * code change (FR-DG-02, task 015 acceptance criterion #1).
 *
 * ## Auth (ADR-028 — Spaarke Auth v2)
 * All HTTP calls flow through the caller-supplied `authenticatedFetch` (a
 * `(url, init?) => Promise<Response>` function). The canonical source is
 * `import { authenticatedFetch } from '@spaarke/auth'`. The wrapper handles
 * token acquisition, 401 retry with exponential backoff, and `Authorization`
 * header attachment — this client does NONE of those.
 *
 * The shared library (`@spaarke/ui-components`) intentionally does NOT take a
 * runtime dependency on `@spaarke/auth` — `authenticatedFetch` is injected at
 * construction so this package stays decoupled from a specific bootstrap
 * strategy (same pattern as `bffDataServiceAdapter` and `EntityCreationService`).
 *
 * ## Error mapping (ProblemDetails → typed errors)
 * Non-2xx responses are unwrapped from RFC 7807 ProblemDetails (with the
 * Spaarke `errorCode` + `correlationId` extension fields) into typed errors:
 *   - 400 → {@link BffBadRequestError}
 *   - 403 → {@link BffForbiddenError}
 *   - 404 → {@link BffNotFoundError}
 *   - 5xx → {@link BffServerError}
 * Consumers can `instanceof` the typed error to differentiate "not found" from
 * "no privilege" from "bad request" without parsing string detail messages.
 *
 * ## R1 read-only scope
 * `IDataverseClient` defines only read operations — there are no `createRecord`
 * / `updateRecord` / `deleteRecord` methods to throw from. Write capability is
 * a future-R extension; for now the existing `bffDataServiceAdapter` /
 * `XrmContext` CRUD paths cover writes.
 *
 * **Spec**: projects/spaarke-datagrid-framework-r1/design.md §6.2 (FR-BFF-06)
 * **ADRs**: ADR-012 (shared-lib home), ADR-019 (ProblemDetails), ADR-028 (auth)
 *
 * @see IDataverseClient — the 5-method contract this class satisfies
 * @see XrmDataverseClient — sibling MDA implementation (task 002)
 */

import type {
  IDataverseClient,
  SavedQueryResult,
  SavedQuerySummary,
  EntityMetadata,
  FetchMultipleResult,
} from './IDataverseClient';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * `authenticatedFetch` function type.
 *
 * Structurally identical to `AuthenticatedFetchFn` exported by `@spaarke/auth`;
 * kept as a local alias so this package has zero runtime dependency on the
 * auth library. The expected caller is `authenticatedFetch` from
 * `import { authenticatedFetch } from '@spaarke/auth'` — it handles token
 * acquisition, 401 retry, and `Authorization: Bearer <jwt>` header attachment.
 */
export type AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<Response>;

/**
 * Constructor options for {@link BffDataverseClient}.
 */
export interface BffDataverseClientOptions {
  /**
   * `authenticatedFetch` from `@spaarke/auth`. REQUIRED — there is no fallback
   * to raw `fetch`. ADR-028 mandates this is the only sanctioned auth surface.
   */
  authenticatedFetch: AuthenticatedFetchFn;
  /**
   * Base URL of the Spaarke BFF API
   * (e.g. `https://spe-api-dev-67e2xz.azurewebsites.net`).
   *
   * Optional — when omitted, falls back to:
   *   1. `window.SPAARKE_BFF_URL` (browser globals)
   *   2. `process.env.SPAARKE_BFF_URL` (node/test contexts)
   * Throws {@link BffDataverseClientConfigurationError} if none resolve.
   *
   * Trailing slashes are trimmed.
   */
  bffBaseUrl?: string;
}

// ---------------------------------------------------------------------------
// Typed errors
// ---------------------------------------------------------------------------

/**
 * Base class for all BffDataverseClient HTTP errors. Carries the BFF
 * `errorCode` (stable string from the 8-code catalog) and `correlationId`
 * (per-request id) extracted from the ProblemDetails response.
 *
 * Stable error codes:
 * `DV_NO_USER_IDENTITY`, `DV_NO_TARGET_ENTITY`, `DV_FETCHXML_MALFORMED`,
 * `DV_FETCHXML_ENTITY_MISMATCH`, `DV_SAVEDQUERY_NOT_FOUND`,
 * `DV_PRIVILEGE_DENIED`, `DV_UPSTREAM_TIMEOUT`, `DV_INTERNAL_ERROR`.
 * `BFF_UNKNOWN_ERROR` is used when the response body is not parseable
 * ProblemDetails.
 */
export class BffDataverseClientError extends Error {
  constructor(
    public readonly errorCode: string,
    public readonly status: number,
    public readonly correlationId: string | undefined,
    message: string
  ) {
    super(message);
    this.name = 'BffDataverseClientError';
  }
}

/** Thrown for 404 responses (e.g. `DV_SAVEDQUERY_NOT_FOUND`). */
export class BffNotFoundError extends BffDataverseClientError {
  constructor(errorCode: string, correlationId: string | undefined, message: string) {
    super(errorCode, 404, correlationId, message);
    this.name = 'BffNotFoundError';
  }
}

/** Thrown for 403 responses (typically `DV_PRIVILEGE_DENIED`). */
export class BffForbiddenError extends BffDataverseClientError {
  constructor(errorCode: string, correlationId: string | undefined, message: string) {
    super(errorCode, 403, correlationId, message);
    this.name = 'BffForbiddenError';
  }
}

/** Thrown for 400 responses (e.g. `DV_FETCHXML_MALFORMED`, `DV_NO_TARGET_ENTITY`). */
export class BffBadRequestError extends BffDataverseClientError {
  constructor(errorCode: string, correlationId: string | undefined, message: string) {
    super(errorCode, 400, correlationId, message);
    this.name = 'BffBadRequestError';
  }
}

/** Thrown for 5xx responses (e.g. `DV_UPSTREAM_TIMEOUT`, `DV_INTERNAL_ERROR`). */
export class BffServerError extends BffDataverseClientError {
  constructor(errorCode: string, status: number, correlationId: string | undefined, message: string) {
    super(errorCode, status, correlationId, message);
    this.name = 'BffServerError';
  }
}

/**
 * Thrown at construction time when `bffBaseUrl` cannot be resolved from
 * options, `window.SPAARKE_BFF_URL`, or `process.env.SPAARKE_BFF_URL`.
 */
export class BffDataverseClientConfigurationError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'BffDataverseClientConfigurationError';
  }
}

// ---------------------------------------------------------------------------
// Internal — bffBaseUrl resolution + ProblemDetails unwrap
// ---------------------------------------------------------------------------

const BFF_URL_RESOLUTION_ERROR =
  'BffDataverseClient requires bffBaseUrl. Pass it via constructor options, ' +
  'or set window.SPAARKE_BFF_URL / process.env.SPAARKE_BFF_URL.';

/**
 * Resolve `bffBaseUrl` from constructor options → `window.SPAARKE_BFF_URL` →
 * `process.env.SPAARKE_BFF_URL`. Throws {@link BffDataverseClientConfigurationError}
 * if none are present.
 *
 * Trims trailing slashes from the resolved URL.
 *
 * @internal
 */
function resolveBffBaseUrl(optionsUrl: string | undefined): string {
  // 1. Constructor option (highest priority)
  if (typeof optionsUrl === 'string' && optionsUrl.length > 0) {
    return optionsUrl.replace(/\/+$/, '');
  }

  // 2. Browser global `window.SPAARKE_BFF_URL`
  try {
    if (typeof window !== 'undefined') {
      const windowUrl = (window as unknown as { SPAARKE_BFF_URL?: string }).SPAARKE_BFF_URL;
      if (typeof windowUrl === 'string' && windowUrl.length > 0) {
        return windowUrl.replace(/\/+$/, '');
      }
    }
  } catch {
    // Defensive — fall through if window access throws (SSR edge case).
  }

  // 3. Node/test env `process.env.SPAARKE_BFF_URL`
  try {
    if (typeof process !== 'undefined' && process.env && typeof process.env.SPAARKE_BFF_URL === 'string') {
      const envUrl = process.env.SPAARKE_BFF_URL;
      if (envUrl.length > 0) {
        return envUrl.replace(/\/+$/, '');
      }
    }
  } catch {
    // Defensive — fall through if process access throws.
  }

  throw new BffDataverseClientConfigurationError(BFF_URL_RESOLUTION_ERROR);
}

/**
 * RFC 7807 ProblemDetails shape, with the Spaarke `errorCode` + `correlationId`
 * extension fields the BFF emits for the 5 Dataverse passthrough endpoints.
 */
interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errorCode?: string;
  correlationId?: string;
}

/**
 * Parse the response body as ProblemDetails. Returns `null` if the body is
 * not JSON or does not have a ProblemDetails-like shape.
 *
 * @internal
 */
async function tryParseProblemDetails(response: Response): Promise<ProblemDetails | null> {
  try {
    const contentType = response.headers.get('content-type') ?? '';
    // Accept application/json (Spaarke BFF) and application/problem+json (canonical RFC 7807).
    if (!contentType.includes('application/json') && !contentType.includes('application/problem+json')) {
      return null;
    }
    const body = await response.json();
    if (body && typeof body === 'object' && ('title' in body || 'status' in body || 'errorCode' in body)) {
      return body as ProblemDetails;
    }
    return null;
  } catch {
    return null;
  }
}

/**
 * Map a non-2xx response to the appropriate typed error. The error code falls
 * back to `BFF_UNKNOWN_ERROR` when the body is not parseable ProblemDetails;
 * the message falls back to `HTTP <status>` when no detail is available.
 *
 * @internal
 */
async function mapErrorResponse(response: Response): Promise<BffDataverseClientError> {
  const problem = await tryParseProblemDetails(response);
  const errorCode = problem?.errorCode ?? 'BFF_UNKNOWN_ERROR';
  const correlationId = problem?.correlationId;
  const message = problem?.detail ?? problem?.title ?? `HTTP ${response.status} ${response.statusText}`.trim();
  const status = response.status;

  if (status === 404) {
    return new BffNotFoundError(errorCode, correlationId, message);
  }
  if (status === 403) {
    return new BffForbiddenError(errorCode, correlationId, message);
  }
  if (status === 400) {
    return new BffBadRequestError(errorCode, correlationId, message);
  }
  if (status >= 500) {
    return new BffServerError(errorCode, status, correlationId, message);
  }
  // Other 4xx (401, 405, 409, etc.) — surface as generic BffDataverseClientError.
  return new BffDataverseClientError(errorCode, status, correlationId, message);
}

// ---------------------------------------------------------------------------
// Class
// ---------------------------------------------------------------------------

/**
 * BFF-passthrough implementation of {@link IDataverseClient}. See file-level
 * JSDoc for context, auth model, and error mapping semantics.
 */
export class BffDataverseClient implements IDataverseClient {
  private readonly authenticatedFetch: AuthenticatedFetchFn;
  private readonly bffBaseUrl: string;

  constructor(options: BffDataverseClientOptions) {
    if (!options || typeof options.authenticatedFetch !== 'function') {
      throw new BffDataverseClientConfigurationError(
        'BffDataverseClient requires options.authenticatedFetch (from @spaarke/auth).'
      );
    }
    this.authenticatedFetch = options.authenticatedFetch;
    this.bffBaseUrl = resolveBffBaseUrl(options.bffBaseUrl);
  }

  /**
   * `GET {bffBaseUrl}/api/dataverse/savedquery/{savedQueryId}`
   *
   * Returns the savedquery's `{ entityName, fetchXml, layoutXml, name }`.
   * 404 → {@link BffNotFoundError} (`DV_SAVEDQUERY_NOT_FOUND`).
   * 403 → {@link BffForbiddenError} (`DV_PRIVILEGE_DENIED` on the savedquery's entity).
   */
  async retrieveSavedQuery(savedQueryId: string): Promise<SavedQueryResult> {
    const url = `${this.bffBaseUrl}/api/dataverse/savedquery/${encodeURIComponent(savedQueryId)}`;
    const response = await this.authenticatedFetch(url, {
      method: 'GET',
      headers: { Accept: 'application/json' },
    });
    if (!response.ok) {
      throw await mapErrorResponse(response);
    }
    return (await response.json()) as SavedQueryResult;
  }

  /**
   * `GET {bffBaseUrl}/api/dataverse/savedqueries/{entityLogicalName}`
   *
   * Returns the active main views (`statecode=0, querytype=0`) for the entity
   * as `[{ id, name, isDefault, queryType }]`.
   */
  async retrieveSavedQueriesForEntity(entityName: string): Promise<SavedQuerySummary[]> {
    const url = `${this.bffBaseUrl}/api/dataverse/savedqueries/${encodeURIComponent(entityName)}`;
    const response = await this.authenticatedFetch(url, {
      method: 'GET',
      headers: { Accept: 'application/json' },
    });
    if (!response.ok) {
      throw await mapErrorResponse(response);
    }
    return (await response.json()) as SavedQuerySummary[];
  }

  /**
   * `GET {bffBaseUrl}/api/dataverse/metadata/{entityLogicalName}`
   *
   * Returns projected `EntityMetadata` (primaryIdAttribute, primaryNameAttribute,
   * and attribute map with type/format/optionSet). The BFF caches this 6h
   * (FR-BFF-03); this client adds no extra layer of caching.
   */
  async retrieveEntityMetadata(entityName: string): Promise<EntityMetadata> {
    const url = `${this.bffBaseUrl}/api/dataverse/metadata/${encodeURIComponent(entityName)}`;
    const response = await this.authenticatedFetch(url, {
      method: 'GET',
      headers: { Accept: 'application/json' },
    });
    if (!response.ok) {
      throw await mapErrorResponse(response);
    }
    return (await response.json()) as EntityMetadata;
  }

  /**
   * `POST {bffBaseUrl}/api/dataverse/fetch`
   *
   * Body: `{ entityName, fetchXml, pagingCookie }`.
   * Returns `{ entities, moreRecords, pagingCookie? }`.
   *
   * **Pagination (R1 — Option A)**: The `IDataverseClient` 2-arg contract does
   * NOT expose `pagingCookie`. This client therefore always passes
   * `pagingCookie: undefined` — paging across this surface is a future-R
   * extension. The `useLazyLoad` hook (task 003) handles paging differently
   * via the XrmDataverseClient direct path.
   *
   * 400 → {@link BffBadRequestError} (`DV_FETCHXML_MALFORMED`, `DV_FETCHXML_ENTITY_MISMATCH`).
   * 403 → {@link BffForbiddenError} (`DV_PRIVILEGE_DENIED`).
   */
  async retrieveMultipleRecords<T = Record<string, unknown>>(
    entityName: string,
    fetchXml: string
  ): Promise<FetchMultipleResult<T>> {
    const url = `${this.bffBaseUrl}/api/dataverse/fetch`;
    const response = await this.authenticatedFetch(url, {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        entityName,
        fetchXml,
        pagingCookie: undefined,
      }),
    });
    if (!response.ok) {
      throw await mapErrorResponse(response);
    }
    return (await response.json()) as FetchMultipleResult<T>;
  }

  /**
   * `GET {bffBaseUrl}/api/dataverse/record/{entityLogicalName}/{id}?$select=...`
   *
   * When `select` is empty/undefined, the `$select` query string is omitted
   * and the BFF returns the default projection.
   */
  async retrieveRecord<T = Record<string, unknown>>(entityName: string, id: string, select?: string[]): Promise<T> {
    const selectClause = select && select.length > 0 ? `?$select=${select.join(',')}` : '';
    const url =
      `${this.bffBaseUrl}/api/dataverse/record/${encodeURIComponent(entityName)}/` +
      `${encodeURIComponent(id)}${selectClause}`;
    const response = await this.authenticatedFetch(url, {
      method: 'GET',
      headers: { Accept: 'application/json' },
    });
    if (!response.ok) {
      throw await mapErrorResponse(response);
    }
    return (await response.json()) as T;
  }
}
