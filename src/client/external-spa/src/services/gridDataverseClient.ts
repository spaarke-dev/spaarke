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

import { BFF_API_URL } from '../config';
import { acquireActiveBffToken } from '../auth/msal-auth';
import { BffDataverseClient, type AuthenticatedFetchFn } from '@spaarke/ui-components/services/BffDataverseClient';

/**
 * `authenticatedFetch`-shaped adapter over this SPA's own MSAL token acquisition. Mirrors
 * `auth/bff-client.ts`'s `bffApiCall` token-acquire + single 401-retry behavior, but returns the
 * raw `Response` (not parsed JSON) so `BffDataverseClient` can do its own ProblemDetails unwrap.
 */
const gridAuthenticatedFetch: AuthenticatedFetchFn = async (url, init) => {
  const token = await acquireActiveBffToken();
  const response = await executeWithToken(url, init, token);
  if (response.status !== 401) {
    return response;
  }
  // Single retry with a freshly-acquired token — same contract as bff-client.ts's bffApiCall.
  const freshToken = await acquireActiveBffToken();
  return executeWithToken(url, init, freshToken);
};

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

/** The BFF base URL for the task-015 module read seam (`/api/v1/external/api/dataverse/*`). */
const GRID_BFF_BASE_URL = `${BFF_API_URL}/api/v1/external`;

/**
 * Shared singleton — every widget's `<DataGrid dataverseClient={gridDataverseClient} />` passes
 * this same instance. Stateless aside from its constructor options, so sharing is safe.
 */
export const gridDataverseClient = new BffDataverseClient({
  authenticatedFetch: gridAuthenticatedFetch,
  bffBaseUrl: GRID_BFF_BASE_URL,
});
