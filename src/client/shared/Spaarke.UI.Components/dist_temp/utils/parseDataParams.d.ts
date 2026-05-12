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
/**
 * Parse Code Page data parameters from the current URL.
 *
 * Handles both the Xrm `data` envelope format and raw URL query parameters.
 * When a `data` param is present, its decoded contents take priority. Any
 * additional top-level query params (excluding `data` itself) are merged in,
 * with `data` contents winning on key conflicts.
 *
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
 */
export declare function parseDataParams(): Record<string, string>;
//# sourceMappingURL=parseDataParams.d.ts.map