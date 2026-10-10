/**
 * The two shapes of injected fetch this package handles — structural copies of `@spaarke/auth`'s
 * `OkStatus`, `OkResponse`, `AuthenticatedFetchFn` and `ResponseFetchFn` (same definitions, kept in
 * step by hand).
 *
 * Copies, not imports, for the reason given in `thrownFetchError.ts`: this package takes its fetch
 * by injection so it need not depend on `@spaarke/auth`, and some hosts compile this package's
 * source without `@spaarke/auth` installed (the external SPA) or map it to stubs in their test
 * suites. A type-only import would still have to resolve there.
 *
 * Which to use for an injected fetch:
 *  - {@link AuthenticatedFetchFn} — the prop/service is only ever given `@spaarke/auth`'s
 *    `authenticatedFetch` (a Dataverse code page, PCF or wizard). It THROWS on failure (`ApiError`,
 *    `AuthError`) and resolves only with an {@link OkResponse}, so a `res.status === 404` check
 *    after it does not compile. Handle failures in the `catch` (`thrownFetchError.ts`).
 *  - {@link ResponseFetchFn} — the surface is also reached from a host whose fetch RETURNS failures
 *    (`ok: false`): the external SPA's `createAuthenticatedFetch`, the Office add-ins'
 *    `authenticatedJsonFetch`. The code must handle both a returned `!res.ok` and a thrown error.
 */

/** The registered 2xx statuses — `Response.ok` is true only for 200–299. */
export type OkStatus = 200 | 201 | 202 | 203 | 204 | 205 | 206 | 207 | 208 | 226;

/** A `Response` a throwing fetch RETURNED — a success; failures were thrown instead. */
export type OkResponse = Response & { readonly ok: true; readonly status: OkStatus };

/** `@spaarke/auth`'s `authenticatedFetch`: resolves only with a success, throws every failure. */
export type AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<OkResponse>;

/** A fetch that may RETURN a failed `Response`. An {@link AuthenticatedFetchFn} is assignable to it. */
export type ResponseFetchFn = (url: string, init?: RequestInit) => Promise<Response>;
