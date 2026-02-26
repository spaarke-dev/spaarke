/**
 * parseUrlParams — Extract URL parameters with Dataverse data envelope unwrap
 *
 * Dataverse navigateTo({ pageType: "webresource", data: "k=v&k2=v2" }) wraps
 * the caller's data string inside a single `?data=encodedString` query param.
 * This utility unwraps that envelope, with a fallback to direct URL params
 * for non-Dataverse testing (direct browser URL).
 *
 * @see ADR-026 — Dataverse web resource URL parameter conventions
 * @see DocumentRelationshipViewer/src/index.tsx — reference pattern
 */

import type { SearchDomain, AppUrlParams } from "../types";

const VALID_DOMAINS: SearchDomain[] = ["documents", "matters", "projects", "invoices"];

/**
 * Parse URL parameters from the current page URL.
 * Handles the Dataverse data envelope unwrap pattern.
 */
export function parseUrlParams(): AppUrlParams {
    const urlParams = new URLSearchParams(window.location.search);
    const dataEnvelope = urlParams.get("data");

    // Unwrap Dataverse data envelope, or fall back to direct URL params
    const params = dataEnvelope
        ? new URLSearchParams(decodeURIComponent(dataEnvelope))
        : urlParams;

    const rawDomain = params.get("domain")?.toLowerCase();
    const domain = VALID_DOMAINS.includes(rawDomain as SearchDomain)
        ? (rawDomain as SearchDomain)
        : undefined;

    return {
        theme: params.get("theme") ?? undefined,
        query: params.get("query") ?? undefined,
        domain,
        scope: params.get("scope") ?? undefined,
        entityId: params.get("entityId") ?? undefined,
        savedSearchId: params.get("savedSearchId") ?? undefined,
    };
}
