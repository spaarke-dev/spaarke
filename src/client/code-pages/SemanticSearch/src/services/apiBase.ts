/**
 * Shared API base utilities for BFF API service clients.
 *
 * Provides:
 * - BFF_API_BASE_URL constant
 * - buildAuthHeaders() — constructs Authorization + Content-Type headers via MSAL
 * - handleApiResponse<T>() — parses success or throws typed ApiError (RFC 7807 ProblemDetails)
 *
 * @see ADR-013: All AI calls go through BFF API
 */

import { msalAuthProvider } from "./auth/MsalAuthProvider";
import type { ApiError } from "../types";

/** BFF API base URL (dev environment). */
export const BFF_API_BASE_URL = "https://spe-api-dev-67e2xz.azurewebsites.net";

/**
 * Build authenticated request headers for BFF API calls.
 * Acquires a Bearer token via MsalAuthProvider and returns
 * Authorization + Content-Type headers.
 *
 * @returns Header record ready for fetch() calls
 * @throws Error if MSAL is not initialized or token acquisition fails
 */
export async function buildAuthHeaders(): Promise<Record<string, string>> {
    const authHeader = await msalAuthProvider.getAuthHeader();
    return {
        "Authorization": authHeader,
        "Content-Type": "application/json",
    };
}

/**
 * Handle a fetch Response, parsing JSON on success or throwing a typed ApiError.
 *
 * On success (2xx): parses and returns JSON body as T.
 * On failure: attempts to parse RFC 7807 ProblemDetails body, then throws ApiError.
 *
 * @template T - Expected response body type
 * @param response - The fetch Response object
 * @returns Parsed response body
 * @throws ApiError with status, title, detail, type, and optional validation errors
 */
export async function handleApiResponse<T>(response: Response): Promise<T> {
    if (response.ok) {
        return response.json() as Promise<T>;
    }

    // Parse ProblemDetails error response (RFC 7807)
    let error: ApiError;
    try {
        const body = await response.json();
        error = {
            status: response.status,
            title: body.title || response.statusText,
            detail: body.detail,
            type: body.type,
            errors: body.errors,
        };
    } catch {
        error = {
            status: response.status,
            title: response.statusText,
        };
    }

    throw error;
}
