/**
 * BFF API client module for the Secure Project Workspace SPA.
 *
 * Acquires OAuth tokens via MSAL (Entra B2B, authorization code + PKCE) and provides
 * typed wrappers for authenticated BFF API calls with Bearer token auth.
 *
 * Token caching is handled by MSAL internally (no manual cache management needed).
 * On 401: MSAL's silent token refresh will handle expiry automatically on the next call.
 *
 * AUTH STRATEGY CHOICE (Auth v2, Phase B / task 027)
 * --------------------------------------------------
 * This SPA intentionally does NOT use `@spaarke/auth`'s `authenticatedFetch` or
 * `SpaarkeAuthProvider`. The Spaarke Auth v2 library is designed for the
 * INTERNAL Spaarke surfaces (PCFs + internal Code Pages) hosted inside a
 * customer's own Azure tenant — its MSAL config uses `localStorage` for
 * cross-tab survival, which is the right call there but the wrong call here.
 *
 * The Secure Project Workspace is a B2B portal: external guest users from many
 * different organizations sign in via Entra B2B into the main Spaarke workforce
 * tenant. Each browser tab can be a different user/organization, so the SPA
 * uses MSAL `sessionStorage` to prevent token leakage across tabs. See
 * `msal-config.ts` for the full rationale.
 *
 * Adopting `@spaarke/auth` here would require either (a) a B2B-aware
 * `OfficeNaaStrategy`-style strategy, or (b) running two `PublicClientApplication`
 * instances (one from `@spaarke/auth`, one from `@azure/msal-react`), which
 * fragments the MSAL cache. Neither is in scope for Phase B; the task POML
 * (027 step 3) explicitly defers a B2B strategy to a later workstream.
 *
 * What Phase B DOES enforce here: the raw `Authorization: Bearer ${token}`
 * literal is centralized into the single internal helper `executeFetch` so
 * the project's ESLint Bearer-literal ban (task 070) has exactly one
 * allowlisted exception in the external SPA. All public-facing API methods
 * (`bffApiCall`, `getExternalUserContext`, `grantAccessAsContact`, etc.) call
 * `bffApiCall`, which calls `executeFetch`. There is no other place in the
 * external SPA where a Bearer header is set.
 *
 * HOST-DETECTION WIRING (task 012 — Teams host adapter)
 * ------------------------------------------------------
 * `bffApiCall` acquires its token via `acquireActiveBffToken()`, a pluggable seam in msal-auth.ts
 * that defaults to the CIAM strategy (`acquireBffToken`) — the same request shape as before for the
 * standalone SPA (FR-15). The Teams host adapter re-points this seam at the Teams workforce
 * strategy (task 011) once at bootstrap, so this SAME module — no fork — serves BFF calls correctly
 * for both hosts. See `src/client/external-spa/src/host/TeamsHostAdapter.ts`.
 *
 * RUN-TIME BACKEND (task 041 — FR-17, NFR-02)
 * -------------------------------------------
 * There is no build-time BFF URL. Each request reads the ACTIVE backend once (`backend-selection.ts`: the T240c
 * directory, or dev config until it ships — never a link or query string) and uses that one record for BOTH the
 * request URL and the token scope (`acquireActiveBffToken(backend.bffScope)`), so a token for one customer's BFF is
 * never sent to another origin. No active backend → the call rejects before any token or fetch (fail closed).
 *
 * See: docs/architecture/power-pages-spa-guide.md — Authentication section
 * See: notes/auth-migration-b2b-msal.md
 * See: .claude/AUDIT-FINDINGS-AUTH-SYSTEM.md — external SPA out-of-scope rationale
 */

import { acquireActiveBffToken, type BffTokenAcquireOptions } from './msal-auth';
import { bffRequestUrl, getActiveBackend } from './backend-selection';
import { INVITE_CODE_HEADER, clearInviteCode, inviteCodeFor } from './invite-code';
import { ApiError } from '../types';
import { getMockResponse } from '../mocks/mock-service';
import { isDevMockEnabled } from '../dev-mock';

// ---------------------------------------------------------------------------
// Request / Response types for BFF endpoints
// ---------------------------------------------------------------------------

/**
 * Request body for contact-side Grant Access (unified-access-control-r2 task 140):
 * POST /api/v1/external/contact-grants.
 *
 * A contact holding Collaborate or Full Access grants ONE colleague of their own
 * organization, at or below their own level. The body is CLOSED server-side: an
 * unknown member (e.g. an organization) is refused 400, so there is deliberately
 * no organization field here — an organization-wide grant is not possible.
 *
 * (The former grantAccess / revokeAccess / inviteUser calls posted to
 * the internal Manage Access route group, which a CIAM token
 * cannot authenticate to — they were removed by task 140.)
 */
export interface ContactGrantRequest {
  /** 'project' | 'matter' | 'workassignment' */
  recordType: string;
  /** The record to grant access to */
  recordId: string;
  /** The colleague, by contact id — exactly one of this and granteeEmail */
  granteeContactId?: string;
  /**
   * The colleague, by email — matched among the active members of the caller's own organizations ONLY
   * (nobody outside them is considered or disclosed). No such colleague → 422; several → 409.
   */
  granteeEmail?: string;
  /**
   * Requested level (Dataverse sprk_accesslevel option value).
   * 100000000 = ViewOnly, 100000001 = Collaborate, 100000002 = FullAccess.
   * Written at most at the caller's own level.
   */
  accessLevel: number;
  /** Optional ISO date (yyyy-MM-dd). Absent → today + 90 days, never later than the caller's own grant. */
  expiryDate?: string;
}

/** The outcome of a contact grant. */
export interface ContactGrantResponse {
  accessRecordId: string;
  granteeContactId: string;
  /** The level actually written */
  grantedAccessLevel: number | null;
  /** The caller's own level lowered the request */
  narrowed: boolean;
  /** The expiry the grant carries (yyyy-MM-dd) */
  expiryDate: string | null;
  /** The requested (or default) expiry was cut back to the caller's own grant */
  expiryNarrowed: boolean;
}

/** One grant the caller issued on a record. */
export interface ContactIssuedGrant {
  accessRecordId: string;
  contactId: string;
  fullName: string | null;
  email: string | null;
  accessLevel: number | null;
  expiryDate: string | null;
}

/** GET /api/v1/external/contact-grants response. */
export interface ContactIssuedGrantsResponse {
  grants: ContactIssuedGrant[];
}

/** POST /api/v1/external/contact-grants/revoke response. */
export interface ContactGrantRevokeResponse {
  accessRecordId: string;
  deactivatedCount: number;
  /** The colleague still holds access somebody else granted (never touched by the caller's revoke) */
  accessRemainsFromOthers: boolean;
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
  /**
   * The caller's OWN Dataverse systemuser id - a licensed workforce (systemuser) principal only. Optional: `null` (or
   * absent, from a BFF that predates it) for a CIAM partner and for a workforce contact with no systemuser; the BFF
   * never sends the empty GUID. The detail view compares it with a row's `sprk_createdbyperson` stamp to offer Delete
   * (task 054); the server's delete check stays the authority.
   */
  systemUserId?: string | null;
  /**
   * The firms the caller can submit invoices for (ebilling-r1 task 051): only the caller's own, id and name. `[]` means
   * the caller belongs to no firm; `null` or absent (a BFF that predates it, eBilling off, or a read that failed) means
   * unknown, never "no firm". Read only by the invoice submission firm step.
   */
  firms?: Array<{ firmId: string; name: string }> | null;
  /**
   * `true` when the caller's write access is decided per record, as Dataverse decides it (a licensed systemuser under
   * unified-access-control-r2 task 036, fix round 1). The `projects` levels then say only what the caller may SEE; create
   * and upload controls follow the record's own list (`canCreate`) instead. Absent for every other caller.
   */
  writeAccessPerRecord?: boolean;
  /** List of projects the user can access, with their current access level */
  projects: Array<{
    /** Secure Project record ID */
    projectId: string;
    /** Access level label (e.g., "ViewOnly", "Collaborate", "FullAccess") */
    accessLevel: string;
  }>;
}

// Token acquisition is delegated to msal-auth.ts (acquireActiveBffToken — host-selected strategy)

// ---------------------------------------------------------------------------
// Core fetch wrapper
// ---------------------------------------------------------------------------

/**
 * Make an authenticated call to the BFF API.
 *
 * - Prepends the ACTIVE backend's base URL to path (task 041; never a URL from a link).
 * - Adds `Authorization: Bearer {token}` header (MSAL-acquired OAuth token).
 * - On 401: forces a network token refresh (MSAL handles silent refresh / redirect) and retries once.
 * - Throws ApiError on any non-2xx response (including after the retry).
 *
 * @param path    Root-relative API path on the active backend (e.g. "/api/v1/external/me")
 * @param options Standard RequestInit options (method, body, headers, etc.)
 * @param auth    Token-acquisition options. `{ noInteraction: true }` is for a TIMER-driven background read only (the
 *                notification bell's poll): if the token cannot be had silently the call rejects with
 *                `SignInRequiredError` instead of starting a sign-in redirect. Absent, every call acquires exactly as before.
 * @returns Parsed JSON response body typed as T.
 */
export async function bffApiCall<T>(
  path: string,
  options: RequestInit = {},
  auth?: Pick<BffTokenAcquireOptions, 'noInteraction'>
): Promise<T> {
  if (isDevMockEnabled()) {
    return getMockResponse<T>(path, options);
  }

  return parseResponse<T>(await authorizedFetch(path, options, auth));
}

/**
 * Make an authenticated call to the BFF API that returns BINARY content.
 *
 * Identical auth/retry semantics to {@link bffApiCall} — it deliberately shares
 * `acquireActiveBffToken` + `executeFetch` rather than forking a second fetch
 * path — but resolves the body as a `Blob` instead of parsing it as JSON.
 *
 * Why this exists: the external document-download route
 * (`GET /api/v1/external/projects/{id}/documents/{documentId}/content`) streams
 * the file itself as `application/octet-stream`. It deliberately NEVER returns a
 * signed URL or any SPE pointer — see `ExternalProjectDataEndpoints.cs`
 * ("Pointers are never surfaced to the client"). So a JSON-parsing caller can
 * never consume it; the bytes must be read directly.
 *
 * @param path    Root-relative API path on the active backend
 * @param options Standard RequestInit options
 * @returns The response body as a Blob.
 */
export async function bffApiBlob(path: string, options: RequestInit = {}): Promise<Blob> {
  const response = await authorizedFetch(path, options);

  if (!response.ok) {
    let message: string;
    try {
      message = await response.text();
    } catch {
      message = `HTTP ${response.status}`;
    }
    throw new ApiError(response.status, message);
  }

  return response.blob();
}

/**
 * Internal helper shared by {@link bffApiCall} and {@link bffApiBlob}: read the active backend ONCE, build the URL on
 * its origin, acquire a token for ITS scope, fetch; on 401 acquire a fresh token for the same scope and retry once.
 * The URL and the scope come from the same snapshot, so a backend switch mid-request cannot pair one backend's token
 * with another backend's host.
 */
async function authorizedFetch(
  path: string,
  options: RequestInit,
  auth?: Pick<BffTokenAcquireOptions, 'noInteraction'>
): Promise<Response> {
  const backend = getActiveBackend();
  const url = bffRequestUrl(backend, path);
  // Passed only when set, so the default call shapes (`acquire(backend)`, `acquire(backend, { forceRefresh: true })`) stay.
  const noInteraction = auth?.noInteraction ? ({ noInteraction: true } as const) : undefined;

  // Task 185: the invite code captured from the invite link (invite-code.ts), sent until the first CIAM-plane 2xx.
  const inviteCode = inviteCodeFor(backend);

  const token = await acquireActiveBffToken(backend, noInteraction);
  let response = await executeFetch(url, options, token, inviteCode);

  // On 401, force a NETWORK refresh (MSAL redeems the refresh token, or redirects) and retry once. Without
  // `forceRefresh` MSAL would return the cached access token the BFF just refused: the same 401, a sign-in loop.
  if (response.status === 401) {
    const freshToken = await acquireActiveBffToken(backend, { forceRefresh: true, ...noInteraction });
    response = await executeFetch(url, options, freshToken, inviteCode);
  }

  if (inviteCode && response.ok) clearInviteCode();
  return response;
}

/**
 * Internal helper: perform the actual fetch with the Authorization header set.
 *
 * This is the SOLE Bearer-literal site in the external SPA — the project's
 * Phase E ESLint rule banning `Authorization: \`Bearer ${...}\`` literals
 * outside `authenticatedFetch.ts` will allowlist this path because the
 * external SPA is out of scope for `@spaarke/auth` (see file header).
 */
async function executeFetch(
  url: string,
  options: RequestInit,
  token: string,
  inviteCode: string | null = null
): Promise<Response> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    // Auth v2 (D-AUTH-7): External SPA uses sessionStorage MSAL (B2B threat
    // model) — opts out of @spaarke/auth's authenticatedFetch. This is the
    // single allowlisted Bearer-literal site for this surface.
    Authorization: `Bearer ${token}`,
    ...(inviteCode ? { [INVITE_CODE_HEADER]: inviteCode } : {}),
    ...(options.headers as Record<string, string> | undefined),
  };

  // FormData uploads must NOT have Content-Type set — the browser sets it
  // automatically with the correct multipart/form-data boundary value.
  if (options.body instanceof FormData) {
    delete headers['Content-Type'];
  }

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
  return bffApiCall<ExternalUserContextResponse>('/api/v1/external/me');
}

/**
 * POST /api/v1/external/contact-grants (task 140)
 *
 * The caller (a Collaborate or Full Access contact) grants a colleague of their
 * own organization access to a record. A refusal is a ProblemDetails whose
 * `detail` is a user-facing message — see {@link problemMessage}.
 */
export async function grantAccessAsContact(request: ContactGrantRequest): Promise<ContactGrantResponse> {
  return bffApiCall<ContactGrantResponse>('/api/v1/external/contact-grants', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

/** GET /api/v1/external/contact-grants (task 140) — the grants the caller issued on a record. */
export async function listContactGrants(recordType: string, recordId: string): Promise<ContactIssuedGrantsResponse> {
  const query = `recordType=${encodeURIComponent(recordType)}&recordId=${encodeURIComponent(recordId)}`;
  return bffApiCall<ContactIssuedGrantsResponse>(`/api/v1/external/contact-grants?${query}`);
}

/** POST /api/v1/external/contact-grants/revoke (task 140) — revoke one grant the caller issued. */
export async function revokeContactGrant(accessRecordId: string): Promise<ContactGrantRevokeResponse> {
  return bffApiCall<ContactGrantRevokeResponse>('/api/v1/external/contact-grants/revoke', {
    method: 'POST',
    body: JSON.stringify({ accessRecordId }),
  });
}

/**
 * The user-facing message of a failed BFF call: the ProblemDetails `detail` the
 * server wrote for exactly this refusal (rendered verbatim — it names the rule,
 * e.g. "… is not yet a member of your organization …"), else the fallback.
 */
export function problemMessage(err: unknown, fallback: string): string {
  if (err instanceof ApiError) {
    try {
      const body = JSON.parse(err.message) as { detail?: unknown };
      if (typeof body.detail === 'string' && body.detail.trim()) {
        return body.detail;
      }
    } catch {
      /* not a ProblemDetails body */
    }
  }
  return fallback;
}
