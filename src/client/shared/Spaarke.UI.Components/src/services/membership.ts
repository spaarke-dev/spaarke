/**
 * membership — client resolver for the shared user-record membership service.
 *
 * Backs the DataGrid framework's `behavior.membershipFilter` feature
 * (spaarkeai-assistant-enhancements-r1 task 050). Given an entity type, it calls
 * `GET /api/users/me/memberships/{entityType}` (BFF, Spaarke Auth v2 OBO — ADR-028)
 * and returns the flat list of record ids the CALLER is a member of, so the grid
 * can overlay an `IN(ids)` condition (see `overlayMembershipFilter`).
 *
 * Context-agnostic by design (ADR-012): this module has NO hard dependency on
 * `@spaarke/auth`. The host injects its `authenticatedFetch` (already bound to a
 * BFF base URL) via {@link createMembershipResolver}, so the shared lib stays
 * usable from PCF, Code Pages, and Storybook alike.
 *
 * Fail-soft contract: any transport / non-2xx / parse failure returns `null`
 * (NOT a throw), which the DataGrid treats as "no overlay — run the base query".
 * A genuine empty membership resolves to `[]` (distinct from `null`), which the
 * grid renders as an empty result rather than everyone's records.
 *
 * @see MembershipFilter — configjson schema in `types/DataGridConfiguration.ts`
 * @see overlayMembershipFilter — the FetchXML overlay in `components/DataGrid/fetchXmlOverlay.ts`
 * @see MembershipEndpoints.cs — the BFF endpoint contract (`GET /api/users/me/memberships/{entityType}`)
 */

/**
 * Options forwarded to the membership endpoint as query parameters.
 * All optional — omit for the broadest "everything I'm on" resolution.
 */
export interface MembershipResolveOptions {
  /** Restrict to specific roles (`?roles=` CSV, e.g. `['owner', 'assignedAttorney']`). */
  roles?: readonly string[];
  /** Restrict identity tables (`?identityTypes=` CSV, e.g. `['systemuser', 'contact']`). */
  identityTypes?: readonly string[];
  /** Request 1-hop transitive expansion (`?includeRelated=true`). Rarely needed for grid scoping. */
  includeRelated?: boolean;
  /** Cap the number of ids returned (`?limit=`). Server clamps to its hard max. */
  limit?: number;
}

/**
 * Resolves the caller's membership record ids for `entityType`.
 * - Returns `string[]` (possibly empty) on success.
 * - Returns `null` on any failure (fail-soft → the grid runs its base query).
 */
export type MembershipResolver = (entityType: string, opts?: MembershipResolveOptions) => Promise<string[] | null>;

/**
 * Minimal shape of the membership endpoint response we consume. The endpoint
 * returns a richer payload (`byRole`, `count`, `cacheExpiresAt`, …); the grid
 * only needs the flat `ids` union.
 */
export interface MembershipResponseBody {
  ids?: string[];
  count?: number;
}

/** A host-supplied fetch already bound to the BFF (path in, `Response` out). */
export type MembershipFetch = (path: string, init?: RequestInit) => Promise<Response>;

const DEFAULT_MEMBERSHIP_PATH = '/api/users/me/memberships';

/**
 * Build a {@link MembershipResolver} backed by a host-supplied authenticated fetch.
 *
 * The host passes a `fetch` that already resolves the BFF base URL + attaches the
 * bearer token (typically wrapping `authenticatedFetch` from `@spaarke/auth` with
 * `buildBffApiUrl`). This keeps `@spaarke/ui-components` free of an auth dependency.
 *
 * @param authFetch  Host fetch: `(path, init) => Promise<Response>`. `path` is the
 *                   endpoint path (e.g. `/api/users/me/memberships/sprk_event?roles=owner`).
 * @param options.basePath  Override the membership route base (default
 *                          `/api/users/me/memberships`).
 * @returns A fail-soft {@link MembershipResolver}.
 */
export function createMembershipResolver(
  authFetch: MembershipFetch,
  options?: { basePath?: string }
): MembershipResolver {
  const basePath = (options?.basePath ?? DEFAULT_MEMBERSHIP_PATH).replace(/\/+$/, '');

  return async (entityType, opts) => {
    if (!entityType || !entityType.trim()) return null;
    try {
      const params = new URLSearchParams();
      if (opts?.roles && opts.roles.length > 0) params.set('roles', opts.roles.join(','));
      if (opts?.identityTypes && opts.identityTypes.length > 0) {
        params.set('identityTypes', opts.identityTypes.join(','));
      }
      if (opts?.includeRelated) params.set('includeRelated', 'true');
      if (typeof opts?.limit === 'number' && opts.limit > 0) params.set('limit', String(opts.limit));

      const qs = params.toString();
      const path = `${basePath}/${encodeURIComponent(entityType)}${qs ? `?${qs}` : ''}`;

      const res = await authFetch(path, { method: 'GET' });
      if (!res.ok) {
        // eslint-disable-next-line no-console
        console.warn(`[membership] resolve failed for ${entityType}: HTTP ${res.status}`);
        return null;
      }
      const body = (await res.json()) as MembershipResponseBody;
      return Array.isArray(body.ids) ? body.ids.map(String) : [];
    } catch (err) {
      // eslint-disable-next-line no-console
      console.warn(`[membership] resolve error for ${entityType}:`, err);
      return null;
    }
  };
}
