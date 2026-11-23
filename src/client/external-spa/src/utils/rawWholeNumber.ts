/**
 * rawWholeNumber — a raw option-set (or other whole-number) column value, as a number.
 *
 * WHY: the SPA reads records and list rows through the BFF module fetch seam, which serialises Dataverse SDK values as
 * objects (System.Text.Json Web defaults). An option-set column (`statecode`, `statuscode`, `sprk_reviewdetail`, ...)
 * arrives as `{ "value": 1, "extensionData": null }`, not as `1`; the typed record-route DTOs carry plain numbers. Code
 * that gates on the RAW value (never the display label, which an owner can rename) reads it through this one function so
 * it works on both shapes.
 *
 * Pure: no React, no I/O, no imports.
 */

/**
 * The whole number in a raw column value, or `null` when it is not one. Accepts:
 *   - an integer number (`1`);
 *   - a string of 1-12 digits (`'1'`; surrounding whitespace is ignored);
 *   - the SDK object form `{ value: n }` or `{ Value: n }` with `n` an integer number (any `extensionData` is ignored).
 * Everything else is `null`: a display label, a non-integer, any other object, an array, `null` and `undefined`.
 */
export function rawWholeNumberOf(raw: unknown): number | null {
  if (typeof raw === 'number') return Number.isInteger(raw) ? raw : null;
  if (typeof raw === 'string') return /^\d{1,12}$/.test(raw.trim()) ? Number(raw.trim()) : null;
  if (raw !== null && typeof raw === 'object' && !Array.isArray(raw)) {
    const wrapped = raw as { value?: unknown; Value?: unknown };
    const inner = wrapped.value ?? wrapped.Value;
    return typeof inner === 'number' && Number.isInteger(inner) ? inner : null;
  }
  return null;
}
