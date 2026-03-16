/**
 * Portal Authentication Module — Secure Project Workspace SPA
 *
 * Provides:
 *   - getPortalUser()        — retrieve current portal user from Power Pages shell
 *   - getAntiForgeryToken()  — retrieve CSRF token with 1-hour TTL cache
 *   - portalApiCall<T>()     — typed fetch wrapper with CSRF + session-expiry handling
 *   - ApiError               — re-exported from types for convenience
 *
 * Power Pages context:
 *   - window.Shell.isAuthenticatedUser / window.Shell.userId / window.Shell.userName
 *     are provided by the Power Pages portal shell when authenticated.
 *   - If window.Shell is unavailable (e.g. during development), falls back to
 *     calling /_layout/GetCurrentUser.
 *   - Anti-forgery token is sourced from a hidden input on the page, or from
 *     /_layout/GetAntiForgeryToken if the hidden input is absent.
 */

import { PortalUser, ApiError } from "../types";

// ---------------------------------------------------------------------------
// Power Pages global shell type augmentation
// ---------------------------------------------------------------------------

/**
 * Subset of the Power Pages window.Shell global that we depend on.
 * Declared here so TypeScript can reference it without a separate .d.ts file.
 */
interface PowerPagesShell {
  isAuthenticatedUser: boolean;
  /** GUID of the authenticated contact record */
  userId: string;
  /** Username / email of the authenticated user */
  userName: string;
  /** Display name of the authenticated user */
  userDisplayName?: string;
}

declare global {
  interface Window {
    Shell?: PowerPagesShell;
  }
}

// ---------------------------------------------------------------------------
// Anti-forgery token cache
// ---------------------------------------------------------------------------

interface TokenCacheEntry {
  token: string;
  /** Unix epoch ms when this entry expires */
  expiresAt: number;
}

const TOKEN_TTL_MS = 60 * 60 * 1000; // 1 hour

let _tokenCache: TokenCacheEntry | null = null;

// ---------------------------------------------------------------------------
// getPortalUser
// ---------------------------------------------------------------------------

/**
 * Retrieve the current authenticated portal user from Power Pages.
 *
 * Resolution order:
 *   1. window.Shell (Power Pages global — most reliable in production)
 *   2. /_layout/GetCurrentUser endpoint (fallback, e.g. local dev proxy)
 *
 * Returns null if the user is not authenticated.
 */
export async function getPortalUser(): Promise<PortalUser | null> {
  // 1. Try Power Pages shell global
  const shell = window.Shell;
  if (shell !== undefined) {
    if (!shell.isAuthenticatedUser) {
      return null;
    }

    // Shell is present and user is authenticated — build PortalUser from shell values.
    const displayName = shell.userDisplayName ?? shell.userName ?? "";
    const nameParts = displayName.split(" ");
    const firstName = nameParts[0] ?? "";
    const lastName = nameParts.slice(1).join(" ");

    return {
      userName: shell.userName ?? "",
      firstName,
      lastName,
      displayName,
    };
  }

  // 2. Fallback: call /_layout/GetCurrentUser
  try {
    const response = await fetch("/_layout/GetCurrentUser", {
      method: "GET",
      credentials: "include",
      headers: {
        Accept: "application/json",
      },
    });

    if (response.status === 401 || response.status === 403) {
      return null;
    }

    if (!response.ok) {
      throw new ApiError(
        response.status,
        `GetCurrentUser failed: HTTP ${response.status}`
      );
    }

    // The endpoint returns a JSON object.  The exact shape varies by portal
    // version; we pick out the fields we need defensively.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const data: Record<string, any> = await response.json();

    if (!data || !data.IsAuthenticated) {
      return null;
    }

    const displayName: string = data.DisplayName ?? data.UserName ?? "";
    const nameParts = displayName.split(" ");
    const firstName = nameParts[0] ?? "";
    const lastName = nameParts.slice(1).join(" ");

    return {
      userName: data.UserName ?? data.Email ?? "",
      firstName,
      lastName,
      displayName,
    };
  } catch (err) {
    if (err instanceof ApiError) {
      throw err;
    }
    // Network failure or unexpected error — treat as not authenticated rather
    // than crashing the SPA.
    console.error("[portal-auth] getPortalUser fallback failed:", err);
    return null;
  }
}

// ---------------------------------------------------------------------------
// getAntiForgeryToken
// ---------------------------------------------------------------------------

/**
 * Retrieve the anti-forgery (CSRF) token for the current portal session.
 *
 * Resolution order:
 *   1. In-memory cache (valid for 1 hour)
 *   2. Hidden input `<input name="__RequestVerificationToken">` on the page
 *   3. /_layout/GetAntiForgeryToken endpoint
 *
 * The returned token must be sent with all POST / PUT / DELETE requests as
 * either the `RequestVerificationToken` request header or the
 * `__RequestVerificationToken` form field.
 */
export async function getAntiForgeryToken(): Promise<string> {
  const now = Date.now();

  // 1. Return cached token if still valid
  if (_tokenCache !== null && now < _tokenCache.expiresAt) {
    return _tokenCache.token;
  }

  // 2. Try hidden input on the current page (Power Pages renders this
  //    automatically on pages that use server-side forms)
  const hiddenInput = document.querySelector<HTMLInputElement>(
    'input[name="__RequestVerificationToken"]'
  );
  if (hiddenInput?.value) {
    _tokenCache = { token: hiddenInput.value, expiresAt: now + TOKEN_TTL_MS };
    return _tokenCache.token;
  }

  // 3. Fetch from the dedicated endpoint
  const response = await fetch("/_layout/GetAntiForgeryToken", {
    method: "GET",
    credentials: "include",
    headers: { Accept: "application/json" },
  });

  if (!response.ok) {
    throw new ApiError(
      response.status,
      `GetAntiForgeryToken failed: HTTP ${response.status}`
    );
  }

  // The endpoint returns a JSON string (quoted token) or an object like
  // { "token": "..." }.  Handle both.
  const raw = await response.text();
  let token: string;
  try {
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed === "string") {
      token = parsed;
    } else if (
      parsed !== null &&
      typeof parsed === "object" &&
      "token" in parsed &&
      typeof (parsed as Record<string, unknown>).token === "string"
    ) {
      token = (parsed as Record<string, string>).token;
    } else {
      // Treat the raw body as the token if JSON parsing yields an unexpected shape
      token = raw;
    }
  } catch {
    token = raw;
  }

  _tokenCache = { token, expiresAt: now + TOKEN_TTL_MS };
  return token;
}

/**
 * Invalidate the cached anti-forgery token.
 * Call this after receiving a 400 Invalid CSRF token error.
 */
export function invalidateAntiForgeryToken(): void {
  _tokenCache = null;
}

// ---------------------------------------------------------------------------
// portalApiCall
// ---------------------------------------------------------------------------

/** HTTP methods that mutate state and therefore require a CSRF token */
const WRITE_METHODS = new Set(["POST", "PUT", "PATCH", "DELETE"]);

/**
 * Typed fetch wrapper for Power Pages Web API and BFF API calls.
 *
 * Behaviour:
 *   - Adds `OData-Version: 4.0` + `OData-MaxVersion: 4.0` for /_api/ paths
 *   - For POST / PUT / PATCH / DELETE: fetches anti-forgery token and adds
 *     it as the `RequestVerificationToken` request header
 *   - On 401 / 403: redirects to the portal sign-in page
 *   - On non-2xx: throws ApiError with status code and correlation ID (if available)
 *
 * @param url     URL to fetch (absolute or relative)
 * @param options Standard RequestInit options (method, body, headers, …)
 * @returns       Parsed JSON response body typed as T
 */
export async function portalApiCall<T>(
  url: string,
  options: RequestInit = {}
): Promise<T> {
  const method = (options.method ?? "GET").toUpperCase();
  const isWriteOperation = WRITE_METHODS.has(method);
  const isODataPath = url.includes("/_api/");

  // Build headers
  const headers = new Headers(options.headers);

  // OData headers for Dataverse Web API calls
  if (isODataPath) {
    if (!headers.has("OData-Version")) {
      headers.set("OData-Version", "4.0");
    }
    if (!headers.has("OData-MaxVersion")) {
      headers.set("OData-MaxVersion", "4.0");
    }
    if (!headers.has("Accept")) {
      headers.set("Accept", "application/json;odata.metadata=minimal");
    }
  } else if (!headers.has("Accept")) {
    headers.set("Accept", "application/json");
  }

  // CSRF token for write operations
  if (isWriteOperation) {
    const csrfToken = await getAntiForgeryToken();
    headers.set("RequestVerificationToken", csrfToken);
  }

  const response = await fetch(url, {
    ...options,
    method,
    credentials: "include",
    headers,
  });

  // Session expiry — redirect to sign-in
  if (response.status === 401 || response.status === 403) {
    const returnUrl = encodeURIComponent(window.location.href);
    window.location.href = `/SignIn?returnUrl=${returnUrl}`;
    // Throw to stop any further processing in the calling code
    throw new ApiError(
      response.status,
      "Session expired or access denied. Redirecting to sign-in."
    );
  }

  // Non-2xx responses
  if (!response.ok) {
    const correlationId =
      response.headers.get("x-correlation-id") ??
      response.headers.get("request-id") ??
      undefined;

    let message = `HTTP ${response.status} ${response.statusText}`;
    try {
      const body: unknown = await response.json();
      if (
        body !== null &&
        typeof body === "object" &&
        "message" in body &&
        typeof (body as Record<string, unknown>).message === "string"
      ) {
        message = (body as Record<string, string>).message;
      } else if (
        body !== null &&
        typeof body === "object" &&
        "title" in body &&
        typeof (body as Record<string, unknown>).title === "string"
      ) {
        // ProblemDetails shape
        message = (body as Record<string, string>).title;
      }
    } catch {
      // Response body was not JSON — keep default message
    }

    const error = new ApiError(response.status, message);
    if (correlationId !== undefined) {
      // Attach correlationId as a non-enumerable property so callers can
      // inspect it without it appearing in serialised error output
      Object.defineProperty(error, "correlationId", {
        value: correlationId,
        writable: false,
        enumerable: false,
        configurable: true,
      });
    }
    throw error;
  }

  // 204 No Content — return empty object cast to T
  if (response.status === 204) {
    return {} as T;
  }

  return response.json() as Promise<T>;
}

// ---------------------------------------------------------------------------
// Re-exports for module consumers
// ---------------------------------------------------------------------------

export { ApiError } from "../types";
export type { PortalUser } from "../types";
