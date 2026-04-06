/**
 * buildBffApiUrl — the ONLY sanctioned way to construct BFF API URLs.
 *
 * PURPOSE
 * -------
 * Ensures every BFF API request URL has the correct `/api/` prefix.
 * Eliminates the recurring `/api` missing / duplicated production bug
 * by centralizing URL construction in a single, idempotent helper.
 *
 * USAGE
 * -----
 *   import { buildBffApiUrl } from '@spaarke/auth';
 *
 *   // From a Code Page with resolved runtime config
 *   const config = await resolveRuntimeConfig();
 *   const url = buildBffApiUrl(config.bffBaseUrl, '/ai/visualization/related/123');
 *   // → 'https://host.azurewebsites.net/api/ai/visualization/related/123'
 *
 *   // Accepts path WITH or WITHOUT leading /api — both produce the correct URL
 *   buildBffApiUrl(base, '/ai/chat/sessions')     // → base + '/api/ai/chat/sessions'
 *   buildBffApiUrl(base, '/api/ai/chat/sessions') // → base + '/api/ai/chat/sessions' (same)
 *   buildBffApiUrl(base, 'ai/chat/sessions')      // → base + '/api/ai/chat/sessions' (same)
 *
 * WHY THIS EXISTS
 * ---------------
 * The Dataverse env var `sprk_BffApiBaseUrl` stores the URL WITH `/api`
 * (e.g., `https://host/api`). Our normalization functions strip `/api`
 * so the resolved base URL is HOST ONLY. Every caller must then add
 * `/api/` back when constructing request URLs. This manual step is
 * error-prone and has been the source of multiple production bugs.
 *
 * This helper is idempotent:
 *   - If the path already starts with `/api/`, it uses the path as-is.
 *   - If the path doesn't have `/api/`, the helper adds it.
 *   - Leading slash is normalized (optional).
 *   - Trailing slash on base URL is stripped.
 *
 * RULES (enforce via code review or lint)
 * ---------------------------------------
 * - MUST use `buildBffApiUrl()` for all new BFF API URL construction.
 * - MUST NOT use template literals like `${bffBaseUrl}/api/...` directly.
 * - MUST NOT read `sprk_BffApiBaseUrl` and concatenate manually.
 *
 * @see docs/architecture/AUTH-AND-BFF-URL-PATTERN.md
 * @see .claude/patterns/auth/bff-url-normalization.md
 * @see .claude/constraints/auth.md → "BFF Base URL Convention"
 */

/**
 * Build a BFF API URL from a base URL and a path.
 *
 * @param baseUrl - BFF base URL (host only, e.g. `https://host.azurewebsites.net`).
 *                  Trailing slashes are tolerated and stripped.
 * @param path    - Endpoint path. May start with `/api/`, `/`, or nothing.
 *                  The helper ensures the final URL has exactly one `/api/` segment.
 * @returns Full URL ready to pass to `fetch()` or `authenticatedFetch()`.
 *
 * @throws Error if baseUrl is empty or missing (to fail fast with a clear message
 *         instead of silently producing a broken relative URL).
 */
export function buildBffApiUrl(baseUrl: string, path: string): string {
  if (!baseUrl || baseUrl.trim() === '') {
    throw new Error(
      '[buildBffApiUrl] baseUrl is empty. ' +
        'Did you forget to await resolveRuntimeConfig() or call getApiBaseUrl() first?'
    );
  }

  // 1. Normalize base URL — strip trailing slashes
  const base = baseUrl.replace(/\/+$/, '');

  // 2. Normalize path — ensure it starts with a single leading slash
  let normalizedPath = path.trim();
  if (!normalizedPath.startsWith('/')) {
    normalizedPath = '/' + normalizedPath;
  }

  // 3. Ensure the path has an `/api/` prefix (idempotent)
  //    Accept both `/api/...` and `/api...` (no trailing slash after api)
  if (!/^\/api(\/|$)/i.test(normalizedPath)) {
    normalizedPath = '/api' + normalizedPath;
  }

  return base + normalizedPath;
}
