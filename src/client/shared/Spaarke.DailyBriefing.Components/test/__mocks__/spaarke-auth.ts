/**
 * Test-local mock for `@spaarke/auth`.
 *
 * R2 task 019 / NFR-05:
 *   The smoke test for `DailyBriefingApp` mounts the component with mocked
 *   Xrm, so it transitively imports `briefingService.ts` which imports
 *   `authenticatedFetch` from `@spaarke/auth`. The Jest `moduleNameMapper`
 *   routes that import here so we don't need MSAL/window-globals.
 *
 *   Individual tests can override the implementation with
 *   `jest.spyOn(spaarkeAuth, "authenticatedFetch")` after import.
 */

// `resolveTenantIdSync` (task 092, 2026-10-04): `composeEditor.registration.ts`
// (one of the LegalWorkspace section registrations `sectionRegistry.ts`
// aggregates) imports it. Not exercised by any test here — a bare string
// return satisfies the type checker.
export const resolveTenantIdSync: (...args: unknown[]) => string = () => '';

// `authInit.ts` (LegalWorkspace service, task 092, 2026-10-04) imports these.
export interface CodePageAuthInitializer {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
export const createCodePageAuthInitializer: (...args: unknown[]) => CodePageAuthInitializer = () => ({});

// `createRuntimeConfigStore` (LegalWorkspace's config/runtimeConfig.ts, task
// 092, 2026-10-04). Typed options param (not bare `any[]`) so the registry's
// `onLazyTenantResolve: (payload) => ...` callback gets a contextual type.
export interface RuntimeConfigLazyTenantResolvePayload {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
export interface RuntimeConfigStoreOptions {
  onLazyTenantResolve?: (payload: RuntimeConfigLazyTenantResolvePayload) => void;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const createRuntimeConfigStore: (options?: RuntimeConfigStoreOptions) => any = () => ({});

// Drift guard (task 092, 2026-10-04) — the 3 exports added in this task,
// checked against the real `@spaarke/auth` module via `satisfies`.
// `authenticatedFetch` (pre-existing, a `jest.Mock`) intentionally excluded —
// its mock shape predates this task and isn't part of what drifted.
const _driftGuard = {
  resolveTenantIdSync,
  createCodePageAuthInitializer,
  createRuntimeConfigStore,
} satisfies Partial<typeof import('@spaarke/auth')>;
void _driftGuard;

// The real error classes and guards — `briefingService` classifies what `authenticatedFetch` THROWS
// (ApiError with `.status`, AuthError for an exhausted 401) through them. Dependency-free source.
export { ApiError, AuthError } from '../../../Spaarke.Auth/src/errors';
export { isApiError, problemOf, isAuthFailure } from '../../../Spaarke.Auth/src/errorGuards';

export const authenticatedFetch: jest.Mock = jest.fn(() =>
  Promise.resolve(
    new Response(
      JSON.stringify({
        tldr: { summary: '', keyTakeaways: [], topAction: '', categoryCount: 0, priorityItemCount: 0 },
        channelNarratives: [],
        generatedAtUtc: new Date().toISOString(),
      }),
      { status: 200, headers: { 'Content-Type': 'application/json' } }
    )
  )
);
