/**
 * parseUrlParams — Extract URL parameters with Dataverse data envelope unwrap.
 *
 * Dataverse `Xrm.Navigation.navigateTo({ pageType: "webresource", data: "k=v&k2=v2" })`
 * wraps the caller's data string inside a single `?data=encodedString` query
 * param. This utility unwraps that envelope, with a fallback to direct URL
 * params for non-Dataverse testing (direct browser URL).
 *
 * Envelope contract: FR-CP-01 + FR-PARITY-01 in
 * `projects/spaarke-multi-container-multi-index-r1/spec.md`.
 *
 * Parsing contract for EVERY key:
 * - Present + valid → typed value
 * - Absent → `undefined`
 * - Malformed → `undefined` (NEVER throw — page-stability invariant)
 *
 * Per-type helpers (`parseNumber`, `parseIsoDate`, `parseCsv`, `parseBoolean`,
 * `parseSearchMode`) are exported so tests + downstream code can reuse them.
 *
 * @see ADR-026 — Dataverse web resource URL parameter conventions
 * @see ADR-022 — Code Page uses React 18
 * @see DocumentRelationshipViewer/src/index.tsx — reference pattern
 */

import type { SearchDomain, AppUrlParams, EnvelopeSearchMode } from '../types';

const VALID_DOMAINS: SearchDomain[] = ['documents', 'matters', 'projects', 'invoices'];
const VALID_SEARCH_MODES: EnvelopeSearchMode[] = ['hybrid', 'vectorOnly', 'keywordOnly'];

// ---------------------------------------------------------------------------
// Type-safe parsing helpers
// ---------------------------------------------------------------------------

/**
 * Pass-through string parser — trims whitespace and rejects empty strings.
 * Returns `undefined` for `null`, `undefined`, or empty/whitespace-only input.
 */
export function parseString(raw: string | null | undefined): string | undefined {
  if (raw == null) return undefined;
  const trimmed = raw.trim();
  return trimmed.length > 0 ? trimmed : undefined;
}

/**
 * Parse a number from string. Returns `undefined` for null/empty/non-numeric
 * input. Optional `min`/`max` bounds clamp acceptance (out-of-range → `undefined`).
 *
 * @example parseNumber("50", 0, 100) === 50
 * @example parseNumber("abc") === undefined
 * @example parseNumber("150", 0, 100) === undefined
 */
export function parseNumber(raw: string | null | undefined, min?: number, max?: number): number | undefined {
  if (raw == null) return undefined;
  const trimmed = raw.trim();
  if (trimmed.length === 0) return undefined;
  const n = Number(trimmed);
  if (!Number.isFinite(n)) return undefined;
  if (min !== undefined && n < min) return undefined;
  if (max !== undefined && n > max) return undefined;
  return n;
}

/**
 * Parse an ISO 8601 date string. Validates via `Date.parse` + `Number.isFinite`.
 * Returns the ORIGINAL string (not a Date object) so the value can be passed
 * straight to API filters. Returns `undefined` for malformed input.
 *
 * @example parseIsoDate("2026-06-01T00:00:00Z") === "2026-06-01T00:00:00Z"
 * @example parseIsoDate("not-a-date") === undefined
 */
export function parseIsoDate(raw: string | null | undefined): string | undefined {
  if (raw == null) return undefined;
  const trimmed = raw.trim();
  if (trimmed.length === 0) return undefined;
  const ms = Date.parse(trimmed);
  return Number.isFinite(ms) ? trimmed : undefined;
}

/**
 * Parse a CSV string into a trimmed, non-empty string array. Returns
 * `undefined` for null/empty input so callers can distinguish "not provided"
 * from "provided as empty list".
 *
 * @example parseCsv("pdf, docx ,") === ["pdf", "docx"]
 * @example parseCsv("") === undefined
 * @example parseCsv(",,") === undefined
 */
export function parseCsv(raw: string | null | undefined): string[] | undefined {
  if (raw == null) return undefined;
  const items = raw
    .split(',')
    .map(s => s.trim())
    .filter(s => s.length > 0);
  return items.length > 0 ? items : undefined;
}

/**
 * Parse a boolean from string. Accepts `true`/`false`, `1`/`0`, `yes`/`no`
 * (case-insensitive). Returns `undefined` for unknown values so callers can
 * distinguish "not provided / malformed" from explicit `false`.
 *
 * @example parseBoolean("true") === true
 * @example parseBoolean("0") === false
 * @example parseBoolean("maybe") === undefined
 */
export function parseBoolean(raw: string | null | undefined): boolean | undefined {
  if (raw == null) return undefined;
  const v = raw.trim().toLowerCase();
  if (v === 'true' || v === '1' || v === 'yes') return true;
  if (v === 'false' || v === '0' || v === 'no') return false;
  return undefined;
}

/**
 * Parse the envelope `searchMode` literal. Validates against the allow-list
 * (`hybrid`/`vectorOnly`/`keywordOnly`). Returns `undefined` for malformed
 * input. Case-sensitive — the PCF emits exact-case literals.
 */
export function parseSearchMode(raw: string | null | undefined): EnvelopeSearchMode | undefined {
  if (raw == null) return undefined;
  const trimmed = raw.trim();
  return (VALID_SEARCH_MODES as string[]).includes(trimmed) ? (trimmed as EnvelopeSearchMode) : undefined;
}

/**
 * Parse the domain tab literal. Validates against the allow-list
 * (`documents`/`matters`/`projects`/`invoices`). Case-insensitive on input.
 */
export function parseDomain(raw: string | null | undefined): SearchDomain | undefined {
  if (raw == null) return undefined;
  const lowered = raw.trim().toLowerCase();
  return (VALID_DOMAINS as string[]).includes(lowered) ? (lowered as SearchDomain) : undefined;
}

// ---------------------------------------------------------------------------
// Main parser
// ---------------------------------------------------------------------------

/**
 * Parse URL parameters from the current page URL.
 * Handles the Dataverse data envelope unwrap pattern.
 *
 * Test seam: pass a custom search string (e.g. from a unit test) to skip the
 * `window.location.search` lookup. Production callers omit the argument.
 */
export function parseUrlParams(search?: string): AppUrlParams {
  const sourceSearch = search ?? (typeof window !== 'undefined' ? window.location.search : '');
  const urlParams = new URLSearchParams(sourceSearch);
  const dataEnvelope = urlParams.get('data');

  // Unwrap Dataverse data envelope, or fall back to direct URL params
  const params = dataEnvelope ? new URLSearchParams(decodeURIComponent(dataEnvelope)) : urlParams;

  return {
    theme: parseString(params.get('theme')),
    query: parseString(params.get('query')),
    domain: parseDomain(params.get('domain')),
    scope: parseString(params.get('scope')),
    entityId: parseString(params.get('entityId')),
    savedSearchId: parseString(params.get('savedSearchId')),
    searchIndexName: parseString(params.get('searchIndexName')),
    threshold: parseNumber(params.get('threshold'), 0, 100),
    searchMode: parseSearchMode(params.get('searchMode')),
    fileTypes: parseCsv(params.get('fileTypes')),
    dateFrom: parseIsoDate(params.get('dateFrom')),
    dateTo: parseIsoDate(params.get('dateTo')),
    tags: parseCsv(params.get('tags')),
    associatedOnly: parseBoolean(params.get('associatedOnly')),
  };
}
