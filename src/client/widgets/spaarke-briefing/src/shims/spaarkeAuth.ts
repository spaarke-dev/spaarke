/**
 * The build-time stand-in for `@spaarke/auth` in this widget (spaarkeai-mcp-bff-api-r2 task 034; FR-16: no fetch, no sign-in library).
 *
 * `DailyBriefingApp` and its package import `@spaarke/auth` statically, for the calls the Console makes (the briefing, the inline To Do, the
 * email dialog). In this widget the host supplies a `BriefingDataService`, and the component then never reaches any of them (task 010's
 * seam test proves it; this widget's tests prove it again through the built wiring). Bundling the real module would put the sign-in library
 * and its network layer into a sandboxed page that has neither a network origin nor a token, so the build maps the package name to this
 * file (vite alias, tsconfig `paths`, jest `moduleNameMapper`). Every member here fails closed: a call that somehow got through is
 * refused, never sent.
 */

/** The shape of the real `authenticatedFetch` (a relative `/api` path and a `fetch`-style init), so the package type-checks unchanged. */
export type AuthenticatedFetchFn = (input: string, init?: RequestInit) => Promise<Response>;

const NO_NETWORK = 'This widget has no network access: everything goes through the host.';

/** Always rejects: the widget declares no network origin and holds no token. */
export const authenticatedFetch: AuthenticatedFetchFn = () => Promise.reject(new Error(NO_NETWORK));

/** No error is an API error here: nothing is ever sent, so nothing answers with a status. */
export function isApiError(_error: unknown): _error is { status: number } {
  return false;
}

/** No error is a sign-in failure here: there is no sign-in. */
export function isAuthFailure(_error: unknown): boolean {
  return false;
}
