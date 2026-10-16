/**
 * ontologyAdminCapability - the ONE question the Console asks before it shows the "Ontology admin" tab:
 * `GET /api/v1/ontology/admin/capabilities` (task 104; D-22, FR-56).
 *
 * The BFF answers it AS THE CALLER: does the caller hold `prvCreatesprk_PolicyVersion`? The answer only decides whether the tab
 * is offered. It is never a security boundary: every admin write goes through a BFF route that runs as the caller, so Dataverse
 * refuses a user without the Spaarke Ontology Administrator role whatever this function returned.
 *
 * Fail closed: only a 2xx whose body says `canAuthorPolicies: true` is true. A 401/403/503, a network failure, or a body that
 * cannot be read is false (the tab is not offered). A failure reaches here two ways and both are handled: the injected fetch
 * THROWS (`@spaarke/auth`'s `authenticatedFetch` throws an `ApiError` on a non-2xx) or RETURNS a non-2xx `Response`.
 *
 * The fetch is injected (ADR-012, ADR-028): the host passes its authenticated fetch; this package holds no token.
 *
 * Task: spaarke-ontology-platform-r1, task 104.
 */

import type { ResponseFetchFn } from '../../utils/fetchTypes';

/** The probe route (relative to the BFF base URL). */
export const ONTOLOGY_ADMIN_CAPABILITIES_ROUTE = '/api/v1/ontology/admin/capabilities';

export interface OntologyAdminCapabilityOptions {
  /** The host's authenticated fetch (it may throw on a non-2xx, or return it). */
  fetch: ResponseFetchFn;
  /** The BFF base URL, with or without a trailing slash. */
  baseUrl: string;
}

/** True only when the BFF says, as the caller, that they can author policies. Never throws. */
export async function fetchOntologyAdminCapability({
  fetch,
  baseUrl,
}: OntologyAdminCapabilityOptions): Promise<boolean> {
  const address = `${baseUrl.replace(/\/+$/, '')}${ONTOLOGY_ADMIN_CAPABILITIES_ROUTE}`;
  try {
    const response = await fetch(address, { method: 'GET', headers: { Accept: 'application/json' } });
    if (!response.ok) {
      return false;
    }
    const body: unknown = await response.json();
    return (
      typeof body === 'object' && body !== null && (body as { canAuthorPolicies?: unknown }).canAuthorPolicies === true
    );
  } catch {
    // An ApiError (non-2xx), a network failure or an unreadable body: the tab is not offered.
    return false;
  }
}
