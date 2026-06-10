/**
 * Parse Data Parameters for Code Page Wrappers
 *
 * When a Code Page is opened via `Xrm.Navigation.navigateTo`, parameters are
 * passed through the URL in one of two formats:
 *
 * 1. **Xrm data envelope**: `?data=key1%3Dval1%26key2%3Dval2`
 *    (Dataverse URL-encodes the `data` param value)
 * 2. **Raw URL params**: `?key1=val1&key2=val2`
 *    (direct query string, used in local dev or direct navigation)
 *
 * This utility handles both formats transparently.
 *
 * @see ADR-006 - Code Pages for standalone dialogs
 */

// ============================================================================
// Main Function
// ============================================================================

/**
 * Parse Code Page data parameters from the current URL (or a supplied search string).
 *
 * Handles both the Xrm `data` envelope format and raw URL query parameters.
 * When a `data` param is present, its decoded contents take priority. Any
 * additional top-level query params (excluding `data` itself) are merged in,
 * with `data` contents winning on key conflicts.
 *
 * @param search - Optional search string to parse (e.g., `'?key=val'`). When
 *   omitted, reads `window.location.search`. Supplying this argument is
 *   primarily for unit-test injection — production callers typically omit it.
 * @returns A flat key-value map of all parsed parameters
 *
 * @example
 * ```typescript
 * // Xrm data envelope: ?data=documentId%3Dabc-123%26matterId%3Ddef-456
 * const params = parseDataParams();
 * // { documentId: "abc-123", matterId: "def-456" }
 * ```
 *
 * @example
 * ```typescript
 * // Raw URL params: ?documentId=abc-123&matterId=def-456
 * const params = parseDataParams();
 * // { documentId: "abc-123", matterId: "def-456" }
 * ```
 *
 * @example
 * ```typescript
 * // Mixed: ?data=mode%3Dedit&theme=dark
 * const params = parseDataParams();
 * // { mode: "edit", theme: "dark" }
 * ```
 *
 * @example
 * ```typescript
 * // Typical Code Page entry point usage:
 * import { parseDataParams } from '@spaarke/ui-components';
 *
 * const params = parseDataParams();
 * const documentId = params.documentId ?? '';
 * const matterId = params.matterId ?? '';
 * ```
 *
 * @example
 * ```typescript
 * // Unit-test injection (no DOM required):
 * const params = parseDataParams('?data=key%3Dval');
 * // { key: "val" }
 * ```
 */
export function parseDataParams(search?: string): Record<string, string> {
  try {
    const sourceSearch =
      typeof search === 'string'
        ? search
        : typeof window !== 'undefined'
          ? window.location.search
          : '';
    const urlParams = new URLSearchParams(sourceSearch);
    const result: Record<string, string> = {};

    // Collect all top-level params (except 'data' itself)
    urlParams.forEach((value, key) => {
      if (key !== 'data') {
        result[key] = value;
      }
    });

    // Parse the Xrm `data` envelope if present
    const dataValue = urlParams.get('data');
    if (dataValue) {
      const decoded = decodeURIComponent(dataValue);
      const dataPairs = parseKeyValueString(decoded);
      // Data envelope values override top-level params on conflict
      Object.assign(result, dataPairs);
    }

    return result;
  } catch {
    // Graceful degradation if URL parsing fails
    return {};
  }
}

// ============================================================================
// Internal Helpers
// ============================================================================

/**
 * Parse a `key1=val1&key2=val2` string into a key-value map.
 *
 * Handles edge cases:
 * - Values containing `=` (only splits on the first `=`)
 * - Empty values (`key=` produces `{ key: "" }`)
 * - Whitespace trimming on keys and values
 * - URL-encoded values within the string
 *
 * @param input - The raw key=value&key=value string
 * @returns Parsed key-value map
 */
function parseKeyValueString(input: string): Record<string, string> {
  const result: Record<string, string> = {};
  if (!input) return result;

  // Try URLSearchParams first (handles URL-encoded values correctly)
  try {
    const parsed = new URLSearchParams(input);
    parsed.forEach((value, key) => {
      if (key) {
        result[key.trim()] = value.trim();
      }
    });
    return result;
  } catch {
    // Fall back to manual parsing
  }

  for (const pair of input.split('&')) {
    const eqIndex = pair.indexOf('=');
    if (eqIndex === -1) {
      // Key with no value
      const key = pair.trim();
      if (key) result[key] = '';
    } else {
      const key = pair.substring(0, eqIndex).trim();
      const value = pair.substring(eqIndex + 1).trim();
      if (key) result[key] = value;
    }
  }

  return result;
}
