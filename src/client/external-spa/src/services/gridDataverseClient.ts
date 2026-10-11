/**
 * gridDataverseClient — the shared `BffDataverseClient` instance every outside-counsel workspace
 * widget's `<DataGrid configId=… />` reads through (task 016).
 *
 * §11 three-question justification:
 *   1. Existing — `BffDataverseClient` (`@spaarke/ui-components/services/BffDataverseClient`)
 *      already implements the full read-only `IDataverseClient` contract over the BFF passthrough
 *      endpoints; it is REUSED as-is, unmodified.
 *   2. Extension — the only missing piece is an `authenticatedFetch`-SHAPED function
 *      (`(url, init?) => Promise<Response>`) to inject at construction. This SPA's existing
 *      `bff-client.ts` (`bffApiCall`) already implements token-acquire + 401-retry, but it returns
 *      PARSED JSON (`Promise<T>`), not a raw `Response` — `BffDataverseClient` needs the raw
 *      Response so it can unwrap RFC 7807 ProblemDetails into its OWN typed errors
 *      (`BffNotFoundError` / `BffForbiddenError` / …). `bffApiCall` cannot be reused directly; a
 *      thin ~25-line local adapter mirroring its token-acquire + 401-retry shape is the smallest
 *      correct fix.
 *   3. Cost of doing nothing — without this adapter, `new BffDataverseClient({...})` cannot be
 *      constructed (its constructor throws without `authenticatedFetch`), so NONE of the five
 *      outside-counsel widgets can read Dataverse data — task 016's entire goal fails.
 *
 * AUTH STRATEGY (matches `auth/bff-client.ts` file-header rationale — read, not modified):
 * this SPA does NOT use `@spaarke/auth`'s `authenticatedFetch` (B2B/CIAM sessionStorage isolation
 * — ADR-028 documented exception, NFR-05). This adapter reuses the SAME `acquireActiveBffToken()`
 * seam `bff-client.ts` uses (host-selected: CIAM strategy standalone, Teams workforce strategy
 * when embedded — see `host/TeamsHostAdapter.ts`) so both auth paths keep working unchanged.
 *
 * bffBaseUrl points at the task-015 generalized module read seam
 * (`ExternalModuleDataEndpoints`, mounted under `/api/v1/external`) — NOT the internal
 * `/api/dataverse/*` group. D-015-2 (task-015 notes) flagged this wiring as task 016's concern.
 */

import { acquireActiveBffToken } from '../auth/msal-auth';
import { getActiveBackend, type ActiveBackend } from '../auth/backend-selection';
import { BffDataverseClient, type AuthenticatedFetchFn } from '@spaarke/ui-components/services/BffDataverseClient';
import type { IDataverseClient } from '@spaarke/ui-components/services/IDataverseClient';

/*
 * RUN-TIME BACKEND (task 041 — FR-17, NFR-02). The grid's base URL is the ACTIVE backend's
 * (`backend-selection.ts`), resolved when a grid call is made — not at module load, which runs before the bootstrap
 * selects a backend. The token is acquired for the SAME backend's scope, and a URL that is not on that backend's
 * origin is refused before any token is acquired (fail closed): a token for one customer's BFF never goes elsewhere.
 */

/**
 * `authenticatedFetch`-shaped adapter over this SPA's own MSAL token acquisition. Mirrors
 * `auth/bff-client.ts`'s `bffApiCall` token-acquire + single 401-retry behavior, but returns the
 * raw `Response` (not parsed JSON) so `BffDataverseClient` can do its own ProblemDetails unwrap.
 */
function gridAuthenticatedFetchFor(backend: ActiveBackend): AuthenticatedFetchFn {
  return async (url, init) => {
    if (new URL(url).origin !== backend.bffBaseUrl) {
      throw new Error('[gridDataverseClient] The request URL is not on the active backend; the request was not sent.');
    }
    const token = await acquireActiveBffToken(backend);
    const response = await executeWithToken(url, init, token);
    if (response.status !== 401) {
      return response;
    }
    // Single retry with a freshly-acquired token — same contract as bff-client.ts's bffApiCall.
    const freshToken = await acquireActiveBffToken(backend);
    return executeWithToken(url, init, freshToken);
  };
}

function executeWithToken(url: string, init: RequestInit | undefined, token: string): Promise<Response> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    ...(init?.headers as Record<string, string> | undefined),
    // Single allowlisted Bearer-literal site for grid reads — mirrors bff-client.ts's executeFetch
    // (the project's ESLint Bearer-literal ban allowlists both).
    Authorization: `Bearer ${token}`,
  };
  return fetch(url, { ...init, headers });
}

/** One `BffDataverseClient` per active backend (`{bffBaseUrl}/api/v1/external`, the task-015 module read seam). */
let cached: { key: string; client: BffDataverseClient } | null = null;

function clientForActiveBackend(): BffDataverseClient {
  const backend = getActiveBackend();
  const key = `${backend.plane}|${backend.customerKey}|${backend.bffBaseUrl}|${backend.bffScope}`;
  if (!cached || cached.key !== key) {
    cached = {
      key,
      client: new BffDataverseClient({
        authenticatedFetch: gridAuthenticatedFetchFor(backend),
        bffBaseUrl: `${backend.bffBaseUrl}/api/v1/external`,
      }),
    };
  }
  return cached.client;
}

/**
 * Shared singleton — every widget's `<DataGrid dataverseClient={gridDataverseClient} />` passes
 * this same instance. Each call is delegated to the `BffDataverseClient` of the backend active at that moment, so
 * the object identity is stable while the base URL follows the run-time selection. With no active backend a call
 * rejects (never a render-time throw).
 */
class ActiveBackendDataverseClient implements IDataverseClient {
  async retrieveSavedQuery(savedQueryId: string) {
    return clientForActiveBackend().retrieveSavedQuery(savedQueryId);
  }

  async retrieveSavedQueriesForEntity(entityName: string) {
    return clientForActiveBackend().retrieveSavedQueriesForEntity(entityName);
  }

  async retrieveEntityMetadata(entityName: string) {
    return clientForActiveBackend().retrieveEntityMetadata(entityName);
  }

  async retrieveMultipleRecords<T = Record<string, unknown>>(entityName: string, fetchXml: string) {
    return clientForActiveBackend().retrieveMultipleRecords<T>(entityName, fetchXml);
  }

  async retrieveRecord<T = Record<string, unknown>>(entityName: string, id: string, select?: string[]) {
    return clientForActiveBackend().retrieveRecord<T>(entityName, id, select);
  }
}

export const gridDataverseClient: IDataverseClient = new ActiveBackendDataverseClient();
