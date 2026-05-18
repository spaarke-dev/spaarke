/**
 * @spaarke/auth stub for @spaarke/ai-widgets Jest tests.
 *
 * AiSessionProvider imports buildBffApiUrl and authenticatedFetch from
 * @spaarke/auth. The real implementation requires a browser MSAL context
 * that is unavailable in jsdom. This stub provides the minimal API surface
 * needed for module resolution.
 *
 * Tests that exercise BFF calls override these with jest.mock('@spaarke/auth').
 */

export function buildBffApiUrl(base: string, path: string): string {
  return `${base}${path}`;
}

export const authenticatedFetch = jest.fn().mockResolvedValue({
  ok: false,
  status: 503,
  json: async () => ({}),
});
