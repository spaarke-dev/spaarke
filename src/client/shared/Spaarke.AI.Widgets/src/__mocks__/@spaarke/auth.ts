/**
 * @spaarke/auth stub for @spaarke/ai-widgets Jest tests.
 *
 * AiSessionProvider imports buildBffApiUrl, useAuth, and AuthenticatedFetchFn
 * from @spaarke/auth. The real implementation requires a browser MSAL context
 * that is unavailable in jsdom. This stub provides the minimal API surface
 * needed for module resolution and a default useAuth() that satisfies the
 * function-based contract introduced in Spaarke Auth v2 (§H-4).
 *
 * Tests that exercise specific auth behaviour override these with
 * jest.mock('@spaarke/auth', () => ({ ... })) — but the defaults below are
 * enough for the AiSessionProvider provider/context tests because the BFF
 * fetch is short-circuited by passing entityContext=null.
 */

import type { AuthenticatedFetchFn, OkResponse } from '../../../../Spaarke.Auth/src/types';
import { ApiError } from '../../../../Spaarke.Auth/src/errors';

// The REAL fetch types: `authenticatedFetch` resolves only with a success (`OkResponse`) and throws
// every failure.
export type { AuthenticatedFetchFn, ResponseFetchFn, OkResponse, OkStatus } from '../../../../Spaarke.Auth/src/types';

// The REAL error classes and guards — pure, no MSAL. Code under test branches on what the real
// authenticatedFetch throws, so a stub of these would let a test pass against behaviour production
// never shows.
export { ApiError, AuthError } from '../../../../Spaarke.Auth/src/errors';
export { isApiError, problemOf, isAuthFailure } from '../../../../Spaarke.Auth/src/errorGuards';

export function buildBffApiUrl(base: string, path: string): string {
  return `${base}${path}`;
}

/**
 * Default: the BFF is unavailable — THROWN as the real `authenticatedFetch` throws it
 * (`ApiError('HTTP 503', 503)`), never resolved as `{ ok: false }` (a shape that fetch never
 * returns). Tests that need a success or another failure override it per call.
 */
export const authenticatedFetch: jest.MockedFunction<AuthenticatedFetchFn> = jest
  .fn<Promise<OkResponse>, Parameters<AuthenticatedFetchFn>>()
  .mockRejectedValue(new ApiError('HTTP 503', 503, null));

/**
 * Minimal runtime-config store stub (ai-architecture-redesign-r1 task 023).
 * SpaarkeAi's `src/config/runtimeConfig.ts` calls this at MODULE LOAD (via
 * the ContextPaneController → ThreePaneShell import chain), so any SpaarkeAi
 * suite that loads those modules died with "createRuntimeConfigStore is not
 * a function" before this stub existed. Returns a store with stable test
 * defaults; setRuntimeConfig overrides them.
 */
export function createRuntimeConfigStore(_options: { errorLabel: string } & Record<string, unknown>) {
  let config: Record<string, unknown> | null = null;
  return {
    setRuntimeConfig: (c: Record<string, unknown>): void => {
      config = c;
    },
    waitForConfig: (): Promise<void> => Promise.resolve(),
    getBffBaseUrl: (): string => (config?.bffBaseUrl as string) ?? 'https://test-bff.example.com',
    getBffOAuthScope: (): string => (config?.bffOAuthScope as string) ?? 'api://test/.default',
    getMsalClientId: (): string => (config?.msalClientId as string) ?? 'test-client-id',
    getTenantId: (): string => (config?.tenantId as string) ?? 'test-tenant-guid',
  };
}

/**
 * Default useAuth() for tests — provides a stable, authenticated session.
 * Individual tests may override via jest.mock('@spaarke/auth', () => ({ ... }))
 * to simulate logged-out / token-expired / etc states.
 */
export const useAuth = jest.fn(() => ({
  isAuthenticated: true,
  getAccessToken: jest.fn<Promise<string>, []>().mockResolvedValue('test-token'),
  authenticatedFetch,
  tenantId: 'test-tenant-guid',
  logout: jest.fn<Promise<void>, []>().mockResolvedValue(undefined),
}));
