/**
 * Section/modal title resolution for the CommunicationConnections PCF (UAT R3 B11-2).
 *
 * Kept dependency-free (no Fluent / shared-lib imports) so the default + trim
 * behavior is unit-testable in isolation without dragging the whole React app —
 * and its ESM-only `@spaarke/ui-components` dist — into the jest runtime.
 */

/** Default section/modal title when the `titleText` manifest property is unset. */
export const DEFAULT_CONNECTIONS_TITLE = 'RELATED RECORDS';

/**
 * Resolve the section/modal title from the manifest `titleText` property: the
 * trimmed value when provided, otherwise the default ("RELATED RECORDS").
 */
export function resolveTitle(raw: string | null | undefined): string {
  const trimmed = (raw ?? '').trim();
  return trimmed.length > 0 ? trimmed : DEFAULT_CONNECTIONS_TITLE;
}
