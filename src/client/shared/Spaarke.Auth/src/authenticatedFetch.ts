import type { IProblemDetails } from './types';
import { ApiError, AuthError } from './errors';
import { getAuthProvider } from './initAuth';

/** Exponential backoff base delay (ms). */
const RETRY_BASE_MS = 500;

/** Maximum number of 401 retry attempts. */
const MAX_RETRIES = 3;

/**
 * Performs a fetch request with BFF Bearer token authentication.
 *
 * Features:
 *   - Auto-attaches Bearer header from SpaarkeAuthProvider
 *   - 401 retry with exponential backoff (up to 3 attempts)
 *   - RFC 7807 ProblemDetails error parsing
 *   - Returns Response on success, throws ApiError or AuthError on failure
 *
 * @param url Full or relative URL to fetch
 * @param init Standard fetch RequestInit options
 * @returns Fetch Response (status 2xx-3xx)
 * @throws ApiError for non-2xx responses with ProblemDetails
 * @throws AuthError when token acquisition fails after retries
 */
export async function authenticatedFetch(url: string, init?: RequestInit): Promise<Response> {
  const provider = getAuthProvider();

  // Resolve relative URLs against the configured BFF base URL
  const resolvedUrl = resolveUrl(url, provider.getConfig().bffBaseUrl);

  let lastResponse: Response | null = null;

  for (let attempt = 0; attempt < MAX_RETRIES; attempt++) {
    const token = await provider.getAccessToken();
    const headers = new Headers(init?.headers);
    if (token) {
      headers.set('Authorization', `Bearer ${token}`);
    }

    lastResponse = await fetch(resolvedUrl, { ...init, headers });

    // Success — return immediately
    if (lastResponse.ok) {
      return lastResponse;
    }

    // 401 — clear cache and retry with backoff
    if (lastResponse.status === 401 && attempt < MAX_RETRIES - 1) {
      provider.clearCache();
      const delay = RETRY_BASE_MS * Math.pow(2, attempt);
      await sleep(delay);
      continue;
    }

    // Non-401 error — parse and throw immediately
    break;
  }

  // All retries exhausted or non-retryable error
  if (!lastResponse) {
    throw new AuthError('No response received', 'no_response');
  }

  if (lastResponse.status === 401) {
    throw new AuthError('Authentication failed after all retry attempts', 'auth_exhausted');
  }

  // Try to parse RFC 7807 ProblemDetails
  const problemDetails = await tryParseProblemDetails(lastResponse);
  throw new ApiError(
    problemDetails?.detail ?? problemDetails?.title ?? `HTTP ${lastResponse.status}`,
    lastResponse.status,
    problemDetails
  );
}

/** Resolve a potentially relative URL against the BFF base URL. */
function resolveUrl(url: string, baseUrl: string): string {
  if (url.startsWith('http://') || url.startsWith('https://')) {
    return url;
  }
  if (!baseUrl) return url;

  const base = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
  const path = url.startsWith('/') ? url : `/${url}`;
  return `${base}${path}`;
}

/** Attempt to parse a response body as RFC 7807 ProblemDetails. */
async function tryParseProblemDetails(response: Response): Promise<IProblemDetails | null> {
  try {
    const contentType = response.headers.get('content-type') ?? '';
    if (contentType.includes('application/json') || contentType.includes('application/problem+json')) {
      const body = await response.json();
      if (body && typeof body === 'object' && ('title' in body || 'status' in body)) {
        return body as IProblemDetails;
      }
    }
  } catch {
    // Body parsing failed — return null
  }
  return null;
}

function sleep(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms));
}
