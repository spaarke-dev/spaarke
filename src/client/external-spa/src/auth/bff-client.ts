/**
 * BFF API client module for the Secure Project Workspace SPA.
 *
 * Acquires OAuth tokens via MSAL (Entra B2B, authorization code + PKCE) and provides
 * typed wrappers for authenticated BFF API calls with Bearer token auth.
 *
 * Token caching is handled by MSAL internally (no manual cache management needed).
 * On 401: MSAL's silent token refresh will handle expiry automatically on the next call.
 *
 * See: docs/architecture/power-pages-spa-guide.md — Authentication section
 * See: notes/auth-migration-b2b-msal.md
 */

import { BFF_API_URL } from "../config";
import { acquireBffToken } from "./msal-auth";
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

// Token acquisition is delegated to msal-auth.ts (acquireBffToken)

// ---------------------------------------------------------------------------
// Core fetch wrapper
// ---------------------------------------------------------------------------

/**
 * Make an authenticated call to the BFF API.
 *
 * - Prepends BFF_API_URL to path.
 * - Adds `Authorization: Bearer {token}` header (MSAL-acquired OAuth token).
 * - On 401: acquires a fresh token (MSAL handles silent refresh / redirect) and retries once.
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
  const token = await acquireBffToken();
  const response = await executeFetch(path, options, token);

  // On 401, acquire a fresh token (MSAL will refresh silently or redirect) and retry once
  if (response.status === 401) {
    const freshToken = await acquireBffToken();
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

/** Response returned after inviting an external user via Azure AD B2B */
export interface InviteUserResponse {
  /** The Dataverse Contact record ID for the invited user */
  contactId: string;
  /** The Azure AD B2B invitation redemption URL to share with the user */
  inviteRedeemUrl: string;
  /** Invitation status (e.g. "PendingAcceptance", "Completed") */
  status: string;
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
