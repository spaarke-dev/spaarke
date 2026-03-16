/**
 * BFF API client module for the Secure Project Workspace SPA.
 *
 * Acquires OAuth tokens via the Power Pages implicit grant flow and provides
 * typed wrappers for authenticated BFF API calls with Bearer token auth.
 *
 * Token caching: in-memory only (never localStorage) with expiry tracking.
 * Auto-refreshes when fewer than 5 minutes remain before expiry.
 *
 * See: docs/architecture/power-pages-spa-guide.md — Authentication section
 */

import { BFF_API_URL, PORTAL_CLIENT_ID } from "../config";
import { ApiError } from "../types";

// ---------------------------------------------------------------------------
// Request / Response types for BFF endpoints
// ---------------------------------------------------------------------------

/** Request body for granting external access to a project */
export interface GrantAccessRequest {
  /** Contact record ID of the user to grant access to */
  contactId: string;
  /** Secure Project record ID */
  projectId: string;
  /**
   * Access level value matching the Dataverse sprk_accesslevel option set.
   * 100000000 = ViewOnly, 100000001 = Collaborate, 100000002 = FullAccess
   */
  accessLevel: number;
  /** ISO date string for access expiry (optional) */
  expiryDate?: string;
  /** Account record ID (optional, for firm-level scoping) */
  accountId?: string;
}

/** Request body for revoking an existing external access record */
export interface RevokeAccessRequest {
  /** The sprk_externalrecordaccess record ID to revoke */
  accessRecordId: string;
  /** Contact record ID of the user whose access is being revoked */
  contactId: string;
  /** Secure Project record ID */
  projectId: string;
  /** SPE container ID (optional, used for SPE permission cleanup) */
  containerId?: string;
}

/** Request body for inviting a new external user to a project */
export interface InviteUserRequest {
  /** Email address of the user to invite (used to look up or create the Contact) */
  email: string;
  /** Secure Project record ID */
  projectId: string;
  /**
   * Access level to grant to the invited user.
   * 100000000 = ViewOnly, 100000001 = Collaborate, 100000002 = FullAccess
   */
  accessLevel: number;
  /** Optional first name for the invited user (used when creating a new Contact) */
  firstName?: string;
  /** Optional last name for the invited user (used when creating a new Contact) */
  lastName?: string;
  /** ISO date string for invitation expiry (optional) */
  expiryDate?: string;
  /** Account record ID (optional, for firm-level scoping) */
  accountId?: string;
}

/**
 * External user context returned by GET /api/v1/external/me.
 * Combines portal identity with the user's project access records.
 */
export interface ExternalUserContextResponse {
  /** The Contact record ID in Dataverse */
  contactId: string;
  /** The authenticated user's email */
  email: string;
  /** List of projects the user can access, with their current access level */
  projects: Array<{
    /** Secure Project record ID */
    projectId: string;
    /** Access level label (e.g., "ViewOnly", "Collaborate", "FullAccess") */
    accessLevel: string;
  }>;
}

// ---------------------------------------------------------------------------
// Token cache — in-memory only, never persisted to localStorage
// ---------------------------------------------------------------------------

interface TokenCacheEntry {
  /** Raw Bearer token string */
  token: string;
  /** Unix timestamp (ms) when the token expires */
  expiresAt: number;
}

/** In-memory token cache. Intentionally module-scoped (not exported). */
let tokenCache: TokenCacheEntry | null = null;

/** Minimum remaining lifetime (ms) before we proactively refresh */
const TOKEN_REFRESH_THRESHOLD_MS = 5 * 60 * 1000; // 5 minutes

/**
 * Returns true when the cached token is present and has more than
 * TOKEN_REFRESH_THRESHOLD_MS of lifetime remaining.
 */
function isCachedTokenValid(): boolean {
  if (tokenCache === null) return false;
  return Date.now() < tokenCache.expiresAt - TOKEN_REFRESH_THRESHOLD_MS;
}

// ---------------------------------------------------------------------------
// CSRF token helper (required by the Power Pages token endpoint)
// ---------------------------------------------------------------------------

/**
 * Fetch the anti-forgery token from the portal's layout endpoint.
 * Required as a header on POST requests to `/_services/auth/token`.
 */
async function getAntiForgeryToken(): Promise<string> {
  const response = await fetch("/_layout/tokenhtml");
  if (!response.ok) {
    throw new ApiError(response.status, "Failed to fetch anti-forgery token");
  }
  const text = await response.text();
  const doc = new DOMParser().parseFromString(text, "text/xml");
  return doc.querySelector("input")?.getAttribute("value") ?? "";
}

// ---------------------------------------------------------------------------
// Token acquisition
// ---------------------------------------------------------------------------

/**
 * Acquire (or return a cached) OAuth token via the Power Pages implicit grant
 * flow.
 *
 * Flow:
 *   1. POST to `/_services/auth/token` with client_id, response_type=token,
 *      nonce, state, and the CSRF anti-forgery token as a header.
 *   2. Portal validates the session and returns a short-lived Bearer token.
 *   3. Token is cached in memory with its expiry timestamp.
 *
 * Auto-refreshes when fewer than 5 minutes remain before expiry.
 *
 * @returns The raw Bearer token string to include in Authorization headers.
 * @throws ApiError when the portal token endpoint returns a non-OK response.
 */
export async function getPortalToken(): Promise<string> {
  if (isCachedTokenValid()) {
    // Safe: isCachedTokenValid() guarantees tokenCache is non-null here
    return tokenCache!.token;
  }

  const csrfToken = await getAntiForgeryToken();

  const params = new URLSearchParams({
    client_id: PORTAL_CLIENT_ID,
    response_type: "token",
    nonce: crypto.randomUUID().substring(0, 20),
    state: crypto.randomUUID().substring(0, 20),
  });

  const response = await fetch("/_services/auth/token", {
    method: "POST",
    headers: {
      "__RequestVerificationToken": csrfToken,
      "Content-Type": "application/x-www-form-urlencoded",
    },
    body: params,
  });

  if (!response.ok) {
    throw new ApiError(
      response.status,
      `Portal token endpoint returned ${response.status}: ${await response.text()}`
    );
  }

  const token = (await response.text()).trim();

  // Power Pages tokens have a configurable expiry (default 15 min, max 60 min).
  // We decode the JWT exp claim to set the cache expiry precisely.
  // Fall back to 15 minutes if decoding fails for any reason.
  const expiresAt = parseTokenExpiry(token) ?? Date.now() + 15 * 60 * 1000;

  tokenCache = { token, expiresAt };
  return token;
}

/**
 * Parse the `exp` claim from a JWT token string without a JWT library.
 * Returns the expiry as a Unix timestamp in milliseconds, or null on failure.
 */
function parseTokenExpiry(token: string): number | null {
  try {
    const parts = token.split(".");
    if (parts.length < 2) return null;
    const payload = JSON.parse(atob(parts[1])) as Record<string, unknown>;
    const exp = payload["exp"];
    if (typeof exp === "number") return exp * 1000;
  } catch {
    // Ignore — caller uses fallback
  }
  return null;
}

/**
 * Invalidate the token cache, forcing a fresh token on the next call.
 * Called automatically on 401 responses to trigger a single retry.
 */
function clearTokenCache(): void {
  tokenCache = null;
}

// ---------------------------------------------------------------------------
// Core fetch wrapper
// ---------------------------------------------------------------------------

/**
 * Make an authenticated call to the BFF API.
 *
 * - Prepends BFF_API_URL to path.
 * - Adds `Authorization: Bearer {token}` header.
 * - On 401: clears the token cache, acquires a fresh token, and retries once.
 * - Throws ApiError on any non-2xx response (including after the retry).
 *
 * @param path    API path relative to BFF_API_URL (e.g. "/api/v1/external/me")
 * @param options Standard RequestInit options (method, body, headers, etc.)
 * @returns Parsed JSON response body typed as T.
 */
export async function bffApiCall<T>(
  path: string,
  options: RequestInit = {}
): Promise<T> {
  const token = await getPortalToken();
  const response = await executeFetch(path, options, token);

  // On 401, clear cache, get a fresh token, and retry once
  if (response.status === 401) {
    clearTokenCache();
    const freshToken = await getPortalToken();
    const retryResponse = await executeFetch(path, options, freshToken);
    return parseResponse<T>(retryResponse);
  }

  return parseResponse<T>(response);
}

/**
 * Internal helper: perform the actual fetch with the Authorization header set.
 */
async function executeFetch(
  path: string,
  options: RequestInit,
  token: string
): Promise<Response> {
  const url = `${BFF_API_URL}${path}`;

  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    "Authorization": `Bearer ${token}`,
    ...(options.headers as Record<string, string> | undefined),
  };

  return fetch(url, {
    ...options,
    headers,
  });
}

/**
 * Internal helper: read and parse a Response, throwing ApiError on non-2xx.
 */
async function parseResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let message: string;
    try {
      message = await response.text();
    } catch {
      message = `HTTP ${response.status}`;
    }
    throw new ApiError(response.status, message);
  }

  // Handle 204 No Content
  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

// ---------------------------------------------------------------------------
// Typed convenience methods
// ---------------------------------------------------------------------------

/**
 * GET /api/v1/external/me
 *
 * Returns the authenticated external user's context — their Dataverse Contact
 * ID, email, and the list of projects they have access to with their access
 * level for each.
 */
export async function getExternalUserContext(): Promise<ExternalUserContextResponse> {
  return bffApiCall<ExternalUserContextResponse>("/api/v1/external/me");
}

/**
 * POST /api/v1/external-access/grant
 *
 * Grants a contact access to a secure project at the specified access level.
 * Creates an sprk_externalrecordaccess record and provisions SPE permissions.
 */
export async function grantAccess(
  request: GrantAccessRequest
): Promise<void> {
  return bffApiCall<void>("/api/v1/external-access/grant", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/**
 * POST /api/v1/external-access/revoke
 *
 * Revokes an external user's access to a secure project.
 * Sets the access record to revoked and removes SPE permissions.
 */
export async function revokeAccess(
  request: RevokeAccessRequest
): Promise<void> {
  return bffApiCall<void>("/api/v1/external-access/revoke", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/** Response returned after creating a portal invitation for an external user */
export interface InviteUserResponse {
  /** The ID of the created adx_invitation record */
  invitationId: string;
  /** The invitation code the Contact uses to redeem access */
  invitationCode: string;
  /** ISO date string for the invitation expiry, if set */
  expiryDate?: string;
}

/**
 * POST /api/v1/external-access/invite
 *
 * Invites a new external user to a secure project by email.
 * The BFF looks up or creates the Contact record, creates the access record,
 * and sends the portal invitation email via adx_invitation.
 */
export async function inviteUser(
  request: InviteUserRequest
): Promise<InviteUserResponse> {
  return bffApiCall<InviteUserResponse>("/api/v1/external-access/invite", {
    method: "POST",
    body: JSON.stringify(request),
  });
}
